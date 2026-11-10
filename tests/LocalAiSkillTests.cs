using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using FreeIsland;

internal static class LocalAiSkillTests
{
    private static int checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; } catch (InvalidDataException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, message);
    }
    private static LocalAiAction Action(string kind, string title, int seconds = 0) { return new LocalAiAction { Kind = kind, Title = title, Seconds = seconds }; }
    private static List<LocalAiAction> Actions() { return new List<LocalAiAction> { Action("countdown", "课堂讨论", 300), Action("media_toggle", "播放或暂停"), Action("open_reminders", "提醒日程") }; }
    private static string FileName(string directory) { return Path.Combine(directory, "assistant-skills.json"); }
    private static byte[] Document(params LocalAiSkill[] skills)
    {
        using (var stream = new MemoryStream())
        {
            new DataContractJsonSerializer(typeof(LocalAiSkillDocument)).WriteObject(stream, new LocalAiSkillDocument { Version = 1, Skills = skills.ToList() });
            return stream.ToArray();
        }
    }
    private static LocalAiSkill Skill() { return new LocalAiSkill { Id = "dcd7f9e0-29da-4ac7-9103-ece8ec481edb", Name = "课堂开始", Actions = Actions() }; }
    private static void Normal(string directory)
    {
        var store = new LocalAiSkillStore(directory);
        Check(store.Error == null && store.List().Count == 0, "Missing store starts empty without error");
        Check(!Directory.Exists(directory), "Reading an empty store does not create directories");
        var incoming = Actions();
        var saved = store.Save("  课堂开始  ", incoming);
        Check(saved.Name == "课堂开始" && saved.Actions.Count == 3, "Save trims skill name and preserves a three-action panel");
        Guid identifier;
        Check(Guid.TryParseExact(saved.Id, "D", out identifier) && identifier != Guid.Empty, "Save creates a strict GUID identifier");
        Check(File.Exists(FileName(directory)), "Explicit Save persists a JSON document");
        byte[] persisted = File.ReadAllBytes(FileName(directory));
        incoming[0].Seconds = 60; incoming.Clear(); saved.Name = "changed"; saved.Actions[0].Seconds = 120; saved.Actions.Clear();
        Check(store.List()[0].Name == "课堂开始" && store.List()[0].Actions[0].Seconds == 300 && store.List()[0].Actions.Count == 3, "Save input and result are defensive copies");
        var copy = store.List(); copy[0].Name = "mutated"; copy[0].Actions[0].Title = "mutated"; copy[0].Actions.RemoveAt(1);
        Check(store.List()[0].Name == "课堂开始" && store.List()[0].Actions[0].Title == "课堂讨论" && store.List()[0].Actions.Count == 3, "List cannot alias mutable store data");
        Check(persisted.SequenceEqual(File.ReadAllBytes(FileName(directory))), "Listing and mutating a returned panel never writes the store");
        var reloaded = new LocalAiSkillStore(directory);
        Check(reloaded.Error == null && reloaded.List().Count == 1 && reloaded.List()[0].Actions[0].Seconds == 300, "Fresh instance restores validated action parameters");
        Check(reloaded.List()[0].Id == store.List()[0].Id, "Identifiers survive reload");
        var second = store.Save("第二个", new[] { Action("volume", "音量") });
        Check(store.List().Count == 2 && store.List()[1].Id == second.Id, "A subsequent atomic replacement preserves prior skills and order");
        string firstId = store.List()[0].Id;
        store.Remove(firstId);
        Check(store.List().Count == 1 && store.List()[0].Id == second.Id, "Remove affects only the specified skill");
        Check(new LocalAiSkillStore(directory).List()[0].Id == second.Id, "Removal persists without executing any action");
        persisted = File.ReadAllBytes(FileName(directory));
        store.Remove("dcd7f9e0-29da-4ac7-9103-ece8ec481edb");
        Check(persisted.SequenceEqual(File.ReadAllBytes(FileName(directory))), "Removing an absent valid identifier is a no-op");
        foreach (string invalid in new[] { null, "", "../assistant-skills.json", Guid.Empty.ToString("D"), second.Id.ToUpperInvariant(), second.Id.Replace("-", "") })
            Reject(delegate { store.Remove(invalid); }, "Non-canonical skill identifier is rejected");
        Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "Atomic persistence leaves no temporary files after success");
    }
    private static void Validation(string directory)
    {
        var store = new LocalAiSkillStore(directory);
        foreach (string invalid in new[] { null, "", "  ", new string('中', 25), "换行\n", "\u202e名称", "名字\0" })
            Reject(delegate { store.Save(invalid, Actions()); }, "Invalid or control-bearing name is rejected");
        foreach (IList<LocalAiAction> invalid in new IList<LocalAiAction>[] {
            null, new LocalAiAction[0], new[] { (LocalAiAction)null },
            new[] { Action("shell", "命令") }, new[] { Action("shutdown", "关机") },
            new[] { Action("volume", "音量"), Action("volume", "重复音量") },
            new[] { Action("volume", "音量"), Action("open_timer", "计时"), Action("media_toggle", "媒体"), Action("open_reminders", "提醒") },
            new[] { Action("countdown", "不足一分钟", 59) }, new[] { Action("countdown", "超过四小时", 14401) },
            new[] { Action("volume", "错误参数", 60) }, new[] { Action("volume", "") }, new[] { Action("volume", "\u202e伪装") },
            new[] { Action("volume", new string('a', 25)) }
        }) Reject(delegate { store.Save("测试", invalid); }, "Existing action validator rejects unsupported, duplicate or malformed tools");
        Check(store.Error == null && store.List().Count == 0 && !Directory.Exists(directory), "Input errors neither poison the store nor create files");
        var minimum = store.Save(new string('中', 24), new[] { Action("countdown", "一分钟", 60) });
        var maximum = store.Save("四小时", new[] { Action("countdown", "四小时", 14400) });
        Check(minimum.Name.Length == 24 && maximum.Actions[0].Seconds == 14400, "Name and countdown boundary values are accepted");
        foreach (string kind in new[] { "volume", "media_toggle", "open_timer", "open_reminders" })
            Check(store.Save(kind, new[] { Action(kind, "  快捷操作  ") }).Actions[0].Title == "快捷操作", "All supported non-timer action kinds round-trip and normalize titles");
        while (store.List().Count < 12) store.Save("技能" + store.List().Count, new[] { Action("open_timer", "计时") });
        byte[] before = File.ReadAllBytes(FileName(directory));
        Reject(delegate { store.Save("第十三个", Actions()); }, "At most twelve skills may be saved");
        Check(before.SequenceEqual(File.ReadAllBytes(FileName(directory))) && store.List().Count == 12, "Capacity rejection does not mutate stored skills");
    }
    private static void Corrupt(string root)
    {
        var badId = Skill(); badId.Id = "../../file";
        var emptyId = Skill(); emptyId.Id = Guid.Empty.ToString("D");
        var badName = Skill(); badName.Name = "不合法\n";
        var untrimmedName = Skill(); untrimmedName.Name = " 空白 ";
        var badAction = Skill(); badAction.Actions = new List<LocalAiAction> { Action("shell", "任意命令") };
        var noAction = Skill(); noAction.Actions.Clear();
        var duplicateAction = Skill(); duplicateAction.Actions = new List<LocalAiAction> { Action("volume", "音量"), Action("volume", "音量二") };
        var invalid = new List<byte[]> {
            new byte[0], Encoding.UTF8.GetBytes("not json"), Encoding.UTF8.GetBytes("{}"), Encoding.UTF8.GetBytes("{\"version\":1,\"skills\":[]} trailing junk"),
            Encoding.UTF8.GetBytes("{\"version\":2,\"skills\":[]}"), Encoding.UTF8.GetBytes("{\"version\":1,\"skills\":null}"),
            new byte[65537], Document(badId), Document(emptyId), Document(badName), Document(untrimmedName), Document(badAction), Document(noAction), Document(duplicateAction), Document(Skill(), Skill())
        };
        invalid.Add(Document(Enumerable.Range(0, 13).Select(i => new LocalAiSkill { Id = Guid.NewGuid().ToString("D"), Name = "技能" + i, Actions = Actions() }).ToArray()));
        for (int i = 0; i < invalid.Count; i++)
        {
            string directory = Path.Combine(root, "invalid-" + i);
            Directory.CreateDirectory(directory); File.WriteAllBytes(FileName(directory), invalid[i]);
            var store = new LocalAiSkillStore(directory);
            Check(!string.IsNullOrEmpty(store.Error) && store.List().Count == 0, "Corrupt store is visible as an error without constructor failure or partial acceptance");
            Reject(delegate { store.Save("不可覆盖", Actions()); }, "Save refuses to overwrite corrupt data");
            Reject(delegate { store.Remove(Skill().Id); }, "Remove refuses to overwrite corrupt data");
            Check(invalid[i].SequenceEqual(File.ReadAllBytes(FileName(directory))), "Corrupt file is retained byte for byte");
        }
        string externalDirectory = Path.Combine(root, "external-change");
        var original = new LocalAiSkillStore(externalDirectory); original.Save("原始", Actions());
        byte[] external = Encoding.UTF8.GetBytes("external corruption"); File.WriteAllBytes(FileName(externalDirectory), external);
        Reject(delegate { original.Save("不能覆盖外部更改", Actions()); }, "External changes after load are detected before any replacement");
        Check(!string.IsNullOrEmpty(original.Error) && external.SequenceEqual(File.ReadAllBytes(FileName(externalDirectory))) && original.List().Count == 1, "Failed persistence preserves both external bytes and prior in-memory state");
        string concurrentDirectory = Path.Combine(root, "concurrent");
        var first = new LocalAiSkillStore(concurrentDirectory); var second = new LocalAiSkillStore(concurrentDirectory);
        first.Save("第一个窗口", Actions()); byte[] concurrent = File.ReadAllBytes(FileName(concurrentDirectory));
        Reject(delegate { second.Save("第二个窗口", Actions()); }, "An instance loaded before file creation cannot silently replace another writer");
        Check(concurrent.SequenceEqual(File.ReadAllBytes(FileName(concurrentDirectory))), "Conflicting writer does not lose the existing skill");
        string lockedDirectory = Path.Combine(root, "locked");
        var locked = new LocalAiSkillStore(lockedDirectory); locked.Save("保留已有技能", Actions());
        byte[] lockedBytes = File.ReadAllBytes(FileName(lockedDirectory));
        using (var hold = new FileStream(FileName(lockedDirectory), FileMode.Open, FileAccess.Read, FileShare.Read))
            Reject(delegate { locked.Save("保存应失败", Actions()); }, "Replacement failure is surfaced when the target is held without delete sharing");
        Check(locked.Error == null && locked.List().Count == 1 && lockedBytes.SequenceEqual(File.ReadAllBytes(FileName(lockedDirectory))), "Temporary replacement failure preserves data without permanently blocking writes");
        Check(Directory.GetFiles(lockedDirectory, "*.tmp").Length == 0, "Failed replacement cleans only its own temporary file");
        locked.Save("锁释放后重试", Actions());
        Check(locked.List().Count == 2 && new LocalAiSkillStore(lockedDirectory).List().Count == 2, "Explicit save succeeds after temporary lock is released without restarting");
    }

    private static void EditingAndLookup(string directory)
    {
        var store = new LocalAiSkillStore(directory);
        var initial = store.Save("课堂练习", Actions());
        var draft = Actions(); draft[0].Seconds = 600;
        var edited = store.Update(initial.Id, "  小组讨论  ", draft);
        Check(edited.Id == initial.Id && edited.Name == "小组讨论" && edited.Actions[0].Seconds == 600, "Update preserves identity and commits edited name and duration");
        draft[0].Seconds = 3600; edited.Actions[0].Seconds = 120;
        Check(store.List()[0].Actions[0].Seconds == 600 && new LocalAiSkillStore(directory).List()[0].Name == "小组讨论", "Update input/result are isolated and changes survive restart");
        byte[] before = File.ReadAllBytes(FileName(directory));
        Reject(delegate { store.Update(initial.Id, "", Actions()); }, "Invalid edit name is rejected");
        Reject(delegate { store.Update(initial.Id, "不能更新", new[] { Action("countdown", "越界", 5) }); }, "Invalid edited action is rejected");
        Reject(delegate { store.Update(Guid.NewGuid().ToString("D"), "已移除", Actions()); }, "A stale missing identifier cannot create a replacement");
        Reject(delegate { store.Update("../技能", "不合法", Actions()); }, "Edit identifiers are validated");
        Check(store.Error == null && before.SequenceEqual(File.ReadAllBytes(FileName(directory))) && store.List()[0].Actions[0].Seconds == 600, "Rejected edits preserve disk and memory without poisoning future writes");
        store.Save("小组讨论进阶", new[] { Action("open_timer", "计时") });
        store.Save("Evening MUSIC", new[] { Action("volume", "音量") });
        Check(store.Find("小组讨论").Count == 1 && store.Find("小组讨论")[0].Id == initial.Id, "Exact skill match takes precedence over substring matches");
        Check(store.Find("讨论").Count == 2 && store.Find("music").Count == 1, "Lookup supports partial names and invariant case-insensitive matching");
        Check(store.Find(null).Count == 0 && store.Find("  ").Count == 0 && store.Find(new string('a', 25)).Count == 0 && store.Find("讨论\n").Count == 0, "Empty, oversized and control-bearing searches produce no skills");
        var found = store.Find("讨论"); found[0].Name = "伪造"; found[0].Actions.Clear();
        Check(store.List()[0].Name == "小组讨论" && store.List()[0].Actions.Count == 3, "Lookup returns defensive copies without executing or mutating anything");
        while (store.List().Count < 12) store.Save("练习" + store.List().Count, new[] { Action("open_timer", "计时") });
        store.Update(initial.Id, "容量满也能改名", Actions());
        Check(store.List().Count == 12 && store.List()[0].Name == "容量满也能改名", "Editing a full library does not consume an additional slot");
    }

    private static void Transfer(string root)
    {
        string sourceDirectory = Path.Combine(root, "source"), targetDirectory = Path.Combine(root, "target");
        var source = new LocalAiSkillStore(sourceDirectory);
        var first = source.Save("课前准备", Actions()); var second = source.Save("调整音量", new[] { Action("volume", "音量") });
        byte[] exported = source.Export();
        Check(exported.SequenceEqual(File.ReadAllBytes(FileName(sourceDirectory))), "Export represents the complete version-one library without changing it");
        var target = new LocalAiSkillStore(targetDirectory);
        var preview = target.PreviewImport(exported);
        Check(preview.Count == 2 && preview[0].Name == first.Name && !Directory.Exists(targetDirectory), "Import preview validates names and parameters without creating or executing anything");
        preview[0].Name = "篡改预览"; preview[0].Actions.Clear();
        Check(target.Import(exported) == 2 && target.List()[0].Name == first.Name && target.List()[0].Actions.Count == 3, "Explicit import uses validated source bytes rather than mutable preview aliases");
        Check(target.List()[0].Id != first.Id && target.List()[1].Id != second.Id, "Imported skills receive new local identifiers");
        var restored = new LocalAiSkillStore(targetDirectory);
        Check(restored.List().Count == 2 && restored.List()[0].Actions[0].Seconds == 300 && restored.List()[1].Actions[0].Kind == "volume", "Export/import preserves useful action content across independent data directories");
        byte[] before = File.ReadAllBytes(FileName(targetDirectory));
        Check(target.Import(exported) == 0 && target.List().Count == 2 && before.SequenceEqual(File.ReadAllBytes(FileName(targetDirectory))), "Repeated import deduplicates exact content without rewriting the file");
        var altered = Skill(); altered.Id = first.Id; altered.Name = first.Name; altered.Actions[0].Seconds = 600;
        Check(target.Import(Document(altered)) == 1 && target.List().Count == 3 && target.List()[0].Actions[0].Seconds == 300 && target.List()[2].Actions[0].Seconds == 600, "An imported same-name edited composition is added without overwriting an existing skill");
        var duplicate = Skill(); duplicate.Id = Guid.NewGuid().ToString("D");
        var duplicate2 = Skill(); duplicate2.Id = Guid.NewGuid().ToString("D");
        Check(target.Import(Document(duplicate, duplicate2)) == 1, "Equivalent skills inside one import consume only one new slot");
        while (target.List().Count < 11) target.Save("现有" + target.List().Count, new[] { Action("open_timer", "计时") });
        before = File.ReadAllBytes(FileName(targetDirectory));
        var extra1 = Skill(); extra1.Id = Guid.NewGuid().ToString("D"); extra1.Name = "额外一";
        var extra2 = Skill(); extra2.Id = Guid.NewGuid().ToString("D"); extra2.Name = "额外二";
        Reject(delegate { target.Import(Document(extra1, extra2)); }, "An entire import is rejected if additions would exceed capacity");
        Check(target.List().Count == 11 && before.SequenceEqual(File.ReadAllBytes(FileName(targetDirectory))), "An over-capacity import never partially adds its first item");
        var unsupported = Skill(); unsupported.Actions = new List<LocalAiAction> { Action("shell", "运行脚本") };
        var invalid = new[] { new byte[0], new byte[65537], Encoding.UTF8.GetBytes("not json"), Encoding.UTF8.GetBytes("{\"version\":1,\"skills\":[]} junk"), Document(unsupported), Document(Skill(), Skill()) };
        foreach (var data in invalid)
        {
            Reject(delegate { target.PreviewImport(data); }, "Malformed, unsupported or excessive import is rejected during preview");
            Reject(delegate { target.Import(data); }, "Malformed import cannot bypass preview validation");
        }
        Check(target.Error == null && before.SequenceEqual(File.ReadAllBytes(FileName(targetDirectory))) && target.List().Count == 11, "Rejected import bytes leave the existing library usable and byte-identical");
        string emptyDirectory = Path.Combine(root, "empty"); var empty = new LocalAiSkillStore(emptyDirectory);
        Check(empty.PreviewImport(empty.Export()).Count == 0 && !Directory.Exists(emptyDirectory), "Exporting an empty library is side-effect free and produces a valid portable document");
        byte[] sourceBytes = File.ReadAllBytes(FileName(sourceDirectory));
        exported[0] = 0;
        Check(sourceBytes.SequenceEqual(source.Export()), "Exported arrays cannot mutate source data or subsequent exports");
    }
    public static int Main(string[] args)
    {
        try
        {
            string root = Path.GetFullPath(args.Length == 0 ? Path.Combine("artifacts", "skills-platform-1.0.13") : args[0]);
            root = Path.Combine(root, "fixtures-" + Guid.NewGuid().ToString("N"));
            Normal(Path.Combine(root, "normal")); Validation(Path.Combine(root, "validation")); Corrupt(Path.Combine(root, "corrupt"));
            EditingAndLookup(Path.Combine(root, "editing")); Transfer(Path.Combine(root, "transfer"));
            Console.WriteLine("PASS " + checks + " local skill checks; isolated fixture directory: " + root); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
