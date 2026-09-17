using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using FreeIsland.Installation;

// Fake registration/integration/launch adapters; only isolated fixture files mutate.
internal static class InstallerAutoUpdateTests
{
    private static int checks, installs, launches;
    private static string root, target;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string link, string existing, IntPtr security);
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); checks++; Console.WriteLine("PASS " + message); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (ArgumentException) { Check(true, message); return; }
        throw new InvalidOperationException("Expected rejection: " + message);
    }
    private static Dictionary<string, byte[]> Payload(string version)
    {
        var payload = new Dictionary<string, byte[]>();
        foreach (string file in Common.PayloadFiles) payload.Add(file, Encoding.UTF8.GetBytes(version + " " + file));
        payload.Add("FreeIsland.install", Encoding.UTF8.GetBytes(Common.MarkerContents)); return payload;
    }
    private static void Seed()
    {
        foreach (KeyValuePair<string, byte[]> file in Payload("old")) File.WriteAllBytes(Path.Combine(target, file.Key), file.Value);
        File.WriteAllText(Path.Combine(target, "unrelated.keep"), "user data");
        Common.TestRegisteredInstallPath = target;
        Common.BeforeReplaceForTest = null; installs = launches = 0;
        Setup.AutoStartupForTest = false; Setup.AutoDesktopForTest = true;
        Setup.AutoInstallForTest = delegate(bool startup, bool desktop)
        {
            installs++; Check(!startup && desktop, "Unattended install preserves both current choices");
            using (Common.HoldAppInstance()) Common.ReplacePayload(Payload("new"));
            return "";
        };
        Setup.AutoLaunchForTest = delegate(string file, string arguments)
        {
            launches++; Check(Common.SamePath(file, Path.Combine(target, "FreeIsland.exe")) && arguments == "--silent", "Restart targets verified installed app silently");
            bool created;
            using (Mutex mutex = new Mutex(false, Common.TestInstancePrefix + ".Instance", out created))
                Check(created, "App instance guard is released before restart");
        };
    }
    private static string[] Result()
    { return File.ReadAllLines(Path.Combine(Common.TestDataPath, "update-result.txt"), Encoding.UTF8); }
    private static void CheckStatus(string expected)
    {
        string[] lines = Result();
        Check(lines.Length >= 4 && lines[0] == expected && lines[1] == Setup.InstallerVersion, "Structured result status " + expected + " and version");
        DateTime utc; Check(DateTime.TryParse(lines[2], out utc), "Structured result includes parseable timestamp");
    }
    private static int Main()
    {
        root = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "installer-auto-update-" + Guid.NewGuid().ToString("N"));
        target = Path.Combine(root, "安装目录 with spaces"); Directory.CreateDirectory(target);
        Common.TestInstallPath = target; Common.TestRegisteredInstallPath = target; Common.TestRegistryIsolation = true;
        Common.TestDataPath = Path.Combine(root, "isolated-data"); Common.TestInstancePrefix = @"Local\FreeIsland.AutoUpdateTest." + Guid.NewGuid().ToString("N");
        Common.ReplacementTimeoutMilliseconds = 150;
        try
        {
            Check(SetupOptions.Parse(new[] { "--auto-update", target }).AutoUpdateDirectory == target, "Exact unattended argument accepted");
            Reject(delegate { SetupOptions.Parse(new[] { "--auto-update", target, "--startup", "true" }); }, "Cannot override startup preference during unattended update");
            Reject(delegate { SetupOptions.Parse(new[] { "--auto-update", target, "--install-dir", target }); }, "Cannot mix unattended and interactive install target");
            Reject(delegate { SetupOptions.Parse(new[] { "--auto-update", target, "--user-sid", Common.CurrentUserSid }); }, "Unattended update cannot request elevation handoff");
            Reject(delegate { SetupOptions.Parse(new[] { "--auto-update", "" }); }, "Empty unattended target rejected");
            Seed(); Check(Setup.RunAutoUpdate(target.Replace('\\', '/') + "/") == 0, "Normalized same registered target upgrades");
            Check(installs == 1 && launches == 1, "Successful update installs and restarts exactly once"); CheckStatus("success");
            Check(File.ReadAllText(Path.Combine(target, "FreeIsland.exe")) == "new FreeIsland.exe", "Verified payload replacement completed");
            Check(File.ReadAllText(Path.Combine(target, "unrelated.keep")) == "user data", "Unlisted user data preserved");
            Check(Directory.GetDirectories(target, ".upgrade-*").Length == 0, "No staging directory remains after successful update");

            Seed(); Common.TestRegisteredInstallPath = null;
            Check(Setup.RunAutoUpdate(target) == 2 && installs == 0 && launches == 0, "Portable directory cannot become unattended install"); CheckStatus("rejected");
            Seed(); Check(Setup.RunAutoUpdate(Path.Combine(root, "other")) == 2 && installs == 0, "Mismatched target rejected before filesystem replacement");
            Seed(); File.WriteAllText(Path.Combine(target, "FreeIsland.install"), "other product");
            Check(Setup.RunAutoUpdate(target) == 2 && installs == 0 && launches == 0, "Foreign marker rejects unattended update and launch");
            Seed(); File.Delete(Path.Combine(target, "FreeIsland.exe"));
            Check(Setup.RunAutoUpdate(target) == 2 && installs == 0, "Missing original executable requires manual repair");

            Seed(); string markerAlias = Path.Combine(root, "marker-alias");
            Check(CreateHardLink(markerAlias, Path.Combine(target, "FreeIsland.install"), IntPtr.Zero), "Created isolated marker hardlink fixture");
            Check(Setup.RunAutoUpdate(target) == 2 && installs == 0, "Hardlinked install marker rejected"); File.Delete(markerAlias);
            Seed(); string appAlias = Path.Combine(root, "app-alias");
            Check(CreateHardLink(appAlias, Path.Combine(target, "FreeIsland.exe"), IntPtr.Zero), "Created isolated executable hardlink fixture");
            Check(Setup.RunAutoUpdate(target) == 2 && installs == 0 && launches == 0, "Hardlinked executable rejected before launch"); File.Delete(appAlias);

            Seed(); Setup.AutoInstallForTest = delegate { installs++; throw new UnauthorizedAccessException("isolated permission denial"); };
            Check(Setup.RunAutoUpdate(target) == 1 && installs == 1 && launches == 1, "Permission rejection restores original running app without elevation"); CheckStatus("failed");
            Check(File.ReadAllText(Path.Combine(target, "FreeIsland.exe")) == "old FreeIsland.exe", "Permission failure leaves original bytes intact");

            Seed(); Common.BeforeReplaceForTest = delegate(string path) { if (path.EndsWith(".config")) throw new IOException("isolated failure after first payload replacement"); };
            Check(Setup.RunAutoUpdate(target) == 1 && launches == 1, "Transactional rollback restores launchable original app"); CheckStatus("failed");
            Check(File.ReadAllText(Path.Combine(target, "FreeIsland.exe")) == "old FreeIsland.exe", "Post-write failure restores original executable hash");

            Seed(); Setup.AutoInstallForTest = delegate { installs++; File.WriteAllText(Path.Combine(target, "FreeIsland.exe"), "partial replacement"); throw new IOException("unrecoverable fixture"); };
            Check(Setup.RunAutoUpdate(target) == 1 && launches == 0, "Incomplete recovery never launches unverified partial installation"); CheckStatus("failed");
            Check(String.Join("\n", Result()).Contains("手动运行安装包修复"), "Incomplete recovery directs user to manual repair");

            Seed(); Setup.AutoLaunchForTest = delegate { launches++; throw new IOException("isolated process launch failure"); };
            Check(Setup.RunAutoUpdate(target) == 3 && installs == 1 && launches == 1, "Successful install with restart failure remains distinguishable"); CheckStatus("restart-failed");
            Check(File.ReadAllText(Path.Combine(target, "FreeIsland.exe")) == "new FreeIsland.exe", "Restart failure does not overwrite successful update with old bytes");

            Seed(); using (Common.HoldSetupInstance())
                Check(Setup.RunAutoUpdate(target) == 2 && installs == 0 && launches == 0, "Concurrent installer prevents unattended update without racing launch");
            Seed(); Setup.AutoInstallForTest = delegate { Common.ReplacePayload(Payload("new")); return "fixture shortcut warning"; };
            Check(Setup.RunAutoUpdate(target) == 0 && launches == 1, "Optional integration warning still restarts updated app"); CheckStatus("success");
            Check(String.Join("\n", Result()).Contains("fixture shortcut warning"), "Optional integration warning retained in result");
            Check(Directory.GetFiles(Common.TestDataPath, "update-result-*.tmp").Length == 0, "Atomic result writes leave no temporary files");
            Console.WriteLine("PASS " + checks + " assertions. No real installation, registry, startup, elevation, process launch or shutdown performed.");
            Console.WriteLine("Fixtures: " + root); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
