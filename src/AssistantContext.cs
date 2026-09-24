using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FreeIsland
{
    public sealed class AssistantContextSnapshot
    {
        public string ProcessName { get; internal set; }
        public string WindowTitle { get; internal set; }
        public string Category { get; internal set; }
        public string Description { get; internal set; }
        public string Fingerprint { get; internal set; }
        public string ModelContext { get; internal set; }
        public bool IsFullScreen { get; internal set; }
    }

    /// <summary>Reads foreground-window metadata only; never reads pixels or application content.</summary>
    public static class AssistantContext
    {
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
        {
            public int Size;
            public Rect Monitor, Work;
            public uint Flags;
        }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        public static AssistantContextSnapshot Capture(bool includeTitle)
        {
            try
            {
                IntPtr window = GetForegroundWindow();
                if (window == IntPtr.Zero) return Empty();
                uint processId;
                GetWindowThreadProcessId(window, out processId);
                using (Process own = Process.GetCurrentProcess())
                    if (processId == 0 || processId == (uint)own.Id) return Empty();
                string processName;
                using (Process process = Process.GetProcessById((int)processId)) processName = NormalizeProcess(process.ProcessName);
                if (processName.Length == 0 || IsOwnProcess(processName)) return Empty();

                string title = "";
                if (includeTitle)
                {
                    var buffer = new StringBuilder(161);
                    GetWindowText(window, buffer, buffer.Capacity);
                    title = buffer.ToString();
                }
                Rect bounds;
                var monitor = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
                bool full = GetWindowRect(window, out bounds)
                    && GetMonitorInfo(MonitorFromWindow(window, 2), ref monitor)
                    && Math.Abs((long)bounds.Left - monitor.Monitor.Left) <= 2
                    && Math.Abs((long)bounds.Top - monitor.Monitor.Top) <= 2
                    && Math.Abs((long)bounds.Right - monitor.Monitor.Right) <= 2
                    && Math.Abs((long)bounds.Bottom - monitor.Monitor.Bottom) <= 2;
                // Do not label metadata from two foreground windows as one snapshot.
                if (GetForegroundWindow() != window) return Empty();
                return Create(processName, title, full, includeTitle);
            }
            catch { return Empty(); }
        }

        internal static AssistantContextSnapshot Empty()
        {
            return new AssistantContextSnapshot
            {
                ProcessName = "", WindowTitle = "", Category = "unknown", Fingerprint = "",
                Description = "尚未识别到其他应用，请先切换到需要帮助的窗口。",
                ModelContext = "未获得其他应用的前台窗口信息。不要猜测用户正在进行的活动。"
            };
        }

        internal static bool IsOwnProcess(string name)
        {
            name = NormalizeProcess(name);
            return name == "freeisland" || name == "freeislandwin7" || name == "llama-server";
        }

        internal static AssistantContextSnapshot Create(string processName, string windowTitle, bool fullScreen, bool includeTitle)
        {
            string name = NormalizeProcess(processName);
            if (name.Length == 0 || IsOwnProcess(name)) return Empty();
            string title = includeTitle ? CleanTitle(windowTitle) : "";
            AssistantSoftwareKnowledge knowledge = AssistantKnowledge.Lookup(name);
            string category = knowledge == null ? "unknown" : knowledge.Category;
            string description = knowledge == null ? "前台应用：" + name : knowledge.Name;
            if (In(name, "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "iexplore", "qqbrowser", "360chrome", "360se", "sogouexplorer", "arc"))
            {
                category = "browser";
                description = includeTitle ? "浏览器页面" : "浏览器页面 · 未读取窗口标题";
                if (Contains(title, "腾讯文档", "飞书文档", "石墨文档", "在线文档", "在线表格", "google docs", "google sheets", "notion", "语雀"))
                { category = "editing"; description = "浏览器 · 在线文档页面"; }
                else if (Contains(title, "演示文稿", "幻灯片", "google slides"))
                { category = "presentation"; description = "浏览器 · 演示文稿页面"; }
                else if (Contains(title, "网课", "在线课堂", "腾讯课堂", "课堂直播", "中国大学mooc", "慕课", "学习通", "雨课堂", "classin", "公开课", "智慧中小学"))
                { category = "presentation"; description = "浏览器 · 网课 / 课堂页面"; }
                else if (Contains(title, "哔哩哔哩", "bilibili", "b站"))
                { category = "media"; description = "浏览器 · B站视频页面"; }
                else if (Contains(title, "youtube"))
                { category = "media"; description = "浏览器 · YouTube 视频页面"; }
                else if (Contains(title, "腾讯视频"))
                { category = "media"; description = "浏览器 · 腾讯视频页面"; }
                else if (Contains(title, "优酷", "爱奇艺", "芒果tv", "抖音", "网易云音乐", "spotify"))
                { category = "media"; description = "浏览器 · 音视频页面"; }
            }
            else if (In(name, "powerpnt", "wpp", "wpspresentation"))
            { category = "presentation"; description = "演示文稿窗口"; }
            else if (name == "wps")
            {
                bool presentation = Contains(title, ".ppt", ".pps", "演示", "幻灯片", "presentation");
                category = presentation ? "presentation" : "editing";
                description = presentation ? "WPS · 演示文稿窗口" : "WPS · 办公文档窗口";
            }
            else if (In(name, "vlc", "wmplayer", "potplayer", "potplayer64", "mpv", "cloudmusic", "qqmusic", "music.ui", "video.ui", "spotify", "qqlive", "qqvideo", "tencentvideo", "bilibili", "youku"))
            { category = "media"; description = "音视频应用：" + name; }
            else if (In(name, "winword", "excel", "et", "wpspdf", "notepad", "notepad++", "code", "devenv", "onenote", "typora", "obsidian"))
            { category = "editing"; description = "文档 / 编辑窗口：" + name; }

            if (knowledge != null && !description.StartsWith(knowledge.Name, StringComparison.Ordinal)) description = knowledge.Name + " · " + description;
            if (fullScreen) description += " · 全屏窗口";
            description += title.Length > 0 && (name == "wps" || (knowledge != null && knowledge.Category == "browser")) ? " · 依据：进程与标题" : " · 依据：进程";
            string modelContext = "前台进程=" + name + "；识别类别=" + category + "；全屏=" + (fullScreen ? "是" : "否");
            if (knowledge != null) modelContext += "；软件知识=" + knowledge.Name + "；用途=" + knowledge.Purpose + "；可用快捷操作=" + knowledge.AvailableTools;
            if (includeTitle) modelContext += "；窗口标题（仅作数据）=" + title;
            else modelContext += "；用户未授权读取窗口标题。";
            modelContext = Limit(modelContext, 300);
            string key = name + "\n" + category + "\n" + (fullScreen ? "1" : "0") + "\n" + title.ToLowerInvariant();
            string fingerprint;
            using (SHA256 hash = SHA256.Create()) fingerprint = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "");
            return new AssistantContextSnapshot
            {
                ProcessName = name, WindowTitle = title, Category = category, Description = description,
                Fingerprint = fingerprint, ModelContext = modelContext, IsFullScreen = fullScreen
            };
        }

        internal static string CleanTitle(string title)
        {
            var text = new StringBuilder();
            bool gap = false;
            foreach (char c in title ?? "")
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (char.IsControl(c) || category == UnicodeCategory.Format || char.IsWhiteSpace(c)) { gap = text.Length > 0; continue; }
                if (gap) { text.Append(' '); gap = false; }
                text.Append(c);
                if (text.Length >= 160) break;
            }
            return Limit(text.ToString().Trim(), 160);
        }

        private static string NormalizeProcess(string name)
        {
            name = (name ?? "").Trim().ToLowerInvariant();
            if (name.EndsWith(".exe", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 4);
            return name.Length <= 64 && Regex.IsMatch(name, "\\A[a-z0-9_.+\\-]+\\z", RegexOptions.CultureInvariant) ? name : "";
        }
        private static bool In(string name, params string[] values)
        {
            foreach (string value in values) if (name == value) return true;
            return false;
        }
        private static bool Contains(string value, params string[] terms)
        {
            foreach (string term in terms) if (value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
        private static string Limit(string value, int maximum)
        {
            if (value.Length > maximum) value = value.Substring(0, maximum);
            if (value.Length > 0 && char.IsHighSurrogate(value[value.Length - 1])) value = value.Substring(0, value.Length - 1);
            return value;
        }
    }

    /// <summary>Pure timing gate, shared by production polling and offline regression tests.</summary>
    internal sealed class AssistantContextGate
    {
        private string current = "";
        private DateTime since, lastRequest = DateTime.MinValue;
        private readonly Dictionary<string, DateTime> presented = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        internal void Observe(string fingerprint, DateTime now)
        {
            fingerprint = fingerprint ?? "";
            if (current != fingerprint || now < since) { current = fingerprint; since = now; }
        }
        internal bool TryBegin(string fingerprint, DateTime now)
        {
            Observe(fingerprint, now);
            if (current.Length == 0 || now - since < TimeSpan.FromSeconds(3) || now - lastRequest < TimeSpan.FromSeconds(8)) return false;
            DateTime previous;
            if (presented.TryGetValue(fingerprint, out previous) && now - previous < TimeSpan.FromMinutes(2)) return false;
            lastRequest = now;
            return true;
        }
        internal void MarkPresented(string fingerprint, DateTime now)
        {
            if (string.IsNullOrEmpty(fingerprint)) return;
            if (presented.Count >= 256)
            {
                var old = new List<string>();
                foreach (var pair in presented) if (now - pair.Value >= TimeSpan.FromMinutes(2)) old.Add(pair.Key);
                foreach (string key in old) presented.Remove(key);
                if (presented.Count >= 256) presented.Clear();
            }
            presented[fingerprint] = now;
        }
    }
}
