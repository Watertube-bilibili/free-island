using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace FreeIsland
{
    [DataContract] internal sealed class GitHubRelease
    {
        [DataMember(Name = "tag_name")] public string Tag { get; set; }
        [DataMember(Name = "draft", IsRequired = true)] public bool Draft { get; set; }
        [DataMember(Name = "prerelease", IsRequired = true)] public bool Prerelease { get; set; }
        [DataMember(Name = "assets")] public List<GitHubAsset> Assets { get; set; }
    }
    [DataContract] internal sealed class GitHubAsset
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "browser_download_url")] public string Url { get; set; }
        [DataMember(Name = "digest")] public string Digest { get; set; }
        [DataMember(Name = "size")] public long Size { get; set; }
        [DataMember(Name = "state")] public string State { get; set; }
    }
    internal sealed class UpdatePackage
    {
        public string Version, FileName, Hash;
        public Uri Url, ChecksumUrl;
        public long Size;
    }

    internal static class UpdateRules
    {
        internal const string Repository = "https://github.com/Watertube-bilibili/free-island";
        internal const string LatestApi = "https://api.github.com/repos/Watertube-bilibili/free-island/releases/latest";
        internal const long MaximumInstallerBytes = 128L * 1024 * 1024;
        internal const int MaximumMetadataBytes = 2 * 1024 * 1024;
        internal const int MaximumChecksumBytes = 128 * 1024;
        private static readonly Regex StableVersion = new Regex(@"\Av?(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\z", RegexOptions.CultureInvariant);
        private static readonly Regex HashPattern = new Regex(@"\A[a-fA-F0-9]{64}\z", RegexOptions.CultureInvariant);

        internal static bool TryVersion(string tag, out Version version)
        {
            version = null; var match = StableVersion.Match(tag ?? ""); if (!match.Success) return false;
            version = new Version(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)); return true;
        }
        internal static UpdatePackage ParseRelease(byte[] json, Version current)
        {
            if (json == null || json.Length > MaximumMetadataBytes) throw new InvalidDataException("发布信息过大。");
            GitHubRelease release;
            using (var stream = new MemoryStream(json)) release = (GitHubRelease)new DataContractJsonSerializer(typeof(GitHubRelease)).ReadObject(stream);
            Version version;
            if (release == null || release.Draft || release.Prerelease || !TryVersion(release.Tag, out version)) return null;
            var installed = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
            if (version <= installed) return null;
            string number = version.ToString(3), name = "FreeIsland-Setup-" + number + ".exe";
            var assets = release.Assets ?? new List<GitHubAsset>();
            var installers = assets.Where(a => a != null && a.Name == name).ToList();
            if (installers.Count != 1) throw new InvalidDataException("没有唯一匹配的 Windows 10/11 安装包。");
            var installer = installers[0];
            if (installer.State != "uploaded" || installer.Size <= 0 || installer.Size > MaximumInstallerBytes) throw new InvalidDataException("安装包大小或状态无效。");
            var result = new UpdatePackage { Version = number, FileName = name, Size = installer.Size, Url = ExactAssetUri(installer.Url, release.Tag, name) };
            if (!string.IsNullOrEmpty(installer.Digest))
            {
                if (!installer.Digest.StartsWith("sha256:", StringComparison.Ordinal) || !HashPattern.IsMatch(installer.Digest.Substring(7))) throw new InvalidDataException("安装包校验格式无效。");
                result.Hash = installer.Digest.Substring(7).ToLowerInvariant();
            }
            else
            {
                string checksumName = "SHA256SUMS-" + number + ".txt";
                var checksums = assets.Where(a => a != null && a.Name == checksumName).ToList();
                if (checksums.Count != 1 || checksums[0].State != "uploaded" || checksums[0].Size <= 0 || checksums[0].Size > MaximumChecksumBytes) throw new InvalidDataException("发布缺少 SHA-256 校验信息。");
                result.ChecksumUrl = ExactAssetUri(checksums[0].Url, release.Tag, checksumName);
            }
            return result;
        }
        internal static Uri ExactAssetUri(string value, string tag, string name)
        {
            string expected = Repository + "/releases/download/" + tag + "/" + name;
            Uri uri; if (!string.Equals(value, expected, StringComparison.Ordinal) || !Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) throw new InvalidDataException("安装包地址不属于官方仓库的当前版本。");
            return uri;
        }
        internal static string ManifestHash(byte[] bytes, string name)
        {
            if (bytes == null || bytes.Length > MaximumChecksumBytes) throw new InvalidDataException("校验清单过大。");
            string found = null;
            foreach (string raw in Encoding.UTF8.GetString(bytes).Split('\n'))
            {
                string entry = raw.Trim().TrimStart('\uFEFF');
                var match = Regex.Match(entry, @"^([a-fA-F0-9]{64})[ \t]+\*?([^ \t]+)$", RegexOptions.CultureInvariant);
                if (!match.Success || match.Groups[2].Value != name) continue;
                if (found != null) throw new InvalidDataException("安装包存在重复校验项。"); found = match.Groups[1].Value.ToLowerInvariant();
            }
            if (found == null) throw new InvalidDataException("校验清单缺少当前安装包。"); return found;
        }
        internal static bool VerifyFile(string path, UpdatePackage package)
        {
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length != package.Size) return false;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha = SHA256.Create()) return string.Equals(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(), package.Hash, StringComparison.Ordinal);
        }
        internal static bool InstallerVersionMatches(string path, string version)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                return info.FileMajorPart + "." + info.FileMinorPart + "." + info.FileBuildPart == version && info.FilePrivatePart == 0;
            }
            catch { return false; }
        }
        internal static bool AllowedRedirect(Uri uri)
        {
            return uri != null && uri.Scheme == "https" && uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
                (string.Equals(uri.Host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase) || string.Equals(uri.Host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase));
        }
        internal static void EnsurePlainDirectory(string path)
        {
            string directory = Path.GetFullPath(path);
            for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("更新目录不能使用链接或重定向文件夹。");
            Directory.CreateDirectory(directory);
        }
        [StructLayout(LayoutKind.Sequential)] private struct FileInformation
        {
            public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
            public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle handle, out FileInformation information);
        internal static bool IsPlainOwnedInstallation(string directory)
        {
            try
            {
                if (!Directory.Exists(directory)) return false;
                for (var current = new DirectoryInfo(directory); current != null; current = current.Parent)
                    if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                string marker = Path.Combine(directory, "FreeIsland.install");
                if (!File.Exists(marker) || new FileInfo(marker).Length > 256 || File.ReadAllText(marker).Trim() != "FreeIsland per-user installation v1") return false;
                foreach (string name in new[] { "FreeIsland.exe", "FreeIsland.exe.config", "FreeIsland.ico", "FreeIsland.Uninstall.exe", "FreeIsland.install" })
                {
                    string path = Path.Combine(directory, name);
                    if (!File.Exists(path)) { if (name == "FreeIsland.exe") return false; continue; }
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
                    using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        FileInformation info; if (!GetFileInformationByHandle(file.SafeFileHandle, out info) || info.Links != 1) return false;
                    }
                }
                return true;
            }
            catch { return false; }
        }
    }

    internal interface IUpdateTransport
    {
        byte[] Fetch(Uri uri, int maximum, CancellationToken token);
        void Download(Uri uri, string path, long maximum, Action<long> progress, CancellationToken token);
    }
    internal sealed class GitHubUpdateTransport : IUpdateTransport
    {
        public byte[] Fetch(Uri uri, int maximum, CancellationToken token)
        {
            using (var memory = new MemoryStream()) { Copy(uri, memory, maximum, null, token); return memory.ToArray(); }
        }
        public void Download(Uri uri, string path, long maximum, Action<long> progress, CancellationToken token)
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) Copy(uri, file, maximum, progress, token);
        }
        private static void Copy(Uri uri, Stream output, long maximum, Action<long> progress, CancellationToken token)
        {
            // Windows 10's original .NET 4.6 does not negotiate GitHub TLS reliably via its old default.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            bool metadata = string.Equals(uri.AbsoluteUri, UpdateRules.LatestApi, StringComparison.Ordinal);
            if (!metadata && (uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsoluteUri.StartsWith(UpdateRules.Repository + "/releases/download/", StringComparison.Ordinal))) throw new InvalidDataException("更新地址无效。");
            for (int redirects = 0; redirects <= 4; redirects++)
            {
                token.ThrowIfCancellationRequested();
                var request = (HttpWebRequest)WebRequest.Create(uri); request.Method = "GET"; request.AllowAutoRedirect = false; request.UserAgent = "FreeIsland-Updater";
                request.Accept = metadata ? "application/vnd.github+json" : "application/octet-stream"; request.Timeout = 15000; request.ReadWriteTimeout = 15000; request.AutomaticDecompression = DecompressionMethods.None;
                request.UseDefaultCredentials = false; request.Credentials = null; request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                using (token.Register(delegate { request.Abort(); }))
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    int status = (int)response.StatusCode;
                    if (status >= 300 && status < 400)
                    {
                        Uri next; if (metadata || redirects == 4 || !Uri.TryCreate(uri, response.Headers["Location"], out next) || !UpdateRules.AllowedRedirect(next)) throw new InvalidDataException("更新下载发生不受信任的跳转。");
                        uri = next; continue;
                    }
                    if (status != 200 || response.ContentLength > maximum) throw new InvalidDataException("更新响应状态或大小无效。");
                    using (var input = response.GetResponseStream())
                    {
                        var buffer = new byte[32768]; long count = 0; int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            token.ThrowIfCancellationRequested(); count += read; if (count > maximum) throw new InvalidDataException("更新文件超过允许大小。");
                            output.Write(buffer, 0, read); if (progress != null) progress(count);
                        }
                    }
                    return;
                }
            }
            throw new InvalidDataException("更新跳转次数过多。");
        }
    }

    public sealed class UpdateService : IDisposable
    {
        private readonly CoreEngine engine;
        private readonly bool disabled;
        private readonly Func<bool> panelVisible;
        private readonly Func<string> installedDirectory;
        private readonly Func<bool> elevated;
        private readonly Func<string, string, bool> installerVersion;
        private readonly Func<string, string, bool> launch;
        private readonly Action exit;
        private readonly Func<DateTime> now;
        private readonly IUpdateTransport transport;
        private readonly string cacheDirectory;
        private readonly Version currentVersion;
        private DateTime nextCheck;
        private CancellationTokenSource cancellation;
        private UpdatePackage package;
        private string readyPath;
        private string failedVersion;
        private bool disposed, applyAttempted;
        private long received;
        public string Status { get; private set; }
        public bool IsBusy { get; private set; }
        public bool Disabled { get { return disabled; } }
        public bool HasDownload { get { return readyPath != null && !IsBusy; } }
        public int Progress { get { return package == null || package.Size <= 0 ? 0 : Math.Max(0, Math.Min(100, (int)(Interlocked.Read(ref received) * 100 / package.Size))); } }
        public string CurrentVersion { get { return currentVersion.ToString(3); } }
        public bool HasActiveWork { get { return engine.StopwatchActive || engine.CountdownActive || engine.ShutdownBlocksAutoUpdate; } }

        public UpdateService(CoreEngine engine, Func<bool> panelVisible, Action exit)
            : this(engine, engine.IsSafeMode, panelVisible, FindInstalledDirectory, StartInstaller, exit, new GitHubUpdateTransport(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreeIsland", "Updates"),
                Assembly.GetExecutingAssembly().GetName().Version, delegate { return DateTime.UtcNow; }, IsElevated)
        {
            if (!disabled) ReadInstallResult();
        }
        internal UpdateService(CoreEngine engine, bool disabled, Func<bool> panelVisible, Func<string> installedDirectory, Func<string, string, bool> launch, Action exit, IUpdateTransport transport, string cacheDirectory, Version version, Func<DateTime> clock, Func<bool> elevated = null, Func<string, string, bool> installerVersion = null)
        {
            this.engine = engine; this.disabled = disabled; this.panelVisible = panelVisible; this.installedDirectory = installedDirectory; this.launch = launch; this.exit = exit; this.transport = transport; this.cacheDirectory = cacheDirectory; currentVersion = version; now = clock; this.elevated = elevated ?? delegate { return false; }; this.installerVersion = installerVersion ?? UpdateRules.InstallerVersionMatches;
            nextCheck = now().AddSeconds(30); Status = disabled ? "安全预览中，不联网、不下载或安装更新。" : "启动后自动检查新版本；任务进行中不会安装。";
        }
        public void Poll()
        {
            if (disposed || disabled) return;
            if (engine.Settings.AutoUpdate && !IsBusy && now() >= nextCheck) { var ignored = CheckAsync(false); }
            if (engine.Settings.AutoUpdate && HasDownload && !applyAttempted && !HasActiveWork && !panelVisible()) Apply(false);
        }
        public void PreferencesChanged()
        {
            if (!engine.Settings.AutoUpdate) { Cancel(); Status = HasDownload ? "已保留下载的更新，可手动安装。" : "自动更新已关闭，可随时手动检查。"; }
            else { nextCheck = now().AddSeconds(30); Status = HasDownload ? "更新已下载，等待任务结束且控制中心收起后安装。" : "已开启自动更新，将在后台检查。"; }
        }
        public async Task CheckAsync(bool manual)
        {
            if (disposed || IsBusy || disabled || (!manual && !engine.Settings.AutoUpdate)) return;
            nextCheck = now().AddHours(6); IsBusy = true; Status = "正在检查 GitHub 上的最新正式版本…"; Interlocked.Exchange(ref received, 0);
            cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3)); CancellationToken token = cancellation.Token;
            string partial = null;
            try
            {
                var found = await Task.Run(delegate { return UpdateRules.ParseRelease(transport.Fetch(new Uri(UpdateRules.LatestApi), UpdateRules.MaximumMetadataBytes, token), currentVersion); }, token);
                token.ThrowIfCancellationRequested(); if (found == null) { readyPath = null; package = null; Status = "当前已是最新正式版本。"; return; }
                if (!manual && found.Version == failedVersion) { readyPath = null; Status = "此版本上次自动安装未完成，已暂停自动重试。请手动检查并安装。"; return; }
                if (found.Hash == null) found.Hash = await Task.Run(delegate { return UpdateRules.ManifestHash(transport.Fetch(found.ChecksumUrl, UpdateRules.MaximumChecksumBytes, token), found.FileName); }, token);
                if (readyPath != null && package != null && package.Version == found.Version && package.Hash == found.Hash && package.Size == found.Size && UpdateRules.VerifyFile(readyPath, found) && installerVersion(readyPath, found.Version))
                {
                    package = found; Status = ReadyStatus(found); return;
                }
                readyPath = null; package = found;
                token.ThrowIfCancellationRequested(); Status = "发现 " + found.Version + "，正在下载并校验…";
                string directory = Path.Combine(cacheDirectory, found.Version + "-" + Guid.NewGuid().ToString("N")); UpdateRules.EnsurePlainDirectory(directory);
                partial = Path.Combine(directory, found.FileName + ".part"); string destination = Path.Combine(directory, found.FileName); string downloadPath = partial;
                await Task.Run(delegate
                {
                    transport.Download(found.Url, downloadPath, found.Size, delegate(long bytes) { Interlocked.Exchange(ref received, bytes); }, token);
                    token.ThrowIfCancellationRequested(); if (!UpdateRules.VerifyFile(downloadPath, found)) throw new InvalidDataException("SHA-256 校验失败，已拒绝安装。");
                    if (!installerVersion(downloadPath, found.Version)) throw new InvalidDataException("安装器内置版本与发布版本不符，已拒绝安装。");
                    File.Move(downloadPath, destination);
                }, token);
                token.ThrowIfCancellationRequested(); readyPath = destination; partial = null; applyAttempted = false;
                Status = ReadyStatus(found); TrimCache(directory);
            }
            catch (OperationCanceledException) { readyPath = null; Status = "更新已取消，可稍后重试。"; }
            catch (Exception ex) { readyPath = null; Status = token.IsCancellationRequested ? "下载已取消或超时，可稍后重试。" : "更新暂未完成：" + FriendlyFailure(ex); }
            finally
            {
                if (partial != null) { try { if (File.Exists(partial)) File.Delete(partial); } catch { } }
                IsBusy = false; if (cancellation != null) { cancellation.Dispose(); cancellation = null; }
            }
        }
        private string ReadyStatus(UpdatePackage found)
        {
            return string.IsNullOrEmpty(installedDirectory()) ? "已下载 " + found.Version + "。当前为便携运行，请手动安装。" : elevated() ? "已下载 " + found.Version + "。当前以管理员运行，请手动安装更新。" : found.Version == failedVersion ? "已重新下载，请点击手动安装；此前失败的版本不会自动重试。" : engine.Settings.AutoUpdate ? "已下载 " + found.Version + "，等待空闲且收起窗口后自动安装；重复关机前 5 分钟暂停更新。" : "已下载 " + found.Version + "，可手动安装。";
        }
        private void TrimCache(string keep)
        {
            try
            {
                UpdateRules.EnsurePlainDirectory(cacheDirectory);
                var folders = new DirectoryInfo(cacheDirectory).GetDirectories().Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0 && Regex.IsMatch(d.Name, @"\A[0-9]+\.[0-9]+\.[0-9]+-[a-f0-9]{32}\z")).OrderByDescending(d => d.CreationTimeUtc).ToArray();
                foreach (var folder in folders.Skip(2))
                {
                    if (string.Equals(folder.FullName, keep, StringComparison.OrdinalIgnoreCase)) continue;
                    string name = "FreeIsland-Setup-" + folder.Name.Split('-')[0] + ".exe";
                    foreach (string suffix in new[] { "", ".part" }) { string file = Path.Combine(folder.FullName, name + suffix); if (File.Exists(file)) File.Delete(file); }
                    if (!Directory.EnumerateFileSystemEntries(folder.FullName).Any()) Directory.Delete(folder.FullName, false);
                }
            }
            catch { /* Cache cleanup must never prevent a verified update. */ }
        }
        public void Cancel() { if (cancellation != null) cancellation.Cancel(); }
        public void InstallManually() { Apply(true); }
        private void Apply(bool manual)
        {
            if (disposed || disabled || !HasDownload) return;
            if (HasActiveWork) { Status = "计时、单次关机预约或临近的重复关机仍在进行，更新已保留。请稍后安装。"; return; }
            if (!manual && (panelVisible() || !engine.Settings.AutoUpdate || elevated() || package.Version == failedVersion || applyAttempted)) return;
            string directory = installedDirectory();
            if (string.IsNullOrEmpty(directory) && !manual) { Status = "便携运行：更新已下载，点击「手动安装更新」开始安装。"; return; }
            try
            {
                // Verify again immediately before handing control to a separate process.
                if (!UpdateRules.VerifyFile(readyPath, package) || !installerVersion(readyPath, package.Version)) { readyPath = null; throw new InvalidDataException("下载文件已改变，拒绝启动。请重新检查更新。"); }
                applyAttempted = true;
                bool automaticInstaller = !string.IsNullOrEmpty(directory) && !elevated() && !(manual && package.Version == failedVersion);
                string arguments = automaticInstaller ? "--auto-update \"" + directory + "\"" : "";
                if (!launch(readyPath, arguments)) throw new IOException("安装器未能启动。");
                Status = "安装器已启动。";
                if (automaticInstaller) exit();
            }
            catch (Exception ex) { Status = "更新未安装：" + FriendlyFailure(ex) + " 已保留当前程序。"; }
        }
        private static string FriendlyFailure(Exception error)
        {
            if (error is WebException) return "无法连接 GitHub，请检查网络后重试。";
            if (error is UnauthorizedAccessException) return "更新目录没有写入权限，可使用手动安装包。";
            if (error is InvalidDataException || error is IOException) return error.Message;
            return "发布信息不可用，请稍后重试。";
        }
        private static string FindInstalledDirectory()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\FreeIsland", false))
                {
                    string value = key == null ? null : key.GetValue("InstallLocation") as string;
                    if (string.IsNullOrWhiteSpace(value) || value.IndexOf('"') >= 0) return null;
                    string registered = Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar), running = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
                    return string.Equals(registered, running, StringComparison.OrdinalIgnoreCase) && UpdateRules.IsPlainOwnedInstallation(registered) ? registered : null;
                }
            }
            catch { return null; }
        }
        private static bool StartInstaller(string path, string arguments)
        {
            using (var process = Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(path) })) return process != null;
        }
        private static bool IsElevated()
        {
            try { using (var identity = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return true; }
        }
        private void ReadInstallResult()
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FreeIsland", "update-result.txt");
                if (!File.Exists(path) || new FileInfo(path).Length > 8192) return;
                string[] result = File.ReadAllLines(path, Encoding.UTF8);
                ReadInstallResultLines(result);
            }
            catch { }
        }
        internal void ReadInstallResultLines(string[] result)
        {
            if (result.Length >= 3 && (result[0] == "failed" || result[0] == "rejected" || result[0] == "restart-failed"))
            {
                Version version; if (Version.TryParse(result[1], out version) && version.Build >= 0) failedVersion = version.ToString(3);
                Status = "上次自动更新未完成，已暂停该版本的自动安装。当前程序仍可使用，可手动检查并安装。";
            }
        }
        public void Dispose() { disposed = true; Cancel(); }
    }
}
