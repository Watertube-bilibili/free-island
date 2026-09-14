using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FreeIsland
{
    internal sealed class BackdropFrame
    {
        internal byte[] Pixels;
        internal int Width, Height, Stride;
        internal Rect ScreenBounds;
    }

    /// <summary>
    /// UI-thread-only, bounded desktop sampling for optical refraction. Frames remain
    /// in caller-owned RAM; this service has no file, network, timer or screenshot path.
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
        private static readonly ConditionalWeakTable<Window, State> States = new ConditionalWeakTable<Window, State>();
        private static string failureReason = "";
        internal static string FailureReason { get { return failureReason; } }

        private sealed class State
        {
            internal bool Enabled, Applied, ApplyQueued;
            internal IntPtr Handle;
            internal uint PreviousAffinity;
            internal WeakReference Owner;
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
                    // Earlier Windows treats 0x11 as WDA_MONITOR (black capture), so
                    // never attempt the affinity on an older or unknown OS.
                    if (version.Major < 10 || (version.Major == 10 && version.Build < 19041))
                        return Fail("真实背景折射需要 Windows 10 2004 或更新版本。");
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
            if (!state.Applied) return true;
            if (IsWindow(state.Handle) && !SetWindowDisplayAffinity(state.Handle, state.PreviousAffinity))
                return NativeFailure("浮窗的原捕获状态未能恢复");
            state.Applied = false; state.Handle = IntPtr.Zero;
            Window owner = state.Owner == null ? null : state.Owner.Target as Window;
            if (owner != null && owner.IsVisible) owner.InvalidateVisual();
            return true;
        }

        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }

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
