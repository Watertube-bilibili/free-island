using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FreeIsland;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

// Controlled cross-process Win32 paint fixtures only. Compile with
// FI_BACKDROP_TESTING and DesktopBackdrop.cs. No app settings, installation,
// background files, real shutdown, or external application automation.
internal static class LegacyBackdropTests
{
    private static int checks;
    private static Window overlay;
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("gdi32.dll")] private static extern bool PatBlt(IntPtr dc, int x, int y, int width, int height, uint operation);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rectangle rectangle);
    [StructLayout(LayoutKind.Sequential)] private struct Rectangle { internal int Left, Top, Right, Bottom; }

    private sealed class PaintFixture : Forms.Form
    {
        internal bool Foreground;
        internal PaintFixture(bool foreground)
        {
            Foreground = foreground;
            FormBorderStyle = Forms.FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = Forms.FormStartPosition.Manual;
            Bounds = foreground ? new Drawing.Rectangle(260, 230, 60, 60) : new Drawing.Rectangle(180, 180, 400, 260);
        }
        protected override void OnPaint(Forms.PaintEventArgs unused) { }
        protected override void OnPaintBackground(Forms.PaintEventArgs e)
        {
            if (Foreground) { e.Graphics.Clear(Drawing.Color.FromArgb(19, 211, 79)); return; }
            for (int y = 0; y < Height; y += 10) for (int x = 0; x < Width; x += 10)
            {
                using (var brush = new Drawing.SolidBrush(((x / 10 + y / 10) & 1) == 0 ? Drawing.Color.FromArgb(20, 80, 200) : Drawing.Color.FromArgb(230, 100, 30)))
                    e.Graphics.FillRectangle(brush, x, y, 10, 10);
            }
        }
    }

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); checks++; }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static Rect Bounds(Window window)
    {
        Rectangle rectangle;
        Check(GetWindowRect(new WindowInteropHelper(window).Handle, out rectangle), "read fixture bounds");
        return new Rect(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
    }
    private static BackdropFrame AwaitFrame(Rect bounds)
    {
        var elapsed = Stopwatch.StartNew();
        BackdropFrame frame;
        while (elapsed.ElapsedMilliseconds < 6000)
        {
            if (DesktopBackdrop.TryCapture(overlay, bounds, out frame)) return frame;
            Pump(40);
        }
        throw new Exception("No fixture frame: " + DesktopBackdrop.FailureReason);
    }
    private static void Pixel(BackdropFrame frame, int screenX, int screenY, byte red, byte green, byte blue, string label)
    {
        int x = screenX - (int)frame.ScreenBounds.X, y = screenY - (int)frame.ScreenBounds.Y;
        int index = y * frame.Stride + x * 4;
        Check(frame.Pixels[index] == blue && frame.Pixels[index + 1] == green && frame.Pixels[index + 2] == red && frame.Pixels[index + 3] == 255,
            label + " got RGB " + frame.Pixels[index + 2] + "," + frame.Pixels[index + 1] + "," + frame.Pixels[index]);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 0 && args[0] == "--fixture")
        {
            Forms.Application.EnableVisualStyles();
            var back = new PaintFixture(false); var front = new PaintFixture(true);
            back.Shown += delegate { front.Show(); Console.WriteLine(back.Handle.ToInt64()); Console.Out.Flush(); };
            Forms.Application.Run(back); return 0;
        }
        Process fixture = null; Window ownUnderlay = null;
        string output = args.Length == 0 ? "." : args[0];
        Directory.CreateDirectory(output);
        try
        {
            fixture = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "--fixture")
            { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            IntPtr fixtureHandle = new IntPtr(Int64.Parse(fixture.StandardOutput.ReadLine()));
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            DesktopBackdrop.ForceLegacy = true;
            ownUnderlay = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Cyan,
                Left = 200, Top = 200, Width = 180, Height = 160, Topmost = true, ShowInTaskbar = false };
            ownUnderlay.Show();
            overlay = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Magenta,
                Left = 220, Top = 220, Width = 140, Height = 100, Topmost = true, ShowInTaskbar = false };
            overlay.Show(); Pump(150);
            double opacity = overlay.Opacity;
            uint before, after;
            IntPtr hwnd = new WindowInteropHelper(overlay).Handle;
            Check(GetWindowDisplayAffinity(hwnd, out before), "read initial affinity");
            Check(DesktopBackdrop.IsSupported && DesktopBackdrop.UsesLegacyCapture, "force oldest-Win10 backend on current OS");
            Check(DesktopBackdrop.SetEnabled(overlay, true), "enable legacy capture");
            Rect bounds = Bounds(overlay);
            BackdropFrame frame = AwaitFrame(bounds);
            Pixel(frame, 225, 225, 20, 80, 200, "back pattern preserves crop coordinates and excludes own cyan/magenta surfaces");
            Pixel(frame, 235, 225, 230, 100, 30, "second checker colour");
            Pixel(frame, 275, 245, 19, 211, 79, "front source has correct z-order");
            Check(GetWindowDisplayAffinity(hwnd, out after) && before == after && after == 0, "legacy never changes display affinity");
            Check(overlay.IsVisible && overlay.Opacity == opacity, "legacy never hides or changes opacity");
            Check(frame.Pixels.Length == frame.Width * frame.Height * 4, "allocation limited to overlay crop");
            Console.WriteLine("PASS: genuine cross-process pixels, crop geometry, z-order, own-process exclusion, no affinity/visibility cycling");

            int nativeBefore = DesktopBackdrop.LegacyNativeCaptures;
            for (int pass = 0; pass < 4; pass++) for (int index = 0; index < 6; index++)
            {
                Rect crop = new Rect(bounds.Left + 5 + index * 15, bounds.Top + 5, 12, 12);
                BackdropFrame shared;
                Check(DesktopBackdrop.TryCapture(overlay, crop, out shared), "interleaved radial surface always gets a frame");
                Check(shared.ScreenBounds == bounds && shared.Pixels.Length == frame.Pixels.Length, "all radial surfaces share whole-owner coordinates");
            }
            Check(DesktopBackdrop.LegacyNativeCaptures == nativeBefore, "24 adjacent crop requests do not recapture native source windows");
            DesktopBackdrop.SetEnabled(ownUnderlay, true);
            BackdropFrame secondary = null;
            Rect secondaryBounds = Bounds(ownUnderlay);
            var fairness = Stopwatch.StartNew();
            while (secondary == null && fairness.ElapsedMilliseconds < 2500)
            {
                BackdropFrame ignored;
                DesktopBackdrop.TryCapture(overlay, bounds, out ignored);
                DesktopBackdrop.TryCapture(ownUnderlay, secondaryBounds, out secondary);
                if (secondary == null) Pump(20);
            }
            Check(secondary != null && secondary.ScreenBounds == secondaryBounds, "simultaneous floating windows receive fair coalesced work");
            Pixel(secondary, 225, 225, 20, 80, 200, "second window uses same genuine source pixels");
            Check(DesktopBackdrop.LegacyNativeCaptures - nativeBefore <= 2, "second window reuses the two-entry source cache within its refresh budget");
            Check(DesktopBackdrop.LegacyPeakSourcePixels <= 4096 * 2160, "total native cache stays inside pixel budget");
            DesktopBackdrop.SetEnabled(ownUnderlay, false);
            Console.WriteLine("PASS: 24 interleaved radial crops, cross-window fairness, shared native cache, bounded native memory");

            // A moved request must not reuse pixels captured at the previous position.
            overlay.Left += 10; Pump(40);
            Rect moved = Bounds(overlay);
            BackdropFrame pending;
            Check(!DesktopBackdrop.TryCapture(overlay, moved, out pending) && pending == null, "old-position cache rejected immediately");
            frame = AwaitFrame(moved);
            Pixel(frame, 235, 225, 230, 100, 30, "moved capture is correctly aligned");
            Rect oversized = new Rect(-10000000, -10000000, 20000000, 20000000);
            frame = AwaitFrame(oversized);
            Check(frame.ScreenBounds == moved, "huge requests clipped before integer conversion/allocation");
            Check(!DesktopBackdrop.TryCapture(overlay, Rect.Empty, out pending), "empty region rejected");
            Console.WriteLine("PASS: changed position, physical bounds, oversized and invalid request guards");

            DesktopBackdrop.SetEnabled(overlay, false); Pump(100);
            Check(!DesktopBackdrop.TryCapture(overlay, moved, out pending) && pending == null, "disabled capture returns no cache");
            // Inject only at the PrintWindow boundary to model APIs that return
            // success without pixels. Earlier assertions use the real native API.
            DesktopBackdrop.LegacyPrintOverride = delegate(IntPtr handle, IntPtr dc) { return handle == fixtureHandle || PrintWindow(handle, dc, 2); };
            DesktopBackdrop.SetEnabled(overlay, true); Pump(200);
            DesktopBackdrop.TryCapture(overlay, moved, out pending); Pump(150);
            Check(!DesktopBackdrop.TryCapture(overlay, moved, out pending) && pending == null, "unpainted source falls back without invented pixels");
            DesktopBackdrop.SetEnabled(overlay, false); Pump(150);
            DesktopBackdrop.LegacyPrintOverride = delegate(IntPtr handle, IntPtr dc)
            { return handle == fixtureHandle ? PatBlt(dc, 0, 0, 400, 260, 0x42) : PrintWindow(handle, dc, 2); };
            DesktopBackdrop.SetEnabled(overlay, true);
            DesktopBackdrop.TryCapture(overlay, moved, out pending); Pump(150);
            Check(!DesktopBackdrop.TryCapture(overlay, moved, out pending) && pending == null, "successful black capture falls back without false refraction");
            Console.WriteLine("PASS: unsupported window content returns clear fallback");

            DesktopBackdrop.SetEnabled(overlay, false); Pump(150);
            int delayOnce = 1;
            DesktopBackdrop.LegacyPrintOverride = delegate(IntPtr handle, IntPtr dc)
            {
                if (handle == fixtureHandle && Interlocked.Exchange(ref delayOnce, 0) == 1) Thread.Sleep(750);
                return PrintWindow(handle, dc, 2);
            };
            DesktopBackdrop.SetEnabled(overlay, true);
            DesktopBackdrop.TryCapture(overlay, moved, out pending); Pump(100);
            int jobs = DesktopBackdrop.LegacyJobsStarted;
            var calls = Stopwatch.StartNew();
            for (int i = 0; i < 200; i++) DesktopBackdrop.TryCapture(overlay, moved, out pending);
            calls.Stop();
            Check(calls.ElapsedMilliseconds < 100, "slow source does not block UI calls");
            Check(DesktopBackdrop.LegacyJobsStarted == jobs, "slow source does not spawn a job queue");
            Pump(220);
            Check(!DesktopBackdrop.TryCapture(overlay, moved, out pending) && pending == null, "overdue background job does not publish a frame");
            Check(DesktopBackdrop.LegacyPeakWorkers == 1, "one worker bound");
            overlay.Hide(); Pump(550);
            Check(!DesktopBackdrop.TryCapture(overlay, moved, out pending) && pending == null, "hide discards late completion");
            overlay.Show(); Pump(100); frame = AwaitFrame(Bounds(overlay));
            Check(frame != null, "capture resumes after source returns and overlay is shown");
            DesktopBackdrop.SetEnabled(overlay, false);
            Pump(100);
            Check(DesktopBackdrop.LegacyCachedSourcePixels == 0, "disabling last glass window releases source pixels");
            Console.WriteLine("PASS: hung-source responsiveness, bounded worker, timeout, hide/reshow lifecycle");
            File.WriteAllText(Path.Combine(output, "result.txt"), "PASS " + checks + " assertions; forced legacy backend; controlled GDI fixture on current Windows; no actual Windows 10 1507 hardware test.\r\n");
            Console.WriteLine("PASS " + checks + " assertions");
            return 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(output, "result.txt"), exception.ToString()); Console.WriteLine(exception); return 1;
        }
        finally
        {
            if (overlay != null) overlay.Close(); if (ownUnderlay != null) ownUnderlay.Close();
            if (fixture != null && !fixture.HasExited) { fixture.CloseMainWindow(); if (!fixture.WaitForExit(1500)) fixture.Kill(); }
        }
    }
}
