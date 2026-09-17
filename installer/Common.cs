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
using System.Security.Principal;

namespace FreeIsland.Installation
{
    internal static class Common
    {
        internal const string Product = "浮岛 · Free Island";
        internal const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FreeIsland";
        internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string MarkerContents = "FreeIsland per-user installation v1";
        internal static readonly string[] PayloadFiles = { "FreeIsland.exe", "FreeIsland.exe.config", "FreeIsland.ico", "FreeIsland.Uninstall.exe" };
        private static string selectedInstallPath;
        internal static string DefaultInstallPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FreeIsland"); } }
        internal static string RegisteredInstallPath
        {
            get {
#if FI_INSTALLER_TESTING
                if (TestRegistryIsolation) return TestRegisteredInstallPath;
#endif
                try {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallKey))
                    {
                        string path = key == null ? null : key.GetValue("InstallLocation") as string;
                        return String.IsNullOrWhiteSpace(path) ? null : NormalizeInstallDirectory(path);
                    }
                } catch { return null; }
            }
        }
        internal static string InstallPath { get {
#if FI_INSTALLER_TESTING
            if (TestInstallPath != null) return TestInstallPath;
#endif
            return selectedInstallPath ?? RegisteredInstallPath ?? DefaultInstallPath; } }
#if FI_INSTALLER_TESTING
        internal static string TestInstallPath;
        internal static string TestInstancePrefix;
        internal static int ReplacementTimeoutMilliseconds = 8000;
        internal static Action<string> BeforeReplaceForTest;
        internal static bool TestRegistryIsolation;
        internal static string TestRegisteredInstallPath;
        internal static string TestDataPath;
#else
        private const int ReplacementTimeoutMilliseconds = 8000;
