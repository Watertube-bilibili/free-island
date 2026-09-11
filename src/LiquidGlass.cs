using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FreeIsland
{
    /// <summary>Vector optical material. It does not capture or blur the desktop.</summary>
    public sealed class LiquidGlassSurface : FrameworkElement
    {
        public static readonly DependencyProperty ModeProperty = DependencyProperty.Register("Mode", typeof(int), typeof(LiquidGlassSurface), new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty OrbProperty = DependencyProperty.Register("Orb", typeof(bool), typeof(LiquidGlassSurface), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register("Radius", typeof(double), typeof(LiquidGlassSurface), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty PressedProperty = DependencyProperty.Register("Pressed", typeof(bool), typeof(LiquidGlassSurface), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
        public int Mode { get { return (int)GetValue(ModeProperty); } set { SetValue(ModeProperty, value); pointer = null; } }
        public bool Orb { get { return (bool)GetValue(OrbProperty); } set { SetValue(OrbProperty, value); } }
        public double Radius { get { return (double)GetValue(RadiusProperty); } set { SetValue(RadiusProperty, value); } }
        public bool Pressed { get { return (bool)GetValue(PressedProperty); } set { SetValue(PressedProperty, value); } }
        private Point? pointer;

        private static readonly Brush LitePaper = Gradient(Color.FromArgb(246, 255, 255, 255), Color.FromArgb(232, 228, 235, 249));
        private static readonly Brush StandardPaper = Gradient(Color.FromArgb(241, 255, 255, 255), Color.FromArgb(221, 222, 232, 249));
        private static readonly Brush LiteBlue = Gradient(Color.FromArgb(247, 115, 143, 250), Color.FromArgb(241, 62, 83, 211));
        private static readonly Brush StandardBlue = Gradient(Color.FromArgb(237, 133, 163, 255), Color.FromArgb(234, 57, 78, 203));
        private static readonly Brush Edge = Gradient(Color.FromArgb(232, 255, 255, 255), Color.FromArgb(133, 120, 143, 183));
        private static readonly Brush InnerEdge = Gradient(Color.FromArgb(165, 255, 255, 255), Color.FromArgb(85, 111, 142, 207));
        private static readonly Brush Reflection = Gradient(Color.FromArgb(151, 255, 255, 255), Color.FromArgb(0, 255, 255, 255));
        private static readonly Brush Caustic = Radial(Color.FromArgb(40, 107, 144, 246), Color.FromArgb(0, 133, 171, 255));
        private static readonly Brush Highlight = Radial(Color.FromArgb(165, 255, 255, 255), Color.FromArgb(0, 255, 255, 255));
        private static readonly Brush PressShade = Solid(Color.FromArgb(20, 38, 67, 124));
        private static readonly Brush Paper = Solid(Color.FromRgb(241, 243, 250));
        private static readonly Brush Cobalt = Solid(Color.FromRgb(79, 102, 232));

        public LiquidGlassSurface() { IsHitTestVisible = false; SnapsToDevicePixels = false; }

        internal void Pointer(Point? point)
        {
            // Changes only come from pointer input. Resting surfaces have no render timer.
            if (Mode != 2 || SurfaceStyle.SnapshotMode) return;
            if (point.HasValue && pointer.HasValue && (point.Value - pointer.Value).LengthSquared < 2.25) return;
            if (!point.HasValue && !pointer.HasValue) return;
            pointer = point; InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            double w = ActualWidth, h = ActualHeight;
            if (w < 2 || h < 2) return;
            var bounds = new Rect(.75, .75, w - 1.5, h - 1.5);
            double radius = Orb ? Math.Min(w, h) / 2 : Math.Min(Radius, Math.Min(w, h) / 2);
            int mode = Mode >= 0 && Mode <= 2 ? Mode : 1;
            if (mode == 0)
            {
                drawing.DrawRoundedRectangle(Orb ? Cobalt : Paper, null, bounds, radius, radius);
                if (Pressed) drawing.DrawRoundedRectangle(PressShade, null, bounds, radius, radius);
                return;
            }
            Brush fill = Orb ? (mode == 1 ? LiteBlue : StandardBlue) : (mode == 1 ? LitePaper : StandardPaper);
            drawing.DrawRoundedRectangle(fill, new Pen(Edge, 1), bounds, radius, radius);
            if (mode == 2)
            {
                var clip = new RectangleGeometry(bounds, radius, radius);
                drawing.PushClip(clip);
                // A curved upper reflection and a lower cool caustic imply lens thickness.
                drawing.DrawEllipse(Reflection, null, new Point(w * .36, -h * .04), w * .71, h * .59);
                drawing.DrawEllipse(Caustic, null, new Point(w * .71, h * 1.05), w * .71, h * .43);
                if (w > 7 && h > 7)
                    drawing.DrawRoundedRectangle(null, new Pen(InnerEdge, .8), new Rect(2.5, 2.5, w - 5, h - 5), Math.Max(0, radius - 2), Math.Max(0, radius - 2));
                if (pointer.HasValue)
                {
                    double glow = Math.Min(Orb ? w * .67 : 80, Math.Max(24, h * .95));
                    drawing.DrawEllipse(Highlight, null, pointer.Value, glow, glow * .75);
                }
                drawing.Pop();
            }
            if (Pressed) drawing.DrawRoundedRectangle(PressShade, null, bounds, radius, radius);
        }

        private static Brush Solid(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
        private static Brush Gradient(Color top, Color bottom)
        {
            var brush = new LinearGradientBrush(top, bottom, new Point(.18, 0), new Point(.85, 1)); brush.Freeze(); return brush;
        }
        private static Brush Radial(Color center, Color edge)
        {
            var brush = new RadialGradientBrush(center, edge) { Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .5, RadiusY = .5 };
            brush.Freeze(); return brush;
        }
    }

    internal static class LiquidGlass
    {
        private static readonly DependencyProperty PointerWiredProperty = DependencyProperty.RegisterAttached("PointerWired", typeof(bool), typeof(LiquidGlass), new PropertyMetadata(false));

        public static Grid Orb(double size, int mode)
        {
            var grid = new Grid { Width = size, Height = size, Background = Brushes.Transparent };
            var material = new LiquidGlassSurface { Orb = true, Mode = mode, Name = "OrbGlassMaterial" };
            grid.Children.Add(material);
            var glyph = new Canvas { Width = size, Height = size, IsHitTestVisible = false };
            double unit = size / 24;
            var island = new Rectangle { Width = 14 * unit, Height = 5 * unit, RadiusX = 2.5 * unit, RadiusY = 2.5 * unit, Fill = Brushes.White };
            Canvas.SetLeft(island, 5 * unit); Canvas.SetTop(island, 12.5 * unit); glyph.Children.Add(island);
            var sky = new Rectangle { Width = 7 * unit, Height = 3 * unit, RadiusX = 1.5 * unit, RadiusY = 1.5 * unit, Fill = SurfaceStyle.Brush("#B9CDFF") };
            Canvas.SetLeft(sky, 10 * unit); Canvas.SetTop(sky, 6.5 * unit); glyph.Children.Add(sky);
            grid.Children.Add(glyph); Track(grid, material); return grid;
        }

        public static void Track(FrameworkElement host, LiquidGlassSurface material)
        {
            host.MouseMove += delegate(object sender, MouseEventArgs e) { material.Pointer(e.GetPosition(material)); };
            host.MouseLeave += delegate { material.Pointer(null); };
            host.TouchMove += delegate(object sender, TouchEventArgs e) { material.Pointer(e.GetTouchPoint(material).Position); };
            host.TouchLeave += delegate { material.Pointer(null); };
            host.TouchUp += delegate { material.Pointer(null); };
            host.IsVisibleChanged += delegate { if (!host.IsVisible) material.Pointer(null); };
        }

        public static void Button(Button button, int mode)
        {
            var template = new ControlTemplate(typeof(Button));
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
            var material = new FrameworkElementFactory(typeof(LiquidGlassSurface)); material.Name = "GlassMaterial";
            material.SetValue(LiquidGlassSurface.ModeProperty, mode); material.SetValue(LiquidGlassSurface.RadiusProperty, 12.0); grid.AppendChild(material);
            var focus = new FrameworkElementFactory(typeof(Border)); focus.Name = "KeyboardFocus";
            focus.SetValue(Border.MarginProperty, new Thickness(3)); focus.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
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
            if ((bool)button.GetValue(PointerWiredProperty)) return;
            button.SetValue(PointerWiredProperty, true);
            Action<Point?> pointer = delegate(Point? point)
            {
                var surface = button.Template.FindName("GlassMaterial", button) as LiquidGlassSurface;
                if (surface != null) surface.Pointer(point);
            };
            button.MouseMove += delegate(object sender, MouseEventArgs e) { pointer(e.GetPosition(button)); };
            button.MouseLeave += delegate { pointer(null); };
            button.TouchMove += delegate(object sender, TouchEventArgs e) { pointer(e.GetTouchPoint(button).Position); };
            button.TouchUp += delegate { pointer(null); };
            button.IsVisibleChanged += delegate { if (!button.IsVisible) pointer(null); };
        }
    }
}
