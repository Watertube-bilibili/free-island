using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FreeIsland
{
    // Exercised only by --smoke-test, against isolated test data and an inert shutdown adapter.
    internal static class SmokeChecks
    {
        public static string Run(CoreEngine engine, ControlWindow panel, BallWindow ball, IslandWindow island, IslandHandleWindow handle, Action<Point> dock, PresentationWindow presentation)
        {
            if (!engine.IsSafeMode) throw new InvalidOperationException("UI checks require safe mode.");
            engine.ResetStopwatch(); engine.CancelCountdown(); engine.CancelShutdown();
            panel.Navigate("stopwatch"); Click(panel, "开始计时");
            Require(engine.StopwatchRunning, "stopwatch start button");
            panel.Navigate("stopwatch"); Click(panel, "暂停计时");
            Require(!engine.StopwatchRunning, "stopwatch pause button");
            engine.ResetStopwatch();

            panel.Navigate("countdown"); panel.UpdateLayout();
            var inputs = Children<TextBox>(panel).ToArray();
            Require(inputs.Length == 3, "countdown duration inputs");
            inputs[0].Text = "0"; inputs[1].Text = "0"; inputs[2].Text = "30";
            Click(panel, "开始倒计时");
            Require(engine.CountdownRunning && engine.CountdownRemaining.TotalSeconds <= 30, "custom countdown start button");
            panel.Navigate("countdown"); Click(panel, "暂停");
            Require(engine.CountdownActive && !engine.CountdownRunning, "countdown pause button");
            panel.Navigate("countdown"); Click(panel, "继续");
            Require(engine.CountdownRunning, "countdown resume button");
            Click(panel, "结束倒计时"); Require(!engine.CountdownActive, "countdown cancel button");

            panel.Navigate("reminders"); panel.UpdateLayout();
            int before = engine.Reminders.Count;
            var reminderInputs = Children<TextBox>(panel).ToArray();
            Require(reminderInputs.Length == 3, "reminder form inputs");
            reminderInputs[0].Text = "UI 自动检查";
            Click(panel, "＋  添加提醒");
            Require(engine.Reminders.Count == before + 1, "reminder add button");
            engine.RemoveReminder(engine.Reminders.Last().Id);

            panel.Navigate("shutdown"); Click(panel, "确认预约关机");
            Require(engine.ShutdownAt.HasValue, "safe shutdown schedule button");
            panel.Navigate("shutdown"); Click(panel, "取消关机预约");
            Require(!engine.ShutdownAt.HasValue, "shutdown cancellation button");

            presentation.Open(); Click(presentation, "开始 5 分钟倒计时");
            Require(engine.CountdownRunning && engine.CountdownRemaining.TotalMinutes <= 5, "classroom preset button");
            Click(presentation, "暂停计时"); Require(!engine.CountdownRunning, "classroom pause button");
            Click(presentation, "继续计时"); Require(engine.CountdownRunning, "classroom resume button");
            Click(presentation, "结束计时"); Require(!engine.CountdownActive, "classroom stop button"); presentation.Hide();

            panel.Navigate("settings"); panel.UpdateLayout();
            var dotSlider = Children<Slider>(panel).Single(s => s.Name == "IslandDotPercentSlider");
            var scaleInput = Children<TextBox>(panel).Single(t => t.Name == "IslandScaleInput");
            Require(dotSlider.Minimum == 0 && dotSlider.Maximum == 100, "dot slider bounds");
            dotSlider.Value = 100; scaleInput.Text = "150"; Click(panel, "应用大小");
            Require(engine.Settings.IslandDotSize == 20 && engine.Settings.IslandScale == 1.5, "island sizing apply");
            dotSlider.Value = 20; scaleInput.Text = "50"; Click(panel, "应用大小");
            Require(engine.Settings.IslandDotSize == 20 && engine.Settings.IslandScale == 1.5, "invalid sizing rejected");
            Click(panel, "恢复默认大小");
            Require(engine.Settings.IslandDotPercent == 20 && engine.Settings.IslandDotSize == 6 && engine.Settings.IslandScale == 1, "island sizing defaults");
            Click(panel, "标准液态玻璃"); Require(engine.Settings.GlassMode == 2, "standard glass selector");
            Click(panel, "关闭液态玻璃"); Require(engine.Settings.GlassMode == 0, "glass off selector");
            Click(panel, "轻量液态玻璃"); Require(engine.Settings.GlassMode == 1, "lite glass selector");

            IslandPlacement placement = engine.Settings.Placement;
            double originalAnchor = engine.Settings.IslandAnchor;
            string originalScreen = engine.Settings.IslandScreen;
            foreach (IslandPlacement value in Enum.GetValues(typeof(IslandPlacement)))
            {
                engine.Settings.Placement = value; island.ShowActivity("stopwatch");
                Require(island.IsVisible && !double.IsNaN(island.Left) && !double.IsNaN(island.Top), "island placement " + value);
                island.Collapse();
                Require(handle.IsVisible && handle.Width == SceneMetrics.HandleWidth(engine, value) && handle.Height == SceneMetrics.HandleHeight(engine, value), "collapsed island arrow " + value);
            }
            Rect work = island.LastWorkArea;
            dock(new Point(work.Left + 3, work.Top + work.Height * .7));
            Require(engine.Settings.Placement == IslandPlacement.Left && Math.Abs(engine.Settings.IslandAnchor - .7) < .01, "drag docking to left edge");
            dock(new Point(work.Right - 3, work.Top + work.Height * .65));
            Require(engine.Settings.Placement == IslandPlacement.Right && Math.Abs(engine.Settings.IslandAnchor - .65) < .01, "drag docking to right edge");
            dock(new Point(work.Left + work.Width * .6, work.Top + 3));
            Require(engine.Settings.Placement == IslandPlacement.Top && Math.Abs(engine.Settings.IslandAnchor - .6) < .01, "drag docking to top edge");
            engine.Settings.Placement = placement;
            engine.Settings.IslandAnchor = originalAnchor;
            engine.Settings.IslandScreen = originalScreen;
            ball.VerifyEdgeCollapse();
            return "PASS: stopwatch start/pause; custom countdown start/pause/resume/cancel; reminder add; safe shutdown schedule/cancel; classroom stage preset/pause/resume/stop; three island placements and tiny handles; drag docking/position persistence; four ball edge-arrow sizes.";
        }

        private static void Click(Window panel, string text)
        {
            panel.UpdateLayout();
            var button = Children<Button>(panel).FirstOrDefault(b => string.Equals(b.Content as string, text, StringComparison.Ordinal) || string.Equals(System.Windows.Automation.AutomationProperties.GetName(b), text, StringComparison.Ordinal));
            if (button == null) throw new InvalidOperationException("Button not found: " + text);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
        }
        private static IEnumerable<T> Children<T>(DependencyObject node) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is T) yield return (T)child;
                foreach (T nested in Children<T>(child)) yield return nested;
            }
        }
        private static void Require(bool condition, string name) { if (!condition) throw new InvalidOperationException("UI check failed: " + name); }
    }
}
