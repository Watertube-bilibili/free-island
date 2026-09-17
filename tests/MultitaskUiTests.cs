using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using FreeIsland;
using Path = System.IO.Path;

// Production surfaces and actions with a deterministic clock and owned backdrop pixels.
// This entry point never constructs FreeIslandApp or invokes real shutdown/registry changes.
internal static class MultitaskUiTests
{
    private static string output;
    private static int checks, samples;
    private static Application app;
    private static readonly List<string> log = new List<string>();
    private static readonly Dictionary<string, BitmapSource> shots = new Dictionary<string, BitmapSource>();

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); checks++; }

    private static List<T> FindAll<T>(DependencyObject node) where T : DependencyObject
    {
        var found = new List<T>();
        T match = node as T; if (match != null) found.Add(match);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) found.AddRange(FindAll<T>(VisualTreeHelper.GetChild(node, i)));
        return found;
    }
    private static T Find<T>(DependencyObject node, string name) where T : FrameworkElement
    {
        foreach (T element in FindAll<T>(node)) if (name == null || element.Name == name) return element;
        throw new InvalidOperationException("Missing " + typeof(T).Name + " " + name);
    }
    private static void Click(DependencyObject node, string name)
    { Find<Button>(node, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(35); }
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
    private static BackdropFrame Pattern(Rect rect)
    {
        samples++;
        int x = (int)Math.Floor(rect.X), y = (int)Math.Floor(rect.Y);
        int width = Math.Max(2, (int)Math.Ceiling(rect.Right) - x), height = Math.Max(2, (int)Math.Ceiling(rect.Bottom) - y);
        byte[] bytes = new byte[width * height * 4];
        for (int yy = 0, i = 0; yy < height; yy++) for (int xx = 0; xx < width; xx++, i += 4)
        {
            int gx = x + xx, gy = y + yy;
            bool line = gx % 38 < 2 || gy % 38 < 2;
            bool color = (gx / 140 + gy / 140) % 3 == 0;
            bytes[i] = (byte)(line ? 145 : color ? 229 : 249);
            bytes[i + 1] = (byte)(line ? 121 : color ? 215 : 244);
            bytes[i + 2] = (byte)(line ? 80 : color ? 160 : 239);
            bytes[i + 3] = 255;
        }
        return new BackdropFrame { Pixels = bytes, Width = width, Height = height, Stride = width * 4, ScreenBounds = new Rect(x, y, width, height) };
    }
    private static BitmapSource Snapshot(Window window)
    {
        window.UpdateLayout();
        Matrix device = Device(window);
        int width = (int)Math.Ceiling(window.ActualWidth * device.M11), height = (int)Math.Ceiling(window.ActualHeight * device.M22);
        Point origin = window.PointToScreen(new Point());
        BackdropFrame frame = Pattern(new Rect(origin.X, origin.Y, width, height));
        BitmapSource background = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Pixels, frame.Stride);
        var overlay = new RenderTargetBitmap(width, height, 96 * device.M11, 96 * device.M22, PixelFormats.Pbgra32); overlay.Render(window);
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        { dc.DrawImage(background, new Rect(0, 0, width, height)); dc.DrawImage(overlay, new Rect(0, 0, width, height)); }
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual); return image;
    }
    private static void Save(BitmapSource image, string name)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (FileStream file = File.Create(Path.Combine(output, name))) encoder.Save(file);
    }
    private static void InWork(Window window, Rect work, string label)
    {
        Check(window.Left >= work.Left - .01 && window.Top >= work.Top - .01 &&
            window.Left + window.Width <= work.Right + .01 && window.Top + window.Height <= work.Bottom + .01, label + " escaped work area");
    }
    private static void DragAttachment(Window window, DependencyObject button)
    {
        var field = typeof(TouchWindowDrag).GetField("StateProperty", BindingFlags.Static | BindingFlags.NonPublic);
        var state = window.GetValue((DependencyProperty)field.GetValue(null));
        Check(state != null && !Stylus.GetIsPressAndHoldEnabled(window), "Direct touch drag attachment retained");
        if (button != null)
        {
            var accepts = (Func<DependencyObject, bool>)state.GetType().GetField("accepts", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(state);
            Check(accepts != null && !accepts(button) && accepts((DependencyObject)window.Content), "Task buttons stay operable while empty surface accepts touch drag");
        }
    }
    private static void CheckRows(IslandWindow island, int expected)
    {
        var rows = FindAll<IslandTaskCard>(island);
        Check(rows.Count == expected, "Expected simultaneous " + expected + " task cards, got " + rows.Count);
        double bottom = -1;
        foreach (IslandTaskCard row in rows)
        {
            Point origin = row.TranslatePoint(new Point(), island);
            Point end = row.TranslatePoint(new Point(row.ActualWidth, row.ActualHeight), island);
            Check(row.IsVisible && row.ActualHeight > 0 && origin.Y >= bottom - .01, "Task cards overlap or are hidden");
            Check(origin.X >= -.01 && end.X <= island.ActualWidth + .01 && end.Y <= island.ActualHeight + .01, "Task card clips outside island");
            bottom = end.Y;
            Check(Find<Button>(row, "TaskPrimary_" + row.Kind).ActualHeight >= 44, "Task primary touch target below 44 DIP");
        }
    }
    private static void Geometry(CoreEngine engine, IslandHandleWindow handle, Rect work)
    {
        foreach (int configured in new[] { 0, 40, 64, 88, 160 })
        {
            engine.Settings.ActiveIslandSize = configured;
            int expected = configured == 0 ? engine.Settings.Scene == UsageScene.Classroom ? 88 : 64 : configured;
            foreach (IslandPlacement placement in new[] { IslandPlacement.Top, IslandPlacement.Left, IslandPlacement.Right })
            {
                engine.Settings.Placement = placement; handle.ShowAt(work); Pump(15); handle.UpdateLayout();
                var thumbnail = Find<TaskThumbnail>(handle, "IslandTaskThumbnail");
                var glass = Find<LiquidGlassSurface>(handle, "IslandHandleGlassMaterial");
                Matrix device = Device(handle);
                Check(Math.Abs(thumbnail.Width * device.M11 - expected) < .01 && Math.Abs(thumbnail.Height * device.M22 - expected) < .01, "Thumbnail physical pixels mismatch " + expected);
                Check(Math.Abs(glass.Width * device.M11 - expected) < .01 && Math.Abs(glass.Height * device.M22 - expected) < .01, "Glass physical pixels mismatch " + expected);
                double target = engine.Settings.Scene == UsageScene.Classroom ? 44 : 24;
                Check(handle.ActualWidth >= target && handle.ActualHeight >= target, "Active handle touch target shrank below scene minimum");
                Check(handle.InputHitTest(new Point(1, handle.ActualHeight - 1)) != null, "Transparent touch area rejects input");
                InWork(handle, work, "Active handle");
            }
        }
        engine.Settings.ActiveIslandSize = 0; engine.Settings.Placement = IslandPlacement.Top; handle.ShowAt(work);
    }
    private static void Case(UsageScene scene, int mode)
    {
        DateTime now = new DateTime(2030, 5, 10, 8, 0, 0, DateTimeKind.Utc);
        string label = scene.ToString().ToLowerInvariant() + "-" + new[] { "off", "lite", "water" }[mode];
        using (CoreEngine engine = new CoreEngine(Path.Combine(output, "isolated-" + label), true, delegate { return now; }, delegate { throw new Exception("Shutdown forbidden"); }))
        {
            engine.Settings.AutoStart = false; engine.Settings.Scene = scene; engine.Settings.GlassMode = mode; engine.Settings.SoundEnabled = false;
            LiquidGlass.Configure(engine.Settings);
            engine.StartCountdown(TimeSpan.FromSeconds(40)); engine.ToggleStopwatch(); now = now.AddSeconds(20);
            IslandWindow island = null; IslandHandleWindow handle = null;
            try
            {
                island = new IslandWindow(engine, delegate { }, delegate { });
                Rect work = island.LastWorkArea;
                handle = new IslandHandleWindow(engine, delegate { island.ShowTasks(); }, delegate { });
                engine.Changed += delegate { if (island.IsVisible) island.RefreshActivity(); handle.RefreshTasks(); };
                engine.Notice += delegate(object sender, IslandNoticeEventArgs notice) { island.ShowNotice(notice); };
                island.ShowTasks(); Pump(mode == 2 ? 220 : 60); island.UpdateLayout();
                CheckRows(island, 2);
                DragAttachment(island, Find<Button>(island, "TaskPrimary_countdown")); DragAttachment(handle, null);
                foreach (IslandPlacement placement in new[] { IslandPlacement.Top, IslandPlacement.Left, IslandPlacement.Right })
                {
                    engine.Settings.Placement = placement; island.Reposition(); island.UpdateLayout(); InWork(island, island.LastWorkArea, "Task stack " + placement);
                }
                engine.Settings.Placement = IslandPlacement.Top; island.Reposition();
                shots[label + "-stack"] = Snapshot(island); Save(shots[label + "-stack"], label + "-stack.png");
                Click(island, "TaskPrimary_countdown"); Check(!engine.CountdownRunning && engine.StopwatchRunning, "Countdown pause affected other task");
                Check(Find<Button>(island, "TaskPrimary_countdown").Content.ToString() == "继续", "Countdown pause action label");
                Click(island, "TaskPrimary_stopwatch"); Check(!engine.StopwatchRunning && engine.StopwatchActive, "Stopwatch remains active when paused");
                CheckRows(island, 2);
                Click(island, "TaskPrimary_countdown"); Click(island, "TaskPrimary_stopwatch");
                Check(engine.CountdownRunning && engine.StopwatchRunning, "Both cards independently resume");
                island.ShowNotice(new IslandNoticeEventArgs { Title = "课间提醒", Message = "喝水，准备下一节课", Kind = "reminder", Urgent = true }); Pump(30);
                CheckRows(island, 2); Check(Find<Border>(island, "IslandTextBacking").IsVisible, "Reminder notice disappears beside tasks");
                Check(island.Height > shots[label + "-stack"].PixelHeight / Device(island).M22 + 30, "Notice did not add its own row");
                island.ShowNotice(new IslandNoticeEventArgs { Title = "已取消过期关机", Message = "计划已取消", Kind = "shutdown", Urgent = true }); Pump(30);
                Check(Find<Border>(island, "IslandTextBacking").IsVisible, "Shutdown cancellation notice hidden by unrelated tasks");
                island.ShowTasks(); island.Collapse(); Pump(30);
                handle.ShowAt(work); Pump(mode == 2 ? 220 : 40);
                var thumbnail = Find<TaskThumbnail>(handle, "IslandTaskThumbnail");
                Check(AutomationProperties.GetName(thumbnail).Contains("2 个任务"), "Collapsed thumbnail does not expose Arabic task count");
                Check(thumbnail.IsVisible, "Task thumbnail hidden while tasks exist");
                Geometry(engine, handle, work); Pump(mode == 2 ? 180 : 20);
                shots[label + "-thumbnail"] = Snapshot(handle); Save(shots[label + "-thumbnail"], label + "-thumbnail.png");
                var handleGlass = Find<LiquidGlassSurface>(handle, "IslandHandleGlassMaterial");
                Check(handleGlass.IsUpdating == (mode == 2), "Thumbnail glass timer mode mismatch");
                Check(handleGlass.HasRefraction == (mode == 2), "Thumbnail refraction missing or active in wrong mode");
                handle.Hide(); Pump(30);
                Check(!handleGlass.IsUpdating && !handleGlass.HasRefraction, "Hidden thumbnail keeps optical work alive");
                foreach (LiquidGlassSurface glass in FindAll<LiquidGlassSurface>(island)) Check(!glass.IsUpdating, "Collapsed stack material timer remains active");
                int hiddenSamples = samples; Pump(130); Check(samples == hiddenSamples, "Hidden surfaces keep sampling backdrop");
                island.ShowTasks(); Pump(40); Click(island, "TaskStop_countdown");
                Check(!engine.CountdownActive && engine.StopwatchActive, "Ending countdown also ended stopwatch"); CheckRows(island, 1);
                Click(island, "TaskStop_stopwatch"); Check(!engine.StopwatchActive && !island.IsVisible, "Ending last task does not collapse stack");
                handle.ShowAt(work); Pump(30); var dot = Find<Ellipse>(handle, null); Matrix scale = Device(handle);
                Check(!thumbnail.IsVisible && Math.Abs(dot.Width * scale.M11 - engine.Settings.IslandDotSize) < .01, "No-task handle failed to shrink to configured dot");
                Check(handle.Width == (scene == UsageScene.Classroom ? 44 : 24), "No-task touch area did not restore scene size");
                Check(AutomationProperties.GetName(thumbnail) == "灵动岛小点", "Stale task count remains after all tasks ended");
                engine.ScheduleShutdown(now.AddMilliseconds(10001));
                handle.RefreshTasks(); engine.Tick();
                Check(!island.IsVisible && !thumbnail.IsVisible, "Shutdown visible before final ten seconds");
                now = now.AddMilliseconds(1); engine.Tick(); Pump(40);
                Check(island.IsVisible, "Final ten seconds failed to automatically show shutdown"); CheckRows(island, 1);
                Check(Find<Button>(island, "TaskPrimary_shutdown").Content.ToString() == "取消关机", "Urgent shutdown lacks cancel action");
                Click(island, "TaskPrimary_shutdown"); Check(!engine.ShutdownAt.HasValue && !island.IsVisible, "Urgent cancel failed");
                log.Add("PASS " + label + ": independent actions, notice coexistence, 40/64/88/160 physical px, docking, touch, hidden optics, shrink, final10s shutdown; device scale " + scale.M11.ToString(CultureInfo.InvariantCulture));
            }
            finally { if (handle != null) handle.Close(); if (island != null) island.Close(); }
        }
    }
    private static void Boards()
    {
        foreach (string scene in new[] { "desktop", "classroom" })
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1040, 1140));
                for (int mode = 0; mode < 3; mode++)
                {
                    string name = scene + "-" + new[] { "off", "lite", "water" }[mode];
                    double y = mode * 380;
                    dc.DrawText(new FormattedText(name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 20, Brushes.Black), new Point(18, y + 12));
                    BitmapSource stack = shots[name + "-stack"], thumbnail = shots[name + "-thumbnail"];
                    double width = Math.Min(780, stack.PixelWidth), height = stack.PixelHeight * width / stack.PixelWidth;
                    dc.DrawImage(stack, new Rect(18, y + 52, width, height));
                    dc.DrawText(new FormattedText("1x / 3x", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, Brushes.Black), new Point(810, y + 55));
                    dc.DrawImage(thumbnail, new Rect(810, y + 85, thumbnail.PixelWidth, thumbnail.PixelHeight));
                    dc.DrawImage(thumbnail, new Rect(810, y + 165, thumbnail.PixelWidth * 3, thumbnail.PixelHeight * 3));
                }
            }
            var image = new RenderTargetBitmap(1040, 1140, 96, 96, PixelFormats.Pbgra32); image.Render(visual); Save(image, "review-" + scene + ".png");
        }
    }
    [STAThread] private static int Main(string[] args)
    {
        output = Path.GetFullPath(args.Length == 0 ? "artifacts/multitask-ui-1.0.6" : args[0]); Directory.CreateDirectory(output);
        int result = 1;
        try
        {
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SurfaceStyle.SnapshotMode = true; LiquidGlassSurface.PreviewBackdrop = Pattern;
            foreach (UsageScene scene in new[] { UsageScene.Desktop, UsageScene.Classroom }) for (int mode = 0; mode < 3; mode++) Case(scene, mode);
            Boards(); log.Add("RESULT " + checks + " checks passed; owned fixture + injected clock, no desktop capture or OS actions. DPI uses current composition target; other OS display scales not changed."); result = 0;
        }
        catch (Exception error) { log.Add("FAIL after " + checks + " checks: " + error); }
        finally
        {
            LiquidGlassSurface.PreviewBackdrop = null;
            if (app != null) app.Shutdown(result);
            File.WriteAllLines(Path.Combine(output, "result.txt"), log.ToArray()); foreach (string line in log) Console.WriteLine(line);
        }
        return result;
    }
}
