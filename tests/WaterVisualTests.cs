using System;
using System.IO;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FreeIsland;

internal static class WaterVisualTests
{
    private static Application app;
    private static CoreEngine engine;
    private static IslandWindow island;
    private static RadialWindow radial;
    private static BallWindow ball;
    private static DispatcherTimer timer;
    private static int step, checks;
    private static bool dark;
    private static string output;
    private static readonly List<BitmapSource> boards = new List<BitmapSource>();
    private static readonly List<LiquidGlassSurface> surfaces = new List<LiquidGlassSurface>();
    private static void Check(bool ok, string message) { if(!ok) throw new Exception(message); checks++; }
    private static void Find(DependencyObject node)
    {
        var lens=node as LiquidGlassSurface;if(lens!=null)surfaces.Add(lens);
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)Find(VisualTreeHelper.GetChild(node,i));
    }
    private static BackdropFrame Pattern(Rect rect)
    {
        int x=(int)Math.Floor(rect.X),y=(int)Math.Floor(rect.Y),w=Math.Max(2,(int)Math.Ceiling(rect.Right)-x),h=Math.Max(2,(int)Math.Ceiling(rect.Bottom)-y);
        byte[] bytes=new byte[w*h*4];
        for(int yy=0,i=0;yy<h;yy++)for(int xx=0;xx<w;xx++,i+=4)
        {
            int gx=x+xx,gy=y+yy; bool grid=gx%38<2||gy%38<2;
            bool colour=(gx/140+gy/140)%3==0;
            byte r=(byte)(dark ? 26 : 239),g=(byte)(dark ? 38 : 244),b=(byte)(dark ? 48 : 249);
            if(colour){r=(byte)(dark?28:160);g=(byte)(dark?78:215);b=(byte)(dark?98:229);}
            if(grid){r=(byte)(dark?120:80);g=(byte)(dark?152:121);b=(byte)(dark?175:145);}
            bytes[i]=b;bytes[i+1]=g;bytes[i+2]=r;bytes[i+3]=255;
        }
        return new BackdropFrame{Pixels=bytes,Width=w,Height=h,Stride=w*4,ScreenBounds=new Rect(x,y,w,h)};
    }
    private static BitmapSource Snapshot(Window window)
    {
        window.UpdateLayout();int w=(int)Math.Ceiling(window.ActualWidth),h=(int)Math.Ceiling(window.ActualHeight);
        Point a=window.PointToScreen(new Point()),b=window.PointToScreen(new Point(w,h));
        var frame=Pattern(new Rect(a,b));
        var background=BitmapSource.Create(frame.Width,frame.Height,96,96,PixelFormats.Bgra32,null,frame.Pixels,frame.Stride);
        var overlay=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);overlay.Render(window);
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawImage(background,new Rect(0,0,w,h));dc.DrawImage(overlay,new Rect(0,0,w,h));}
        var result=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);result.Render(visual);return result;
    }
    private static void Save(BitmapSource image,string name)
    {var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(Path.Combine(output,name)))encoder.Save(file);}
    private static void Tick(object sender,EventArgs args)
    {
        try
        {
            if(step%2==0 && step<16)
            {
                int scene=step/2;dark=(scene%2)==1;
                engine.Settings.Scene=scene<4?UsageScene.Desktop:UsageScene.Classroom;
                engine.Settings.GlassMode=(scene/2)%2==0?2:1;
                island=new IslandWindow(engine,delegate{},null);radial=new RadialWindow(engine,delegate{},delegate{});ball=new BallWindow(engine,delegate{},delegate{});
                ball.Show();island.ShowActivity("countdown");radial.OpenAt(650,430,new Rect(0,0,1500,1000));
                island.Left=120;island.Top=150;ball.Left=180;ball.Top=370;
                surfaces.Clear();Find(island);Find(radial);Find(ball);
                step++;return;
            }
            if(step<16)
            {
                foreach(var surface in surfaces)Check(engine.Settings.GlassMode==2?surface.HasRefraction:!surface.HasRefraction,"material mode mismatch: "+surface.Name+" mode="+surface.Mode+" visible="+surface.IsVisible+" updating="+surface.IsUpdating+" size="+surface.ActualWidth+"x"+surface.ActualHeight);
                string label=(engine.Settings.Scene==UsageScene.Classroom?"classroom":"desktop")+"-"+(engine.Settings.GlassMode==2?"water":"lite")+"-"+(dark?"dark":"light");
                var a=Snapshot(island);var b=Snapshot(radial);var c=Snapshot(ball);
                Save(a,label+"-island.png");Save(b,label+"-menu.png");Save(c,label+"-orb.png");
                var visual=new DrawingVisual();using(var dc=visual.RenderOpen())
                {
                    dc.DrawRectangle(dark?new SolidColorBrush(Color.FromRgb(26,38,48)):Brushes.White,null,new Rect(0,0,900,500));
                    dc.DrawText(new FormattedText(label,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),16,dark?Brushes.White:Brushes.Black),new Point(16,10));
                    dc.DrawImage(a,new Rect(15,45,Math.Min(860,a.Width),Math.Min(860,a.Width)*a.Height/a.Width));
                    dc.DrawImage(b,new Rect(350,170,310,310));dc.DrawImage(c,new Rect(110,270,80,80));
                }
                var board=new RenderTargetBitmap(900,500,96,96,PixelFormats.Pbgra32);board.Render(visual);boards.Add(board);
                island.Hide();radial.Hide();ball.Hide();foreach(var surface in surfaces)Check(!surface.IsUpdating,"hidden material must stop capture and motion timer");
                island.Close();radial.Close();ball.Close();step++;return;
            }
            for(int page=0;page<2;page++)
            {
                var visual=new DrawingVisual();using(var dc=visual.RenderOpen())for(int i=0;i<4;i++)dc.DrawImage(boards[page*4+i],new Rect((i%2)*900,(i/2)*500,900,500));
                var image=new RenderTargetBitmap(1800,1000,96,96,PixelFormats.Pbgra32);image.Render(visual);Save(image,"review-"+(page==0?"desktop":"classroom")+".png");
            }
            File.WriteAllText(Path.Combine(output,"result.txt"),checks+" checks passed; deterministic owned fixture only; production surfaces rendered.");timer.Stop();app.Shutdown(0);
        }
        catch(Exception error){File.WriteAllText(Path.Combine(output,"result.txt"),error.ToString());timer.Stop();app.Shutdown(1);}
    }
    [STAThread]private static int Main(string[] args)
    {
        output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
        SurfaceStyle.SnapshotMode=true;LiquidGlassSurface.PreviewBackdrop=Pattern;
        engine=new CoreEngine(Path.Combine(output,"test-data"),true);engine.Settings.AutoStart=false;engine.Settings.EdgeHide=false;
        app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(650) };timer.Tick+=Tick;timer.Start();return app.Run();
    }
}
