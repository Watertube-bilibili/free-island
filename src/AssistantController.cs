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
        [DataMember] public bool IncludeWindowTitle;
        [DataMember] public string SelectedModelId;
        [DataMember] public string ModelDirectory;
    }

    public sealed class AssistantSuggestion
    {
        public string ProcessName;
        public string Source;
        public string ContextDescription;
        public IList<LocalAiAction> Actions;
    }

    // Only produces proposals. System actions are executed by explicit UI gestures.
    public sealed class AssistantController : IDisposable
    {
        private readonly string settingsPath;
        private readonly string defaultModelDirectory;
        private string modelDirectory;
        private readonly bool safe;
        private readonly CoreEngine engine;
        private readonly Func<bool> canPresent;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly AssistantContextGate contextGate = new AssistantContextGate();
        private readonly SemaphoreSlim requests = new SemaphoreSlim(1, 1);
        private CancellationTokenSource suggestionRequest, chatRequest;
        private DateTime retryAfter;
        private bool polling, chatting, disposed, activating, changingDirectory;
        private bool savedEnabled, savedRules, savedTitle;
        private string savedModel;
        private int operationVersion;
        public LocalAiService Service { get; private set; }
        public string ModelDirectory { get { return modelDirectory; } }
        public AssistantPreferences Preferences { get; private set; }
        public string LastError { get; private set; }
        public AssistantContextSnapshot LastContext { get; private set; }
        public bool IsChatting { get { return chatting; } }
        public event Action<AssistantSuggestion> Suggested;

        public AssistantController(string directory, bool safeMode, CoreEngine engine, Func<bool> canPresent)
        {
            this.engine = engine; this.canPresent = canPresent; safe = safeMode;
            settingsPath = Path.Combine(directory, "assistant-settings.json");
            defaultModelDirectory = Path.GetFullPath(Path.Combine(directory, "local-ai"));
            Preferences = new AssistantPreferences();
            try { using (var stream = File.OpenRead(settingsPath)) Preferences = (AssistantPreferences)new DataContractJsonSerializer(typeof(AssistantPreferences)).ReadObject(stream) ?? Preferences; } catch { }
            try { modelDirectory = ValidateModelDirectory(Preferences.ModelDirectory); }
            catch (Exception ex)
            {
                modelDirectory = defaultModelDirectory; Preferences.ModelDirectory = null; Preferences.Enabled = false;
                LastError = "保存的模型目录不可用，已改用默认目录且未启用模型：" + ex.Message;
            }
            Service = new LocalAiService(modelDirectory, safeMode);
            if (LocalAiCatalog.Models.All(m => m.Id != Preferences.SelectedModelId)) Preferences.SelectedModelId = LocalAiCatalog.Models[0].Id;
            LastContext = AssistantContext.Empty();
            RememberPreferences();
        }

        public void Save()
        {
            if (savedEnabled != Preferences.Enabled || savedRules != Preferences.RuleShortcutsEnabled || savedTitle != Preferences.IncludeWindowTitle || savedModel != Preferences.SelectedModelId)
            {
                operationVersion++;
                CancelRequests();
                Service.CancelInstall();
                contextGate.Observe("", DateTime.UtcNow);
                retryAfter = DateTime.MinValue;
                if (!Preferences.IncludeWindowTitle && LastContext != null)
                    LastContext = AssistantContext.Create(LastContext.ProcessName, "", LastContext.IsFullScreen, false);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            string temp = settingsPath + ".tmp";
            using (var stream = File.Create(temp)) new DataContractJsonSerializer(typeof(AssistantPreferences)).WriteObject(stream, Preferences);
            if (File.Exists(settingsPath)) File.Replace(temp, settingsPath, null); else File.Move(temp, settingsPath);
            RememberPreferences();
        }

        private void RememberPreferences()
        {
            savedEnabled = Preferences.Enabled; savedRules = Preferences.RuleShortcutsEnabled;
            savedTitle = Preferences.IncludeWindowTitle; savedModel = Preferences.SelectedModelId;
        }

        private void CancelRequests()
        {
            CancelRequest(suggestionRequest);
            CancelRequest(chatRequest);
        }

        private static void CancelRequest(CancellationTokenSource source)
        {
            try { if (source != null) source.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        public void DisableModel()
        {
            operationVersion++; CancelRequests(); Preferences.Enabled = false; Save(); Service.CancelInstall(); Service.Stop();
        }
        public void CancelInstall() { operationVersion++; CancelRequests(); Service.CancelInstall(); }

        public Task InstallAndEnableAsync() { return ActivateAsync(true); }
        public Task EnableAsync() { return ActivateAsync(false); }

        private async Task ActivateAsync(bool install)
        {
            if (disposed) throw new ObjectDisposedException("AssistantController");
            if (changingDirectory) throw new InvalidOperationException("正在切换模型目录，请稍后再启用模型。");
            if (activating) throw new InvalidOperationException("正在安装或启动模型，请先等待完成或取消。");
            activating = true;
            int version = ++operationVersion; LastError = null;
            string modelId = Preferences.SelectedModelId;
            CancelRequests();
            bool entered = false;
            try
            {
                await requests.WaitAsync(lifetime.Token); entered = true;
                if (disposed || version != operationVersion || modelId != Preferences.SelectedModelId) throw new OperationCanceledException();
                ValidateModelDirectory(modelDirectory);
                if (install && !String.Equals(modelDirectory, defaultModelDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(modelDirectory);
                    ClaimModelDirectory(modelDirectory);
                }
                if (install) await Service.InstallAsync(modelId, lifetime.Token);
                if (disposed || version != operationVersion || modelId != Preferences.SelectedModelId) throw new OperationCanceledException();
                await Service.StartAsync(modelId, lifetime.Token);
                if (disposed || version != operationVersion || modelId != Preferences.SelectedModelId) { Service.Stop(); throw new OperationCanceledException(); }
                Preferences.Enabled = true; retryAfter = DateTime.MinValue; Save();
            }
            finally { if (entered) requests.Release(); activating = false; }
        }

        public async Task SetModelDirectoryAsync(string path)
        {
            if (disposed) throw new ObjectDisposedException("AssistantController");
            if (activating || (Service.IsBusy && !polling && !chatting))
                throw new InvalidOperationException("正在安装或启动模型，请先点击取消并等待操作停止，再更换目录。");
            if (changingDirectory) throw new InvalidOperationException("正在切换模型目录，请等待完成。");
            string destination = ValidateModelDirectory(path);
            if (String.Equals(destination, modelDirectory, StringComparison.OrdinalIgnoreCase)) return;
            changingDirectory = true;
            int version = ++operationVersion;
            CancelRequests();
            bool entered = false;
            LocalAiService replacement = null;
            try
            {
                await requests.WaitAsync(lifetime.Token); entered = true;
                if (disposed || version != operationVersion) throw new OperationCanceledException("切换模型目录已取消。");
                if (activating || Service.IsBusy) throw new InvalidOperationException("模型仍在处理操作，请等待停止后重试。");
                destination = ValidateModelDirectory(path);
                Directory.CreateDirectory(destination);
                LocalAiStorage.EnsurePlainPath(destination);
                if (!String.Equals(destination, defaultModelDirectory, StringComparison.OrdinalIgnoreCase)) ClaimModelDirectory(destination);
                replacement = new LocalAiService(destination, safe);

                string previousPreference = Preferences.ModelDirectory;
                bool previousEnabled = Preferences.Enabled;
                Preferences.ModelDirectory = String.Equals(destination, defaultModelDirectory, StringComparison.OrdinalIgnoreCase) ? null : destination;
                Preferences.Enabled = false;
                try { Save(); }
                catch
                {
                    Preferences.ModelDirectory = previousPreference;
                    Preferences.Enabled = previousEnabled;
                    RememberPreferences();
                    throw;
                }
                // No files are moved or deleted. The new service was validated before
                // committing settings; the previous directory remains available to select.
                LocalAiService previous = Service;
                previous.Stop(); previous.Dispose();
                Service = replacement; replacement = null;
                modelDirectory = destination;
                LastError = null;
            }
            finally
            {
                if (replacement != null) replacement.Dispose();
                if (entered) requests.Release();
                changingDirectory = false;
            }
        }

        private const string DirectoryMarker = "FreeIsland.local-ai.directory";
        private const string DirectoryMarkerContent = "FreeIsland local AI model directory v1";

        private string ValidateModelDirectory(string preference)
        {
            string path = String.IsNullOrWhiteSpace(preference) ? defaultModelDirectory : preference.Trim();
            if (path.Length < 3 || !Char.IsLetter(path[0]) || path[1] != ':' || (path[2] != '\\' && path[2] != '/'))
                throw new ArgumentException("请选择本地磁盘上的完整路径，例如 D:\\FreeIslandModels；不支持相对路径或网络目录。", "path");
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string root = Path.GetPathRoot(full);
            if (String.Equals(full.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("请在磁盘中选择一个专门保存模型的子文件夹，不能直接使用磁盘根目录。", "path");
            var drive = new DriveInfo(root);
            if (drive.DriveType == DriveType.Network || drive.DriveType == DriveType.NoRootDirectory || drive.DriveType == DriveType.Unknown)
                throw new ArgumentException("模型目录必须位于可用的本地磁盘。", "path");
            LocalAiStorage.EnsurePlainPath(full);
            if (File.Exists(full)) throw new IOException("所选路径是文件，请选择模型文件夹。");
            if (!String.Equals(full, defaultModelDirectory, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full))
            {
                string marker = LocalAiStorage.Inside(full, DirectoryMarker);
                bool owned = File.Exists(marker) && new FileInfo(marker).Length < 128 && File.ReadAllText(marker) == DirectoryMarkerContent;
                if (!owned && Directory.EnumerateFileSystemEntries(full).Any())
                    throw new IOException("所选目录已有其他文件。请创建或选择一个空文件夹；已有浮岛模型目录可以直接选回。");
            }
            return full;
        }

        private static void ClaimModelDirectory(string directory)
        {
            string marker = LocalAiStorage.Inside(directory, DirectoryMarker);
            if (File.Exists(marker))
            {
                if (new FileInfo(marker).Length >= 128 || File.ReadAllText(marker) != DirectoryMarkerContent)
                    throw new IOException("所选目录的浮岛标记不匹配，未修改现有文件。");
                return;
            }
            if (Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("所选目录已出现其他文件，请选择空文件夹后重试。");
            using (var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream)) writer.Write(DirectoryMarkerContent);
        }

        public AssistantContextSnapshot RefreshContext()
        {
            if (disposed) throw new ObjectDisposedException("AssistantController");
            if (!Preferences.IncludeWindowTitle && LastContext.WindowTitle.Length > 0)
                LastContext = AssistantContext.Create(LastContext.ProcessName, "", LastContext.IsFullScreen, false);
            if (safe) return LastContext;
            AssistantContextSnapshot snapshot = AssistantContext.Capture(Preferences.IncludeWindowTitle);
            if (snapshot.ProcessName.Length > 0) LastContext = snapshot;
            // Opening the island focuses our own window. Keep the last external snapshot,
            // rather than substituting our own window or fabricating a fresh scene.
            return LastContext;
        }

        public async Task<LocalAiReply> ChatAsync(string text, IList<LocalAiTurn> history, CancellationToken token)
        {
            if (disposed) throw new ObjectDisposedException("AssistantController");
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("请输入要问本地助手的问题。", "text");
            if (text.Length > 600) throw new ArgumentException("问题最多输入 600 个字符，请缩短后重试。", "text");
            if (changingDirectory) throw new InvalidOperationException("正在切换模型目录，请稍后再发送问题。");
            if (!Preferences.Enabled) throw new InvalidOperationException("尚未启用本地 AI。请先在助手设置中安装并启用模型，再开始对话。");
            if (safe) throw new InvalidOperationException("安全测试模式不会启动模型或生成 AI 回复。");
            if (!Service.IsModelInstalled(Preferences.SelectedModelId)) throw new InvalidOperationException("所选本地模型尚未安装。请先在助手设置中下载并启用模型。");
            if (chatting) throw new InvalidOperationException("上一条问题仍在处理，请等待完成或先取消。");

            chatting = true;
            int version = operationVersion;
            bool includeTitle = Preferences.IncludeWindowTitle;
            string modelId = Preferences.SelectedModelId;
            AssistantContextSnapshot snapshot = RefreshContext();
            bool entered = false;
            var source = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            chatRequest = source;
            CancelRequest(suggestionRequest);
            try
            {
                // The local runner has one inference slot. Cancellation must finish before
                // a chat starts; otherwise the service would reject it as another operation.
                await requests.WaitAsync(source.Token);
                entered = true;
                CheckChatOperation(version, modelId, includeTitle, source.Token);
                if (!Service.IsRunning) await Service.StartAsync(modelId, source.Token);
                CheckChatOperation(version, modelId, includeTitle, source.Token);
                LocalAiReply reply = await Service.ChatAsync(text.Trim(), snapshot.ModelContext, history, source.Token);
                CheckChatOperation(version, modelId, includeTitle, source.Token);
                LastError = null;
                return reply;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { if (!disposed && version == operationVersion) LastError = ex.Message; throw; }
            finally
            {
                if (entered) requests.Release();
                if (chatRequest == source) chatRequest = null;
                source.Dispose();
                chatting = false;
            }
        }

        private void CheckChatOperation(int version, string modelId, bool includeTitle, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (disposed || version != operationVersion || !Preferences.Enabled || modelId != Preferences.SelectedModelId || includeTitle != Preferences.IncludeWindowTitle)
                throw new OperationCanceledException("助手设置已更改，本次对话已取消。", token);
        }

        public async void Poll()
        {
            if (disposed || safe || polling || chatting || changingDirectory || (!Preferences.Enabled && !Preferences.RuleShortcutsEnabled)) return;
            AssistantContextSnapshot snapshot = AssistantContext.Capture(Preferences.IncludeWindowTitle);
            var now = DateTime.UtcNow;
            contextGate.Observe(snapshot.Fingerprint, now);
            if (snapshot.ProcessName.Length == 0) return;
            LastContext = snapshot;
            if (!canPresent() || (engine.ShutdownRemaining.HasValue && engine.ShutdownRemaining.Value.TotalSeconds <= 30) || Service.IsBusy) return;
            if (!contextGate.TryBegin(snapshot.Fingerprint, now)) return;
            polling = true; int version = operationVersion;
            bool includeTitle = Preferences.IncludeWindowTitle;
            bool entered = false;
            var sourceToken = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            suggestionRequest = sourceToken;
            try
            {
                await requests.WaitAsync(sourceToken.Token);
                entered = true;
                if (chatting || disposed || version != operationVersion) return;
                IList<LocalAiAction> actions = null; string source = "场景快捷操作";
                if (Preferences.Enabled && now >= retryAfter)
                {
                    try
                    {
                        if (!Service.IsModelInstalled(Preferences.SelectedModelId)) throw new InvalidOperationException("所选本地模型尚未安装，请先在助手设置中安装模型。");
                        if (!Service.IsRunning) await Service.StartAsync(Preferences.SelectedModelId, sourceToken.Token);
                        sourceToken.Token.ThrowIfCancellationRequested();
                        actions = await Service.SuggestContextAsync(snapshot.ModelContext, engine.Settings.Scene == UsageScene.Classroom ? "classroom" : "desktop", sourceToken.Token);
                        source = "本地 AI 建议"; LastError = null;
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) { if (version == operationVersion) { LastError = ex.Message; retryAfter = DateTime.UtcNow.AddSeconds(30); } }
                }
                if ((actions == null || actions.Count == 0) && Preferences.RuleShortcutsEnabled) { actions = RuleActions(snapshot); source = "场景快捷操作"; }
                sourceToken.Token.ThrowIfCancellationRequested();
                if (disposed || chatting || version != operationVersion || includeTitle != Preferences.IncludeWindowTitle || (!Preferences.Enabled && !Preferences.RuleShortcutsEnabled) || !canPresent() || (engine.ShutdownRemaining.HasValue && engine.ShutdownRemaining.Value.TotalSeconds <= 30)) return;
                AssistantContextSnapshot current = AssistantContext.Capture(Preferences.IncludeWindowTitle);
                if (current.Fingerprint != snapshot.Fingerprint) return;
                if (actions != null && actions.Count > 0)
                {
                    var handler = Suggested;
                    if (handler != null)
                    {
                        handler(new AssistantSuggestion { ProcessName = snapshot.ProcessName, ContextDescription = snapshot.Description, Source = source, Actions = actions });
                        contextGate.MarkPresented(snapshot.Fingerprint, DateTime.UtcNow);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!disposed && version == operationVersion) LastError = ex.Message; }
            finally
            {
                if (entered) requests.Release();
                if (suggestionRequest == sourceToken) suggestionRequest = null;
                sourceToken.Dispose(); polling = false;
            }
        }

        public static bool IsOwnProcess(string name)
        {
            return AssistantContext.IsOwnProcess(name);
        }

        public static IList<LocalAiAction> RuleActions(string processName)
        {
            return RuleActions(AssistantContext.Create(processName, "", false, false));
        }

        public static IList<LocalAiAction> RuleActions(AssistantContextSnapshot snapshot)
        {
            if (snapshot == null) return new List<LocalAiAction>();
            if (snapshot.Category == "media")
                return new List<LocalAiAction> { new LocalAiAction { Kind = "volume", Title = "调节系统音量" }, new LocalAiAction { Kind = "media_toggle", Title = "播放 / 暂停" } };
            if (snapshot.Category == "presentation")
                return new List<LocalAiAction> { new LocalAiAction { Kind = "countdown", Title = "课堂练习 · 5 分钟", Seconds = 300 }, new LocalAiAction { Kind = "open_timer", Title = "打开计时器" } };
            if (snapshot.Category == "editing")
                return new List<LocalAiAction> { new LocalAiAction { Kind = "countdown", Title = "专注 · 25 分钟", Seconds = 1500 }, new LocalAiAction { Kind = "open_reminders", Title = "添加日程" } };
            return new List<LocalAiAction>();
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true; operationVersion++; CancelRequests(); lifetime.Cancel(); Service.Dispose(); lifetime.Dispose();
        }
    }
}
