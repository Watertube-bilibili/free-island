using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FreeIsland
{
    public sealed class ControlWindow : Window
    {
        private readonly CoreEngine engine;
        private readonly Action previewIsland, restoreBall, openPresentation;
        private readonly bool classroom;
        private readonly DispatcherTimer refreshTimer;
        private readonly DispatcherTimer glassSaveTimer;
        private bool glassSettingsPending;
        private readonly ContentControl content;
        private readonly TextBlock pageTitle, pageCaption, clock, feedback;
        private readonly Dictionary<string, Button> navigation = new Dictionary<string, Button>();
        private readonly Dictionary<string, string> labels = new Dictionary<string, string> { { "home", "工作台" }, { "stopwatch", "正向计时" }, { "countdown", "倒计时" }, { "reminders", "日程提醒" }, { "shutdown", "定时关机" }, { "settings", "设置" } };
        private static readonly List<TimeSpan> laps = new List<TimeSpan>();
        private readonly Brush ink = B("#18243A"), muted = B("#58657A"), accent = B("#4F66E8"), line = B("#DCE2EB"), paper = B("#F6F7FB"), selected = B("#EEF1FF");
        private Action updatePage;
        private DateTime feedbackUntil;
        private string currentPage = "home";
        public bool AllowClose { get; set; }
        public string CurrentPage { get { return currentPage; } }
        private double BodySize { get { return classroom ? 20 : 14; } }
        private double SmallSize { get { return classroom ? 16 : 12; } }
        private double TargetHeight { get { return classroom ? 54 : 40; } }

        public ControlWindow(CoreEngine engine, Action previewIsland, Action restoreBall, Action openPresentation)
        {
            this.engine = engine; this.previewIsland = previewIsland; this.restoreBall = restoreBall; this.openPresentation = openPresentation;
            classroom = engine.Settings.Scene == UsageScene.Classroom;
            Title = "浮岛 · 控制中心";
            var work = SystemParameters.WorkArea;
            Width = Math.Min(classroom ? 1280 : 1000, work.Width - 24); Height = Math.Min(classroom ? 840 : 740, work.Height - 24);
            MinWidth = Math.Min(classroom ? 900 : 860, work.Width - 24); MinHeight = Math.Min(590, work.Height - 24);
            WindowStartupLocation = WindowStartupLocation.CenterScreen; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip;
            AllowsTransparency = true; Background = Brushes.Transparent; FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = BodySize; Foreground = ink; UseLayoutRounding = true;
            Resources.Add(typeof(ScrollBar), ScrollBarStyle());
            var shell = new Border { Background = paper, BorderBrush = line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), ClipToBounds = true }; Content = shell;
            var outer = new Grid(); outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); shell.Child = outer;
            var header = new Grid { Background = Brushes.White };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            outer.Children.Add(new Border { BorderBrush = line, BorderThickness = new Thickness(0, 0, 0, 1), Child = header, Padding = new Thickness(classroom ? 25 : 20, 12, 14, 12), Background = Brushes.White });
            var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, classroom ? 28 : 22, 0) };
            brand.Children.Add(AppVisual.Brand(classroom ? 37 : 31)); brand.Children.Add(T("浮岛", classroom ? 25 : 21, ink, FontWeights.SemiBold, new Thickness(10, 0, 0, 0))); header.Children.Add(brand); header.MouseLeftButtonDown += DragHeader;
            var scene = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var classScene = IconButton("classroom", "教室触屏", delegate { SelectScene(UsageScene.Classroom); });
            var deskScene = IconButton("desktop", "电脑桌面", delegate { SelectScene(UsageScene.Desktop); });
            classScene.Content = IconLabel("classroom", "教室触屏", ink, classroom ? 16 : 13); deskScene.Content = IconLabel("desktop", "电脑桌面", ink, classroom ? 16 : 13);
            classScene.MinHeight = deskScene.MinHeight = classroom ? 48 : 38; classScene.Padding = deskScene.Padding = new Thickness(13, 8, 13, 8); classScene.Margin = new Thickness(0, 0, 6, 0);
            classScene.Background = classroom ? selected : Brushes.White; deskScene.Background = classroom ? Brushes.White : selected;
            classScene.BorderBrush = classroom ? B("#BCC8FC") : line; deskScene.BorderBrush = classroom ? line : B("#BCC8FC");
            classScene.ToolTip = "大字与大触控目标，适合教室屏幕"; deskScene.ToolTip = "紧凑布局，适合键鼠操作";
            scene.Children.Add(classScene); scene.Children.Add(deskScene); Grid.SetColumn(scene, 1); header.Children.Add(scene);
            clock = T("", classroom ? 16 : 12, muted); clock.TextAlignment = TextAlignment.Right; clock.VerticalAlignment = VerticalAlignment.Center; clock.Margin = new Thickness(12, 0, 18, 0); Grid.SetColumn(clock, 2); header.Children.Add(clock);
            var chrome = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            chrome.Children.Add(WindowButton("minimize", "最小化", delegate { WindowState = WindowState.Minimized; })); chrome.Children.Add(WindowButton("maximize", "最大化或还原", delegate { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; })); chrome.Children.Add(WindowButton("close", "收起控制中心，任务继续运行", delegate { Hide(); })); Grid.SetColumn(chrome, 3); header.Children.Add(chrome);
            var body = new Grid(); Grid.SetRow(body, 1); outer.Children.Add(body);
            var workspace = new Grid { Margin = new Thickness(classroom ? 28 : 24, classroom ? 20 : 22, classroom ? 28 : 24, 12) };
            workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); workspace.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); workspace.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (classroom)
            {
                body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                var nav = new UniformGrid { Columns = 6, Margin = new Thickness(24, 12, 24, 0) }; foreach (var key in labels.Keys) AddNavigation(nav, key); body.Children.Add(nav); Grid.SetRow(workspace, 1);
            }
            else
            {
                body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var nav = new StackPanel(); foreach (var key in labels.Keys) AddNavigation(nav, key); nav.Children.Add(T("关闭窗口后\n任务仍会继续", 12, muted, FontWeights.Normal, new Thickness(12, 30, 0, 0)));
                body.Children.Add(new Border { Background = Brushes.White, BorderBrush = line, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(12, 22, 12, 18), Child = nav }); Grid.SetColumn(workspace, 1);
            }
            body.Children.Add(workspace);
            var heading = new Grid { Margin = new Thickness(0, 0, 0, classroom ? 18 : 20) }; heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var copy = new StackPanel { Margin = new Thickness(0, 0, 16, 0) }; pageTitle = T("工作台", classroom ? 28 : 25, ink, FontWeights.SemiBold); pageCaption = T("", SmallSize, muted, FontWeights.Normal, new Thickness(0, 5, 0, 0)); copy.Children.Add(pageTitle); copy.Children.Add(pageCaption); heading.Children.Add(copy);
            var presentation = IconButton("expand", "大屏展示", delegate { if (openPresentation != null) openPresentation(); }, classroom); presentation.VerticalAlignment = VerticalAlignment.Center; presentation.ToolTip = "全屏显示计时，方便全班查看"; Grid.SetColumn(presentation, 1); heading.Children.Add(presentation); workspace.Children.Add(heading);
            content = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch }; Grid.SetRow(content, 1); workspace.Children.Add(content);
            feedback = T("", SmallSize, accent, FontWeights.Medium, new Thickness(0, 8, 0, 0)); feedback.MinHeight = classroom ? 27 : 21; Grid.SetRow(feedback, 2); workspace.Children.Add(feedback);
            Closing += delegate(object sender, CancelEventArgs e) { if (!AllowClose) { e.Cancel = true; Hide(); } };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Hide(); e.Handled = true; } };
            refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            refreshTimer.Tick += delegate { if (!IsVisible) return; RefreshClock(); if (updatePage != null) updatePage(); if (DateTime.Now > feedbackUntil) feedback.Text = ""; };
            glassSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            glassSaveTimer.Tick += delegate { SaveGlassSettings(); };
            IsVisibleChanged += delegate { if (!IsVisible && !AllowClose) SaveGlassSettings(); };
            Closed += delegate { refreshTimer.Stop(); glassSaveTimer.Stop(); }; refreshTimer.Start(); Navigate("home");
        }

        public void Navigate(string page)
        {
            SaveGlassSettings();
            bool transition = content.Content != null && IsVisible && page != currentPage; currentPage = labels.ContainsKey(page) ? page : "home"; updatePage = null;
            foreach (var pair in navigation) { bool active = pair.Key == currentPage; pair.Value.Background = active ? selected : Brushes.Transparent; pair.Value.BorderBrush = active ? B("#CDD6FF") : Brushes.Transparent; pair.Value.Content = IconLabel(pair.Key, labels[pair.Key], active ? B("#384FC2") : muted, classroom ? 18 : 14); }
            if (currentPage == "stopwatch") BuildStopwatch(); else if (currentPage == "countdown") BuildCountdown(); else if (currentPage == "reminders") BuildReminders(); else if (currentPage == "shutdown") BuildShutdown(); else if (currentPage == "settings") BuildSettings(); else BuildHome();
            RefreshClock(); if (updatePage != null) updatePage();
            if (transition && !SurfaceStyle.SnapshotMode && SystemParameters.ClientAreaAnimation)
            {
                var shift = new TranslateTransform(0, 6); content.RenderTransform = shift; var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
                shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease }); content.BeginAnimation(OpacityProperty, new DoubleAnimation(.72, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            }
        }
        private void SelectScene(UsageScene scene) { if (engine.Settings.Scene == scene && engine.Settings.SceneSelected) return; engine.Settings.Scene = scene; engine.Settings.SceneSelected = true; engine.SaveSettings(); }
        private void RefreshClock() { clock.Text = DateTime.Now.ToString(classroom ? "M月d日  dddd\nHH:mm" : "M月d日  ddd\nHH:mm", CultureInfo.GetCultureInfo("zh-CN")); }
        private void Heading(string title, string caption) { pageTitle.Text = title; pageCaption.Text = caption; }
        private void DragHeader(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled || e.LeftButton != MouseButtonState.Pressed) return;
            DependencyObject node = e.OriginalSource as DependencyObject;
            while (node != null && node != sender) { if (node is ButtonBase) return; node = VisualTreeHelper.GetParent(node); }
            if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else if (WindowState == WindowState.Normal) DragMove(); e.Handled = true;
        }
        private void AddNavigation(Panel parent, string key)
        {
            var button = Btn("", delegate { Navigate(key); }); button.Content = IconLabel(key, labels[key], muted, classroom ? 18 : 14); button.Padding = new Thickness(classroom ? 10 : 12, 12, classroom ? 10 : 12, 12); button.MinHeight = classroom ? 56 : 48; button.Margin = classroom ? new Thickness(4, 0, 4, 0) : new Thickness(0, 0, 0, 7); button.HorizontalContentAlignment = classroom ? HorizontalAlignment.Center : HorizontalAlignment.Left; button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; System.Windows.Automation.AutomationProperties.SetName(button, labels[key]); navigation.Add(key, button); parent.Children.Add(button);
        }

        private void BuildHome()
        {
            Heading(classroom ? "课堂工作台" : "桌面工作台", classroom ? "设置课堂计时，或查看接下来的日程。" : "计时、提醒和关机预约，都在这里。"); var page = Page();
            var layout = new Grid(); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(classroom ? 305 : 232) }); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); page.Children.Add(layout);
            var task = new StackPanel(); layout.Children.Add(task); var timer = new StackPanel(); var titleRow = new Grid(); titleRow.Children.Add(T("倒计时", classroom ? 23 : 20, ink, FontWeights.SemiBold)); var custom = TextButton("自定义时长", delegate { Navigate("countdown"); }); custom.HorizontalAlignment = HorizontalAlignment.Right; titleRow.Children.Add(custom); timer.Children.Add(titleRow);
            int selectedMinutes = classroom ? 10 : 25; var digits = Numerals("10:00", classroom ? 112 : 79, new Thickness(0, classroom ? 18 : 12, 0, 0)); digits.HorizontalAlignment = HorizontalAlignment.Center; timer.Children.Add(digits); var state = T("选择时长后开始", SmallSize, muted); state.HorizontalAlignment = HorizontalAlignment.Center; timer.Children.Add(state);
            var choices = new UniformGrid { Columns = 4, Margin = new Thickness(-4, classroom ? 24 : 18, -4, 0) }; var buttons = new Dictionary<int, Button>();
            foreach (int number in new[] { 5, 10, 25, 45 })
            {
                int minutes = number; var button = Btn(minutes + " 分钟", delegate { selectedMinutes = minutes; foreach (var pair in buttons) { pair.Value.Background = pair.Key == minutes ? selected : Brushes.White; pair.Value.BorderBrush = pair.Key == minutes ? accent : line; } }); button.FontSize = classroom ? 19 : 14; button.Padding = new Thickness(7, 10, 7, 10); button.Margin = new Thickness(4, 0, 4, 0); button.Background = minutes == selectedMinutes ? selected : Brushes.White; button.BorderBrush = minutes == selectedMinutes ? accent : line; choices.Children.Add(button); buttons.Add(minutes, button);
            }
            timer.Children.Add(choices); var actions = new Grid { Margin = new Thickness(0, 14, 0, 0) }; actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var start = Btn("开始倒计时", delegate { if (engine.CountdownActive) engine.PauseResumeCountdown(); else engine.StartCountdown(TimeSpan.FromMinutes(selectedMinutes)); }, true); actions.Children.Add(start); var stop = Btn("结束", delegate { engine.CancelCountdown(); }); stop.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(stop, 1); actions.Children.Add(stop); timer.Children.Add(actions); task.Children.Add(Surface(timer, classroom ? 25 : 22));
            var quick = new WrapPanel { Margin = new Thickness(0, classroom ? 18 : 14, 0, 0) }; foreach (string key in new[] { "stopwatch", "reminders", "shutdown" }) { string target = key; var button = IconButton(key, labels[key], delegate { Navigate(target); }); button.Margin = new Thickness(0, 0, 10, 6); button.Padding = new Thickness(classroom ? 15 : 12, 10, classroom ? 15 : 12, 10); quick.Children.Add(button); } task.Children.Add(quick); task.Children.Add(T("收起控制中心后，计时与提醒仍会继续。", SmallSize, muted, FontWeights.Normal, new Thickness(0, 12, 0, 0)));
            var agenda = new StackPanel(); agenda.Children.Add(T("接下来的日程", classroom ? 22 : 18, ink, FontWeights.SemiBold)); var items = new StackPanel { Margin = new Thickness(0, classroom ? 21 : 16, 0, 14) }; agenda.Children.Add(items); agenda.Children.Add(IconButton("plus", "添加日程", delegate { Navigate("reminders"); }));
            var aside = new Border { Child = agenda, BorderBrush = line, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(classroom ? 25 : 20, 6, 0, 0), Margin = new Thickness(classroom ? 28 : 20, 0, 0, 0) }; Grid.SetColumn(aside, 1); layout.Children.Add(aside);
            layout.SizeChanged += delegate { bool wide = layout.ActualWidth >= (classroom ? 940 : 705); layout.ColumnDefinitions[1].Width = new GridLength(wide ? (classroom ? 305 : 232) : 0); Grid.SetColumn(aside, wide ? 1 : 0); Grid.SetRow(aside, wide ? 0 : 1); aside.Margin = wide ? new Thickness(classroom ? 28 : 20, 0, 0, 0) : new Thickness(0, 26, 0, 0); aside.Padding = wide ? new Thickness(classroom ? 25 : 20, 6, 0, 0) : new Thickness(0, 20, 0, 0); aside.BorderThickness = wide ? new Thickness(1, 0, 0, 0) : new Thickness(0, 1, 0, 0); };
            string signature = null; updatePage = delegate
            {
                digits.Text = engine.CountdownActive ? FormatTime(engine.CountdownRemaining) : selectedMinutes.ToString("00") + ":00"; state.Text = engine.CountdownActive ? (engine.CountdownRunning ? "计时进行中，到时自动提醒" : "计时已暂停") : "选择时长后开始"; start.Content = engine.CountdownActive ? (engine.CountdownRunning ? "暂停" : "继续") : "开始倒计时"; stop.Visibility = engine.CountdownActive ? Visibility.Visible : Visibility.Collapsed; choices.IsEnabled = !engine.CountdownActive;
                string next = ReminderSignature(); if (next == signature) return; signature = next; items.Children.Clear(); var upcoming = engine.Reminders.Where(r => !r.Completed).OrderBy(r => r.DueAt).Take(classroom ? 4 : 3).ToList();
                if (upcoming.Count == 0) { var icon = AppVisual.Icon("reminders", classroom ? 32 : 27, muted); icon.HorizontalAlignment = HorizontalAlignment.Left; items.Children.Add(icon); items.Children.Add(T("还没有日程提醒", BodySize, ink, FontWeights.Medium, new Thickness(0, 15, 0, 7))); items.Children.Add(T("添加课程、会议或休息提醒，到时自动弹出。", SmallSize, muted)); }
                foreach (var reminder in upcoming) { items.Children.Add(T(reminder.DueAt.ToString("MM/dd  HH:mm") + (reminder.Daily ? " · 每天" : ""), SmallSize, accent, FontWeights.Medium)); items.Children.Add(T(reminder.Title, BodySize, ink, FontWeights.Medium, new Thickness(0, 7, 0, 0))); items.Children.Add(Divider(classroom ? 18 : 13)); }
            };
        }

        private void BuildStopwatch()
        {
            if (laps.Count > 0 && laps[laps.Count - 1] > engine.StopwatchElapsed) laps.Clear();
            Heading("正向计时", classroom ? "开始、暂停或记录分段；需要时切换大屏展示。" : "记录总时长，也可以保存本次计时的分段。"); var page = Page(); var timing = new StackPanel(); var digits = Numerals("00:00:00", classroom ? 110 : 77, new Thickness(0, classroom ? 22 : 15, 0, 0)); digits.HorizontalAlignment = HorizontalAlignment.Center; timing.Children.Add(digits); var state = T("尚未开始", SmallSize, muted, FontWeights.Normal, new Thickness(0, 6, 0, classroom ? 29 : 22)); state.HorizontalAlignment = HorizontalAlignment.Center; timing.Children.Add(state);
            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center }; var start = Btn("开始计时", delegate { engine.ToggleStopwatch(); }, true); start.MinWidth = classroom ? 205 : 150; actions.Children.Add(start); actions.Children.Add(Btn("记录分段", delegate { if (engine.StopwatchElapsed > TimeSpan.Zero) { laps.Add(engine.StopwatchElapsed); Navigate("stopwatch"); } }, false, new Thickness(10, 0, 0, 0))); actions.Children.Add(Btn("重置", delegate { engine.ResetStopwatch(); laps.Clear(); Navigate("stopwatch"); }, false, new Thickness(10, 0, 0, 0))); timing.Children.Add(actions); page.Children.Add(Surface(timing, classroom ? 30 : 25)); page.Children.Add(SectionTitle("分段记录"));
            if (laps.Count == 0) page.Children.Add(EmptyState("暂无分段", "计时开始后点击「记录分段」，保存当前累计时间。"));
            else { var list = new StackPanel(); for (int i = laps.Count - 1; i >= 0; i--) { var row = new Grid { Margin = new Thickness(0, classroom ? 13 : 10, 0, classroom ? 13 : 10) }; row.Children.Add(T("分段 " + (i + 1), BodySize, muted)); var value = Numerals(FormatStopwatch(laps[i]), classroom ? 27 : 20, new Thickness(0)); value.HorizontalAlignment = HorizontalAlignment.Right; row.Children.Add(value); list.Children.Add(row); if (i > 0) list.Children.Add(new Border { Height = 1, Background = line }); } page.Children.Add(Surface(list, classroom ? 24 : 20)); }
            updatePage = delegate { digits.Text = FormatStopwatch(engine.StopwatchElapsed); start.Content = engine.StopwatchRunning ? "暂停计时" : engine.StopwatchElapsed > TimeSpan.Zero ? "继续计时" : "开始计时"; state.Text = engine.StopwatchRunning ? "正在计时" : engine.StopwatchElapsed > TimeSpan.Zero ? "已暂停，可随时继续" : "尚未开始"; };
        }

        private void BuildCountdown()
        {
            Heading("倒计时", "选择常用时长，或输入小时、分钟和秒。"); var page = Page(); var timing = new StackPanel(); var digits = Numerals("25:00", classroom ? 112 : 79, new Thickness(0, 3, 0, 0)); digits.HorizontalAlignment = HorizontalAlignment.Center; timing.Children.Add(digits); var state = T("选择时长后开始", SmallSize, muted, FontWeights.Normal, new Thickness(0, 4, 0, 0)); state.HorizontalAlignment = HorizontalAlignment.Center; timing.Children.Add(state);
            var running = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 0) }; var pause = Btn("暂停", delegate { engine.PauseResumeCountdown(); }, true); pause.MinWidth = classroom ? 190 : 130; running.Children.Add(pause); running.Children.Add(Btn("结束倒计时", delegate { engine.CancelCountdown(); }, false, new Thickness(10, 0, 0, 0))); timing.Children.Add(running); page.Children.Add(Surface(timing, classroom ? 24 : 21)); page.Children.Add(SectionTitle("常用时长"));
            var presets = new UniformGrid { Columns = 4, Margin = new Thickness(-5, 0, -5, 0) }; foreach (int number in new[] { 5, 10, 25, 45 }) { int minutes = number; presets.Children.Add(Btn(minutes + " 分钟", delegate { engine.StartCountdown(TimeSpan.FromMinutes(minutes)); Notify("已开始 " + minutes + " 分钟倒计时。"); }, false, new Thickness(5, 0, 5, 0))); } page.Children.Add(presets); page.Children.Add(SectionTitle("自定义时长"));
            var fields = new WrapPanel(); var hours = Input("0", classroom ? 105 : 76); var minutesInput = Input("25", classroom ? 105 : 76); var seconds = Input("0", classroom ? 105 : 76); fields.Children.Add(Field("小时", hours, 12)); fields.Children.Add(Field("分钟", minutesInput, 12)); fields.Children.Add(Field("秒", seconds, 20));
            var start = Btn("开始倒计时", delegate { int h, m, s; if (!int.TryParse(hours.Text, out h) || !int.TryParse(minutesInput.Text, out m) || !int.TryParse(seconds.Text, out s) || h < 0 || h > 168 || m < 0 || m > 59 || s < 0 || s > 59 || (h == 0 && m == 0 && s == 0) || (h == 168 && (m > 0 || s > 0))) { Notify("时长需大于零且不超过 7 天；分钟和秒请输入 0–59。", true); return; } engine.StartCountdown(TimeSpan.FromSeconds((long)h * 3600 + m * 60 + s)); Notify("倒计时已开始。"); }, true); start.Margin = new Thickness(0, classroom ? 30 : 24, 0, 0); fields.Children.Add(start); page.Children.Add(fields); page.Children.Add(T("到时会唤起灵动岛。关闭控制中心不会中断计时。", SmallSize, muted, FontWeights.Normal, new Thickness(0, 15, 0, 0)));
            updatePage = delegate { digits.Text = engine.CountdownActive ? FormatTime(engine.CountdownRemaining) : "25:00"; state.Text = engine.CountdownActive ? (engine.CountdownRunning ? "正在倒计时，到时自动提醒" : "已暂停") : "选择时长后开始"; running.Visibility = engine.CountdownActive ? Visibility.Visible : Visibility.Collapsed; pause.Content = engine.CountdownRunning ? "暂停" : "继续"; };
        }

        private void BuildReminders()
        {
            Heading("日程提醒", "设置一次提醒，或每天在同一时间重复。"); var page = Page(); var form = new StackPanel(); var title = Input("", double.NaN); title.MaxLength = 100; title.ToolTip = "例如：下课休息、参加会议"; form.Children.Add(Field("提醒内容", title));
            var fields = new WrapPanel { Margin = new Thickness(0, 17, 0, 0) }; DateTime nextHour = DateTime.Now.AddHours(1); var date = Input(nextHour.ToString("yyyy-MM-dd"), classroom ? 184 : 145); var time = Input(nextHour.ToString("HH:mm"), classroom ? 123 : 90); fields.Children.Add(Field("日期", date, 12)); fields.Children.Add(Field("时间", time, 17)); var daily = Check("每天重复", false); daily.Margin = new Thickness(0, classroom ? 30 : 24, 17, 0); fields.Children.Add(daily);
            var add = Btn("＋  添加提醒", delegate { DateTime due; if (string.IsNullOrWhiteSpace(title.Text)) { Notify("请填写提醒内容，再添加提醒。", true); title.Focus(); return; } if (!ParseDateTime(date.Text, time.Text, out due)) { Notify("日期请输入 2026-09-10，时间请输入 14:30 这样的格式。", true); return; } bool repeat = daily.IsChecked == true; if (repeat && due <= DateTime.Now) due = DateTime.Today.Add(due.TimeOfDay).AddDays(1); if (due <= DateTime.Now) { Notify("提醒时间已经过去，请选择之后的时间。", true); return; } engine.AddReminder(title.Text.Trim(), due, repeat); Navigate("reminders"); Notify("提醒已添加，到时会自动弹出。"); }, true); add.Content = IconLabel("plus", "添加提醒", Brushes.White, BodySize); System.Windows.Automation.AutomationProperties.SetName(add, "＋  添加提醒"); add.Margin = new Thickness(0, classroom ? 30 : 24, 0, 0); fields.Children.Add(add); form.Children.Add(fields); page.Children.Add(Surface(form, classroom ? 25 : 22));
            var listTitle = SectionTitle("已安排的日程"); page.Children.Add(listTitle); var list = new StackPanel(); page.Children.Add(list); string signature = null;
            updatePage = delegate
            {
                string next = ReminderSignature(); if (next == signature) return; signature = next; list.Children.Clear(); var items = engine.Reminders.OrderBy(r => r.Completed).ThenBy(r => r.DueAt).ToList(); listTitle.Text = "已安排的日程" + (items.Count > 0 ? "（" + items.Count + "）" : ""); if (items.Count == 0) { list.Children.Add(EmptyState("还没有日程提醒", "在上方填写内容和时间，点击「添加提醒」。")); return; }
                foreach (var reminder in items) { var item = reminder; var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(classroom ? 42 : 32) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); var icon = AppVisual.Icon(item.Completed ? "check" : "reminders", classroom ? 25 : 21, item.Completed ? muted : accent); icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new Thickness(0, 4, 0, 0); row.Children.Add(icon); var copy = new StackPanel { Margin = new Thickness(0, 0, 18, 0) }; copy.Children.Add(T(item.Title, BodySize, item.Completed ? muted : ink, FontWeights.Medium)); copy.Children.Add(T(item.DueAt.ToString("yyyy-MM-dd  HH:mm") + (item.Daily ? " · 每天" : "") + (item.Completed ? " · 已提醒" : " · 待提醒"), SmallSize, muted, FontWeights.Normal, new Thickness(0, 5, 0, 0))); Grid.SetColumn(copy, 1); row.Children.Add(copy); var remove = Btn("删除", delegate { engine.RemoveReminder(item.Id); Notify("提醒已删除。"); }); remove.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(remove, 2); row.Children.Add(remove); var surface = Surface(row, classroom ? 20 : 17); surface.Margin = new Thickness(0, 0, 0, 10); list.Children.Add(surface); }
            };
        }

        private void BuildShutdown()
        {
            Heading("定时关机", "明确预约时间；关机前会提醒，也可随时取消。"); var page = Page(); var status = new StackPanel(); var due = T("尚未预约关机", classroom ? 27 : 23, ink, FontWeights.SemiBold); status.Children.Add(due); var remaining = T("选择下方时间，确认后预约才会生效。", SmallSize, muted, FontWeights.Normal, new Thickness(0, 10, 0, 0)); status.Children.Add(remaining); var cancel = Btn("取消关机预约", delegate { engine.CancelShutdown(); Notify("已取消关机预约。"); }, false, new Thickness(0, 18, 0, 0)); cancel.HorizontalAlignment = HorizontalAlignment.Left; status.Children.Add(cancel); page.Children.Add(Surface(status, classroom ? 25 : 22)); page.Children.Add(SectionTitle("预约时间"));
            var date = Input(DateTime.Now.AddHours(1).ToString("yyyy-MM-dd"), classroom ? 200 : 150); var time = Input(DateTime.Now.AddHours(1).ToString("HH:mm"), classroom ? 135 : 95); var presets = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            foreach (int number in new[] { 30, 60, 120, 180 }) { int delay = number; presets.Children.Add(Btn(delay < 60 ? "30 分钟后" : (delay / 60) + " 小时后", delegate { var at = DateTime.Now.AddMinutes(delay); date.Text = at.ToString("yyyy-MM-dd"); time.Text = at.ToString("HH:mm"); }, false, new Thickness(0, 0, 10, 6))); } page.Children.Add(presets); var fields = new WrapPanel(); fields.Children.Add(Field("日期", date, 14)); fields.Children.Add(Field("时间", time)); page.Children.Add(fields);
            var warning = new StackPanel { Margin = new Thickness(0, 0, classroom ? 24 : 20, 0) }; warning.Children.Add(T("预约前请保存正在进行的工作。", BodySize, B("#80540A"), FontWeights.Medium)); warning.Children.Add(T("关机前 60 秒会提醒，可随时取消。不会强制关闭应用；未保存的工作可能阻止关机。", SmallSize, B("#80540A"), FontWeights.Normal, new Thickness(0, 7, 0, 0))); if (engine.IsSafeMode) warning.Children.Add(T("安全预览：只演示预约，不执行系统关机。", SmallSize, B("#80540A"), FontWeights.Medium, new Thickness(0, 8, 0, 0)));
            var confirm = Btn("确认预约关机", delegate { DateTime at; if (!ParseDateTime(date.Text, time.Text, out at)) { Notify("日期请输入 2026-09-10，时间请输入 23:30 这样的格式。", true); return; } if (at <= DateTime.Now.AddMinutes(1)) { Notify("请预约至少 1 分钟之后的关机时间。", true); return; } engine.ScheduleShutdown(at); Notify(engine.IsSafeMode ? "已创建安全预览预约，不会执行关机。" : "关机已预约，可随时取消。"); }, true); confirm.VerticalAlignment = VerticalAlignment.Center;
            var confirmation = new Grid(); confirmation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); confirmation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); confirmation.Children.Add(warning); Grid.SetColumn(confirm, 1); confirmation.Children.Add(confirm);
            ReserveActions(new Border { Child = confirmation, Background = B("#FFF5E4"), BorderBrush = B("#F0D6A4"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(classroom ? 20 : 17) });
            updatePage = delegate { due.Text = engine.ShutdownAt.HasValue ? engine.ShutdownAt.Value.ToString("MM月dd日  HH:mm") + " 关机" : "尚未预约关机"; remaining.Text = engine.ShutdownAt.HasValue ? "距离预约关机还有 " + FormatTime(engine.ShutdownAt.Value - DateTime.Now) : "选择下方时间，确认后预约才会生效。"; cancel.Visibility = engine.ShutdownAt.HasValue ? Visibility.Visible : Visibility.Collapsed; };
        }

        private void BuildSettings()
        {
            Heading("设置", "调整灵动岛的位置、大小和后台运行方式。"); var page = Page(); var placement = new StackPanel(); placement.Children.Add(T("灵动岛出现位置", classroom ? 23 : 19, ink, FontWeights.SemiBold)); placement.Children.Add(T("也可以直接拖动灵动岛或收起后的小黑点调整位置。", SmallSize, muted, FontWeights.Normal, new Thickness(0, 7, 0, classroom ? 14 : 18)));
            var positions = new UniformGrid { Columns = 3, Margin = new Thickness(-5, 0, -5, 0) }; IslandPlacement[] values = { IslandPlacement.Top, IslandPlacement.Left, IslandPlacement.Right }; string[] names = { "顶部", "左侧", "右侧" }; string[] icons = { "top", "left", "right" };
            for (int i = 0; i < values.Length; i++) { var value = values[i]; var button = IconButton(icons[i], names[i], delegate { engine.Settings.Placement = value; engine.SaveSettings(); Navigate("settings"); previewIsland(); }); button.Margin = new Thickness(5, 0, 5, 0); button.Background = value == engine.Settings.Placement ? selected : Brushes.White; button.BorderBrush = value == engine.Settings.Placement ? accent : line; positions.Children.Add(button); } placement.Children.Add(positions);
            placement.Children.Add(Divider(classroom ? 18 : 16));
            placement.Children.Add(T("液态玻璃", classroom ? 23 : 19, ink, FontWeights.SemiBold));
            var glassHint = T("", SmallSize, muted, FontWeights.Normal, new Thickness(0, 7, 0, 12));
            string[] glassDescriptions = { "纯色表面，清晰安静。玻璃参数暂不生效，重新开启后保留。", "清透表面与细水滴边缘，低占用；适合录屏、共享和低配置电脑。折射度仅在水滴模式生效。", "水滴：背景折射与柔和形变。早期 Win10 使用兼容采样；无法获取背景时保持清透材质。现代系统水滴模式可能不出现在录屏中；录屏请选择轻量。" };
            string[] glassNames = { "关闭", "轻量", "水滴" };
            string[] glassIds = { "GlassModeOff", "GlassModeLite", "GlassModeStandard" };
            var glassButtons = new Dictionary<int, Button>();
            var glassChoices = new UniformGrid { Columns = 3, Margin = new Thickness(-5, 0, -5, 0) };
            var refraction = GlassSlider("GlassRefractionSlider", "玻璃折射度", engine.Settings.GlassRefraction, 50);
            var transparency = GlassSlider("GlassTransparencySlider", "玻璃透明度", engine.Settings.GlassTransparency, 65);
            var highlight = GlassSlider("GlassHighlightSlider", "玻璃边缘高光", engine.Settings.GlassHighlight, 55);
            var glassControls = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            glassControls.Children.Add(GlassParameter("折射度", "背景弯曲的强弱，仅水滴模式生效。", refraction));
            glassControls.Children.Add(GlassParameter("透明度", "越高越清透；文字衬底保持清晰。", transparency));
            glassControls.Children.Add(GlassParameter("边缘高光", "调节水滴边缘的明亮程度。", highlight));
            bool resetGlassValues = false;
            Action previewGlass = delegate
            {
                if (resetGlassValues) return;
                engine.Settings.GlassRefraction = (int)Math.Round(refraction.Value);
                engine.Settings.GlassTransparency = (int)Math.Round(transparency.Value);
                engine.Settings.GlassHighlight = (int)Math.Round(highlight.Value);
                LiquidGlass.Configure(engine.Settings);
                glassSettingsPending = true; glassSaveTimer.Stop(); glassSaveTimer.Start();
            };
            refraction.ValueChanged += delegate { previewGlass(); };
            transparency.ValueChanged += delegate { previewGlass(); };
            highlight.ValueChanged += delegate { previewGlass(); };
            var glassActions = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            var previewGlassButton = Btn("预览玻璃", delegate { previewIsland(); }); previewGlassButton.Name = "PreviewGlass"; previewGlassButton.MinHeight = Math.Max(44, TargetHeight); glassActions.Children.Add(previewGlassButton);
            var resetGlass = Btn("恢复推荐参数", delegate
            {
                resetGlassValues = true;
                refraction.Value = 50; transparency.Value = 65; highlight.Value = 55;
                resetGlassValues = false; previewGlass(); SaveGlassSettings();
            }, false, new Thickness(10, 0, 0, 0));
            resetGlass.Name = "ResetGlassParameters"; resetGlass.MinHeight = Math.Max(44, TargetHeight); glassActions.Children.Add(resetGlass);
            glassControls.Children.Add(glassActions);
            glassControls.Children.Add(T("实时预览，停止调整后自动保存；悬浮球、灵动岛与小水滴一起生效。", SmallSize, muted, FontWeights.Normal, new Thickness(0, 9, 0, 0)));
            Action updateGlassChoice = delegate
            {
                foreach (var choice in glassButtons)
                {
                    bool active = choice.Key == engine.Settings.GlassMode;
                    choice.Value.Background = active ? selected : Brushes.White;
                    choice.Value.BorderBrush = active ? accent : line;
                    System.Windows.Automation.AutomationProperties.SetItemStatus(choice.Value, active ? "已选择" : "未选择");
                }
                glassHint.Text = glassDescriptions[Math.Max(0, Math.Min(2, engine.Settings.GlassMode))];
                refraction.IsEnabled = engine.Settings.GlassMode == 2;
                transparency.IsEnabled = highlight.IsEnabled = resetGlass.IsEnabled = engine.Settings.GlassMode != 0;
            };
            for (int i = 0; i < glassNames.Length; i++)
            {
                int mode = i;
                var button = Btn(glassNames[i], delegate { engine.Settings.GlassMode = mode; engine.SaveSettings(); updateGlassChoice(); Notify("已切换为" + glassNames[mode] + "玻璃外观。"); });
                button.Name = glassIds[i]; button.Margin = new Thickness(5, 0, 5, 0);
                System.Windows.Automation.AutomationProperties.SetName(button, (i == 2 ? "标准" : glassNames[i]) + "液态玻璃");
                button.ToolTip = glassDescriptions[i]; glassButtons.Add(i, button); glassChoices.Children.Add(button);
            }
            updateGlassChoice(); placement.Children.Add(glassHint); placement.Children.Add(glassChoices); placement.Children.Add(glassControls); placement.Children.Add(Divider(classroom ? 18 : 16));
            var dotSize = DotSlider(engine.Settings.IslandDotPercent);
            var dotReadout = T("", BodySize, ink, FontWeights.Medium, new Thickness(0, 1, 0, 0));
            System.Windows.Automation.AutomationProperties.SetName(dotReadout, "黑点大小预览");
            Action updateDotReadout = delegate { int percent = (int)Math.Round(dotSize.Value); dotReadout.Text = percent + "% · 约 " + (3 + (17 * percent + 50) / 100) + " 像素"; };
            dotSize.ValueChanged += delegate { updateDotReadout(); }; updateDotReadout();
            var dotControls = new StackPanel(); dotControls.Children.Add(dotSize); dotControls.Children.Add(dotReadout);
            var islandSize = Input(Math.Round(engine.Settings.IslandScale * 100).ToString(CultureInfo.InvariantCulture), classroom ? 88 : 76); islandSize.Name = "IslandScaleInput";
            var sizes = new WrapPanel();
            var dotField = Field("小黑点大小（0–100%）", dotControls, classroom ? 34 : 28); dotField.Margin = new Thickness(0, 0, classroom ? 34 : 28, 10); sizes.Children.Add(dotField);
            var islandField = Field("展开大小（75–150%）", SizeStepper(islandSize, 75, 150, 5, "展开大小")); islandField.Margin = new Thickness(0, 0, 0, 10); sizes.Children.Add(islandField); placement.Children.Add(sizes);
            placement.Children.Add(T("默认黑点 20%（约 6 像素），展开大小 100%。调整后点击应用。", SmallSize, muted, FontWeights.Normal, new Thickness(0, 1, 0, 12)));
            var sizeActions = new WrapPanel();
            var applySize = Btn("应用大小", delegate
            {
                int percent;
                if (!int.TryParse(islandSize.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out percent) || percent < 75 || percent > 150) { Notify("展开大小请输入 75 到 150 的整数。", true); islandSize.Focus(); return; }
                engine.Settings.IslandDotPercent = (int)Math.Round(dotSize.Value); engine.Settings.IslandScale = percent / 100.0; engine.SaveSettings(); Notify("灵动岛大小已保存。");
            }, true);
            applySize.Name = "ApplyIslandSize"; sizeActions.Children.Add(applySize);
            var resetSize = Btn("恢复默认大小", delegate { dotSize.Value = 20; islandSize.Text = "100"; engine.Settings.IslandDotPercent = 20; engine.Settings.IslandScale = 1; engine.SaveSettings(); Notify("已恢复黑点 20%（约 6 像素）和展开大小 100%。"); }, false, new Thickness(10, 0, 0, 0));
            resetSize.Name = "ResetIslandSize"; sizeActions.Children.Add(resetSize);
            placement.Children.Add(sizeActions); page.Children.Add(Surface(placement, classroom ? 20 : 21));
            var preferences = new StackPanel(); preferences.Children.Add(Setting("开机自启动", "登录 Windows 后静默启动，不弹出控制中心。", engine.Settings.AutoStart, delegate(bool value) { if (!engine.IsSafeMode) StartupRegistration.SetEnabled(value); engine.Settings.AutoStart = value; engine.SaveSettings(); })); preferences.Children.Add(Divider(classroom ? 14 : 16)); preferences.Children.Add(Setting("提醒声音", "倒计时结束、日程到时发出提示音。", engine.Settings.SoundEnabled, delegate(bool value) { engine.Settings.SoundEnabled = value; engine.SaveSettings(); })); preferences.Children.Add(Divider(classroom ? 14 : 16)); preferences.Children.Add(Setting("悬浮球靠边隐藏", "拖到屏幕边缘后收起，仅保留小箭头。", engine.Settings.EdgeHide, delegate(bool value) { engine.Settings.EdgeHide = value; engine.SaveSettings(); })); var settings = Surface(preferences, classroom ? 20 : 21); settings.Margin = new Thickness(0, classroom ? 16 : 20, 0, 0); page.Children.Add(settings);
            var actions = new WrapPanel(); actions.Children.Add(IconButton("home", "找回悬浮球", delegate { restoreBall(); Notify("悬浮球已回到可见位置。"); }, true)); var preview = IconButton("expand", "预览灵动岛", delegate { previewIsland(); }); preview.Margin = new Thickness(10, 0, 0, 0); actions.Children.Add(preview); ReserveActions(actions);
        }

        private void SaveGlassSettings()
        {
            if (!glassSettingsPending) return;
            glassSaveTimer.Stop();
            try { engine.SaveSettings(); glassSettingsPending = false; Notify("玻璃参数已保存。"); }
            catch (Exception ex) { Notify("玻璃参数未保存：" + ex.Message + "。请再次调整后重试。", true); }
        }

        private Slider GlassSlider(string name, string label, int value, int recommended)
        {
            var slider = DotSlider(value);
            slider.Name = name; slider.Width = double.NaN; slider.HorizontalAlignment = HorizontalAlignment.Stretch;
            slider.Height = Math.Max(44, TargetHeight);
            System.Windows.Automation.AutomationProperties.SetName(slider, label);
            System.Windows.Automation.AutomationProperties.SetHelpText(slider, "0 到 100%，推荐 " + recommended + "%。拖动或使用方向键微调；停止调整后自动保存。");
            return slider;
        }

        private FrameworkElement GlassParameter(string title, string description, Slider slider)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, classroom ? 13 : 10) };
            var heading = new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.Children.Add(T(title, BodySize, ink, FontWeights.Medium));
            var value = T("", BodySize, ink, FontWeights.SemiBold); value.MinWidth = 60; value.TextAlignment = TextAlignment.Right;
            Action refresh = delegate { value.Text = Math.Round(slider.Value).ToString(CultureInfo.InvariantCulture) + "%"; };
            slider.ValueChanged += delegate { refresh(); }; refresh(); Grid.SetColumn(value, 1); heading.Children.Add(value);
            row.Children.Add(heading); row.Children.Add(slider); row.Children.Add(T(description, SmallSize, muted)); return row;
        }

        private Slider DotSlider(int percent)
        {
            var slider = new Slider
            {
                Name = "IslandDotPercentSlider", Minimum = 0, Maximum = 100, Value = Math.Max(0, Math.Min(100, percent)),
                TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 10,
                IsMoveToPointEnabled = true, Width = classroom ? 340 : 280, Height = TargetHeight, Cursor = Cursors.Hand
            };
            System.Windows.Automation.AutomationProperties.SetName(slider, "黑点大小");
            System.Windows.Automation.AutomationProperties.SetHelpText(slider, "0 到 100%，默认 20%。可拖动或使用方向键微调，点击应用大小后保存。");
            const string markup = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Slider}'>
