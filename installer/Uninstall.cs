using System;
using System.Diagnostics;
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
                Common.ValidateInstallPath(Common.InstallPath);
                string marker = Path.Combine(Common.InstallPath, "FreeIsland.install");
                if (File.Exists(marker) && (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("安装记录包含文件链接，未删除任何文件。");
                if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != Common.MarkerContents)
                    throw new InvalidOperationException("未找到有效的浮岛安装记录。没有删除任何文件。");
                if (args.Length >= 1 && args[0] == "--remove")
                {
                    if (!Common.SamePath(Path.GetDirectoryName(Application.ExecutablePath), Path.GetTempPath()) || !Path.GetFileName(Application.ExecutablePath).StartsWith("FreeIsland-Uninstall-", StringComparison.Ordinal))
                        throw new InvalidOperationException("卸载进程位置校验失败。");
                    if (args.Length > 2 || (args.Length == 2 && args[1] != "--delete-data")) return 2;
                    Remove(args.Length == 2);
                    MessageBox.Show("浮岛已卸载。" + (args.Length == 2 ? "\n本地设置和日程已删除。" : "\n本地设置和日程已保留，重新安装后可以继续使用。"), "浮岛 · 再会", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                if (args.Length != 0) return 2;
                if (!Common.SamePath(Application.ExecutablePath, Path.Combine(Common.InstallPath, "FreeIsland.Uninstall.exe")))
                    throw new InvalidOperationException("请从 Windows「已安装的应用」或安装目录运行浮岛卸载程序。");
                using (UninstallForm form = new UninstallForm())
                {
                    if (form.ShowDialog() != DialogResult.OK) return 0;
                    string temporary = Path.Combine(Path.GetTempPath(), "FreeIsland-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                    File.Copy(Application.ExecutablePath, temporary, false);
                    Process.Start(new ProcessStartInfo(temporary, "--remove" + (form.DeleteData ? " --delete-data" : "")) { UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
                }
                return 0;
            }
            catch (Exception error)
            {
                Common.WriteLog("Uninstall: " + error);
                MessageBox.Show(error.Message + "\n\n如果浮岛仍在运行，请退出后重试。", "浮岛卸载未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
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
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Common.UninstallKey))
            {
                if (key != null && !Common.SamePath(Convert.ToString(key.GetValue("InstallLocation")), Common.InstallPath))
                    throw new InvalidOperationException("卸载记录指向其他目录，卸载已停止。");
            }
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
                DateTime until = DateTime.UtcNow.AddSeconds(5);
                while (true)
                {
                    try { if (File.Exists(path)) File.Delete(path); break; }
                    catch (IOException) { if (DateTime.UtcNow >= until) throw; Thread.Sleep(150); }
                }
            }
            Common.RemoveOwnedShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            Common.RemoveOwnedShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
            Common.SetStartup(false);
            Registry.CurrentUser.DeleteSubKeyTree(Common.UninstallKey, false);
            string marker = Path.Combine(Common.InstallPath, "FreeIsland.install");
            if (File.Exists(marker)) File.Delete(marker);
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

    internal sealed class UninstallForm : Form
    {
        private readonly CheckBox removeData;
        internal bool DeleteData { get { return removeData.Checked; } }
        internal UninstallForm()
        {
            Text = "卸载浮岛";
            ClientSize = new Size(470, 238);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(18, 22, 32);
            ForeColor = Color.FromArgb(230, 237, 246);
            Controls.Add(new Label { Text = "要卸载浮岛吗？", Font = new Font(Font.FontFamily, 16F, FontStyle.Bold), Location = new Point(28, 25), Size = new Size(412, 37) });
            Controls.Add(new Label { Text = "将退出浮岛，移除应用、快捷方式和开机启动项。\n默认保留你的本地设置与日程。", Location = new Point(30, 77), Size = new Size(410, 50) });
            removeData = new CheckBox { Text = "同时删除本地设置和日程（不可恢复）", Location = new Point(31, 139), Size = new Size(405, 25), Checked = false };
            Controls.Add(removeData);
            Button cancel = new Button { Text = "取消", Location = new Point(228, 185), Size = new Size(100, 32), DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat };
            Button remove = new Button { Text = "卸载", Location = new Point(340, 185), Size = new Size(100, 32), DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(148, 239, 207), ForeColor = Color.FromArgb(14, 40, 34) };
            cancel.FlatAppearance.BorderColor = Color.FromArgb(75, 83, 100);
            remove.FlatAppearance.BorderSize = 0;
            Controls.Add(cancel);
            Controls.Add(remove);
            AcceptButton = cancel;
            CancelButton = cancel;
        }
    }
}
