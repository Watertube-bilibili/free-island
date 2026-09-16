using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FreeIsland;

// Owns isolated safe-mode state and test windows. Does not sample the desktop,
// execute the app entry point, change startup entries or issue shutdown commands.
internal static class GlassSettingsUiTests
{
    private static int checks;
    private static string output;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
    }

    private static T Find<T>(DependencyObject node, string name) where T : FrameworkElement
    {
        T match = node as T;
        if (match != null && (name == null || match.Name == name)) return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            match = Find<T>(VisualTreeHelper.GetChild(node, i), name);
            if (match != null) return match;
        }
        return null;
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }

    private static void Click(ControlWindow panel, string name)
    {
        var button = Find<Button>(panel, name);
        Check(button != null && button.IsEnabled, "Available action " + name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void Capture(ControlWindow panel, string name)
    {
        panel.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth), (int)Math.Ceiling(panel.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(panel);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(stream);
    }

    private static void Run(UsageScene scene)
    {
        string state = Path.Combine(output, scene.ToString() + "-state");
        using (var engine = new CoreEngine(state, true))
        {
            engine.Settings.Scene = scene; engine.Settings.GlassMode = 2; engine.SaveSettings();
            LiquidGlass.Configure(engine.Settings);
            int changes = 0, previews = 0;
            engine.Changed += delegate { changes++; };
            var panel = new ControlWindow(engine, delegate { previews++; }, delegate { }, delegate { });
            try
            {
                panel.Navigate("settings"); panel.Show(); Pump(120);
                var refraction = Find<Slider>(panel, "GlassRefractionSlider");
                var transparency = Find<Slider>(panel, "GlassTransparencySlider");
                var highlight = Find<Slider>(panel, "GlassHighlightSlider");
                Check(refraction != null && transparency != null && highlight != null, "All glass controls exist");
                foreach (var slider in new[] { refraction, transparency, highlight })
                {
                    Check(slider.ActualHeight >= 44 && slider.ActualWidth >= 200, "Touch target and useful adjustment width");
                    Check(slider.Minimum == 0 && slider.Maximum == 100 && slider.SmallChange == 1 && slider.IsSnapToTickEnabled, "Integer percent range and keyboard precision");
                    Check(!string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(slider)), "Named accessible slider");
                }
                Check(refraction.Value == 50 && transparency.Value == 65 && highlight.Value == 55, "Recommended values displayed");
                Capture(panel, "settings-" + scene + "-top");

                string before = File.ReadAllText(Path.Combine(state, "state.json"));
                for (int value = 0; value <= 100; value += 10) refraction.Value = value;
                transparency.Value = 24; highlight.Value = 0;
                Check(engine.Settings.GlassRefraction == 100 && engine.Settings.GlassTransparency == 24 && engine.Settings.GlassHighlight == 0, "Settings update while dragging");
                Check(LiquidGlass.Refraction == 2 && Math.Abs(LiquidGlass.Transparency - .24) < .0001 && LiquidGlass.Highlight == 0, "Actual material configuration updates before disk save");
                Check(changes == 0 && File.ReadAllText(Path.Combine(state, "state.json")) == before, "Drag sequence does not write every value");
                Pump(650);
                Check(changes == 1, "Drag sequence persists in a single debounced save");
                using (var restored = new CoreEngine(state, true))
                    Check(restored.Settings.GlassRefraction == 100 && restored.Settings.GlassTransparency == 24 && restored.Settings.GlassHighlight == 0, "Previewed values survive restart");

                Click(panel, "GlassModeLite");
                Check(!refraction.IsEnabled && transparency.IsEnabled && highlight.IsEnabled, "Lite only disables refraction");
                Click(panel, "GlassModeOff");
                Check(!refraction.IsEnabled && !transparency.IsEnabled && !highlight.IsEnabled && !Find<Button>(panel, "ResetGlassParameters").IsEnabled, "Off disables inactive material parameters");
                Check(engine.Settings.GlassRefraction == 100 && engine.Settings.GlassTransparency == 24 && engine.Settings.GlassHighlight == 0, "Off preserves configured values");
                Click(panel, "GlassModeStandard"); Click(panel, "ResetGlassParameters");
                Check(refraction.Value == 50 && transparency.Value == 65 && highlight.Value == 55, "Reset immediately updates all controls");
                Check(engine.Settings.GlassRefraction == 50 && engine.Settings.GlassTransparency == 65 && engine.Settings.GlassHighlight == 55 && LiquidGlass.Refraction == 1 && LiquidGlass.Highlight == 1, "Reset immediately applies and saves recommendation");
                Click(panel, "PreviewGlass"); Check(previews == 1, "Preview button invokes actual island preview callback");

                var scroll = Find<ScrollViewer>(panel, null);
                Check(scroll != null, "Settings remain scrollable");
                refraction.BringIntoView(); Pump(80);
                var offset = refraction.TranslatePoint(new Point(), scroll);
                scroll.ScrollToVerticalOffset(Math.Max(0, scroll.VerticalOffset + offset.Y - 48)); Pump(80);
                Capture(panel, "settings-" + scene + "-parameters");
                foreach (var slider in new[] { refraction, transparency, highlight })
                {
                    var point = slider.TranslatePoint(new Point(), scroll);
                    Check(point.X >= 0 && point.X + slider.ActualWidth <= scroll.ActualWidth, "Parameter does not clip horizontally");
                }

                highlight.Value = 77; panel.Navigate("home");
                using (var restored = new CoreEngine(state, true)) Check(restored.Settings.GlassHighlight == 77, "Navigation flushes pending values");
                panel.Navigate("settings"); panel.UpdateLayout();
                Find<Slider>(panel, "GlassTransparencySlider").Value = 91; panel.Hide();
                using (var restored = new CoreEngine(state, true)) Check(restored.Settings.GlassTransparency == 91, "Hiding controls flushes pending values");
                panel.Show(); panel.UpdateLayout();
                Find<Slider>(panel, "GlassRefractionSlider").Value = 19;
                panel.AllowClose = true; panel.Close(); Pump(600);
                Check(engine.Settings.GlassRefraction == 19, "Closing retains live values for engine shutdown persistence");
            }
            finally { panel.AllowClose = true; panel.Close(); }
        }
        using (var restored = new CoreEngine(state, true)) Check(restored.Settings.GlassRefraction == 19, "App shutdown persistence keeps the last pending adjustment");
    }

    [STAThread]
    private static int Main(string[] args)
    {
        output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "glass-settings-ui");
        Directory.CreateDirectory(output);
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SurfaceStyle.SnapshotMode = true;
            Run(UsageScene.Desktop); Run(UsageScene.Classroom);
            string result = "PASS: " + checks + " glass settings UI checks across desktop and classroom; 4 settings captures. Isolated state only; no desktop capture, installation, startup changes or shutdown.\n";
            File.WriteAllText(Path.Combine(output, "result.txt"), result); Console.Write(result); app.Shutdown(); return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "result.txt"), ex.ToString()); Console.Error.WriteLine(ex); return 1; }
    }
}
