using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace FreeIsland.Installation
{
    internal static class Common
    {
        internal const string Product = "浮岛 · Free Island";
        internal const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FreeIsland";
        internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string MarkerContents = "FreeIsland per-user installation v1";
        internal static readonly string[] PayloadFiles = { "FreeIsland.exe", "FreeIsland.exe.config", "FreeIsland.ico", "FreeIsland.Uninstall.exe" };
        internal static string InstallPath { get {
#if FI_INSTALLER_TESTING
            if (TestInstallPath != null) return TestInstallPath;
#endif
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FreeIsland"); } }
#if FI_INSTALLER_TESTING
        internal static string TestInstallPath;
        internal static string TestInstancePrefix;
        internal static int ReplacementTimeoutMilliseconds = 8000;
        internal static Action<string> BeforeReplaceForTest;
#else
        private const int ReplacementTimeoutMilliseconds = 8000;
#endif
        private static string InstancePrefix { get {
#if FI_INSTALLER_TESTING
            if (TestInstancePrefix != null) return TestInstancePrefix;
#endif
            return @"Local\FreeIsland"; } }
        internal static string DataPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FreeIsland"); } }
        internal static string ShortcutName { get { return "浮岛 Free Island.lnk"; } }

        internal static bool SamePath(string first, string second)
        {
            try { return String.Equals(Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        internal static void ValidateInstallPath(string path)
        {
            if (!SamePath(path, InstallPath)) throw new InvalidOperationException("安装目录验证失败，未改动任何文件。");
            string current = Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(current))
            {
                if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("安装路径包含目录链接，无法安全地继续。");
                DirectoryInfo parent = Directory.GetParent(current);
                current = parent == null ? null : parent.FullName;
            }
        }

        internal static void StopInstalledApp()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                // Repeat the request if a just-starting app had not created its exit event yet.
                try { using (EventWaitHandle signal = EventWaitHandle.OpenExisting(InstancePrefix + ".Exit")) signal.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
                bool found = false;
                foreach (Process process in Process.GetProcessesByName("FreeIsland"))
                {
                    using (process)
                    {
                        try { if (!process.HasExited && SamePath(process.MainModule.FileName, Path.Combine(InstallPath, "FreeIsland.exe"))) found = true; }
                        catch (System.ComponentModel.Win32Exception) { }
                        catch (InvalidOperationException) { }
                    }
                }
                if (!found) return;
                Thread.Sleep(150);
            }
            throw new IOException("浮岛仍在运行。请从系统托盘退出浮岛，再重新运行安装程序。");
        }

        internal static IDisposable HoldAppInstance()
        {
            // The app checks whether this named object already exists. Keeping a handle
            // prevents login/shortcut launches from racing with file replacement.
            return new Mutex(false, InstancePrefix + ".Instance");
        }

        internal static IDisposable HoldSetupInstance()
        {
            bool created;
            Mutex guard = new Mutex(false, InstancePrefix + ".Setup", out created);
            if (created) return guard;
            guard.Dispose();
            throw new IOException("另一个浮岛安装或卸载任务正在运行。请先关闭它，再重试。");
        }

        internal static bool IsInstalled()
        {
            try {
                string marker = Path.Combine(InstallPath, "FreeIsland.install");
                return File.Exists(marker) && (File.GetAttributes(marker) & FileAttributes.ReparsePoint) == 0 && File.ReadAllText(marker).Trim() == MarkerContents;
            } catch { return false; }
        }

        internal static bool IsStartupEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && StartupTargetsInstall(key.GetValue("FreeIsland") as string);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileInformation
        {
            internal uint Attributes;
            internal System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
            internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

        private static void ValidatePayloadFile(string path)
        {
            if (Directory.Exists(path)) throw new IOException("安装目标被同名文件夹占用：" + Path.GetFileName(path));
            if (!File.Exists(path)) return;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("安装目标包含文件链接：" + Path.GetFileName(path));
            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                FileInformation information;
                if (!GetFileInformationByHandle(file.SafeFileHandle, out information) || information.Links != 1)
                    throw new IOException("安装目标不是可安全替换的普通文件：" + Path.GetFileName(path));
            }
        }

        private static void RetryFileOperation(Action operation)
        {
            DateTime until = DateTime.UtcNow.AddMilliseconds(ReplacementTimeoutMilliseconds);
            while (true)
            {
                try { operation(); return; }
                catch (IOException) { if (DateTime.UtcNow >= until) throw; }
                catch (UnauthorizedAccessException) { if (DateTime.UtcNow >= until) throw; }
                Thread.Sleep(100);
            }
        }

        private sealed class Replacement
        {
            internal string Destination, Staged, Backup;
            internal bool Existed, Committed;
            internal FileAttributes Attributes;
        }

        // No settings, shortcuts, or registry writes. Also used by the isolated regression harness.
        internal static void ReplacePayload(Dictionary<string, byte[]> payload)
        {
            ValidateInstallPath(InstallPath);
            Directory.CreateDirectory(InstallPath);
            string transaction = Path.Combine(InstallPath, ".upgrade-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(transaction);
            List<Replacement> files = new List<Replacement>();
            bool keepRecovery = false;
            try
            {
                // Stage every new file before touching any installed file.
                foreach (KeyValuePair<string, byte[]> file in payload)
                {
                    if (String.IsNullOrEmpty(file.Key) || Path.GetFileName(file.Key) != file.Key || file.Key == "." || file.Key == "..")
                        throw new IOException("安装包文件名无效。");
                    Replacement entry = new Replacement { Destination = Path.Combine(InstallPath, file.Key), Staged = Path.Combine(transaction, "new-" + file.Key), Backup = Path.Combine(transaction, "old-" + file.Key) };
                    RetryFileOperation(delegate { ValidatePayloadFile(entry.Destination); });
                    entry.Existed = File.Exists(entry.Destination);
                    if (entry.Existed) entry.Attributes = File.GetAttributes(entry.Destination);
                    files.Add(entry);
                    using (FileStream output = new FileStream(entry.Staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { output.Write(file.Value, 0, file.Value.Length); output.Flush(true); }
                }
                foreach (Replacement entry in files)
                {
#if FI_INSTALLER_TESTING
                    if (BeforeReplaceForTest != null) BeforeReplaceForTest(entry.Destination);
#endif
                    RetryFileOperation(delegate {
                        ValidatePayloadFile(entry.Destination);
                        if (entry.Existed)
                        {
                            File.SetAttributes(entry.Destination, entry.Attributes & ~FileAttributes.ReadOnly);
                            File.Replace(entry.Staged, entry.Destination, entry.Backup, true);
                        }
                        else File.Move(entry.Staged, entry.Destination);
                    });
                    entry.Committed = true;
                }
            }
            catch (Exception original)
            {
                // Restore already-replaced files in reverse order. Keep backups if recovery fails.
                for (int i = files.Count - 1; i >= 0; --i)
                {
                    Replacement entry = files[i];
                    try {
                        if (entry.Committed)
                        {
                            RetryFileOperation(delegate {
                                if (entry.Existed) File.Replace(entry.Backup, entry.Destination, null, true);
                                else if (File.Exists(entry.Destination)) File.Delete(entry.Destination);
                            });
                        }
                        if (entry.Existed && File.Exists(entry.Destination)) File.SetAttributes(entry.Destination, entry.Attributes);
                    } catch (Exception recovery) { keepRecovery = true; WriteLog("Upgrade rollback: " + recovery); }
                }
                throw new IOException("升级未完成。请从托盘退出浮岛，关闭正在打开安装目录文件的窗口后重试。" +
                    (keepRecovery ? "\n原文件备份保留在：" + transaction : "\n已保留原有安装文件和用户数据。") + "\n" + original.Message, original);
            }
            finally
            {
                if (!keepRecovery)
                {
                    foreach (Replacement entry in files)
                    {
                        foreach (string path in new[] { entry.Staged, entry.Backup })
                            try { if (File.Exists(path)) { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); } } catch (Exception cleanup) { WriteLog("Upgrade cleanup: " + cleanup); }
                    }
                    try { Directory.Delete(transaction, false); } catch (Exception cleanup) { WriteLog("Upgrade cleanup: " + cleanup); }
                }
            }
        }

        internal static void SetStartup(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue("FreeIsland", "\"" + Path.Combine(InstallPath, "FreeIsland.exe") + "\" --silent");
                else if (StartupTargetsInstall(key.GetValue("FreeIsland") as string)) key.DeleteValue("FreeIsland", false);
            }
        }

        internal static bool StartupTargetsInstall(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return false;
            string executable;
            value = value.Trim();
            if (value.StartsWith("\""))
            {
                int end = value.IndexOf('"', 1);
                executable = end > 1 ? value.Substring(1, end - 1) : "";
            }
            else executable = value.Split(new[] { ' ' }, 2)[0];
            return SamePath(executable, Path.Combine(InstallPath, "FreeIsland.exe"));
        }

        internal static void CreateShortcut(string folder)
        {
            Directory.CreateDirectory(folder);
            object shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { Path.Combine(folder, ShortcutName) });
                SetComProperty(shortcut, "TargetPath", Path.Combine(InstallPath, "FreeIsland.exe"));
                SetComProperty(shortcut, "WorkingDirectory", InstallPath);
                SetComProperty(shortcut, "Description", "浮岛 · 让时间与提醒，轻轻浮现");
                SetComProperty(shortcut, "IconLocation", Path.Combine(InstallPath, "FreeIsland.ico"));
                shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void SetComProperty(object target, string property, object value)
        {
            target.GetType().InvokeMember(property, BindingFlags.SetProperty, null, target, new object[] { value });
        }

        internal static void RemoveOwnedShortcut(string folder)
        {
            string path = Path.Combine(folder, ShortcutName);
            if (HasOwnedShortcut(folder)) File.Delete(path);
        }

        internal static bool HasOwnedShortcut(string folder)
        {
            string path = Path.Combine(folder, ShortcutName);
            if (!File.Exists(path)) return false;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
            object shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                string target = Convert.ToString(shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null));
                return SamePath(target, Path.Combine(InstallPath, "FreeIsland.exe"));
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        internal static void WriteLog(string message)
        {
            try
            {
                string directory = Path.Combine(Path.GetTempPath(), "FreeIsland-Logs");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "installation.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }
}
