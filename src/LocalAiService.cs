using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FreeIsland
{
    public sealed class LocalAiService : IDisposable
    {
        private readonly string root;
        private readonly bool safeMode;
        private readonly ILocalAiTransport transport;
        private readonly object sync = new object();
        private CancellationTokenSource operation;
        private Process server;
        private LocalAiOwnedJob job;
        private FileStream modelLease;
        private string apiKey, runningModel, status = "本地 AI 未启动";
        private int port;
        private double progress;
        private bool busy, disposed;
        public event Action Changed;
        public string Status { get { lock (sync) return status; } }
        public double Progress { get { lock (sync) return progress; } }
        public bool IsBusy { get { lock (sync) return busy; } }
        public bool IsRunning { get { lock (sync) return server != null && !server.HasExited && runningModel != null; } }
        public string InstalledModelId
        {
            get
            {
                try
                {
                    string marker = LocalAiStorage.Inside(root, "installed-model.txt");
                    if (!File.Exists(marker) || new FileInfo(marker).Length > 100) return null;
                    string id = File.ReadAllText(marker).Trim(); var model = LocalAiCatalog.Find(id);
                    return File.Exists(ModelPath(model)) ? id : null;
                }
                catch { return null; }
            }
        }
        public static string GetSupportMessage() { return LocalAiPlatform.GetSupportMessage(); }
        public LocalAiService(string dataDirectory, bool safeMode) : this(dataDirectory, safeMode, new LocalAiTransport()) { }
        internal LocalAiService(string dataDirectory, bool safeMode, ILocalAiTransport transport)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentException("AI 数据目录不能为空。");
            root = Path.GetFullPath(dataDirectory); LocalAiStorage.EnsurePlainPath(root); this.safeMode = safeMode; this.transport = transport;
        }
        private string ArchivePath { get { return LocalAiStorage.Inside(root, "runtime-" + LocalAiCatalog.RuntimeVersion + ".zip"); } }
        private string RuntimePath { get { return LocalAiStorage.Inside(root, "runtime-" + LocalAiCatalog.RuntimeVersion); } }
        private string ModelPath(LocalAiModel model) { return LocalAiStorage.Inside(root, "models/" + model.Id + ".gguf"); }
        public bool IsModelInstalled(string id)
        {
            try { var model = LocalAiCatalog.Find(id); string path = ModelPath(model); return File.Exists(path) && new FileInfo(path).Length == model.DownloadBytes; }
            catch { return false; }
        }
        private void Notify() { var changed = Changed; if (changed != null) try { changed(); } catch { } }
        private void State(string text, double value) { lock (sync) { status = text; progress = Math.Max(0, Math.Min(1, value)); } Notify(); }
        private Task Run(Action<CancellationToken> action, CancellationToken token)
        {
            CancellationTokenSource source;
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException("LocalAiService");
                if (busy) throw new InvalidOperationException("AI 正在处理另一个操作。");
                busy = true; source = CancellationTokenSource.CreateLinkedTokenSource(token); operation = source;
            }
            Notify();
            return Task.Run(delegate
            {
                try { source.Token.ThrowIfCancellationRequested(); action(source.Token); }
                catch (Exception ex)
                {
                    if (source.IsCancellationRequested || ex is OperationCanceledException) { State("AI 操作已取消", 0); throw new OperationCanceledException(source.Token); }
                    State("AI 操作未完成：" + FriendlyError(ex), 0); throw;
                }
                finally
                {
                    lock (sync) { if (operation == source) operation = null; busy = false; }
                    source.Dispose(); Notify();
                }
            });
        }
        private static string FriendlyError(Exception ex)
        {
            if (ex is WebException) return "网络连接失败，请检查 GitHub / Hugging Face 连接后重试。";
            if (ex is UnauthorizedAccessException) return "AI 数据目录访问被拒绝。";
            if (ex is System.ComponentModel.Win32Exception) return "运行时无法启动；请检查系统版本、处理器和系统运行库。";
            return ex.Message.Length > 180 ? ex.Message.Substring(0, 180) : ex.Message;
        }
        private void RequireSupport()
        {
            if (safeMode) throw new InvalidOperationException("安全测试模式不会下载或启动模型。");
            string unsupported = GetSupportMessage(); if (unsupported != null) throw new PlatformNotSupportedException(unsupported);
        }
        public Task InstallAsync(string modelId, CancellationToken token)
        {
            var model = LocalAiCatalog.Find(modelId);
            return Run(delegate(CancellationToken ct)
            {
                RequireSupport(); StopRunner(); LocalAiStorage.EnsurePlainPath(root); Directory.CreateDirectory(root);
                string stageName = ".stage-" + Guid.NewGuid().ToString("N"), stage = LocalAiStorage.Inside(root, stageName); Directory.CreateDirectory(stage);
                try
                {
                    long expected = model.DownloadBytes + LocalAiCatalog.RuntimeBytes;
                    State("正在检查已下载的运行时…", 0);
                    if (!LocalAiStorage.Verify(ArchivePath, LocalAiCatalog.RuntimeBytes, LocalAiCatalog.RuntimeSha256, false, ct))
                    {
                        string pending = LocalAiStorage.Inside(stage, "runtime.zip"); var update = Stopwatch.StartNew();
                        transport.Download(LocalAiCatalog.RuntimeUrl, pending, LocalAiCatalog.RuntimeBytes, delegate(long count) { if (update.ElapsedMilliseconds > 150) { State("下载 llama.cpp 运行时…", count / (double)expected); update.Restart(); } }, ct);
                        if (!LocalAiStorage.Verify(pending, LocalAiCatalog.RuntimeBytes, LocalAiCatalog.RuntimeSha256, false, ct)) throw new InvalidDataException("运行时 SHA-256 校验失败，未安装。");
                        ReplaceFile(pending, ArchivePath, ct);
                    }
                    if (!LocalAiStorage.VerifyRuntime(ArchivePath, RuntimePath, ct))
                    {
                        string unpacked = LocalAiStorage.Inside(stage, "runtime"); LocalAiStorage.ExtractRuntime(ArchivePath, unpacked, ct);
                        ct.ThrowIfCancellationRequested(); LocalAiStorage.DeleteOwnedTree(root, "runtime-" + LocalAiCatalog.RuntimeVersion); Directory.Move(unpacked, RuntimePath);
                    }
                    State("正在检查 " + model.Name + "…", LocalAiCatalog.RuntimeBytes / (double)expected);
                    if (!LocalAiStorage.Verify(ModelPath(model), model.DownloadBytes, model.Sha256, true, ct))
                    {
                        var drive = new DriveInfo(Path.GetPathRoot(root));
                        if (drive.AvailableFreeSpace < model.DownloadBytes + 256L * 1024 * 1024) throw new IOException("磁盘空间不足，请至少留出模型大小加 256 MB 的空间。");
                        string pending = LocalAiStorage.Inside(stage, "model.gguf"); var update = Stopwatch.StartNew();
                        transport.Download(model.DownloadUrl, pending, model.DownloadBytes, delegate(long count) { if (update.ElapsedMilliseconds > 150) { State("下载 " + model.Name + "…", (LocalAiCatalog.RuntimeBytes + count) / (double)expected); update.Restart(); } }, ct);
                        State("校验模型 SHA-256…", 0.99);
                        if (!LocalAiStorage.Verify(pending, model.DownloadBytes, model.Sha256, true, ct)) throw new InvalidDataException("模型 SHA-256 校验失败，未安装。");
                        Directory.CreateDirectory(LocalAiStorage.Inside(root, "models")); ReplaceFile(pending, ModelPath(model), ct);
                    }
                    ct.ThrowIfCancellationRequested(); string marker = LocalAiStorage.Inside(stage, "installed-model.txt"); File.WriteAllText(marker, model.Id); ReplaceFile(marker, LocalAiStorage.Inside(root, "installed-model.txt"), ct);
                    State(model.Name + " 已安装，等待启用", 1);
                }
                finally { try { LocalAiStorage.DeleteOwnedTree(root, stageName); } catch { } }
            }, token);
        }
        private static void ReplaceFile(string source, string destination, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); LocalAiStorage.EnsurePlainPath(source); LocalAiStorage.EnsurePlainPath(destination);
            if (File.Exists(destination)) File.Replace(source, destination, null); else File.Move(source, destination);
        }
        public void CancelInstall() { lock (sync) { if (operation != null) operation.Cancel(); } }
        public Task StartAsync(string modelId, CancellationToken token)
        {
            var model = LocalAiCatalog.Find(modelId);
            return Run(delegate(CancellationToken ct)
            {
                RequireSupport(); StopRunner(); State("检查运行时及模型完整性…", 0);
                if (!LocalAiStorage.VerifyRuntime(ArchivePath, RuntimePath, ct) || !LocalAiStorage.Verify(ModelPath(model), model.DownloadBytes, model.Sha256, true, ct)) throw new InvalidDataException("模型或运行时尚未安装，或完整性检查失败，请重新安装所选模型。");
                ct.ThrowIfCancellationRequested();
                try
                {
                    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int chosenPort = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
                    byte[] random = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(random); string key = Convert.ToBase64String(random);
                    var info = new ProcessStartInfo(LocalAiStorage.Inside(RuntimePath, "llama-server.exe"), ServerArguments(ModelPath(model), chosenPort));
                    info.UseShellExecute = false; info.CreateNoWindow = true; info.WindowStyle = ProcessWindowStyle.Hidden; info.WorkingDirectory = RuntimePath; info.RedirectStandardOutput = true; info.RedirectStandardError = true;
                    info.EnvironmentVariables.Clear(); string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    info.EnvironmentVariables["SystemRoot"] = windows; info.EnvironmentVariables["WINDIR"] = windows; info.EnvironmentVariables["PATH"] = Path.Combine(windows, "System32");
                    info.EnvironmentVariables["TEMP"] = Path.GetTempPath(); info.EnvironmentVariables["TMP"] = Path.GetTempPath(); info.EnvironmentVariables["LLAMA_API_KEY"] = key;
                    var owned = new Process { StartInfo = info }; owned.OutputDataReceived += delegate { }; owned.ErrorDataReceived += delegate { };
                    lock (sync)
                    {
                        ct.ThrowIfCancellationRequested(); if (disposed) throw new ObjectDisposedException("LocalAiService");
                        modelLease = new FileStream(ModelPath(model), FileMode.Open, FileAccess.Read, FileShare.Read);
                        job = new LocalAiOwnedJob(); server = owned; apiKey = key; port = chosenPort;
                        if (!owned.Start()) throw new IOException("无法启动本地模型服务。");
                        job.Attach(owned); owned.BeginOutputReadLine(); owned.BeginErrorReadLine();
                        try { owned.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                    }
                    State("正在载入 " + model.Name + "…", 0);
                    var watch = Stopwatch.StartNew(); bool ready = false;
                    while (watch.Elapsed < TimeSpan.FromSeconds(90))
                    {
                        ct.ThrowIfCancellationRequested(); lock (sync) { if (server == null || server.HasExited) throw new IOException("本地模型服务提前退出；系统可能缺少运行库，或处理器不兼容。"); }
                        try { Send("/health", null, ct, 2500, 2048); ready = true; break; } catch (WebException) { } catch (IOException) { }
                        if (ct.WaitHandle.WaitOne(400)) ct.ThrowIfCancellationRequested();
                    }
                    if (!ready) throw new TimeoutException("模型载入超过 90 秒，已停止。可尝试更小的模型。");
                    lock (sync) { ct.ThrowIfCancellationRequested(); runningModel = model.Id; }
                    State(model.Name + " 本地运行中", 1);
                }
                catch { StopRunner(); throw; }
            }, token);
        }
        internal static string ServerArguments(string model, int chosenPort)
        {
            if (chosenPort < 1024 || chosenPort > 65535 || model.IndexOf('"') >= 0) throw new ArgumentException("本地服务参数无效。");
            return "--model \"" + model + "\" --host 127.0.0.1 --port " + chosenPort + " --alias local --ctx-size 1024 --parallel 1 --threads " + Math.Max(1, Math.Min(2, Environment.ProcessorCount)) + " --threads-batch " + Math.Max(1, Math.Min(2, Environment.ProcessorCount)) + " --n-gpu-layers 0 --no-webui --no-warmup --jinja";
        }
        public Task<IList<LocalAiAction>> SuggestAsync(string processName, string scene, CancellationToken token)
        {
            string body = LocalAiActionRules.Request(processName, scene);
            return SuggestBodyAsync(body, token);
        }
        public Task<IList<LocalAiAction>> SuggestContextAsync(string context, string scene, CancellationToken token)
        {
            return SuggestBodyAsync(LocalAiConversationRules.ContextRequest(context, scene), token);
        }
        private Task<IList<LocalAiAction>> SuggestBodyAsync(string body, CancellationToken token)
        {
            IList<LocalAiAction> result = null;
            return Run(delegate(CancellationToken ct)
            {
                RequireSupport(); if (!IsRunning) throw new InvalidOperationException("请先启动已安装的本地模型。");
                State("正在为当前应用生成快捷操作…", 1);
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(30));
                    try { result = LocalAiActionRules.ParseChat(Send("/v1/chat/completions", Encoding.UTF8.GetBytes(body), deadline.Token, 30000, 32768)); }
                    catch { if (deadline.IsCancellationRequested) { StopRunner(); throw new OperationCanceledException("推理已取消或超过 30 秒，模型已停止。", deadline.Token); } throw; }
                }
                State("本地 AI 已就绪 · 操作等待点击", 1);
            }, token).ContinueWith<IList<LocalAiAction>>(task => { task.GetAwaiter().GetResult(); return result; }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        public Task<LocalAiReply> ChatAsync(string userText, string context, IList<LocalAiTurn> history, CancellationToken token)
        {
            var prompt = LocalAiConversationRules.Prepare(userText, context, history);
            LocalAiReply result = null;
            return Run(delegate(CancellationToken ct)
            {
                RequireSupport(); if (!IsRunning) throw new InvalidOperationException("请先启动已安装的本地模型。");
                State("正在思考你的问题…", 1);
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(30));
                    try
                    {
                        // Count the actual pinned model's template tokens rather than assuming a
                        // Latin character/token ratio for Chinese text. All requests stay on localhost.
                        for (int attempt = 0; attempt < 2; attempt++)
                        {
                            bool fits = true;
                            while (true)
                            {
                                deadline.Token.ThrowIfCancellationRequested();
                                string applied = LocalAiConversationRules.TemplatePrompt(Send("/apply-template", Encoding.UTF8.GetBytes(prompt.TemplateRequest()), deadline.Token, 5000, 32768));
                                string tokenize = "{\"content\":" + LocalAiActionRules.Escape(applied) + ",\"add_special\":true,\"parse_special\":true,\"with_pieces\":false}";
                                int count = LocalAiConversationRules.TokenCount(Send("/tokenize", Encoding.UTF8.GetBytes(tokenize), deadline.Token, 5000, 65536));
                                if (count <= LocalAiConversationRules.MaximumPromptTokens) break;
                                if (!prompt.TrimForBudget())
                                {
                                    if (attempt > 0) { fits = false; break; }
                                    throw new ArgumentException("这条消息超出了小模型的上下文容量，请缩短后再试。", "userText");
                                }
                            }
                            if (!fits) break;
                            result = LocalAiConversationRules.ParseChat(Send("/v1/chat/completions", Encoding.UTF8.GetBytes(prompt.Request()), deadline.Token, 30000, 32768));
                            if (attempt != 0 || !LocalAiConversationRules.NeedsActionRepair(result)) break;
                            prompt.ActionDraftText = LocalAiConversationRules.Clip(result.Text, 160);
                        }
                        result = LocalAiConversationRules.WithoutMissingActionPromise(result);
                    }
                    catch { if (deadline.IsCancellationRequested) { StopRunner(); throw new OperationCanceledException("对话已取消或超过 30 秒，模型已停止。", deadline.Token); } throw; }
                }
                State("本地 AI 已回复 · 快捷操作等待点击", 1);
            }, token).ContinueWith<LocalAiReply>(task => { task.GetAwaiter().GetResult(); return result; }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        private byte[] Send(string path, byte[] body, CancellationToken token, int timeout, int maximum)
        {
            int currentPort; string key;
            lock (sync) { if (server == null || server.HasExited || string.IsNullOrEmpty(apiKey)) throw new IOException("本地模型未运行。"); currentPort = port; key = apiKey; }
            var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + currentPort + path);
            request.Proxy = null; request.AllowAutoRedirect = false; request.Timeout = timeout; request.ReadWriteTimeout = timeout; request.Headers["Authorization"] = "Bearer " + key;
            request.Method = body == null ? "GET" : "POST"; request.KeepAlive = false;
            using (token.Register(request.Abort))
            {
                if (body != null) { request.ContentType = "application/json"; request.ContentLength = body.Length; using (var output = request.GetRequestStream()) output.Write(body, 0, body.Length); }
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > maximum) throw new IOException("本地模型响应无效。");
                    using (var input = response.GetResponseStream())
                    using (var memory = new MemoryStream())
                    {
                        byte[] buffer = new byte[4096]; int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); if (memory.Length + read > maximum) throw new IOException("本地模型响应过大。"); memory.Write(buffer, 0, read); }
                        return memory.ToArray();
                    }
                }
            }
        }
        private void StopRunner()
        {
            lock (sync)
            {
                runningModel = null; apiKey = null; port = 0;
                if (server != null)
                {
                    try { if (!server.HasExited) { server.Kill(); server.WaitForExit(1500); } } catch { }
                    server.Dispose(); server = null;
                }
                if (job != null) { job.Dispose(); job = null; }
                if (modelLease != null) { modelLease.Dispose(); modelLease = null; }
            }
        }
        public void Stop() { CancelInstall(); StopRunner(); State("本地 AI 已停止，模型内存已释放", 0); }
        public void Dispose() { lock (sync) { if (disposed) return; disposed = true; } Stop(); }
    }
    internal sealed class LocalAiOwnedJob : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
        {
            public long PerProcessUserTime, PerJobUserTime; public uint Flags; public UIntPtr MinimumWorkingSet, MaximumWorkingSet; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int info, ref ExtendedLimits limits, uint length);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        private IntPtr handle;
        internal LocalAiOwnedJob()
        {
            handle = CreateJobObject(IntPtr.Zero, null); if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE, including unexpected application exit.
            if (!SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimits)))) { int error = Marshal.GetLastWin32Error(); Dispose(); throw new System.ComponentModel.Win32Exception(error); }
        }
        internal void Attach(Process process) { if (!AssignProcessToJobObject(handle, process.Handle)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } GC.SuppressFinalize(this); }
        ~LocalAiOwnedJob() { Dispose(); }
    }
}
