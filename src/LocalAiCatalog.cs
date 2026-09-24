using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace FreeIsland
{
    internal sealed class LocalAiModel
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Parameters { get; private set; }
        public string Quantization { get; private set; }
        public long DownloadBytes { get; private set; }
        public int RecommendedRamGb { get; private set; }
        public string License { get { return "Apache-2.0"; } }
        public string SourceUrl { get; private set; }
        internal Uri DownloadUrl { get; private set; }
        internal string Sha256 { get; private set; }
        internal LocalAiModel(string id, string parameters, string quantization, string repository, string revision, string file, long bytes, string hash, int ram)
        {
            Id = id; Name = "Qwen3 " + parameters; Parameters = parameters; Quantization = quantization;
            SourceUrl = "https://huggingface.co/" + repository + "/tree/" + revision;
            DownloadUrl = new Uri("https://huggingface.co/" + repository + "/resolve/" + revision + "/" + file + "?download=true");
            DownloadBytes = bytes; Sha256 = hash; RecommendedRamGb = ram;
        }
        public override string ToString() { return Name + " · " + Quantization + " · " + (DownloadBytes / 1000000000.0).ToString("0.00") + " GB"; }
    }

    internal static class LocalAiCatalog
    {
        // Pinned upstream metadata, checked 2026-09-24. The v0.5.0 release links b11146.
        internal const string RuntimeVersion = "b11146";
        internal const string RuntimeLicense = "MIT";
        internal const long RuntimeBytes = 18560055;
        internal const string RuntimeSha256 = "14cf1303ca9ac3abd94816850532f9f9a69ac66fbaca3776fc6f9061c2fac1d1";
        internal static readonly Uri RuntimeUrl = new Uri("https://github.com/ggml-org/llama.cpp/releases/download/b11146/llama-b11146-bin-win-cpu-x64.zip");
        private static readonly LocalAiModel[] models = {
            new LocalAiModel("qwen3-0.6b", "0.6B", "Q4_0", "ggml-org/Qwen3-0.6B-GGUF", "b5f37287796e5be0ea3dab2e7430873fb3f73e49", "Qwen3-0.6B-Q4_0.gguf", 428970080, "da2572f16c06133561ce56accaa822216f2391ef4d37fba427801cd6736417d4", 4),
            new LocalAiModel("qwen3-1.7b", "1.7B", "Q4_K_M", "ggml-org/Qwen3-1.7B-GGUF", "daeb8e2d528a760970442092f6bf1e55c3b659eb", "Qwen3-1.7B-Q4_K_M.gguf", 1282439264, "d2387ca2dbfee2ffabce7120d3770dadca0b293052bc2f0e138fdc940d9bc7b5", 6),
            new LocalAiModel("qwen3-4b", "4B", "Q4_K_M", "Qwen/Qwen3-4B-GGUF", "bc640142c66e1fdd12af0bd68f40445458f3869b", "Qwen3-4B-Q4_K_M.gguf", 2497280256L, "7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5", 8)
        };
        public static IReadOnlyList<LocalAiModel> Models { get { return Array.AsReadOnly(models); } }
        internal static LocalAiModel Find(string id)
        {
            var model = models.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));
            if (model == null) throw new ArgumentException("请选择列表内的模型。"); return model;
        }
    }

    internal static class LocalAiPlatform
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct OsVersion
        {
            public int Size, Major, Minor, Build, Platform;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string ServicePack;
        }
        [DllImport("ntdll.dll", CharSet = CharSet.Unicode)] private static extern int RtlGetVersion(ref OsVersion version);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsProcessorFeaturePresent(uint feature);
        internal static string GetSupportMessage()
        {
            try
            {
                var v = new OsVersion { Size = Marshal.SizeOf(typeof(OsVersion)) };
                if (RtlGetVersion(ref v) != 0 || v.Major < 10 || (v.Major == 10 && v.Build < 19041)) return "本地模型当前适配 Windows 10 2004（19041）及以上；此系统仍可使用场景快捷操作。";
                if (!Environment.Is64BitOperatingSystem) return "本地模型需要 64 位 Windows；此系统仍可使用场景快捷操作。";
                if (!IsProcessorFeaturePresent(40)) return "本地模型当前需要支持 AVX2 的处理器；可继续使用场景快捷操作。";
                return null;
            }
            catch { return "无法确认本机模型运行条件；可继续使用场景快捷操作。"; }
        }
    }
}
