using System;
using System.IO;
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

    private static int Main()
    {
        root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FreeIsland.StoragePreferences." + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            Switching(); RejectionsAndRollback(); OldAndInvalidPreferences();
            Console.WriteLine("PASS " + checks + " assistant storage preference checks. Safe mode only; no model download/start, registry, network or system actions.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (root.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("FreeIsland.StoragePreferences.", StringComparison.Ordinal) && Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
