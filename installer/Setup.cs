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
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            IDisposable setupGuard = null;
            try
            {
                SetupOptions options = SetupOptions.Parse(args);
                if (options.AutoUpdateDirectory != null) return RunAutoUpdate(options.AutoUpdateDirectory);
                if (options.UserSid != null) Common.ValidateElevationUser(options.UserSid);
                // Read the existing installation choices before changing the target path.
                bool upgrade = Common.IsInstalled();
                bool startup = options.Startup ?? (!upgrade || Common.IsStartupEnabled());
                bool desktop = options.Desktop ?? (!upgrade || Common.HasOwnedShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));
                if (options.InstallDirectory != null) Common.ConfigureInstallPath(options.InstallDirectory);
                Action releaseGuard = delegate { if (setupGuard != null) { setupGuard.Dispose(); setupGuard = null; } };
                Action restoreGuard = delegate { if (setupGuard == null) setupGuard = Common.HoldSetupInstance(); };
                if (options.PreviewPath == null) restoreGuard();
                using (SetupForm form = new SetupForm(startup, desktop, releaseGuard, restoreGuard))
                {
                    if (options.PreviewPath != null)
                    {
                        // Rendering only: no install, elevation, shortcut or registry write.
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-32000, -32000);
                        form.ShowInTaskbar = false;
                        form.Opacity = 0;
                        form.Show();
                        Application.DoEvents();
                        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                        { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.GetFullPath(options.PreviewPath), System.Drawing.Imaging.ImageFormat.Png); }
                        form.Close();
                    }
                    else Application.Run(form);
                }
                return 0;
            }
            catch (Exception error) { MessageBox.Show(error.Message, Common.Product, MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
            finally { if (setupGuard != null) setupGuard.Dispose(); }
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

#if FI_INSTALLER_TESTING
        internal static Func<bool, bool, string> AutoInstallForTest;
        internal static Action<string, string> AutoLaunchForTest;
        internal static bool AutoStartupForTest, AutoDesktopForTest;
#endif
        internal static string InstallerVersion { get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(3); } }

        // No wizard and no elevation. Only a valid current-user installation may enter
        // this path, and the existing transactional installer remains the sole writer.
        internal static int RunAutoUpdate(string directory)
        {
            IDisposable guard = null;
            Dictionary<string, string> original = null;
            bool installed = false;
            try
            {
                guard = Common.HoldSetupInstance();
                Common.ValidateAutoUpdateTarget(directory);
#if !FI_INSTALLER_TESTING
                if (Common.IsAdministrator()) throw new InvalidOperationException("自动更新需要以普通用户权限运行。请从原账户正常启动浮岛后重试。");
#endif
                original = InstalledFingerprints();
                bool startup, desktop;
#if FI_INSTALLER_TESTING
                startup = AutoStartupForTest; desktop = AutoDesktopForTest;
                if (AutoInstallForTest == null || AutoLaunchForTest == null) throw new InvalidOperationException("Test adapters are required; real installation and launch are forbidden.");
#else
                startup = Common.IsStartupEnabled();
                desktop = Common.HasOwnedShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
                string existingVersion = FileVersionInfo.GetVersionInfo(Path.Combine(Common.InstallPath, "FreeIsland.exe")).FileVersion;
                Version previous, incoming;
                if (Version.TryParse(existingVersion, out previous) && Version.TryParse(Assembly.GetExecutingAssembly().GetName().Version.ToString(), out incoming) && previous > incoming)
                    throw new InvalidOperationException("现有浮岛版本较新，已拒绝自动降级。");
#endif
                string warning;
                // Keep new shortcut/login launches out until installation or rollback is
                // finished. Waiting here also makes early prerequisite failures restart
                // only after the updater's original process has actually exited.
                using (Common.HoldAppInstance())
                {
#if !FI_INSTALLER_TESTING
                    Common.StopInstalledApp();
#endif
                    Common.ValidateAutoUpdateTarget(directory);
#if FI_INSTALLER_TESTING
                    warning = AutoInstallForTest(startup, desktop);
#else
                    warning = Install(startup, desktop, delegate { });
#endif
                }
                installed = true;
                SaveUpdateResult("success", String.IsNullOrEmpty(warning) ? "浮岛已完成自动更新。" : "更新完成；部分系统选项需要处理：\n" + warning);
                // Install has disposed its app-instance guard before this launch.
                LaunchAfterUpdate();
                return 0;
            }
            catch (Exception error)
            {
                Common.WriteLog("Automatic update: " + error);
                string status = installed ? "restart-failed" : original == null ? "rejected" : "failed";
                string message = error.Message;
                bool restart = false;
                if (!installed && original != null)
                {
                    try
                    {
                        Common.ValidateAutoUpdateTarget(directory);
                        restart = FingerprintsMatch(original, InstalledFingerprints());
                    }
                    catch { }
                    message += restart ? "\n原安装文件保持完整，将恢复静默运行。" : "\n未能确认原安装完整，请手动运行安装包修复。";
                }
                SaveUpdateResult(status, message);
                if (restart)
                {
                    try { LaunchAfterUpdate(); }
                    catch (Exception launch)
                    {
                        Common.WriteLog("Automatic update recovery launch: " + launch);
                        SaveUpdateResult("restart-failed", message + "\n原版本启动失败：" + launch.Message);
                        return 3;
                    }
                }
                return installed ? 3 : original == null ? 2 : 1;
            }
            finally { if (guard != null) guard.Dispose(); }
        }

        private static Dictionary<string, string> InstalledFingerprints()
        {
            var fingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var names = new List<string>(Common.PayloadFiles); names.Add("FreeIsland.install");
            using (SHA256 sha = SHA256.Create()) foreach (string name in names)
            {
                string path = Path.Combine(Common.InstallPath, name);
                if (!File.Exists(path)) { fingerprints.Add(name, null); continue; }
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    fingerprints.Add(name, Convert.ToBase64String(sha.ComputeHash(stream)));
            }
            return fingerprints;
        }
        private static bool FingerprintsMatch(Dictionary<string, string> expected, Dictionary<string, string> actual)
        {
            foreach (KeyValuePair<string, string> file in expected)
            {
                string value;
                if (!actual.TryGetValue(file.Key, out value) || !String.Equals(file.Value, value, StringComparison.Ordinal)) return false;
            }
            return expected.Count == actual.Count;
        }
        private static void SaveUpdateResult(string status, string detail)
        {
            try { Common.WriteUpdateResult(status, InstallerVersion, detail); }
            catch (Exception error) { Common.WriteLog("Update result could not be saved: " + error); }
        }
        private static void LaunchAfterUpdate()
        {
#if FI_INSTALLER_TESTING
            AutoLaunchForTest(Path.Combine(Common.InstallPath, "FreeIsland.exe"), "--silent");
#else
            using (Process process = Process.Start(new ProcessStartInfo(Path.Combine(Common.InstallPath, "FreeIsland.exe"), "--silent")
                { WorkingDirectory = Common.InstallPath, UseShellExecute = false, CreateNoWindow = true }))
                if (process == null) throw new IOException("更新已完成，但浮岛未能重新启动，请从快捷方式打开。");
#endif
        }

        internal static string Install(bool startup, bool desktop, Action<int, string> progress)
        {
            using (RegistryKey framework = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
            {
                object release = framework == null ? null : framework.GetValue("Release");
                if (release == null || Convert.ToInt32(release) < 393295)
                    throw new InvalidOperationException("浮岛需要 .NET Framework 4.6 或更新版本。Windows 10 已自带；若系统组件缺失，请先修复运行环境后重试。");
            }
            using (Common.HoldAppInstance())
            {
            Common.ValidateInstallTarget();
            progress(12, "正在校验安装包…");
            Dictionary<string, byte[]> payload = ReadPayload();
            progress(28, "正在准备安装…");
            Common.StopInstalledApp();
            Common.ValidateInstallTarget();
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
                else Common.RemoveOwnedShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), true);
            } catch (Exception error) { warnings.Add("桌面快捷方式：" + error.Message); }
            try { Common.SetStartup(startup, true); }
            catch (Exception error) { warnings.Add("开机启动设置：" + error.Message); }
            try {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Common.UninstallKey))
            {
                key.SetValue("DisplayName", Common.Product);
                key.SetValue("DisplayVersion", InstallerVersion);
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
            Common.WriteLog("Installed version " + InstallerVersion + " at " + Common.InstallPath);
            string warning = String.Join(Environment.NewLine + Environment.NewLine, warnings.ToArray());
            if (warning.Length != 0) Common.WriteLog("Installed with integration warnings: " + warning);
            return warning;
            }
        }
    }

    internal sealed class SetupOptions
    {
        internal string InstallDirectory, UserSid, PreviewPath, AutoUpdateDirectory;
        internal bool? Startup, Desktop;

        internal static SetupOptions Parse(string[] args)
        {
            SetupOptions options = new SetupOptions();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < args.Length; index += 2)
            {
                string key = args[index];
                if (index + 1 >= args.Length || !seen.Add(key)) throw new ArgumentException("安装参数缺少取值或重复，请直接打开安装包重试。");
                string value = args[index + 1];
                if (key == "--install-dir") options.InstallDirectory = value;
                else if (key == "--auto-update") options.AutoUpdateDirectory = value;
                else if (key == "--user-sid")
                {
                    if (String.IsNullOrWhiteSpace(value)) throw new ArgumentException("缺少原 Windows 账户标识，请从原账户重新打开安装包。");
                    options.UserSid = value;
                }
                else if (key == "--preview") options.PreviewPath = value;
                else if (key == "--startup" || key == "--desktop")
                {
                    bool enabled;
                    if (!Boolean.TryParse(value, out enabled)) throw new ArgumentException(key + " 只接受 true 或 false。");
                    if (key == "--startup") options.Startup = enabled; else options.Desktop = enabled;
                }
                else throw new ArgumentException("不支持的安装参数：" + key);
            }
            if (options.AutoUpdateDirectory != null && (args.Length != 2 || String.IsNullOrWhiteSpace(options.AutoUpdateDirectory)))
                throw new ArgumentException("自动更新不能与安装目录、启动选项或提权参数混用。");
            return options;
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly CheckBox startup;
        private readonly CheckBox desktop;
        private readonly TextBox location;
        private readonly Label locationHint;
        private readonly Button browse;
        private readonly Button elevate;
        private readonly Label status;
        private readonly ProgressBar progress;
        private readonly Button primary;
        private readonly Button cancel;
        private readonly Action releaseSetupGuard, restoreSetupGuard;
        private readonly string registeredInstallPath;
        private readonly ToolTip pathTip;
        private bool busy;
        private bool installed;
        private bool needsElevation;

        internal SetupForm(bool startupChoice, bool desktopChoice, Action releaseGuard, Action restoreGuard)
        {
            releaseSetupGuard = releaseGuard;
            restoreSetupGuard = restoreGuard;
            registeredInstallPath = Common.RegisteredInstallPath;
            Text = "浮岛 Free Island · 安装";
            ClientSize = new Size(600, 562);
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
            Controls.Add(MakeLabel("安装位置", 33, 221, 530, 20, 9F, FontStyle.Bold));
            location = new TextBox { Name = "InstallDirectory", AccessibleName = "安装位置", Text = Common.InstallPath, Location = new Point(34, 249), Size = new Size(426, 36), AutoSize = false, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Microsoft YaHei UI", 10F), ForeColor = ForeColor, BackColor = Color.White, TabIndex = 0 };
            Controls.Add(location);
            browse = MakeButton("浏览…", 472, 249, 94, Color.White, ForeColor);
            browse.TabIndex = 1;
            browse.Click += BrowseClick;
            Controls.Add(browse);
            locationHint = MakeLabel("", 33, 293, 530, 40, 8F, FontStyle.Regular);
            locationHint.ForeColor = Color.FromArgb(87, 99, 123);
            Controls.Add(locationHint);
            pathTip = new ToolTip();
            pathTip.SetToolTip(location, location.Text);
            bool upgrade = Common.IsInstalled();
            startup = new CheckBox { Text = "开机静默启动（不弹出主窗口）", Checked = startupChoice, Location = new Point(34, 337), Size = new Size(530, 36), ForeColor = ForeColor, TabIndex = 2 };
            desktop = new CheckBox { Text = "创建桌面快捷方式", Checked = desktopChoice, Location = new Point(34, 375), Size = new Size(530, 36), ForeColor = ForeColor, TabIndex = 3 };
            Controls.Add(startup);
            Controls.Add(desktop);
            status = MakeLabel("", 33, 421, 530, 49, 8.5F, FontStyle.Regular);
            status.ForeColor = Color.FromArgb(87, 99, 123);
            Controls.Add(status);
            progress = new ProgressBar { Location = new Point(34, 480), Size = new Size(532, 5), Minimum = 0, Maximum = 100, Visible = false };
            Controls.Add(progress);
            elevate = MakeButton("管理员重试", 34, 505, 132, Color.White, ForeColor);
            elevate.Visible = false;
            elevate.TabIndex = 4;
            elevate.Click += ElevateClick;
            Controls.Add(elevate);
            cancel = MakeButton("取消", 338, 505, 96, Color.White, ForeColor);
            cancel.TabIndex = 5;
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);
            primary = MakeButton(upgrade ? "升级浮岛" : "安装浮岛", 446, 505, 120, Color.FromArgb(79, 102, 232), Color.White);
            primary.TabIndex = 6;
            primary.Click += PrimaryClick;
            Controls.Add(primary);
            AcceptButton = primary;
            CancelButton = cancel;
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
            location.TextChanged += delegate { if (!busy && !installed) { needsElevation = false; elevate.Visible = false; UpdateLocationMessage(false); } };
            UpdateLocationMessage(false);
            FormClosed += delegate { pathTip.Dispose(); };
        }

        private void UpdateLocationMessage(bool validate)
        {
            pathTip.SetToolTip(location, location.Text);
            if (validate) Common.ConfigureInstallPath(location.Text.Trim());
            bool moved = !String.IsNullOrWhiteSpace(registeredInstallPath) && !Common.SamePath(location.Text.Trim(), registeredInstallPath);
            locationHint.Text = moved ? "旧目录中的程序文件会保留，设置和日程继续共用。" : "可选择本地文件夹。应用设置和日程保存在当前账户中。";
            locationHint.ForeColor = Color.FromArgb(87, 99, 123);
            status.Text = moved ? "确认安装目录和选项后，点击“安装浮岛”。" :
                (!String.IsNullOrWhiteSpace(registeredInstallPath) ? "将升级已有安装；保留设置和日程，选项已沿用当前状态。" : "默认目录可直接安装；其他目录可能需要管理员权限。");
            status.ForeColor = Color.FromArgb(87, 99, 123);
            if (!installed) primary.Text = !moved && !String.IsNullOrWhiteSpace(registeredInstallPath) ? "升级浮岛" : "安装浮岛";
        }

        private bool ConfigureLocation()
        {
            try { UpdateLocationMessage(true); location.Text = Common.InstallPath; return true; }
            catch (Exception error)
            {
                locationHint.Text = "安装位置无效。";
                locationHint.ForeColor = Color.FromArgb(158, 50, 64);
                status.Text = "请修改安装目录后重试。";
                ShowInstallError(error);
                location.Focus();
                return false;
            }
        }

        private void BrowseClick(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择浮岛的安装文件夹";
                dialog.ShowNewFolderButton = true;
                try { if (Directory.Exists(location.Text)) dialog.SelectedPath = location.Text; } catch { }
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                location.Text = dialog.SelectedPath;
                ConfigureLocation();
            }
        }

        private static bool IsAccessDenied(Exception error)
        {
            for (Exception current = error; current != null; current = current.InnerException)
            {
                Win32Exception native = current as Win32Exception;
                if ((native != null && native.NativeErrorCode == 5) || (current.HResult & 0xFFFF) == 5) return true;
            }
            return false;
        }

        private void ShowInstallError(Exception error)
        {
            needsElevation = IsAccessDenied(error) && !Common.IsAdministrator();
            elevate.Visible = needsElevation;
            status.Text = needsElevation ? "此目录的访问被拒绝。可更换目录，或点击“管理员重试”。" : "安装未完成，请根据提示处理后重试。";
            status.ForeColor = Color.FromArgb(158, 50, 64);
            Common.WriteLog(error.ToString());
            MessageBox.Show(this, error.Message + "\n\n日志位置：" + Path.Combine(Path.GetTempPath(), "FreeIsland-Logs", "installation.log"), "浮岛安装未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void ElevateClick(object sender, EventArgs e)
        {
            if (busy || installed || !needsElevation || Common.IsAdministrator()) return;
            string arguments;
            try { arguments = Common.ElevationArguments(location.Text.Trim(), startup.Checked, desktop.Checked); }
            catch (Exception error) { ShowInstallError(error); return; }
            busy = true;
            startup.Enabled = desktop.Enabled = location.Enabled = browse.Enabled = primary.Enabled = cancel.Enabled = elevate.Enabled = false;
            try
            {
                // Release the singleton before opening the elevated copy. A cancelled
                // prompt reacquires it, while a successful launch closes this window.
                releaseSetupGuard();
                using (Process process = Process.Start(new ProcessStartInfo(Application.ExecutablePath, arguments) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath) }))
                { if (process == null) throw new IOException("没有启动管理员安装窗口，请重试。"); }
                busy = false;
                Close();
            }
            catch (Exception error)
            {
                busy = false;
                try { restoreSetupGuard(); }
                catch (Exception guardError) { MessageBox.Show(this, guardError.Message, Common.Product, MessageBoxButtons.OK, MessageBoxIcon.Information); Close(); return; }
                startup.Enabled = desktop.Enabled = location.Enabled = browse.Enabled = primary.Enabled = cancel.Enabled = elevate.Enabled = true;
                Win32Exception cancelled = error as Win32Exception;
                if (cancelled != null && cancelled.NativeErrorCode == 1223)
                {
                    status.Text = "已取消管理员授权。可继续选择其他目录或重新尝试。";
                    status.ForeColor = Color.FromArgb(87, 99, 123);
                }
                else { status.Text = "管理员安装窗口未能打开，原选项已保留。"; MessageBox.Show(this, error.Message, "无法打开管理员安装窗口", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
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
                // Applications launched by an elevated setup would inherit its
                // token. Let Explorer launch the app at the user's normal level.
                if (Common.IsAdministrator()) { Close(); return; }
                try { Process.Start(new ProcessStartInfo(Path.Combine(Common.InstallPath, "FreeIsland.exe")) { WorkingDirectory = Common.InstallPath, UseShellExecute = true }); Close(); }
                catch (Exception error) { MessageBox.Show(this, "应用启动失败：" + error.Message, Common.Product, MessageBoxButtons.OK, MessageBoxIcon.Error); }
                return;
            }
            if (!ConfigureLocation()) return;
            bool enableStartup = startup.Checked;
            bool enableDesktop = desktop.Checked;
            needsElevation = false;
            elevate.Visible = false;
            busy = true;
            startup.Enabled = desktop.Enabled = location.Enabled = browse.Enabled = primary.Enabled = cancel.Enabled = false;
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
                    startup.Enabled = desktop.Enabled = location.Enabled = browse.Enabled = true;
                    progress.Value = 0;
                    ShowInstallError(result.Error);
                }
                else
                {
                    installed = true;
                    primary.Text = Common.IsAdministrator() ? "完成" : "打开浮岛";
                    cancel.Text = "完成";
                    status.Text = Common.IsAdministrator() ? "已安装到上方所选目录。请从桌面或开始菜单打开浮岛。" : "已安装到上方所选目录。下次登录时会按所选设置静默启动。";
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
