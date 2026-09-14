using System;
using FreeIsland.Installation;

// Compile with /main:UninstallOptionsTests. These tests only parse arguments and
// validate the current account identifier; they never enter Uninstall.Main,
// Remove, a confirmation window, UAC, process launch, or a registry writer.
internal static class UninstallOptionsTests
{
    private static int checks;

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        checks++;
        Console.WriteLine("PASS: " + description);
    }

    private static void Reject(string description, params string[] args)
    {
        bool rejected = false;
        try { UninstallOptions.Parse(args); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, description);
    }

    private static int Main()
    {
        try
        {
            UninstallOptions defaults = UninstallOptions.Parse(new string[0]);
            Check(defaults.InstallDirectory == null && defaults.UserSid == null &&
                defaults.ParentExe == null && defaults.ParentPid == 0 &&
                !defaults.Remove && !defaults.DeleteData,
                "default launch requests confirmation and preserves shared user data");

            const string directory = @"D:\Classroom Apps\浮岛";
            const string parentExecutable = @"D:\下载\Free Island 卸载恢复.exe";
            string userSid = Common.CurrentUserSid;
            UninstallOptions custom = UninstallOptions.Parse(new[] {
                "--install-dir", directory, "--user-sid", userSid
            });
            Check(custom.InstallDirectory == directory && custom.UserSid == userSid &&
                !custom.Remove && !custom.DeleteData,
                "custom directory preserves spaces and Chinese without skipping confirmation");

            UninstallOptions temporary = UninstallOptions.Parse(new[] {
                "--remove", "--install-dir", directory, "--parent-pid", "4321",
                "--parent-exe", parentExecutable, "--user-sid", userSid, "--delete-data"
            });
            Check(temporary.Remove && temporary.DeleteData && temporary.ParentPid == 4321 &&
                temporary.ParentExe == parentExecutable && temporary.InstallDirectory == directory &&
                temporary.UserSid == userSid,
                "temporary handoff retains explicit data choice, original directory, parent process and account");

            UninstallOptions preserve = UninstallOptions.Parse(new[] {
                "--parent-exe", parentExecutable, "--user-sid", userSid,
                "--install-dir", directory, "--remove", "--parent-pid", "4321"
            });
            Check(preserve.Remove && !preserve.DeleteData && preserve.ParentPid == 4321 &&
                preserve.InstallDirectory == directory,
                "temporary handoff preserves data by default regardless of option order");

            Reject("duplicate directory rejected", "--install-dir", directory, "--install-dir", directory);
            Reject("duplicate remove flag rejected", "--remove", "--remove");
            Reject("duplicate data deletion flag rejected", "--remove", "--delete-data", "--delete-data");
            Reject("missing directory value rejected", "--install-dir");
            Reject("missing account value rejected", "--user-sid");
            Reject("missing parent executable value rejected", "--remove", "--parent-exe");
            Reject("missing parent process value rejected", "--remove", "--parent-pid");
            Reject("empty account identifier rejected", "--user-sid", "");
            Reject("whitespace account identifier rejected", "--user-sid", " \t ");
            Reject("ordinary launch cannot preapprove data deletion", "--delete-data");
            Reject("ordinary custom launch cannot preapprove data deletion", "--install-dir", directory, "--delete-data");
            Reject("ordinary launch rejects parent process handoff", "--parent-pid", "4321");
            Reject("ordinary launch rejects parent executable handoff", "--parent-exe", parentExecutable);
            Reject("unknown argument rejected", "--force", "true");
            Reject("nonnumeric parent process rejected", "--remove", "--parent-pid", "abc");
            Reject("zero parent process rejected", "--remove", "--parent-pid", "0");
            Reject("negative parent process rejected", "--remove", "--parent-pid", "-1");
            Reject("overflowing parent process rejected", "--remove", "--parent-pid", "2147483648");

            Common.ValidateElevationUser(userSid);
            Check(true, "same-account elevation handoff passes account validation");
            bool otherAccountRejected = false;
            try { Common.ValidateElevationUser("S-1-0-0"); }
            catch (InvalidOperationException) { otherAccountRejected = true; }
            Check(otherAccountRejected, "a different account cannot reuse the original elevation handoff");

            Console.WriteLine("Passed " + checks + " uninstall argument checks. No uninstall, UAC, process launch, registry write or user-data change was performed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
