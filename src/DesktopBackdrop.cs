using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Threading;

namespace FreeIsland
{
    internal sealed class BackdropFrame
    {
        internal byte[] Pixels;
        internal int Width, Height, Stride;
        internal Rect ScreenBounds;
    }

    /// <summary>
    /// Bounded, in-memory desktop sampling for optical refraction. Public calls are
    /// UI-thread-only. Legacy window rendering runs on one background worker because
    /// PrintWindow is synchronous and third-party windows can stop responding.
    /// WDA_EXCLUDEFROMCAPTURE also excludes enabled visible windows from supported
    /// screen-recording/sharing APIs. The setting must disclose this side effect.
    /// Merely omitting CAPTUREBLT does not exclude a WPF layered window on modern DWM.
    /// </summary>
    internal static class DesktopBackdrop
    {
        // Include other applications' layered windows in the real backdrop. WDA, not
        // the CAPTUREBLT flag, is what reliably removes our own WPF window on DWM.
        private const uint ExcludeFromCapture = 0x11, CaptureLayered = 0x40CC0020;
        private const int MaximumPixels = 4 * 1024 * 1024;
        private const int MaximumLegacySourcePixels = 4096 * 2160;
        private static readonly ConditionalWeakTable<Window, State> States = new ConditionalWeakTable<Window, State>();
        private static string failureReason = "";
        internal static string FailureReason { get { return failureReason; } }
        private static readonly object LegacyGate = new object();
        private static readonly HashSet<State> LegacyActive = new HashSet<State>();
        private static readonly List<LegacySource> LegacySources = new List<LegacySource>();
        private static LegacyWork legacyWork;
        private static Thread legacyThread;
        private static readonly AutoResetEvent LegacyWake = new AutoResetEvent(false);
        private static readonly uint OwnProcess = (uint)Process.GetCurrentProcess().Id;
        private const double LegacyFrameLifetime = 400, LegacyJobLifetime = 250;
#if FI_BACKDROP_TESTING
        internal static bool ForceLegacy;
        internal static int LegacyJobsStarted, LegacyPeakWorkers, LegacyActiveWorkers;
        internal static Func<IntPtr, IntPtr, bool> LegacyPrintOverride;
        internal static int LegacyNativeCaptures, LegacyCachedSourcePixels, LegacyPeakSourcePixels;
#endif

        private sealed class State
        {
            internal bool Enabled, Applied, ApplyQueued;
            internal IntPtr Handle;
            internal uint PreviousAffinity;
            internal WeakReference Owner;
            internal bool Legacy;
            internal int Generation;
            internal BackdropFrame LegacyFrame;
            internal double LegacyTime, NextLegacyRequest;
            internal string LegacyFailure;
            internal LegacyWork Pending;
        }

        private sealed class LegacyWork
        {
            internal State State;
            internal IntPtr OwnerHandle;
            internal int Generation, Left, Top, Width, Height;
            internal double Started;
        }

        private sealed class LegacySource : IDisposable
        {
            internal IntPtr Handle, Dc, Bitmap, Previous, Pointer;
            internal uint Process;
            internal NativeRect Bounds;
            internal int Width, Height;
            internal double Captured, Used;
            public void Dispose()
            {
                if (Previous != IntPtr.Zero && Previous != new IntPtr(-1)) SelectObject(Dc, Previous);
                if (Bitmap != IntPtr.Zero) DeleteObject(Bitmap);
                if (Dc != IntPtr.Zero) DeleteDC(Dc);
                Previous = Bitmap = Dc = Pointer = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct VersionInfo
        {
            internal int Size, Major, Minor, Build, Platform;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string ServicePack;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            internal uint Size;
            internal int Width, Height;
            internal ushort Planes, BitCount;
            internal uint Compression, SizeImage;
            internal int XPels, YPels;
            internal uint Used, Important;
        }

        [DllImport("ntdll.dll", CharSet = CharSet.Unicode)] private static extern int RtlGetVersion(ref VersionInfo version);
        [DllImport("dwmapi.dll")] private static extern int DwmIsCompositionEnabled(out bool enabled);
        [DllImport("dwmapi.dll")] private static extern int DwmFlush();
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetWindowRectangle(IntPtr window, int attribute, out NativeRect value, int size);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int metric);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool GdiFlush();
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] private static extern bool PtInRegion(IntPtr region, int x, int y);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);

