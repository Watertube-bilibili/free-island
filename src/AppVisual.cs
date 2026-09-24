using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FreeIsland
{
    /// <summary>One outline icon vocabulary, drawn in a normalized 24-unit canvas.</summary>
    public static class AppVisual
    {
        private static readonly Brush Cobalt = FrozenBrush(79, 102, 232);
        private static readonly Brush Sky = FrozenBrush(185, 205, 255);
        private static readonly Dictionary<string, string> Paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "home", "M3,10.5 L12,3 L21,10.5 M5.5,9 V20 H9.5 V14 H14.5 V20 H18.5 V9" },
            { "assistant", "M8,4 H16 A4,4 0 0 1 20,8 V15 A4,4 0 0 1 16,19 H11 L6,22 V18 A4,4 0 0 1 4,14 V8 A4,4 0 0 1 8,4 M8,10 V12 M16,10 V12 M9,15 H15" },
            { "stopwatch", "M9,2 H15 M12,2 V5 M17.6,7.4 L19.5,5.5 M18.4,4.4 L20.6,6.6 M19.5,13.5 A7.5,7.5 0 1 1 4.5,13.5 A7.5,7.5 0 1 1 19.5,13.5 M12,9 V13.5 L15,15.5" },
            { "countdown", "M4.6,6.7 A8.4,8.4 0 1 1 3.8,15.4 M4.6,2.8 V6.7 H8.5 M12,7 V12 L15.5,14" },
            { "reminders", "M8,2.5 V6 M16,2.5 V6 M20,10 V6 A2,2 0 0 0 18,4 H6 A2,2 0 0 0 4,6 V18 A2,2 0 0 0 6,20 H10 M4,9 H20 M14,18 V14.5 A3,3 0 0 1 20,14.5 V18 L21,19 H13 Z M16,21 H18" },
            { "shutdown", "M12,2 V11 M6.5,5.3 A8.5,8.5 0 1 0 17.5,5.3" },
            { "desktop", "M4,4 H20 A1.5,1.5 0 0 1 21.5,5.5 V15 A1.5,1.5 0 0 1 20,16.5 H4 A1.5,1.5 0 0 1 2.5,15 V5.5 A1.5,1.5 0 0 1 4,4 Z M12,16.5 V21 M8,21 H16" },
            { "classroom", "M3,4 H21 M5,4 V16 H19 V4 M12,16 V21 M7.5,21 L12,18 L16.5,21 M8,8 H16 M8,11.5 H13" },
            { "expand", "M8,3 H3 V8 M16,3 H21 V8 M21,16 V21 H16 M8,21 H3 V16" },
            { "close", "M6,6 L18,18 M18,6 L6,18" },
            { "minimize", "M5,12 H19" },
            { "maximize", "M5,4.5 H19 A.5,.5 0 0 1 19.5,5 V19 A.5,.5 0 0 1 19,19.5 H5 A.5,.5 0 0 1 4.5,19 V5 A.5,.5 0 0 1 5,4.5 Z" },
            { "play", "M8,4.5 L19,12 L8,19.5 Z" },
            { "pause", "M8.5,5 V19 M15.5,5 V19" },
            { "stop", "M6.5,5.5 H17.5 A1,1 0 0 1 18.5,6.5 V17.5 A1,1 0 0 1 17.5,18.5 H6.5 A1,1 0 0 1 5.5,17.5 V6.5 A1,1 0 0 1 6.5,5.5 Z" },
            { "reset", "M4.5,6.7 A8.3,8.3 0 1 1 3.8,15 M4.5,2.8 V6.7 H8.4" },
            { "arrowleft", "M14.5,5 L7.5,12 L14.5,19" },
            { "arrowright", "M9.5,5 L16.5,12 L9.5,19" },
            { "arrowdown", "M5,9 L12,16 L19,9" },
            { "arrowup", "M5,15 L12,8 L19,15" },
            { "check", "M4.5,12.5 L9.5,17.5 L19.5,6.5" },
            { "plus", "M12,4 V20 M4,12 H20" },
            { "trash", "M3.5,6 H20.5 M8.5,6 V3 H15.5 V6 M5.5,6 L6.5,21 H17.5 L18.5,6 M9.5,10 V17 M14.5,10 V17" },
            { "sun", "M16.5,12 A4.5,4.5 0 1 1 7.5,12 A4.5,4.5 0 1 1 16.5,12 M12,1.5 V3.5 M12,20.5 V22.5 M1.5,12 H3.5 M20.5,12 H22.5 M4.6,4.6 L6,6 M18,18 L19.4,19.4 M4.6,19.4 L6,18 M18,6 L19.4,4.6" },
            { "top", "M5,3.5 H19 A1.5,1.5 0 0 1 20.5,5 V19 A1.5,1.5 0 0 1 19,20.5 H5 A1.5,1.5 0 0 1 3.5,19 V5 A1.5,1.5 0 0 1 5,3.5 Z M8,7 H16" },
            { "left", "M5,3.5 H19 A1.5,1.5 0 0 1 20.5,5 V19 A1.5,1.5 0 0 1 19,20.5 H5 A1.5,1.5 0 0 1 3.5,19 V5 A1.5,1.5 0 0 1 5,3.5 Z M7,8 V16" },
            { "right", "M5,3.5 H19 A1.5,1.5 0 0 1 20.5,5 V19 A1.5,1.5 0 0 1 19,20.5 H5 A1.5,1.5 0 0 1 3.5,19 V5 A1.5,1.5 0 0 1 5,3.5 Z M17,8 V16" }
        };

        public static FrameworkElement Icon(string name, double size, Brush stroke)
        {
            ValidateSize(size);
            name = (name ?? "home").Trim().ToLowerInvariant();
            switch (name)
            {
                case "calendar": name = "reminders"; break;
                case "power": name = "shutdown"; break;
                case "fullscreen": name = "expand"; break;
                case "chevron-right": name = "arrowright"; break;
                case "chevron-left": name = "arrowleft"; break;
                case "chevron-down": name = "arrowdown"; break;
                case "chevron-up": name = "arrowup"; break;
            }

            Geometry geometry;
            if (name == "settings") geometry = Gear();
            else
            {
                string data;
                if (!Paths.TryGetValue(name, out data)) data = Paths["home"];
                geometry = Geometry.Parse(data);
                if (geometry.CanFreeze) geometry.Freeze();
            }
            Canvas canvas = Canvas24();
            canvas.Children.Add(new Path
            {
                Data = geometry,
                Stroke = stroke ?? Cobalt,
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = null,
                IsHitTestVisible = false
            });
            return InViewbox(canvas, size);
        }

        public static FrameworkElement Brand(double size)
        {
            ValidateSize(size);
            Canvas canvas = Canvas24();
            canvas.IsHitTestVisible = true;
            canvas.Children.Add(new Ellipse { Width = 24, Height = 24, Fill = Cobalt });
            var island = new Rectangle { Width = 14, Height = 5, RadiusX = 2.5, RadiusY = 2.5, Fill = Brushes.White };
            Canvas.SetLeft(island, 5); Canvas.SetTop(island, 12.5); canvas.Children.Add(island);
            var sky = new Rectangle { Width = 7, Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = Sky };
            Canvas.SetLeft(sky, 10); Canvas.SetTop(sky, 6.5); canvas.Children.Add(sky);
            FrameworkElement brand = InViewbox(canvas, size);
            brand.IsHitTestVisible = true;
            return brand;
        }

        private static Geometry Gear()
        {
            // Eight mechanical teeth, centered around the same 24-unit optical frame.
            var outline = new StreamGeometry();
            using (StreamGeometryContext context = outline.Open())
            {
                for (int tooth = 0; tooth < 8; tooth++)
                {
                    double middle = -Math.PI / 2 + tooth * Math.PI / 4;
                    double[] offsets = { -0.28, -0.16, 0.16, 0.28 };
                    double[] radii = { 7.1, 9.5, 9.5, 7.1 };
                    for (int point = 0; point < offsets.Length; point++)
                    {
                        double angle = middle + offsets[point];
                        Point position = new Point(12 + Math.Cos(angle) * radii[point], 12 + Math.Sin(angle) * radii[point]);
                        if (tooth == 0 && point == 0) context.BeginFigure(position, false, true);
                        else context.LineTo(position, true, false);
                    }
                }
            }
            var geometry = new GeometryGroup();
            geometry.Children.Add(outline);
            geometry.Children.Add(new EllipseGeometry(new Point(12, 12), 3, 3));
            geometry.Freeze();
            return geometry;
        }

        private static Canvas Canvas24()
        {
            return new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
        }

        private static FrameworkElement InViewbox(UIElement content, double size)
        {
            return new Viewbox
            {
                Width = size, Height = size, Stretch = Stretch.Uniform,
                Child = content, IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static Brush FrozenBrush(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        private static void ValidateSize(double size)
        {
            if (double.IsNaN(size) || double.IsInfinity(size) || size <= 0)
                throw new ArgumentOutOfRangeException("size", "图标大小必须大于零。");
        }
    }

    public static class SceneMetrics
    {
        public static double Scale(CoreEngine engine) { return Classroom(engine) ? 1.5 : 1; }
        public static double BallSize(CoreEngine engine) { return Classroom(engine) ? 104 : 68; }
        public static double BallOrbSize(CoreEngine engine) { return Classroom(engine) ? 80 : 48; }
        public static double BallArrowThin(CoreEngine engine) { return Classroom(engine) ? 44 : 10; }
        public static double BallArrowLong(CoreEngine engine) { return Classroom(engine) ? 64 : 28; }

        public static double HandleWidth(UsageScene scene, IslandPlacement placement)
        {
            return scene == UsageScene.Classroom ? 44 : 24;
        }

        public static double HandleHeight(UsageScene scene, IslandPlacement placement)
        {
            return scene == UsageScene.Classroom ? 44 : 24;
        }

        public static double HandleWidth(CoreEngine engine, IslandPlacement placement)
        {
            return HandleWidth(Classroom(engine) ? UsageScene.Classroom : UsageScene.Desktop, placement);
        }

        public static double HandleHeight(CoreEngine engine, IslandPlacement placement)
        {
            return HandleHeight(Classroom(engine) ? UsageScene.Classroom : UsageScene.Desktop, placement);
        }

        private static bool Classroom(CoreEngine engine)
        {
            return engine == null || engine.Settings.Scene == UsageScene.Classroom;
        }
    }
}
