using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using FreeIsland;
using Path = System.IO.Path;

// Production handle + material, deterministic in-memory background only.
// No desktop capture, application startup, OS setting changes or shutdown actions.
internal static class IslandHandleGlassTests
{
    private static readonly int[] Sizes = { 3, 6, 20 };
    private static readonly int[] Modes = { 0, 1, 2, 1, 2, 0, 2 };
    private static readonly Dictionary<string, BitmapSource> Snapshots = new Dictionary<string, BitmapSource>();
    private static readonly List<string> Log = new List<string>();
    private static string output;
    private static bool dark;
    private static int checks, fixtureCalls;
    private static Application app;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
    }

    private static T Find<T>(DependencyObject node, string name) where T : FrameworkElement
    {
        T match = node as T;
        if (match != null && (name == null || match.Name == name)) return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            match = Find<T>(VisualTreeHelper.GetChild(node, i), name);
            if (match != null) return match;
        }
        return null;
    }

    private static BackdropFrame Pattern(Rect rect)
    {
        fixtureCalls++;
        int x = (int)Math.Floor(rect.X), y = (int)Math.Floor(rect.Y);
        int w = Math.Max(2, (int)Math.Ceiling(rect.Right) - x), h = Math.Max(2, (int)Math.Ceiling(rect.Bottom) - y);
        byte[] bytes = new byte[w * h * 4];
        for (int yy = 0, i = 0; yy < h; yy++) for (int xx = 0; xx < w; xx++, i += 4)
        {
            bool stripe = (((xx + x) / 2 + (yy + y) / 3) & 1) == 0;
            bytes[i] = (byte)(dark ? (stripe ? 55 : 92) : (stripe ? 239 : 205));
            bytes[i + 1] = (byte)(dark ? (stripe ? 33 : 59) : (stripe ? 246 : 226));
            bytes[i + 2] = (byte)(dark ? (stripe ? 22 : 32) : (stripe ? 250 : 211));
            bytes[i + 3] = 255;
        }
        return new BackdropFrame { Pixels = bytes, Width = w, Height = h, Stride = w * 4, ScreenBounds = new Rect(x, y, w, h) };
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }

    private static Matrix Device(Visual visual)
    {
        PresentationSource source = PresentationSource.FromVisual(visual);
        return source == null || source.CompositionTarget == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
    }

    private static BitmapSource Render(IslandHandleWindow window, FrameworkElement visual, Matrix device)
    {
        window.UpdateLayout();
        int w = Math.Max(1, (int)Math.Round(visual.ActualWidth * device.M11));
        int h = Math.Max(1, (int)Math.Round(visual.ActualHeight * device.M22));
        int ww = Math.Max(1, (int)Math.Round(window.ActualWidth * device.M11));
        int wh = Math.Max(1, (int)Math.Round(window.ActualHeight * device.M22));
        var bitmap = new RenderTargetBitmap(ww, wh, 96 * device.M11, 96 * device.M22, PixelFormats.Pbgra32);
        bitmap.Render(window);
        Point origin = visual.TranslatePoint(new Point(), window);
        return new CroppedBitmap(bitmap, new Int32Rect((int)Math.Round(origin.X * device.M11), (int)Math.Round(origin.Y * device.M22), w, h));
    }

    private static void CheckPixels(BitmapSource image, string label)
    {
        byte[] pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        int visible = 0;
        bool premultiplied = true;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            // Ignore the alpha=1 transparent hit area behind the actual material.
            if (pixels[i + 3] > 4) visible++;
            if (pixels[i] > pixels[i + 3] || pixels[i + 1] > pixels[i + 3] || pixels[i + 2] > pixels[i + 3]) premultiplied = false;
        }
        Check(visible > 0, label + ": tiny material rendered entirely transparent");
        Check(premultiplied, label + ": invalid premultiplied output");
    }

    private static void Geometry(IslandHandleWindow window, Ellipse dot, LiquidGlassSurface glass, UsageScene scene, int size)
    {
        window.UpdateLayout();
        Matrix device = Device(window);
        double target = scene == UsageScene.Classroom ? 44 : 24;
        Check(Math.Abs(window.ActualWidth - target) < .01 && Math.Abs(window.ActualHeight - target) < .01, "touch target changed with glass mode");
        Check(Math.Abs(dot.Width * device.M11 - size) < .01 && Math.Abs(dot.Height * device.M22 - size) < .01, "black dot physical diameter changed");
        Check(Math.Abs(glass.Width * device.M11 - size) < .01 && Math.Abs(glass.Height * device.M22 - size) < .01, "glass physical diameter changed");
        Check(Math.Abs(Canvas.GetLeft(dot) - Canvas.GetLeft(glass)) < .01 && Math.Abs(Canvas.GetTop(dot) - Canvas.GetTop(glass)) < .01, "dot and water positions differ");
        Check(window.InputHitTest(new Point(target - 2, target - 2)) != null, "transparent outer touch area stopped accepting input");
        Check(!glass.IsHitTestVisible && !dot.IsHitTestVisible, "material intercepted the handle drag/click target");
    }

    private static void Case(CoreEngine engine, UsageScene scene, int size)
    {
        engine.Settings.Scene = scene; engine.Settings.IslandDotSize = size; engine.Settings.GlassMode = 0;
        var window = new IslandHandleWindow(engine, delegate { throw new Exception("test must not activate real input"); }, delegate(Point p) { throw new Exception("test must not drag via real input"); });
        try
        {
            Rect work = new Rect(100, 100, 680, 460);
            window.ShowAt(work); Pump(70);
            var glass = Find<LiquidGlassSurface>(window, "IslandHandleGlassMaterial");
            var dot = Find<Ellipse>(window, null);
            Check(glass != null, "missing IslandHandleGlassMaterial"); Check(dot != null, "missing original black ellipse");
            Check(glass.Compact && glass.Orb, "tiny handle must opt into compact round glass");
            foreach (IslandPlacement placement in new[] { IslandPlacement.Top, IslandPlacement.Left, IslandPlacement.Right })
            {
                engine.Settings.Placement = placement; window.ShowAt(work); Pump(20);
                Geometry(window, dot, glass, scene, size);
                Check(window.Left >= work.Left && window.Top >= work.Top && window.Left + window.Width <= work.Right && window.Top + window.Height <= work.Bottom, "docked touch target extends outside work area");
            }
            engine.Settings.Placement = IslandPlacement.Top; window.ShowAt(work);
            foreach (int mode in Modes)
            {
                engine.Settings.GlassMode = mode; window.ApplyMaterial(); Pump(mode == 2 ? 180 : 30);
                string label = scene + "/" + size + "px/" + mode + "/" + (dark ? "dark" : "light");
                Check(glass.Mode == mode, label + ": setting was not applied");
                Check(dot.IsVisible == (mode == 0), label + ": black ellipse visibility");
                Check(glass.IsVisible == (mode != 0), label + ": glass visibility");
                Check(glass.HasRefraction == (mode == 2), label + ": refraction lifecycle");
                Check(glass.IsUpdating == (mode == 2), label + ": timer lifecycle");
                Geometry(window, dot, glass, scene, size);
                Matrix device = Device(window);
                BitmapSource material = Render(window, mode == 0 ? (FrameworkElement)dot : glass, device);
                CheckPixels(material, label);
                Check(material.PixelWidth == size && material.PixelHeight == size, label + ": physical raster dimensions");
                Snapshots[scene + "/" + (dark ? "dark" : "light") + "/" + size + "/" + mode] = material;
                if (mode != 2)
                {
                    int calls = fixtureCalls; Pump(110);
                    Check(fixtureCalls == calls, label + ": off/lite still samples fixture");
                }
            }
            window.Hide(); Pump(30);
            Check(!glass.IsUpdating && !glass.HasRefraction, "hidden handle retains timer or refraction");
            int hiddenCalls = fixtureCalls; Pump(140);
            Check(fixtureCalls == hiddenCalls, "hidden handle still samples fixture");
            window.ShowAt(work); Pump(180);
            Check(glass.HasRefraction && glass.IsUpdating, "reshown standard handle failed to resume");
            window.Close(); Pump(20);
            Check(!glass.IsUpdating && !glass.HasRefraction, "closed handle retains timer or refraction");
            Log.Add("PASS " + scene + " " + size + " physical px " + (dark ? "dark" : "light") + ": docking, transparent input target, 0/1/2 mode transitions, hide/show/close");
        }
        finally { if (window.IsLoaded) window.Close(); }
    }

    private static void Boards()
    {
        foreach (UsageScene scene in new[] { UsageScene.Desktop, UsageScene.Classroom }) foreach (string tone in new[] { "light", "dark" })
        {
            bool isDark = tone == "dark";
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(isDark ? Color.FromRgb(22, 33, 55) : Color.FromRgb(250, 246, 239)), null, new Rect(0, 0, 840, 660));
                Brush ink = isDark ? Brushes.White : Brushes.Black;
                for (int row = 0; row < Sizes.Length; row++) for (int mode = 0; mode < 3; mode++)
                {
                    int size = Sizes[row]; double x = mode * 280, y = row * 220;
                    string label = size + " px  " + new[] { "Off", "Lite", "Water" }[mode];
                    dc.DrawText(new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 16, ink), new Point(x + 20, y + 15));
                    BitmapSource material = Snapshots[scene + "/" + tone + "/" + size + "/" + mode];
                    dc.DrawImage(material, new Rect(x + 35, y + 55, size, size));
                    // Fixed 6x magnification makes the 3 px edges inspectable, without
                    // presenting the enlarged preview as the actual touch target.
                    dc.DrawImage(material, new Rect(x + 120, y + 55, size * 6, size * 6));
                    dc.DrawText(new FormattedText("1x / 6x", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, ink), new Point(x + 20, y + 190));
                }
            }
            var image = new RenderTargetBitmap(840, 660, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var file = File.Create(Path.Combine(output, "handle-" + scene.ToString().ToLowerInvariant() + "-" + tone + ".png"))) encoder.Save(file);
        }
    }

    [STAThread] private static int Main(string[] args)
    {
        output = Path.GetFullPath(args.Length == 0 ? "artifacts/island-handle-glass" : args[0]); Directory.CreateDirectory(output);
        int result = 1;
        try
        {
            Check(!SystemParameters.HighContrast, "fixture requires ordinary contrast; it never changes OS accessibility settings");
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SurfaceStyle.SnapshotMode = true; LiquidGlassSurface.PreviewBackdrop = Pattern;
            var engine = new CoreEngine(Path.Combine(output, "isolated-data"), true);
            engine.Settings.AutoStart = false; engine.Settings.EdgeHide = false;
            foreach (UsageScene scene in new[] { UsageScene.Desktop, UsageScene.Classroom }) foreach (int size in Sizes) foreach (bool tone in new[] { false, true })
            { dark = tone; Case(engine, scene, size); }
            Boards(); Log.Add("RESULT " + checks + " checks passed; 12 cases; fixed owned pattern only; no desktop capture or OS actions."); result = 0;
        }
        catch (Exception error) { Log.Add("FAIL after " + checks + " checks: " + error); }
        finally
        {
            LiquidGlassSurface.PreviewBackdrop = null;
            if (app != null) app.Shutdown(result);
            File.WriteAllLines(Path.Combine(output, "result.txt"), Log.ToArray());
            foreach (string line in Log) Console.WriteLine(line);
        }
        return result;
    }
}
