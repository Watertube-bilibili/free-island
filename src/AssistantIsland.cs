using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FreeIsland
{
    internal static class AssistantIsland
    {
        public static void Present(IslandWindow island, CoreEngine engine, AssistantSuggestion suggestion, Action<string> navigate)
        {
            if (suggestion == null || suggestion.Actions == null || suggestion.Actions.Count == 0) return;
            var body = new StackPanel();
            var header = new DockPanel();
            var close = SurfaceStyle.Button("收起", delegate { island.DismissAssistant(); island.Collapse(); }, "#EEF1FF"); close.MinHeight = 36; DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
            var heading = SurfaceStyle.Text(suggestion.Source, 16, "#18243A"); heading.FontWeight = FontWeights.SemiBold; heading.TextAlignment = TextAlignment.Left; heading.VerticalAlignment = VerticalAlignment.Center; header.Children.Add(heading); body.Children.Add(header);
            var status = SurfaceStyle.Text("识别应用：" + suggestion.ProcessName, 12, "#58657A"); status.TextAlignment = TextAlignment.Left; status.TextTrimming = TextTrimming.CharacterEllipsis; body.Children.Add(status);
            double rowHeight = 0; DateTime expires = DateTime.UtcNow.AddSeconds(90);
            foreach (var proposal in suggestion.Actions.Take(3))
            {
                var action = proposal;
                if (action.Kind == "volume")
                {
                    int current = 50; bool available = engine.IsSafeMode || SystemVolume.TryGet(out current);
                    var label = SurfaceStyle.Text(available ? "系统音量  " + current + "%" : "未找到可调节的音频设备", 14, "#18243A"); label.TextAlignment = TextAlignment.Left; label.Margin = new Thickness(0, 6, 0, 0); body.Children.Add(label);
                    var slider = new Slider { Name = "AssistantVolume", Minimum = 0, Maximum = 100, Value = current, IsEnabled = available, IsMoveToPointEnabled = true, SmallChange = 1, LargeChange = 5, MinHeight = 44, VerticalAlignment = VerticalAlignment.Center };
                    System.Windows.Automation.AutomationProperties.SetName(slider, "系统音量");
                    slider.ValueChanged += delegate
                    {
                        island.KeepOpenAfterDrag();
                        label.Text = engine.IsSafeMode || SystemVolume.TrySet((int)slider.Value) ? "系统音量  " + ((int)slider.Value) + "%" : "音频设备已变化，请重新打开建议";
                    };
                    body.Children.Add(slider); rowHeight += 78;
                }
                else if (action.Kind == "media_toggle" || action.Kind == "countdown" || action.Kind == "open_timer" || action.Kind == "open_reminders")
                {
                    string actualAction = action.Kind == "media_toggle" ? "播放 / 暂停" : action.Kind == "open_timer" ? "打开倒计时" : action.Kind == "open_reminders" ? "打开日程提醒" : "开始倒计时 " + TimeSpan.FromSeconds(action.Seconds).ToString(@"h\:mm\:ss");
                    var button = SurfaceStyle.Button(actualAction, delegate
                    {
                        if (DateTime.UtcNow > expires) { status.Text = "建议已过期，请切换应用后重试。"; return; }
                        if (action.Kind == "media_toggle")
                        {
                            if (!engine.IsSafeMode && !String.Equals(LocalAiContext.ForegroundProcessName(), suggestion.ProcessName, StringComparison.OrdinalIgnoreCase)) { status.Text = "播放器已切换，请重新打开播放器。"; return; }
                            if (!engine.IsSafeMode) SystemVolume.ToggleMedia();
                            status.Text = engine.IsSafeMode ? "演示：播放 / 暂停" : "已发送系统媒体键"; island.KeepOpenAfterDrag();
                        }
                        else if (action.Kind == "countdown")
                        {
                            if (action.Seconds < 60 || action.Seconds > 14400) { status.Text = "建议时长无效。"; return; }
                            if (engine.CountdownActive) { navigate("countdown"); status.Text = "已有倒计时，请在控制中心调整。"; return; }
                            engine.StartCountdown(TimeSpan.FromSeconds(action.Seconds));
                        }
                        else navigate(action.Kind == "open_timer" ? "countdown" : "reminders");
                    }, "#EEF1FF");
                    button.MinHeight = 44; button.Margin = new Thickness(0, 5, 0, 0); button.HorizontalAlignment = HorizontalAlignment.Stretch;
                    if (action.Kind == "countdown")
                    {
                        var copy = new StackPanel(); var actual = SurfaceStyle.Text(actualAction, 14, "#18243A"); var proposed = SurfaceStyle.Text(action.Title, 11, "#58657A"); proposed.TextTrimming = TextTrimming.CharacterEllipsis; proposed.MaxWidth = 310; copy.Children.Add(actual); copy.Children.Add(proposed); button.Content = copy; button.MinHeight = 58;
                    }
                    System.Windows.Automation.AutomationProperties.SetName(button, actualAction); body.Children.Add(button); rowHeight += button.MinHeight + 5;
                }
            }
            island.ShowAssistant(body, 106 + rowHeight);
        }
    }
}
