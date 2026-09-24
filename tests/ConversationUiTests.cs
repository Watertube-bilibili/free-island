using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FreeIsland;

internal static class ConversationUiTests
{
    private static int checks;
    private static int fixtureCalls;
    private static string output;
    private static void Check(bool pass, string message) { if (!pass) throw new Exception(message); ++checks; }
    private static IEnumerable<T> All<T>(DependencyObject node) where T : DependencyObject
    {
        var match = node as T; if (match != null) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var child in All<T>(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    private static T Find<T>(DependencyObject node, string name) where T : FrameworkElement { return All<T>(node).Single(item => item.Name == name); }
    private static void Pump(int milliseconds = 180)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Await(Task task)
    {
        DateTime limit = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < limit) Pump(30);
        Check(task.IsCompleted, "Synthetic UI response completes promptly"); task.GetAwaiter().GetResult(); Pump();
    }
    private static void Capture(Window window, string name)
    {
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(stream);
    }
    private static SolidColorBrush TriggerBackground(Button button, DependencyProperty property)
    {
        var trigger = button.Template.Triggers.OfType<Trigger>().Single(t => t.Property == property && Equals(t.Value, true));
        var setter = trigger.Setters.OfType<Setter>().Single(s => s.Property == Border.BackgroundProperty && s.TargetName == "Surface");
        return (SolidColorBrush)setter.Value;
    }
    private static double Linear(byte component) { double value = component / 255.0; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
    private static double WhiteContrast(Color background) { return 1.05 / (.2126 * Linear(background.R) + .7152 * Linear(background.G) + .0722 * Linear(background.B) + .05); }
    private static void CaptureSyntheticButtonState(Window window, Button button, SolidColorBrush color, string label, string name)
    {
        button.ApplyTemplate(); var surface = (Border)button.Template.FindName("Surface", button); Brush previous = surface.Background;
        try
        {
            // Apply the actual template's state brush for an explicit rendering fixture.
            // This is not a real pointer event and does not execute the button action.
            surface.SetCurrentValue(Border.BackgroundProperty, color); window.UpdateLayout();
            int width = (int)Math.Ceiling(window.ActualWidth), height = (int)Math.Ceiling(window.ActualHeight);
            var source = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); source.Render(window);
            var visual = new DrawingVisual();
            using (DrawingContext drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, 30));
                drawing.DrawText(new FormattedText("合成状态：" + label + " 模板颜色；未触发真实鼠标事件", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 12, Brushes.Black), new Point(8, 6));
                drawing.DrawImage(source, new Rect(0, 30, width, height));
            }
            var image = new RenderTargetBitmap(width, height + 30, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var stream = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(stream);
        }
        finally { surface.SetCurrentValue(Border.BackgroundProperty, previous); }
    }
    private static BackdropFrame SyntheticBackdrop(Rect rect)
    {
        ++fixtureCalls;
        int x = (int)Math.Floor(rect.X), y = (int)Math.Floor(rect.Y);
        int w = Math.Max(2, (int)Math.Ceiling(rect.Right) - x), h = Math.Max(2, (int)Math.Ceiling(rect.Bottom) - y);
        byte[] pixels = new byte[w * h * 4];
        for (int yy = 0, i = 0; yy < h; ++yy) for (int xx = 0; xx < w; ++xx, i += 4)
        {
            bool blue = (((xx + x) / 88 + (yy + y) / 68) & 1) == 0;
            bool line = (xx + x) % 88 < 5 || (yy + y) % 68 < 5;
            pixels[i] = (byte)(line ? 155 : blue ? 236 : 212);
            pixels[i + 1] = (byte)(line ? 126 : blue ? 201 : 224);
            pixels[i + 2] = (byte)(line ? 113 : blue ? 171 : 242); pixels[i + 3] = 255;
        }
        return new BackdropFrame { Pixels = pixels, Width = w, Height = h, Stride = w * 4, ScreenBounds = new Rect(x, y, w, h) };
    }
    private static void VerifyConversationGlass(CoreEngine engine, AssistantController assistant, IslandWindow island, string prefix)
    {
        var responder = new Responder(); var history = new List<LocalAiTurn>();
        var view = new AssistantChatView(assistant, engine, island, delegate { }, history, responder.Respond);
        island.ShowAssistant(view, 548, true); Pump();
        Find<TextBox>(view, "ConversationInput").Text = "为课堂练习准备五分钟，并给我一个音量滑块"; Await(view.SendAsync());
        var material = Find<LiquidGlassSurface>(island, "AssistantGlassMaterial");
        foreach (int mode in new[] { 0, 1, 2 })
        {
            engine.Settings.GlassMode = mode; LiquidGlass.Configure(engine.Settings); island.ApplyMaterial(); Pump(250);
            Check(material.Mode == mode, "Conversation follows Off/Lite/Water preference");
            Check(material.HasRefraction == (mode == 2), "Conversation refraction exists only in Water mode");
            Check(material.IsUpdating == (mode == 2), "Off and Lite conversation materials do not run sampling timer");
            if (mode != 2) { int prior = fixtureCalls; Pump(120); Check(prior == fixtureCalls, "Off/Lite conversation never samples backdrop"); }
        }
        Check(fixtureCalls > 0 && LiquidGlassSurface.PreviewBackdrop != null, "Water samples deterministic in-memory fixture only");
        var slider = Find<Slider>(view, "ConversationVolume"); slider.Value = 73; Pump();
        All<ScrollViewer>(view).Single(s => AutomationProperties.GetName(s) == "对话记录").ScrollToEnd(); Pump(); InWorkArea(island);
        Capture(island, prefix + "-conversation-water-fixture");
        island.Hide(); Pump(100);
        Check(!material.IsUpdating && !material.HasRefraction, "Hidden Water conversation releases timer and refraction buffer");
        int stoppedCalls = fixtureCalls; Pump(180); Check(fixtureCalls == stoppedCalls, "Hidden conversation stops backdrop sampling completely");
    }
    private static void Click(Button button) { Check(button != null && button.IsEnabled, "Clickable safe UI action"); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
    private static void InWorkArea(Window window)
    {
        Rect work = SystemParameters.WorkArea;
        Check(window.Left >= work.Left - 1 && window.Top >= work.Top - 1 && window.Left + window.ActualWidth <= work.Right + 1 && window.Top + window.ActualHeight <= work.Bottom + 1, "Conversation stays within monitor work area");
    }
    private sealed class Responder
    {
        public int Calls;
        public string Request;
        public IList<LocalAiTurn> Previous;
        public CancellationToken Token;
        public TaskCompletionSource<LocalAiReply> Pending;
        public LocalAiReply Reply = new LocalAiReply {
            Text = "可以。为课堂练习准备一个五分钟倒计时；你也可以直接拖动下方滑块调节系统音量。点击确认后才会开始计时。",
            Actions = new List<LocalAiAction> {
                new LocalAiAction { Kind = "countdown", Title = "课堂练习", Seconds = 300 },
                new LocalAiAction { Kind = "volume", Title = "音量" },
                new LocalAiAction { Kind = "media_toggle", Title = "播放" }
            }
        };
        public Task<LocalAiReply> Respond(string request, IList<LocalAiTurn> previous, CancellationToken token)
        {
            ++Calls; Request = request; Previous = previous; Token = token;
            return Pending == null ? Task.FromResult(Reply) : Pending.Task;
        }
    }
    private static void Scene(UsageScene scene)
    {
        string prefix = scene.ToString().ToLowerInvariant(); string directory = Path.Combine(output, "state-" + prefix + "-" + Guid.NewGuid().ToString("N"));
        using (var engine = new CoreEngine(directory, true))
        using (var assistant = new AssistantController(directory, true, engine, delegate { return true; }))
        using (var audio = new AlertAudioService(directory, true))
        {
            engine.Settings.Scene = scene; engine.Settings.GlassMode = 1;
            typeof(AssistantController).GetProperty("LastContext").GetSetMethod(true).Invoke(assistant, new object[] { AssistantContext.Create("PotPlayerMini64.exe", "", false, false) });
            var panel = new ControlWindow(engine, delegate { }, delegate { }, delegate { }, null, assistant, audio);
            string navigation = null; var island = new IslandWindow(engine, delegate(string page) { navigation = page; }, delegate { });
            var history = new List<LocalAiTurn>(); var responder = new Responder();
            var view = new AssistantChatView(assistant, engine, island, delegate(string page) { navigation = page; }, history, responder.Respond);
            try
            {
                panel.Show(); panel.Navigate("assistant"); Pump();
                Check(All<Button>(panel).Any(b => (b.Content as string) == "打开岛上对话"), "Settings exposes conversation entry");
                Check(!assistant.Preferences.IncludeWindowTitle, "Window-title access remains opt-in");
                Capture(panel, prefix + "-settings-first-screen");
                var modelPath = All<TextBox>(panel).Single(t => AutomationProperties.GetName(t) == "模型与运行组件存储目录");
                Check(modelPath.IsReadOnly && modelPath.Text == assistant.ModelDirectory, "Settings exposes current model storage directory");
                Check(All<Button>(panel).Any(b => (b.Content as string) == "选择目录") && All<Button>(panel).Any(b => (b.Content as string) == "恢复默认目录"), "Settings offers model directory selection and reset");
                string defaultModelDirectory = assistant.ModelDirectory, alternateModelDirectory = Path.Combine(directory, "isolated-model-choice");
                Await(assistant.SetModelDirectoryAsync(alternateModelDirectory));
                var updatePage = (Action)typeof(ControlWindow).GetField("updatePage", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel); updatePage(); Pump();
                Check(modelPath.Text == alternateModelDirectory && !assistant.Preferences.Enabled, "Directory change updates UI while model stays disabled");
                Click(All<Button>(panel).Single(b => (b.Content as string) == "恢复默认目录"));
                DateTime restoreDeadline = DateTime.UtcNow.AddSeconds(5);
                while (assistant.ModelDirectory != defaultModelDirectory && DateTime.UtcNow < restoreDeadline) Pump(30);
                updatePage(); Pump();
                var feedback = (TextBlock)typeof(ControlWindow).GetField("feedback", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(panel);
                Check(assistant.ModelDirectory == defaultModelDirectory && modelPath.Text == defaultModelDirectory, "Actual reset button restores default model directory");
                Check(feedback.Text.Contains("已恢复默认模型目录") && !feedback.Text.Contains("已启用"), "Directory reset reports restoration without claiming model was enabled");
                Check(!assistant.Preferences.Enabled && !assistant.Service.IsRunning, "Resetting model storage never enables or starts model");
                var settingsScroll = All<ScrollViewer>(panel).First(s => s.Content is StackPanel);
                settingsScroll.ScrollToVerticalOffset(Math.Max(0, modelPath.TranslatePoint(new Point(), (UIElement)settingsScroll.Content).Y - 42)); Pump();
                Capture(panel, prefix + "-model-directory-settings"); panel.Hide();
                island.ShowAssistant(view, 548, true); view.FocusInput(); Pump(); InWorkArea(island);
                var input = Find<TextBox>(view, "ConversationInput"); var send = Find<Button>(view, "ConversationSend");
                var sendTemplate = send.Template;
                var hoverBrush = TriggerBackground(send, UIElement.IsMouseOverProperty);
                var pressedBrush = TriggerBackground(send, System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty);
                Check(hoverBrush.Color == (Color)ColorConverter.ConvertFromString("#4258D4") && pressedBrush.Color == (Color)ColorConverter.ConvertFromString("#3448BA"), "Primary send hover and pressed use actual dark template colors");
                Check(((SolidColorBrush)send.Foreground).Color == Colors.White && WhiteContrast(hoverBrush.Color) >= 4.5 && WhiteContrast(pressedBrush.Color) >= 4.5, "White send text exceeds 4.5 contrast in hover and pressed states");
                var secondary = All<Button>(view).Single(b => (b.Content as string) == "调节音量");
                Check(TriggerBackground(secondary, UIElement.IsMouseOverProperty).Color == (Color)ColorConverter.ConvertFromString("#E2E9F8"), "Secondary conversation controls retain pale hover style");
                Check(input.IsKeyboardFocusWithin && island.ShowActivated, "Interactive conversation accepts keyboard focus");
                Check(input.MaxLength == 600 && input.AcceptsReturn, "Input bounds and multiline editing");
                Check(send.ActualHeight >= 44 && input.ActualHeight >= 44, "Input and send have usable touch targets");
                var scroll = All<ScrollViewer>(view).Single(s => AutomationProperties.GetName(s) == "对话记录");
                var isControl = typeof(IslandWindow).GetMethod("IsButtonSource", BindingFlags.NonPublic | BindingFlags.Static);
                Check(isControl != null && (bool)isControl.Invoke(null, new object[] { input }) && (bool)isControl.Invoke(null, new object[] { scroll }), "Text input and conversation scrolling bypass island drag interception");
                Check(!(bool)isControl.Invoke(null, new object[] { view }), "Blank conversation chrome can still initiate island drag");
                Capture(island, prefix + "-conversation-empty");
                if (scene == UsageScene.Classroom) CaptureSyntheticButtonState(island, send, hoverBrush, "发送 Hover", "classroom-send-hover-synthetic-state");

                input.Text = "帮我准备课堂练习，并给一个音量滑块";
                var enter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                input.RaiseEvent(enter); Pump();
                Check(enter.Handled && responder.Calls == 1 && responder.Request == "帮我准备课堂练习，并给一个音量滑块", "Enter sends exactly one synthetic request");
                Check(history.Count == 2 && history[0].Role == "user" && history[1].Role == "assistant" && responder.Previous.Count == 0, "First exchange stores both turns after success");
                Check(input.Text.Length == 0 && !input.IsReadOnly && (send.Content as string) == "发送", "Composer recovers after completed response");
                Check(!engine.CountdownActive && !engine.ShutdownAt.HasValue && navigation == null, "Presenting suggested tools never executes actions");
                var volume = Find<Slider>(view, "ConversationVolume"); Check(volume.IsEnabled && volume.MinHeight >= 44, "Safe volume action creates usable inline slider"); volume.Value = 73;
                Check(All<TextBlock>(view).Any(t => t.Text == "系统音量  73%"), "Safe volume interaction updates UI without touching system volume");
                Check((bool)isControl.Invoke(null, new object[] { volume }), "Volume slider bypasses island dragging");
                var media = All<Button>(view).Single(b => (b.Content as string) == "系统播放 / 暂停"); Click(media);
                Check(Find<TextBlock>(view, "ConversationState").Text == "演示：系统播放 / 暂停", "Safe media action is simulated");
                scroll.ScrollToEnd(); Pump(); InWorkArea(island); Capture(island, prefix + "-conversation-answer-actions");
                var countdown = All<Button>(view).Single(b => (b.Content as string) == "确认开始 0:05:00 倒计时");
                engine.StartCountdown(TimeSpan.FromMinutes(8)); Click(countdown);
                Check(engine.CountdownRemaining.TotalMinutes > 7 && engine.CountdownRemaining.TotalMinutes <= 8, "Existing countdown cannot be silently overwritten");
                engine.CancelCountdown(); Click(countdown);
                Check(engine.CountdownActive && engine.CountdownRemaining.TotalMinutes > 4 && engine.CountdownRemaining.TotalMinutes <= 5, "Explicit confirmation starts bounded countdown"); engine.CancelCountdown();

                responder.Reply = new LocalAiReply { Text = "不会执行未支持的操作。", Actions = new List<LocalAiAction> {
                    new LocalAiAction { Kind = "shutdown", Title = "关闭电脑" }, new LocalAiAction { Kind = "exec", Title = "运行命令" }, new LocalAiAction { Kind = "countdown", Title = "越界计时", Seconds = 1 }
                } };
                int actionCount = All<Button>(view).Count(); input.Text = "上一条说了什么？"; Await(view.SendAsync());
                Check(responder.Previous.Count == 2 && responder.Previous[0].Content == history[0].Content && history.Count == 4, "Follow-up receives prior exchange for continuous conversation");
                Check(All<Button>(view).Count() == actionCount && !engine.ShutdownAt.HasValue && !engine.CountdownActive, "Unknown and out-of-range actions create no executable controls");
                for (int i = 0; i < 4; i++) { input.Text = "继续" + i; Await(view.SendAsync()); }
                Check(history.Count == 8 && history[0].Role == "user" && history[7].Role == "assistant", "Conversation history stays bounded to four exchanges");

                int beforeCanceled = history.Count; responder.Pending = new TaskCompletionSource<LocalAiReply>(); input.Text = "等待后收起"; Task closing = view.SendAsync();
                Check(!closing.IsCompleted && input.IsReadOnly && (send.Content as string) == "停止", "Pending request exposes stop state");
                Check(ReferenceEquals(send.Template, sendTemplate) && ((SolidColorBrush)send.Foreground).Color == Colors.White && WhiteContrast(TriggerBackground(send, UIElement.IsMouseOverProperty).Color) >= 4.5 && WhiteContrast(TriggerBackground(send, System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty).Color) >= 4.5, "Stop state reuses accessible primary template and white foreground");
                if (scene == UsageScene.Classroom) CaptureSyntheticButtonState(island, send, pressedBrush, "停止 Pressed", "classroom-stop-pressed-synthetic-state");
                Click(All<Button>(view).Single(b => AutomationProperties.GetName(b) == "收起岛上对话"));
                Check(responder.Token.IsCancellationRequested, "Closing conversation cancels pending request");
                responder.Pending.SetResult(new LocalAiReply { Text = "迟到的回答", Actions = new List<LocalAiAction>() }); Await(closing);
                Check(history.Count == beforeCanceled && !All<TextBlock>(view).Any(t => t.Text == "迟到的回答"), "Late response cannot repopulate a closed conversation");

                var noticeHistory = new List<LocalAiTurn>(); responder.Pending = new TaskCompletionSource<LocalAiReply>();
                var replaced = new AssistantChatView(assistant, engine, island, delegate { }, noticeHistory, responder.Respond);
                island.ShowAssistant(replaced, 548, true); Pump(); Find<TextBox>(replaced, "ConversationInput").Text = "等待期间有提醒"; Task replacedTask = replaced.SendAsync();
                island.ShowNotice(new IslandNoticeEventArgs { Kind = "reminder", Title = "上课提醒", Message = "正式提醒优先", Urgent = true }); Pump();
                Check(responder.Token.IsCancellationRequested && !All<AssistantChatView>(island).Any(), "Official reminder replaces conversation and cancels inference");
                responder.Pending.SetResult(new LocalAiReply { Text = "不应显示", Actions = new List<LocalAiAction>() }); Await(replacedTask);
                Check(noticeHistory.Count == 0 && !engine.ShutdownAt.HasValue, "Canceled response stores no history and never schedules shutdown");
                Check(!assistant.Service.IsRunning && !assistant.Service.IsBusy, "All UI checks leave real local model stopped");
                VerifyConversationGlass(engine, assistant, island, prefix);
            }
            finally { view.CancelRequest(); island.Close(); panel.AllowClose = true; panel.Close(); }
        }
    }
    [STAThread] private static int Main(string[] args)
    {
        output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/conversation-ui-1.0.10"); Directory.CreateDirectory(output);
        try
        {
            new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; SurfaceStyle.SnapshotMode = true; LiquidGlassSurface.PreviewBackdrop = SyntheticBackdrop;
            Scene(UsageScene.Desktop); Scene(UsageScene.Classroom);
            string result = "PASS " + checks + " conversation UI assertions. Synthetic replies and safe mode only; no model/network/audio/media keys/system volume/shutdown effects.";
            File.WriteAllText(Path.Combine(output, "result.txt"), result); Console.WriteLine(result); return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output, "result.txt"), error.ToString()); Console.Error.WriteLine(error); return 1; }
        finally { LiquidGlassSurface.PreviewBackdrop = null; }
    }
}