        internal static bool IsSupported
        {
            get
            {
                try
                {
                    VersionInfo version = new VersionInfo { Size = Marshal.SizeOf(typeof(VersionInfo)) };
                    if (RtlGetVersion(ref version) != 0)
                        return Fail("无法确认实际 Windows 版本，已停用背景折射。");
                    if (version.Major < 10 || (version.Major == 10 && version.Build < 10240))
                        return Fail("背景折射需要 Windows 10 1507 或更新版本。");
                    bool composed;
                    if (DwmIsCompositionEnabled(out composed) != 0 || !composed)
                        return Fail("桌面合成不可用，已停用背景折射。");
                    failureReason = "";
                    return true;
                }
                catch (DllNotFoundException) { return Fail("当前系统缺少背景折射所需的桌面接口。"); }
                catch (EntryPointNotFoundException) { return Fail("当前系统不支持背景折射所需的桌面接口。"); }
            }
        }

        internal static bool UsesLegacyCapture
        {
            get
            {
#if FI_BACKDROP_TESTING
                if (ForceLegacy) return true;
#endif
                VersionInfo version = new VersionInfo { Size = Marshal.SizeOf(typeof(VersionInfo)) };
                try { return RtlGetVersion(ref version) != 0 || version.Major < 10 || (version.Major == 10 && version.Build < 19041); }
                catch (DllNotFoundException) { return true; }
                catch (EntryPointNotFoundException) { return true; }
            }
        }

        internal static string BackendDescription
        {
            get { return UsesLegacyCapture ? "早期 Win10 兼容折射：异步采样下层窗口；不支持采样的窗口上自动保留清透玻璃。" : "实时桌面折射：玻璃浮窗会从部分录屏和屏幕共享中排除。"; }
        }

        private static bool Fail(string reason) { failureReason = reason; return false; }
        private static bool NativeFailure(string operation)
        {
            int code = Marshal.GetLastWin32Error();
            return Fail(operation + "（Windows " + code + "：" + new Win32Exception(code).Message + "）。");
        }

        internal static bool SetEnabled(Window window, bool enabled)
        {
            if (window == null) return Fail("未提供需要折射背景的浮窗。");
            if (!window.Dispatcher.CheckAccess()) return Fail("背景折射必须从窗口 UI 线程调用。");
            State state;
            if (!States.TryGetValue(window, out state))
            {
                if (!enabled) return true;
                state = new State { Owner = new WeakReference(window) };
                States.Add(window, state);
                State tracked = state;
                window.SourceInitialized += delegate { if (tracked.Enabled) QueueApply(window, tracked); };
                window.IsVisibleChanged += delegate
                {
                    if (!window.IsVisible) Restore(tracked);
                    else if (tracked.Enabled) QueueApply(window, tracked);
                };
                window.StateChanged += delegate
                {
                    if (window.WindowState == WindowState.Minimized) Restore(tracked);
                    else if (tracked.Enabled && window.IsVisible) QueueApply(window, tracked);
                };
                window.Closed += delegate { tracked.Enabled = false; Restore(tracked); States.Remove(window); };
            }
            state.Enabled = enabled;
            if (!enabled) return Restore(state);
            return Apply(window, state);
        }

