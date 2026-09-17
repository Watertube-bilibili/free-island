using System;
using System.Windows;
using System.Windows.Media;
using System.Globalization;

namespace FreeIsland
{
    // A quiet, once-per-second thumbnail. The underlying water surface owns all motion.
    internal sealed class TaskThumbnail : FrameworkElement
    {
        private IslandTaskInfo task;
        private int count;
        private string signature = "";
        public void Update(IslandTaskInfo value, int total)
        {
            string next = value == null ? "" : value.Kind + ":" + (long)(value.Kind == "stopwatch" ? Math.Floor(value.Time.TotalSeconds) : Math.Ceiling(value.Time.TotalSeconds)) + ":" + value.Running + ":" + total;
            task = value; count = total;
            if (signature == next) return;
            signature = next; InvalidateVisual();
            System.Windows.Automation.AutomationProperties.SetName(this, value == null ? "灵动岛小点" : total + " 个任务，" + value.Title + "，" + TimeLabel(value) + (value.Running ? "，运行中" : "，已暂停"));
        }
        internal static string TimeLabel(IslandTaskInfo value)
        {
            long seconds = Math.Max(0, (long)(value.Kind == "stopwatch" ? Math.Floor(value.Time.TotalSeconds) : Math.Ceiling(value.Time.TotalSeconds)));
            if (seconds < 60) return seconds.ToString(CultureInfo.InvariantCulture);
            if (seconds < 3600) return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
            return (seconds / 3600).ToString() + ":" + ((seconds / 60) % 60).ToString("00");
        }
        protected override void OnRender(DrawingContext dc)
        {
            if (task == null || ActualWidth < 1) return;
            double size = Math.Min(ActualWidth, ActualHeight), middle = size / 2;
            Point center = new Point(middle, middle);
            Brush accent = new SolidColorBrush(task.Kind == "shutdown" ? Color.FromRgb(190, 58, 42) : task.Kind == "countdown" ? Color.FromRgb(218, 119, 0) : Color.FromRgb(57, 88, 192));
            // The ink backing is independent of the user's glass transparency setting.
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(240, 250, 252, 255)), null, center, size * .32, size * .32);
            double radius = size * .39;
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(58, 51, 66, 85)), Math.Max(1, size / 30)), center, radius, radius);
            double progress = task.Progress < 0 ? (task.Time.TotalSeconds % 60) / 60 : Math.Max(0, Math.Min(1, task.Progress));
            var stroke = new Pen(accent, Math.Max(1.5, size / 24)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (progress >= .999) dc.DrawEllipse(null, stroke, center, radius, radius);
            else if (progress > .001)
            {
                double angle = progress * Math.PI * 2;
                var arc = new StreamGeometry();
                using (var g = arc.Open())
                {
                    g.BeginFigure(new Point(middle, middle - radius), false, false);
                    g.ArcTo(new Point(middle + Math.Sin(angle) * radius, middle - Math.Cos(angle) * radius), new Size(radius, radius), 0, progress > .5, SweepDirection.Clockwise, true, false);
                }
                arc.Freeze(); dc.DrawGeometry(null, stroke, arc);
            }
            string label = TimeLabel(task);
            DrawText(dc, label, size * (label.Length > 4 ? .22 : label.Length > 2 ? .25 : .34), new Point(middle, middle - size * .08), Brushes.Black, FontWeights.SemiBold);
            string unit = !task.Running ? "暂停" : task.Time.TotalSeconds < 60 ? "秒" : task.Time.TotalSeconds < 3600 ? "分:秒" : "时:分";
            DrawText(dc, unit, size * .145, new Point(middle, middle + size * .19), new SolidColorBrush(Color.FromRgb(48, 62, 82)), FontWeights.Normal);
            if (count > 1)
            {
                double badge = size * .16;
                Point badgeCenter = new Point(size * .91, size * .09);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(29, 44, 69)), new Pen(Brushes.White, Math.Max(1, size / 38)), badgeCenter, badge, badge);
                DrawText(dc, count.ToString(CultureInfo.InvariantCulture), size * .20, badgeCenter, Brushes.White, FontWeights.SemiBold);
            }
        }
        private static void DrawText(DrawingContext dc, string value, double size, Point center, Brush brush, FontWeight weight)
        {
            var text = new FormattedText(value, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Microsoft YaHei UI, Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal), size, brush);
            dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
        }
    }
}
