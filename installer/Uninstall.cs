using System;
using System.Diagnostics;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FreeIsland.Installation
{
    internal static class Uninstall
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                UninstallOptions options = UninstallOptions.Parse(args);
                Common.ValidateElevationUser(options.UserSid);
                string ownDirectory = Path.GetDirectoryName(Application.ExecutablePath);
                Common.ConfigureInstallPath(options.InstallDirectory ??
                    (Common.IsOwnedInstallation(ownDirectory) ? ownDirectory : Common.InstallPath));
                Common.ValidateInstallPath(Common.InstallPath);
                string marker = Path.Combine(Common.InstallPath, "FreeIsland.install");
                if (File.Exists(marker) && (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("安装记录包含文件链接，未删除任何文件。");
                if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != Common.MarkerContents)
                    throw new InvalidOperationException("未找到有效的浮岛安装记录。没有删除任何文件。");
                if (options.Remove)
                {
                    if (!Common.SamePath(Path.GetDirectoryName(Application.ExecutablePath), Path.GetTempPath()) || !Path.GetFileName(Application.ExecutablePath).StartsWith("FreeIsland-Uninstall-", StringComparison.Ordinal))
                        throw new InvalidOperationException("卸载进程位置校验失败。");
                    if (options.InstallDirectory == null || options.ParentPid <= 0 || options.ParentExe == null)
                        throw new ArgumentException("临时卸载进程缺少原安装目录或父进程信息。");
                    WaitForParent(options);
                    Remove(options.DeleteData);
                    MessageBox.Show("浮岛已卸载。" + (options.DeleteData ? "\n本地设置和日程已删除。" : "\n本地设置和日程已保留，重新安装后可以继续使用。"), "浮岛 · 再会", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                // A fresh standalone uninstaller can repair an older installation's
                // uninstall flow without first overwriting a blocked program file.
                using (UninstallForm form = new UninstallForm())
                {
                    if (form.ShowDialog() != DialogResult.OK) return 0;
                    string temporary = Path.Combine(Path.GetTempPath(), "FreeIsland-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                    File.Copy(Application.ExecutablePath, temporary, false);
                    string arguments = "--remove --install-dir " + Common.QuoteArgument(Common.InstallPath) +
                        " --parent-pid " + Process.GetCurrentProcess().Id + " --parent-exe " + Common.QuoteArgument(Application.ExecutablePath) +
                        " --user-sid " + Common.CurrentUserSid + (form.DeleteData ? " --delete-data" : "");
                    Process.Start(new ProcessStartInfo(temporary, arguments) { UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
                }
                return 0;
            }
            catch (Exception error)
            {
                Common.WriteLog("Uninstall: " + error);
                bool denied = false;
                for (Exception current = error; current != null; current = current.InnerException)
                    if (current is UnauthorizedAccessException || (current.HResult & 0xffff) == 5) denied = true;
                string message = error.Message + "\n\n日志：" + Path.Combine(Path.GetTempPath(), "FreeIsland-Logs", "installation.log");
                if (denied && !Common.IsAdministrator() && Common.IsOwnedInstallation(Common.InstallPath))
                {
                    if (MessageBox.Show(message + "\n\n要以管理员身份重新打开卸载确认窗口吗？", "浮岛卸载未完成", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                    {
                        try {
                            Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--install-dir " + Common.QuoteArgument(Common.InstallPath) + " --user-sid " + Common.CurrentUserSid)
                                { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath) });
                        } catch (Win32Exception elevation) {
                            if (elevation.NativeErrorCode != 1223) MessageBox.Show(elevation.Message, "无法打开管理员卸载窗口", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
                else MessageBox.Show(message, "浮岛卸载未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static void WaitForParent(UninstallOptions options)
        {
            Process parent;
            try { parent = Process.GetProcessById(options.ParentPid); }
            catch (ArgumentException) { return; }
            using (parent)
            {
                if (parent.HasExited) return;
                string parentPath;
                try { parentPath = parent.MainModule.FileName; }
                catch (InvalidOperationException) { return; }
                if (!Common.SamePath(parentPath, options.ParentExe))
                    throw new InvalidOperationException("原卸载进程校验失败，未删除任何文件。");
                if (!parent.WaitForExit(8000)) throw new IOException("原卸载窗口尚未退出，请关闭后重试。");
            }
        }

        private static void Remove(bool deleteData)
        {
            using (Common.HoldSetupInstance())
            using (Common.HoldAppInstance())
            {
            Common.StopInstalledApp();
            Common.ValidateInstallPath(Common.InstallPath);
            if (deleteData && Directory.Exists(Common.DataPath)) ValidateDataTree(Common.DataPath, Common.DataPath);
            foreach (string file in Common.PayloadFiles)
            {
                string path = Path.Combine(Common.InstallPath, file);
                if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("安装目录包含文件链接，卸载已停止：" + file);
            }
            // Allow the original, installed uninstaller process to finish before removing it.
            foreach (string file in Common.PayloadFiles)
            {
                string path = Path.Combine(Common.InstallPath, file);
                Common.DeletePayloadFile(path);
            }
            Common.RemoveOwnedShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            Common.RemoveOwnedShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
            Common.SetStartup(false);
            // Removing an older retained directory must not unregister a newer one.
            if (Common.SamePath(Common.RegisteredInstallPath, Common.InstallPath))
                Registry.CurrentUser.DeleteSubKeyTree(Common.UninstallKey, false);
            string marker = Path.Combine(Common.InstallPath, "FreeIsland.install");
            Common.DeletePayloadFile(marker);
            if (Directory.GetFileSystemEntries(Common.InstallPath).Length == 0) Directory.Delete(Common.InstallPath, false);
            if (deleteData && Directory.Exists(Common.DataPath)) RemoveDataTree(Common.DataPath, Common.DataPath);
            Common.WriteLog("Uninstalled; removed user data = " + deleteData);
            }
        }

        private static void ValidateDataTree(string path, string expectedRoot)
        {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            string root = Path.GetFullPath(expectedRoot).TrimEnd(Path.DirectorySeparatorChar);
            if (!Common.SamePath(expectedRoot, Common.DataPath) || (!Common.SamePath(full, root) && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("用户数据路径不在浮岛目录内。");
            string current = full;
            while (!String.IsNullOrEmpty(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("用户数据中包含文件或目录链接，数据未删除。");
                DirectoryInfo parent = Directory.GetParent(current);
                current = parent == null ? null : parent.FullName;
            }
            if (Directory.Exists(full)) foreach (string child in Directory.GetFileSystemEntries(full)) ValidateDataTree(child, root);
        }

        private static void RemoveDataTree(string path, string expectedRoot)
        {
            ValidateDataTree(path, expectedRoot);
            foreach (string file in Directory.GetFiles(path)) File.Delete(file);
            foreach (string child in Directory.GetDirectories(path)) RemoveDataTree(child, expectedRoot);
            Directory.Delete(path, false);
        }
    }

    internal sealed class UninstallOptions
    {
        internal string InstallDirectory, UserSid, ParentExe;
        internal int ParentPid;
        internal bool Remove, DeleteData;
        internal static UninstallOptions Parse(string[] args)
        {
            UninstallOptions options = new UninstallOptions();
            System.Collections.Generic.HashSet<string> seen = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                if (!seen.Add(key)) throw new ArgumentException("卸载参数重复。");
                if (key == "--remove") options.Remove = true;
                else if (key == "--delete-data") options.DeleteData = true;
                else {
                    if (++i == args.Length) throw new ArgumentException("卸载参数缺少取值。");
                    if (key == "--install-dir") options.InstallDirectory = args[i];
                    else if (key == "--user-sid") {
                        if (String.IsNullOrWhiteSpace(args[i])) throw new ArgumentException("Windows 账户校验参数不能为空。");
                        options.UserSid = args[i];
                    }
                    else if (key == "--parent-exe") options.ParentExe = args[i];
                    else if (key == "--parent-pid" && Int32.TryParse(args[i], out options.ParentPid) && options.ParentPid > 0) { }
                    else throw new ArgumentException("卸载参数无效：" + key);
                }
            }
            if (!options.Remove && (options.DeleteData || options.ParentPid != 0 || options.ParentExe != null))
                throw new ArgumentException("请从卸载窗口确认要移除的内容。");
            return options;
        }
    }

    internal sealed class UninstallForm : Form
    {
        private readonly CheckBox removeData;
        internal bool DeleteData { get { return removeData.Checked; } }
        internal UninstallForm()
        {
            Text = "卸载浮岛";
            ClientSize = new Size(530, 300);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(18, 22, 32);
            ForeColor = Color.FromArgb(230, 237, 246);
            Controls.Add(new Label { Text = "要卸载浮岛吗？", Font = new Font(Font.FontFamily, 16F, FontStyle.Bold), Location = new Point(28, 25), Size = new Size(412, 37) });
            Controls.Add(new Label { Text = "将退出浮岛，移除应用、快捷方式和开机启动项。\n默认保留你的本地设置与日程。", Location = new Point(30, 77), Size = new Size(410, 50) });
            Controls.Add(new TextBox { Text = Common.InstallPath, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = BackColor, ForeColor = ForeColor, Location = new Point(31, 140), Size = new Size(465, 43), Multiline = true, TabStop = false });
            removeData = new CheckBox { Text = "同时删除共用的本地设置和日程（不可恢复）", Location = new Point(31, 196), Size = new Size(465, 25), Checked = false };
            Controls.Add(removeData);
            Button cancel = new Button { Text = "取消", Location = new Point(288, 247), Size = new Size(100, 32), DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat };
            Button remove = new Button { Text = "卸载", Location = new Point(400, 247), Size = new Size(100, 32), DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(148, 239, 207), ForeColor = Color.FromArgb(14, 40, 34) };
            cancel.FlatAppearance.BorderColor = Color.FromArgb(75, 83, 100);
            remove.FlatAppearance.BorderSize = 0;
            Controls.Add(cancel);
            Controls.Add(remove);
            AcceptButton = cancel;
            CancelButton = cancel;
        }
    }
}
