using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace FreeIsland
{
    /// <summary>Direct WPF touch dragging for an overlay; never reads or moves the OS cursor.</summary>
    internal static class TouchWindowDrag
    {
        private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
            "State", typeof(DragState), typeof(TouchWindowDrag), new PropertyMetadata(null));

        internal static void Attach(Window window, Func<DependencyObject, bool> accepts,
            Action begin, Action tap, Action<Point> dropped, Action finish)
        {
            if (window == null) throw new ArgumentNullException("window");
            if (window.GetValue(StateProperty) != null)
                throw new InvalidOperationException("TouchWindowDrag is already attached to this window.");
            Stylus.SetIsPressAndHoldEnabled(window, false);
            window.SetValue(StateProperty, new DragState(window, accepts, begin, tap, dropped, finish));
        }

        private sealed class DragState
        {
            private readonly Window window;
            private readonly Func<DependencyObject, bool> accepts;
            private readonly Action begin;
            private readonly Action tap;
            private readonly Action<Point> dropped;
            private readonly Action finish;
            private readonly HashSet<TouchDevice> suppressed = new HashSet<TouchDevice>();
            private TouchDevice active;
            private Point pointerStart;
            private Point windowStart;
            private bool dragging;
            private bool started;

            internal DragState(Window window, Func<DependencyObject, bool> accepts,
                Action begin, Action tap, Action<Point> dropped, Action finish)
            {
                this.window = window;
                this.accepts = accepts;
                this.begin = begin;
                this.tap = tap;
                this.dropped = dropped;
                this.finish = finish;
                // Preview handlers stop handled touch streams from becoming mouse drags.
                window.PreviewTouchDown += Down;
                window.PreviewTouchMove += Move;
                window.PreviewTouchUp += Up;
                window.LostTouchCapture += CaptureLost;
                window.Closed += Closed;
            }

            private void Down(object sender, TouchEventArgs e)
            {
                if (active != null || suppressed.Contains(e.TouchDevice))
                {
                    e.Handled = true;
                    if (e.TouchDevice != active && !suppressed.Contains(e.TouchDevice))
                    {
                        // Keep each additional contact suppressed through its own TouchUp,
                        // including when the original contact has already been released.
                        suppressed.Add(e.TouchDevice);
                        if (!e.TouchDevice.Capture(window, CaptureMode.Element)) suppressed.Remove(e.TouchDevice);
                    }
                    return;
                }
                DependencyObject original = e.OriginalSource as DependencyObject;
                if (accepts != null && !accepts(original)) return;
                e.Handled = true;
                active = e.TouchDevice;
                dragging = false;
                started = true;
                try
                {
                    if (begin != null) begin();
                    if (active == null) return;
                    // Begin may reveal or resize a collapsed handle, so take the reference
                    // position afterward, as a desktop DIP point rather than a local point.
                    pointerStart = AbsolutePoint(e);
                    windowStart = new Point(window.Left, window.Top);
                    if (!active.Capture(window, CaptureMode.Element)) CancelActive(true);
                }
                catch
                {
                    CancelActive(true);
                    throw;
                }
            }

            private void Move(object sender, TouchEventArgs e)
            {
                if (suppressed.Contains(e.TouchDevice)) { e.Handled = true; return; }
                if (active == null) return;
                e.Handled = true;
                if (e.TouchDevice != active) return;
                try { MoveTo(e); }
                catch { CancelActive(true); throw; }
            }

            private void Up(object sender, TouchEventArgs e)
            {
                if (suppressed.Remove(e.TouchDevice))
                {
                    e.Handled = true;
                    if (e.TouchDevice.Captured == window) e.TouchDevice.Capture(null);
                    return;
                }
                if (active == null) return;
                e.Handled = true;
                if (e.TouchDevice != active) return;

                try { MoveTo(e); }
                catch { CancelActive(true); throw; }
                TouchDevice device = active;
                bool wasDragged = dragging;
                Point center = WindowCenter();
                bool mustFinish = ClearActive();
                try
                {
                    // Clear state before release: LostTouchCapture can fire synchronously.
                    if (device.Captured == window) device.Capture(null);
                    if (wasDragged) { if (dropped != null) dropped(center); }
                    else if (tap != null) tap();
                }
                finally { if (mustFinish && finish != null) finish(); }
            }

            private void MoveTo(TouchEventArgs e)
            {
                // As the overlay moves, its local touch coordinate changes by the opposite
                // amount. Adding the current origin reconstructs the same desktop point.
                Vector delta = AbsolutePoint(e) - pointerStart;
                if (!dragging && delta.Length < 5) return;
                dragging = true;
                window.Left = windowStart.X + delta.X;
                window.Top = windowStart.Y + delta.Y;
            }

            private Point AbsolutePoint(TouchEventArgs e)
            {
                Point local = e.GetTouchPoint(window).Position;
                return new Point(window.Left + local.X, window.Top + local.Y);
            }

            private Point WindowCenter()
            {
                double width = double.IsNaN(window.Width) ? window.ActualWidth : window.Width;
                double height = double.IsNaN(window.Height) ? window.ActualHeight : window.Height;
                return new Point(window.Left + width / 2, window.Top + height / 2);
            }

            private void CaptureLost(object sender, TouchEventArgs e)
            {
                // A descendant's capture-loss event also bubbles through the window;
                // only losing the capture held by this overlay ends our interaction.
                if (e.OriginalSource != window) return;
                suppressed.Remove(e.TouchDevice);
                if (e.TouchDevice != active) return;
                e.Handled = true;
                // Capture loss is cancellation, never a tap or a committed docking drop.
                CancelActive(false);
            }

            private bool ClearActive()
            {
                bool mustFinish = started;
                active = null;
                dragging = false;
                started = false;
                return mustFinish;
            }

            private void CancelActive(bool release)
            {
                TouchDevice device = active;
                bool mustFinish = ClearActive();
                try
                {
                    if (release && device != null && device.Captured == window) device.Capture(null);
                }
                finally { if (mustFinish && finish != null) finish(); }
            }

            private void Closed(object sender, EventArgs e)
            {
                try { CancelActive(true); }
                finally
                {
                    // Capture release can mutate the set through CaptureLost.
                    TouchDevice[] remaining = new TouchDevice[suppressed.Count];
                    suppressed.CopyTo(remaining);
                    suppressed.Clear();
                    foreach (TouchDevice device in remaining)
                        if (device.Captured == window) device.Capture(null);
                }
            }
        }
    }
}
