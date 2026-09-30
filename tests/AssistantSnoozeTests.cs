using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FreeIsland;

internal static class AssistantSnoozeTests
{
    private static int checks, fixtureCalls;
    private static string output, fixtures;
    private static readonly DateTime Start = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private static void Check(bool pass, string message) { if (!pass) throw new Exception(message); ++checks; }
    private static FieldInfo Field(string name) { return typeof(AssistantController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic); }
    private static IEnumerable<T> All<T>(DependencyObject node) where T : DependencyObject
    {
        T match = node as T; if (match != null) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); ++i)
            foreach (T child in All<T>(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    private static void Pump(int milliseconds = 180)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Capture(Window window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(stream);
    }
    private static BackdropFrame SyntheticBackdrop(Rect bounds)
    {
        ++fixtureCalls;
        int x = (int)Math.Floor(bounds.X), y = (int)Math.Floor(bounds.Y);
        int w = Math.Max(2, (int)Math.Ceiling(bounds.Right) - x), h = Math.Max(2, (int)Math.Ceiling(bounds.Bottom) - y);
        byte[] pixels = new byte[w * h * 4];
        for (int yy = 0, i = 0; yy < h; ++yy) for (int xx = 0; xx < w; ++xx, i += 4)
        {
            bool stripe = ((xx + x) / 80 + (yy + y) / 64) % 2 == 0;
            pixels[i] = (byte)(stripe ? 244 : 218); pixels[i + 1] = (byte)(stripe ? 221 : 234);
            pixels[i + 2] = (byte)(stripe ? 208 : 246); pixels[i + 3] = 255;
        }
        return new BackdropFrame { Pixels = pixels, Width = w, Height = h, Stride = w * 4, ScreenBounds = new Rect(x, y, w, h) };
    }
    private static void PersistenceAndCancellation()
    {
        string data = Path.Combine(fixtures, "persistence"), models = Path.Combine(fixtures, "models");
        using (var engine = new CoreEngine(data, true))
        {
            using (var assistant = new AssistantController(data, true, engine, delegate { return false; }))
            {
                Check(assistant.SuggestionsSnoozedUntilUtc == DateTime.MinValue, "Fresh settings have no pause");
                assistant.SetModelDirectoryAsync(models).GetAwaiter().GetResult();
                assistant.Preferences.Enabled = true; assistant.Preferences.RuleShortcutsEnabled = true;
                assistant.Preferences.IncludeWindowTitle = true; assistant.Save();
                string selected = assistant.Preferences.SelectedModelId; LocalAiService service = assistant.Service;
                int version = (int)Field("operationVersion").GetValue(assistant);
                using (var suggestion = new CancellationTokenSource())
                using (var chat = new CancellationTokenSource())
                {
                    Field("suggestionRequest").SetValue(assistant, suggestion); Field("chatRequest").SetValue(assistant, chat);
                    try
                    {
                        assistant.SnoozeSuggestionsForOneHour(Start);
                        Check(suggestion.IsCancellationRequested, "Pause cancels an in-flight automatic suggestion");
                        Check(!chat.IsCancellationRequested, "Pause preserves a manual conversation request");
                        Check((int)Field("operationVersion").GetValue(assistant) == version, "Pause does not invalidate manual conversation operation version");
                    }
                    finally { Field("suggestionRequest").SetValue(assistant, null); Field("chatRequest").SetValue(assistant, null); }
                }
                Check(assistant.SuggestionsSnoozedUntilUtc == Start.AddHours(1) && assistant.SuggestionsSnoozedUntilUtc.Kind == DateTimeKind.Utc, "Pause uses exact one-hour UTC deadline");
                Check(assistant.Preferences.Enabled && assistant.Preferences.RuleShortcutsEnabled && assistant.Preferences.IncludeWindowTitle, "Pause preserves enabled model, rules and title preferences");
                Check(assistant.ModelDirectory == models && assistant.Preferences.ModelDirectory == models && assistant.Preferences.SelectedModelId == selected && ReferenceEquals(service, assistant.Service), "Pause preserves model directory, selection and service");
                Check(!service.IsRunning && !service.IsBusy, "Safe test never starts or downloads a model");
            }
            using (var restored = new AssistantController(data, true, engine, delegate { return false; }))
            {
                Check(restored.SuggestionsSnoozedUntilUtc == Start.AddHours(1), "Pause deadline survives controller restart");
                Check(restored.Preferences.Enabled && restored.ModelDirectory == models, "Restart keeps model enabled preference and storage");
                var snapshot = AssistantContext.Create("vlc", "", false, false); var gate = new AssistantContextGate();
                Check(!gate.CanPresent(snapshot, Start.AddHours(1).AddTicks(-1), restored.SuggestionsSnoozedUntilUtc), "Pause blocks through final tick before expiry");
                Check(gate.CanPresent(snapshot, Start.AddHours(1), restored.SuggestionsSnoozedUntilUtc), "Pause expires exactly at saved UTC deadline");
                restored.SnoozeSuggestionsForOneHour(Start.AddMinutes(20));
                Check(restored.SuggestionsSnoozedUntilUtc == Start.AddMinutes(80), "Repeated pause uses one hour from latest gesture");
            }
        }
    }
    private static void LegacyAndRollback()
    {
        string data = Path.Combine(fixtures, "legacy");
        using (var engine = new CoreEngine(data, true))
        {
            string path = Path.Combine(data, "assistant-settings.json");
            File.WriteAllText(path, "{\"Enabled\":true,\"RuleShortcutsEnabled\":true}");
            using (var assistant = new AssistantController(data, true, engine, delegate { return false; }))
            {
                Check(assistant.Preferences.SuggestionsSnoozedUntilUtcTicks == 0 && assistant.SuggestionsSnoozedUntilUtc == DateTime.MinValue, "Legacy settings without pause field remain unsnoozed");
                foreach (long invalid in new[] { -1L, long.MaxValue })
                {
                    assistant.Preferences.SuggestionsSnoozedUntilUtcTicks = invalid;
                    Check(assistant.SuggestionsSnoozedUntilUtc == DateTime.MinValue, "Invalid saved pause ticks are ignored");
                }
                assistant.SnoozeSuggestionsForOneHour(Start);
                Directory.CreateDirectory(path + ".tmp");
                using (var suggestion = new CancellationTokenSource())
                using (var chat = new CancellationTokenSource())
                {
                    Field("suggestionRequest").SetValue(assistant, suggestion); Field("chatRequest").SetValue(assistant, chat);
                    try
                    {
                        bool failed = false;
                        try { assistant.SnoozeSuggestionsForOneHour(Start.AddMinutes(10)); }
                        catch (IOException) { failed = true; } catch (UnauthorizedAccessException) { failed = true; }
                        Check(failed, "Settings failure is reported to caller");
                        Check(assistant.SuggestionsSnoozedUntilUtc == Start.AddHours(1), "Settings failure restores previous in-memory deadline");
                        Check(!suggestion.IsCancellationRequested && !chat.IsCancellationRequested, "Uncommitted pause cancels no requests");
                        Check(assistant.Preferences.Enabled && assistant.Preferences.RuleShortcutsEnabled, "Save failure preserves model and rule preferences");
                    }
                    finally { Field("suggestionRequest").SetValue(assistant, null); Field("chatRequest").SetValue(assistant, null); }
                }
                using (var restored = new AssistantController(data, true, engine, delegate { return false; }))
                    Check(restored.SuggestionsSnoozedUntilUtc == Start.AddHours(1), "Failed save preserves previously persisted deadline");
            }
        }
    }
    private static void RealNoticeSeparation()
    {
        DateTime now = Start; var kinds = new List<string>();
        using (var engine = new CoreEngine(Path.Combine(fixtures, "notices"), true, delegate { return now; }, delegate { throw new Exception("No actual shutdown permitted"); }))
        using (var assistant = new AssistantController(Path.Combine(fixtures, "notices"), true, engine, delegate { return false; }))
        {
            engine.Notice += delegate(object sender, IslandNoticeEventArgs e) { kinds.Add(e.Kind); };
            engine.StartCountdown(TimeSpan.FromMinutes(1)); engine.AddReminder("离线提醒", now.AddMinutes(1).ToLocalTime(), false);
            engine.ScheduleShutdown(now.AddMinutes(2).ToLocalTime()); assistant.SnoozeSuggestionsForOneHour(now);
            now = Start.AddMinutes(1); engine.Tick();
            Check(kinds.Contains("countdown") && kinds.Contains("reminder"), "Countdown and reminder notices still fire while automatic suggestions are paused");
            now = Start.AddSeconds(110); engine.Tick();
            Check(kinds.Contains("shutdown") && engine.ShutdownRemaining.HasValue, "Scheduled shutdown warning still fires during pause");
            engine.CancelShutdown();
        }
    }
    private static Button PauseButton(IslandWindow island) { return All<Button>(island).Single(b => b.Name == "SnoozeAssistantSuggestions"); }
    private static void Fits(Button button, IslandWindow island)
    {
        Check(button.IsEnabled && button.IsVisible && button.IsHitTestVisible && button.ActualHeight >= 44, "Pause control is visible, enabled and at least 44 DIP high");
        Rect bounds = button.TransformToAncestor(island).TransformBounds(new Rect(button.RenderSize));
        Check(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= island.ActualWidth + .5 && bounds.Bottom <= island.ActualHeight + .5, "Pause control fits within island bounds");
        var layout = new List<string>();
        for (DependencyObject parent = VisualTreeHelper.GetParent(button); parent != null && parent != island; parent = VisualTreeHelper.GetParent(parent))
        {
            var element = parent as FrameworkElement;
            if (element == null) continue;
            Rect relative = button.TransformToAncestor(element).TransformBounds(new Rect(button.RenderSize));
            Geometry clip = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(element);
            layout.Add(element.GetType().Name + " " + element.Name + " actual=" + element.RenderSize + " button=" + relative + " layoutClip=" + (clip == null ? "none" : clip.Bounds.ToString()));
            File.WriteAllLines(Path.Combine(output, "pause-layout.txt"), layout);
            if (element.ClipToBounds)
                Check(relative.Left >= -.5 && relative.Top >= -.5 && relative.Right <= element.ActualWidth + .5 && relative.Bottom <= element.ActualHeight + .5, "Pause control fits every clipping ancestor");
            if (clip != null)
            {
                Rect clipped = clip.Bounds; clipped.Inflate(.5, .5);
                Check(clipped.Contains(relative), "Pause control fits implicit WPF layout clipping on " + element.GetType().Name);
            }
        }
        var label = All<TextBlock>(button).Single(t => t.Text == "1 小时内不再建议");
        Check(label.ActualWidth + .5 >= label.DesiredSize.Width && label.ActualHeight + .5 >= label.DesiredSize.Height, "Pause label is fully measured without clipping");
        Check(AutomationProperties.GetName(button) == "1 小时内不再建议", "Pause control has explicit accessible name");
    }
    private static void Scene(UsageScene scene, int glassMode)
    {
        string prefix = scene.ToString().ToLowerInvariant(), data = Path.Combine(fixtures, prefix);
        using (var engine = new CoreEngine(data, true))
        using (var assistant = new AssistantController(data, true, engine, delegate { return false; }))
        {
            engine.Settings.Scene = scene; engine.Settings.GlassMode = glassMode; LiquidGlass.Configure(engine.Settings);
            var island = new IslandWindow(engine, delegate { }, delegate { });
            var suggestion = new AssistantSuggestion { ProcessName = "vlc", Source = "本地 AI 建议", ContextDescription = "播放器 · 本地场景建议", Actions = AssistantController.RuleActions("vlc") };
            try
            {
                int callbacks = 0;
                AssistantIsland.Present(island, engine, suggestion, delegate { }, delegate { ++callbacks; assistant.SnoozeSuggestionsForOneHour(Start); }); Pump(300);
                Button pause = PauseButton(island); Fits(pause, island);
                Check(All<LiquidGlassSurface>(island).Any(s => s.Name == "AssistantGlassMaterial" && s.Mode == glassMode), "Suggestion uses requested Lite or Water appearance");
                Capture(island, prefix + (glassMode == 2 ? "-water" : "-lite") + "-suggestion");
                pause.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                Check(callbacks == 1 && assistant.SuggestionsSnoozedUntilUtc == Start.AddHours(1), "Actual pause button invokes persistence once");
                Check(!island.IsVisible && !All<Button>(island).Any(b => b.Name == "SnoozeAssistantSuggestions"), "Successful pause removes suggestion and collapses island");
                Check(!engine.CountdownActive && !engine.ShutdownAt.HasValue && !assistant.Service.IsRunning, "Pause gesture does not start timer, shutdown or model");
                Directory.CreateDirectory(Path.Combine(data, "assistant-settings.json.tmp"));
                AssistantIsland.Present(island, engine, suggestion, delegate { }, delegate { assistant.SnoozeSuggestionsForOneHour(Start.AddMinutes(10)); }); Pump();
                PauseButton(island).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                Check(island.IsVisible && PauseButton(island).IsEnabled, "Failed save keeps suggestion open and retry enabled");
                Check(All<TextBlock>(island).Any(t => t.IsVisible && t.Text == "暂停未保存，请重试。"), "Failed save shows clear inline status");
                Check(assistant.SuggestionsSnoozedUntilUtc == Start.AddHours(1), "UI save failure preserves previous pause deadline");
                island.ShowNotice(new IslandNoticeEventArgs { Kind = "reminder", Title = "日程提醒", Message = "真实提醒照常显示", Urgent = true }); Pump();
                Check(island.IsVisible && !All<Button>(island).Any(b => b.Name == "SnoozeAssistantSuggestions") && All<TextBlock>(island).Any(t => t.Text == "真实提醒照常显示"), "Real reminder still replaces suggestion while paused");
            }
            finally { island.Close(); }
        }
    }
    [STAThread] private static int Main(string[] args)
    {
        output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/assistant-snooze-1.0.11"); Directory.CreateDirectory(output);
        fixtures = Path.Combine(output, "fixtures-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixtures);
        try
        {
            new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; SurfaceStyle.SnapshotMode = true; LiquidGlassSurface.PreviewBackdrop = SyntheticBackdrop;
            PersistenceAndCancellation(); LegacyAndRollback(); RealNoticeSeparation(); Scene(UsageScene.Desktop, 1); Scene(UsageScene.Classroom, 2);
            Check(fixtureCalls > 0, "Water screenshot uses only deterministic synthetic backdrop");
            string result = "PASS " + checks + " assistant snooze assertions. Safe offline fixtures and synthetic backdrop only; no models, network, desktop capture, audio, media keys, volume or shutdown effects.";
            File.WriteAllText(Path.Combine(output, "result.txt"), result); Console.WriteLine(result); return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "result.txt"), error.ToString()); Console.Error.WriteLine(error); return 1; }
        finally { LiquidGlassSurface.PreviewBackdrop = null; }
    }
}
