using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using FreeIsland;

internal static class AssistantStoragePreferencesTests
{
    private static int checks;
    private static string root;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; } catch (IOException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, message);
    }
    private static void Switch(AssistantController controller, string path) { controller.SetModelDirectoryAsync(path).GetAwaiter().GetResult(); }
    private static bool Same(string first, string second) { return String.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase); }

    private static void Switching()
    {
        string data = Path.Combine(root, "settings"), first = Path.Combine(root, "first-models"), second = Path.Combine(root, "second-models");
        string defaultPath = Path.Combine(data, "local-ai");
        using (var engine = new CoreEngine(data, true))
        {
            using (var controller = new AssistantController(data, true, engine, delegate { return false; }))
            {
                Check(Same(controller.ModelDirectory, defaultPath), "Empty preference resolves to current default");
                Directory.CreateDirectory(defaultPath);
                File.WriteAllText(Path.Combine(defaultPath, "old-model.keep"), "old default data");
                controller.Preferences.RuleShortcutsEnabled = true;
                controller.Preferences.IncludeWindowTitle = true;
                controller.Preferences.Enabled = true;
                controller.Save();
                LocalAiService original = controller.Service;
                Switch(controller, first);
                Check(Same(controller.ModelDirectory, first) && Same(controller.Preferences.ModelDirectory, first), "Custom path becomes effective and persisted preference");
                Check(!controller.Preferences.Enabled && !controller.Service.IsRunning, "Directory switch disables model without starting another");
                Check(controller.Preferences.RuleShortcutsEnabled && controller.Preferences.IncludeWindowTitle, "Other assistant preferences survive switch");
                Check(!Object.ReferenceEquals(original, controller.Service), "Service instance replaced");
                bool disposed = false;
                try { original.StartAsync(controller.Preferences.SelectedModelId, System.Threading.CancellationToken.None).GetAwaiter().GetResult(); }
                catch (ObjectDisposedException) { disposed = true; }
                Check(disposed, "Previous service disposed rather than retained");
                Check(File.ReadAllText(Path.Combine(defaultPath, "old-model.keep")) == "old default data", "Old default model directory remains unchanged");
                Check(File.Exists(Path.Combine(first, "FreeIsland.local-ai.directory")), "New custom directory is claimed without model downloads");
                Directory.CreateDirectory(Path.Combine(first, "models"));
                File.WriteAllText(Path.Combine(first, "models", "retained.gguf"), "fixture only, not a model");
                Directory.CreateDirectory(second);
                Switch(controller, second);
                Check(Same(controller.ModelDirectory, second), "Existing empty local directory accepted");
                Check(File.ReadAllText(Path.Combine(first, "models", "retained.gguf")) == "fixture only, not a model", "Previously chosen model data is not moved or removed");
                Switch(controller, first);
                Check(Same(controller.ModelDirectory, first), "Previously claimed nonempty model directory can be selected again");
                var sameService = controller.Service;
                Switch(controller, first + Path.DirectorySeparatorChar);
                Check(Object.ReferenceEquals(sameService, controller.Service), "Selecting the same normalized path is a no-op");
            }
            using (var restored = new AssistantController(data, true, engine, delegate { return false; }))
            {
                Check(Same(restored.ModelDirectory, first) && !restored.Preferences.Enabled, "Custom directory and disabled state survive restart");
                Switch(restored, null);
                Check(Same(restored.ModelDirectory, defaultPath) && restored.Preferences.ModelDirectory == null, "Null resets the default without deleting data");
                Check(File.Exists(Path.Combine(first, "models", "retained.gguf")), "Reset leaves custom model data available");
            }
            using (var restored = new AssistantController(data, true, engine, delegate { return false; }))
                Check(Same(restored.ModelDirectory, defaultPath), "Default-directory reset persists");
        }
    }

    private static void RejectionsAndRollback()
    {
        string data = Path.Combine(root, "reject-settings"), first = Path.Combine(root, "active-models");
        using (var engine = new CoreEngine(data, true))
        using (var controller = new AssistantController(data, true, engine, delegate { return false; }))
        {
            Switch(controller, first);
            controller.Preferences.Enabled = true; controller.Save();
            LocalAiService original = controller.Service;
            foreach (string invalid in new[] { "relative-models", @"C:relative-models", @"\\server\share\models", @"\\?\C:\models", Path.GetPathRoot(first) })
                Reject(delegate { Switch(controller, invalid); }, "Invalid or nonlocal path rejected: " + invalid);
            string foreign = Path.Combine(root, "unrelated-data"); Directory.CreateDirectory(foreign);
            string important = Path.Combine(foreign, "important.txt"); File.WriteAllText(important, "do not change");
            Reject(delegate { Switch(controller, foreign); }, "Nonempty foreign directory rejected");
            Reject(delegate { Switch(controller, important); }, "A file cannot become a model directory");
            Check(File.ReadAllText(important) == "do not change" && Directory.GetFileSystemEntries(foreign).Length == 1, "Foreign files are neither overwritten nor annotated");
            Check(Same(controller.ModelDirectory, first) && controller.Preferences.Enabled && Object.ReferenceEquals(original, controller.Service), "Validation failures preserve active configuration and service");

            FieldInfo activating = typeof(AssistantController).GetField("activating", BindingFlags.Instance | BindingFlags.NonPublic);
            activating.SetValue(controller, true);
            try { Reject(delegate { Switch(controller, Path.Combine(root, "while-installing")); }, "Ongoing installation must be canceled before switching"); }
            finally { activating.SetValue(controller, false); }
            Check(!Directory.Exists(Path.Combine(root, "while-installing")), "Refused installation-time switch writes no target files");

            string settingsFile = Path.Combine(data, "assistant-settings.json");
            using (var locked = new FileStream(settingsFile, FileMode.Open, FileAccess.Read, FileShare.None))
                Reject(delegate { Switch(controller, Path.Combine(root, "save-failure-models")); }, "Settings save failure is reported");
            Check(Same(controller.ModelDirectory, first) && Same(controller.Preferences.ModelDirectory, first), "Failed commit rolls back effective and preferred path");
            Check(controller.Preferences.Enabled && Object.ReferenceEquals(original, controller.Service), "Failed commit keeps previous service and enabled preference");
            using (var restored = new AssistantController(data, true, engine, delegate { return false; }))
                Check(Same(restored.ModelDirectory, first), "Failed commit did not corrupt saved configuration");
            Check(!controller.Service.IsRunning && !controller.Service.IsBusy, "Safe fixture has no real runner or model operation");
        }
    }

    private static void OldAndInvalidPreferences()
    {
        string data = Path.Combine(root, "legacy-settings");
        using (var engine = new CoreEngine(data, true))
        {
            string path = Path.Combine(data, "assistant-settings.json");
            File.WriteAllText(path, "{\"Enabled\":false,\"RuleShortcutsEnabled\":true}");
            using (var controller = new AssistantController(data, true, engine, delegate { return false; }))
                Check(Same(controller.ModelDirectory, Path.Combine(data, "local-ai")) && controller.Preferences.RuleShortcutsEnabled, "Legacy settings default the new field without changing rules");
            using (var stream = File.Create(path))
                new DataContractJsonSerializer(typeof(AssistantPreferences)).WriteObject(stream, new AssistantPreferences { Enabled = true, ModelDirectory = @"\\untrusted\network\models" });
            using (var controller = new AssistantController(data, true, engine, delegate { return false; }))
            {
                Check(Same(controller.ModelDirectory, Path.Combine(data, "local-ai")) && !controller.Preferences.Enabled, "Invalid persisted path cannot enable a network model store");
                Check(!String.IsNullOrEmpty(controller.LastError), "Invalid saved path has an explicit explanation");
            }
        }
    }

    private static void PreferenceWriteRecovery()
    {
        string data = Path.Combine(root, "preference-recovery");
        using (var engine = new CoreEngine(data, true))
        using (var controller = new AssistantController(data, true, engine, delegate { return false; }))
        {
            controller.Save();
            string path = Path.Combine(data, "assistant-settings.json");
            byte[] original = File.ReadAllBytes(path);
            string originalModel = controller.Preferences.SelectedModelId;
            var lastContext = typeof(AssistantController).GetProperty("LastContext").GetSetMethod(true);
            lastContext.Invoke(controller, new object[] { AssistantContext.Create("chrome", "测试私人标题 - YouTube", false, true) });
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                controller.Preferences.Enabled = true;
                controller.Preferences.RuleShortcutsEnabled = true;
                controller.Preferences.IncludeWindowTitle = true;
                controller.Preferences.SelectedModelId = LocalAiCatalog.Models.First(m => m.Id != originalModel).Id;
                Reject(controller.Save, "A locked settings target reports a failed preference transaction");
            }
            Check(!controller.Preferences.Enabled && !controller.Preferences.RuleShortcutsEnabled && !controller.Preferences.IncludeWindowTitle && controller.Preferences.SelectedModelId == originalModel, "Failed save restores all four committed assistant preferences");
            Check(controller.LastContext.WindowTitle.Length == 0 && !controller.LastContext.ModelContext.Contains("测试私人标题"), "Rolled-back title permission also removes raw title from retained context");
            Check(original.SequenceEqual(File.ReadAllBytes(path)), "A failed preference commit preserves the prior settings bytes");
            controller.Preferences.IncludeWindowTitle = true;
            controller.Save();
            using (var restored = new AssistantController(data, true, engine, delegate { return false; }))
                Check(restored.Preferences.IncludeWindowTitle, "An unlocked preference save can be retried and survives restart");
            lastContext.Invoke(controller, new object[] { AssistantContext.Create("chrome", "已授权但随后清除的标题 - YouTube", false, true) });
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                controller.Preferences.IncludeWindowTitle = false;
                Reject(controller.Save, "A failed title-disable commit is reported");
            }
            Check(controller.Preferences.IncludeWindowTitle && controller.LastContext.WindowTitle.Length == 0 && !controller.LastContext.ModelContext.Contains("已授权但随后清除的标题"), "Rollback never resurrects title text already cleared during the request");
            Check(!controller.Service.IsRunning && !controller.Service.IsBusy, "Preference recovery never starts a model");
        }
    }

    private static void StopWhenSettingsAreLocked()
    {
        string data = Path.Combine(root, "stop-recovery");
        using (var engine = new CoreEngine(data, true))
        using (var controller = new AssistantController(data, true, engine, delegate { return false; }))
        {
            controller.Preferences.Enabled = true; controller.Save();
            string path = Path.Combine(data, "assistant-settings.json");
            byte[] original = File.ReadAllBytes(path);
            // An owned fixture lease exercises StopRunner cleanup without a real process or model.
            var lease = new FileStream(Path.Combine(data, "owned-lease.fixture"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            typeof(LocalAiService).GetField("modelLease", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller.Service, lease);
            try
            {
                string failure = null;
                using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    try { controller.DisableModel(); } catch (InvalidOperationException ex) { failure = ex.Message; }
                    Check(failure != null && failure.Contains("本次模型已关闭") && failure.Contains("关闭状态未保存") && failure.Contains("下次启动"), "Failed persistence explicitly distinguishes current shutdown from next-launch preference");
                    Check(!controller.Preferences.Enabled && controller.Service.Status.Contains("已停止") && !controller.Service.IsRunning, "Disable stops the service even if its settings cannot be persisted");
                    Check(!lease.CanRead && !lease.CanWrite, "Failed persistence cannot prevent release of the owned model lease");
                    controller.Preferences.RuleShortcutsEnabled = true;
                    Reject(controller.Save, "Another preference save remains independently fallible while locked");
                    Check(!controller.Preferences.Enabled && !controller.Preferences.RuleShortcutsEnabled, "A later failed save cannot re-enable a model stopped for this session");
                }
                Check(original.SequenceEqual(File.ReadAllBytes(path)), "Failed disable persistence preserves the original on-disk configuration");
                using (var restored = new AssistantController(data, true, engine, delegate { return false; }))
                    Check(restored.Preferences.Enabled && !restored.Service.IsRunning, "Reported next-launch preference remains true on disk without starting a model in the fixture");
                controller.DisableModel();
                Check(!controller.Preferences.Enabled && controller.LastError == null, "Retrying disable after unlocking persists and clears the error");
                using (var restored = new AssistantController(data, true, engine, delegate { return false; }))
                    Check(!restored.Preferences.Enabled, "Successful retry keeps the model disabled after restart");
                Check(!controller.Service.IsRunning && !controller.Service.IsBusy, "Stop recovery leaves no real runner or model work");
            }
            finally { lease.Dispose(); }
        }
    }

    private static int Main(string[] args)
    {
        string fixtureParent = Path.GetFullPath(args.Length == 0 ? Path.GetTempPath() : args[0]).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        root = Path.GetFullPath(Path.Combine(fixtureParent, "FreeIsland.StoragePreferences." + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            Switching(); RejectionsAndRollback(); OldAndInvalidPreferences(); PreferenceWriteRecovery(); StopWhenSettingsAreLocked();
            Console.WriteLine("PASS " + checks + " assistant storage preference checks. Safe mode only; no model download/start, registry, network or system actions.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            if (root.StartsWith(fixtureParent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("FreeIsland.StoragePreferences.", StringComparison.Ordinal) && Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
