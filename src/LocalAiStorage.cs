using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;

namespace FreeIsland
{
    internal interface ILocalAiTransport
    {
        void Download(Uri uri, string destination, long exactBytes, Action<long> progress, CancellationToken token);
    }
    internal sealed class LocalAiTransport : ILocalAiTransport
    {
        internal static bool AllowedUri(Uri uri)
        {
            if (uri == null || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
            string host = uri.Host.ToLowerInvariant();
            return host == "github.com" || host == "release-assets.githubusercontent.com" || host == "objects.githubusercontent.com" || host == "huggingface.co" || host == "cdn-lfs.huggingface.co" || host == "cdn-lfs-us-1.huggingface.co" || host == "cdn-lfs-eu-1.huggingface.co" || host == "cas-bridge.xethub.hf.co" || host == "cas-server.xethub.hf.co";
        }
        public void Download(Uri uri, string destination, long exactBytes, Action<long> progress, CancellationToken token)
        {
            if (exactBytes < 1 || exactBytes > 3L * 1024 * 1024 * 1024) throw new InvalidDataException("下载大小无效。");
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var clock = Stopwatch.StartNew();
            for (int redirect = 0; redirect < 8; redirect++)
            {
                token.ThrowIfCancellationRequested(); if (!AllowedUri(uri)) throw new InvalidDataException("下载地址不在官方来源范围内。");
                var request = (HttpWebRequest)WebRequest.Create(uri); request.AllowAutoRedirect = false; request.Timeout = 30000; request.ReadWriteTimeout = 30000;
                request.UserAgent = "FreeIsland-OptionalLocalAI/1.0"; request.AutomaticDecompression = DecompressionMethods.None;
                using (token.Register(request.Abort))
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    int status = (int)response.StatusCode;
                    if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                    {
                        Uri next; if (!Uri.TryCreate(uri, response.Headers["Location"], out next) || !AllowedUri(next)) throw new InvalidDataException("下载重定向被拒绝。");
                        uri = next; continue;
                    }
                    if (status != 200 || (response.ContentLength >= 0 && response.ContentLength != exactBytes)) throw new InvalidDataException("下载文件大小与固定版本不符。");
                    using (var input = response.GetResponseStream())
                    using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[65536]; long total = 0; int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            token.ThrowIfCancellationRequested(); if (clock.Elapsed > TimeSpan.FromHours(2)) throw new TimeoutException("下载已超过 2 小时，请检查网络后重试。");
                            total += read; if (total > exactBytes) throw new InvalidDataException("下载超过允许大小。");
                            output.Write(buffer, 0, read); if (progress != null) progress(total);
                        }
                        token.ThrowIfCancellationRequested(); if (total != exactBytes) throw new InvalidDataException("下载未完成，请重新下载。");
                    }
                    return;
                }
            }
            throw new InvalidDataException("下载重定向次数过多。");
        }
    }
    internal static class LocalAiStorage
    {
        internal static void EnsurePlainPath(string value)
        {
            string current = Path.GetFullPath(value);
            while (!string.IsNullOrEmpty(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("AI 文件夹不能使用符号链接或目录联接。");
                string parent = Path.GetDirectoryName(current); if (parent == current) break; current = parent;
            }
        }
        internal static string Inside(string root, string relative)
        {
            string basis = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(basis, relative));
            if (!path.StartsWith(basis, StringComparison.OrdinalIgnoreCase)) throw new IOException("AI 文件路径超出数据目录。");
            EnsurePlainPath(path); return path;
        }
        internal static string Hash(Stream input, CancellationToken token)
        {
            using (var hash = SHA256.Create())
            {
                byte[] buffer = new byte[65536]; int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); hash.TransformBlock(buffer, 0, count, null, 0); }
                hash.TransformFinalBlock(buffer, 0, 0); return BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
            }
        }
        internal static bool Verify(string path, long bytes, string expectedHash, bool gguf, CancellationToken token)
        {
            EnsurePlainPath(path); token.ThrowIfCancellationRequested();
            if (!File.Exists(path) || new FileInfo(path).Length != bytes) return false;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (gguf) { byte[] magic = new byte[4]; if (stream.Read(magic, 0, 4) != 4 || magic[0] != 'G' || magic[1] != 'G' || magic[2] != 'U' || magic[3] != 'F') return false; stream.Position = 0; }
                return string.Equals(Hash(stream, token), expectedHash, StringComparison.Ordinal);
            }
        }
        internal static string RuntimeEntryName(string entry)
        {
            string normalized = entry.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.IndexOf(':') >= 0 || normalized.Split('/').Any(p => p == ".." || p == ".")) throw new InvalidDataException("运行时压缩包包含无效路径。");
            if (normalized.EndsWith("/", StringComparison.Ordinal)) return null;
            string name = normalized.Substring(normalized.LastIndexOf('/') + 1);
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidDataException("运行时文件名无效。");
            return name.Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase) ? name : null;
        }
        internal static void ExtractRuntime(string archive, string destination, CancellationToken token)
        {
            EnsurePlainPath(archive); EnsurePlainPath(destination); Directory.CreateDirectory(destination);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
            using (var zip = ZipFile.OpenRead(archive))
            {
                if (zip.Entries.Count > 256) throw new InvalidDataException("运行时压缩包文件过多。");
                foreach (var entry in zip.Entries)
                {
                    token.ThrowIfCancellationRequested(); string name = RuntimeEntryName(entry.FullName); if (name == null) continue;
                    if (!seen.Add(name) || entry.Length < 0 || entry.Length > 128L * 1024 * 1024 || (total += entry.Length) > 256L * 1024 * 1024) throw new InvalidDataException("运行时压缩包大小或文件名无效。");
                    string path = Inside(destination, name);
                    using (var input = entry.Open())
                    using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[65536]; long count = 0; int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); count += read; if (count > entry.Length) throw new InvalidDataException("运行时解压超过声明大小。"); output.Write(buffer, 0, read); }
                        if (count != entry.Length) throw new InvalidDataException("运行时解压不完整。");
                    }
                }
            }
            if (!seen.Contains("llama-server.exe") || !seen.Any(n => n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("运行时缺少服务器或依赖文件。");
        }
        internal static bool VerifyRuntime(string archive, string directory, CancellationToken token)
        {
            if (!Verify(archive, LocalAiCatalog.RuntimeBytes, LocalAiCatalog.RuntimeSha256, false, token) || !Directory.Exists(directory)) return false;
            EnsurePlainPath(directory); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var zip = ZipFile.OpenRead(archive))
            {
                foreach (var entry in zip.Entries)
                {
                    token.ThrowIfCancellationRequested(); string name = RuntimeEntryName(entry.FullName); if (name == null) continue;
                    if (!names.Add(name)) return false; string path = Inside(directory, name);
                    if (!File.Exists(path) || new FileInfo(path).Length != entry.Length) return false;
                    using (var input = entry.Open())
                    using (var actual = File.OpenRead(path)) if (!string.Equals(Hash(input, token), Hash(actual, token), StringComparison.Ordinal)) return false;
                }
            }
            return names.Contains("llama-server.exe") && Directory.GetFiles(directory).All(p => names.Contains(Path.GetFileName(p))) && Directory.GetDirectories(directory).Length == 0;
        }
        internal static void DeleteOwnedTree(string root, string relative)
        {
            string path = Inside(root, relative); if (!Directory.Exists(path)) return;
            // Verify every descendant before recursive deletion, including directory junctions.
            CheckTree(path); Directory.Delete(path, true);
        }
        private static void CheckTree(string path)
        {
            EnsurePlainPath(path);
            foreach (string child in Directory.GetFileSystemEntries(path)) { EnsurePlainPath(child); if (Directory.Exists(child)) CheckTree(child); }
        }
    }
}
