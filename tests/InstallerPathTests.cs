using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using FreeIsland.Installation;
using Microsoft.Win32.SafeHandles;

internal static class InstallerPathTests
{
    private static int passed, failed;
    private static string fixtureRoot;

    private static void Check(bool result, string name)
    {
        if (!result) throw new Exception(name);
    }

    private static void Case(string name, Action action)
    {
        try { action(); ++passed; Console.WriteLine("PASS: " + name); }
        catch (Exception error) { ++failed; Console.Error.WriteLine("FAIL: " + name + " -- " + error); }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }
        catch (IOException) { return; }
        throw new Exception("Expected a rejected installation path.");
    }

    private static string Fixture(string relative)
    {
        string path = Path.GetFullPath(Path.Combine(fixtureRoot, relative));
        if (!path.StartsWith(fixtureRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new Exception("Test fixture escaped its isolated root.");
        return path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle file, uint code, byte[] input, int inputSize, IntPtr output, int outputSize, out int returned, IntPtr overlapped);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static void Junction(string link, string target)
    {
        // Both arguments come from Fixture(). No external directory is linked or changed.
        if (!link.StartsWith(fixtureRoot + "\\", StringComparison.OrdinalIgnoreCase) ||
            !target.StartsWith(fixtureRoot + "\\", StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe junction fixture.");
        Directory.CreateDirectory(link);
        byte[] substitute = Encoding.Unicode.GetBytes(@"\??\" + target);
        byte[] display = Encoding.Unicode.GetBytes(target);
        byte[] data = new byte[16 + substitute.Length + 2 + display.Length + 2];
        using (BinaryWriter writer = new BinaryWriter(new MemoryStream(data)))
        {
            writer.Write(0xA0000003u); writer.Write((ushort)(data.Length - 8)); writer.Write((ushort)0);
            writer.Write((ushort)0); writer.Write((ushort)substitute.Length);
            writer.Write((ushort)(substitute.Length + 2)); writer.Write((ushort)display.Length);
            writer.Write(substitute); writer.Write((ushort)0); writer.Write(display); writer.Write((ushort)0);
        }
        using (SafeFileHandle handle = CreateFile(link, 0x40000000, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero))
        {
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            int returned;
            if (!DeviceIoControl(handle, 0x000900A4, data, data.Length, IntPtr.Zero, 0, out returned, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static string[] ParseArguments(string value)
    {
        int count;
        IntPtr argv = CommandLineToArgvW("fixture.exe " + value, out count);
        if (argv == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            string[] result = new string[count - 1];
            for (int i = 1; i < count; ++i) result[i - 1] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size));
            return result;
        }
        finally { LocalFree(argv); }
    }

    private static int Main()
    {
        fixtureRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "installer-path-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(fixtureRoot);
        string chosen = Fixture("自选目录 with spaces\\浮岛");
        // Configure before any InstallPath read: this harness never reads or writes the
        // real uninstall/startup registry, and never touches a real installation.
        Common.ConfigureInstallPath(chosen);
        Case("Chinese and space-containing absolute path configures without creating files", delegate {
            Check(Common.InstallPath == chosen && !Directory.Exists(chosen), "Configuration should be non-mutating.");
            Common.ValidateInstallTarget();
        });
        Case("slash normalization and surrounding whitespace", delegate {
            Check(Common.NormalizeInstallDirectory("  " + chosen.Replace('\\', '/') + "/  ") == chosen, "Unexpected normalization.");
        });
        Case("empty custom directory is accepted", delegate {
            Directory.CreateDirectory(chosen); Common.ValidateInstallTarget();
        });
        Case("nonempty directory without installation marker is rejected and preserved", delegate {
            File.WriteAllText(Path.Combine(chosen, "unrelated.keep"), "preserved");
            Reject(delegate { Common.ValidateInstallTarget(); });
            Check(File.ReadAllText(Path.Combine(chosen, "unrelated.keep")) == "preserved", "User file changed.");
        });
        Case("invalid installation marker does not authorize overwrite", delegate {
            File.WriteAllText(Path.Combine(chosen, "FreeIsland.install"), "another product");
            Check(!Common.IsInstalled(), "Invalid marker accepted."); Reject(delegate { Common.ValidateInstallTarget(); });
        });
        Case("existing marked FreeIsland directory supports upgrade and keeps extra data", delegate {
            File.WriteAllText(Path.Combine(chosen, "FreeIsland.install"), Common.MarkerContents);
            Check(Common.IsInstalled(), "Valid marker rejected."); Common.ValidateInstallTarget();
            Check(File.Exists(Path.Combine(chosen, "unrelated.keep")), "User file removed.");
        });
        Case("validation rejects a directory other than the selected path", delegate {
            Reject(delegate { Common.ValidateInstallPath(Fixture("different")); });
        });
        Case("changing selection leaves the previous installation intact", delegate {
            Common.ConfigureInstallPath(Fixture("second installation")); Common.ValidateInstallTarget();
            Check(File.ReadAllText(Path.Combine(chosen, "FreeIsland.install")) == Common.MarkerContents, "Previous installation changed.");
        });
        Case("isolated test override retains precedence", delegate {
            try { Common.TestInstallPath = chosen; Check(Common.SamePath(Common.InstallPath, chosen), "Test override ignored."); }
            finally { Common.TestInstallPath = null; }
        });

        string[] rejected = { "", "   ", "relative\\FreeIsland", @"D:relative", @"\\server\share\FreeIsland", @"\\?\D:\FreeIsland",
            Path.GetPathRoot(fixtureRoot), fixtureRoot + @"\parent\..\escape", fixtureRoot + @"\parent\.\child", fixtureRoot + @"\trailing.\child",
            fixtureRoot + @"\trailing \child", fixtureRoot + @"\invalid*name", fixtureRoot + @"\stream:name", fixtureRoot + "\\quote\"name", fixtureRoot + @"\pipe|name",
            fixtureRoot + @"\question?name", fixtureRoot + @"\angle<name", fixtureRoot + @"\angle>name", fixtureRoot + "\\nul\0name",
            fixtureRoot + @"\NUL", fixtureRoot + @"\con.txt", fixtureRoot + @"\PRN", fixtureRoot + @"\AUX", fixtureRoot + @"\COM1", fixtureRoot + @"\lpt9.log",
            fixtureRoot + "\\" + new string('x', 211) };
        // Preserve syntactically invalid inputs verbatim; Path.GetFullPath would itself
        // reject or collapse these before Common could exercise its own boundary checks.
        for (int i = 0; i < rejected.Length; ++i)
        {
            string value = rejected[i];
            Case("reject invalid path #" + i, delegate { Reject(delegate { Common.NormalizeInstallDirectory(value); }); });
        }
        Case("same-name file rejects an installation directory", delegate {
            string file = Fixture("occupied.fixture"); File.WriteAllText(file, "unchanged");
            Reject(delegate { Common.ConfigureInstallPath(file); });
            Check(File.ReadAllText(file) == "unchanged", "File changed.");
        });
        Case("Windows directory and its descendants are rejected", delegate {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            Reject(delegate { Common.NormalizeInstallDirectory(windows); });
            Reject(delegate { Common.NormalizeInstallDirectory(Path.Combine(windows, "FreeIsland")); });
        });
        Case("system and user folder roots are rejected", delegate {
            foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.CommonApplicationData })
            {
                string value = Environment.GetFolderPath(folder);
                if (!String.IsNullOrEmpty(value)) Reject(delegate { Common.NormalizeInstallDirectory(value); });
            }
        });
        Case("real directory junction and junction ancestor are rejected", delegate {
            string target = Fixture("junction-target"), link = Fixture("junction-link");
            Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "target.keep"), "preserved");
            try
            {
                Junction(link, target);
                Check((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "Junction fixture was not created.");
                Reject(delegate { Common.ConfigureInstallPath(link); });
                Reject(delegate { Common.ConfigureInstallPath(Path.Combine(link, "child")); });
                Check(File.ReadAllText(Path.Combine(target, "target.keep")) == "preserved", "Junction target changed.");
            }
            finally
            {
                // Explicitly bounded, nonrecursive removal removes the junction itself.
                if (Directory.Exists(link)) Directory.Delete(link, false);
            }
        });
        Case("UAC arguments round-trip Chinese paths and both option choices", delegate {
            for (int bits = 0; bits < 4; ++bits)
            {
                bool startup = (bits & 1) != 0, desktop = (bits & 2) != 0;
                string[] args = ParseArguments(Common.ElevationArguments(chosen + "\\", startup, desktop));
                Check(args.Length == 8 && args[0] == "--install-dir" && args[1] == chosen && args[2] == "--startup" &&
                    args[3] == startup.ToString().ToLowerInvariant() && args[4] == "--desktop" && args[5] == desktop.ToString().ToLowerInvariant() &&
                    args[6] == "--user-sid" && args[7] == Common.CurrentUserSid, "Elevation arguments lost their values.");
            }
        });
        Case("invalid quoting cannot inject elevation arguments", delegate {
            Reject(delegate { Common.QuoteArgument("value\" --desktop false"); });
            Reject(delegate { Common.QuoteArgument("value\0suffix"); });
            Reject(delegate { Common.ElevationArguments(chosen + "\" --startup false", true, true); });
        });
        Case("same-account SID accepted and switched-account SID rejected", delegate {
            using (WindowsIdentity user = WindowsIdentity.GetCurrent())
                Check(Common.CurrentUserSid == user.User.Value, "Current SID does not match process token.");
            Common.ValidateElevationUser(Common.CurrentUserSid);
            string other = Common.CurrentUserSid == "S-1-5-18" ? "S-1-5-19" : "S-1-5-18";
            Reject(delegate { Common.ValidateElevationUser(other); });
            Reject(delegate { Common.ValidateElevationUser("invalid-sid"); });
            // Missing SID is supported for an ordinary non-elevated launch.
            Common.ValidateElevationUser(null);
        });
        Console.WriteLine("Results: " + passed + " passed; " + failed + " failed.");
        Console.WriteLine("Isolated artifacts: " + fixtureRoot);
        Console.WriteLine("No real install, startup/uninstall registry, UAC prompt, application process, or shutdown action was used.");
        return failed == 0 ? 0 : 1;
    }
}
