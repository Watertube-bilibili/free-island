using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FreeIsland;

// Isolated in-process UI actions. Safe-mode engine; no real shutdown or startup changes.
internal static class TouchTimeControlsTests
{
    private static int checks;
    private static string output;
    private static string stateRoot;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        var match = root as T; if (match != null && (name == null || match.Name == name)) return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { match = Find<T>(VisualTreeHelper.GetChild(root, i), name); if (match != null) return match; }
        return null;
    }
    private static int Count<T>(DependencyObject root) where T : DependencyObject
    {
        int count = root is T ? 1 : 0;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) count += Count<T>(VisualTreeHelper.GetChild(root, i));
        return count;
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Click(ControlWindow panel, string name)
    {
        var button = Find<Button>(panel, name); Check(button != null && button.IsEnabled, "Available " + name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); panel.UpdateLayout();
    }
    private static void Capture(ControlWindow panel, string name)
    {
        panel.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)panel.ActualWidth, (int)panel.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(panel);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(stream);
    }
    private static void Classroom()
    {
        using (var engine = new CoreEngine(Path.Combine(stateRoot, "classroom"), true))
        {
            engine.Settings.Scene = UsageScene.Classroom; var panel = new ControlWindow(engine, delegate { }, delegate { }, delegate { });
            try
            {
                panel.Show(); panel.Navigate("countdown"); Pump();
                var hours = Find<Slider>(panel, "CountdownHours"); var minutes = Find<Slider>(panel, "CountdownMinutes"); var seconds = Find<Slider>(panel, "CountdownSeconds");
                Check(hours != null && minutes != null && seconds != null && Count<TextBox>(panel) == 0, "Classroom duration needs no keyboard fields");
                foreach (var slider in new[] { hours, minutes, seconds }) { Check(slider.ActualHeight >= 54 && slider.ActualWidth >= 160, "Large slider target"); Check(slider.SmallChange == 1 && slider.IsSnapToTickEnabled, "Whole-unit adjustment"); }
                Click(panel, "CountdownPreset10"); Check(!engine.CountdownActive && minutes.Value == 10 && hours.Value == 0 && seconds.Value == 0, "Preset selects without starting");
                seconds.Value = 20; Click(panel, "CountdownSecondsIncrease"); Check(seconds.Value == 21, "Plus adjusts exactly one second"); Click(panel, "CountdownSecondsDecrease"); Check(seconds.Value == 20, "Minus adjusts exactly one second");
                Check(Find<TextBlock>(panel, "CountdownPreview").Text == "10:20", "Visible selection updates before start");
                var scroll = Find<ScrollViewer>(panel, null); scroll.ScrollToBottom(); Pump(); Capture(panel, "classroom-countdown-sliders");
                Click(panel, "StartCustomCountdown"); Check(engine.CountdownActive && engine.CountdownRemaining.TotalSeconds > 619 && engine.CountdownRemaining.TotalSeconds <= 620, "Start applies exact selected duration"); Pump(); Check(!hours.IsEnabled && !Find<Button>(panel, "StartCustomCountdown").IsEnabled, "Active countdown cannot be reset by accidental input");
                engine.CancelCountdown(); Pump(); hours.Value = 168; Check(minutes.Value == 0 && seconds.Value == 0 && !minutes.IsEnabled && !seconds.IsEnabled, "Seven-day boundary clears excess fields");
                hours.Value = 0; minutes.Value = 0; seconds.Value = 0; Click(panel, "StartCustomCountdown"); Check(!engine.CountdownActive, "Zero duration cannot start");
                panel.Navigate("reminders"); Pump(); Check(Count<TextBox>(panel) == 1, "Only reminder content needs text entry");
                Find<TextBox>(panel, "ReminderTitle").Text = "测试课程"; Click(panel, "ReminderTomorrow"); Find<Slider>(panel, "ReminderHours").Value = 8; Find<Slider>(panel, "ReminderMinutes").Value = 30;
                Click(panel, "ReminderMinutesIncrease"); Click(panel, "AddReminder"); Check(engine.Reminders.Count == 1 && engine.Reminders[0].DueAt == DateTime.Today.AddDays(1).AddHours(8).AddMinutes(31), "Date and sliders create exact reminder");
                Pump(); Capture(panel, "classroom-reminder-touch"); Click(panel, "ReminderDatePicker"); Pump();
                var calendar = Find<Calendar>(panel, "ReminderCalendar"); Check(calendar.Visibility == Visibility.Visible, "Calendar opens on touch button"); Check((double)calendar.CalendarDayButtonStyle.Setters.OfType<Setter>().First(s => s.Property == FrameworkElement.MinHeightProperty).Value >= 44, "Calendar days have touch hit height");
                calendar.SelectedDate = DateTime.Today.AddDays(3); Check(calendar.Visibility == Visibility.Collapsed && Find<TextBlock>(panel, "ReminderSelectedTime").Text.Contains(DateTime.Today.AddDays(3).ToString("M月d日")), "Calendar selection closes and updates selected date");
                panel.Navigate("shutdown"); Pump(); Check(Count<TextBox>(panel) == 0, "Shutdown scheduling requires no typing");
                Click(panel, "ShutdownPreset30"); Check(!engine.ShutdownAt.HasValue, "Shutdown preset only chooses time"); Click(panel, "ShutdownTomorrow"); Find<Slider>(panel, "ShutdownHours").Value = 18; Find<Slider>(panel, "ShutdownMinutes").Value = 45;
                Click(panel, "ConfirmShutdown"); Check(engine.ShutdownAt == DateTime.Today.AddDays(1).AddHours(18).AddMinutes(45), "Explicit confirmation schedules chosen date/time in safe mode"); engine.CancelShutdown();
                Capture(panel, "classroom-shutdown-touch");
                panel.Navigate("settings"); Pump(); var size = Find<Slider>(panel, "ActiveIslandSizeSlider"); var automatic = Find<CheckBox>(panel, "ActiveIslandSizeAuto");
                Check(size != null && size.Minimum == 30 && size.Maximum == 50 && size.Value == 48 && !size.IsEnabled, "Classroom automatic task-ball size"); automatic.IsChecked = false; size.Value = 42; Click(panel, "ApplyIslandSize"); Check(engine.Settings.ActiveIslandSize == 42, "Custom task-ball diameter saves"); Click(panel, "ResetIslandSize"); Check(engine.Settings.ActiveIslandSize == 0 && size.Value == 48 && !size.IsEnabled, "Default size restores automatic scene sizing");
                panel.Navigate("countdown"); panel.Width = 900; panel.Height = 590; Pump();
                var start = Find<Button>(panel, "StartCustomCountdown"); var position = start.TranslatePoint(new Point(), panel); Check(position.Y >= 0 && position.Y + start.ActualHeight <= panel.ActualHeight, "Start remains reachable at minimum classroom size"); Capture(panel, "classroom-minimum-countdown");
            }
            finally { panel.AllowClose = true; panel.Close(); }
        }
    }
    private static void Desktop()
    {
        using (var engine = new CoreEngine(Path.Combine(stateRoot, "desktop"), true))
        {
            engine.Settings.Scene = UsageScene.Desktop; var panel = new ControlWindow(engine, delegate { }, delegate { }, delegate { });
            try { panel.Show(); panel.Navigate("countdown"); Pump(); Check(Count<TextBox>(panel) == 3 && Find<Slider>(panel, "CountdownHours") == null, "Desktop duration text inputs preserved"); Capture(panel, "desktop-countdown"); panel.Navigate("shutdown"); Pump(); Check(Count<TextBox>(panel) == 2, "Desktop date and time inputs preserved"); panel.Navigate("settings"); Pump(); Check(Find<Slider>(panel, "ActiveIslandSizeSlider").Value == 36, "Desktop automatic task-ball size"); }
            finally { panel.AllowClose = true; panel.Close(); }
        }
    }
    [STAThread] private static int Main(string[] args)
    {
        output = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "artifacts", "touch-time-controls"); Directory.CreateDirectory(output); stateRoot = Path.Combine(output, "state-" + Guid.NewGuid().ToString("N"));
        try { new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; SurfaceStyle.SnapshotMode = true; Classroom(); Desktop(); File.WriteAllText(Path.Combine(output, "result.txt"), "PASS " + checks + " checks; safe-mode UI only."); return 0; }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "result.txt"), ex.ToString()); return 1; }
    }
}
