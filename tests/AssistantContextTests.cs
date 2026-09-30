using System;
using FreeIsland;

internal static class AssistantContextTests
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    private static AssistantContextSnapshot Scene(string process, string title)
    {
        return AssistantContext.Create(process, title, false, true);
    }
    private static void Classification()
    {
        Check(Scene("vlc", "Lesson.mp4").Category == "media", "Native video player");
        Check(Scene("Chrome", "我的视频 - 哔哩哔哩 - Google Chrome").Category == "media", "Bilibili browser page");
        Check(Scene("msedge", "A lesson - YouTube").Description.Contains("YouTube"), "YouTube evidence described");
        Check(Scene("firefox", "节目 - 腾讯视频").Description.Contains("腾讯视频"), "Tencent video evidence described");
        Check(Scene("chrome", "高等数学 - 中国大学MOOC").Category == "presentation", "Online classroom browser page");
        Check(Scene("msedge", "课程表 - 腾讯文档").Category == "editing", "Online document is not assumed to be playing media");
        Check(Scene("chrome", "Lesson - Google Slides").Category == "presentation", "Browser slides");
        Check(Scene("wps", "地理.pptx - WPS Office").Category == "presentation", "WPS presentation distinguished");
        Check(Scene("wps", "教学计划.docx - WPS 文字").Category == "editing", "WPS document distinguished");
        Check(Scene("wps", "成绩.xlsx - WPS 表格").Category == "editing", "WPS spreadsheet distinguished");
        Check(Scene("powerpnt", "").Category == "presentation", "Native presentation without title");
        Check(Scene("notepad++.exe", "notes").Category == "editing", "Editor process normalized");
        Check(Scene("notepad++.exe", "notes").ProcessName == "notepad++", "Restricted plus sign allowed in executable identifiers");
        Check(Scene("chrome", "普通网页").Category == "browser", "Ordinary browser remains unclassified within browser");
        Check(Scene("unknownapp", "YouTube").Category == "unknown", "Unknown native app is not classified solely from arbitrary text");
        Check(AssistantContext.Create("unknownapp", "", true, false).Category == "unknown", "Fullscreen alone is not assumed to be video");
        Check(AssistantContext.Create("vlc", "", true, false).Description.Contains("全屏"), "Fullscreen metadata described");
    }
    private static void PrivacyAndFingerprint()
    {
        AssistantContextSnapshot hidden = AssistantContext.Create("chrome", "私人视频 - YouTube", false, false);
        Check(hidden.WindowTitle == "" && hidden.Category == "browser", "Opt-out discards title before classification");
        Check(!hidden.ModelContext.Contains("私人") && !hidden.Description.Contains("YouTube"), "Opt-out does not leak title via derived fields");
        AssistantContextSnapshot otherHidden = AssistantContext.Create("chrome", "另一私密页面", false, false);
        Check(hidden.Fingerprint == otherHidden.Fingerprint, "Opt-out fingerprint contains no title data");
        AssistantContextSnapshot video = Scene("chrome", "视频 - YouTube");
        AssistantContextSnapshot docs = Scene("chrome", "计划 - 腾讯文档");
        Check(video.Fingerprint != docs.Fingerprint, "Switching pages within same browser changes fingerprint");
        Check(video.Fingerprint != Scene("chrome", "另一视频 - YouTube").Fingerprint, "Different titles within same category remain distinct scenes");
        Check(video.Fingerprint != AssistantContext.Create("chrome", "视频 - YouTube", true, true).Fingerprint, "Fullscreen transition changes fingerprint");
        Check(video.Fingerprint.Length == 64 && !video.Fingerprint.Contains("YouTube"), "Fingerprint does not store raw title text");
        Check(Scene("FreeIsland", "private").ProcessName == "", "Own app excluded");
        Check(Scene("llama-server", "private").ProcessName == "", "Owned model runtime excluded");
        Check(Scene("../chrome", "private").ProcessName == "", "Invalid process identifier rejected");
        Check(AssistantContext.CleanTitle("  标题\r\n内容\u202E\u200B\t末尾  ") == "标题 内容 末尾", "Control/format characters sanitized");
        AssistantContextSnapshot longTitle = Scene(new string('a', 64), new string('中', 200));
        Check(longTitle.WindowTitle.Length == 160, "Title bounded to 160 characters");
        Check(longTitle.ModelContext.Length <= 300, "Model metadata bounded to service limit");
        string surrogateEdge = new string('a', 159) + "\ud83d\ude00";
        string trimmed = Scene("chrome", surrogateEdge).WindowTitle;
        Check(trimmed.Length == 159 && !char.IsHighSurrogate(trimmed[trimmed.Length - 1]), "Truncation avoids dangling high surrogate");
    }
    private static void Knowledge()
    {
        foreach (string alias in new[] { "PotPlayer", "PotPlayerMini", "PotPlayerMini64", "PotPlayer64.exe" })
        {
            AssistantSoftwareKnowledge known = AssistantKnowledge.Lookup(alias);
            Check(known != null && known.Name == "PotPlayer 播放器" && known.Category == "media", "PotPlayer executable alias: " + alias);
            AssistantContextSnapshot scene = AssistantContext.Create(alias, "", false, false);
            Check(scene.Category == "media" && scene.Description.Contains("PotPlayer") && scene.Description.Contains("依据"), "Knowledge participates in observed scene: " + alias);
            Check(scene.ModelContext.Contains("播放本地音视频") && scene.ModelContext.Contains("media_toggle") && scene.ModelContext.Contains("volume"), "Model receives software purpose and available tools: " + alias);
        }
        foreach (string alias in new[] { "vlc", "mpv", "cloudmusic", "qqmusic", "spotify", "wps", "powerpnt", "EasiNote", "msedge", "code" })
            Check(AssistantKnowledge.Lookup(alias) != null, "Required software knowledge: " + alias);
        Check(AssistantContext.Create("EasiNote", "", false, false).Category == "presentation", "Seewo classroom software identified from offline knowledge");
        Check(AssistantKnowledge.Lookup("made-up-player") == null, "Unknown software knowledge is not fabricated");
        Check(AssistantContext.Create("PotPlayerMini64", new string('中', 200), true, true).ModelContext.Length <= 300, "Knowledge plus title still respects model context bound");
    }
    private static void AutomaticScope()
    {
        foreach (string player in new[] { "PotPlayerMini64.exe", "vlc", "mpv", "cloudmusic", "qqmusic", "foobar2000", "kugou" })
            Check(AssistantContext.IsAutomaticMedia(AssistantContext.CreateForSuggestions(player, "unread", false, false)), "Known player permits automatic suggestions: " + player);
        foreach (string process in new[] { "code", "winword", "powerpnt", "wps", "explorer", "unknown", "vlc-helper" })
            Check(!AssistantContext.IsAutomaticMedia(AssistantContext.CreateForSuggestions(process, "Video - YouTube", false, false)), "Non-player does not trigger inference: " + process);
        foreach (string title in new[] { "视频 - YouTube - Google Chrome", "课程_哔哩哔哩_bilibili - Google Chrome", "音乐 - 网易云音乐 - Google Chrome", "歌曲 - QQ音乐 - Google Chrome", "Song | Spotify - Google Chrome", "直播 - 腾讯视频 - Google Chrome" })
        {
            var scene = AssistantContext.CreateForSuggestions("chrome", title, false, false);
            Check(AssistantContext.IsAutomaticMedia(scene), "Chrome explicitly recognizes media site suffix: " + title);
            Check(scene.WindowTitle == "" && !scene.ModelContext.Contains(title) && scene.ModelContext.Contains("不提供原文"), "Chrome title used locally but not passed to model without title opt-in");
        }
        foreach (string title in new[] { "", "New Tab - Google Chrome", "日程 - Google Calendar - Google Chrome", "YouTube - Google 搜索 - Google Chrome", "如何下载哔哩哔哩 - 知乎 - Google Chrome", "QQ音乐安装教程 - 百度搜索 - Google Chrome", "Spotify account settings - Google Chrome", "GitHub - Google Chrome" })
            Check(!AssistantContext.IsAutomaticMedia(AssistantContext.CreateForSuggestions("chrome", title, false, false)), "Ordinary Chrome page stays quiet: " + title);
        var media = AssistantContext.CreateForSuggestions("chrome", "视频一 - YouTube - Google Chrome", false, false);
        var second = AssistantContext.CreateForSuggestions("chrome", "视频二 - YouTube - Google Chrome", false, false);
        Check(media.Fingerprint != second.Fingerprint, "Locally recognized Chrome title changes still reset stable scene");
        Check(AssistantContext.CreateForSuggestions("chrome", "私密视频 - YouTube", false, true).WindowTitle == "私密视频 - YouTube", "Explicit title opt-in continues to include title");
        Check(AssistantContext.CreateForSuggestions("notepad", "private note", false, false).WindowTitle == "", "Other application titles remain unread by default");
        var gate = new AssistantContextGate();
        var start = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var ordinary = AssistantContext.CreateForSuggestions("chrome", "新闻 - Google Chrome", false, false);
        Check(!gate.TryBegin(ordinary, start, DateTime.MinValue) && !gate.TryBegin(ordinary, start.AddHours(2), DateTime.MinValue), "Non-media cannot start automatic inference even after a long dwell");
        Check(!gate.TryBegin(media, start.AddHours(2), DateTime.MinValue), "Opening a media page starts a new dwell");
        Check(gate.TryBegin(media, start.AddHours(2).AddSeconds(15), DateTime.MinValue), "Chrome media title becomes eligible after stable dwell");
        Check(!gate.CanPresent(ordinary, start.AddHours(2).AddSeconds(16), DateTime.MinValue), "Page changed to non-media while inference ran blocks late display");
        Check(gate.CanPresentResult(media, media, start.AddHours(2).AddSeconds(16), DateTime.MinValue), "Unchanged eligible scene may deliver result");
        Check(!gate.CanPresentResult(media, second, start.AddHours(2).AddSeconds(16), DateTime.MinValue), "Changing to another video during inference discards old result");
        Check(!gate.CanPresentResult(media, ordinary, start.AddHours(2).AddSeconds(16), DateTime.MinValue), "Changing to a normal page discards old result");
        Check(!gate.CanPresentResult(media, AssistantContext.Empty(), start.AddHours(2).AddSeconds(16), DateTime.MinValue), "Unavailable window discards old result");
        Check(!gate.CanPresentResult(media, media, start.AddHours(2).AddSeconds(16), start.AddHours(3)), "One-hour quiet period discards an in-flight result");
        var fullScreen = AssistantContext.CreateForSuggestions("chrome", "视频一 - YouTube - Google Chrome", true, false);
        Check(!gate.CanPresentResult(media, fullScreen, start.AddHours(2).AddSeconds(16), DateTime.MinValue), "Fullscreen transition discards old result");
    }
    private static void Timing()
    {
        DateTime start = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime unpaused = DateTime.MinValue;
        var video = Scene("potplayer", "Video one");
        var changedTitle = Scene("potplayer", "Video two");
        var document = Scene("vlc", "Lesson video");
        var gate = new AssistantContextGate();
        Check(!gate.TryBegin(video, start, unpaused), "Scene first needs stability");
        Check(!gate.TryBegin(video, start.AddMilliseconds(14999), unpaused), "No request before fifteen seconds");
        Check(gate.TryBegin(video, start.AddSeconds(15), unpaused), "Stable for fifteen seconds permits request");
        Check(!gate.TryBegin(video, start.AddSeconds(74), unpaused), "Empty results cannot retry before one minute");
        Check(gate.TryBegin(video, start.AddSeconds(75), unpaused), "Empty results can retry after one minute");
        gate.MarkPresented(video.ProcessName, start.AddSeconds(75));
        Check(!gate.TryBegin(document, start.AddSeconds(650), unpaused), "New app starts its own stability interval");
        Check(!gate.TryBegin(document, start.AddSeconds(674), unpaused), "A different app cannot bypass the ten minute global cooldown");
        Check(gate.TryBegin(document, start.AddSeconds(675), unpaused), "Different app is eligible exactly ten minutes after display");
        gate.MarkPresented(document.ProcessName, start.AddSeconds(675));
        Check(!gate.TryBegin(changedTitle, start.AddSeconds(1300), unpaused), "Changing title does not immediately trigger a suggestion");
        Check(!gate.TryBegin(changedTitle, start.AddSeconds(1315), unpaused), "Changing title cannot bypass thirty minute app cooldown");
        Check(!gate.TryBegin(changedTitle, start.AddSeconds(1874), unpaused), "Same app remains quiet just before thirty minutes");
        Check(gate.TryBegin(changedTitle, start.AddSeconds(1875), unpaused), "Same app eligible at thirty minute boundary");
        Check(!gate.CanPresent(video, start, unpaused), "Clock rollback cannot produce a burst of repeated suggestions");

        var full = AssistantContext.Create("potplayer", "Video one", true, true);
        var fullGate = new AssistantContextGate();
        Check(!fullGate.TryBegin(full, start, unpaused), "Fullscreen never starts inference");
        Check(!fullGate.TryBegin(full, start.AddMinutes(1), unpaused), "Fullscreen does not accumulate stable time");
        Check(!fullGate.TryBegin(video, start.AddMinutes(1), unpaused), "Leaving fullscreen starts a fresh stability interval");
        Check(fullGate.TryBegin(video, start.AddSeconds(75), unpaused), "Windowed scene can resume after stability");
        Check(!fullGate.CanPresent(full, start.AddSeconds(80), unpaused), "Fullscreen reached during inference blocks late display");
        Check(!fullGate.CanPresent(AssistantContext.Empty(), start.AddSeconds(80), unpaused), "Missing foreground blocks late display");
        Check(!fullGate.CanPresent(video, start.AddSeconds(80), start.AddHours(1)), "A newly applied snooze blocks in-flight results");

        var pauseGate = new AssistantContextGate();
        Check(!pauseGate.TryBegin(video, start, start.AddHours(1)), "One-hour pause prevents requests");
        Check(!pauseGate.TryBegin(video, start.AddSeconds(3599), start.AddHours(1)), "Pause holds through its last second");
        Check(pauseGate.CanPresent(video, start.AddHours(1), start.AddHours(1)), "Pause expires exactly at its deadline");
        pauseGate.Observe("", start.AddSeconds(3599));
        Check(!pauseGate.TryBegin(video, start.AddHours(1), start.AddHours(1)), "Polling resume still waits for stable context");
        Check(pauseGate.TryBegin(video, start.AddSeconds(3615), start.AddHours(1)), "Automatic suggestions resume after pause and stability");
        Check(!pauseGate.TryBegin(null, start.AddHours(2), unpaused), "Null context is quiet");
    }
    private static int Main()
    {
        try
        {
            Classification(); PrivacyAndFingerprint(); Knowledge(); AutomaticScope(); Timing();
            Console.WriteLine("PASS " + checks + " assistant context checks. Offline fixtures only; no foreground capture, model, network or system actions.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
