using System;
using System.IO;
using FreeIsland;

internal static class AlertAudioTests
{
    private static int checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); ++checks; }
    private static void Reject(Action action, string message) { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } Check(rejected, message); }
    private sealed class Output : IAlertAudioOutput
    {
        public int Starts, Stops, Disposals; public bool Fail; public double Volume; public string Path; public Action Failed, Ended;
        public void Play(string path, double volume, Action failed, Action ended) { ++Starts; Path = path; Volume = volume; Failed = failed; Ended = ended; if (Fail) throw new InvalidOperationException("fixture decoder failure"); }
        public void Stop() { ++Stops; }
        public void SetVolume(double volume) { Volume = volume; }
        public void Dispose() { ++Disposals; }
    }
    [STAThread] private static int Main(string[] args)
    {
        try {
            if (args.Length != 1) throw new Exception("Provide isolated workspace fixture directory");
            string root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
            string wav = Path.Combine(root, "课堂铃声.wav"), mp3 = Path.Combine(root, "提醒音频.MP3"), other = Path.Combine(root, "not-audio.exe");
            File.WriteAllText(wav, "fixture, never sent to a real decoder"); File.WriteAllText(mp3, "fixture, never sent to a real decoder"); File.WriteAllText(other, "fixture");
            var output = new Output(); int fallback = 0; DateTime now = new DateTime(2026, 1, 1);
            string settings = Path.Combine(root, Guid.NewGuid().ToString("N"));
            using (var audio = new AlertAudioService(settings, false, output, delegate { ++fallback; }, delegate { return now; }, false)) {
                Check(audio.VolumePercent == 70 && audio.GetPath(AlertSoundKind.Countdown) == "", "Default audio preferences");
                audio.Play(AlertSoundKind.Countdown); Check(fallback == 1 && output.Starts == 0 && !audio.IsPlaying, "Default sound once");
                Reject(delegate { audio.SetPath(AlertSoundKind.Countdown, "https://example.test/a.wav"); }, "URL rejected");
                Reject(delegate { audio.SetPath(AlertSoundKind.Countdown, "\\\\server\\share\\a.wav"); }, "Network path rejected");
                Reject(delegate { audio.SetPath(AlertSoundKind.Countdown, "\\\\?\\C:\\a.wav"); }, "Device path rejected");
                Reject(delegate { audio.SetPath(AlertSoundKind.Countdown, wav + ":evil.mp3"); }, "Alternate data stream rejected");
                Reject(delegate { audio.SetPath(AlertSoundKind.Countdown, other); }, "Executable rejected");
                Reject(delegate { audio.SetPath(AlertSoundKind.Countdown, Path.Combine(root, "missing.wav")); }, "Missing file rejected");
                Reject(delegate { audio.GetPath((AlertSoundKind)99); }, "Invalid kind rejected");
                audio.SetPath(AlertSoundKind.Countdown, wav); audio.SetPath(AlertSoundKind.Reminder, mp3); audio.SetPath(AlertSoundKind.Shutdown, wav); audio.VolumePercent = 42; audio.Save();
                using (var restored = new AlertAudioService(settings, true, new Output(), delegate { throw new Exception("Unexpected sound"); }, delegate { return now; }, false)) {
                    Check(restored.GetPath(AlertSoundKind.Countdown) == wav && restored.GetPath(AlertSoundKind.Reminder) == mp3 && restored.GetPath(AlertSoundKind.Shutdown) == wav && restored.VolumePercent == 42, "Unicode paths, three events and volume survive restart");
                }
                audio.Play(AlertSoundKind.Countdown); Check(output.Starts == 1 && output.Path == wav && output.Volume == .42 && audio.IsPlaying, "Custom countdown uses selected path and volume");
                Action staleFailure = output.Failed, staleEnd = output.Ended;
                audio.Play(AlertSoundKind.Reminder); Check(output.Starts == 2 && output.Path == mp3, "New event replaces previous audio");
                staleFailure(); staleEnd(); Check(audio.IsPlaying && fallback == 1, "Late callbacks cannot stop replacement or beep");
                audio.StopShutdown(); Check(audio.IsPlaying, "Cancel shutdown preserves unrelated reminder");
                now = now.AddSeconds(29); audio.Tick(); Check(audio.IsPlaying, "Ordinary alert remains before cap");
                now = now.AddSeconds(1); audio.Tick(); Check(!audio.IsPlaying, "Ordinary alert stops after 30 seconds");
                audio.Play(AlertSoundKind.Shutdown); now = now.AddSeconds(10); audio.Tick(); Check(!audio.IsPlaying, "Shutdown warning cap is 10 seconds");
                audio.Play(AlertSoundKind.Shutdown); audio.StopShutdown(); Check(!audio.IsPlaying, "Cancel or fire stops shutdown warning immediately");
                audio.Play(AlertSoundKind.Shutdown, true); audio.StopShutdown(); Check(audio.IsPlaying, "Explicit shutdown audio preview is independent of shutdown plan state"); audio.Stop();
                audio.Play(AlertSoundKind.Reminder, true); now = now.AddSeconds(8); audio.Tick(); Check(!audio.IsPlaying, "Preview capped to 8 seconds");
                audio.Play(AlertSoundKind.Reminder); output.Ended(); Check(!audio.IsPlaying, "Completed media is released without loop");
                audio.Play(AlertSoundKind.Reminder); output.Failed(); int once = fallback; output.Failed(); Check(!audio.IsPlaying && fallback == once && audio.LastError.Length > 0, "Asynchronous decoder error falls back only once");
                output.Fail = true; audio.Play(AlertSoundKind.Reminder); Check(fallback == once + 1 && !audio.IsPlaying, "Synchronous decoder error falls back"); output.Fail = false;
                File.Delete(mp3); audio.Play(AlertSoundKind.Reminder); Check(fallback == once + 2 && !audio.IsPlaying && audio.LastError.Length > 0, "Removed file falls back and keeps selection");
                Check(audio.GetPath(AlertSoundKind.Reminder) == mp3, "Missing removable file choice retained");
                audio.Play(AlertSoundKind.Countdown); audio.VolumePercent = 0; int starts = output.Starts; once = fallback; audio.Play(AlertSoundKind.Shutdown); Check(!audio.IsPlaying && starts == output.Starts && once == fallback, "Zero volume stops and mutes both custom and default sounds");
                audio.VolumePercent = 200; Check(audio.VolumePercent == 100 && output.Volume == 1, "High volume clamped"); audio.VolumePercent = -10; Check(audio.VolumePercent == 0, "Negative volume clamped");
                audio.SetPath(AlertSoundKind.Countdown, ""); Check(audio.GetPath(AlertSoundKind.Countdown) == "", "Reset event to default");
            }
            Check(output.Disposals == 1, "Owned output disposed once");
            var safeOutput = new Output(); int safeFallback = 0;
            using (var safeAudio = new AlertAudioService(Path.Combine(root, "safe"), true, safeOutput, delegate { ++safeFallback; }, delegate { return now; }, false)) {
                safeAudio.Play(AlertSoundKind.Countdown); safeAudio.SetPath(AlertSoundKind.Countdown, wav); safeAudio.Play(AlertSoundKind.Countdown, true);
                Check(safeOutput.Starts == 0 && safeFallback == 0 && !safeAudio.IsPlaying, "Safe mode suppresses default, custom and preview audio");
            }
            File.WriteAllText(Path.Combine(settings, "alert-audio.json"), "broken JSON");
            using (var corrupt = new AlertAudioService(settings, true, new Output(), delegate { }, delegate { return now; }, false)) Check(corrupt.VolumePercent == 70 && corrupt.LastError.Length > 0, "Corrupt settings recover defaults");
            File.WriteAllText(Path.Combine(settings, "alert-audio.json"), "{}");
            using (var partial = new AlertAudioService(settings, true, new Output(), delegate { }, delegate { return now; }, false)) Check(partial.VolumePercent == 70 && partial.GetPath(AlertSoundKind.Countdown) == "", "Missing preference members retain defaults");
            Console.WriteLine("PASS: " + checks + " alert audio checks; injected playback only, no real sound."); return 0;
        } catch (Exception error) { Console.Error.WriteLine("FAIL: " + error); return 1; }
    }
}
