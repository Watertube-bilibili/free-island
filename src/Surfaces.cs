using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Interop;

namespace FreeIsland
{
    internal static class SurfaceStyle
    {
        public static bool SnapshotMode;
        public static SolidColorBrush Brush(string hex) { return (SolidColorBrush)new BrushConverter().ConvertFromString(hex); }
        public static TextBlock Text(string value, double size, string color)
        {
            return new TextBlock { Text = value, FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = size, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
        }
        public static void Setup(Window window, string title)
        {
            window.Title = title; window.WindowStyle = WindowStyle.None; window.ResizeMode = ResizeMode.NoResize;
            window.AllowsTransparency = true; window.Background = Brushes.Transparent;
            window.ShowInTaskbar = false; window.Topmost = true; window.ShowActivated = false;
            window.FontFamily = new FontFamily("Microsoft YaHei UI"); window.UseLayoutRounding = true;
        }
        public static Grid Orb(double size, int glassMode)
        {
            return LiquidGlass.Orb(size, glassMode);
        }
        public static void Scale(FrameworkElement element, double from, double to, int milliseconds)
        {
            var transform = new ScaleTransform(to, to); element.RenderTransformOrigin = new Point(.5, .5); element.RenderTransform = transform;
            if (SnapshotMode) return;
            var motion = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = new BackEase { Amplitude = .22, EasingMode = EasingMode.EaseOut } };
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, motion); transform.BeginAnimation(ScaleTransform.ScaleYProperty, motion);
        }
        public static void Fade(UIElement element, double from, double to, int milliseconds, Action completed)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = to;
            if (SnapshotMode) { if (completed != null) completed(); return; }
            var motion = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            if (completed != null) motion.Completed += delegate { completed(); };
            element.BeginAnimation(UIElement.OpacityProperty, motion);
        }
        public static void StopPosition(Window window)
        {
            double x = window.Left, y = window.Top;
            window.BeginAnimation(Window.LeftProperty, null); window.BeginAnimation(Window.TopProperty, null); window.Left = x; window.Top = y;
        }
        public static void AnimateDock(Window window, Point origin)
        {
            if (SnapshotMode) return;
            window.BeginAnimation(Window.LeftProperty, new DoubleAnimation(origin.X, window.Left, TimeSpan.FromMilliseconds(220)) { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            window.BeginAnimation(Window.TopProperty, new DoubleAnimation(origin.Y, window.Top, TimeSpan.FromMilliseconds(220)) { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        public static Button Button(string title, Action action, string fill)
        {
            var button = new Button { Content = title, Background = Brush(fill), Foreground = Brush("#263553"), BorderThickness = new Thickness(0), Padding = new Thickness(12, 6, 12, 6), MinHeight = 34, Cursor = Cursors.Hand, FontSize = 12 };
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 5, 8, 5));
            border.AppendChild(presenter); template.VisualTree = border;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            border.Name = "Surface"; hover.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#E2E9F8"), "Surface")); template.Triggers.Add(hover);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#263DAF"), "Surface")); focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Surface")); template.Triggers.Add(focus);
            button.Template = template; button.PreviewMouseLeftButtonDown += delegate { if (!UsesOpticalMaterial(button)) Scale(button, 1, .96, 95); };
            button.PreviewMouseLeftButtonUp += delegate { if (!UsesOpticalMaterial(button)) Scale(button, .96, 1, 170); };
            button.Click += delegate { action(); };
            return button;
        }
        private static bool UsesOpticalMaterial(Button button)
        {
            if (!LiquidGlass.IsWaterButton(button)) return false;
            var material = button.Template.FindName("GlassMaterial", button) as LiquidGlassSurface;
            return material != null && material.Mode != 0;
        }
    }

    public sealed class BallWindow : Window
    {
        private readonly CoreEngine engine;
        private readonly Action clicked;
        private readonly Action<string> navigate;
        private readonly DispatcherTimer hideTimer;
        private readonly DispatcherTimer hoverTimer;
        private bool pointerDown, dragged, tucked; private int materialMode = -1;
        private Point mouseStart, windowStart;
        private string edge = "";
        private Rect area;
        public Rect WorkArea { get { return Native.ScreenWorkArea(this, new Point(Left + Width / 2, Top + Height / 2)); } }

        public BallWindow(CoreEngine engine, Action clicked, Action<string> navigate)
        {
            this.engine = engine; this.clicked = clicked; this.navigate = navigate;
            SurfaceStyle.Setup(this, "浮岛 · 悬浮球"); Width = SceneMetrics.BallSize(engine); Height = Width;
            hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
            hideTimer.Tick += delegate { hideTimer.Stop(); if (!IsMouseOver && !pointerDown && IsVisible && engine.Settings.EdgeHide && edge != "") Tuck(); };
            hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
            hoverTimer.Tick += delegate { hoverTimer.Stop(); if (tucked && IsMouseOver) Reveal(); };
            Loaded += delegate { EnsureOnScreen(); };
            MouseLeftButtonDown += Press;
            MouseMove += Move;
            MouseLeftButtonUp += Release;
            LostMouseCapture += delegate { pointerDown = false; SetMaterialPressed(false); };
            IsVisibleChanged += delegate { if (!IsVisible) SetMaterialPressed(false); };
            MouseEnter += delegate { hideTimer.Stop(); if (tucked) hoverTimer.Start(); else if (engine.Settings.GlassMode == 0) SurfaceStyle.Scale((FrameworkElement)Content, 1, 1.04, 180); };
            MouseLeave += delegate { hoverTimer.Stop(); if (!tucked && engine.Settings.GlassMode == 0) SurfaceStyle.Scale((FrameworkElement)Content, 1.04, 1, 200); ScheduleHide(); };
            bool touchWasTucked = false;
            TouchWindowDrag.Attach(this, null,
                delegate { hideTimer.Stop(); hoverTimer.Stop(); SurfaceStyle.StopPosition(this); touchWasTucked = tucked; Reveal(); pointerDown = true; SetMaterialPressed(true); if (engine.Settings.GlassMode == 0) SurfaceStyle.Scale((FrameworkElement)Content, 1, .94, 100); },
                delegate { if (!touchWasTucked) clicked(); },
                delegate(Point point) { pointerDown = false; SetMaterialPressed(false); SnapAndSave(); },
                delegate { pointerDown = false; SetMaterialPressed(false); if (!tucked && engine.Settings.GlassMode == 0) SurfaceStyle.Scale((FrameworkElement)Content, .94, 1, 220); ScheduleHide(); });
            var context = new ContextMenu();
            AddMenu(context, "打开控制中心", delegate { navigate("home"); });
            AddMenu(context, "设置", delegate { navigate("settings"); });
            AddMenu(context, "移回屏幕内", RestorePosition);
            ContextMenu = context;
            RenderBall();
            var work = SystemParameters.WorkArea;
            Left = double.IsNaN(engine.Settings.BallX) ? work.Right - 104 : engine.Settings.BallX;
            Top = double.IsNaN(engine.Settings.BallY) ? work.Top + work.Height * .58 : engine.Settings.BallY;
        }
        private void AddMenu(ContextMenu menu, string title, Action action) { var item = new MenuItem { Header = title }; item.Click += delegate { action(); }; menu.Items.Add(item); }
        private void RenderBall()
        {
            materialMode = engine.Settings.GlassMode; var grid = new Grid { Background = Brushes.Transparent, ToolTip = "点击展开功能 · 拖动到边缘收起\nCtrl + Alt + 空格 找回悬浮球" };
            var orb = SurfaceStyle.Orb(SceneMetrics.BallOrbSize(engine), engine.Settings.GlassMode); if (engine.Settings.GlassMode == 2) orb.Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Color = Color.FromRgb(34, 48, 91), Opacity = .16 }; grid.Children.Add(orb); Content = grid;
            System.Windows.Automation.AutomationProperties.SetName(grid, "悬浮球，点击展开环形功能菜单");
        }
        private void Press(object sender, MouseButtonEventArgs e)
        {
            hideTimer.Stop(); hoverTimer.Stop();
            SurfaceStyle.StopPosition(this);
            if (tucked) { Reveal(); e.Handled = true; return; }
            mouseStart = Native.Cursor(this); windowStart = new Point(Left, Top); pointerDown = true; dragged = false; CaptureMouse(); SetMaterialPressed(true);
            if (engine.Settings.GlassMode == 0) SurfaceStyle.Scale((FrameworkElement)Content, 1, .94, 100); e.Handled = true;
        }
        private void SetMaterialPressed(bool pressed)
        {
            var host = Content as DependencyObject;
            if (host != null) LiquidGlass.Press(host, pressed);
        }
        private void Move(object sender, MouseEventArgs e)
        {
            if (!pointerDown || e.LeftButton != MouseButtonState.Pressed) return;
            var p = Native.Cursor(this); var delta = p - mouseStart;
            if (!dragged && delta.Length < 5) return;
            dragged = true; Left = windowStart.X + delta.X; Top = windowStart.Y + delta.Y;
        }
        private void Release(object sender, MouseButtonEventArgs e)
        {
            SetMaterialPressed(false);
            if (!pointerDown) return;
            pointerDown = false; ReleaseMouseCapture();
            if (!tucked && engine.Settings.GlassMode == 0) SurfaceStyle.Scale((FrameworkElement)Content, .94, 1, 220);
            if (dragged) { SnapAndSave(); } else clicked();
            e.Handled = true;
        }
        private void SnapAndSave()
        {
            SurfaceStyle.StopPosition(this);
            Point origin = new Point(Left, Top);
            area = WorkArea; edge = "";
            if (engine.Settings.EdgeHide)
            {
                if (Left <= area.Left + 22) edge = "left";
                else if (Left + Width >= area.Right - 22) edge = "right";
                else if (Top <= area.Top + 18) edge = "top";
                else if (Top + Height >= area.Bottom - 18) edge = "bottom";
            }
            Left = Math.Max(area.Left, Math.Min(Left, area.Right - Width)); Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
            if (edge == "left") Left = area.Left;
            if (edge == "right") Left = area.Right - Width;
            if (edge == "top") Top = area.Top;
            if (edge == "bottom") Top = area.Bottom - Height;
            SavePosition(); ScheduleHide();
            if (IsVisible) SurfaceStyle.AnimateDock(this, origin);
        }
        public void ScheduleHide() { if (edge != "" && engine.Settings.EdgeHide) { hideTimer.Stop(); hideTimer.Start(); } }
        private void Tuck()
        {
            if (tucked || edge == "") return;
            tucked = true; area = WorkArea;
            bool side = edge == "left" || edge == "right";
            double centerX = Left + Width / 2, centerY = Top + Height / 2;
            Width = side ? SceneMetrics.BallArrowThin(engine) : SceneMetrics.BallArrowLong(engine); Height = side ? SceneMetrics.BallArrowLong(engine) : SceneMetrics.BallArrowThin(engine);
            Left = side ? (edge == "left" ? area.Left : area.Right - Width) : Math.Max(area.Left, Math.Min(centerX - Width / 2, area.Right - Width));
            Top = side ? Math.Max(area.Top, Math.Min(centerY - Height / 2, area.Bottom - Height)) : (edge == "top" ? area.Top : area.Bottom - Height);
            bool classroom = engine.Settings.Scene == UsageScene.Classroom;
            var border = new Border { Background = SurfaceStyle.Brush("#EDF0FF"), CornerRadius = new CornerRadius(6), Width = classroom ? (side ? 16 : 40) : Width, Height = classroom ? (side ? 40 : 16) : Height, ToolTip = "浮岛 · 点击或悬停展开", Cursor = Cursors.Hand };
            border.HorizontalAlignment = edge == "left" ? HorizontalAlignment.Left : edge == "right" ? HorizontalAlignment.Right : HorizontalAlignment.Center;
            border.VerticalAlignment = edge == "top" ? VerticalAlignment.Top : edge == "bottom" ? VerticalAlignment.Bottom : VerticalAlignment.Center;
            var arrow = AppVisual.Icon(edge == "left" ? "arrowright" : edge == "right" ? "arrowleft" : "arrowdown", classroom ? 13 : 9, SurfaceStyle.Brush("#4F66E8")); if (edge == "bottom") { arrow.RenderTransformOrigin = new Point(.5, .5); arrow.RenderTransform = new RotateTransform(180); }
            border.Child = arrow; var touchArea = new Grid { Background = Brushes.Transparent }; touchArea.Children.Add(border); Content = touchArea;
            SurfaceStyle.Fade(touchArea, .25, 1, 160, null);
        }
        private void Reveal()
        {
            if (!tucked) return;
            double cx = Left + Width / 2, cy = Top + Height / 2;
            tucked = false; Width = SceneMetrics.BallSize(engine); Height = Width;
            Left = Math.Max(area.Left, Math.Min(cx - Width / 2, area.Right - Width)); Top = Math.Max(area.Top, Math.Min(cy - Height / 2, area.Bottom - Height));
            RenderBall();
            if (engine.Settings.GlassMode == 0) SurfaceStyle.Scale((FrameworkElement)Content, .72, 1, 260);
            else if (engine.Settings.GlassMode == 2) LiquidGlass.Arrive((DependencyObject)Content);
            ScheduleHide();
        }
        public void ApplyScene() { Reveal(); Width = SceneMetrics.BallSize(engine); Height = Width; RenderBall(); EnsureOnScreen(); }
        public void ApplyMaterial() { if (!tucked && materialMode != engine.Settings.GlassMode) RenderBall(); }
        public void ApplyEdgePreference() { if (!engine.Settings.EdgeHide) { Reveal(); edge = ""; hideTimer.Stop(); } else SnapAndSave(); }
        public void RestorePosition()
        {
            Reveal(); edge = ""; hideTimer.Stop();
            var work = Native.ScreenWorkArea(this, new Point(Left, Top)); Left = work.Right - 110; Top = work.Top + work.Height * .58;
            Show(); SavePosition();
        }
        public void EnsureOnScreen() { if (tucked) Reveal(); SnapAndSave(); }
        internal void VerifyEdgeCollapse()
        {
            if (!engine.IsSafeMode) throw new InvalidOperationException("Edge checks require safe mode.");
            Reveal();
            var original = new Point(Left, Top); bool originalPreference = engine.Settings.EdgeHide;
            var work = WorkArea;
            engine.Settings.EdgeHide = true;
            double size = SceneMetrics.BallSize(engine);
            Point[] points = { new Point(work.Left, work.Top + work.Height / 2), new Point(work.Right - size, work.Top + work.Height / 2), new Point(work.Left + work.Width / 2, work.Top), new Point(work.Left + work.Width / 2, work.Bottom - size) };
            try
            {
                for (int i = 0; i < points.Length; i++)
                {
                    Reveal(); Left = points[i].X; Top = points[i].Y; SnapAndSave(); Tuck();
                    if (!tucked || Width != (i < 2 ? SceneMetrics.BallArrowThin(engine) : SceneMetrics.BallArrowLong(engine)) || Height != (i < 2 ? SceneMetrics.BallArrowLong(engine) : SceneMetrics.BallArrowThin(engine))) throw new InvalidOperationException("Edge collapse check failed: " + i);
                }
            }
            finally { Reveal(); engine.Settings.EdgeHide = originalPreference; Left = original.X; Top = original.Y; SnapAndSave(); }
        }
        private void SavePosition() { engine.Settings.BallX = Left; engine.Settings.BallY = Top; engine.SaveSettings(); }
    }

    public sealed class RadialWindow : Window
    {
        private readonly CoreEngine engine;
        private readonly Action<string> navigate;
        private readonly Action dismissed;
        private readonly Canvas canvas;
        private readonly ContentControl centerOrb;
        private readonly System.Collections.Generic.List<Button> choices = new System.Collections.Generic.List<Button>();
        private bool dismissing; private int materialMode = -1;
        private int motionVersion;
        public RadialWindow(CoreEngine engine, Action<string> navigate, Action dismissed)
        {
            this.engine = engine; this.navigate = navigate; this.dismissed = dismissed;
            SurfaceStyle.Setup(this, "浮岛 · 环形菜单"); ShowActivated = true;
            canvas = new Canvas { Width = 354, Height = 354 };
            string[] labels = { "正向计时", "倒计时", "日程提醒", "定时关机", "设置", "控制中心" };
            string[] pages = { "stopwatch", "countdown", "reminders", "shutdown", "settings", "home" };
            for (int i = 0; i < 6; i++)
            {
                string page = pages[i]; double angle = (-90 + i * 60) * Math.PI / 180;
                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var icon = AppVisual.Icon(page, 26, SurfaceStyle.Brush(page == "shutdown" ? "#AA624B" : "#4F66E8")); icon.Margin = new Thickness(0, 0, 0, 9); stack.Children.Add(icon);
                stack.Children.Add(SurfaceStyle.Text(labels[i], 12, "#263553"));
                var button = SurfaceStyle.Button("", delegate { Dismiss(); navigate(page); }, "#F1F3FA");
                button.Content = stack; button.Width = 80; button.Height = 76;
                System.Windows.Automation.AutomationProperties.SetName(button, labels[i]);
                Canvas.SetLeft(button, 177 + Math.Cos(angle) * 116 - 40); Canvas.SetTop(button, 177 + Math.Sin(angle) * 116 - 38);
                canvas.Children.Add(button); choices.Add(button);
            }
            var center = new StackPanel { Width = 84 };
            centerOrb = new ContentControl { Width = 54, Height = 54, Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Center };
            centerOrb.MouseLeftButtonUp += delegate { Dismiss(); }; center.Children.Add(centerOrb);
            Canvas.SetLeft(center, 135); Canvas.SetTop(center, 142); canvas.Children.Add(center);
            Content = canvas;
            Deactivated += delegate { Dismiss(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; } };
            ApplyScene();
        }
        public void ApplyScene()
        {
            double scale = SceneMetrics.Scale(engine); Width = 354 * scale; Height = Width;
            canvas.LayoutTransform = new ScaleTransform(scale, scale);
            ApplyMaterial();
        }
        public void ApplyMaterial()
        {
            if (materialMode == engine.Settings.GlassMode) return; materialMode = engine.Settings.GlassMode;
            foreach (Button button in choices) { LiquidGlass.Button(button, engine.Settings.GlassMode); if (materialMode != 0) button.RenderTransform = Transform.Identity; }
            centerOrb.Content = SurfaceStyle.Orb(54, engine.Settings.GlassMode);
        }
        public void OpenAt(double x, double y, Rect work)
        {
            motionVersion++; dismissing = false;
            Left = Math.Max(work.Left + 4, Math.Min(x - Width / 2, work.Right - Width - 4));
            Top = Math.Max(work.Top + 4, Math.Min(y - Height / 2, work.Bottom - Height - 4));
            Show(); Activate();
            if (engine.Settings.GlassMode == 1) { canvas.BeginAnimation(UIElement.OpacityProperty, null); canvas.Opacity = 1; }
            else SurfaceStyle.Fade(canvas, .3, 1, engine.Settings.GlassMode == 2 ? 220 : 180, null);
            if (engine.Settings.GlassMode == 2 && !SurfaceStyle.SnapshotMode) LiquidGlass.Arrive(centerOrb);
            for (int i = 0; i < choices.Count; i++)
            {
                var button = choices[i];
                if (SurfaceStyle.SnapshotMode || engine.Settings.GlassMode == 1) { button.RenderTransform = Transform.Identity; continue; }
                var translate = new TranslateTransform(); var scale = new ScaleTransform(1, 1);
                var group = new TransformGroup(); if (engine.Settings.GlassMode == 0) group.Children.Add(scale); group.Children.Add(translate);
                button.RenderTransformOrigin = new Point(.5, .5); button.RenderTransform = group;
                double angle = (-90 + i * 60) * Math.PI / 180;
                IEasingFunction ease = engine.Settings.GlassMode == 0 ? (IEasingFunction)new BackEase { Amplitude = .35, EasingMode = EasingMode.EaseOut } : new CubicEase { EasingMode = EasingMode.EaseOut };
                TimeSpan delay = TimeSpan.FromMilliseconds(i * (engine.Settings.GlassMode == 2 ? 10 : 16));
                translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-Math.Cos(angle) * 28, 0, TimeSpan.FromMilliseconds(240)) { BeginTime = delay, EasingFunction = ease });
                translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-Math.Sin(angle) * 28, 0, TimeSpan.FromMilliseconds(240)) { BeginTime = delay, EasingFunction = ease });
                if (engine.Settings.GlassMode == 0)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.74, 1, TimeSpan.FromMilliseconds(240)) { BeginTime = delay, EasingFunction = ease });
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.74, 1, TimeSpan.FromMilliseconds(240)) { BeginTime = delay, EasingFunction = ease });
                }
                else LiquidGlass.Arrive(button);
            }
        }
        public void Dismiss()
        {
            if (!IsVisible || dismissing) return;
            dismissing = true; int version = ++motionVersion;
            SurfaceStyle.Fade(canvas, canvas.Opacity, 0, engine.Settings.GlassMode == 1 ? 0 : 125, delegate
            {
                if (version != motionVersion) return;
                Hide(); dismissed(); dismissing = false;
            });
        }
    }

    public sealed class IslandWindow : Window
    {
        public event EventHandler Expanded;
        public event EventHandler Collapsed;
        public Rect LastWorkArea { get; private set; }
        private readonly CoreEngine engine;
        private readonly Action<string> navigate;
        private readonly TextBlock title, detail;
        private readonly ContentControl symbol;
        private readonly Grid surface;
        private readonly Button action;
        private readonly DispatcherTimer hideTimer;
        private string activity = "";
        private bool urgent;
        private DateTime visibleUntil;
        private Border card;
        private readonly LiquidGlassSurface material; private int materialMode = -1;
        private bool pointerDown, dragged;
        private Point dragStart, positionStart;
        private readonly Action<Point> dropped;
        private int motionVersion;
        private bool collapsing;
        private string iconName = "";

        public IslandWindow(CoreEngine engine, Action<string> navigate, Action<Point> dropped)
        {
            this.engine = engine; this.navigate = navigate;
            this.dropped = dropped;
            LastWorkArea = SystemParameters.WorkArea;
            SurfaceStyle.Setup(this, "浮岛 · 灵动岛"); Width = 420; Height = 98;
            card = new Border { Margin = new Thickness(10), Background = Brushes.Transparent, CornerRadius = new CornerRadius(32) };
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            symbol = new ContentControl { VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center }; grid.Children.Add(symbol); SetIcon("countdown");
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0) }; Grid.SetColumn(words, 1);
            title = SurfaceStyle.Text("浮岛", 16, "#18243A"); title.TextAlignment = TextAlignment.Left; title.FontWeight = FontWeights.SemiBold; title.TextTrimming = TextTrimming.CharacterEllipsis;
            detail = SurfaceStyle.Text("点击查看 · 拖动调整位置", 12, "#58657A"); detail.TextAlignment = TextAlignment.Left; detail.TextTrimming = TextTrimming.CharacterEllipsis; detail.Margin = new Thickness(0, 5, 0, 0);
            words.Children.Add(title); words.Children.Add(detail); grid.Children.Add(words);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(actions, 2);
            action = SurfaceStyle.Button("查看", Act, "#EEF1FF"); actions.Children.Add(action);
            var close = SurfaceStyle.Button("", delegate { urgent = false; Collapse(); }, "#F2F4F9"); close.Content = AppVisual.Icon("close", 15, SurfaceStyle.Brush("#596783")); close.Width = 34; close.Margin = new Thickness(5, 0, 0, 0); close.ToolTip = "收起为小黑点，任务继续运行"; actions.Children.Add(close); grid.Children.Add(actions);
            material = new LiquidGlassSurface { Mode = engine.Settings.GlassMode, Radius = 32, Name = "IslandGlassMaterial" };
            var layers = new Grid(); layers.Children.Add(material);
            grid.Margin = new Thickness(16, 10, 12, 10); layers.Children.Add(grid); card.Child = layers;
            LiquidGlass.Track(card, material);
            surface = new Grid { Width = 440, Height = 102 }; surface.Children.Add(card); Content = surface;
            hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            hideTimer.Tick += delegate { if (!urgent && !pointerDown && !IsMouseOver && DateTime.UtcNow >= visibleUntil) Collapse(); };
            MouseEnter += delegate { visibleUntil = DateTime.UtcNow.AddSeconds(4); };
            MouseLeave += delegate { if (!urgent) visibleUntil = DateTime.UtcNow.AddSeconds(3); };
            card.Cursor = Cursors.SizeAll;
            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (IsButtonSource(e.OriginalSource as DependencyObject)) return;
                SurfaceStyle.StopPosition(this);
                dragStart = Native.Cursor(this); positionStart = new Point(Left, Top);
                pointerDown = true; dragged = false; CaptureMouse(); LiquidGlass.Press(card, true); e.Handled = true;
            };
            MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!pointerDown || e.LeftButton != MouseButtonState.Pressed) return;
                Vector delta = Native.Cursor(this) - dragStart;
                if (!dragged && delta.Length < 4) return;
                dragged = true; Left = positionStart.X + delta.X; Top = positionStart.Y + delta.Y;
            };
            MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                LiquidGlass.Press(card, false);
                if (!pointerDown) return;
                pointerDown = false; ReleaseMouseCapture();
                if (dragged && dropped != null) dropped(new Point(Left + Width / 2, Top + Height / 2));
                KeepOpenAfterDrag(); e.Handled = true;
            };
            LostMouseCapture += delegate { pointerDown = false; LiquidGlass.Press(card, false); };
            TouchWindowDrag.Attach(this, delegate(DependencyObject source) { return !IsButtonSource(source); },
                delegate { SurfaceStyle.StopPosition(this); pointerDown = true; LiquidGlass.Press(card, true); },
                KeepOpenAfterDrag,
                delegate(Point point) { pointerDown = false; LiquidGlass.Press(card, false); if (dropped != null) dropped(point); },
                delegate { pointerDown = false; LiquidGlass.Press(card, false); KeepOpenAfterDrag(); });
            SourceInitialized += delegate
            {
                var source = PresentationSource.FromVisual(this) as HwndSource;
                if (source != null) source.AddHook(delegate(IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled)
                { if (message == 0x0021) { handled = true; return new IntPtr(3); } return IntPtr.Zero; });
                Reposition();
            };
            ApplyScene();
        }
        public void ApplyScene()
        {
            ApplyMaterial();
            Reposition();
        }
        public void ApplyMaterial() { if (materialMode == engine.Settings.GlassMode) return; materialMode = engine.Settings.GlassMode; material.Mode = materialMode; if (materialMode != 0) card.RenderTransform = Transform.Identity; card.Effect = materialMode == 2 ? new DropShadowEffect { BlurRadius = 15, Opacity = .14, ShadowDepth = 4, Color = Color.FromRgb(35, 51, 83) } : null; }
        private void SetIcon(string name) { if (iconName == name) return; iconName = name; symbol.Content = AppVisual.Icon(name, 28, SurfaceStyle.Brush("#4F66E8")); }
        private static bool IsButtonSource(DependencyObject source)
        {
            while (source != null) { if (source is Button) return true; source = VisualTreeHelper.GetParent(source); }
            return false;
        }
        public void KeepOpenAfterDrag() { visibleUntil = DateTime.UtcNow.AddSeconds(7); }
        public void ShowActivity(string kind)
        {
            if (urgent && activity == "shutdown" && engine.ShutdownAt.HasValue) return;
            activity = kind; urgent = false; action.Content = kind == "shutdown" ? "取消" : "查看";
            card.ToolTip = null;
            RefreshActivity(); Present(7);
        }
        public void RefreshActivity()
        {
            if (activity == "countdown")
            {
                title.Text = "倒计时  " + Format(engine.CountdownRemaining); detail.Text = engine.CountdownRunning ? "正在计时 · 点击查看" : engine.CountdownActive ? "已暂停 · 点击继续" : "计时已结束"; SetIcon("countdown");
            }
            else if (activity == "stopwatch") { title.Text = "正向计时  " + Format(engine.StopwatchElapsed); detail.Text = engine.StopwatchRunning ? "正在计时 · 点击查看" : "已暂停"; SetIcon("stopwatch"); }
            else if (activity == "shutdown")
            {
                title.Text = engine.ShutdownAt.HasValue ? "关机倒计时  " + Format(engine.ShutdownAt.Value - DateTime.Now) : "关机计划已取消";
                detail.Text = engine.IsSafeMode ? "演示模式 · 不会实际关机" : "请保存工作 · 随时可以取消"; SetIcon("shutdown");
                if (!engine.ShutdownAt.HasValue && urgent) { urgent = false; visibleUntil = DateTime.UtcNow.AddSeconds(4); }
            }
        }
        public void ShowNotice(IslandNoticeEventArgs notice)
        {
            if (urgent && activity == "shutdown" && engine.ShutdownAt.HasValue && notice.Kind != "shutdown") return;
            activity = notice.Urgent && notice.Kind == "shutdown" && engine.ShutdownAt.HasValue ? "shutdown" : "";
            urgent = notice.Urgent && activity == "shutdown";
            title.Text = notice.Title; detail.Text = notice.Message;
            // Long reminder titles remain accessible in full via hover and the reminders page.
            card.ToolTip = notice.Title + "\n" + notice.Message;
            SetIcon(notice.Kind == "shutdown" ? "shutdown" : notice.Kind == "countdown" ? "countdown" : "reminders");
            action.Content = activity == "shutdown" ? "取消" : "知道了";
            Present(notice.Kind == "info" ? 6 : 20);
        }
        private void Act()
        {
            if (activity == "shutdown" && engine.ShutdownAt.HasValue) { engine.CancelShutdown(); urgent = false; Collapse(); }
            else if (activity != "") { navigate(activity); Collapse(); }
            else { urgent = false; Collapse(); }
        }
        private void Present(int seconds)
        {
            motionVersion++; collapsing = false; SurfaceStyle.StopPosition(this);
            visibleUntil = DateTime.UtcNow.AddSeconds(seconds); Reposition(); Show(); hideTimer.Start();
            var handler = Expanded; if (handler != null) handler(this, EventArgs.Empty);
            LiquidGlass.Press(card, false);
            if (engine.Settings.GlassMode == 0) { SurfaceStyle.Scale(card, .76, 1, 290); SurfaceStyle.Fade(card, .3, 1, 200, null); }
            else
            {
                card.RenderTransform = Transform.Identity;
                if (engine.Settings.GlassMode == 2) { LiquidGlass.Arrive(card); SurfaceStyle.Fade(card, .3, 1, 220, null); }
                else { card.BeginAnimation(UIElement.OpacityProperty, null); card.Opacity = 1; }
            }
        }
        public void Reposition()
        {
            SurfaceStyle.StopPosition(this);
            var work = Native.DockWorkArea(this, engine.Settings.IslandScreen);
            LastWorkArea = work;
            double preference = engine.Settings.IslandScale;
            if (double.IsNaN(preference) || double.IsInfinity(preference) || preference < .75 || preference > 1.5) preference = 1;
            double scale = SceneMetrics.Scale(engine) * preference;
            scale = Math.Min(scale, Math.Min(Math.Max(1, work.Width - 8) / 440, Math.Max(1, work.Height - 8) / 102));
            Width = 440 * scale; Height = 102 * scale; surface.LayoutTransform = new ScaleTransform(scale, scale);
            double anchor = engine.Settings.IslandAnchor;
            double left = engine.Settings.Placement == IslandPlacement.Top ? work.Left + work.Width * anchor - Width / 2 : engine.Settings.Placement == IslandPlacement.Left ? work.Left + 2 : work.Right - Width - 2;
            double top = engine.Settings.Placement == IslandPlacement.Top ? work.Top + 4 : work.Top + work.Height * anchor - Height / 2;
            Left = Math.Max(work.Left, Math.Min(left, work.Right - Width)); Top = Math.Max(work.Top, Math.Min(top, work.Bottom - Height));
        }
        public void Collapse()
        {
            if (collapsing) return;
            collapsing = true; int version = ++motionVersion; hideTimer.Stop();
            if (engine.Settings.GlassMode == 0) SurfaceStyle.Scale(card, 1, .78, 160);
            else { card.RenderTransform = Transform.Identity; LiquidGlass.Press(card, engine.Settings.GlassMode == 2); }
            SurfaceStyle.Fade(card, card.Opacity, 0, engine.Settings.GlassMode == 1 ? 0 : 140, delegate
            {
                if (version != motionVersion) return;
                LiquidGlass.Press(card, false);
                Hide(); collapsing = false;
                var handler = Collapsed; if (handler != null) handler(this, EventArgs.Empty);
            });
        }
        private static string Format(TimeSpan value) { if (value < TimeSpan.Zero) value = TimeSpan.Zero; return ((int)value.TotalHours).ToString("00") + ":" + value.Minutes.ToString("00") + ":" + value.Seconds.ToString("00"); }
    }
}
