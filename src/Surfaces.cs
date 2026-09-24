using System;
using System.Collections.Generic;
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
        public static Button Button(string title, Action action, string fill, bool primary = false)
        {
            var button = new Button { Content = title, Background = Brush(fill), Foreground = Brush(primary ? "#FFFFFF" : "#263553"), BorderThickness = new Thickness(0), Padding = new Thickness(12, 6, 12, 6), MinHeight = 34, Cursor = Cursors.Hand, FontSize = 12 };
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 5, 8, 5));
            border.AppendChild(presenter); template.VisualTree = border;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            border.Name = "Surface"; hover.Setters.Add(new Setter(Border.BackgroundProperty, Brush(primary ? "#4258D4" : "#E2E9F8"), "Surface")); template.Triggers.Add(hover);
            if (primary) { var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true }; pressed.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#3448BA"), "Surface")); template.Triggers.Add(pressed); }
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
            centerOrb.MouseLeftButtonUp += delegate { Dismiss(); navigate("chat"); }; center.Children.Add(centerOrb);
            centerOrb.ToolTip = "问浮岛 · 岛上对话";
            var ask = SurfaceStyle.Button("问浮岛", delegate { Dismiss(); navigate("chat"); }, "#EEF1FF");
            ask.MinHeight = 32; ask.Padding = new Thickness(0); ask.Margin = new Thickness(0, 3, 0, 0);
            System.Windows.Automation.AutomationProperties.SetName(ask, "打开岛上对话"); center.Children.Add(ask);
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
        public event EventHandler NoticeAcknowledged;
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
        private readonly StackPanel taskStack;
        private readonly List<IslandTaskCard> taskCards = new List<IslandTaskCard>();
        private bool tasksMode, noticeVisible;
        private Border assistantCard;
        private LiquidGlassSurface assistantMaterial;
        private double assistantHeight;
        private bool assistantInteractive;

        public IslandWindow(CoreEngine engine, Action<string> navigate, Action<Point> dropped)
        {
            this.engine = engine; this.navigate = navigate;
            this.dropped = dropped;
            LastWorkArea = SystemParameters.WorkArea;
            SurfaceStyle.Setup(this, "浮岛 · 灵动岛"); Width = 420; Height = 98;
            card = new Border { Margin = new Thickness(10), Background = Brushes.Transparent, CornerRadius = new CornerRadius(32) };
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            symbol = new ContentControl { VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center };
            var symbolBacking = new Border { Child = symbol, Padding = new Thickness(4), CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Center };
            symbolBacking.SetValue(LiquidGlass.ReadablePanelProperty, true); grid.Children.Add(symbolBacking); SetIcon("countdown");
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var readablePanel = new Border { Name = "IslandTextBacking", Child = words, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 7, 0), Padding = new Thickness(8, 5, 8, 5), CornerRadius = new CornerRadius(10) };
            readablePanel.SetValue(LiquidGlass.ReadablePanelProperty, true); Grid.SetColumn(readablePanel, 1);
            title = SurfaceStyle.Text("浮岛", 17, "#18243A"); title.TextAlignment = TextAlignment.Left; title.FontWeight = FontWeights.SemiBold; title.TextTrimming = TextTrimming.CharacterEllipsis;
            detail = SurfaceStyle.Text("点击查看 · 拖动调整位置", 13, "#3D4A60"); detail.TextAlignment = TextAlignment.Left; detail.FontWeight = FontWeights.Medium; detail.TextTrimming = TextTrimming.CharacterEllipsis; detail.Margin = new Thickness(0, 3, 0, 0);
            words.Children.Add(title); words.Children.Add(detail); grid.Children.Add(readablePanel);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(actions, 2);
            action = SurfaceStyle.Button("查看", Act, "#EEF1FF"); actions.Children.Add(action);
            var close = SurfaceStyle.Button("", delegate { AcknowledgeNotice(); urgent = false; Collapse(); }, "#F2F4F9"); close.Content = AppVisual.Icon("close", 15, SurfaceStyle.Brush("#596783")); close.Width = 34; close.Margin = new Thickness(5, 0, 0, 0); close.ToolTip = "收起为小黑点，任务继续运行"; actions.Children.Add(close); grid.Children.Add(actions);
            material = new LiquidGlassSurface { Mode = engine.Settings.GlassMode, Radius = 32, Name = "IslandGlassMaterial" };
            var layers = new Grid(); layers.Children.Add(material);
            grid.Margin = new Thickness(16, 10, 12, 10); layers.Children.Add(grid); card.Child = layers;
            LiquidGlass.Track(card, material);
            card.Height = 82;
            taskStack = new StackPanel(); taskStack.Children.Add(card);
            surface = new Grid { Width = 440, Height = 102 }; surface.Children.Add(taskStack); Content = surface;
            hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            hideTimer.Tick += delegate { if (!assistantInteractive && !urgent && !pointerDown && !IsMouseOver && DateTime.UtcNow >= visibleUntil) Collapse(); };
            MouseEnter += delegate { visibleUntil = DateTime.UtcNow.AddSeconds(4); };
            MouseLeave += delegate { if (!urgent) visibleUntil = DateTime.UtcNow.AddSeconds(3); };
            card.Cursor = Cursors.SizeAll;
            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (IsButtonSource(e.OriginalSource as DependencyObject)) return;
                SurfaceStyle.StopPosition(this);
                dragStart = Native.Cursor(this); positionStart = new Point(Left, Top);
                pointerDown = true; dragged = false; CaptureMouse(); LiquidGlass.Press(taskStack, true); e.Handled = true;
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
                LiquidGlass.Press(taskStack, false);
                if (!pointerDown) return;
                pointerDown = false; ReleaseMouseCapture();
                if (dragged && dropped != null) dropped(new Point(Left + Width / 2, Top + Height / 2));
                KeepOpenAfterDrag(); e.Handled = true;
            };
            LostMouseCapture += delegate { pointerDown = false; LiquidGlass.Press(taskStack, false); };
            TouchWindowDrag.Attach(this, delegate(DependencyObject source) { return !IsButtonSource(source); },
                delegate { SurfaceStyle.StopPosition(this); pointerDown = true; LiquidGlass.Press(taskStack, true); },
                KeepOpenAfterDrag,
                delegate(Point point) { pointerDown = false; LiquidGlass.Press(taskStack, false); if (dropped != null) dropped(point); },
                delegate { pointerDown = false; LiquidGlass.Press(taskStack, false); KeepOpenAfterDrag(); });
            SourceInitialized += delegate
            {
                var source = PresentationSource.FromVisual(this) as HwndSource;
                if (source != null) source.AddHook(delegate(IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled)
                { if (message == 0x0021 && !assistantInteractive) { handled = true; return new IntPtr(3); } return IntPtr.Zero; });
                Reposition();
            };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (assistantInteractive && e.Key == Key.Escape) { DismissAssistant(); Collapse(); e.Handled = true; } };
            ApplyScene();
        }
        public void ApplyScene()
        {
            ApplyMaterial();
            Reposition();
        }
        public void ApplyMaterial() { if (materialMode == engine.Settings.GlassMode) return; materialMode = engine.Settings.GlassMode; material.Mode = materialMode; if (assistantMaterial != null) assistantMaterial.Mode = materialMode; foreach (var row in taskCards) row.Material.Mode = materialMode; if (materialMode != 0) card.RenderTransform = Transform.Identity; card.Effect = materialMode == 2 ? new DropShadowEffect { BlurRadius = 15, Opacity = .14, ShadowDepth = 4, Color = Color.FromRgb(35, 51, 83) } : null; }
        private void SetIcon(string name)
        {
            if (iconName == name) return; iconName = name;
            var icon = AppVisual.Icon(name, 28, SurfaceStyle.Brush("#4F66E8"));
            icon.Loaded += delegate { if (material != null) material.RefreshInk(); };
            symbol.Content = icon;
        }
        private static bool IsButtonSource(DependencyObject source)
        {
            while (source != null) { if (source is System.Windows.Controls.Primitives.ButtonBase || source is Slider || source is TextBox || source is ScrollViewer || source is System.Windows.Controls.Primitives.ScrollBar) return true; source = VisualTreeHelper.GetParent(source); }
            return false;
        }
        public void KeepOpenAfterDrag() { visibleUntil = DateTime.UtcNow.AddSeconds(7); }
        public void ShowActivity(string kind)
        {
            ClearAssistant();
            if (engine.GetIslandTasks().Count > 0) { ShowTasks(); return; }
            // A future shutdown is deliberately absent from every floating surface.
            if (kind == "shutdown") return;
            if (urgent && activity == "shutdown" && engine.ShutdownAt.HasValue) return;
            tasksMode = false; noticeVisible = false; ClearTaskCards(); card.Visibility = Visibility.Visible; surface.Height = 102;
            activity = kind; urgent = false; action.Content = kind == "shutdown" ? "取消" : "查看";
            card.ToolTip = null;
            RefreshActivity(); Present(7);
        }
        public void RefreshActivity()
        {
            if (tasksMode) { UpdateTasks(); return; }
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
            ClearAssistant();
            TimeSpan? shutdownLeft = engine.ShutdownRemaining;
            if (notice.Kind == "shutdown" && notice.Urgent && shutdownLeft.HasValue && shutdownLeft.Value > TimeSpan.Zero && shutdownLeft.Value.TotalSeconds <= 10) { ShowTasks(); return; }
            noticeVisible = true; tasksMode = true; card.Visibility = Visibility.Visible;
            activity = notice.Urgent && notice.Kind == "shutdown" && engine.ShutdownAt.HasValue ? "shutdown" : "";
            urgent = notice.Urgent && activity == "shutdown";
            title.Text = notice.Title; detail.Text = notice.Message;
            // Long reminder titles remain accessible in full via hover and the reminders page.
            card.ToolTip = notice.Title + "\n" + notice.Message;
            SetIcon(notice.Kind == "shutdown" ? "shutdown" : notice.Kind == "countdown" ? "countdown" : "reminders");
            action.Content = activity == "shutdown" ? "取消" : "知道了";
            UpdateTasks();
            Present(notice.Kind == "info" ? 6 : 20);
        }
        public void ShowTasks()
        {
            ClearAssistant();
            tasksMode = true; noticeVisible = false; card.Visibility = Visibility.Collapsed;
            UpdateTasks();
            if (taskCards.Count > 0) Present(7);
        }
        private void ClearTaskCards()
        {
            foreach (var row in taskCards) taskStack.Children.Remove(row);
            taskCards.Clear();
        }
        private void UpdateTasks()
        {
            var tasks = engine.GetIslandTasks();
            bool rebuild = tasks.Count != taskCards.Count;
            if (!rebuild) for (int i = 0; i < tasks.Count; i++) if (tasks[i].Kind != taskCards[i].Kind) { rebuild = true; break; }
            if (rebuild)
            {
                ClearTaskCards();
                foreach (var task in tasks)
                {
                    var row = new IslandTaskCard(engine, task, Collapse, delegate { KeepOpenAfterDrag(); RefreshActivity(); });
                    taskCards.Add(row); taskStack.Children.Add(row);
                }
            }
            for (int i = 0; i < tasks.Count; i++) taskCards[i].Update(tasks[i]);
            urgent = tasks.Count > 0 && tasks[0].Kind == "shutdown";
            double height = Math.Max(assistantHeight > 0 ? 0 : 102, (tasks.Count + (noticeVisible ? 1 : 0)) * 102 + assistantHeight);
            if (surface.Height != height) { surface.Height = height; if (!pointerDown) Reposition(); }
            if (tasks.Count == 0 && !noticeVisible && assistantCard == null && IsVisible) Collapse();
        }
        private void ClearAssistant()
        {
            if (assistantCard != null) taskStack.Children.Remove(assistantCard);
            assistantCard = null; assistantMaterial = null; assistantHeight = 0; assistantInteractive = false; ShowActivated = false;
        }
        public void DismissAssistant() { ClearAssistant(); if (tasksMode) UpdateTasks(); }
        internal void ResizeAssistant(double height)
        {
            if (assistantCard == null) return;
            height = Math.Max(300, Math.Min(548, height));
            assistantHeight = height; assistantCard.Height = height - 20; UpdateTasks();
        }
        internal void RefreshAssistantInk() { if (assistantMaterial != null) assistantMaterial.RefreshInk(); }
        public void ShowAssistant(FrameworkElement body, double height, bool interactive = false)
        {
            if (engine.ShutdownRemaining.HasValue && engine.ShutdownRemaining.Value.TotalSeconds <= 30) return;
            ClearAssistant(); tasksMode = true; noticeVisible = false; card.Visibility = Visibility.Collapsed; activity = "";
            assistantInteractive = interactive; ShowActivated = interactive;
            var glass = new LiquidGlassSurface { Mode = engine.Settings.GlassMode, Radius = 28, Name = "AssistantGlassMaterial" }; assistantMaterial = glass;
            var layers = new Grid(); layers.Children.Add(glass);
            var backing = new Border { Child = body, Margin = new Thickness(16), Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(14) };
            backing.SetValue(LiquidGlass.ReadablePanelProperty, !interactive); layers.Children.Add(backing);
            assistantCard = new Border { Child = layers, Margin = new Thickness(10), Height = height - 20, CornerRadius = new CornerRadius(28), Cursor = Cursors.SizeAll };
            LiquidGlass.Track(assistantCard, glass); assistantHeight = height; taskStack.Children.Insert(0, assistantCard);
            UpdateTasks(); Present(18);
            if (interactive) Activate();
        }
        private void Act()
        {
            AcknowledgeNotice();
            if (activity == "shutdown" && engine.ShutdownAt.HasValue) { engine.CancelShutdown(); urgent = false; Collapse(); }
            else if (activity != "") { navigate(activity); Collapse(); }
            else { noticeVisible = false; card.Visibility = Visibility.Collapsed; if (engine.GetIslandTasks().Count > 0) { UpdateTasks(); KeepOpenAfterDrag(); } else { urgent = false; Collapse(); } }
        }
        private void AcknowledgeNotice() { var handler = NoticeAcknowledged; if (handler != null) handler(this, EventArgs.Empty); }
        private void Present(int seconds)
        {
            motionVersion++; collapsing = false; SurfaceStyle.StopPosition(this);
            visibleUntil = DateTime.UtcNow.AddSeconds(seconds); Reposition(); Show(); hideTimer.Start();
            var handler = Expanded; if (handler != null) handler(this, EventArgs.Empty);
            LiquidGlass.Press(taskStack, false);
            if (engine.Settings.GlassMode == 0) { SurfaceStyle.Scale(taskStack, .90, 1, 290); SurfaceStyle.Fade(taskStack, .3, 1, 200, null); }
            else
            {
                taskStack.RenderTransform = Transform.Identity;
                if (engine.Settings.GlassMode == 2) { LiquidGlass.Arrive(taskStack); SurfaceStyle.Fade(taskStack, .3, 1, 220, null); }
                else { taskStack.BeginAnimation(UIElement.OpacityProperty, null); taskStack.Opacity = 1; }
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
            scale = Math.Min(scale, Math.Min(Math.Max(1, work.Width - 8) / 440, Math.Max(1, work.Height - 8) / surface.Height));
            Width = 440 * scale; Height = surface.Height * scale; surface.LayoutTransform = new ScaleTransform(scale, scale);
            double anchor = engine.Settings.IslandAnchor;
            double left = engine.Settings.Placement == IslandPlacement.Top ? work.Left + work.Width * anchor - Width / 2 : engine.Settings.Placement == IslandPlacement.Left ? work.Left + 2 : work.Right - Width - 2;
            double top = engine.Settings.Placement == IslandPlacement.Top ? work.Top + 4 : work.Top + work.Height * anchor - Height / 2;
            Left = Math.Max(work.Left, Math.Min(left, work.Right - Width)); Top = Math.Max(work.Top, Math.Min(top, work.Bottom - Height));
        }
        public void Collapse()
        {
            if (collapsing) return;
            collapsing = true; int version = ++motionVersion; hideTimer.Stop();
            if (engine.Settings.GlassMode == 0) SurfaceStyle.Scale(taskStack, 1, .90, 160);
            else { taskStack.RenderTransform = Transform.Identity; LiquidGlass.Press(taskStack, engine.Settings.GlassMode == 2); }
            SurfaceStyle.Fade(taskStack, taskStack.Opacity, 0, engine.Settings.GlassMode == 1 ? 0 : 140, delegate
            {
                if (version != motionVersion) return;
                LiquidGlass.Press(taskStack, false);
                Hide(); collapsing = false;
                var handler = Collapsed; if (handler != null) handler(this, EventArgs.Empty);
            });
        }
        private static string Format(TimeSpan value) { if (value < TimeSpan.Zero) value = TimeSpan.Zero; return ((int)value.TotalHours).ToString("00") + ":" + value.Minutes.ToString("00") + ":" + value.Seconds.ToString("00"); }
    }
}
