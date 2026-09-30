using System;
using System.Collections.Generic;
using System.Linq;
using FreeIsland;

internal static class AssistantMediaSuggestionTests
{
    private static int checks;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    private static int Main()
    {
        try
        {
            var proposals = new List<LocalAiAction> {
                new LocalAiAction { Kind = "volume", Title = "调节音量" },
                new LocalAiAction { Kind = "media_toggle", Title = "播放暂停" },
                new LocalAiAction { Kind = "countdown", Title = "休息提醒", Seconds = 1500 },
                new LocalAiAction { Kind = "open_timer", Title = "记录学习时长" },
                new LocalAiAction { Kind = "open_reminders", Title = "添加稍后提醒" },
                new LocalAiAction { Kind = "shell", Title = "运行脚本" }, null
            };
            var player = AssistantContext.CreateForSuggestions("potplayermini64", "", false, false);
            var media = AssistantContext.CreateForSuggestions("chrome", "课程 - YouTube - Google Chrome", false, false);
            foreach (var scene in new[] { player, media })
            {
                var allowed = AssistantController.AutomaticActions(scene, proposals);
                Check(allowed.Count == 5, "Eligible media scene preserves all five bounded tools, not only volume");
                Check(allowed.Any(a => a.Kind == "countdown") && allowed.Any(a => a.Kind == "open_reminders"), "Media can suggest useful timed reminders");
                Check(!allowed.Any(a => a.Kind == "shell"), "Arbitrary model-created code cannot become an automatic tool");
                Check(AssistantController.AutomaticActions(scene, null).Count == 0, "Missing model reply safely yields no proposal");
            }
            foreach (string process in new[] { "chrome", "code", "winword", "powerpnt", "explorer", "unknown-player" })
            {
                var scene = AssistantContext.CreateForSuggestions(process, "YouTube - Google 搜索 - Google Chrome", false, false);
                Check(AssistantController.AutomaticActions(scene, proposals).Count == 0, "LLM volume or other proposals cannot bypass automatic scene eligibility: " + process);
            }
            Check(AssistantController.AutomaticActions(null, proposals).Count == 0, "Null scene cannot receive automatic suggestions");
            Check(AssistantController.RuleActions("winword").Any(a => a.Kind == "countdown"), "Manual rules retain non-media productivity actions");
            Check(AssistantController.RuleActions("powerpnt").Any(a => a.Kind == "open_timer"), "Manual rules retain presentation timer");
            Check(AssistantController.RuleActions("vlc").Any(a => a.Kind == "volume"), "Explicit player tools remain available");
            Console.WriteLine("PASS " + checks + " automatic media action checks. Synthetic scenes only; no model, foreground capture, audio or network.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
