using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FreeIsland.Installation
{
    internal static class Setup
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--verify-payload")
            {
                try
                {
                    Dictionary<string, byte[]> payload = ReadPayload();
                    string directory = Path.GetFullPath(args[1]);
                    Directory.CreateDirectory(directory);
                    foreach (KeyValuePair<string, byte[]> file in payload) File.WriteAllBytes(Path.Combine(directory, file.Key), file.Value);
                    File.WriteAllText(Path.Combine(directory, "payload-verified.txt"), "All four embedded payload files passed SHA-256 verification. No installation, application launch, shortcuts or registry writes were performed.", Encoding.UTF8);
                    return 0;
                }
                catch (Exception error)
                {
                    try { File.WriteAllText(Path.Combine(Path.GetFullPath(args[1]), "payload-error.txt"), error.ToString()); } catch { }
                    return 1;
                }
            }
            if (args.Length > 0) return 2;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { using (Common.HoldSetupInstance()) Application.Run(new SetupForm()); return 0; }
            catch (Exception error) { MessageBox.Show(error.Message, Common.Product, MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
        }

        internal static Dictionary<string, byte[]> ReadPayload()
        {
            Dictionary<string, string> expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (Stream hashes = Assembly.GetExecutingAssembly().GetManifestResourceStream("FreeIsland.Payload.sha256"))
            {
                if (hashes == null) throw new InvalidDataException("安装包缺少校验清单。");
                using (StreamReader reader = new StreamReader(hashes))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (String.IsNullOrWhiteSpace(line)) continue;
                        int space = line.IndexOf(' ');
                        if (space != 64) throw new InvalidDataException("安装包校验清单无效。");
                        expected.Add(line.Substring(space).Trim().TrimStart('*'), line.Substring(0, space));
                    }
                }
            }
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
            foreach (string name in Common.PayloadFiles)
            {
                using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("FreeIsland.Payload." + name))
                {
                    if (resource == null) throw new InvalidDataException("安装包文件不完整：" + name);
                    using (MemoryStream buffer = new MemoryStream())
                    {
                        resource.CopyTo(buffer);
                        byte[] contents = buffer.ToArray();
                        string hash;
                        using (SHA256 algorithm = SHA256.Create()) hash = BitConverter.ToString(algorithm.ComputeHash(contents)).Replace("-", "");
                        string expectedHash;
                        if (!expected.TryGetValue(name, out expectedHash) || !String.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("文件校验失败：" + name + "。请重新获取完整的安装包。");
                        files.Add(name, contents);
                    }
                }
            }
            return files;
        }

        internal static string Install(bool startup, bool desktop, Action<int, string> progress)
        {
            using (RegistryKey framework = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
            {
                object release = framework == null ? null : framework.GetValue("Release");
                if (release == null || Convert.ToInt32(release) < 528040)
                    throw new InvalidOperationException("浮岛需要 .NET Framework 4.8。请先安装该运行环境，然后重试。");
            }
            using (Common.HoldAppInstance())
            {
            Common.ValidateInstallPath(Common.InstallPath);
            progress(12, "正在校验安装包…");
            Dictionary<string, byte[]> payload = ReadPayload();
            progress(28, "正在准备安装…");
            Common.StopInstalledApp();
            Common.ValidateInstallPath(Common.InstallPath);
            Directory.CreateDirectory(Common.InstallPath);
            string installMarker = Path.Combine(Common.InstallPath, "FreeIsland.install");
            if (File.Exists(installMarker) && (File.GetAttributes(installMarker) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("安装记录包含文件链接，无法继续。");
            if (File.Exists(installMarker) && File.ReadAllText(installMarker).Trim() != Common.MarkerContents)
                throw new IOException("安装记录不属于此版本的浮岛，已停止升级，未覆盖已有文件。");
            progress(44, "正在写入应用文件…");
            payload.Add("FreeIsland.install", Encoding.UTF8.GetBytes(Common.MarkerContents));
            Common.ReplacePayload(payload);
            progress(70, "正在配置快捷方式…");
            // The verified files are installed. Optional integration failures must not
            // misleadingly report that an otherwise successful upgrade was rolled back.
            List<string> warnings = new List<string>();
            try { Common.CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Programs)); }
            catch (Exception error) { warnings.Add("开始菜单快捷方式：" + error.Message); }
            try {
                if (desktop) Common.CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                else Common.RemoveOwnedShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            } catch (Exception error) { warnings.Add("桌面快捷方式：" + error.Message); }
            try { Common.SetStartup(startup); }
            catch (Exception error) { warnings.Add("开机启动设置：" + error.Message); }
            try {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Common.UninstallKey))
            {
                key.SetValue("DisplayName", Common.Product);
                key.SetValue("DisplayVersion", "1.0.1");
                key.SetValue("Publisher", "Free Island");
                key.SetValue("InstallLocation", Common.InstallPath);
                key.SetValue("DisplayIcon", Path.Combine(Common.InstallPath, "FreeIsland.exe"));
                key.SetValue("UninstallString", "\"" + Path.Combine(Common.InstallPath, "FreeIsland.Uninstall.exe") + "\"");
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                int bytes = 0;
                foreach (byte[] content in payload.Values) bytes += content.Length;
                key.SetValue("EstimatedSize", Math.Max(1, bytes / 1024), RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            } catch (Exception error) { warnings.Add("卸载记录：" + error.Message + "。仍可运行安装目录中的 FreeIsland.Uninstall.exe 卸载。"); }
            progress(100, "安装完成，欢迎来到浮岛。");
            Common.WriteLog("Installed version 1.0.1 at " + Common.InstallPath);
            string warning = String.Join(Environment.NewLine + Environment.NewLine, warnings.ToArray());
            if (warning.Length != 0) Common.WriteLog("Installed with integration warnings: " + warning);
            return warning;
            }
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly CheckBox startup;
        private readonly CheckBox desktop;
        private readonly Label status;
        private readonly ProgressBar progress;
        private readonly Button primary;
        private readonly Button cancel;
        private bool busy;
        private bool installed;

        internal SetupForm()
        {
            Text = "浮岛 Free Island · 安装";
            ClientSize = new Size(600, 440);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            BackColor = Color.FromArgb(246, 247, 251);
            ForeColor = Color.FromArgb(24, 36, 58);
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Controls.Add(new BrandPanel { Location = Point.Empty, Size = new Size(600, 130) });
            Label title = MakeLabel("把时间交给浮岛，把专注留给自己。", 32, 150, 540, 28, 13F, FontStyle.Bold);
            Controls.Add(title);
            Controls.Add(MakeLabel("触屏大按钮 · 全屏课堂计时 · 静默自启动", 33, 187, 535, 23, 9F, FontStyle.Regular));
            Label location = MakeLabel("安装位置：" + Common.InstallPath, 33, 216, 530, 21, 8F, FontStyle.Regular);
            location.ForeColor = Color.FromArgb(87, 99, 123);
            location.AutoEllipsis = true;
            Controls.Add(location);
            new ToolTip().SetToolTip(location, Common.InstallPath);
            bool upgrade = Common.IsInstalled();
            startup = new CheckBox { Text = "开机静默启动（不弹出主窗口）", Checked = !upgrade || Common.IsStartupEnabled(), Location = new Point(34, 251), Size = new Size(500, 24), ForeColor = ForeColor };
            desktop = new CheckBox { Text = "创建桌面快捷方式", Checked = !upgrade || Common.HasOwnedShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)), Location = new Point(34, 282), Size = new Size(500, 24), ForeColor = ForeColor };
            Controls.Add(startup);
            Controls.Add(desktop);
            status = MakeLabel(upgrade ? "将升级已有安装；保留设置和日程，选项已沿用当前状态。" : "仅为当前用户安装，无需管理员权限。", 33, 320, 530, 22, 8F, FontStyle.Regular);
            status.ForeColor = Color.FromArgb(87, 99, 123);
            Controls.Add(status);
            progress = new ProgressBar { Location = new Point(34, 351), Size = new Size(532, 5), Minimum = 0, Maximum = 100, Visible = false };
            Controls.Add(progress);
            cancel = MakeButton("取消", 338, 376, 96, Color.White, ForeColor);
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);
            primary = MakeButton(upgrade ? "升级浮岛" : "安装浮岛", 446, 376, 120, Color.FromArgb(79, 102, 232), Color.White);
            primary.Click += PrimaryClick;
            Controls.Add(primary);
            AcceptButton = primary;
            CancelButton = cancel;
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
        }

        private static Label MakeLabel(string text, int x, int y, int width, int height, float size, FontStyle style)
        {
            return new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height), Font = new Font("Microsoft YaHei UI", size, style), BackColor = Color.Transparent };
        }

        private static Button MakeButton(string text, int x, int y, int width, Color background, Color foreground)
        {
            Button button = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, 36), FlatStyle = FlatStyle.Flat, BackColor = background, ForeColor = foreground, Cursor = Cursors.Hand };
            bool isPrimary = background.ToArgb() == Color.FromArgb(79, 102, 232).ToArgb();
            button.FlatAppearance.BorderSize = isPrimary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(213, 220, 234);
            button.FlatAppearance.MouseOverBackColor = isPrimary ? Color.FromArgb(66, 88, 206) : Color.FromArgb(237, 240, 250);
            button.FlatAppearance.MouseDownBackColor = isPrimary ? Color.FromArgb(52, 73, 185) : Color.FromArgb(223, 230, 246);
            return button;
        }

        private void PrimaryClick(object sender, EventArgs e)
        {
            if (installed)
            {
                try { Process.Start(new ProcessStartInfo(Path.Combine(Common.InstallPath, "FreeIsland.exe")) { WorkingDirectory = Common.InstallPath, UseShellExecute = true }); Close(); }
                catch (Exception error) { MessageBox.Show(this, "应用启动失败：" + error.Message, Common.Product, MessageBoxButtons.OK, MessageBoxIcon.Error); }
                return;
            }
            bool enableStartup = startup.Checked;
            bool enableDesktop = desktop.Checked;
            busy = true;
            startup.Enabled = desktop.Enabled = primary.Enabled = cancel.Enabled = false;
            progress.Visible = true;
            BackgroundWorker worker = new BackgroundWorker { WorkerReportsProgress = true };
            worker.DoWork += delegate(object source, DoWorkEventArgs work) { work.Result = Setup.Install(enableStartup, enableDesktop, delegate(int percent, string message) { worker.ReportProgress(percent, message); }); };
            worker.ProgressChanged += delegate(object source, ProgressChangedEventArgs change) { progress.Value = change.ProgressPercentage; status.Text = Convert.ToString(change.UserState); };
            worker.RunWorkerCompleted += delegate(object source, RunWorkerCompletedEventArgs result)
            {
                busy = false;
                primary.Enabled = cancel.Enabled = true;
                if (result.Error != null)
                {
                    startup.Enabled = desktop.Enabled = true;
                    progress.Value = 0;
                    status.Text = "安装未完成，可以修正问题后重试。";
                    Common.WriteLog(result.Error.ToString());
                    MessageBox.Show(this, result.Error.Message + "\n\n日志位置：" + Path.Combine(Path.GetTempPath(), "FreeIsland-Logs", "installation.log"), "浮岛安装未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    installed = true;
                    primary.Text = "打开浮岛";
                    cancel.Text = "完成";
                    status.Text = "已安装。下次登录时会按所选设置静默启动。";
                    status.ForeColor = Color.FromArgb(39, 105, 86);
                    string warning = result.Result as string;
                    if (!String.IsNullOrEmpty(warning))
                    {
                        status.Text = "应用已安装；部分快捷方式或系统选项需要处理。";
                        MessageBox.Show(this, warning, "浮岛已安装，部分选项需要处理", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                worker.Dispose();
            };
            worker.RunWorkerAsync();
        }
    }

    internal sealed class BrandPanel : Panel
    {
        internal BrandPanel() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.White);
            using (SolidBrush cobalt = new SolidBrush(Color.FromArgb(79, 102, 232))) g.FillEllipse(cobalt, 34, 32, 60, 60);
            // Exact 24-unit AppVisual.Brand geometry, scaled to 60 px.
            using (Pen island = new Pen(Color.White, 12.5F))
            {
                island.StartCap = island.EndCap = LineCap.Round;
                g.DrawLine(island, 52.75F, 69.5F, 75.25F, 69.5F);
            }
            using (Pen sky = new Pen(Color.FromArgb(185, 205, 255), 7.5F))
            {
                sky.StartCap = sky.EndCap = LineCap.Round;
                g.DrawLine(sky, 62.75F, 52F, 72.75F, 52F);
            }
            using (Font title = new Font("Microsoft YaHei UI", 23F, FontStyle.Bold))
            using (Brush ink = new SolidBrush(Color.FromArgb(24, 36, 58))) g.DrawString("浮岛", title, ink, 113, 24);
            using (Font caption = new Font("Segoe UI", 8.5F, FontStyle.Bold))
            using (Brush muted = new SolidBrush(Color.FromArgb(87, 99, 123))) g.DrawString("FREE ISLAND", caption, muted, 209, 43);
            using (Font tag = new Font("Microsoft YaHei UI", 9F))
            using (Brush muted = new SolidBrush(Color.FromArgb(87, 99, 123)))
                g.DrawString("课堂大屏与电脑，都能轻松掌控时间", tag, muted, new RectangleF(116, 76, 450, 28));
        }
    }
}
