using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FreeIsland
{
    [DataContract]
    public sealed class AssistantPreferences
    {
        [DataMember] public bool Enabled;
        [DataMember] public bool RuleShortcutsEnabled;
        [DataMember] public string SelectedModelId;
    }

    public sealed class AssistantSuggestion
    {
        public string ProcessName;
        public string Source;
        public IList<LocalAiAction> Actions;
    }

    // Only produces proposals. System actions are executed by explicit UI gestures.
    public sealed class AssistantController : IDisposable
    {
        private readonly string settingsPath;
        private readonly bool safe;
        private readonly CoreEngine engine;
        private readonly Func<bool> canPresent;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly Dictionary<string, DateTime> shown = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private string foreground = "";
        private DateTime foregroundSince, lastRequest, retryAfter;
        private bool polling, disposed;
        private int operationVersion;
        public readonly LocalAiService Service;
        public AssistantPreferences Preferences { get; private set; }
        public string LastError { get; private set; }
        public event Action<AssistantSuggestion> Suggested;

        public AssistantController(string directory, bool safeMode, CoreEngine engine, Func<bool> canPresent)
        {
            this.engine = engine; this.canPresent = canPresent; safe = safeMode;
            settingsPath = Path.Combine(directory, "assistant-settings.json");
            Preferences = new AssistantPreferences();
            try { using (var stream = File.OpenRead(settingsPath)) Preferences = (AssistantPreferences)new DataContractJsonSerializer(typeof(AssistantPreferences)).ReadObject(stream) ?? Preferences; } catch { }
            Service = new LocalAiService(Path.Combine(directory, "local-ai"), safeMode);
            if (LocalAiCatalog.Models.All(m => m.Id != Preferences.SelectedModelId)) Preferences.SelectedModelId = LocalAiCatalog.Models[0].Id;
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            string temp = settingsPath + ".tmp";
            using (var stream = File.Create(temp)) new DataContractJsonSerializer(typeof(AssistantPreferences)).WriteObject(stream, Preferences);
            if (File.Exists(settingsPath)) File.Replace(temp, settingsPath, null); else File.Move(temp, settingsPath);
        }

        public void DisableModel()
        {
            operationVersion++; Preferences.Enabled = false; Save(); Service.CancelInstall(); Service.Stop();
        }
        public void CancelInstall() { operationVersion++; Service.CancelInstall(); }

        public async Task InstallAndEnableAsync()
        {
            int version = ++operationVersion; LastError = null;
            await Service.InstallAsync(Preferences.SelectedModelId, lifetime.Token);
            if (disposed || version != operationVersion) throw new OperationCanceledException();
            await Service.StartAsync(Preferences.SelectedModelId, lifetime.Token);
            if (disposed || version != operationVersion) { Service.Stop(); throw new OperationCanceledException(); }
            Preferences.Enabled = true; retryAfter = DateTime.MinValue; Save();
        }

        public async Task EnableAsync()
        {
            int version = ++operationVersion; LastError = null;
            await Service.StartAsync(Preferences.SelectedModelId, lifetime.Token);
            if (disposed || version != operationVersion) { Service.Stop(); throw new OperationCanceledException(); }
            Preferences.Enabled = true; retryAfter = DateTime.MinValue; Save();
        }

        public async void Poll()
        {
            if (disposed || safe || polling || (!Preferences.Enabled && !Preferences.RuleShortcutsEnabled)) return;
            if (!canPresent() || (engine.ShutdownRemaining.HasValue && engine.ShutdownRemaining.Value.TotalSeconds <= 30)) return;
            string name = LocalAiContext.ForegroundProcessName();
            if (IsOwnProcess(name) || string.IsNullOrEmpty(name)) return;
            var now = DateTime.UtcNow;
            if (!String.Equals(foreground, name, StringComparison.OrdinalIgnoreCase)) { foreground = name; foregroundSince = now; return; }
            if ((now - foregroundSince).TotalSeconds < 10 || (now - lastRequest).TotalSeconds < 30) return;
            DateTime previous;
            if (shown.TryGetValue(name, out previous) && (now - previous).TotalMinutes < 10) return;
            polling = true; lastRequest = now; int version = operationVersion;
            try
            {
                IList<LocalAiAction> actions = null; string source = "场景快捷操作";
                if (Preferences.Enabled && now >= retryAfter)
                {
                    try
                    {
                        if (!Service.IsRunning) await Service.StartAsync(Preferences.SelectedModelId, lifetime.Token);
                        actions = await Service.SuggestAsync(name, engine.Settings.Scene == UsageScene.Classroom ? "classroom" : "desktop", lifetime.Token);
                        source = "本地 AI 建议"; LastError = null;
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) { LastError = ex.Message; retryAfter = now.AddMinutes(5); }
                }
                if ((actions == null || actions.Count == 0) && Preferences.RuleShortcutsEnabled) { actions = RuleActions(name); source = "场景快捷操作"; }
                if (disposed || version != operationVersion || (!Preferences.Enabled && !Preferences.RuleShortcutsEnabled) || !canPresent() || !String.Equals(name, LocalAiContext.ForegroundProcessName(), StringComparison.OrdinalIgnoreCase)) return;
                shown[name] = DateTime.UtcNow;
                if (actions != null && actions.Count > 0)
                {
                    var handler = Suggested;
                    if (handler != null) handler(new AssistantSuggestion { ProcessName = name, Source = source, Actions = actions });
                }
            }
            finally { polling = false; }
        }

        public static bool IsOwnProcess(string name)
        {
            return String.Equals(name, "FreeIsland", StringComparison.OrdinalIgnoreCase) || String.Equals(name, "FreeIslandWin7", StringComparison.OrdinalIgnoreCase) || String.Equals(name, "llama-server", StringComparison.OrdinalIgnoreCase);
        }

        public static IList<LocalAiAction> RuleActions(string processName)
        {
            string name = (processName ?? "").ToLowerInvariant();
            if (new[] { "vlc", "wmplayer", "potplayer", "potplayer64", "mpv", "cloudmusic", "qqmusic", "music.ui", "video.ui", "spotify" }.Contains(name))
                return new List<LocalAiAction> { new LocalAiAction { Kind = "volume", Title = "调节系统音量" }, new LocalAiAction { Kind = "media_toggle", Title = "播放 / 暂停" } };
            if (new[] { "powerpnt", "wpp", "wps" }.Contains(name))
                return new List<LocalAiAction> { new LocalAiAction { Kind = "countdown", Title = "课堂练习 · 5 分钟", Seconds = 300 }, new LocalAiAction { Kind = "open_timer", Title = "打开计时器" } };
            if (new[] { "winword", "excel", "notepad", "code", "devenv" }.Contains(name))
                return new List<LocalAiAction> { new LocalAiAction { Kind = "countdown", Title = "专注 · 25 分钟", Seconds = 1500 }, new LocalAiAction { Kind = "open_reminders", Title = "添加日程" } };
            return new List<LocalAiAction>();
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true; lifetime.Cancel(); Service.Dispose(); lifetime.Dispose();
        }
    }
}
