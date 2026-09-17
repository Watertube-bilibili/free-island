using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FreeIsland
{
    internal sealed class IslandTaskCard : Border
    {
        private readonly CoreEngine engine;
        private readonly TextBlock clock, state;
        private readonly Button primary;
        internal readonly LiquidGlassSurface Material;
        internal string Kind { get; private set; }

        internal IslandTaskCard(CoreEngine engine, IslandTaskInfo task, Action collapse, Action changed)
        {
            this.engine = engine; Kind = task.Kind;
            Name = "TaskCard_" + Kind; Height = 102; Margin = new Thickness(10, 0, 10, 0);
            Padding = new Thickness(0, 7, 0, 7); Background = Brushes.Transparent; Cursor = System.Windows.Input.Cursors.SizeAll;
            var layers = new Grid(); Material = new LiquidGlassSurface { Mode = engine.Settings.GlassMode, Radius = 28, Name = "TaskGlass_" + Kind }; layers.Children.Add(Material);
            var row = new Grid { Margin = new Thickness(14, 9, 12, 9) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new Border { Child = AppVisual.Icon(Kind, 24, SurfaceStyle.Brush(Kind == "countdown" ? "#B56500" : Kind == "shutdown" ? "#B53928" : "#3458B8")), Padding = new Thickness(4), CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Center };
            icon.SetValue(LiquidGlass.ReadablePanelProperty, true); row.Children.Add(icon);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            state = SurfaceStyle.Text("", 12, "#3D4A60"); state.TextAlignment = TextAlignment.Left;
            clock = SurfaceStyle.Text("", 23, "#18243A"); clock.TextAlignment = TextAlignment.Left; clock.FontWeight = FontWeights.SemiBold; clock.FontFamily = new FontFamily("Segoe UI");
            words.Children.Add(state); words.Children.Add(clock);
            var backing = new Border { Name = "TaskTextBacking_" + Kind, Child = words, Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(6, 0, 7, 0), CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Center };
            backing.SetValue(LiquidGlass.ReadablePanelProperty, true); Grid.SetColumn(backing, 1); row.Children.Add(backing);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(buttons, 2);
            primary = SurfaceStyle.Button("", delegate
            {
                if (Kind == "countdown") engine.PauseResumeCountdown();
                else if (Kind == "stopwatch") engine.ToggleStopwatch();
                else engine.CancelShutdown();
                changed();
            }, "#EEF1FF"); primary.Name = "TaskPrimary_" + Kind; primary.MinHeight = 44; primary.MinWidth = 48; buttons.Children.Add(primary);
            if (Kind != "shutdown")
            {
                var stop = SurfaceStyle.Button("结束", delegate { if (Kind == "countdown") engine.CancelCountdown(); else engine.ResetStopwatch(); changed(); }, "#F2F4F9");
                stop.Name = "TaskStop_" + Kind; stop.MinHeight = 44; stop.Margin = new Thickness(4, 0, 0, 0); buttons.Children.Add(stop);
            }
            var tuck = SurfaceStyle.Button("", collapse, "#F2F4F9"); tuck.Content = AppVisual.Icon("close", 14, SurfaceStyle.Brush("#596783")); tuck.Width = 36; tuck.MinHeight = 44; tuck.Margin = new Thickness(4, 0, 0, 0); tuck.ToolTip = "收起全部任务，计时继续";
            System.Windows.Automation.AutomationProperties.SetName(tuck, "收起全部任务"); buttons.Children.Add(tuck);
            row.Children.Add(buttons); layers.Children.Add(row); Child = layers; LiquidGlass.Track(this, Material); Update(task);
        }
        internal void Update(IslandTaskInfo task)
        {
            TimeSpan time = TimeSpan.FromSeconds(Math.Max(0, task.Kind == "stopwatch" ? Math.Floor(task.Time.TotalSeconds) : Math.Ceiling(task.Time.TotalSeconds)));
            clock.Text = time.TotalHours >= 1 ? ((int)time.TotalHours).ToString("00") + ":" + time.Minutes.ToString("00") + ":" + time.Seconds.ToString("00") : ((int)time.TotalMinutes).ToString("00") + ":" + time.Seconds.ToString("00");
            state.Text = task.Title + (task.Kind == "shutdown" ? engine.IsSafeMode ? " · 安全预览" : " · 请保存工作" : task.Running ? " · 进行中" : " · 已暂停");
            primary.Content = task.Kind == "shutdown" ? "取消关机" : task.Running ? "暂停" : "继续";
            System.Windows.Automation.AutomationProperties.SetName(primary, primary.Content + task.Title);
            if (Material.Mode != engine.Settings.GlassMode) Material.Mode = engine.Settings.GlassMode;
        }
    }
}
