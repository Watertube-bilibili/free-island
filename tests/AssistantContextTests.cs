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
    private static void Timing()
    {
        DateTime start = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var gate = new AssistantContextGate();
        Check(!gate.TryBegin("video", start), "Scene first needs stability");
        Check(!gate.TryBegin("video", start.AddMilliseconds(2999)), "No request before three seconds");
        Check(gate.TryBegin("video", start.AddSeconds(3)), "Stable for three seconds permits request");
        Check(!gate.TryBegin("video", start.AddSeconds(10)), "Requests globally throttled for eight seconds");
        Check(gate.TryBegin("video", start.AddSeconds(11)), "Empty suggestions can retry after eight seconds");
        gate.MarkPresented("video", start.AddSeconds(11));
        Check(!gate.TryBegin("video", start.AddSeconds(130)), "Same shown scene cools down for two minutes");
        Check(gate.TryBegin("video", start.AddSeconds(131)), "Same scene becomes eligible at two minutes");
        gate.Observe("document", start.AddSeconds(132));
        Check(!gate.TryBegin("document", start.AddSeconds(134)), "Changed browser title needs its own stability interval");
        Check(!gate.TryBegin("document", start.AddSeconds(135)), "Scene change does not bypass global throttle");
        Check(gate.TryBegin("document", start.AddSeconds(139)), "Different scene is not blocked by video cooldown");
        gate.Observe("", start.AddSeconds(140));
        Check(!gate.TryBegin("", start.AddSeconds(150)), "No requests for own or unavailable foreground");
        Check(!gate.TryBegin("document", start.AddSeconds(151)), "Returning from own app restarts stability interval");
        Check(gate.TryBegin("document", start.AddSeconds(154)), "Unshown returned scene is eligible after stability");
    }
    private static int Main()
    {
        try
        {
            Classification(); PrivacyAndFingerprint(); Knowledge(); Timing();
            Console.WriteLine("PASS " + checks + " assistant context checks. Offline fixtures only; no foreground capture, model, network or system actions.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