#endif
        private static string InstancePrefix { get {
#if FI_INSTALLER_TESTING
            if (TestInstancePrefix != null) return TestInstancePrefix;
#endif
            return @"Local\FreeIsland"; } }
        internal static string DataPath { get {
#if FI_INSTALLER_TESTING
            if (TestDataPath != null) return TestDataPath;
#endif
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FreeIsland"); } }
        internal static string ShortcutName { get { return "浮岛 Free Island.lnk"; } }
        internal static string CurrentUserSid { get { using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) return identity.User.Value; } }
        internal static bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        internal static void ValidateElevationUser(string sid)
        {
            if (!String.IsNullOrEmpty(sid) && !String.Equals(sid, CurrentUserSid, StringComparison.Ordinal))
                throw new InvalidOperationException("提权后切换到了另一个 Windows 账户。请使用原账户重试，避免改动其他账户的安装记录。");
        }
        internal static string QuoteArgument(string value)
        {
            if (String.IsNullOrEmpty(value) || value.IndexOf('"') >= 0 || value.IndexOf('\0') >= 0)
                throw new ArgumentException("启动参数无效。");
            return "\"" + value.TrimEnd('\\') + "\"";
        }
        internal static string ElevationArguments(string path, bool startup, bool desktop)
        {
            return "--install-dir " + QuoteArgument(NormalizeInstallDirectory(path)) + " --startup " + startup.ToString().ToLowerInvariant() +
                " --desktop " + desktop.ToString().ToLowerInvariant() + " --user-sid " + CurrentUserSid;
        }
        internal static string NormalizeInstallDirectory(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new ArgumentException("请选择浮岛安装目录。");
            string path = value.Trim();
            if (path.Length < 3 || !Char.IsLetter(path[0]) || path[1] != ':' || (path[2] != '\\' && path[2] != '/'))
                throw new ArgumentException("请输入本地磁盘的完整路径，例如 D:\\Apps\\FreeIsland。");
            if (path.IndexOfAny(new[] { '"', '<', '>', '|', '*', '?', '\0' }) >= 0 || path.Substring(2).IndexOf(':') >= 0)
                throw new ArgumentException("安装目录包含无效字符。");
            foreach (string part in path.Substring(3).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ")) throw new ArgumentException("安装目录不能包含点路径或末尾空格。");
                string device = part.Split('.')[0].ToUpperInvariant();
                if (device == "CON" || device == "PRN" || device == "AUX" || device == "NUL" ||
                    (device.Length == 4 && (device.StartsWith("COM") || device.StartsWith("LPT")) && device[3] >= '0' && device[3] <= '9'))
                    throw new ArgumentException("安装目录不能使用 Windows 保留设备名。");
            }
            path = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (path.Length > 210 || SamePath(path, Path.GetPathRoot(path))) throw new ArgumentException("请选择磁盘中的独立文件夹，且路径不要超过 210 个字符。");
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
            if (SamePath(path, windows) || path.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("不能将浮岛安装到 Windows 系统目录。");
            foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.CommonApplicationData })
            {
                string protectedRoot = Environment.GetFolderPath(folder);
                if (!String.IsNullOrEmpty(protectedRoot) && SamePath(path, protectedRoot)) throw new ArgumentException("请选择独立子文件夹，不要直接使用系统或个人资料文件夹。");
            }
            if (File.Exists(path)) throw new ArgumentException("安装位置已被同名文件占用。");
            return path;
        }
        internal static void ConfigureInstallPath(string path)
        {
            string normalized = NormalizeInstallDirectory(path);
            CheckDirectoryAncestors(normalized);
            selectedInstallPath = normalized;
        }

        internal static bool SamePath(string first, string second)
        {
            try { return String.Equals(Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        internal static void ValidateInstallPath(string path)
        {
            if (!SamePath(path, InstallPath)) throw new InvalidOperationException("安装目录验证失败，未改动任何文件。");
            CheckDirectoryAncestors(NormalizeInstallDirectory(path));
        }
        private static void CheckDirectoryAncestors(string path)
        {
            string current = path;
            while (!String.IsNullOrEmpty(current))
            {
                if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("安装路径包含目录链接，无法安全地继续。");
                DirectoryInfo parent = Directory.GetParent(current);
                current = parent == null ? null : parent.FullName;
            }
        }
        internal static void ValidateInstallTarget()
        {
            ValidateInstallPath(InstallPath);
            if (!Directory.Exists(InstallPath) || IsInstalled()) return;
            if (Directory.GetFileSystemEntries(InstallPath).Length != 0)
                throw new IOException("这个目录已有其他文件，且没有有效的浮岛安装记录。请选择空文件夹或原浮岛安装目录。");
        }

        internal static void StopInstalledApp()
        {
            string registered = RegisteredInstallPath;
#if FI_INSTALLER_TESTING
            if (TestInstallPath != null) registered = null;
#endif
            string previousExecutable = IsOwnedInstallation(registered) ? Path.Combine(registered, "FreeIsland.exe") : null;
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
                        try {
                            if (!process.HasExited) {
                                string executable = process.MainModule.FileName;
                                if (SamePath(executable, Path.Combine(InstallPath, "FreeIsland.exe")) || SamePath(executable, previousExecutable)) found = true;
                            }
                        }
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
            return IsOwnedInstallation(InstallPath);
        }

        internal static void ValidateAutoUpdateTarget(string directory)
        {
            string target = NormalizeInstallDirectory(directory);
            string registered = RegisteredInstallPath;
            if (String.IsNullOrEmpty(registered) || !SamePath(target, registered))
                throw new InvalidOperationException("自动更新只适用于当前账户已登记的浮岛安装目录；便携版请手动更新。");
            if (!IsOwnedInstallation(target)) throw new InvalidOperationException("原安装记录缺失或无效，自动更新已取消。");
            foreach (string name in PayloadFiles) ValidatePayloadFile(Path.Combine(target, name));
            ValidatePayloadFile(Path.Combine(target, "FreeIsland.install"));
            if (!File.Exists(Path.Combine(target, "FreeIsland.exe"))) throw new IOException("原安装缺少主程序，请手动运行安装包修复。");
            ConfigureInstallPath(target);
        }

        internal static void WriteUpdateResult(string status, string version, string detail)
        {
            string directory = DataPath;
            CheckDirectoryAncestors(directory);
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, "update-result.txt");
            ValidatePayloadFile(destination);
            string temporary = Path.Combine(directory, "update-result-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.WriteLine(status); writer.WriteLine(version);
                    writer.WriteLine(DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
                    writer.Write(detail ?? ""); writer.Flush(); stream.Flush(true);
                }
                CheckDirectoryAncestors(directory); ValidatePayloadFile(destination);
                if (File.Exists(destination)) File.Replace(temporary, destination, null, true);
                else File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static bool IsOwnedInstallation(string directory)
        {
            try {
                CheckDirectoryAncestors(NormalizeInstallDirectory(directory));
                string marker = Path.Combine(directory, "FreeIsland.install");
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
            internal bool Existed, Committed, AttributesChanged;
            internal FileAttributes Attributes;
        }

        internal static string FormatFileFailure(string action, string path, Exception error)
        {
            Exception reason = error;
            while (reason.InnerException != null) reason = reason.InnerException;
            System.ComponentModel.Win32Exception native = reason as System.ComponentModel.Win32Exception;
            int code = native == null ? reason.HResult & 0xffff : native.NativeErrorCode;
            string detail;
            if (reason is UnauthorizedAccessException || code == 5)
                detail = "Windows 拒绝访问（错误 5）。请检查该文件的权限或系统访问限制。";
            else if (code == 32 || code == 33)
                detail = "文件被其他程序占用（错误 " + code + "）。请关闭占用它的程序后重试。";
            else detail = reason.Message;
            return action + (String.IsNullOrEmpty(path) ? "" : "：" + path) + "\n" + detail;
        }

        internal static string AccessFailureMessage(Exception error)
        {
            return FormatFileFailure("文件操作未完成", null, error);
        }

        internal static void DeletePayloadFile(string path)
        {
            ValidateInstallPath(InstallPath);
            string root = Path.GetFullPath(InstallPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new IOException("删除目标不在所选安装目录中。");
            FileAttributes attributes = FileAttributes.Normal;
            bool changed = false;
            try
            {
                RetryFileOperation(delegate {
                    ValidatePayloadFile(path);
                    if (!File.Exists(path)) return;
                    if (!changed)
                    {
                        attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReadOnly) != 0)
                        {
                            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                            changed = true;
                        }
                    }
                    File.Delete(path);
                });
            }
            catch (Exception error)
            {
                string restoration = "";
                if (changed && File.Exists(path))
                {
                    try { File.SetAttributes(path, attributes); }
                    catch (Exception restore) { restoration = "\n只读属性未能恢复：" + restore.Message; WriteLog("Delete attribute restore: " + restore); }
                }
                throw new IOException(FormatFileFailure("无法删除安装文件", path, error) + restoration, error);
            }
        }

        // No settings, shortcuts, or registry writes. Also used by the isolated regression harness.
        internal static void ReplacePayload(Dictionary<string, byte[]> payload)
        {
            ValidateInstallPath(InstallPath);
            Directory.CreateDirectory(InstallPath);
            string transaction = Path.Combine(InstallPath, ".upgrade-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(transaction);
            List<Replacement> files = new List<Replacement>();
            bool keepRecovery = false, filesTouched = false;
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
                            if (!entry.AttributesChanged && (entry.Attributes & FileAttributes.ReadOnly) != 0)
                            {
                                File.SetAttributes(entry.Destination, entry.Attributes & ~FileAttributes.ReadOnly);
                                entry.AttributesChanged = true; filesTouched = true;
                            }
                            File.Replace(entry.Staged, entry.Destination, entry.Backup, true);
                        }
                        else File.Move(entry.Staged, entry.Destination);
                    });
                    entry.Committed = true; filesTouched = true;
                }
            }
            catch (Exception original)
            {
                // Restore only files actually replaced or whose attributes we changed.
                // A pre-write permission failure must not manufacture a rollback failure.
                List<string> recoveryProblems = new List<string>();
                for (int i = files.Count - 1; i >= 0; --i)
                {
                    Replacement entry = files[i];
                    try {
                        if (entry.Committed || File.Exists(entry.Backup))
                        {
                            RetryFileOperation(delegate {
                                if (entry.Existed)
                                {
                                    if (File.Exists(entry.Destination)) File.Replace(entry.Backup, entry.Destination, null, true);
                                    else File.Move(entry.Backup, entry.Destination);
                                }
                                else if (File.Exists(entry.Destination)) DeletePayloadFile(entry.Destination);
                            });
                            entry.Committed = false;
                        }
                        if (entry.AttributesChanged && File.Exists(entry.Destination))
                        {
                            File.SetAttributes(entry.Destination, entry.Attributes);
                            entry.AttributesChanged = false;
                        }
                    } catch (Exception recovery) { recoveryProblems.Add(recovery.Message); WriteLog("Upgrade rollback: " + recovery); }
                    if (File.Exists(entry.Backup)) keepRecovery = true;
                }
                string state = keepRecovery ? "\n原文件备份保留在：" + transaction :
                    recoveryProblems.Count != 0 ? "\n用户数据未改动。" :
                    !filesTouched ? "\n尚未替换任何安装文件；用户数据未改动。" : "\n原有安装文件已恢复；用户数据未改动。";
                if (recoveryProblems.Count != 0) state += "\n部分恢复操作未完成：" + String.Join("；", recoveryProblems.ToArray());
                throw new IOException(FormatFileFailure("升级未完成", null, original) + state, original);
            }
            finally
            {
                foreach (Replacement entry in files)
                {
                    // New staging files are never recovery data, even if an old backup
                    // must be retained. Normal files need DELETE, not WRITE_ATTRIBUTES.
                    try { DeletePayloadFile(entry.Staged); } catch (Exception cleanup) { WriteLog("Upgrade cleanup: " + cleanup); }
                    if (!keepRecovery)
                        try { DeletePayloadFile(entry.Backup); } catch (Exception cleanup) { WriteLog("Upgrade cleanup: " + cleanup); }
                }
                try { Directory.Delete(transaction, false); } catch (Exception cleanup) { WriteLog("Upgrade cleanup: " + cleanup); }
            }
        }

        internal static void SetStartup(bool enabled)
        {
            SetStartup(enabled, false);
        }

        internal static void SetStartup(bool enabled, bool migrateRegistered)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                string existing = key.GetValue("FreeIsland") as string;
                string previous = migrateRegistered ? RegisteredInstallPath : null;
                bool owned = StartupTargetsInstall(existing) || (IsOwnedInstallation(previous) && StartupTargetsPath(existing, previous));
                if (enabled && !String.IsNullOrWhiteSpace(existing) && !owned)
                    throw new IOException("同名开机启动项指向其他程序，已保留原项。");
                if (enabled) key.SetValue("FreeIsland", "\"" + Path.Combine(InstallPath, "FreeIsland.exe") + "\" --silent");
                else if (owned) key.DeleteValue("FreeIsland", false);
            }
        }

        internal static bool StartupTargetsInstall(string value)
        {
            return StartupTargetsPath(value, InstallPath);
        }

        internal static bool StartupTargetsPath(string value, string directory)
        {
            if (String.IsNullOrWhiteSpace(value) || String.IsNullOrWhiteSpace(directory)) return false;
            string executable;
            value = value.Trim();
            if (value.StartsWith("\""))
            {
                int end = value.IndexOf('"', 1);
                executable = end > 1 ? value.Substring(1, end - 1) : "";
            }
            else executable = value.Split(new[] { ' ' }, 2)[0];
            return SamePath(executable, Path.Combine(directory, "FreeIsland.exe"));
        }

        internal static void CreateShortcut(string folder)
        {
            Directory.CreateDirectory(folder);
            string previous = RegisteredInstallPath;
            if (File.Exists(Path.Combine(folder, ShortcutName)) && !HasOwnedShortcut(folder) &&
                !(IsOwnedInstallation(previous) && ShortcutTargetsPath(folder, previous)))
                throw new IOException("同名快捷方式指向其他位置，已保留原快捷方式。");
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
            RemoveOwnedShortcut(folder, false);
        }

        internal static void RemoveOwnedShortcut(string folder, bool migrateRegistered)
        {
            string path = Path.Combine(folder, ShortcutName);
            string previous = migrateRegistered ? RegisteredInstallPath : null;
            if (HasOwnedShortcut(folder) || (IsOwnedInstallation(previous) && ShortcutTargetsPath(folder, previous))) File.Delete(path);
        }

        internal static bool HasOwnedShortcut(string folder)
        {
            return ShortcutTargetsPath(folder, InstallPath);
        }

        private static bool ShortcutTargetsPath(string folder, string directory)
        {
            if (String.IsNullOrWhiteSpace(directory)) return false;
            string path = Path.Combine(folder, ShortcutName);
            if (!File.Exists(path)) return false;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
            object shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                shortcut = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                string target = Convert.ToString(shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null));
                return SamePath(target, Path.Combine(directory, "FreeIsland.exe"));
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
#if FI_INSTALLER_TESTING
                if (TestDataPath != null) directory = Path.Combine(TestDataPath, "test-logs");
#endif
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "installation.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }
}
