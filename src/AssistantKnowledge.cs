using System;
using System.Collections.Generic;

namespace FreeIsland
{
    /// <summary>Local software facts supplied to the model, never an action executor.</summary>
    public sealed class AssistantSoftwareKnowledge
    {
        public string Name { get; private set; }
        public string Category { get; private set; }
        public string Purpose { get; private set; }
        public string AvailableTools { get; private set; }
        public IList<string> ProcessAliases { get; private set; }
        internal AssistantSoftwareKnowledge(string name, string category, string purpose, string tools, string[] aliases)
        {
            Name = name; Category = category; Purpose = purpose; AvailableTools = tools;
            ProcessAliases = Array.AsReadOnly(aliases);
        }
    }

    public static class AssistantKnowledge
    {
        private static readonly Dictionary<string, AssistantSoftwareKnowledge> Software = Build();
        public static AssistantSoftwareKnowledge Lookup(string processName)
        {
            string name = (processName ?? "").Trim().ToLowerInvariant();
            if (name.EndsWith(".exe", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 4);
            AssistantSoftwareKnowledge known;
            return Software.TryGetValue(name, out known) ? known : null;
        }
        private static Dictionary<string, AssistantSoftwareKnowledge> Build()
        {
            var result = new Dictionary<string, AssistantSoftwareKnowledge>(StringComparer.OrdinalIgnoreCase);
            Add(result, "PotPlayer 播放器", "media", "播放本地音视频，可暂停或调节音量", "volume,media_toggle", "potplayer", "potplayermini", "potplayermini64", "potplayer64");
            Add(result, "VLC 播放器", "media", "播放音视频文件或媒体流", "volume,media_toggle", "vlc");
            Add(result, "mpv 播放器", "media", "播放本地文件或网络音视频", "volume,media_toggle", "mpv");
            Add(result, "Windows 媒体播放器", "media", "播放音乐或视频", "volume,media_toggle", "wmplayer", "music.ui", "video.ui");
            Add(result, "网易云音乐", "media", "播放音乐、歌单和播客", "volume,media_toggle", "cloudmusic");
            Add(result, "QQ 音乐", "media", "播放音乐和歌单", "volume,media_toggle", "qqmusic");
            Add(result, "Spotify 音乐", "media", "播放音乐、歌单和播客", "volume,media_toggle", "spotify");
            Add(result, "腾讯视频", "media", "浏览或播放视频节目", "volume,media_toggle", "qqlive", "qqvideo", "tencentvideo");
            Add(result, "哔哩哔哩", "media", "浏览或播放视频、直播", "volume,media_toggle", "bilibili");
            Add(result, "优酷视频", "media", "浏览或播放视频节目", "volume,media_toggle", "youku");
            Add(result, "WPS Office", "editing", "文字、表格及演示；标题可区分当前文档类型", "countdown,open_timer,open_reminders", "wps");
            Add(result, "WPS 演示", "presentation", "制作或播放幻灯片", "countdown,open_timer", "wpp", "wpspresentation");
            Add(result, "WPS 表格", "editing", "编辑和查看电子表格", "countdown,open_reminders", "et");
            Add(result, "WPS PDF", "editing", "阅读或编辑 PDF 文档", "countdown,open_reminders", "wpspdf");
            Add(result, "PowerPoint 演示", "presentation", "制作或播放幻灯片、课堂演示", "countdown,open_timer", "powerpnt");
            Add(result, "希沃白板", "presentation", "课堂互动白板、课件演示与教学", "countdown,open_timer", "easinote", "easinote5", "easinote6", "seewowhiteboard");
            Add(result, "希沃展台", "presentation", "教学内容展示", "countdown,open_timer", "easishow");
            Add(result, "Microsoft Edge", "browser", "浏览网页；标题可区分视频、网课和在线文档", "按具体页面选择，不确定时无工具", "msedge");
            Add(result, "Google Chrome", "browser", "浏览网页；标题可区分视频、网课和在线文档", "按具体页面选择，不确定时无工具", "chrome");
            Add(result, "Firefox 浏览器", "browser", "浏览网页；标题可区分视频、网课和在线文档", "按具体页面选择，不确定时无工具", "firefox");
            Add(result, "网页浏览器", "browser", "浏览网页；标题可区分视频、网课和在线文档", "按具体页面选择，不确定时无工具", "brave", "opera", "vivaldi", "iexplore", "qqbrowser", "360chrome", "360se", "sogouexplorer", "arc");
            Add(result, "Word 文档", "editing", "编辑和阅读文字文档", "countdown,open_reminders", "winword");
            Add(result, "Excel 表格", "editing", "编辑和分析电子表格", "countdown,open_reminders", "excel");
            Add(result, "记事本", "editing", "编辑纯文本", "countdown,open_reminders", "notepad", "notepad++");
            Add(result, "Visual Studio Code", "editing", "编辑代码、文本和项目文件", "countdown,open_reminders", "code");
            Add(result, "Visual Studio", "editing", "软件开发与调试", "countdown,open_reminders", "devenv");
            Add(result, "笔记编辑器", "editing", "记录和编辑笔记", "countdown,open_reminders", "onenote", "typora", "obsidian");
            return result;
        }
        private static void Add(Dictionary<string, AssistantSoftwareKnowledge> destination, string name, string category, string purpose, string tools, params string[] aliases)
        {
            var knowledge = new AssistantSoftwareKnowledge(name, category, purpose, tools, aliases);
            foreach (string alias in aliases) destination.Add(alias, knowledge);
        }
    }
}
