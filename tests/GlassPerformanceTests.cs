using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using FreeIsland;

// Dynamic in-memory pixels only. No desktop capture, user data or system actions.
internal static class GlassPerformanceTests
{
    private static int checks, calls;
    private static BackdropFrame frame;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    private static void Pump(int milliseconds)
    {
        var loop = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += delegate { timer.Stop(); loop.Continue = false; }; timer.Start(); Dispatcher.PushFrame(loop);
    }
    private static BackdropFrame Backdrop(Rect rect)
    {
        calls++;
        int width = Math.Max(2, (int)Math.Ceiling(rect.Width)), height = Math.Max(2, (int)Math.Ceiling(rect.Height));
        if (frame == null || frame.Width != width || frame.Height != height)
        {
            frame = new BackdropFrame { Width = width, Height = height, Stride = width * 4, Pixels = new byte[width * height * 4] };
            for (int i = 0; i < frame.Pixels.Length; i += 4) { frame.Pixels[i] = 170; frame.Pixels[i + 1] = 204; frame.Pixels[i + 2] = 236; frame.Pixels[i + 3] = 255; }
        }
        frame.ScreenBounds = rect;
        // A changing sampled pixel requires genuine refraction and WritePixels each tick.
        frame.Pixels[0] = (byte)(calls % 251);
        return frame;
    }
    private static double Measure(LiquidGlassSurface surface, string label, int milliseconds)
    {
        Pump(200); int count = surface.RenderedFrames; var clock = Stopwatch.StartNew(); Pump(milliseconds); clock.Stop();
        double fps = (surface.RenderedFrames - count) / clock.Elapsed.TotalSeconds;
        Console.WriteLine(label + ": " + fps.ToString("F2") + " generated material frames/sec; " + (surface.RenderedFrames - count) + " distinct updates / " + clock.Elapsed.TotalSeconds.ToString("F3") + " seconds");
        return fps;
    }
    [STAThread] private static int Main()
    {
        Window window = null;
        try
        {
            new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SurfaceStyle.SnapshotMode = false; LiquidGlassSurface.PreviewBackdrop = Backdrop;
            var surface = new LiquidGlassSurface { Mode = 2, Radius = 28, HighDetailRefraction = true };
            window = new Window { Width = 660, Height = 822, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                ShowInTaskbar = false, Left = 60, Top = 60, Content = surface };
            window.Show(); Pump(250);
            Check(surface.HasRefraction && surface.IsUpdating, "visible synthetic Water has an active bounded lens");
            double resting = Measure(surface, "Classroom 660x822 resting dynamic backdrop", 2400);
            surface.Pressed = true; surface.Pointer(new Point(600, 80));
            double phase = 0;
            var drag = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            drag.Tick += delegate { phase += .16; surface.Pointer(new Point(330 + 250 * Math.Sin(phase), 410 + 300 * Math.Cos(phase))); };
            drag.Start();
            double active = Measure(surface, "Classroom 660x822 active dynamic backdrop", 2400);
            drag.Stop();
            Check(resting >= 30, "resting dynamic material must reach 30 generated FPS on this test PC");
            Check(active >= 30, "active dynamic material must reach 30 generated FPS on this test PC");
            var image = (System.Windows.Media.Imaging.WriteableBitmap)typeof(LiquidGlassSurface).GetField("image", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(surface);
            Check(image.PixelHeight >= 821 && image.PixelHeight <= 1024, "performance retains full classroom lens detail");
            surface.Mode = 1; Pump(100); int paused = calls; Pump(120);
            Check(!surface.IsUpdating && !surface.HasRefraction && calls == paused, "Lite does not sample or update");
            surface.Mode = 0; Pump(100); paused = calls; Pump(120);
            Check(!surface.IsUpdating && calls == paused, "Off does not sample or update");
            surface.Mode = 2; Pump(100); window.Hide(); Pump(100); paused = calls; Pump(120);
            Check(!surface.IsUpdating && !surface.HasRefraction && calls == paused, "hidden material releases buffers and stops sampling");
            Console.WriteLine("PASS " + checks + " glass performance/lifecycle checks. Generated-material FPS includes fixture capture, refraction and WritePixels; it is not a display/compositor or real desktop-capture benchmark.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (window != null) window.Close(); LiquidGlassSurface.PreviewBackdrop = null; SurfaceStyle.SnapshotMode = false; }
    }
}
