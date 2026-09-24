using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Forms = System.Windows.Forms;

namespace FreeIsland
{
    public sealed class IslandApp : Application
    {
        private CoreEngine engine;
        private ControlWindow panel;
        private BallWindow ball;
        private RadialWindow radial;
        private IslandWindow island;
        private IslandHandleWindow islandHandle;
        private PresentationWindow presentation;
        private Forms.NotifyIcon tray;
        private DispatcherTimer ticker;
        private DispatcherTimer updateTicker;
        private UpdateService updater;
        private AssistantController assistant;
        private AlertAudioService alertAudio;
        private DispatcherTimer assistantTicker;
        private EventWaitHandle exitEvent, showEvent;
        private RegisteredWaitHandle exitWait, showWait;
        private bool ending, safe;
        private bool lastCountdown, lastStopwatch;
        private bool lastSoundEnabled;
        private IslandPlacement lastPlacement;
        private bool lastEdge;
        private UsageScene lastScene;
        private int lastIslandDotSize, lastGlassMode, lastActiveIslandSize;
        private int lastGlassRefraction, lastGlassTransparency, lastGlassHighlight;
        private double lastIslandScale;
        private HwndSource hotkey;
        private string dataPath;
        private IslandNoticeEventArgs lastNotice;

        [STAThread]
        public static int Main(string[] args)
        {
            bool safeMode = args.Contains("--test-ui") || args.Contains("--smoke-test");
            if (args.Contains("--exit"))
            {
                try { using (var e = EventWaitHandle.OpenExisting("Local\\FreeIsland.Exit")) e.Set(); } catch (WaitHandleCannotBeOpenedException) { }
                return 0;
            }
            bool created;
            using (var mutex = new Mutex(true, safeMode ? "Local\\FreeIsland.Test.Instance" : "Local\\FreeIsland.Instance", out created))
            {
                if (!created)
                {
                    if (!args.Contains("--silent"))
                        try { using (var e = EventWaitHandle.OpenExisting(safeMode ? "Local\\FreeIsland.Test.Show" : "Local\\FreeIsland.Show")) e.Set(); } catch (WaitHandleCannotBeOpenedException) { }
                    return 0;
                }
                try
                {
                    var app = new IslandApp();
                    app.safe = safeMode;
                    app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    app.Startup += delegate { app.Initialize(args); };
                    app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
                    {
                        app.Log(e.Exception);
                        if (app.tray != null) app.tray.ShowBalloonTip(5000, "浮岛", "操作未完成，请重试。详情已记录在本地日志。", Forms.ToolTipIcon.Warning);
                        e.Handled = true;
                    };
                    app.Run();
                    return Environment.ExitCode;
                }
                catch (Exception ex)
                {
                    if (safeMode) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-error.txt"), ex.ToString()); return 1; }
                    MessageBox.Show("浮岛未能启动：\n" + ex.Message, "浮岛", MessageBoxButton.OK, MessageBoxImage.Error);
                    return 1;
                }
            }
        }