<Grid Background='Transparent'>
<Border x:Name='FocusRing' BorderBrush='Transparent' BorderThickness='2' CornerRadius='9' Margin='0,2'/>
<Border Height='5' CornerRadius='2.5' Background='#DCE2EB' Margin='15,0' VerticalAlignment='Center'/>
<Track x:Name='PART_Track' Margin='6,0' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}' Orientation='Horizontal'>
<Track.DecreaseRepeatButton><RepeatButton Command='{x:Static Slider.DecreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='#4F66E8' Height='5' CornerRadius='2.5' VerticalAlignment='Center'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
<Track.Thumb><Thumb Width='28' Height='28' Focusable='False'><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Grid><Ellipse Fill='White' Stroke='#4F66E8' StrokeThickness='2'/><Ellipse Width='8' Height='8' Fill='#4F66E8'/></Grid></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
<Track.IncreaseRepeatButton><RepeatButton Command='{x:Static Slider.IncreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
</Track>
</Grid>
<ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='FocusRing' Property='BorderBrush' Value='#263DAF'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.47'/></Trigger></ControlTemplate.Triggers>
</ControlTemplate>";
            slider.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(markup);
            Action<TouchEventArgs> touchValue = delegate(TouchEventArgs e)
            {
                double available = Math.Max(1, slider.ActualWidth - 40);
                slider.Value = Math.Round(Math.Max(0, Math.Min(100, (e.GetTouchPoint(slider).Position.X - 20) * 100 / available)));
            };
            slider.PreviewTouchDown += delegate(object sender, TouchEventArgs e) { slider.Focus(); slider.CaptureTouch(e.TouchDevice); touchValue(e); e.Handled = true; };
            slider.PreviewTouchMove += delegate(object sender, TouchEventArgs e) { if (e.TouchDevice.Captured == slider) { touchValue(e); e.Handled = true; } };
            slider.PreviewTouchUp += delegate(object sender, TouchEventArgs e) { if (e.TouchDevice.Captured == slider) { touchValue(e); slider.ReleaseTouchCapture(e.TouchDevice); e.Handled = true; } };
            return slider;
        }
        private FrameworkElement SizeStepper(TextBox input, int minimum, int maximum, int step, string name)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            Action<int> adjust = delegate(int delta) { int value; if (!int.TryParse(input.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) value = minimum; input.Text = Math.Max(minimum, Math.Min(maximum, (long)value + delta)).ToString(CultureInfo.InvariantCulture); };
            var decrease = Btn("−", delegate { adjust(-step); }); var increase = Btn("+", delegate { adjust(step); });
            decrease.Width = increase.Width = TargetHeight; decrease.Padding = increase.Padding = new Thickness(0);
            System.Windows.Automation.AutomationProperties.SetName(decrease, "减小" + name); System.Windows.Automation.AutomationProperties.SetName(increase, "增大" + name); System.Windows.Automation.AutomationProperties.SetName(input, name);
            input.TextAlignment = TextAlignment.Center; input.Margin = new Thickness(7, 0, 7, 0); row.Children.Add(decrease); row.Children.Add(input); row.Children.Add(increase); return row;
        }

        private FrameworkElement Setting(string title, string caption, bool initial, Action<bool> changed)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); var copy = new StackPanel { Margin = new Thickness(0, 0, 24, 0) }; copy.Children.Add(T(title, BodySize, ink, FontWeights.Medium)); copy.Children.Add(T(caption, SmallSize, muted, FontWeights.Normal, new Thickness(0, 5, 0, 0))); row.Children.Add(copy);
            var toggle = new CheckBox { IsChecked = initial, Width = classroom ? 76 : 62, MinHeight = TargetHeight, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, ToolTip = title, Focusable = true }; var template = new ControlTemplate(typeof(CheckBox));
            var hit = new FrameworkElementFactory(typeof(Border)); hit.Name = "Focus"; hit.SetValue(Border.BackgroundProperty, Brushes.Transparent); hit.SetValue(Border.BorderBrushProperty, Brushes.Transparent); hit.SetValue(Border.BorderThicknessProperty, new Thickness(2)); hit.SetValue(Border.CornerRadiusProperty, new CornerRadius(10)); hit.SetValue(Border.PaddingProperty, new Thickness(6));
            var track = new FrameworkElementFactory(typeof(Border)); track.Name = "Track"; track.SetValue(Border.WidthProperty, classroom ? 55.0 : 43.0); track.SetValue(Border.HeightProperty, classroom ? 31.0 : 25.0); track.SetValue(Border.CornerRadiusProperty, new CornerRadius(16)); track.SetValue(Border.BackgroundProperty, B("#AAB4C3")); track.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center);
            var thumb = new FrameworkElementFactory(typeof(Ellipse)); thumb.Name = "Thumb"; thumb.SetValue(Ellipse.WidthProperty, classroom ? 23.0 : 17.0); thumb.SetValue(Ellipse.HeightProperty, classroom ? 23.0 : 17.0); thumb.SetValue(Ellipse.FillProperty, Brushes.White); thumb.SetValue(Ellipse.HorizontalAlignmentProperty, HorizontalAlignment.Left); thumb.SetValue(Ellipse.MarginProperty, new Thickness(4, 0, 4, 0)); track.AppendChild(thumb); hit.AppendChild(track); template.VisualTree = hit;
            var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true }; on.Setters.Add(new Setter(Border.BackgroundProperty, accent, "Track")); on.Setters.Add(new Setter(Ellipse.HorizontalAlignmentProperty, HorizontalAlignment.Right, "Thumb")); template.Triggers.Add(on); var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty, accent, "Focus")); template.Triggers.Add(focus); toggle.Template = template;
            toggle.Click += delegate { bool value = toggle.IsChecked == true; try { changed(value); Notify("设置已保存。"); } catch (Exception ex) { toggle.IsChecked = !value; Notify("设置未保存：" + ex.Message, true); } }; System.Windows.Automation.AutomationProperties.SetName(toggle, title); Grid.SetColumn(toggle, 1); row.Children.Add(toggle); return row;
        }

        private StackPanel Page() { var panel = new StackPanel { Margin = new Thickness(0, 0, classroom ? 10 : 7, 4) }; content.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly, CanContentScroll = false }; return panel; }
        private void ReserveActions(UIElement actions)
        {
            var scrolling = content.Content as ScrollViewer;
            content.Content = null;
            var layout = new Grid(); layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (scrolling != null) layout.Children.Add(scrolling);
            var footer = new Border { Child = actions, Margin = new Thickness(0, 14, classroom ? 10 : 7, 4) }; Grid.SetRow(footer, 1); layout.Children.Add(footer); content.Content = layout;
        }
        private Border Surface(UIElement child, double padding) { return new Border { Child = child, Background = Brushes.White, BorderBrush = line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(padding) }; }
        private FrameworkElement EmptyState(string title, string caption) { var stack = new StackPanel { Margin = new Thickness(0, 7, 0, 7) }; stack.Children.Add(T(title, BodySize, ink, FontWeights.Medium)); stack.Children.Add(T(caption, SmallSize, muted, FontWeights.Normal, new Thickness(0, 8, 0, 0))); return stack; }
        private TextBlock SectionTitle(string title) { return T(title, classroom ? 22 : 18, ink, FontWeights.SemiBold, new Thickness(0, classroom ? 24 : 21, 0, classroom ? 14 : 12)); }
        private Border Divider(double gap) { return new Border { Height = 1, Background = line, Margin = new Thickness(0, gap, 0, gap) }; }

        private Button Btn(string label, Action action, bool primary = false, Thickness? margin = null)
        {
            var button = new Button { Content = label, Background = primary ? accent : Brushes.White, Foreground = primary ? Brushes.White : ink, BorderBrush = primary ? accent : line, BorderThickness = new Thickness(1), Padding = new Thickness(classroom ? 22 : 17, 10, classroom ? 22 : 17, 10), MinHeight = TargetHeight, FontSize = BodySize, FontWeight = FontWeights.Medium, Margin = margin ?? new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, Focusable = true };
            var template = new ControlTemplate(typeof(Button)); var border = new FrameworkElementFactory(typeof(Border)); border.Name = "Surface"; border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty)); border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty)); border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty)); border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty)); presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center); presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true); border.AppendChild(presenter); template.VisualTree = border;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Border.BackgroundProperty, primary ? B("#4258D4") : B("#EDF1F9"), "Surface")); template.Triggers.Add(hover); var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true }; pressed.Setters.Add(new Setter(Border.BackgroundProperty, primary ? B("#3448BA") : B("#DDE5F5"), "Surface")); template.Triggers.Add(pressed); var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty, primary ? B("#152D9E") : accent, "Surface")); focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Surface")); template.Triggers.Add(focus); var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .47, "Surface")); template.Triggers.Add(disabled); button.Template = template;
            button.Click += delegate { try { action(); } catch (Exception ex) { Notify("操作未完成：" + ex.Message, true); } }; if (!string.IsNullOrEmpty(label)) System.Windows.Automation.AutomationProperties.SetName(button, label); return button;
        }
        private Button IconButton(string icon, string label, Action action, bool primary = false) { var button = Btn("", action, primary); button.Content = IconLabel(icon, label, primary ? Brushes.White : ink, BodySize); System.Windows.Automation.AutomationProperties.SetName(button, label); return button; }
        private StackPanel IconLabel(string icon, string label, Brush brush, double size) { var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; var drawing = AppVisual.Icon(icon, classroom ? 23 : 19, brush); drawing.VerticalAlignment = VerticalAlignment.Center; drawing.Margin = new Thickness(0, 0, classroom ? 10 : 8, 0); panel.Children.Add(drawing); var text = T(label, size, brush, FontWeights.Medium); text.VerticalAlignment = VerticalAlignment.Center; text.TextWrapping = TextWrapping.NoWrap; panel.Children.Add(text); return panel; }
        private Button TextButton(string label, Action action) { var button = Btn(label, action); button.Foreground = accent; button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; button.FontSize = SmallSize; button.Padding = new Thickness(10, 0, 10, 0); button.MinHeight = classroom ? 48 : 32; return button; }
        private Button WindowButton(string icon, string label, Action action) { var button = Btn("", action); button.Content = AppVisual.Icon(icon, classroom ? 19 : 16, muted); button.Width = classroom ? 46 : 34; button.MinHeight = classroom ? 48 : 36; button.Padding = new Thickness(0); button.Margin = new Thickness(1, 0, 0, 0); button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; button.ToolTip = label; System.Windows.Automation.AutomationProperties.SetName(button, label); return button; }
        private TextBox Input(string value, double width)
        {
            var box = new TextBox { Text = value, Width = width, MinHeight = TargetHeight, Background = Brushes.White, Foreground = ink, Padding = new Thickness(classroom ? 14 : 11, 9, classroom ? 14 : 11, 9), FontSize = BodySize, CaretBrush = accent, SelectionBrush = B("#CDD6FF"), VerticalContentAlignment = VerticalAlignment.Center };
            var style = new Style(typeof(TextBox)); style.Setters.Add(new Setter(Control.BorderBrushProperty, B("#AAB6C8"))); style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1))); var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true }; focus.Setters.Add(new Setter(Control.BorderBrushProperty, accent)); focus.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2))); style.Triggers.Add(focus); box.Style = style; return box;
        }
        private StackPanel Field(string label, UIElement input, double right = 0) { var field = new StackPanel { Margin = new Thickness(0, 0, right, 0) }; field.Children.Add(T(label, SmallSize, muted, FontWeights.Medium, new Thickness(0, 0, 0, 7))); field.Children.Add(input); return field; }
        private CheckBox Check(string label, bool value)
        {
            var check = new CheckBox { Content = label, IsChecked = value, MinHeight = TargetHeight, FontSize = BodySize, Foreground = ink, VerticalContentAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand }; var template = new ControlTemplate(typeof(CheckBox));
            var border = new FrameworkElementFactory(typeof(Border)); border.Name = "Focus"; border.SetValue(Border.BackgroundProperty, Brushes.Transparent); border.SetValue(Border.BorderBrushProperty, Brushes.Transparent); border.SetValue(Border.BorderThicknessProperty, new Thickness(2)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8)); border.SetValue(Border.PaddingProperty, new Thickness(5));
            var panel = new FrameworkElementFactory(typeof(StackPanel)); panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal); panel.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);
            var square = new FrameworkElementFactory(typeof(Border)); square.Name = "Box"; square.SetValue(Border.WidthProperty, classroom ? 25.0 : 19.0); square.SetValue(Border.HeightProperty, classroom ? 25.0 : 19.0); square.SetValue(Border.CornerRadiusProperty, new CornerRadius(5)); square.SetValue(Border.BorderBrushProperty, B("#8492A8")); square.SetValue(Border.BorderThicknessProperty, new Thickness(1)); square.SetValue(Border.BackgroundProperty, Brushes.White); square.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center);
            var tick = new FrameworkElementFactory(typeof(Path)); tick.Name = "Tick"; tick.SetValue(Path.DataProperty, Geometry.Parse("M 2,7 L 6,11 L 13,3")); tick.SetValue(Path.StrokeProperty, Brushes.White); tick.SetValue(Path.StrokeThicknessProperty, 2.0); tick.SetValue(Path.WidthProperty, 15.0); tick.SetValue(Path.HeightProperty, 14.0); tick.SetValue(Path.HorizontalAlignmentProperty, HorizontalAlignment.Center); tick.SetValue(Path.VerticalAlignmentProperty, VerticalAlignment.Center); tick.SetValue(Path.VisibilityProperty, Visibility.Collapsed); square.AppendChild(tick); panel.AppendChild(square); var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(ContentPresenter.MarginProperty, new Thickness(9, 0, 0, 0)); presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center); panel.AppendChild(presenter); border.AppendChild(panel); template.VisualTree = border;
            var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true }; on.Setters.Add(new Setter(Border.BackgroundProperty, accent, "Box")); on.Setters.Add(new Setter(Border.BorderBrushProperty, accent, "Box")); on.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Tick")); template.Triggers.Add(on); var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty, accent, "Focus")); template.Triggers.Add(focus); check.Template = template; return check;
        }
        private TextBlock Numerals(string value, double size, Thickness margin) { var text = T(value, size, ink, FontWeights.SemiBold, margin); text.FontFamily = new FontFamily("Segoe UI"); text.LineHeight = size * 1.19; text.TextWrapping = TextWrapping.NoWrap; text.Typography.NumeralAlignment = FontNumeralAlignment.Tabular; return text; }
        private static TextBlock T(string value, double size, Brush brush, FontWeight? weight = null, Thickness? margin = null) { return new TextBlock { Text = value, FontSize = size, Foreground = brush, FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.45, Margin = margin ?? new Thickness(0) }; }
        private void Notify(string message, bool error = false) { feedback.Text = message; feedback.Foreground = error ? B("#B72B38") : accent; feedbackUntil = DateTime.Now.AddSeconds(error ? 12 : 6); }
        private string ReminderSignature() { return string.Join("|", engine.Reminders.Select(r => r.Id + r.Completed.ToString() + r.DueAt.Ticks.ToString() + r.Title).ToArray()); }
        private static bool ParseDateTime(string date, string time, out DateTime result) { return DateTime.TryParseExact(date.Trim() + " " + time.Trim(), new[] { "yyyy-MM-dd HH:mm", "yyyy-M-d H:mm", "yyyy-MM-dd HH:mm:ss", "yyyy/M/d H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out result); }
        private static string FormatStopwatch(TimeSpan time) { if (time < TimeSpan.Zero) time = TimeSpan.Zero; return ((int)time.TotalHours).ToString("00") + ":" + time.Minutes.ToString("00") + ":" + time.Seconds.ToString("00"); }
        private static string FormatTime(TimeSpan time) { long s = (long)Math.Ceiling(Math.Max(0, time.TotalSeconds)); return s >= 3600 ? (s / 3600).ToString("00") + ":" + ((s / 60) % 60).ToString("00") + ":" + (s % 60).ToString("00") : (s / 60).ToString("00") + ":" + (s % 60).ToString("00"); }
        private static Brush B(string hex) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex); brush.Freeze(); return brush; }
        private Style ScrollBarStyle()
        {
            const string markup = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'>
<Setter Property='Background' Value='#EEF1F6'/><Setter Property='Focusable' Value='False'/>
<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ScrollBar}'>
<Border Background='{TemplateBinding Background}' CornerRadius='5'>
<Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{TemplateBinding Value}' ViewportSize='{TemplateBinding ViewportSize}'>
<Track.DecreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
<Track.Thumb><Thumb MinHeight='30'><Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'><Border x:Name='Grip' Background='#A8B3C6' CornerRadius='5' Margin='2,0'/><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Grip' Property='Background' Value='#7788A2'/></Trigger><Trigger Property='IsDragging' Value='True'><Setter TargetName='Grip' Property='Background' Value='#4F66E8'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
<Track.IncreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='{x:Type RepeatButton}'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
</Track></Border></ControlTemplate></Setter.Value></Setter></Style>";
            var style = (Style)System.Windows.Markup.XamlReader.Parse(markup); style.Setters.Add(new Setter(FrameworkElement.WidthProperty, classroom ? 14.0 : 10.0)); return style;
        }
    }
}
