using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FreeIsland;

internal static class DesktopBackdropProbe
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct VersionInfo { public int Size, Major, Minor, Build, Platform; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string ServicePack; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPels, YPels; public uint Used, Important; }
    [DllImport("ntdll.dll", CharSet=CharSet.Unicode)] private static extern int RtlGetVersion(ref VersionInfo value);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint value);
    [DllImport("user32.dll", SetLastError=true)] private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint value);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError=true)] private static extern bool BitBlt(IntPtr dest, int x,int y,int w,int h, IntPtr source,int sx,int sy,uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window,int index);
    private static Window background, overlay;
    private static DispatcherTimer timer;
    private static string output;
    private static byte[] baseline;
    private static int step;
    private static int left, top, size=160;
    private static bool affinitySet;

    private sealed class Checker : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.White,null,new Rect(0,0,ActualWidth,ActualHeight));
            for(int y=0;y<20;++y)for(int x=0;x<26;++x)
                if((x+y)%2==0)dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(20,70,190)),null,new Rect(x*24,y*24,24,24));
            for(int x=0;x<26;x+=4)dc.DrawRectangle(Brushes.Orange,null,new Rect(x*24+8,0,4,ActualHeight));
        }
    }
    private static byte[] Capture(uint flags)
    {
        DwmFlush();
        IntPtr source=GetDC(IntPtr.Zero), dest=CreateCompatibleDC(source), bits=IntPtr.Zero;
        BitmapInfo info=new BitmapInfo { Size=40, Width=size,Height=-size,Planes=1,BitCount=32 };
        IntPtr bitmap=CreateDIBSection(source,ref info,0,out bits,IntPtr.Zero,0), previous=SelectObject(dest,bitmap);
        try {
            if(source==IntPtr.Zero||dest==IntPtr.Zero||bitmap==IntPtr.Zero||bits==IntPtr.Zero)throw new Exception("GDI allocation failed.");
            if(!BitBlt(dest,0,0,size,size,source,left,top,flags))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            byte[] pixels=new byte[size*size*4];Marshal.Copy(bits,pixels,0,pixels.Length);
            for(int p=3;p<pixels.Length;p+=4)pixels[p]=255;
            return pixels;
        } finally { SelectObject(dest,previous);DeleteObject(bitmap);DeleteDC(dest);ReleaseDC(IntPtr.Zero,source); }
    }
    private static void Save(string name,byte[] pixels)
    {
        // The sampled rectangle is wholly inside our opaque synthetic background.
        BitmapSource bitmap=BitmapSource.Create(size,size,96,96,PixelFormats.Bgra32,null,pixels,size*4);
        PngBitmapEncoder encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using(FileStream file=File.Create(Path.Combine(output,name+".png")))encoder.Save(file);
    }
    private static void Report(string name,uint flags)
    {
        byte[] pixels=Capture(flags);int changed=0,black=0,magenta=0;
        for(int p=0;p<pixels.Length;p+=4) {
            if(Math.Abs(pixels[p]-baseline[p])+Math.Abs(pixels[p+1]-baseline[p+1])+Math.Abs(pixels[p+2]-baseline[p+2])>12)++changed;
            if(pixels[p]<3&&pixels[p+1]<3&&pixels[p+2]<3)++black;
            if(pixels[p]>150&&pixels[p+1]<100&&pixels[p+2]>150)++magenta;
        }
        Console.WriteLine(name+": changed="+changed+"/"+(size*size)+", black="+black+", magenta="+magenta);
        Save(name,pixels);
    }
    private static void Check(bool value,string name)
    {
        if(!value)throw new Exception(name+"; "+DesktopBackdrop.FailureReason);
        Console.WriteLine("PASS: "+name);
    }
    private static void ServiceTests()
    {
        Check(DesktopBackdrop.IsSupported,"service recognizes actual supported OS and DWM");
        Check(DesktopBackdrop.SetEnabled(overlay,true),"service enables verified capture exclusion");
        BackdropFrame frame;
        Check(DesktopBackdrop.TryCapture(overlay,new Rect(left,top,size,size),out frame),"production service captures synthetic background");
        Check(frame.Width==size&&frame.Height==size&&frame.Stride==size*4&&frame.ScreenBounds==new Rect(left,top,size,size),"frame reports exact physical bounds and BGRA dimensions");
        int changed=0;for(int i=0;i<baseline.Length;++i)if(frame.Pixels[i]!=baseline[i])++changed;
        Check(changed==0,"production service background equals original checkerboard byte for byte");
        Save("06-production-service",frame.Pixels);
        Check(DesktopBackdrop.TryCapture(overlay,new Rect(left-40,top-40,80,80),out frame)&&frame.ScreenBounds==new Rect(left,top,40,40),"capture clips to the owning window with truthful bounds");
        Check(!DesktopBackdrop.TryCapture(overlay,Rect.Empty,out frame)&&frame==null,"empty bounds fail without a fabricated frame");
        Check(!DesktopBackdrop.TryCapture(overlay,new Rect(left,top,Double.PositiveInfinity,10),out frame)&&frame==null,"nonfinite bounds fail safely");
        Check(!DesktopBackdrop.TryCapture(overlay,new Rect(-100000,-100000,10,10),out frame)&&frame==null,"offscreen bounds fail safely");
        Check(DesktopBackdrop.SetEnabled(overlay,false),"disable restores display affinity");
        uint affinity;Check(GetWindowDisplayAffinity(new WindowInteropHelper(overlay).Handle,out affinity)&&affinity==0,"disabled window remains available to recording");
        Check(!DesktopBackdrop.TryCapture(overlay,new Rect(left,top,size,size),out frame),"disabled service does not capture");
        Check(DesktopBackdrop.SetEnabled(overlay,true),"re-enable works without rebuilding or hiding the window");
        overlay.Hide();
        Check(GetWindowDisplayAffinity(new WindowInteropHelper(overlay).Handle,out affinity)&&affinity==0,"one lifecycle hide restores affinity immediately");
        Check(!DesktopBackdrop.TryCapture(overlay,new Rect(left,top,size,size),out frame),"hidden window cannot capture");
        overlay.Show();
    }
    private static void Tick(object sender,EventArgs e)
    {
        try {
            if(step==0) {
                Point point=background.PointToScreen(new Point(190,140));left=(int)Math.Round(point.X);top=(int)Math.Round(point.Y);
                baseline=Capture(0x00CC0020);Save("00-synthetic-baseline",baseline);
                Point origin=background.PointFromScreen(new Point(left,top));
                overlay=new Window { Title="FreeIsland synthetic capture probe",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,
                    Background=Brushes.Magenta,Left=background.Left+origin.X,Top=background.Top+origin.Y,Width=160,Height=160,Topmost=true,ShowInTaskbar=false,ShowActivated=false };
                overlay.Show();Console.WriteLine("Overlay layered="+((GetWindowLong(new WindowInteropHelper(overlay).Handle,-20)&0x80000)!=0));
            } else if(step==1) {
                Report("01-layered-srccopy",0x00CC0020);Report("02-layered-captureblt",0x40CC0020);
                VersionInfo os=new VersionInfo { Size=Marshal.SizeOf(typeof(VersionInfo)) };int status=RtlGetVersion(ref os);
                Console.WriteLine("RtlGetVersion status="+status+" OS="+os.Major+"."+os.Minor+"."+os.Build);
                if(status==0&&(os.Major>10||(os.Major==10&&os.Build>=19041))) {
                    affinitySet=SetWindowDisplayAffinity(new WindowInteropHelper(overlay).Handle,0x11);
                    int error=Marshal.GetLastWin32Error();uint actual;bool query=GetWindowDisplayAffinity(new WindowInteropHelper(overlay).Handle,out actual);
                    Console.WriteLine("WDA_EXCLUDEFROMCAPTURE success="+affinitySet+" error="+error+" query="+query+" value="+actual);
                } else Console.WriteLine("WDA excluded: unsupported OS; never try WDA_MONITOR fallback.");
            } else if(step==2) {
                if(affinitySet){Report("03-excluded-srccopy",0x00CC0020);Report("04-excluded-captureblt",0x40CC0020);}
                if(affinitySet){Console.WriteLine("Affinity restoration="+SetWindowDisplayAffinity(new WindowInteropHelper(overlay).Handle,0));overlay.InvalidateVisual();}
            } else if(step==3) {
                Report("05-restored-captureblt",0x40CC0020);ServiceTests();
            } else {
                uint affinity;
                Check(GetWindowDisplayAffinity(new WindowInteropHelper(overlay).Handle,out affinity)&&affinity==17,"subsequent normal show reapplies requested exclusion");
                Check(DesktopBackdrop.SetEnabled(overlay,false),"final disable restores affinity");
                timer.Stop();overlay.Close();background.Close();Application.Current.Shutdown(0);return;
            }
            ++step;
        } catch(Exception error) { Console.Error.WriteLine(error);timer.Stop();Application.Current.Shutdown(1); }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        output=args.Length==1?Path.GetFullPath(args[0]):Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),"desktop-backdrop-probe");
        Directory.CreateDirectory(output);
        Application app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        background=new Window { Title="FreeIsland synthetic checkerboard only",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,
            Background=Brushes.White,Content=new Checker(),Left=80,Top=80,Width=640,Height=480,Topmost=true,ShowInTaskbar=false };
        background.Show();timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(600) };timer.Tick+=Tick;timer.Start();return app.Run();
    }
}
