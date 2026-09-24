using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using FreeIsland;

internal static class LocalAiTests
{
    private static int checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    private static void Reject(Action action, string message) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, message); }
    private static string Json(string kind, string title = "调节音量", int seconds = 0) { return "{\"actions\":[{\"kind\":" + LocalAiActionRules.Escape(kind) + ",\"title\":" + LocalAiActionRules.Escape(title) + ",\"seconds\":" + seconds + "}]}"; }
    private static string Hash(byte[] input) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant(); }
    private sealed class ForbiddenTransport : ILocalAiTransport
    {
        public int Calls;
        public void Download(Uri uri, string path, long count, Action<long> progress, CancellationToken token) { Calls++; throw new Exception("A safe fixture must never download"); }
    }
    private sealed class BlockingTransport : ILocalAiTransport
    {
        internal readonly ManualResetEventSlim Started = new ManualResetEventSlim();
        public void Download(Uri uri, string path, long count, Action<long> progress, CancellationToken token)
        {
            File.WriteAllText(path, "partial fixture only"); Started.Set(); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested();
        }
    }
    private sealed class CorruptTransport : ILocalAiTransport
    {
        public void Download(Uri uri, string path, long count, Action<long> progress, CancellationToken token) { File.WriteAllText(path, "wrong bytes cannot become executable"); }
    }
    private sealed class ProbeTransport : ILocalAiTransport
    {
        private readonly string source;
        internal ProbeTransport(string source) { this.source = source; }
        public void Download(Uri uri, string path, long count, Action<long> progress, CancellationToken token)
        {
            string file = uri == LocalAiCatalog.RuntimeUrl ? "runtime.zip" : uri == LocalAiCatalog.Models[0].DownloadUrl ? "model.gguf" : null;
            if (file == null) throw new Exception("Probe can only use the two verified local artifacts");
            using (var input = File.OpenRead(Path.Combine(source, file)))
            using (var output = new FileStream(path, FileMode.CreateNew))
            {
                byte[] buffer = new byte[65536]; int read; long copied = 0;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); copied += read; progress(copied); }
                Check(copied == count, "Probe fixture matches pinned asset length");
            }
        }
    }
    private static void Rules(string root)
    {
        Check(LocalAiCatalog.Models.Count == 3, "Three real parameter sizes");
        Check(LocalAiCatalog.Models.Select(m => m.Parameters).SequenceEqual(new[] { "0.6B", "1.7B", "4B" }), "Truthful model labels");
        foreach (var m in LocalAiCatalog.Models)
        {
            Check(m.DownloadBytes > 0 && m.DownloadBytes < 3L * 1024 * 1024 * 1024 && m.Sha256.Length == 64, "Pinned bounded model");
            Check(m.DownloadUrl.Scheme == "https" && m.DownloadUrl.Host == "huggingface.co" && !m.DownloadUrl.AbsolutePath.Contains("/main/"), "Immutable official model source");
        }
        Reject(delegate { LocalAiCatalog.Find("../../evil"); }, "Model ids cannot become paths");
        foreach (string kind in new[] { "volume", "media_toggle", "open_timer", "open_reminders" }) Check(LocalAiActionRules.Parse(Json(kind)).Count == 1, "Known action parsed");
        Check(LocalAiActionRules.Parse(Json("countdown", "讨论五分钟", 300))[0].Seconds == 300, "AI can create labeled parameterized timer shortcut");
        Check(LocalAiActionRules.Parse("{\"actions\":[]}").Count == 0, "No relevant suggestion supported");
        foreach (string kind in new[] { "shutdown", "shell", "download", "exec", "volume\n" }) Reject(delegate { LocalAiActionRules.Parse(Json(kind)); }, "Unknown/unsafe kind rejected");
        foreach (int value in new[] { -1, 0, 59, 14401, int.MaxValue }) Reject(delegate { LocalAiActionRules.Parse(Json("countdown", "测试", value)); }, "Timer bounds enforced");
        Reject(delegate { LocalAiActionRules.Parse(Json("volume", "音量", 1)); }, "Unexpected action parameters rejected");
        Reject(delegate { LocalAiActionRules.Parse(Json("volume", "\u202e伪造")); }, "Bidi formatting rejected");
        Reject(delegate { LocalAiActionRules.Parse(Json("volume", "\n错误")); }, "Control characters rejected");
        Reject(delegate { LocalAiActionRules.Parse(Json("volume", new string('a', 25))); }, "Oversized label rejected");
        Reject(delegate { LocalAiActionRules.Parse("{\"actions\":[{\"kind\":\"volume\",\"title\":\"a\"},{\"kind\":\"volume\",\"title\":\"b\"}]}"); }, "Duplicate actions rejected");
        Reject(delegate { LocalAiActionRules.Parse("{\"actions\":[null]}"); }, "Null action rejected");
        Reject(delegate { LocalAiActionRules.Parse(new string('x', 8193)); }, "Inference output bounded");
        Check(LocalAiContext.NormalizeProcessName("VLC") == "vlc", "Foreground identifier normalized");
        foreach (string invalid in new[] { "vlc\nignore system", "C:\\secret.txt", new string('p', 65), "window title content", "" }) Check(LocalAiContext.NormalizeProcessName(invalid) == "", "Non-process context rejected");
        string request = LocalAiActionRules.Request("vlc", "classroom");
        Check(request.Contains("max_tokens\":192") && request.Contains("enable_thinking\":false") && request.Contains("\"json_schema\":{\"name\":\"local_shortcuts\""), "Generation is bounded and uses the runtime's actual nested schema field");
        Reject(delegate { LocalAiActionRules.Request("cmd; shutdown", "desktop"); }, "Prompt injection string excluded at context boundary");
        string arguments = LocalAiService.ServerArguments(@"D:\safe model\model.gguf", 43210);
        Check(arguments.Contains("--host 127.0.0.1") && arguments.Contains("--parallel 1") && arguments.Contains("--ctx-size 1024") && arguments.Contains("--n-gpu-layers 0") && arguments.Contains("--no-webui"), "Server constrained to CPU and localhost");
        Check(!arguments.Contains("api-key"), "Secret omitted from process command line");
        Reject(delegate { LocalAiService.ServerArguments("x\" --host evil", 43210); }, "Command-line quote injection rejected");
        Reject(delegate { LocalAiService.ServerArguments("model.gguf", 80); }, "Privileged/invalid ports rejected");
        foreach (string bad in new[] { "http://huggingface.co/a", "https://evil.test/a", "https://huggingface.co.evil.test/a", "https://a:h@huggingface.co/a", "https://huggingface.co:444/a", "https://github.com/a#b" }) Check(!LocalAiTransport.AllowedUri(new Uri(bad)), "Untrusted redirect rejected");
        foreach (string good in new[] { "https://huggingface.co/a", "https://release-assets.githubusercontent.com/a", "https://cas-bridge.xethub.hf.co/a" }) Check(LocalAiTransport.AllowedUri(new Uri(good)), "Official download delivery allowed");
        Reject(delegate { LocalAiStorage.Inside(root, "../escape"); }, "Path traversal blocked");
        foreach (string bad in new[] { "../evil.dll", "/evil.dll", "sub/../../evil.dll", "C:/evil.dll", "sub/./evil.dll" }) Reject(delegate { LocalAiStorage.RuntimeEntryName(bad); }, "Archive traversal rejected");
        Check(LocalAiStorage.RuntimeEntryName("bin/llama-server.exe") == "llama-server.exe", "Known executable extracted");
        Check(LocalAiStorage.RuntimeEntryName("llama-cli.exe") == null && LocalAiStorage.RuntimeEntryName("evil.cmd") == null, "Only server executable retained");
        byte[] bytes = Encoding.ASCII.GetBytes("GGUF-test fixture"); string file = Path.Combine(root, "fixture.gguf"); File.WriteAllBytes(file, bytes);
        Check(LocalAiStorage.Verify(file, bytes.Length, Hash(bytes), true, CancellationToken.None), "Integrity check accepts exact model fixture");
        Check(!LocalAiStorage.Verify(file, bytes.Length + 1, Hash(bytes), true, CancellationToken.None), "Truncated model rejected");
        Check(!LocalAiStorage.Verify(file, bytes.Length, new string('0', 64), true, CancellationToken.None), "Wrong model digest rejected");
        File.WriteAllBytes(file, Encoding.ASCII.GetBytes("HTML not a model")); Check(!LocalAiStorage.Verify(file, 16, Hash(Encoding.ASCII.GetBytes("HTML not a model")), true, CancellationToken.None), "Wrong magic rejected even when bytes match supplied fixture digest");
        using (var source = new CancellationTokenSource()) { source.Cancel(); Reject(delegate { LocalAiStorage.Verify(file, 16, "", false, source.Token); }, "Hashing cancellation honored"); }
        var transport = new ForbiddenTransport();
        using (var service = new LocalAiService(Path.Combine(root, "safe"), true, transport))
        {
            Check(!service.IsRunning && !service.IsBusy && service.InstalledModelId == null, "AI default remains stopped and uninstalled");
            Reject(delegate { service.InstallAsync(LocalAiCatalog.Models[0].Id, CancellationToken.None).GetAwaiter().GetResult(); }, "Safe mode blocks install");
            Reject(delegate { service.StartAsync(LocalAiCatalog.Models[0].Id, CancellationToken.None).GetAwaiter().GetResult(); }, "Safe mode blocks runtime");
            Check(transport.Calls == 0 && !service.IsBusy, "Safe tests never network and cleanup busy state");
        }
        if (LocalAiService.GetSupportMessage() == null)
        {
            string canceledRoot = Path.Combine(root, "canceled"); var blocked = new BlockingTransport();
            using (var service = new LocalAiService(canceledRoot, false, blocked))
            {
                var pending = service.InstallAsync(LocalAiCatalog.Models[0].Id, CancellationToken.None);
                Check(blocked.Started.Wait(5000), "Injected download reached cancellation point");
                Reject(delegate { service.InstallAsync(LocalAiCatalog.Models[1].Id, CancellationToken.None); }, "Concurrent installation rejected");
                service.CancelInstall(); Reject(delegate { pending.GetAwaiter().GetResult(); }, "Cancellation reaches download worker");
                Check(!service.IsBusy && service.InstalledModelId == null, "Canceled installation cannot become ready");
                Check(Directory.GetDirectories(canceledRoot, ".stage-*").Length == 0 && Directory.GetFiles(canceledRoot).Length == 0, "Partial download and staging removed after cancellation");
            }
            string corruptRoot = Path.Combine(root, "corrupt");
            using (var service = new LocalAiService(corruptRoot, false, new CorruptTransport()))
            {
                Reject(delegate { service.InstallAsync(LocalAiCatalog.Models[0].Id, CancellationToken.None).GetAwaiter().GetResult(); }, "Injected corrupted download fails integrity check");
                Check(!service.IsRunning && service.InstalledModelId == null && Directory.GetFileSystemEntries(corruptRoot).Length == 0, "Corrupted runtime never installed or launched and staging removed");
            }
        }
        string zipPath = Path.Combine(root, "traversal.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create)) { var entry = zip.CreateEntry("../evil.dll"); using (var stream = entry.Open()) stream.WriteByte(1); }
        Reject(delegate { LocalAiStorage.ExtractRuntime(zipPath, Path.Combine(root, "extract"), CancellationToken.None); }, "Real malicious ZIP entry rejected");
        Check(!File.Exists(Path.Combine(root, "evil.dll")), "Extraction did not escape destination");
        string duplicate = Path.Combine(root, "duplicate.zip");
        using (var zip = ZipFile.Open(duplicate, ZipArchiveMode.Create)) { foreach (string path in new[] { "one/llama-server.exe", "two/llama-server.exe" }) { var entry = zip.CreateEntry(path); using (var stream = entry.Open()) stream.WriteByte(1); } }
        Reject(delegate { LocalAiStorage.ExtractRuntime(duplicate, Path.Combine(root, "duplicate"), CancellationToken.None); }, "ZIP basename collisions rejected");
    }
    private static void Probe(string root)
    {
        string destination = Path.Combine(root, "installed");
        using (var service = new LocalAiService(destination, false, new ProbeTransport(root)))
        {
            string previous = null; service.Changed += delegate { if (service.Status != previous) { previous = service.Status; Console.WriteLine(service.Status); } };
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4)))
            {
                service.InstallAsync(LocalAiCatalog.Models[0].Id, timeout.Token).GetAwaiter().GetResult();
                Check(service.IsModelInstalled(LocalAiCatalog.Models[0].Id), "Real smallest model installed with pinned integrity");
                service.StartAsync(LocalAiCatalog.Models[0].Id, timeout.Token).GetAwaiter().GetResult();
                Check(service.IsRunning, "Owned llama.cpp process loaded real model");
                int port = (int)typeof(LocalAiService).GetField("port", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(service);
                bool unauthorized = false;
                try
                {
                    var unauthenticated = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/v1/chat/completions");
                    unauthenticated.Proxy = null; unauthenticated.AllowAutoRedirect = false; unauthenticated.Method = "POST"; unauthenticated.ContentLength = 0; unauthenticated.Timeout = 2000;
                    using (var response = unauthenticated.GetResponse()) { }
                }
                catch (WebException ex) { var response = ex.Response as HttpWebResponse; if (response != null) { unauthorized = response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden; response.Dispose(); } }
                Check(unauthorized, "Real server rejects unauthenticated inference");
                var sender = typeof(LocalAiService).GetMethod("Send", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                byte[] raw = (byte[])sender.Invoke(service, new object[] { "/v1/chat/completions", Encoding.UTF8.GetBytes(LocalAiActionRules.Request("vlc", "desktop")), timeout.Token, 30000, 32768 });
                Console.WriteLine("Synthetic raw response: " + Encoding.UTF8.GetString(raw));
                Check(LocalAiActionRules.ParseChat(raw).All(a => a.Kind == "countdown" || a.Seconds == 0), "Real schema enforces non-timer parameter constraint");
                var actions = service.SuggestAsync("vlc", "desktop", timeout.Token).GetAwaiter().GetResult();
                Console.WriteLine("Synthetic vlc suggestions: " + string.Join(", ", actions.Select(a => a.Kind + ":" + a.Title + ":" + a.Seconds)));
                Check(actions.Count > 0 && actions.Any(a => a.Kind == "volume"), "Real model recommends volume for synthetic player context");
                service.Stop(); Check(!service.IsRunning, "Stopping releases only owned llama.cpp process");
            }
        }
    }
    public static int Main(string[] args)
    {
        try
        {
            string root = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("artifacts", "local-ai-tests-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(root);
            if (args.Length > 1 && args[1] == "--probe") Probe(root); else Rules(root);
            Console.WriteLine("Local AI: " + checks + " checks passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