        private void Initialize(string[] args)
        {
            SurfaceStyle.SnapshotMode = args.Contains("--smoke-test");
            dataPath = safe ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FreeIsland");
            engine = new CoreEngine(dataPath, safe);
            lastSoundEnabled = engine.Settings.SoundEnabled;
            lastCountdown = engine.CountdownRunning;
            lastStopwatch = engine.StopwatchRunning;
            // Honor the installer startup checkbox on the first run and after upgrades.
            if (!safe) engine.Settings.AutoStart = StartupRegistration.IsEnabled();
            lastPlacement = engine.Settings.Placement;
            lastEdge = engine.Settings.EdgeHide;
            lastScene = engine.Settings.Scene;
            lastIslandDotSize = engine.Settings.IslandDotSize;
            lastActiveIslandSize = engine.Settings.ActiveIslandSize;
            lastGlassMode = engine.Settings.GlassMode;
            lastGlassRefraction = engine.Settings.GlassRefraction;
            lastGlassTransparency = engine.Settings.GlassTransparency;
            lastGlassHighlight = engine.Settings.GlassHighlight;
            LiquidGlass.Configure(engine.Settings);
            lastIslandScale = engine.Settings.IslandScale;
            island = new IslandWindow(engine, OpenPanel, CommitIslandDock);
            new WindowInteropHelper(island).EnsureHandle();
            island.Reposition();
            islandHandle = new IslandHandleWindow(engine, PreviewIsland, CommitIslandDock);
            island.Expanded += delegate { islandHandle.Hide(); };
            island.Collapsed += delegate { if (!ending) islandHandle.ShowAt(island.LastWorkArea); };
            ball = new BallWindow(engine, ToggleRadial, OpenPanel);
            presentation = new PresentationWindow(engine);
            ApplyAppIcon(presentation);
            alertAudio = new AlertAudioService(dataPath, safe);
            island.NoticeAcknowledged += delegate { alertAudio.Stop(); };
            assistant = new AssistantController(dataPath, safe, engine, delegate { return !ending && !panel.IsVisible && !presentation.IsVisible && !island.IsVisible && !radial.IsVisible; });
            assistant.Suggested += delegate(AssistantSuggestion suggestion) { if (!ending) AssistantIsland.Present(island, engine, suggestion, OpenPanel); };
            updater = new UpdateService(engine, delegate { return panel != null && panel.IsVisible || presentation != null && presentation.IsVisible || island != null && island.IsVisible || radial != null && radial.IsVisible || assistant.Service.IsBusy; }, ExitApp);
            panel = new ControlWindow(engine, PreviewIsland, delegate { ball.RestorePosition(); }, OpenPresentation, updater, assistant, alertAudio);
            MainWindow = panel;
            ApplyAppIcon(panel);
            radial = new RadialWindow(engine, OpenPanel, delegate { if (!ending) { ball.Show(); ball.ScheduleHide(); } });
            engine.Notice += OnNotice;
            engine.Changed += OnChanged;
            ball.Show();
            if (!island.IsVisible) islandHandle.ShowAt(island.LastWorkArea);
            CreateTray();
            SetupSignals();
            SetupHotkey();
            ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            ticker.Tick += delegate { engine.Tick(); UpdateTickInterval(); };
            UpdateTickInterval();
            ticker.Start();
            updateTicker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            updateTicker.Tick += delegate { updater.Poll(); };
            if (!safe) updateTicker.Start();
            assistantTicker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            assistantTicker.Tick += delegate { assistant.Poll(); };
            if (!safe) assistantTicker.Start();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
            if (!args.Contains("--silent")) OpenPanel("home");
            if (args.Contains("--smoke-test"))
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(RunSmokeTest));
            }
        }

        private void SetupSignals()
        {
            string prefix = safe ? "Local\\FreeIsland.Test." : "Local\\FreeIsland.";
            exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + "Exit");
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + "Show");
            exitWait = ThreadPool.RegisterWaitForSingleObject(exitEvent, delegate { Dispatcher.BeginInvoke(new Action(ExitApp)); }, null, -1, false);
            showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, delegate { Dispatcher.BeginInvoke(new Action(delegate { OpenPanel("home"); })); }, null, -1, false);
        }

        private static void ApplyAppIcon(Window window)
        {
            try
            {
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location))
                { window.Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); }
            }
            catch { }
        }

        private void SetupHotkey()
        {
            var parameters = new HwndSourceParameters("FreeIsland.Hotkey") { Width = 0, Height = 0, WindowStyle = 0, ParentWindow = new IntPtr(-3) };
            hotkey = new HwndSource(parameters);
            hotkey.AddHook(delegate(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
            {
                if (msg == 0x0312 && wp.ToInt32() == 8137) { ball.RestorePosition(); ToggleRadial(); handled = true; }
                return IntPtr.Zero;
            });
            Native.RegisterHotKey(hotkey.Handle, 8137, 0x0001 | 0x0002 | 0x4000, 0x20);
        }

        private void CreateTray()
        {
            tray = new Forms.NotifyIcon();
            try { tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); }
            catch { tray.Icon = System.Drawing.SystemIcons.Application; }
            tray.Text = "浮岛 · 安静待命";
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开控制中心", null, delegate { OpenPanel("home"); });
            menu.Items.Add("找回悬浮球", null, delegate { ball.RestorePosition(); });
            menu.Items.Add("显示当前计时", null, delegate { PreviewIsland(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("取消预约关机", null, delegate { engine.CancelShutdown(); });
            menu.Items.Add("设置", null, delegate { OpenPanel("settings"); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出浮岛", null, delegate { ExitApp(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { OpenPanel("home"); };
            tray.BalloonTipClicked += delegate { OpenPanel("reminders"); };
            tray.Visible = true;
        }

        private void OpenPanel(string page)
        {
            if (radial != null) radial.Dismiss();
            panel.Navigate(page);
            panel.Show();
            if (panel.WindowState == WindowState.Minimized) panel.WindowState = WindowState.Normal;
            panel.Activate();
        }

        private void ToggleRadial()
        {
            if (radial.IsVisible) { radial.Dismiss(); return; }
            radial.OpenAt(ball.Left + ball.Width / 2, ball.Top + ball.Height / 2, ball.WorkArea);
            ball.Hide();
        }

        private void OpenPresentation()
        {
            radial.Dismiss(); presentation.Open();
        }

        private void RefreshScene()
        {
            if (ending) return;
            lastScene = engine.Settings.Scene;
            bool wasVisible = panel.IsVisible;
            string page = panel.CurrentPage;
            radial.Dismiss(); radial.ApplyScene();
            ball.ApplyScene(); island.ApplyScene(); island.Reposition();
            if (!island.IsVisible) islandHandle.ShowAt(island.LastWorkArea);
            panel.AllowClose = true; panel.Close();
            panel = new ControlWindow(engine, PreviewIsland, delegate { ball.RestorePosition(); }, OpenPresentation, updater, assistant, alertAudio);
            ApplyAppIcon(panel);
            MainWindow = panel; panel.Navigate(page);
            if (wasVisible) { panel.Show(); panel.Activate(); }
        }

        private void OnChanged(object sender, EventArgs e)
        {
            if (alertAudio != null)
            {
                if (lastSoundEnabled && !engine.Settings.SoundEnabled) alertAudio.Stop();
                if (!engine.ShutdownRemaining.HasValue || engine.ShutdownRemaining.Value.TotalSeconds > 10 || engine.ShutdownRemaining.Value <= TimeSpan.Zero) alertAudio.StopShutdown();
                lastSoundEnabled = engine.Settings.SoundEnabled;
            }
            if (engine.CountdownRunning != lastCountdown)
            {
                lastCountdown = engine.CountdownRunning;
                if (lastCountdown) island.ShowActivity("countdown");
            }
            if (engine.StopwatchRunning != lastStopwatch)
            {
                lastStopwatch = engine.StopwatchRunning;
                if (lastStopwatch) island.ShowActivity("stopwatch");
            }
            if (lastPlacement != engine.Settings.Placement) { lastPlacement = engine.Settings.Placement; PreviewIsland(); }
            if (lastEdge != engine.Settings.EdgeHide) { lastEdge = engine.Settings.EdgeHide; ball.ApplyEdgePreference(); }
            if (lastGlassRefraction != engine.Settings.GlassRefraction || lastGlassTransparency != engine.Settings.GlassTransparency || lastGlassHighlight != engine.Settings.GlassHighlight)
            {
                lastGlassRefraction = engine.Settings.GlassRefraction;
                lastGlassTransparency = engine.Settings.GlassTransparency;
                lastGlassHighlight = engine.Settings.GlassHighlight;
                LiquidGlass.Configure(engine.Settings);
            }
            if (lastGlassMode != engine.Settings.GlassMode)
            {
                lastGlassMode = engine.Settings.GlassMode;
                ball.ApplyMaterial(); radial.ApplyMaterial(); island.ApplyMaterial(); islandHandle.ApplyMaterial();
            }
            if (lastIslandDotSize != engine.Settings.IslandDotSize || lastIslandScale != engine.Settings.IslandScale || lastActiveIslandSize != engine.Settings.ActiveIslandSize)
            {
                lastIslandDotSize = engine.Settings.IslandDotSize; lastIslandScale = engine.Settings.IslandScale;
                lastActiveIslandSize = engine.Settings.ActiveIslandSize;
                island.ApplyScene(); island.Reposition();
                if (!island.IsVisible) islandHandle.ShowAt(island.LastWorkArea);
            }
            if (lastScene != engine.Settings.Scene)
            {
                lastScene = engine.Settings.Scene;
                Dispatcher.BeginInvoke(new Action(RefreshScene));
            }
            if (island.IsVisible) island.RefreshActivity();
            islandHandle.RefreshTasks();
            UpdateTickInterval();
        }

        private void UpdateTickInterval()
        {
            if (ticker == null) return;
            double milliseconds = engine.StopwatchRunning || engine.CountdownRunning ? 250 : 1000;
            TimeSpan? left = engine.ShutdownRemaining;
            if (left.HasValue)
                milliseconds = left.Value.TotalSeconds <= 10 ? 100 : Math.Min(milliseconds, Math.Max(10, (left.Value.TotalSeconds - 10) * 1000));
            var interval = TimeSpan.FromMilliseconds(milliseconds);
            if (ticker.Interval != interval) ticker.Interval = interval;
        }

        private void OnNotice(object sender, IslandNoticeEventArgs e)
        {
            if (e.Kind != "info") lastNotice = e;
            island.ShowNotice(e);
            if (engine.Settings.SoundEnabled && alertAudio != null)
            {
                if (e.Kind == "countdown") alertAudio.Play(AlertSoundKind.Countdown);
                else if (e.Kind == "reminder") alertAudio.Play(AlertSoundKind.Reminder);
                else if (e.Kind == "shutdown" && engine.ShutdownRemaining.HasValue && engine.ShutdownRemaining.Value > TimeSpan.Zero && engine.ShutdownRemaining.Value.TotalSeconds <= 10) alertAudio.Play(AlertSoundKind.Shutdown);
            }
            if (tray != null && (e.Kind == "reminder" || e.Kind == "countdown" || e.Urgent))
                tray.ShowBalloonTip(10000, e.Title, e.Message, e.Urgent ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
        }

        private void PreviewIsland()
        {
            if (engine.GetIslandTasks().Count > 0) island.ShowTasks();
            else if (lastNotice != null) island.ShowNotice(lastNotice);
            else island.ShowNotice(new IslandNoticeEventArgs { Title = "浮岛已就绪", Message = "点击悬浮球选择功能 · 拖动调整位置", Kind = "info", Urgent = false });
        }

        private void CommitIslandDock(Point point)
        {
            Point origin = island.IsVisible ? new Point(island.Left, island.Top) : new Point(islandHandle.Left, islandHandle.Top);
            Rect work = Native.ScreenWorkArea(island, point);
            double top = Math.Abs(point.Y - work.Top), left = Math.Abs(point.X - work.Left), right = Math.Abs(work.Right - point.X);
            IslandPlacement placement = top <= left && top <= right ? IslandPlacement.Top : left <= right ? IslandPlacement.Left : IslandPlacement.Right;
            double anchor = placement == IslandPlacement.Top ? (point.X - work.Left) / work.Width : (point.Y - work.Top) / work.Height;
            engine.Settings.Placement = placement;
            engine.Settings.IslandAnchor = Math.Max(0, Math.Min(1, anchor));
            engine.Settings.IslandScreen = Native.ScreenNameAt(island, point);
            lastPlacement = placement;
            engine.SaveSettings();
            island.Reposition();
            if (island.IsVisible) { island.KeepOpenAfterDrag(); SurfaceStyle.AnimateDock(island, origin); }
            else { islandHandle.ShowAt(island.LastWorkArea); SurfaceStyle.AnimateDock(islandHandle, origin); }
        }

        private void DisplayChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate { ball.EnsureOnScreen(); island.Reposition(); if (!island.IsVisible) islandHandle.ShowAt(island.LastWorkArea); }));
        }

        private void RunSmokeTest()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "smoke-artifacts");
                Directory.CreateDirectory(path);
                engine.Settings.IslandDotPercent = 20; engine.Settings.IslandScale = 1;
                engine.Settings.GlassMode = 1; engine.SaveSettings();
                foreach (UsageScene scene in new[] { UsageScene.Desktop, UsageScene.Classroom })
                {
                    engine.Settings.Scene = scene; RefreshScene();
                    foreach (string page in new[] { "home", "stopwatch", "countdown", "reminders", "shutdown", "settings" })
                    {
                        panel.Navigate(page); panel.UpdateLayout(); SaveVisual(panel, Path.Combine(path, scene.ToString().ToLowerInvariant() + "-" + page + ".png"));
                    }
                    radial.OpenAt(ball.Left + 30, ball.Top + 30, ball.WorkArea); radial.UpdateLayout();
                    SaveVisual(radial, Path.Combine(path, scene.ToString().ToLowerInvariant() + "-radial.png")); radial.Dismiss();
                    island.Collapse(); islandHandle.ShowAt(island.LastWorkArea); islandHandle.UpdateLayout();
                    SaveVisual(islandHandle, Path.Combine(path, scene.ToString().ToLowerInvariant() + "-dot.png"));
                    foreach (int mode in new[] { 0, 1, 2 })
                    {
                        engine.Settings.GlassMode = mode; engine.SaveSettings();
                        string prefix = scene.ToString().ToLowerInvariant() + "-glass-" + mode;
                        ball.UpdateLayout(); SaveVisual(ball, Path.Combine(path, prefix + "-ball.png"));
                        radial.OpenAt(ball.Left + 30, ball.Top + 30, ball.WorkArea); radial.UpdateLayout();
                        SaveVisual(radial, Path.Combine(path, prefix + "-radial.png")); radial.Dismiss();
                        PreviewIsland(); island.UpdateLayout(); SaveVisual(island, Path.Combine(path, prefix + "-island.png")); island.Collapse();
                    }
                    engine.Settings.GlassMode = 1; engine.SaveSettings();
                }
                engine.StartCountdown(TimeSpan.FromMinutes(5));
                presentation.Open(); presentation.UpdateLayout(); SaveVisual(presentation, Path.Combine(path, "classroom-presentation.png")); presentation.Hide(); engine.CancelCountdown();
                engine.StartCountdown(TimeSpan.FromSeconds(65));
                island.ShowActivity("countdown"); island.UpdateLayout(); SaveVisual(island, Path.Combine(path, "island.png"));
                engine.Settings.IslandDotPercent = 100; engine.Settings.IslandScale = 1.5; engine.SaveSettings();
                island.UpdateLayout(); SaveVisual(island, Path.Combine(path, "island-custom.png"));
                island.Collapse(); islandHandle.ShowAt(island.LastWorkArea); islandHandle.UpdateLayout(); SaveVisual(islandHandle, Path.Combine(path, "dot-custom.png"));
                engine.Settings.IslandDotPercent = 20; engine.Settings.IslandScale = 1; engine.SaveSettings();
                radial.OpenAt(ball.Left + 30, ball.Top + 30, ball.WorkArea); radial.UpdateLayout(); SaveVisual(radial, Path.Combine(path, "radial.png"));
                radial.Dismiss();
                engine.CancelCountdown();
                engine.Settings.Scene = UsageScene.Desktop; RefreshScene();
                string result = SmokeChecks.Run(engine, panel, ball, island, islandHandle, CommitIslandDock, presentation);
                File.WriteAllText(Path.Combine(path, "result.txt"), "PASS: 12 control pages across desktop/classroom, fullscreen stage, countdown island and radial menu rendered.\r\n" + result + "\r\nNo actual shutdown or startup changes.\r\n");
            }
            catch (Exception ex) { Log(ex); Environment.ExitCode = 1; }
            finally { ExitApp(); }
        }

        private static void SaveVisual(Window w, string path)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth), (int)Math.Ceiling(w.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(w);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) png.Save(stream);
        }

        private void Log(Exception ex)
        {
            try { Directory.CreateDirectory(dataPath); File.AppendAllText(Path.Combine(dataPath, "errors.log"), DateTime.Now.ToString("s") + " " + ex + Environment.NewLine); } catch { }
        }

        private void ExitApp()
        {
            if (ending) return;
            ending = true;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            if (ticker != null) ticker.Stop();
            if (updateTicker != null) updateTicker.Stop();
            if (updater != null) updater.Dispose();
            if (assistantTicker != null) assistantTicker.Stop();
            if (assistant != null) assistant.Dispose();
            if (engine != null)
            {
                engine.Notice -= OnNotice; engine.Changed -= OnChanged;
                try { engine.Dispose(); } catch (Exception ex) { Log(ex); }
            }
            if (exitWait != null) exitWait.Unregister(null);
            if (showWait != null) showWait.Unregister(null);
            if (exitEvent != null) exitEvent.Dispose();
            if (showEvent != null) showEvent.Dispose();
            if (hotkey != null) { Native.UnregisterHotKey(hotkey.Handle, 8137); hotkey.Dispose(); }
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (panel != null) { panel.AllowClose = true; panel.Close(); }
            if (alertAudio != null) alertAudio.Dispose();
            if (presentation != null) { presentation.AllowClose = true; presentation.Close(); }
            if (ball != null) ball.Close();
            if (radial != null) radial.Close();
            if (islandHandle != null) islandHandle.Close();
            if (island != null) island.Close();
            Shutdown(Environment.ExitCode);
        }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public static Point Cursor(Window window)
        {
            POINT p; GetCursorPos(out p);
            var source = PresentationSource.FromVisual(window);
            return source == null ? new Point(p.X, p.Y) : source.CompositionTarget.TransformFromDevice.Transform(new Point(p.X, p.Y));
        }

        public static Rect ScreenWorkArea(Window window, Point dipPoint)
        {
            return WorkAreaFor(window, ScreenAt(window, dipPoint));
        }

        public static string ScreenNameAt(Window window, Point dipPoint) { return ScreenAt(window, dipPoint).DeviceName; }
        public static Rect ScreenBounds(Window window, Point dipPoint) { return WorkAreaFor(window, ScreenAt(window, dipPoint), true); }

        public static Rect DockWorkArea(Window window, string deviceName)
        {
            var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == deviceName);
            return WorkAreaFor(window, screen ?? ScreenAt(window, Cursor(window)));
        }

        private static Forms.Screen ScreenAt(Window window, Point dipPoint)
        {
            var source = PresentationSource.FromVisual(window);
            var transform = source == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
            Point point = transform.Transform(dipPoint);
            return Forms.Screen.FromPoint(new System.Drawing.Point((int)point.X, (int)point.Y));
        }

        private static Rect WorkAreaFor(Window window, Forms.Screen screen, bool fullScreen = false)
        {
            var source = PresentationSource.FromVisual(window);
            var transform = source == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
            var rect = fullScreen ? screen.Bounds : screen.WorkingArea;
            if (transform.HasInverse) transform.Invert();
            var start = transform.Transform(new Point(rect.Left, rect.Top));
            var end = transform.Transform(new Point(rect.Right, rect.Bottom));
            return new Rect(start, end);
        }
    }
}
