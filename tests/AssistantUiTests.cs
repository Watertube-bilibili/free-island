using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FreeIsland;

internal static class AssistantUiTests
{
    private static int checks;
    private static string output;
    private static void Check(bool pass, string message) { if (!pass) throw new Exception(message); checks++; }
    private static IEnumerable<T> All<T>(DependencyObject node) where T : DependencyObject
    {
        var match = node as T; if (match != null) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in All<T>(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Capture(Window window, string name)
    {
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(stream);
    }
    private static void Scene(UsageScene scene)
    {
        string path = Path.Combine(output, "state-" + scene + "-" + Guid.NewGuid().ToString("N"));
        using (var engine = new CoreEngine(path, true))
        using (var assistant = new AssistantController(path, true, engine, delegate { return true; }))
        using (var audio = new AlertAudioService(path, true))
        {
            Check(!assistant.Preferences.Enabled && !assistant.Preferences.RuleShortcutsEnabled, "AI and rules default off");
            assistant.Preferences.RuleShortcutsEnabled = true; assistant.Save();
            using (var restored = new AssistantController(path, true, engine, delegate { return true; })) Check(restored.Preferences.RuleShortcutsEnabled && !restored.Preferences.Enabled, "Explicit preference persists without enabling AI");
            engine.Settings.Scene = scene; engine.Settings.GlassMode = 1;
            var panel = new ControlWindow(engine, delegate { }, delegate { }, delegate { }, null, assistant, audio);
            var island = new IslandWindow(engine, delegate { }, delegate { });
            try
            {
                panel.Show(); panel.Navigate("assistant"); Pump();
                Check(All<RadioButton>(panel).Count() == 3, "Three genuine model options");
                var volume = All<Slider>(panel).Single(s => s.Name == "AlertVolume"); volume.Value = 37;
                Check(audio.VolumePercent == 37, "Audio volume is configurable");
                Check(All<Button>(panel).Count(b => (b.Content as string) == "选择音频") == 3, "Separate countdown reminder shutdown audio pickers");
                Check(All<Button>(panel).Where(b => (b.Content as string) == "选择音频").All(b => b.MinHeight >= (scene == UsageScene.Classroom ? 54 : 40)), "Audio buttons keep scene touch target");
                Capture(panel, scene.ToString().ToLowerInvariant() + "-assistant");
                All<ScrollViewer>(panel).First().ScrollToBottom(); Pump(); Capture(panel, scene.ToString().ToLowerInvariant() + "-audio");
                if (scene == UsageScene.Classroom) { panel.Width = 900; panel.Height = 590; All<ScrollViewer>(panel).First().ScrollToTop(); Pump(); Capture(panel, "classroom-assistant-minimum"); }
                panel.Hide();
                AssistantIsland.Present(island, engine, new AssistantSuggestion { ProcessName = "vlc", Source = "场景快捷操作", Actions = AssistantController.RuleActions("vlc") }, delegate { }); Pump();
                var slider = All<Slider>(island).Single(s => s.Name == "AssistantVolume");
                Check(slider.IsEnabled && slider.MinHeight >= 44, "Inline volume touch slider"); slider.Value = 73;
                Check(All<TextBlock>(island).Any(t => t.Text == "系统音量  73%"), "Safe inline volume updates display only");
                Capture(island, scene.ToString().ToLowerInvariant() + "-media-island");
                engine.StartCountdown(TimeSpan.FromMinutes(8));
                var actions = new List<LocalAiAction> { new LocalAiAction { Kind = "countdown", Title = "专注 · 25 分钟", Seconds = 1500 } };
                AssistantIsland.Present(island, engine, new AssistantSuggestion { ProcessName = "code", Source = "本地 AI 建议", Actions = actions }, delegate { }); Pump();
                Check(engine.CountdownRemaining.TotalMinutes <= 8 && engine.CountdownRemaining.TotalMinutes > 7, "Displaying model tool never starts or replaces timer");
                All<Button>(island).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "开始倒计时 0:25:00").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(engine.CountdownRemaining.TotalMinutes <= 8 && engine.CountdownRemaining.TotalMinutes > 7, "Existing countdown is protected on click");
                engine.CancelCountdown();
                All<Button>(island).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "开始倒计时 0:25:00").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(engine.CountdownActive && engine.CountdownRemaining.TotalMinutes > 24, "Explicit click starts parameterized AI countdown");
                island.ShowNotice(new IslandNoticeEventArgs { Kind = "reminder", Title = "上课提醒", Message = "正式提醒优先", Urgent = true }); Pump();
                Check(!All<Button>(island).Any(b => System.Windows.Automation.AutomationProperties.GetName(b) == "开始倒计时 0:25:00"), "Notice supersedes AI recommendation");
                Check(!engine.ShutdownAt.HasValue, "AI actions never arm shutdown");
            }
            finally { panel.AllowClose = true; panel.Close(); island.Close(); }
        }
    }
    [STAThread] private static int Main(string[] args)
    {
        output = args.Length > 0 ? args[0] : "artifacts/assistant-ui"; Directory.CreateDirectory(output);
        try
        {
            new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; SurfaceStyle.SnapshotMode = true;
            Check(AssistantController.RuleActions("unknown-player").Count == 0, "Unknown app does not produce invented rule suggestions");
            Check(AssistantController.RuleActions("vlc").First().Kind == "volume", "Media rule chooses volume");
            Scene(UsageScene.Desktop); Scene(UsageScene.Classroom);
            File.WriteAllText(Path.Combine(output, "result.txt"), "PASS " + checks + " assistant/UI checks. Safe mode; no audio, network, shutdown, media keys or system volume changes."); return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "result.txt"), error.ToString()); return 1; }
    }
}
