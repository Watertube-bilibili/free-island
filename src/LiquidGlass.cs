using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FreeIsland
{
    /// <summary>A clear convex water lens; only the material deforms, never its labels.</summary>
    public sealed class LiquidGlassSurface : FrameworkElement
    {
        public static readonly DependencyProperty ModeProperty = DependencyProperty.Register("Mode", typeof(int), typeof(LiquidGlassSurface), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsRender, Changed));
        public static readonly DependencyProperty OrbProperty = DependencyProperty.Register("Orb", typeof(bool), typeof(LiquidGlassSurface), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register("Radius", typeof(double), typeof(LiquidGlassSurface), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty PressedProperty = DependencyProperty.Register("Pressed", typeof(bool), typeof(LiquidGlassSurface), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, Changed));
        public static readonly DependencyProperty CompactProperty = DependencyProperty.Register("Compact", typeof(bool), typeof(LiquidGlassSurface), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
        public int Mode { get { return (int)GetValue(ModeProperty); } set { SetValue(ModeProperty, value); } }
        public bool Orb { get { return (bool)GetValue(OrbProperty); } set { SetValue(OrbProperty, value); } }
        public double Radius { get { return (double)GetValue(RadiusProperty); } set { SetValue(RadiusProperty, value); } }
        public bool Pressed { get { return (bool)GetValue(PressedProperty); } set { SetValue(PressedProperty, value); } }
        public bool Compact { get { return (bool)GetValue(CompactProperty); } set { SetValue(CompactProperty, value); } }
        private readonly DispatcherTimer timer;
        private readonly WaterSpring pressure = new WaterSpring(), pullX = new WaterSpring(), pullY = new WaterSpring();
        private Window captureWindow;
        private WaterLens lens;
        private WriteableBitmap image;
        private BackdropFrame backdrop;
        private Rect screenBounds;
        private Point? previousOrigin;
        private DateTime lastTick, lastCapture, inkSince;
        private bool lightInk, candidateInk, registered, moving;
        private uint previousHash;
        internal FrameworkElement InkHost;
        // Tests provide a known slide fixture; production never assigns this delegate.
        internal static Func<Rect, BackdropFrame> PreviewBackdrop = null;
        internal bool HasRefraction { get { return image != null && backdrop != null; } }
        internal bool IsUpdating { get { return timer.IsEnabled; } }
        internal void RefreshInk() { if (InkHost != null) LiquidGlass.Ink(InkHost, Mode, lightInk && Capture); }
        private bool Motion { get { return Mode == 2 && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast && !SurfaceStyle.SnapshotMode; } }
        private bool Capture { get { return Mode == 2 && !SystemParameters.HighContrast && (!SurfaceStyle.SnapshotMode || PreviewBackdrop != null); } }

        public LiquidGlassSurface()
        {
            IsHitTestVisible = false;
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
            timer.Tick += Tick;
            Loaded += delegate { LiquidGlass.Surfaces.Add(this); SystemParameters.StaticPropertyChanged += SystemChanged; Refresh(); };
            Unloaded += delegate { LiquidGlass.Surfaces.Remove(this); SystemParameters.StaticPropertyChanged -= SystemChanged; Stop(); };
            IsVisibleChanged += delegate { Refresh(); };
            SizeChanged += delegate { image = null; lens = null; lastCapture = DateTime.MinValue; Refresh(); };
        }
        internal void SettingsChanged()
        {
            lens = null; image = null;
            if (backdrop != null) { RenderLens(); UpdateInk(); }
            InvalidateVisual();
        }
        private void SystemChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "HighContrast" && e.PropertyName != "ClientAreaAnimation") return;
            if (!Motion) { pressure.Value = pressure.Velocity = 0; pullX.Value = pullX.Target = pullX.Velocity = pullY.Value = pullY.Target = pullY.Velocity = 0; moving = false; }
            backdrop = null; image = null; lastCapture = DateTime.MinValue; Refresh();
        }
        private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            var surface = (LiquidGlassSurface)sender;
            if (e.Property == ModeProperty)
            {
                surface.backdrop = null; surface.image = null; surface.previousHash = 0; surface.lastCapture = DateTime.MinValue;
                if (surface.Mode != 2) { surface.pressure.Value = surface.pressure.Velocity = 0; surface.pullX.Value = surface.pullX.Target = surface.pullX.Velocity = surface.pullY.Value = surface.pullY.Target = surface.pullY.Velocity = 0; surface.moving = false; surface.previousOrigin = null; }
            }
            surface.pressure.Target = surface.Pressed ? 1 : 0;
            surface.Refresh();
        }
        private void Refresh()
        {
            if (!IsLoaded || !IsVisible) { Stop(); return; }
            if (Capture && PreviewBackdrop == null && !registered)
            {
                captureWindow = Window.GetWindow(this);
                registered = LiquidGlass.Register(captureWindow, this);
            }
            else if (!Capture && registered) { LiquidGlass.Unregister(captureWindow, this); registered = false; }
            if (!Capture) { backdrop = null; image = null; }
            if (InkHost != null) LiquidGlass.Ink(InkHost, Mode, lightInk && Capture);
            if (Capture || Motion && (Pressed || pressure.Value != 0 || moving))
            {
                lastTick = DateTime.UtcNow; timer.Interval = TimeSpan.FromMilliseconds(33); timer.Start();
            }
            else timer.Stop();
            InvalidateVisual();
        }
        private void Stop()
        {
            timer.Stop();
            if (registered) { LiquidGlass.Unregister(captureWindow, this); registered = false; }
            captureWindow = null; backdrop = null; image = null; previousOrigin = null;
            pressure.Value = pressure.Target = pressure.Velocity = 0;
            pullX.Value = pullX.Target = pullX.Velocity = pullY.Value = pullY.Target = pullY.Velocity = 0;
        }
        internal void Pointer(Point? point)
        {
            if (!Motion || !Pressed && point.HasValue) return;
            if (point.HasValue && Pressed)
            {
                pullX.Target = WaterLens.Clamp((point.Value.X / Math.Max(1, ActualWidth) - .5) * .65, -.4, .4);
                pullY.Target = WaterLens.Clamp((point.Value.Y / Math.Max(1, ActualHeight) - .5) * .65, -.4, .4);
            }
            else if (!point.HasValue) { pullX.Target = pullY.Target = 0; }
            moving = true;
            if (IsLoaded && IsVisible) { timer.Interval = TimeSpan.FromMilliseconds(33); timer.Start(); }
        }
        internal void Arrive()
        {
            if (!Motion) return;
            pressure.Value = .95; pressure.Target = 0; pressure.Velocity = -1.5;
            moving = true; Refresh();
        }
        private void Tick(object sender, EventArgs args)
        {
            if (!IsVisible || !IsLoaded) { Stop(); return; }
            if (!Capture && registered) { Refresh(); return; }
            DateTime now = DateTime.UtcNow;
            double dt = Math.Min(.04, Math.Max(.001, (now - lastTick).TotalSeconds)); lastTick = now;
            bool dirty = false;
            try
            {
                Point origin = PointToScreen(new Point()), end = PointToScreen(new Point(ActualWidth, ActualHeight));
                screenBounds = new Rect(origin, end);
                if (previousOrigin.HasValue && Motion)
                {
                    Vector delta = origin - previousOrigin.Value;
                    if (delta.Length > .5) { pullX.Target = WaterLens.Clamp(delta.X / 24, -1, 1); pullY.Target = WaterLens.Clamp(delta.Y / 24, -1, 1); }
                    else { pullX.Target *= .55; pullY.Target *= .55; }
                }
                previousOrigin = origin;
                bool wasMoving = moving;
                moving = Motion && (pressure.Advance(dt) | pullX.Advance(dt) | pullY.Advance(dt)); dirty = moving || wasMoving;
                if (Capture && (now - lastCapture).TotalMilliseconds >= (moving || Pressed ? 30 : 100))
                {
                    lastCapture = now;
                    BackdropFrame frame = null;
                    bool captured = PreviewBackdrop != null ? (frame = PreviewBackdrop(screenBounds)) != null :
                        registered && DesktopBackdrop.TryCapture(captureWindow, screenBounds, out frame);
                    if (captured && frame.Width >= 2 && frame.Height >= 2)
                    {
                        uint hash = 2166136261;
                        for (int i = 0; i < frame.Pixels.Length; i += 19) hash = unchecked((hash ^ frame.Pixels[i]) * 16777619);
                        dirty |= backdrop == null || hash != previousHash || frame.ScreenBounds != backdrop.ScreenBounds;
                        previousHash = hash; backdrop = frame;
                    }
                    else if (backdrop != null) { backdrop = null; image = null; dirty = true; }
                }
                if (dirty || image == null && backdrop != null)
                {
                    RenderLens(); InvalidateVisual();
                }
                if (lens != null && backdrop != null) UpdateInk();
            }
            catch (InvalidOperationException) { backdrop = null; image = null; InvalidateVisual(); }
            timer.Interval = TimeSpan.FromMilliseconds(moving || Pressed ? 33 : 100);
            if (!Capture && !moving && !Pressed) timer.Stop();
        }
        private void RenderLens()
        {
            if (backdrop == null || ActualWidth <= 0 || ActualHeight <= 0 || !Compact && (ActualWidth < 2 || ActualHeight < 2)) return;
            // Logical-resolution optics keeps large touch displays inexpensive. WPF
            // scales this material; the labels remain native vector text at full DPI.
            double scale = Compact ? Math.Max(4, 16 / Math.Min(ActualWidth, ActualHeight)) : Math.Min(1.5, Math.Min(640 / ActualWidth, 180 / ActualHeight));
            int w = Math.Max(2, (int)Math.Ceiling(ActualWidth * scale)), h = Math.Max(2, (int)Math.Ceiling(ActualHeight * scale));
            if (lens == null || lens.Width != w || lens.Height != h)
            {
                lens = new WaterLens(w, h); image = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
            }
            if (image == null) image = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
            lens.Shape((Orb ? Math.Min(ActualWidth, ActualHeight) / 2 : Radius) * scale, pressure.Value, pullX.Value, pullY.Value, Compact, LiquidGlass.Refraction);
            lens.Refract(backdrop.Pixels, backdrop.Width, backdrop.Height, backdrop.Stride,
                screenBounds.Left - backdrop.ScreenBounds.Left, screenBounds.Top - backdrop.ScreenBounds.Top, screenBounds.Width / w, screenBounds.Height / h,
                LiquidGlass.Transparency, LiquidGlass.Highlight);
            image.WritePixels(new Int32Rect(0, 0, w, h), lens.Pixels, w * 4, 0);
        }
        private void UpdateInk()
        {
            bool desired = lightInk ? lens.MeanBrightness < .57 : lens.MeanBrightness < .38;
            if (desired != candidateInk) { candidateInk = desired; inkSince = DateTime.UtcNow; }
            if (desired != lightInk && (DateTime.UtcNow - inkSince).TotalMilliseconds >= 300)
            { lightInk = desired; if (InkHost != null) LiquidGlass.Ink(InkHost, Mode, lightInk); }
        }
        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            double w = ActualWidth, h = ActualHeight;
            if (Compact) { RenderCompact(drawing, w, h); return; }
            if (w < 2 || h < 2) return;
            if (Mode == 2 && image != null && backdrop != null && !SystemParameters.HighContrast)
            { drawing.DrawImage(image, new Rect(0, 0, w, h)); return; }
            double inset = Mode == 0 ? .75 : Math.Max(2, Math.Min(w, h) * .045);
            var bounds = new Rect(inset, inset, Math.Max(0, w - inset * 2), Math.Max(0, h - inset * 2));
            double radius = Orb ? Math.Min(bounds.Width, bounds.Height) / 2 : Math.Min(Radius, bounds.Height / 2);
            if (Mode == 0 || SystemParameters.HighContrast)
            {
                drawing.DrawRoundedRectangle(SystemParameters.HighContrast ? SystemColors.WindowBrush : Orb ? SurfaceStyle.Brush("#4F66E8") : SurfaceStyle.Brush("#F1F3FA"), null, bounds, radius, radius); return;
            }
            // Lightweight mode uses actual window transparency. No white disk,
            // animated texture, blur or desktop sampling is hidden behind this rim.
            var transform = new ScaleTransform(1 + pressure.Value * .025, 1 - pressure.Value * .037, w / 2, h / 2);
            drawing.PushTransform(transform);
            var edge = new LinearGradientBrush(Color.FromArgb(LiquidGlass.EdgeAlpha(218), 255, 255, 255), Color.FromArgb(LiquidGlass.EdgeAlpha(105), 28, 35, 42), new Point(.15, 0), new Point(.82, 1));
            drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(LiquidGlass.FillAlpha, 240, 245, 249)), new Pen(edge, .7 + .4 * LiquidGlass.Refraction), bounds, radius, radius);
            bounds.Inflate(-1.7, -1.7);
            if (bounds.Width > 0 && bounds.Height > 0) drawing.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(LiquidGlass.EdgeAlpha(65), 255, 255, 255)), .65), bounds, Math.Max(0, radius - 1.7), Math.Max(0, radius - 1.7));
            drawing.Pop();
        }

        private void RenderCompact(DrawingContext drawing, double w, double h)
        {
            if (w <= 0 || h <= 0) return;
            // Supersampled optics and a single fine rim keep a 3 px drop visible.
            // All paint stays within the user's diameter, not the larger hit area.
            drawing.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
            if (Mode == 0 || SystemParameters.HighContrast)
                drawing.DrawEllipse(SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brushes.Black, null, new Point(w / 2, h / 2), w / 2, h / 2);
            else
            {
                bool refracted = Mode == 2 && image != null && backdrop != null;
                if (refracted) drawing.DrawImage(image, new Rect(0, 0, w, h));
                double stroke = Math.Min(.65, Math.Min(w, h) * .14);
                double sx = 1 + pressure.Value * .025, sy = 1 - pressure.Value * .037;
                double rx = Math.Max(0, (w - stroke) * .5 * sx), ry = Math.Max(0, (h - stroke) * .5 * sy);
                var edge = new LinearGradientBrush(Color.FromArgb(Math.Max((byte)90, LiquidGlass.EdgeAlpha(240)), 255, 255, 255), Color.FromArgb(Math.Max((byte)90, LiquidGlass.EdgeAlpha(195)), 23, 34, 45), new Point(.1, 0), new Point(.85, 1));
                drawing.DrawEllipse(refracted ? null : new SolidColorBrush(Color.FromArgb(LiquidGlass.FillAlpha, 238, 245, 250)), new Pen(edge, stroke), new Point(w / 2, h / 2), rx, ry);
            }
            drawing.Pop();
        }
    }

    internal static class LiquidGlass
    {
        internal static readonly HashSet<LiquidGlassSurface> Surfaces = new HashSet<LiquidGlassSurface>();
        internal static double Refraction = 1, Transparency = .65, Highlight = 1;
        internal static byte FillAlpha { get { return (byte)Math.Round(255 * Math.Pow(1 - Transparency, 2)); } }
        internal static byte EdgeAlpha(int original) { return (byte)Math.Min(255, Math.Max(0, original * Highlight)); }
        internal static void Configure(AppSettings settings)
        {
            if (settings == null) return;
            double bend = Math.Max(0, Math.Min(100, settings.GlassRefraction)) / 50.0;
            double transmission = Math.Max(0, Math.Min(100, settings.GlassTransparency)) / 100.0;
            double rim = Math.Max(0, Math.Min(100, settings.GlassHighlight)) / 55.0;
            if (bend == Refraction && transmission == Transparency && rim == Highlight) return;
            Refraction = bend; Transparency = transmission; Highlight = rim;
            foreach (var surface in new List<LiquidGlassSurface>(Surfaces)) surface.SettingsChanged();
        }
        internal static readonly DependencyProperty ReadablePanelProperty = DependencyProperty.RegisterAttached("ReadablePanel", typeof(bool), typeof(LiquidGlass), new PropertyMetadata(false));
        private static readonly DependencyProperty PointerWiredProperty = DependencyProperty.RegisterAttached("PointerWired", typeof(bool), typeof(LiquidGlass), new PropertyMetadata(false));
        private static readonly DependencyProperty OriginalInkProperty = DependencyProperty.RegisterAttached("OriginalInk", typeof(Brush), typeof(LiquidGlass));
        private static readonly Dictionary<Window, HashSet<LiquidGlassSurface>> Windows = new Dictionary<Window, HashSet<LiquidGlassSurface>>();
        internal static bool Register(Window window, LiquidGlassSurface surface)
        {
            if (window == null) return false;
            HashSet<LiquidGlassSurface> surfaces;
            if (!Windows.TryGetValue(window, out surfaces)) { surfaces = new HashSet<LiquidGlassSurface>(); Windows.Add(window, surfaces); }
            surfaces.Add(surface); DesktopBackdrop.SetEnabled(window, true); return true;
        }
        internal static void Unregister(Window window, LiquidGlassSurface surface)
        {
            HashSet<LiquidGlassSurface> surfaces;
            if (window == null || !Windows.TryGetValue(window, out surfaces)) return;
            surfaces.Remove(surface);
            if (surfaces.Count == 0) { Windows.Remove(window); DesktopBackdrop.SetEnabled(window, false); }
        }
        internal static bool IsWaterButton(Button button) { return (bool)button.GetValue(PointerWiredProperty); }
        internal static void Ink(DependencyObject host, int mode, bool light)
        { InkCore(host, mode, light, false); }
        private static void InkCore(DependencyObject host, int mode, bool light, bool readable)
        {
            // Local counter-colour halos protect labels on mixed slides without
            // inserting an opaque card inside the water surface.
            var panel = host as Border;
            if (panel != null && (bool)panel.GetValue(ReadablePanelProperty))
            {
                readable = true;
                panel.Background = mode == 0 ? Brushes.Transparent : SystemParameters.HighContrast ? SystemColors.WindowBrush :
                    new SolidColorBrush(light ? Color.FromArgb(205, 15, 22, 31) : Color.FromArgb(228, 248, 250, 252));
            }
            var text = host as TextBlock; var shape = host as Shape;
            if (text != null || shape != null && !(shape is Rectangle))
            {
                DependencyProperty property = text != null ? TextBlock.ForegroundProperty : Shape.StrokeProperty;
                var original = host.GetValue(OriginalInkProperty) as Brush;
                if (original == null) { original = host.GetValue(property) as Brush; if (original != null) host.SetValue(OriginalInkProperty, original); }
                host.SetValue(property, SystemParameters.HighContrast ? SystemColors.WindowTextBrush : mode == 0 ? original : light ? Brushes.White : SurfaceStyle.Brush("#132133"));
                if (text != null) text.Background = !readable && mode == 1 && !SystemParameters.HighContrast ? new SolidColorBrush(Color.FromArgb(218, 246, 249, 251)) : Brushes.Transparent;
                var element = (UIElement)host;
                element.Effect = readable || mode == 0 || SystemParameters.HighContrast ? null : new DropShadowEffect { Color = light ? Colors.Black : Colors.White, BlurRadius = mode == 1 ? 1 : 3, ShadowDepth = 0, Opacity = 1 };
            }
            // Solid nested action buttons retain their own high-contrast surface.
            if (host is Button && !IsWaterButton((Button)host)) return;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(host); i++) InkCore(VisualTreeHelper.GetChild(host, i), mode, light, readable);
        }
        public static Grid Orb(double size, int mode)
        {
            var grid = new Grid { Width = size, Height = size, Background = Brushes.Transparent };
            var material = new LiquidGlassSurface { Orb = true, Mode = mode, Name = "OrbGlassMaterial" }; grid.Children.Add(material);
            var glyph = AppVisual.Icon("home", size * .43, mode == 0 ? Brushes.White : SurfaceStyle.Brush("#132133"));
            glyph.IsHitTestVisible = false; grid.Children.Add(glyph); Track(grid, material); return grid;
        }
        public static void Track(FrameworkElement host, LiquidGlassSurface material)
        {
            material.InkHost = host;
            host.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(delegate { material.Pressed = true; }), true);
            host.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(delegate { material.Pressed = false; material.Pointer(null); }), true);
            host.AddHandler(UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(delegate { material.Pressed = true; }), true);
            host.AddHandler(UIElement.PreviewTouchUpEvent, new EventHandler<TouchEventArgs>(delegate { material.Pressed = false; material.Pointer(null); }), true);
            host.MouseMove += delegate(object sender, MouseEventArgs e) { material.Pointer(e.GetPosition(material)); };
            host.MouseLeave += delegate { material.Pointer(null); };
            host.TouchMove += delegate(object sender, TouchEventArgs e) { material.Pointer(e.GetTouchPoint(material).Position); };
            host.LostMouseCapture += delegate { material.Pressed = false; };
            host.LostTouchCapture += delegate { material.Pressed = false; };
        }
        internal static void Arrive(DependencyObject host)
        {
            var material = host as LiquidGlassSurface;
            if (material != null) { material.Arrive(); return; }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(host); i++) Arrive(VisualTreeHelper.GetChild(host, i));
        }
        internal static void Press(DependencyObject host, bool pressed)
        {
            var material = host as LiquidGlassSurface;
            if (material != null) { material.Pressed = pressed; if (!pressed) material.Pointer(null); return; }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(host); i++) Press(VisualTreeHelper.GetChild(host, i), pressed);
        }
        public static void Button(Button button, int mode)
        {
            var template = new ControlTemplate(typeof(Button));
            var grid = new FrameworkElementFactory(typeof(Grid)); grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
            var material = new FrameworkElementFactory(typeof(LiquidGlassSurface)); material.Name = "GlassMaterial";
            material.SetValue(LiquidGlassSurface.ModeProperty, mode); material.SetValue(LiquidGlassSurface.RadiusProperty, 18.0); grid.AppendChild(material);
            var focus = new FrameworkElementFactory(typeof(Border)); focus.Name = "KeyboardFocus";
            focus.SetValue(Border.MarginProperty, new Thickness(4)); focus.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            focus.SetValue(Border.BorderThicknessProperty, new Thickness(2)); focus.SetValue(Border.BorderBrushProperty, Brushes.Transparent); focus.SetValue(UIElement.IsHitTestVisibleProperty, false); grid.AppendChild(focus);
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 5, 8, 5)); grid.AppendChild(presenter); template.VisualTree = grid;
            var selected = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            selected.Setters.Add(new Setter(Border.BorderBrushProperty, SurfaceStyle.Brush("#263DAF"), "KeyboardFocus")); template.Triggers.Add(selected);
            var press = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            press.Setters.Add(new Setter(LiquidGlassSurface.PressedProperty, true, "GlassMaterial")); template.Triggers.Add(press);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .47)); template.Triggers.Add(disabled);
            button.Template = template;
            button.ApplyTemplate();
            var surface = button.Template.FindName("GlassMaterial", button) as LiquidGlassSurface;
            if (surface != null) surface.InkHost = button;
            if ((bool)button.GetValue(PointerWiredProperty)) return;
            button.SetValue(PointerWiredProperty, true);
            Action<Point?> pointer = delegate(Point? point) { var s = button.Template.FindName("GlassMaterial", button) as LiquidGlassSurface; if (s != null) s.Pointer(point); };
            button.MouseMove += delegate(object sender, MouseEventArgs e) { pointer(e.GetPosition(button)); };
            button.MouseLeave += delegate { pointer(null); };
            button.TouchMove += delegate(object sender, TouchEventArgs e) { pointer(e.GetTouchPoint(button).Position); };
            button.TouchUp += delegate { pointer(null); };
        }
    }
}
