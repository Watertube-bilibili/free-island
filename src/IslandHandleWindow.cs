using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FreeIsland
{
    /// <summary>A tiny, quiet edge handle for the collapsed island.</summary>
    public sealed class IslandHandleWindow : Window
    {
        private readonly CoreEngine engine;
        private readonly Ellipse dot;
        private bool pointerDown;
        private bool dragged;
        private Point pressCursor;
        private Point pressOrigin;

        public IslandHandleWindow(CoreEngine engine, Action wake, Action<Point> dropped)
        {
            this.engine = engine;
            SurfaceStyle.Setup(this, "浮岛 · 灵动岛小黑点");
            Width = Height = engine.Settings.Scene == UsageScene.Classroom ? 44 : 24;
            Focusable = false;
            dot = new Ellipse
            {
                Fill = Brushes.Black,
                IsHitTestVisible = false,
                UseLayoutRounding = false,
                SnapsToDevicePixels = true
            };
            // A nonzero alpha keeps the larger target clickable in a layered window.
            var touchArea = new Canvas { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Cursor = Cursors.Hand, ToolTip = "点击展开 · 拖动调整位置" }; touchArea.Children.Add(dot); Content = touchArea;
            System.Windows.Automation.AutomationProperties.SetName(touchArea, "点击展开灵动岛，拖动调整位置");
            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                e.Handled = true;
                SurfaceStyle.StopPosition(this);
                pressCursor = Native.Cursor(this);
                pressOrigin = new Point(Left, Top);
                dragged = false;
                pointerDown = CaptureMouse();
            };
            MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!pointerDown) return;
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    pointerDown = false;
                    ReleaseMouseCapture();
                    return;
                }
                Vector delta = Native.Cursor(this) - pressCursor;
                if (!dragged && delta.Length <= 4) return;
                dragged = true;
                Left = pressOrigin.X + delta.X;
                Top = pressOrigin.Y + delta.Y;
                e.Handled = true;
            };
            MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!pointerDown) return;
                e.Handled = true;
                pointerDown = false;
                ReleaseMouseCapture();
                if (dragged)
                {
                    if (dropped != null) dropped(new Point(Left + Width / 2, Top + Height / 2));
                }
                else if (wake != null) wake();
            };
            LostMouseCapture += delegate { pointerDown = false; };
            TouchWindowDrag.Attach(this, null,
                delegate { SurfaceStyle.StopPosition(this); pointerDown = true; },
                delegate { if (wake != null) wake(); },
                delegate(Point point) { pointerDown = false; if (dropped != null) dropped(point); },
                delegate { pointerDown = false; });
            SourceInitialized += delegate
            {
                var source = PresentationSource.FromVisual(this) as HwndSource;
                if (source != null) source.AddHook(PreventActivation);
                UpdateDot();
            };
        }

        public void ShowAt(Rect work)
        {
            if (pointerDown) return;
            SurfaceStyle.StopPosition(this);
            IslandPlacement placement = engine.Settings.Placement;
            bool top = placement == IslandPlacement.Top;
            double anchor = engine.Settings.IslandAnchor;
            if (double.IsNaN(anchor) || double.IsInfinity(anchor)) anchor = .5;
            anchor = Math.Max(0, Math.Min(1, anchor));
            Width = SceneMetrics.HandleWidth(engine, placement);
            Height = SceneMetrics.HandleHeight(engine, placement);
            double left = top ? work.Left + work.Width * anchor - Width / 2
                : placement == IslandPlacement.Left ? work.Left : work.Right - Width;
            double y = top ? work.Top : work.Top + work.Height * anchor - Height / 2;
            Left = Math.Max(work.Left, Math.Min(left, work.Right - Width));
            Top = Math.Max(work.Top, Math.Min(y, work.Bottom - Height));
            UpdateDot();
            if (!IsVisible) Show();
        }

        private void UpdateDot()
        {
            // The setting is a physical-pixel diameter, independent of scene or Windows scaling.
            var source = PresentationSource.FromVisual(this);
            Matrix pixels = source == null || source.CompositionTarget == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
            double dpiX = pixels.M11 > 0 ? pixels.M11 : 1;
            double dpiY = pixels.M22 > 0 ? pixels.M22 : 1;
            int size = Math.Max(3, Math.Min(20, engine.Settings.IslandDotSize));
            dot.Width = size / dpiX; dot.Height = size / dpiY;
            IslandPlacement placement = engine.Settings.Placement;
            double x = placement == IslandPlacement.Left ? 1 : placement == IslandPlacement.Right ? Width * dpiX - size - 1 : (Width * dpiX - size) / 2;
            double y = placement == IslandPlacement.Top ? 1 : (Height * dpiY - size) / 2;
            Canvas.SetLeft(dot, Math.Round(x) / dpiX); Canvas.SetTop(dot, Math.Round(y) / dpiY);
        }

        private static IntPtr PreventActivation(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x0021) // WM_MOUSEACTIVATE: accept the click without taking focus.
            {
                handled = true;
                return new IntPtr(3); // MA_NOACTIVATE
            }
            return IntPtr.Zero;
        }
    }
}
