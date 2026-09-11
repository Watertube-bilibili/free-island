using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FreeIsland
{
    /// <summary>A touch-friendly classroom stage. The shared application owns the engine clock.</summary>
    public sealed class PresentationWindow : Window
    {
        private static readonly Brush Paper = ColorBrush("#F6F7FB");
        private static readonly Brush Ink = ColorBrush("#16213A");
        private static readonly Brush Muted = ColorBrush("#58647D");
        private static readonly Brush Accent = ColorBrush("#4F66E8");
        private static readonly Brush FinishedInk = ColorBrush("#276956");
        private static readonly FontFamily NumberTypeface = new FontFamily("Segoe UI");
        private static readonly FontFamily TextTypeface = new FontFamily("Microsoft YaHei UI");

        private readonly CoreEngine engine;
        private readonly DispatcherTimer uiTimer;
        private readonly Grid layout;
        private readonly Grid stage;
        private readonly TextBlock digits;
        private readonly TextBlock stateText;
        private readonly TextBlock contextText;
        private readonly TextBlock keyboardHint;
        private readonly Button primaryButton;
        private readonly Button resetButton;
        private readonly StackPanel presets;
        private readonly Border progressTrack;
        private readonly Border progressFill;
        private readonly WrapPanel controls;
        private bool completed;
        private bool ownCountdown;
        private TimeSpan knownDuration;
        private TimeSpan previousRemaining;
        private DateTime previousSampleUtc;
        private bool previousCountdownRunning;
        private string shownState;
        private string primaryLabel;
        private string resetLabel;

        public bool AllowClose { get; set; }

        public PresentationWindow(CoreEngine engine)
        {
            if (engine == null) throw new ArgumentNullException("engine");
            this.engine = engine;
            Title = "浮岛 · 课堂计时";
            Background = Paper;
            Foreground = Ink;
            FontFamily = TextTypeface;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = true;
            Topmost = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            layout = new Grid { Margin = new Thickness(48, 28, 48, 28) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = layout;

            Grid header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel identity = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            FrameworkElement brand = AppVisual.Brand(40);
            brand.Margin = new Thickness(0, 0, 16, 0);
            identity.Children.Add(brand);
            identity.Children.Add(new TextBlock { Text = "课堂计时", FontSize = 30, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            header.Children.Add(identity);
            Button exitButton = CreateButton(false, 216);
            SetButtonContent(exitButton, "退出展示  Esc", "close");
            exitButton.Click += delegate { Hide(); };
            Grid.SetColumn(exitButton, 1);
            header.Children.Add(exitButton);
            layout.Children.Add(header);

            stage = new Grid { Margin = new Thickness(36, 12, 36, 32), VerticalAlignment = VerticalAlignment.Stretch };
            stage.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            stage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            stage.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(stage, 1);
            layout.Children.Add(stage);

            digits = new TextBlock
            {
                Text = "00:00",
                FontFamily = NumberTypeface,
                FontWeight = FontWeights.SemiBold,
                FontSize = 240,
                Foreground = Ink,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Typography.SetNumeralAlignment(digits, FontNumeralAlignment.Tabular);
            AutomationProperties.SetName(digits, "课堂计时显示");
            Viewbox numberBox = new Viewbox
            {
                Child = digits,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 1620,
                Margin = new Thickness(0, 8, 0, 10)
            };
            stage.Children.Add(numberBox);

            StackPanel stateGroup = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 18) };
            stateText = new TextBlock { FontSize = 32, FontWeight = FontWeights.Medium, Foreground = Muted, TextAlignment = TextAlignment.Center };
            contextText = new TextBlock { FontSize = 20, Foreground = Muted, Margin = new Thickness(0, 10, 0, 0), TextAlignment = TextAlignment.Center };
            stateGroup.Children.Add(stateText);
            stateGroup.Children.Add(contextText);
            Grid.SetRow(stateGroup, 1);
            stage.Children.Add(stateGroup);

            progressFill = new Border { Background = Accent, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            progressTrack = new Border
            {
                Height = 8,
                Width = 640,
                MaxWidth = 960,
                CornerRadius = new CornerRadius(4),
                Background = ColorBrush("#DEE4F4"),
                Child = progressFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(progressTrack, 2);
            stage.Children.Add(progressTrack);

            StackPanel footer = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetRow(footer, 2);
            layout.Children.Add(footer);
            controls = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            primaryButton = CreateButton(true, 204);
            primaryButton.Margin = new Thickness(8, 0, 8, 0);
            primaryButton.Click += delegate { ToggleTiming(); };
            resetButton = CreateButton(false, 172);
            resetButton.Margin = new Thickness(8, 0, 8, 0);
            resetButton.Click += delegate { EndTiming(); };
            controls.Children.Add(primaryButton);
            controls.Children.Add(resetButton);
            footer.Children.Add(controls);

            presets = new StackPanel { Margin = new Thickness(0, 20, 0, 0) };
            presets.Children.Add(new TextBlock { Text = "快速开始倒计时", FontSize = 18, Foreground = Muted, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 12) });
            WrapPanel presetRow = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            foreach (int minutes in new[] { 5, 10, 40 })
            {
                int chosen = minutes;
                Button preset = CreateButton(false, 168);
                preset.Margin = new Thickness(8, 0, 8, 8);
                preset.Content = new TextBlock { Text = minutes + " 分钟", FontSize = 22, FontWeight = FontWeights.Medium, HorizontalAlignment = HorizontalAlignment.Center };
                AutomationProperties.SetName(preset, "开始 " + minutes + " 分钟倒计时");
                preset.Click += delegate { StartPreset(chosen); };
                presetRow.Children.Add(preset);
            }
            presets.Children.Add(presetRow);
            footer.Children.Add(presets);

            keyboardHint = new TextBlock
            {
                Text = "空格键开始 / 暂停 · Esc 退出展示",
                Foreground = Muted,
                FontSize = 16,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 20, 0, 0)
            };
            footer.Children.Add(keyboardHint);

            uiTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            uiTimer.Tick += delegate { if (IsVisible) Refresh(); };
            engine.Notice += EngineNotice;
            IsVisibleChanged += delegate
            {
                if (IsVisible) { Refresh(); uiTimer.Start(); }
                else uiTimer.Stop();
            };
            SizeChanged += delegate { ApplyResponsiveLayout(); };
            PreviewKeyDown += HandleKey;
            Closing += HandleClosing;
            Closed += delegate { uiTimer.Stop(); engine.Notice -= EngineNotice; };
            Refresh();
        }

        public void Open()
        {
            // A real HWND establishes the system DPI transform before selecting the cursor's monitor.
            new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            Rect workArea = Native.ScreenBounds(this, Native.Cursor(this));
            WindowState = WindowState.Normal;
            Left = workArea.Left;
            Top = workArea.Top;
            Width = workArea.Width;
            Height = workArea.Height;
            Show();
            Activate();
            Focus();
            Refresh();
        }

        private void StartPreset(int minutes)
        {
            completed = false;
            ownCountdown = true;
            knownDuration = TimeSpan.FromMinutes(minutes);
            previousRemaining = knownDuration;
            previousSampleUtc = DateTime.UtcNow;
            previousCountdownRunning = true;
            engine.StartCountdown(knownDuration);
            Refresh();
        }

        private void ToggleTiming()
        {
            if (completed && (engine.StopwatchRunning || engine.StopwatchElapsed > TimeSpan.Zero))
            {
                completed = false;
                Refresh();
                return;
            }
            completed = false;
            if (engine.CountdownActive) engine.PauseResumeCountdown();
            else engine.ToggleStopwatch();
            Refresh();
        }

        private void EndTiming()
        {
            if (completed)
            {
                completed = false;
                ownCountdown = false;
                Refresh();
                return;
            }
            completed = false;
            ownCountdown = false;
            if (engine.CountdownActive) engine.CancelCountdown();
            else engine.ResetStopwatch();
            Refresh();
        }

        private void EngineNotice(object sender, IslandNoticeEventArgs notice)
        {
            if (notice.Kind != "countdown") return;
            completed = true;
            ownCountdown = false;
            if (IsVisible) Refresh();
        }

        private void Refresh()
        {
            bool countdown = engine.CountdownActive;
            bool stopwatchStarted = engine.StopwatchRunning || engine.StopwatchElapsed > TimeSpan.Zero;
            bool running = countdown ? engine.CountdownRunning : engine.StopwatchRunning;
            bool idle = !countdown && !stopwatchStarted && !completed;
            if (countdown) completed = false;

            string nextState;
            if (completed)
            {
                nextState = "completed";
                digits.Text = "时间到";
                digits.FontFamily = TextTypeface;
                digits.Foreground = FinishedInk;
                stateText.Text = "本轮倒计时已结束";
                stateText.Foreground = FinishedInk;
                contextText.Text = "可以开始下一轮了";
            }
            else
            {
                nextState = countdown ? (running ? "countdown" : "countdown-paused") : (running ? "stopwatch" : (idle ? "idle" : "stopwatch-paused"));
                digits.FontFamily = NumberTypeface;
                digits.Foreground = countdown ? Accent : Ink;
                digits.Text = FormatDuration(countdown ? engine.CountdownRemaining : engine.StopwatchElapsed, countdown);
                stateText.Foreground = Muted;
                stateText.Text = idle ? "准备开始" : (running ? (countdown ? "倒计时" : "正向计时") : "已暂停");
                contextText.Text = idle ? "选择一个时长，或开始正向计时" : (countdown ? "剩余时间" : "已用时间");
            }

            string nextPrimary = countdown || stopwatchStarted ? (running ? "暂停计时" : "继续计时") : "开始计时";
            string nextReset = countdown ? "结束计时" : (completed ? "结束本轮" : "归零");
            if (completed) { nextPrimary = stopwatchStarted ? "显示正计时" : "开始计时"; nextReset = "结束本轮"; }
            if (primaryLabel != nextPrimary)
            {
                primaryLabel = nextPrimary;
                SetButtonContent(primaryButton, nextPrimary, running && !completed ? "pause" : "play");
            }
            if (resetLabel != nextReset)
            {
                resetLabel = nextReset;
                SetButtonContent(resetButton, nextReset, countdown || completed ? "stop" : "reset");
            }
            resetButton.Visibility = idle ? Visibility.Collapsed : Visibility.Visible;
            presets.Visibility = idle || completed ? Visibility.Visible : Visibility.Collapsed;
            keyboardHint.Text = idle || completed ? "空格键开始 · Esc 退出展示" : "空格键暂停 / 继续 · Esc 退出展示";

            if (countdown && ownCountdown && knownDuration > TimeSpan.Zero)
            {
                TimeSpan remaining = engine.CountdownRemaining;
                // A replacement countdown has no trustworthy original duration on this surface.
                DateTime now = DateTime.UtcNow;
                TimeSpan expectedRemaining = previousCountdownRunning ? previousRemaining - (now - previousSampleUtc) : previousRemaining;
                if (Math.Abs((remaining - expectedRemaining).TotalSeconds) > 1.25) ownCountdown = false;
                previousRemaining = remaining;
                previousSampleUtc = now;
                previousCountdownRunning = engine.CountdownRunning;
                progressTrack.Visibility = ownCountdown ? Visibility.Visible : Visibility.Collapsed;
                double fraction = Math.Max(0, Math.Min(1, remaining.TotalMilliseconds / knownDuration.TotalMilliseconds));
                progressFill.Width = Math.Max(0, progressTrack.Width * fraction);
            }
            else
            {
                progressTrack.Visibility = Visibility.Collapsed;
                if (!countdown) ownCountdown = false;
            }

            if (shownState != nextState)
            {
                shownState = nextState;
                if (IsVisible && !SurfaceStyle.SnapshotMode && SystemParameters.ClientAreaAnimation)
                {
                    stateText.BeginAnimation(OpacityProperty, new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
                }
            }
        }

        private void ApplyResponsiveLayout()
        {
            bool compact = ActualWidth < 1050 || ActualHeight < 740;
            layout.Margin = compact ? new Thickness(24, 20, 24, 20) : new Thickness(48, 28, 48, 28);
            stage.Margin = compact ? new Thickness(16, 4, 16, 20) : new Thickness(36, 12, 36, 32);
            stateText.FontSize = compact ? 26 : 32;
            contextText.FontSize = compact ? 18 : 20;
            keyboardHint.Visibility = ActualHeight < 620 ? Visibility.Collapsed : Visibility.Visible;
            progressTrack.Width = Math.Max(200, Math.Min(960, ActualWidth - (compact ? 120 : 240)));
            controls.MaxWidth = Math.Max(360, ActualWidth - 48);
        }

        private void HandleKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
            else if (e.Key == Key.Space)
            {
                if (!e.IsRepeat) ToggleTiming();
                e.Handled = true;
            }
        }

        private void HandleClosing(object sender, CancelEventArgs e)
        {
            if (AllowClose) return;
            e.Cancel = true;
            Hide();
        }

        private static string FormatDuration(TimeSpan duration, bool roundUp)
        {
            double seconds = roundUp ? Math.Ceiling(duration.TotalSeconds) : Math.Floor(duration.TotalSeconds);
            long value = (long)Math.Max(0, seconds);
            long hours = value / 3600;
            return hours > 0
                ? String.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", hours, value / 60 % 60, value % 60)
                : String.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", value / 60, value % 60);
        }

        private static Brush ColorBrush(string value)
        {
            Brush brush = (Brush)new BrushConverter().ConvertFromInvariantString(value);
            brush.Freeze();
            return brush;
        }

        private static Button CreateButton(bool primary, double width)
        {
            Button button = new Button
            {
                MinWidth = 160,
                Width = width,
                Height = 68,
                FontSize = 22,
                FontWeight = FontWeights.Medium,
                Foreground = primary ? Brushes.White : Ink,
                Background = primary ? Accent : Brushes.White,
                BorderBrush = primary ? Accent : ColorBrush("#D5DCEA"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 10, 18, 10),
                Cursor = Cursors.Hand
            };
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "ButtonSurface";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            FrameworkElementFactory content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.AppendChild(content);
            template.VisualTree = border;
            Trigger hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, ColorBrush(primary ? "#4258CE" : "#EDF0FA"), "ButtonSurface"));
            template.Triggers.Add(hover);
            Trigger pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, ColorBrush(primary ? "#3449B9" : "#DFE6F6"), "ButtonSurface"));
            template.Triggers.Add(pressed);
            Trigger focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, ColorBrush("#213B97"), "ButtonSurface"));
            focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(3), "ButtonSurface"));
            template.Triggers.Add(focus);
            Trigger disabled = new Trigger { Property = IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(OpacityProperty, 0.45));
            template.Triggers.Add(disabled);
            button.Template = template;
            return button;
        }

        private static void SetButtonContent(Button button, string label, string iconName)
        {
            StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            FrameworkElement icon = AppVisual.Icon(iconName, 23, button.Foreground);
            icon.Margin = new Thickness(0, 0, 10, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            button.Content = row;
            AutomationProperties.SetName(button, label);
        }
    }
}
