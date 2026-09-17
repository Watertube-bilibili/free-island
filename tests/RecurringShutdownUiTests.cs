using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FreeIsland;

internal static class RecurringShutdownUiTests
{
    private static int checks;
    private static string output;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    private static T Find<T>(DependencyObject node, string name) where T : FrameworkElement
    {
        T match = node as T; if (match != null && (name == null || match.Name == name)) return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) { match = Find<T>(VisualTreeHelper.GetChild(node, i), name); if (match != null) return match; }
        return null;
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) }; timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Click(ControlWindow window, string name)
    {
        var button = Find<Button>(window, name); Check(button != null && button.IsEnabled, "Available action " + name); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
    }
    private static int Days(ControlWindow window)
    {
        int result = 0; for (int i = 0; i < 7; i++) if (Find<CheckBox>(window, "ShutdownRepeatDay" + i).IsChecked == true) result |= 1 << i; return result;
    }
    private static void Capture(ControlWindow window, string name)
    {
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(file);
    }
    private static void Scene(UsageScene scene)
    {
        string path = Path.Combine(output, "state-" + scene + "-" + Guid.NewGuid().ToString("N"));
        using (var engine = new CoreEngine(path, true))
        {
            engine.Settings.Scene = scene; var window = new ControlWindow(engine, delegate { }, delegate { }, delegate { });
            try
            {
                window.Show(); window.Navigate("shutdown"); Pump();
                Check(!engine.ShutdownAt.HasValue && Find<StackPanel>(window, "ShutdownOnceFields").IsVisible, "Fresh page keeps original one-off entry");
                Click(window, "ShutdownModeRepeat"); Pump(); Check(Days(window) == 31 && !engine.ShutdownRecurringEnabled && !engine.ShutdownAt.HasValue, "Repeat opens Mon-Fri selection without scheduling");
                for (int i = 0; i < 7; i++) { var day = Find<CheckBox>(window, "ShutdownRepeatDay" + i); Check(day.ActualHeight >= (scene == UsageScene.Classroom ? 54 : 40), "Weekday whole-label touch target"); day.IsChecked = false; }
                Click(window, "ConfirmShutdown"); Check(!engine.ShutdownRecurringEnabled && !engine.ShutdownAt.HasValue, "Zero selected days refuses schedule");
                Click(window, "ShutdownRepeatDaily"); Check(Days(window) == 127 && !engine.ShutdownRecurringEnabled, "Every day is selection only"); Click(window, "ShutdownRepeatWorkdays"); Check(Days(window) == 31, "Weekday preset selects exactly Mon-Fri");
                for (int i = 0; i < 7; i++) Find<CheckBox>(window, "ShutdownRepeatDay" + i).IsChecked = i == 0 || i == 2 || i == 4;
                if (scene == UsageScene.Classroom) { Find<Slider>(window, "ShutdownRepeatHours").Value = 18; Find<Slider>(window, "ShutdownRepeatMinutes").Value = 34; Click(window, "ShutdownRepeatMinutesIncrease"); Check(Find<Slider>(window, "ShutdownRepeatMinutes").Value == 35, "Classroom minute adjustment is exact"); }
                else { var time = Find<TextBox>(window, "ShutdownRepeatTimeInput"); time.Text = "25:20"; Click(window, "ConfirmShutdown"); Check(!engine.ShutdownRecurringEnabled, "Invalid desktop clock refused"); time.Text = "18:35"; }
                Click(window, "ConfirmShutdown"); Pump(); Check(engine.ShutdownRecurringEnabled && engine.ShutdownRepeatDays == 21 && engine.ShutdownRepeatHour == 18 && engine.ShutdownRepeatMinute == 35 && engine.ShutdownAt.HasValue, "Explicit confirmation saves custom weekly plan");
                Check(Find<TextBlock>(window, "ShutdownPlanStatus").Text.StartsWith("下次 ") && Find<TextBlock>(window, "ShutdownPlanDetail").Text.Contains("一、三、五"), "Active plan shows next occurrence and selected weekdays");
                Capture(window, scene.ToString().ToLowerInvariant() + "-recurring-shutdown");
                var scroll = Find<ScrollViewer>(window, null); var fields = Find<StackPanel>(window, "ShutdownRepeatFields");
                Point fieldsPosition = fields.TranslatePoint(new Point(), scroll); scroll.ScrollToVerticalOffset(scroll.VerticalOffset + fieldsPosition.Y); Pump();
                Capture(window, scene.ToString().ToLowerInvariant() + "-recurring-fields");
                using (var restored = new CoreEngine(path, true)) Check(restored.ShutdownRecurringEnabled && restored.ShutdownRepeatDays == 21 && restored.ShutdownRepeatHour == 18 && restored.ShutdownRepeatMinute == 35, "Confirmed repeat restores after app restart");
                Click(window, "ShutdownModeOnce"); Check(engine.ShutdownRecurringEnabled, "Mode switch alone does not alter active plan");
                if (scene == UsageScene.Classroom) Click(window, "ShutdownPreset60");
                Click(window, "ConfirmShutdown"); Check(engine.ShutdownAt.HasValue && !engine.ShutdownRecurringEnabled, "Confirmed one-off explicitly replaces recurring plan");
                Click(window, "CancelShutdown"); Pump(); Check(!engine.ShutdownAt.HasValue && !engine.ShutdownRecurringEnabled, "Cancel removes one-off plan");
                Click(window, "ShutdownModeRepeat"); Click(window, "ShutdownRepeatDaily"); Click(window, "ConfirmShutdown"); Pump(); Click(window, "CancelShutdown"); Pump();
                using (var restored = new CoreEngine(path, true)) Check(!restored.ShutdownRecurringEnabled && !restored.ShutdownAt.HasValue, "Stop recurring disables all future occurrences persistently");
                if (scene == UsageScene.Classroom)
                {
                    window.Width = 900; window.Height = 590; Pump(); var confirm = Find<Button>(window, "ConfirmShutdown"); Point position = confirm.TranslatePoint(new Point(), window);
                    Check(position.Y >= 0 && position.Y + confirm.ActualHeight <= window.ActualHeight, "Confirmation reachable at minimum classroom size"); Capture(window, "classroom-recurring-minimum");
                }
            }
            finally { window.AllowClose = true; window.Close(); }
        }
    }
    [STAThread] private static int Main(string[] args)
    {
        output = args.Length > 0 ? args[0] : "artifacts/recurring-shutdown-ui"; Directory.CreateDirectory(output);
        try { new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; SurfaceStyle.SnapshotMode = true; Scene(UsageScene.Desktop); Scene(UsageScene.Classroom); File.WriteAllText(Path.Combine(output, "result.txt"), "PASS " + checks + " recurring shutdown UI assertions; safe mode only, no real shutdown or startup changes."); return 0; }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "result.txt"), error.ToString()); return 1; }
    }
}