        private static void QueueApply(Window window, State state)
        {
            // WPF visibility/source events can precede native ShowWindow. A single
            // dispatcher callback handles that ordering without any polling timer.
            if (state.ApplyQueued) return;
            state.ApplyQueued = true;
            window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                state.ApplyQueued = false;
                if (state.Enabled && window.IsVisible && window.WindowState != WindowState.Minimized)
                    Apply(window, state);
            }));
        }

        private static bool Apply(Window window, State state)
        {
            if (!IsSupported) { Restore(state); return false; }
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero || !window.IsVisible || window.WindowState == WindowState.Minimized || !IsWindowVisible(handle))
                return Fail("浮窗尚未显示，背景采样已暂停。");
            if (UsesLegacyCapture)
            {
                if (state.Applied && !Restore(state)) return false;
                // 0x11 becomes WDA_MONITOR on old Windows: never apply it here.
                state.Handle = handle; state.Legacy = true;
                lock (LegacyGate)
                {
                    if (!LegacyActive.Contains(state) && LegacyActive.Count >= 16)
                        return Fail("同时启用的玻璃浮窗过多，暂时显示清透玻璃。");
                    LegacyActive.Add(state);
                }
                failureReason = "";
                return true;
            }
            if (state.Legacy) Restore(state);
            if (state.Applied && state.Handle == handle)
            {
                uint current;
                if (GetWindowDisplayAffinity(handle, out current) && current == ExcludeFromCapture) return true;
                if (!Restore(state)) return false;
            }
            uint previous;
            if (!GetWindowDisplayAffinity(handle, out previous)) return NativeFailure("无法读取浮窗捕获状态");
            if (!SetWindowDisplayAffinity(handle, ExcludeFromCapture)) return NativeFailure("系统无法排除浮窗自身，已停用背景折射");
            state.Handle = handle; state.PreviousAffinity = previous; state.Applied = true;
            uint actual;
            if (!GetWindowDisplayAffinity(handle, out actual) || actual != ExcludeFromCapture)
            {
                Restore(state);
                return Fail("系统未确认浮窗捕获排除，已停用背景折射以避免自身反馈。");
            }
            if (DwmFlush() != 0)
            {
                Restore(state);
                return Fail("桌面合成尚未完成，背景折射已暂停。");
            }
            failureReason = "";
            return true;
        }

        private static bool Restore(State state)
        {
            lock (LegacyGate)
            {
                state.Generation++;
                state.LegacyFrame = null; state.LegacyTime = 0; state.NextLegacyRequest = 0;
                state.Legacy = false;
                state.Pending = null; LegacyActive.Remove(state);
                if (LegacyActive.Count == 0 && legacyWork == null) ClearLegacySources();
            }
            if (!state.Applied) return true;
            if (IsWindow(state.Handle) && !SetWindowDisplayAffinity(state.Handle, state.PreviousAffinity))
                return NativeFailure("浮窗的原捕获状态未能恢复");
            state.Applied = false; state.Handle = IntPtr.Zero;
            Window owner = state.Owner == null ? null : state.Owner.Target as Window;
            if (owner != null && owner.IsVisible) owner.InvalidateVisual();
            return true;
        }

        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }

        private static double Clock { get { return Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency); } }

        private static bool TryLegacyCapture(State state, int left, int top, int width, int height, out BackdropFrame frame)
        {
            frame = null;
            double now = Clock;
            lock (LegacyGate)
            {
                BackdropFrame ready = state.LegacyFrame;
                if (ready != null && now - state.LegacyTime <= LegacyFrameLifetime &&
                    ready.ScreenBounds == new Rect(left, top, width, height)) frame = ready;
                if (now >= state.NextLegacyRequest)
                {
                    var work = new LegacyWork { State = state, OwnerHandle = state.Handle, Generation = state.Generation,
                        Left = left, Top = top, Width = width, Height = height, Started = now };
                    state.NextLegacyRequest = now + 100;
                    // One overwriteable request per registered window, not one queued
                    // task per surface/tick. A bounded mailbox keeps different floating
                    // windows fair when their DispatcherTimer ticks coincide.
                    state.Pending = work;
                    if (legacyWork == null)
                    {
                        state.Pending = null; legacyWork = work;
#if FI_BACKDROP_TESTING
                        LegacyJobsStarted++;
#endif
                    // One global in-flight job. A stuck external
                    // PrintWindow retains at most one bounded DIB and one worker. Never
                    // abort it or dispose its DC while the other application may paint.
                        StartLegacyWorker();
                    }
                }
                if (frame != null) { failureReason = ""; return true; }
                if (legacyWork != null && now - legacyWork.Started > LegacyJobLifetime)
                    return Fail("下层窗口响应较慢，已暂停折射；浮窗仍可正常操作。");
                return Fail(state.LegacyFailure ?? "正在读取下层窗口，暂时显示清透玻璃。");
            }
        }

        private static void StartLegacyWorker()
        {
            if (legacyThread == null)
            {
                // A stable worker owns cached GDI DCs for their entire lifetime;
                // CreateCompatibleDC(NULL) resources must not outlive their thread.
                legacyThread = new Thread(delegate()
                {
                    while (true)
                    {
                        LegacyWake.WaitOne();
                        LegacyWork work;
                        lock (LegacyGate) { work = legacyWork; }
                        if (work != null) RunLegacy(work);
                    }
                });
                legacyThread.IsBackground = true; legacyThread.Name = "FreeIsland legacy glass";
                legacyThread.Start();
            }
            LegacyWake.Set();
        }

        private static void RunLegacy(LegacyWork work)
        {
            BackdropFrame captured = null;
            string error = null;
            LegacyWork next = null;
#if FI_BACKDROP_TESTING
            int active = Interlocked.Increment(ref LegacyActiveWorkers);
            LegacyPeakWorkers = Math.Max(LegacyPeakWorkers, active);
#endif
            try { captured = ComposeLegacy(work, out error); }
            catch (Exception) { error = "下层窗口无法提供背景像素，暂时显示清透玻璃。"; }
            finally
            {
                lock (LegacyGate)
                {
                    if (work.State.Generation == work.Generation && work.State.Enabled && work.State.Legacy)
                    {
                        bool fresh = Clock - work.Started <= LegacyJobLifetime;
                        if (fresh && captured != null)
                        {
                            work.State.LegacyFrame = captured;
                            work.State.LegacyTime = work.Started;
                        }
                        // Keep a still-fresh previous frame through an isolated missed
                        // sample; TryLegacyCapture enforces the same short expiry. This
                        // avoids flashing between materials on one late source response.
                        else work.State.NextLegacyRequest = Math.Max(work.State.NextLegacyRequest, Clock + 400);
                        work.State.LegacyFailure = fresh ? error : "下层窗口响应超时，暂时显示清透玻璃。";
                    }
                    legacyWork = null;
                    foreach (State state in LegacyActive)
                        if (state.Enabled && state.Legacy && state.Pending != null && state.Pending.Generation == state.Generation &&
                            (next == null || state.Pending.Started < next.Started)) next = state.Pending;
                    if (next != null)
                    {
                        next.State.Pending = null;
                        // The capture deadline starts when work begins, not while a
                        // coalesced request waits behind another floating window.
                        next.Started = Clock; legacyWork = next;
#if FI_BACKDROP_TESTING
                        LegacyJobsStarted++;
#endif
                    }
                    else if (LegacyActive.Count == 0) ClearLegacySources();
                }
#if FI_BACKDROP_TESTING
                Interlocked.Decrement(ref LegacyActiveWorkers);
#endif
                if (next != null) LegacyWake.Set();
            }
        }

        private static void ClearLegacySources()
        {
            foreach (LegacySource source in LegacySources) source.Dispose();
            LegacySources.Clear();
#if FI_BACKDROP_TESTING
            LegacyCachedSourcePixels = 0;
#endif
        }

        private static BackdropFrame ComposeLegacy(LegacyWork work, out string error)
        {
            error = "下层窗口未提供完整背景，暂时显示清透玻璃。";
            lock (LegacyGate)
                if (!work.State.Enabled || !work.State.Legacy || work.State.Generation != work.Generation) return null;
            if (!IsWindow(work.OwnerHandle) || !IsWindowVisible(work.OwnerHandle)) return null;
            int count = work.Width * work.Height, remaining = count, visited = 0;
            var pixels = new byte[count * 4];
            var covered = new bool[count];
            var seen = new HashSet<IntPtr>();
            // GW_HWNDNEXT walks toward the bottom of the actual Z order. Start below
            // the owner and omit all our process windows, including other glass.
            // Protect against concurrent window destruction / handle-list changes.
            IntPtr candidate = GetWindow(work.OwnerHandle, 2);
            while (candidate != IntPtr.Zero && seen.Add(candidate) && visited++ < 512 && remaining > 0)
            {
                if (Clock - work.Started > LegacyJobLifetime) return null;
                IntPtr next = GetWindow(candidate, 2);
                uint process;
                GetWindowThreadProcessId(candidate, out process);
                int cloaked;
                if (process != 0 && process != OwnProcess && IsWindowVisible(candidate) && !IsIconic(candidate) &&
                    !(DwmGetWindowAttribute(candidate, 14, out cloaked, 4) == 0 && cloaked != 0))
                {
                    NativeRect rectangle;
                    if (GetWindowRect(candidate, out rectangle))
                    {
                        NativeRect visible = rectangle, extended;
                        if (DwmGetWindowRectangle(candidate, 9, out extended, 16) == 0 && extended.Right > extended.Left && extended.Bottom > extended.Top)
                        {
                            visible.Left = Math.Max(visible.Left, extended.Left); visible.Top = Math.Max(visible.Top, extended.Top);
                            visible.Right = Math.Min(visible.Right, extended.Right); visible.Bottom = Math.Min(visible.Bottom, extended.Bottom);
                        }
                        int l = Math.Max(work.Left, visible.Left), t = Math.Max(work.Top, visible.Top);
                        int r = Math.Min(work.Left + work.Width, visible.Right), b = Math.Min(work.Top + work.Height, visible.Bottom);
                        if (r > l && b > t)
                        {
                            IntPtr region = CreateRectRgn(0, 0, 0, 0);
                            try
                            {
                                int regionType = region == IntPtr.Zero ? 0 : GetWindowRgn(candidate, region);
                                if (regionType != 1) // NULLREGION is genuinely empty; ERROR means the normal rectangle.
                                {
                                    var required = new List<int>();
                                    for (int yy = t; yy < b; yy++) for (int xx = l; xx < r; xx++)
                                    {
                                        int index = (yy - work.Top) * work.Width + xx - work.Left;
                                        if (!covered[index] && (regionType == 0 || PtInRegion(region, xx - rectangle.Left, yy - rectangle.Top))) required.Add(index);
                                    }
                                    if (required.Count != 0)
                                    {
                                        uint affinity;
                                        // Layered windows have per-pixel / colour-key alpha that PrintWindow does
                                        // not reliably preserve. Never replace them with an opaque counterfeit.
                                        if ((GetWindowLong(candidate, -20) & 0x00080000) != 0 ||
                                            (GetWindowDisplayAffinity(candidate, out affinity) && affinity != 0))
                                        { error = "当前下层窗口不支持兼容采样，暂时显示清透玻璃。"; return null; }
                                        byte[] rendered = RenderLegacyWindow(candidate, rectangle, work);
                                        NativeRect after;
                                        if (rendered == null || !GetWindowRect(candidate, out after) ||
                                            after.Left != rectangle.Left || after.Top != rectangle.Top || after.Right != rectangle.Right || after.Bottom != rectangle.Bottom)
                                            return null;
                                        bool anyColour = false;
                                        foreach (int index in required)
                                        {
                                            int pixel = index * 4;
                                            if (rendered[pixel] > 2 || rendered[pixel + 1] > 2 || rendered[pixel + 2] > 2) { anyColour = true; break; }
                                        }
                                        // Accelerated/protected sources can report success but supply only black.
                                        // A genuinely black crop also safely uses transparent material instead.
                                        if (!anyColour) { error = "下层窗口未提供可用背景像素，暂时显示清透玻璃。"; return null; }
                                        foreach (int index in required)
                                        {
                                            int pixel = index * 4;
                                            // Detect unpainted pixels: an app may return success without supplying
                                            // its accelerated / protected content. Unknown coverage must stay clear.
                                            if (rendered[pixel] == 253 && rendered[pixel + 1] == 1 && rendered[pixel + 2] == 254 && rendered[pixel + 3] == 127)
                                                return null;
                                            pixels[pixel] = rendered[pixel]; pixels[pixel + 1] = rendered[pixel + 1];
                                            pixels[pixel + 2] = rendered[pixel + 2]; pixels[pixel + 3] = 255;
                                            covered[index] = true; remaining--;
                                        }
                                    }
                                }
                            }
                            finally { if (region != IntPtr.Zero) DeleteObject(region); }
                        }
                    }
                }
                candidate = next;
            }
            if (remaining != 0) return null;
            error = null;
            return new BackdropFrame { Pixels = pixels, Width = work.Width, Height = work.Height, Stride = work.Width * 4,
                ScreenBounds = new Rect(work.Left, work.Top, work.Width, work.Height) };
        }

        private static byte[] RenderLegacyWindow(IntPtr handle, NativeRect window, LegacyWork work)
        {
            long sourceWidth = (long)window.Right - window.Left, sourceHeight = (long)window.Bottom - window.Top;
            if (sourceWidth <= 0 || sourceHeight <= 0 || sourceWidth > 16384 || sourceHeight > 16384 ||
                sourceWidth * sourceHeight > MaximumLegacySourcePixels) return null;
            LegacySource source = null;
            uint process;
            GetWindowThreadProcessId(handle, out process);
            foreach (LegacySource item in LegacySources)
                if (item.Handle == handle && item.Process == process && item.Bounds.Left == window.Left && item.Bounds.Top == window.Top &&
                    item.Bounds.Right == window.Right && item.Bounds.Bottom == window.Bottom) { source = item; break; }
            if (source == null)
            {
                int total = 0;
                foreach (LegacySource item in LegacySources) total += item.Width * item.Height;
                while (LegacySources.Count != 0 && (LegacySources.Count >= 2 || total + sourceWidth * sourceHeight > MaximumLegacySourcePixels))
                {
                    LegacySource oldest = LegacySources[0];
                    foreach (LegacySource item in LegacySources) if (item.Used < oldest.Used) oldest = item;
                    total -= oldest.Width * oldest.Height;
                    LegacySources.Remove(oldest); oldest.Dispose();
                }
                source = new LegacySource { Handle = handle, Process = process, Bounds = window, Width = (int)sourceWidth, Height = (int)sourceHeight };
                source.Dc = CreateCompatibleDC(IntPtr.Zero);
                if (source.Dc == IntPtr.Zero) return null;
                var info = new BitmapInfo { Size = 40, Width = source.Width, Height = -source.Height, Planes = 1, BitCount = 32 };
                source.Bitmap = CreateDIBSection(source.Dc, ref info, 0, out source.Pointer, IntPtr.Zero, 0);
                if (source.Bitmap == IntPtr.Zero || source.Pointer == IntPtr.Zero) { source.Dispose(); return null; }
                source.Previous = SelectObject(source.Dc, source.Bitmap);
                if (source.Previous == IntPtr.Zero || source.Previous == new IntPtr(-1)) { source.Dispose(); return null; }
                LegacySources.Add(source);
#if FI_BACKDROP_TESTING
                LegacyCachedSourcePixels = total + source.Width * source.Height;
                LegacyPeakSourcePixels = Math.Max(LegacyPeakSourcePixels, LegacyCachedSourcePixels);
#endif
            }
            int sourceStride = source.Width * 4;
            source.Used = Clock;
            if (source.Captured == 0 || Clock - source.Captured > 150)
            {
                source.Captured = 0;
                var sentinelRow = new byte[sourceStride];
                for (int i = 0; i < sentinelRow.Length; i += 4)
                { sentinelRow[i] = 253; sentinelRow[i + 1] = 1; sentinelRow[i + 2] = 254; sentinelRow[i + 3] = 127; }
                for (int y = 0; y < source.Height; y++) Marshal.Copy(sentinelRow, 0, IntPtr.Add(source.Pointer, y * sourceStride), sourceStride);
                // PW_RENDERFULLCONTENT is defined by the Windows SDK for >= 8.1
                // (_WIN32_WINNT >= 0x0603), which includes our 1507 minimum. It allows
                // cooperative accelerated windows to supply their composed content.
                // DWM ignores viewport translations for this flag, so a full source
                // DIB is required. Keep at most two source DIBs, <=4096*2160 pixels
                // in TOTAL. Reuse actual source pixels for 150ms across floating
                // windows and their radial buttons, with rectangle checks above.
                bool painted;
#if FI_BACKDROP_TESTING
                LegacyNativeCaptures++;
                painted = LegacyPrintOverride == null ? PrintWindow(handle, source.Dc, 2) : LegacyPrintOverride(handle, source.Dc);
#else
                painted = PrintWindow(handle, source.Dc, 2);
#endif
                if (!painted || !GdiFlush()) return null;
                source.Captured = Clock;
            }
            byte[] result = new byte[work.Width * work.Height * 4];
            for (int i = 0; i < result.Length; i += 4)
            { result[i] = 253; result[i + 1] = 1; result[i + 2] = 254; result[i + 3] = 127; }
            int left = Math.Max(work.Left, window.Left), top = Math.Max(work.Top, window.Top);
            int right = Math.Min(work.Left + work.Width, window.Right), bottom = Math.Min(work.Top + work.Height, window.Bottom);
            for (int y = top; y < bottom; y++)
            {
                int sourceOffset = (y - window.Top) * sourceStride + (left - window.Left) * 4;
                int destinationOffset = ((y - work.Top) * work.Width + left - work.Left) * 4;
                Marshal.Copy(IntPtr.Add(source.Pointer, sourceOffset), result, destinationOffset, (right - left) * 4);
            }
            return result;
        }

        internal static bool TryCapture(Window window, Rect screenPixelBounds, out BackdropFrame frame)
        {
            frame = null;
            if (window == null || !window.Dispatcher.CheckAccess()) return Fail("背景折射必须从窗口 UI 线程调用。");
            State state;
            if (!States.TryGetValue(window, out state) || !state.Enabled) return Fail("背景折射未启用。");
            if (!Apply(window, state)) return false;
            if (screenPixelBounds.IsEmpty || !Finite(screenPixelBounds.X) || !Finite(screenPixelBounds.Y) ||
                !Finite(screenPixelBounds.Width) || !Finite(screenPixelBounds.Height) ||
                !Finite(screenPixelBounds.Right) || !Finite(screenPixelBounds.Bottom) || screenPixelBounds.Width <= 0 || screenPixelBounds.Height <= 0)
                return Fail("背景采样区域为空或无效。");
            NativeRect native;
            if (!GetWindowRect(state.Handle, out native)) return NativeFailure("无法读取浮窗边界");
            int virtualX = GetSystemMetrics(76), virtualY = GetSystemMetrics(77), virtualWidth = GetSystemMetrics(78), virtualHeight = GetSystemMetrics(79);
            if (virtualWidth <= 0 || virtualHeight <= 0 || native.Right <= native.Left || native.Bottom <= native.Top)
                return Fail("当前显示区域不可用。");
            // Clip before integer conversion. Negative monitor coordinates are valid;
            // arbitrarily large/invalid caller rectangles can never expand the capture.
            double x = Math.Max(Math.Max(Math.Floor(screenPixelBounds.Left), native.Left), virtualX);
            double y = Math.Max(Math.Max(Math.Floor(screenPixelBounds.Top), native.Top), virtualY);
            double right = Math.Min(Math.Min(Math.Ceiling(screenPixelBounds.Right), native.Right), (double)virtualX + virtualWidth);
            double bottom = Math.Min(Math.Min(Math.Ceiling(screenPixelBounds.Bottom), native.Bottom), (double)virtualY + virtualHeight);
            if (right <= x || bottom <= y || (right - x) * (bottom - y) > MaximumPixels)
                return Fail("背景采样区域超出浮窗、屏幕或允许的大小。");
            int left = (int)x, top = (int)y, width = (int)(right - x), height = (int)(bottom - y), stride = width * 4;
            if (state.Legacy)
            {
                // All surfaces in one radial-menu HWND consume a common window frame.
                // Alternating per-button crops must never overwrite one another's cache.
                x = Math.Max(native.Left, virtualX); y = Math.Max(native.Top, virtualY);
                right = Math.Min(native.Right, (double)virtualX + virtualWidth);
                bottom = Math.Min(native.Bottom, (double)virtualY + virtualHeight);
                if ((right - x) * (bottom - y) > MaximumPixels) return Fail("浮窗背景区域过大，暂时显示清透玻璃。");
                return TryLegacyCapture(state, (int)x, (int)y, (int)(right - x), (int)(bottom - y), out frame);
            }
            IntPtr screen = IntPtr.Zero, memory = IntPtr.Zero, bitmap = IntPtr.Zero, previousObject = IntPtr.Zero;
            try
            {
                screen = GetDC(IntPtr.Zero);
                if (screen == IntPtr.Zero) return NativeFailure("无法访问当前桌面");
                memory = CreateCompatibleDC(screen);
                if (memory == IntPtr.Zero) return NativeFailure("无法创建背景采样缓冲区");
                BitmapInfo info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
                IntPtr pixels;
                bitmap = CreateDIBSection(screen, ref info, 0, out pixels, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero || pixels == IntPtr.Zero) return NativeFailure("无法分配背景采样像素");
                previousObject = SelectObject(memory, bitmap);
                if (previousObject == IntPtr.Zero || previousObject == new IntPtr(-1)) return NativeFailure("无法选择背景采样缓冲区");
                if (!BitBlt(memory, 0, 0, width, height, screen, left, top, CaptureLayered)) return NativeFailure("背景采样失败");
                if (!GdiFlush()) return Fail("背景像素尚未完成复制，已跳过本帧。");
                byte[] data = new byte[stride * height];
                Marshal.Copy(pixels, data, 0, data.Length);
                // GDI screen DIB alpha is undefined. This is opaque captured backdrop,
                // not premultiplied material output; the renderer supplies the mask.
                for (int i = 3; i < data.Length; i += 4) data[i] = 255;
                frame = new BackdropFrame { Pixels = data, Width = width, Height = height, Stride = stride, ScreenBounds = new Rect(left, top, width, height) };
                failureReason = "";
                return true;
            }
            catch (OutOfMemoryException) { return Fail("背景采样内存不足，已暂停折射。"); }
            finally
            {
                if (previousObject != IntPtr.Zero && previousObject != new IntPtr(-1)) SelectObject(memory, previousObject);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memory != IntPtr.Zero) DeleteDC(memory);
                if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}
