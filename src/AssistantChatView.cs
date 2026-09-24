using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FreeIsland
{
    internal sealed class AssistantChatView : Grid
    {
        private readonly AssistantController assistant;
        private readonly CoreEngine engine;
        private readonly IslandWindow island;
        private readonly Action<string> navigate;
        private readonly IList<LocalAiTurn> history;
        private readonly Func<string, IList<LocalAiTurn>, CancellationToken, Task<LocalAiReply>> respond;
        private readonly StackPanel messages = new StackPanel();
        private readonly ScrollViewer conversation;
        private readonly TextBox input;
        private readonly Button send;
        private readonly TextBlock state;
        private readonly AssistantFlow flow;
        private readonly List<Button> prompts = new List<Button>();
        private readonly List<Control> proposedControls = new List<Control>();
        private CancellationTokenSource pending;
        private int generation;
        private bool closed;
        private readonly double textSize;

        internal AssistantChatView(AssistantController assistant, CoreEngine engine, IslandWindow island, Action<string> navigate, IList<LocalAiTurn> history,
            Func<string, IList<LocalAiTurn>, CancellationToken, Task<LocalAiReply>> respond = null)
        {
            this.assistant = assistant; this.engine = engine; this.island = island; this.navigate = navigate; this.history = history;
            this.respond = respond ?? assistant.ChatAsync;
            textSize = engine.Settings.Scene == UsageScene.Classroom ? 16 : 14;
            Name = "IslandConversation";
            for (int i = 0; i < 6; i++) RowDefinitions.Add(new RowDefinition { Height = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

            var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 7) };
            var close = Button("收起", delegate { CancelRequest(); island.DismissAssistant(); island.Collapse(); });
            AutomationProperties.SetName(close, "收起岛上对话"); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
            flow = new AssistantFlow { Width = 38, Height = 28, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(flow, Dock.Left); header.Children.Add(flow);
            var title = Copy("问浮岛", 21, "#18243A"); title.FontWeight = FontWeights.SemiBold; header.Children.Add(title); Add(Readable(header), 0);

            string description = assistant.LastContext == null ? "先切换到应用，再从悬浮球呼出我。" : assistant.LastContext.Description;
            var context = Copy(description, 12, "#58657A"); context.Name = "ConversationContext"; context.MaxHeight = 40; context.TextTrimming = TextTrimming.CharacterEllipsis;
            context.ToolTip = description; context.Margin = new Thickness(0, 0, 0, 10); Add(Readable(context), 1);

            conversation = new ScrollViewer { Content = messages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly, Margin = new Thickness(0, 0, 0, 8) };
            AutomationProperties.SetName(conversation, "对话记录"); Add(conversation, 2);
            if (history.Count == 0)
            {
                Message("我能结合当前应用给你建议，也能帮你准备计时、音量和日程操作。", false);
                Message(assistant.Preferences.Enabled ? "想先做什么？" : "先在「模型设置」安装并启用模型，然后就能在这里连续对话。", false);
            }
            else foreach (var turn in history) Message(turn.Content, turn.Role == "user");

            var suggestions = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
            foreach (var text in new[] { "现在能做什么", "计时 5 分钟", "调节音量" })
            {
                string prompt = text;
                var button = Button(text, async delegate { input.Text = prompt; await SendAsync(); });
                button.FontSize = 12; button.Margin = new Thickness(0, 0, 6, 0); button.Padding = new Thickness(3, 0, 3, 0);
                suggestions.Children.Add(button); prompts.Add(button);
            }
            Add(suggestions, 3);

            var composer = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            composer.ColumnDefinitions.Add(new ColumnDefinition()); composer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            input = new TextBox { Name = "ConversationInput", FontSize = textSize, MaxLength = 600, MinHeight = 54, MaxHeight = 84, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(10, 8, 10, 8), Background = Brushes.White, Foreground = SurfaceStyle.Brush("#18243A"), BorderBrush = SurfaceStyle.Brush("#AAB6C8"), BorderThickness = new Thickness(1), CaretBrush = SurfaceStyle.Brush("#4F66E8"), SelectionBrush = SurfaceStyle.Brush("#CDD6FF") };
            AutomationProperties.SetName(input, "对浮岛说，最多600字"); input.ToolTip = "输入需求 · Enter 发送，Shift+Enter 换行";
            input.PreviewKeyDown += async delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; if (pending == null) await SendAsync(); } };
            composer.Children.Add(input);
            var placeholder = Copy("输入你的需求…", textSize, "#58657A"); placeholder.IsHitTestVisible = false; placeholder.VerticalAlignment = VerticalAlignment.Top; placeholder.Margin = new Thickness(11, 9, 11, 0);
            composer.Children.Add(placeholder); input.TextChanged += delegate { placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            send = SurfaceStyle.Button("发送", async delegate { if (pending != null) StopResponse(); else await SendAsync(); }, "#4F66E8", true);
            AutomationProperties.SetName(send, "发送或停止回答");
            send.Name = "ConversationSend"; send.Background = SurfaceStyle.Brush("#4F66E8"); send.Foreground = Brushes.White; send.Margin = new Thickness(8, 0, 0, 0); send.MinWidth = 64; send.MinHeight = 54;
            Grid.SetColumn(send, 1); composer.Children.Add(send); Add(composer, 4);

            var footer = new DockPanel();
            var settings = Button("模型设置", delegate { CancelRequest(); island.DismissAssistant(); island.Collapse(); navigate("assistant"); });
            settings.FontSize = 12; settings.Padding = new Thickness(1, 0, 1, 0); settings.MinHeight = 44; DockPanel.SetDock(settings, Dock.Right); footer.Children.Add(settings);
            var clear = Button("清空", delegate { if (pending != null) StopResponse(); history.Clear(); messages.Children.Clear(); Message("新对话。想先做什么？", false); });
            clear.FontSize = 12; clear.Padding = new Thickness(0); clear.MinHeight = 44; DockPanel.SetDock(clear, Dock.Right); footer.Children.Add(clear);
            state = Copy("本机对话 · 操作需确认", 11, "#58657A"); state.Name = "ConversationState"; footer.Children.Add(state); Add(Readable(footer), 5);
            Unloaded += delegate { CancelRequest(); };
            IsVisibleChanged += delegate { if (!IsVisible) CancelRequest(); };
            Loaded += delegate { island.ResizeAssistant(history.Count == 0 ? 408 : 548); };
        }

        private void Add(UIElement item, int row) { SetRow(item, row); Children.Add(item); }
        private static TextBlock Copy(string value, double size, string color) { var t = SurfaceStyle.Text(value, size, color); t.TextAlignment = TextAlignment.Left; t.TextWrapping = TextWrapping.Wrap; return t; }
        private static Border Readable(UIElement child)
        {
            var panel = new Border { Child = child, CornerRadius = new CornerRadius(10), Padding = new Thickness(5, 2, 5, 2) };
            panel.SetValue(LiquidGlass.ReadablePanelProperty, true); return panel;
        }
        private static Button Button(string label, Action action) { var b = SurfaceStyle.Button(label, action, "#EEF1FF"); b.MinHeight = 44; AutomationProperties.SetName(b, label); return b; }
        private void Message(string value, bool user)
        {
            var text = Copy(value ?? "", textSize, "#18243A"); text.Margin = new Thickness(0, 0, 0, 12);
            if (user)
            {
                text.Margin = new Thickness(0);
                var bubble = Readable(text); bubble.Background = SurfaceStyle.Brush("#E6EBFF"); bubble.Padding = new Thickness(10, 8, 10, 8); bubble.Margin = new Thickness(28, 3, 0, 14); bubble.HorizontalAlignment = HorizontalAlignment.Right; bubble.MaxWidth = 310; messages.Children.Add(bubble);
            }
            else { var reply = Readable(text); reply.Margin = new Thickness(0, 0, 12, 8); messages.Children.Add(reply); }
            while (messages.Children.Count > 32) messages.Children.RemoveAt(0);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(island.RefreshAssistantInk));
        }
        public void FocusInput() { Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { if (!closed) { input.Focus(); Keyboard.Focus(input); } })); }
        public void CancelRequest() { closed = true; generation++; if (pending != null) pending.Cancel(); flow.Busy = false; }
        private void StopResponse() { generation++; if (pending != null) pending.Cancel(); state.Text = "已停止 · 可重新发送"; flow.Busy = false; }
        internal async Task SendAsync()
        {
            string request = input.Text.Trim(); if (pending != null || closed || request.Length == 0) return;
            if (request.Length > 600) { state.Text = "请将问题缩短到 600 字以内"; return; }
            var cts = new CancellationTokenSource(); pending = cts; int current = ++generation;
            island.ResizeAssistant(548);
            foreach (var control in proposedControls) control.IsEnabled = false;
            proposedControls.Clear();
            foreach (var button in prompts) button.IsEnabled = false;
            send.Content = "停止"; input.IsReadOnly = true; flow.Busy = true; state.Text = "正在本机思考…";
            Message(request, true); conversation.ScrollToEnd();
            try
            {
                var reply = await respond(request, history.ToList(), cts.Token);
                if (closed || current != generation || cts.IsCancellationRequested) return;
                if (reply == null || String.IsNullOrWhiteSpace(reply.Text)) throw new InvalidOperationException("模型没有返回回答，请重试。");
                history.Add(new LocalAiTurn { Role = "user", Content = request }); history.Add(new LocalAiTurn { Role = "assistant", Content = reply.Text });
                while (history.Count > 8) history.RemoveAt(0);
                Message(reply.Text, false); AddActions(reply.Actions); input.Clear(); state.Text = "本机回答 · 操作需点击确认";
                conversation.ScrollToEnd();
            }
            catch (OperationCanceledException) { if (!closed) state.Text = "已停止 · 可重新发送"; }
            catch (Exception ex) { if (!closed && current == generation) { Message(ex.Message, false); state.Text = "未完成 · 可重试或打开模型设置"; conversation.ScrollToEnd(); } }
            finally
            {
                if (ReferenceEquals(pending, cts)) pending = null; cts.Dispose(); flow.Busy = false;
                send.Content = "发送"; input.IsReadOnly = false; foreach (var button in prompts) button.IsEnabled = true;
                if (!closed) FocusInput();
            }
        }
        private void AddActions(IList<LocalAiAction> actions)
        {
            if (actions == null) return;
            // Only the existing, bounded tool vocabulary can become controls.
            foreach (var action in actions.Take(3))
            {
                var proposal = action; if (proposal == null) continue;
                if (proposal.Kind == "volume")
                {
                    int level = 50; bool available = engine.IsSafeMode || SystemVolume.TryGet(out level);
                    var label = Copy(available ? "系统音量  " + level + "%" : "未找到音频设备", textSize, "#18243A"); messages.Children.Add(Readable(label));
                    var slider = new Slider { Name = "ConversationVolume", Minimum = 0, Maximum = 100, Value = level, IsEnabled = available, MinHeight = 44, IsMoveToPointEnabled = true };
                    ControlWindow.ApplyTouchSlider(slider);
                    AutomationProperties.SetName(slider, "对话中的系统音量");
                    slider.ValueChanged += delegate { label.Text = engine.IsSafeMode || SystemVolume.TrySet((int)slider.Value) ? "系统音量  " + (int)slider.Value + "%" : "音频设备已变化"; };
                    messages.Children.Add(slider); proposedControls.Add(slider); continue;
                }
                if (proposal.Kind != "countdown" && proposal.Kind != "media_toggle" && proposal.Kind != "open_timer" && proposal.Kind != "open_reminders") continue;
                if (proposal.Kind == "countdown" && (proposal.Seconds < 60 || proposal.Seconds > 14400)) continue;
                string labelText = proposal.Kind == "countdown" ? "确认开始 " + TimeSpan.FromSeconds(proposal.Seconds).ToString(@"h\:mm\:ss") + " 倒计时" : proposal.Kind == "media_toggle" ? "系统播放 / 暂停" : proposal.Kind == "open_timer" ? "打开倒计时" : "打开日程提醒";
                var button = Button(labelText, delegate
                {
                    if (proposal.Kind == "countdown")
                    {
                        if (engine.CountdownActive) { state.Text = "已有倒计时，请先结束当前任务"; return; }
                        engine.StartCountdown(TimeSpan.FromSeconds(proposal.Seconds)); state.Text = "倒计时已开始";
                    }
                    else if (proposal.Kind == "media_toggle") { if (!engine.IsSafeMode) SystemVolume.ToggleMedia(); state.Text = engine.IsSafeMode ? "演示：系统播放 / 暂停" : "已发送系统媒体键"; }
                    else { CancelRequest(); island.DismissAssistant(); island.Collapse(); navigate(proposal.Kind == "open_timer" ? "countdown" : "reminders"); }
                });
                button.Margin = new Thickness(0, 0, 0, 8); button.HorizontalAlignment = HorizontalAlignment.Stretch; messages.Children.Add(button); proposedControls.Add(button);
            }
        }
    }

    internal sealed class AssistantFlow : FrameworkElement
    {
        private bool busy, listening;
        private TimeSpan last;
        private double phase;
        internal bool Busy { get { return busy; } set { busy = value; Sync(); InvalidateVisual(); } }
        internal AssistantFlow() { Loaded += delegate { Sync(); }; Unloaded += delegate { Detach(); }; IsVisibleChanged += delegate { Sync(); }; }
        private void Sync()
        {
            bool animate = busy && IsLoaded && IsVisible && SystemParameters.ClientAreaAnimation && !SurfaceStyle.SnapshotMode;
            if (animate && !listening) { CompositionTarget.Rendering += Frame; listening = true; }
            if (!animate) { Detach(); phase = 0; }
        }
        private void Detach() { if (listening) CompositionTarget.Rendering -= Frame; listening = false; }
        private void Frame(object sender, EventArgs e)
        {
            var tick = e as RenderingEventArgs; if (tick == null || (tick.RenderingTime - last).TotalMilliseconds < 42) return;
            last = tick.RenderingTime; phase = tick.RenderingTime.TotalSeconds * 1.8; InvalidateVisual();
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var brush = new LinearGradientBrush(SurfaceStyle.Brush("#4F66E8").Color, SurfaceStyle.Brush("#9364CB").Color, 0); brush.Freeze();
            var pen = new Pen(brush, 2.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; pen.Freeze();
            var line = new StreamGeometry();
            using (var drawing = line.Open())
            {
                drawing.BeginFigure(new Point(2, ActualHeight / 2), false, false);
                for (int i = 1; i <= 36; i++) { double x = i / 36.0; double wave = Math.Sin(x * Math.PI * 3 + phase) * Math.Sin(x * Math.PI) * (busy ? 8 : 3); drawing.LineTo(new Point(2 + x * Math.Max(0, ActualWidth - 4), ActualHeight / 2 + wave), true, false); }
            }
            line.Freeze(); dc.DrawGeometry(null, pen, line);
        }
    }
}
