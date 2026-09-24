using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace FreeIsland
{
    public sealed partial class ControlWindow
    {
        private System.Windows.Threading.DispatcherTimer audioSaveTimer;
        private bool audioSavePending;
        private void FlushAudioPreferences()
        {
            if (!audioSavePending) return;
            if (audioSaveTimer != null) audioSaveTimer.Stop();
            try { alertAudio.Save(); audioSavePending = false; } catch (Exception ex) { Notify("音频设置未保存：" + ex.Message, true); }
        }
        private void BuildAssistant()
        {
            pageTitle.Text = "本地助手与提示音";
            pageCaption.Text = "按需开启，建议出现后由你操作。模型在这台电脑上运行。";
            var page = new StackPanel();
            var scroll = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly };
            content.Content = scroll;
            if (assistant == null) page.Children.Add(EmptyState("本地助手", "从正在运行的浮岛打开此页面以设置助手。"));
            else
            {
                page.Children.Add(Setting("轻量场景快捷操作", "识别播放器、演示文稿和编辑器，提供音量、计时与日程入口。无需下载模型。", assistant.Preferences.RuleShortcutsEnabled, delegate(bool enabled) { assistant.Preferences.RuleShortcutsEnabled = enabled; assistant.Save(); }));
                page.Children.Add(SectionTitle("可选本地模型"));
                page.Children.Add(T("只读取前台应用的进程名称与已选场景，不读取窗口标题、屏幕、文档或剪贴板。模型可组合计时、音量、媒体控制和日程入口，不会自行运行代码或执行关机。", SmallSize, muted));
                string support = LocalAiService.GetSupportMessage();
                if (!String.IsNullOrEmpty(support)) page.Children.Add(T(support, SmallSize, B("#80540A"), FontWeights.Medium, new Thickness(0, 10, 0, 8)));
                var choices = new StackPanel { Margin = new Thickness(0, 14, 0, 14) };
                foreach (var model in LocalAiCatalog.Models)
                {
                    var selectedModel = model;
                    var radio = new RadioButton { Content = T(model.Name + " · " + model.Quantization + " · 下载 " + (model.DownloadBytes / 1000000000.0).ToString("0.00") + " GB · 建议内存 " + model.RecommendedRamGb + " GB", BodySize, ink), GroupName = "LocalModels", FontSize = BodySize, MinHeight = TargetHeight, VerticalContentAlignment = VerticalAlignment.Center, IsChecked = model.Id == assistant.Preferences.SelectedModelId, IsEnabled = !assistant.Service.IsBusy };
                    radio.Checked += delegate { if (assistant.Service.IsRunning || assistant.Preferences.Enabled) assistant.DisableModel(); assistant.Preferences.SelectedModelId = selectedModel.Id; assistant.Save(); };
                    choices.Children.Add(radio);
                }
                page.Children.Add(choices);
                var status = T("", SmallSize, muted); status.Name = "LocalAiStatus";
                var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 7, Margin = new Thickness(0, 8, 0, 12), Foreground = accent };
                var controls = new WrapPanel();
                Button install = null, start = null;
                install = Btn("一键安装并启用", async delegate { await RunAssistantAction(assistant.InstallAndEnableAsync); }, true);
                start = Btn("启用已下载模型", async delegate { await RunAssistantAction(assistant.EnableAsync); }, false, new Thickness(10, 0, 0, 0));
                var stop = Btn("关闭模型 · 释放内存", delegate { assistant.DisableModel(); Notify("模型已关闭，内存已释放。"); }, false, new Thickness(10, 0, 0, 0));
                var cancel = Btn("取消下载", delegate { assistant.CancelInstall(); }, false, new Thickness(10, 0, 0, 0));
                controls.Children.Add(install); controls.Children.Add(start); controls.Children.Add(stop); controls.Children.Add(cancel);
                page.Children.Add(status); page.Children.Add(progress); page.Children.Add(controls);
                page.Children.Add(T("使用 llama.cpp CPU 推理；模型与运行组件单独下载，不包含在安装包中。下载需要网络，推理仅连接本机。关闭模型后仍可使用轻量快捷操作。", SmallSize, muted, FontWeights.Normal, new Thickness(0, 12, 0, 0)));
                var sources = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
                foreach (var model in LocalAiCatalog.Models)
                {
                    var m = model; sources.Children.Add(TextButton(m.Parameters + " 来源与许可", delegate { if (!engine.IsSafeMode) System.Diagnostics.Process.Start(m.SourceUrl); }));
                }
                page.Children.Add(sources);
                updatePage = delegate
                {
                    status.Text = assistant.Service.Status + (String.IsNullOrEmpty(assistant.LastError) ? "" : "\n" + assistant.LastError);
                    progress.Value = assistant.Service.Progress * 100;
                    progress.Visibility = assistant.Service.IsBusy ? Visibility.Visible : Visibility.Collapsed;
                    install.IsEnabled = support == null && !assistant.Service.IsBusy;
                    start.IsEnabled = support == null && !assistant.Service.IsBusy && assistant.Service.IsModelInstalled(assistant.Preferences.SelectedModelId);
                    choices.IsEnabled = !assistant.Service.IsBusy;
                    stop.IsEnabled = assistant.Service.IsRunning || assistant.Preferences.Enabled;
                    cancel.IsEnabled = assistant.Service.IsBusy;
                };
            }
            page.Children.Add(SectionTitle("自定义提醒音频"));
            page.Children.Add(T("倒计时、日程和关机预警可分别选音乐。支持 WAV / MP3；关机只在最后 10 秒播放，取消后立即停止。下方音量控制自选音频，默认提示音跟随系统音量。", SmallSize, muted));
            if (alertAudio == null) return;
            page.Children.Add(Setting("提醒声音总开关", "关闭后静音；试听仍可单独使用。", engine.Settings.SoundEnabled, delegate(bool enabled) { engine.Settings.SoundEnabled = enabled; engine.SaveSettings(); if (!enabled) alertAudio.Stop(); }));
            var volume = TimeSlider("AlertVolume", "提醒音量", alertAudio.VolumePercent, 100);
            page.Children.Add(TouchNumber("音量 %", volume));
            if (audioSaveTimer == null) { audioSaveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) }; audioSaveTimer.Tick += delegate { FlushAudioPreferences(); }; }
            volume.ValueChanged += delegate { alertAudio.VolumePercent = (int)volume.Value; audioSavePending = true; audioSaveTimer.Stop(); audioSaveTimer.Start(); };
            foreach (var kind in new[] { AlertSoundKind.Countdown, AlertSoundKind.Reminder, AlertSoundKind.Shutdown })
            {
                var sound = kind; string label = kind == AlertSoundKind.Countdown ? "倒计时结束" : kind == AlertSoundKind.Reminder ? "日程提醒" : "关机最后 10 秒";
                var path = T(AudioLabel(sound), SmallSize, muted); path.TextTrimming = TextTrimming.CharacterEllipsis;
                page.Children.Add(T(label, BodySize, ink, FontWeights.Medium, new Thickness(0, 16, 0, 6))); page.Children.Add(path);
                var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                row.Children.Add(Btn("选择音频", delegate
                {
                    var picker = new OpenFileDialog { Title = "选择" + label + "音频", Filter = "音频文件 (*.wav;*.mp3)|*.wav;*.mp3", CheckFileExists = true, Multiselect = false };
                    if (picker.ShowDialog(this) != true) return;
                    alertAudio.SetPath(sound, picker.FileName); alertAudio.Save(); path.Text = AudioLabel(sound); Notify("已保存音频；请保留原文件。");
                }));
                row.Children.Add(Btn("试听", delegate { alertAudio.Play(sound, true); Notify(engine.IsSafeMode ? "安全演示：已模拟试听。" : "试听最多 8 秒；可随时停止。"); }, false, new Thickness(10, 0, 0, 0)));
                row.Children.Add(Btn("恢复默认", delegate { alertAudio.Stop(); alertAudio.SetPath(sound, ""); alertAudio.Save(); path.Text = AudioLabel(sound); }, false, new Thickness(10, 0, 0, 0)));
                page.Children.Add(row);
            }
            page.Children.Add(Btn("停止播放", delegate { alertAudio.Stop(); }, false, new Thickness(0, 16, 0, 12)));
            var error = T("", SmallSize, B("#B72B38")); page.Children.Add(error);
            Action previousUpdate = updatePage;
            updatePage = delegate { if (previousUpdate != null) previousUpdate(); error.Text = alertAudio.LastError ?? ""; };
        }

        private string AudioLabel(AlertSoundKind kind)
        {
            string path = alertAudio.GetPath(kind);
            return String.IsNullOrEmpty(path) ? "默认提示音" : Path.GetFileName(path) + (File.Exists(path) ? "" : " · 文件不存在，将使用默认提示音");
        }
        private async Task RunAssistantAction(Func<Task> action)
        {
            try { await action(); Notify("本地模型已启用。"); }
            catch (OperationCanceledException) { Notify("操作已取消。"); }
            catch (Exception ex) { Notify("模型未启用：" + ex.Message, true); }
        }
    }
}
