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
        private readonly LiquidGlassSurface material;
        private readonly TaskThumbnail thumbnail;
        private Rect lastWork;
        private int activeCount, activeSize;
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
            material = new LiquidGlassSurface { Name = "IslandHandleGlassMaterial", Orb = true, Compact = true, IsHitTestVisible = false };
            // A nonzero alpha keeps the larger target clickable in a layered window.
            thumbnail = new TaskThumbnail { Name = "IslandTaskThumbnail", IsHitTestVisible = false };
            var touchArea = new Canvas { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Cursor = Cursors.Hand, ToolTip = "点击展开全部任务 · 拖动调整位置" }; touchArea.Children.Add(dot); touchArea.Children.Add(material); touchArea.Children.Add(thumbnail); Content = touchArea;
            ApplyMaterial();
            System.Windows.Automation.AutomationProperties.SetName(touchArea, "点击展开灵动岛，拖动调整位置");
            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                e.Handled = true;
                SurfaceStyle.StopPosition(this);
                pressCursor = Native.Cursor(this);
                pressOrigin = new Point(Left, Top);
                dragged = false;
                pointerDown = CaptureMouse();
                material.Pressed = pointerDown;
            };
            MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!pointerDown) return;
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    pointerDown = false;
                    EndMaterialPress();
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
                EndMaterialPress();
                ReleaseMouseCapture();
                if (dragged)
                {
                    if (dropped != null) dropped(new Point(Left + Width / 2, Top + Height / 2));
                }
                else if (wake != null) wake();
            };
            LostMouseCapture += delegate { pointerDown = false; EndMaterialPress(); };
            IsVisibleChanged += delegate { if (!IsVisible) EndMaterialPress(); };
            TouchWindowDrag.Attach(this, null,
                delegate { SurfaceStyle.StopPosition(this); pointerDown = true; material.Pressed = true; },
                delegate { if (wake != null) wake(); },
                delegate(Point point) { pointerDown = false; EndMaterialPress(); if (dropped != null) dropped(point); },
                delegate { pointerDown = false; EndMaterialPress(); });
            SourceInitialized += delegate
            {
                var source = PresentationSource.FromVisual(this) as HwndSource;
                if (source != null) source.AddHook(PreventActivation);
                UpdateDot();
            };
        }

        private void EndMaterialPress() { material.Pressed = false; material.Pointer(null); }

        public void ApplyMaterial()
        {
            int mode = Math.Max(0, Math.Min(2, engine.Settings.GlassMode));
            material.Mode = mode;
            material.Visibility = mode == 0 ? Visibility.Collapsed : Visibility.Visible;
            dot.Visibility = mode == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public void ShowAt(Rect work)
        {
            if (pointerDown) return;
            lastWork = work;
            var tasks = engine.GetIslandTasks(); activeCount = tasks.Count;
            activeSize = engine.Settings.ActiveIslandSize == 0 ? engine.Settings.Scene == UsageScene.Classroom ? 48 : 36 : engine.Settings.ActiveIslandSize;
            thumbnail.Update(tasks.Count > 0 ? tasks[0] : null, tasks.Count);
            SurfaceStyle.StopPosition(this);
            IslandPlacement placement = engine.Settings.Placement;
            bool top = placement == IslandPlacement.Top;
            double anchor = engine.Settings.IslandAnchor;
            if (double.IsNaN(anchor) || double.IsInfinity(anchor)) anchor = .5;
            anchor = Math.Max(0, Math.Min(1, anchor));
            Width = SceneMetrics.HandleWidth(engine, placement);
            Height = SceneMetrics.HandleHeight(engine, placement);
            if (activeCount > 0)
            {
                Matrix device = DeviceScale();
                Width = Math.Max(Width, (activeSize + 10) / device.M11);
                Height = Math.Max(Height, (activeSize + 10) / device.M22);
            }
            double left = top ? work.Left + work.Width * anchor - Width / 2
                : placement == IslandPlacement.Left ? work.Left : work.Right - Width;
            double y = top ? work.Top : work.Top + work.Height * anchor - Height / 2;
            Left = Math.Max(work.Left, Math.Min(left, work.Right - Width));
            Top = Math.Max(work.Top, Math.Min(y, work.Bottom - Height));
            UpdateDot();
            if (!IsVisible) Show();
        }

        public void RefreshTasks()
        {
            if (!IsVisible || pointerDown) return;
            var tasks = engine.GetIslandTasks();
            int size = engine.Settings.ActiveIslandSize == 0 ? engine.Settings.Scene == UsageScene.Classroom ? 48 : 36 : engine.Settings.ActiveIslandSize;
            if (tasks.Count != activeCount || size != activeSize) { ShowAt(lastWork); return; }
            thumbnail.Update(tasks.Count > 0 ? tasks[0] : null, tasks.Count);
        }

        private Matrix DeviceScale()
        {
            var source = PresentationSource.FromVisual(this);
            return source == null || source.CompositionTarget == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
        }

        private void UpdateDot()
        {
            // The setting is a physical-pixel diameter, independent of scene or Windows scaling.
            var source = PresentationSource.FromVisual(this);
            Matrix pixels = source == null || source.CompositionTarget == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
            double dpiX = pixels.M11 > 0 ? pixels.M11 : 1;
            double dpiY = pixels.M22 > 0 ? pixels.M22 : 1;
            int size = activeCount > 0 ? activeSize : Math.Max(3, Math.Min(20, engine.Settings.IslandDotSize));
            dot.Width = size / dpiX; dot.Height = size / dpiY;
            material.Width = dot.Width; material.Height = dot.Height;
            IslandPlacement placement = engine.Settings.Placement;
            double x = placement == IslandPlacement.Left ? 1 : placement == IslandPlacement.Right ? Width * dpiX - size - 1 : (Width * dpiX - size) / 2;
            double y = placement == IslandPlacement.Top ? 1 : (Height * dpiY - size) / 2;
            if (activeCount > 0) { x = (Width * dpiX - size) / 2; y = (Height * dpiY - size) / 2; }
            Canvas.SetLeft(dot, Math.Round(x) / dpiX); Canvas.SetTop(dot, Math.Round(y) / dpiY);
            Canvas.SetLeft(material, Canvas.GetLeft(dot)); Canvas.SetTop(material, Canvas.GetTop(dot));
            thumbnail.Width = dot.Width; thumbnail.Height = dot.Height;
            Canvas.SetLeft(thumbnail, Canvas.GetLeft(dot)); Canvas.SetTop(thumbnail, Canvas.GetTop(dot));
            thumbnail.Visibility = activeCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            ApplyMaterial();
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
