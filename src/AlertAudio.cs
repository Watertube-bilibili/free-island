using System;
using System.IO;
using System.Media;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Media;
using System.Windows.Threading;

namespace FreeIsland
{
    public enum AlertSoundKind { Countdown, Reminder, Shutdown }

    [DataContract] internal sealed class AlertAudioPreferences
    {
        [DataMember] public string Countdown = "";
        [DataMember] public string Reminder = "";
        [DataMember] public string Shutdown = "";
        [DataMember] public int VolumePercent = 70;
        [OnDeserializing] private void Initialize(StreamingContext context) { Countdown = Reminder = Shutdown = ""; VolumePercent = 70; }
    }

    internal interface IAlertAudioOutput : IDisposable
    {
        void Play(string path, double volume, Action failed, Action ended);
        void Stop();
        void SetVolume(double volume);
    }

    // A fresh player per request prevents a late failure from an old file affecting its replacement.
    internal sealed class WpfAlertAudioOutput : IAlertAudioOutput
    {
        private MediaPlayer player;
        public void Play(string path, double volume, Action failed, Action ended)
        {
            Stop();
            var next = new MediaPlayer(); player = next; next.Volume = volume;
            next.MediaFailed += delegate { if (player == next) failed(); };
            next.MediaEnded += delegate { if (player == next) ended(); };
            next.Open(new Uri(path, UriKind.Absolute)); next.Play();
        }
        public void SetVolume(double volume) { if (player != null) player.Volume = volume; }
        public void Stop() { var previous = player; player = null; if (previous != null) { try { previous.Close(); } catch { } } }
        public void Dispose() { Stop(); }
    }

    // UI-thread owned, just like the island. No background polling or repeated sound on timer ticks.
    public sealed class AlertAudioService : IDisposable
    {
        private readonly string settingsFile;
        private readonly bool safe;
        private readonly IAlertAudioOutput output;
        private readonly Action fallback;
        private readonly Func<DateTime> clock;
        private readonly Dispatcher dispatcher;
        private readonly DispatcherTimer deadlineTimer;
        private AlertAudioPreferences preferences = new AlertAudioPreferences();
        private DateTime deadline;
        private AlertSoundKind? active;
        private int generation;
        private bool disposed, previewing;
        public string LastError { get; private set; }
        public bool IsPlaying { get { return active.HasValue; } }

        public AlertAudioService(string dataDirectory, bool safe)
            : this(dataDirectory, safe, new WpfAlertAudioOutput(), delegate { SystemSounds.Asterisk.Play(); }, delegate { return DateTime.MinValue.AddSeconds(System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency); }, true) { }

        internal AlertAudioService(string dataDirectory, bool safe, IAlertAudioOutput output, Action fallback, Func<DateTime> clock, bool automaticTimer)
        {
            if (output == null || fallback == null || clock == null) throw new ArgumentNullException();
            this.settingsFile = Path.Combine(dataDirectory, "alert-audio.json"); this.safe = safe;
            this.output = output; this.fallback = fallback; this.clock = clock;
            dispatcher = Dispatcher.CurrentDispatcher;
            if (automaticTimer) { deadlineTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher); deadlineTimer.Interval = TimeSpan.FromMilliseconds(250); deadlineTimer.Tick += delegate { Tick(); }; }
            Load();
        }

        public int VolumePercent
        {
            get { return preferences.VolumePercent; }
            set { Verify(); preferences.VolumePercent = Math.Max(0, Math.Min(100, value)); if (preferences.VolumePercent == 0) Stop(); else output.SetVolume(preferences.VolumePercent / 100.0); }
        }
        public string GetPath(AlertSoundKind kind)
        {
            switch (kind) { case AlertSoundKind.Countdown: return preferences.Countdown; case AlertSoundKind.Reminder: return preferences.Reminder; case AlertSoundKind.Shutdown: return preferences.Shutdown; default: throw new ArgumentOutOfRangeException("kind"); }
        }
        public void SetPath(AlertSoundKind kind, string path)
        {
            Verify(); string validated = ValidatePath(path, true);
            switch (kind) { case AlertSoundKind.Countdown: preferences.Countdown = validated; break; case AlertSoundKind.Reminder: preferences.Reminder = validated; break; case AlertSoundKind.Shutdown: preferences.Shutdown = validated; break; default: throw new ArgumentOutOfRangeException("kind"); }
            LastError = "";
        }
        internal static string ValidatePath(string path, bool mustExist)
        {
            if (String.IsNullOrEmpty(path)) return "";
            if (path.Length > 259 || path.Length < 4 || !Char.IsLetter(path[0]) || path[1] != ':' || (path[2] != '\\' && path[2] != '/') || path.IndexOf(':', 2) >= 0 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                throw new ArgumentException("请选择本地磁盘中的 WAV 或 MP3 文件。");
            string full = Path.GetFullPath(path), extension = Path.GetExtension(full);
            if (!String.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase) && !String.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("支持 WAV 和 MP3 音频。");
            if (mustExist) { var info = new FileInfo(full); if (!info.Exists || info.Length == 0 || info.Length > 100L * 1024 * 1024) throw new ArgumentException("音频不存在、为空或超过 100 MB，请重新选择。"); }
            return full;
        }
        public void Save()
        {
            Verify(); Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));
            string temporary = settingsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { new DataContractJsonSerializer(typeof(AlertAudioPreferences)).WriteObject(stream, preferences); stream.Flush(true); }
                if (File.Exists(settingsFile)) File.Replace(temporary, settingsFile, null); else File.Move(temporary, settingsFile);
                LastError = "";
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private void Load()
        {
            if (!File.Exists(settingsFile)) return;
            try {
                if (new FileInfo(settingsFile).Length > 65536) throw new InvalidDataException();
                AlertAudioPreferences loaded;
                using (var stream = File.OpenRead(settingsFile)) loaded = (AlertAudioPreferences)new DataContractJsonSerializer(typeof(AlertAudioPreferences)).ReadObject(stream);
                if (loaded == null) throw new InvalidDataException();
                loaded.Countdown = ValidatePath(loaded.Countdown, false); loaded.Reminder = ValidatePath(loaded.Reminder, false); loaded.Shutdown = ValidatePath(loaded.Shutdown, false);
                loaded.VolumePercent = Math.Max(0, Math.Min(100, loaded.VolumePercent)); preferences = loaded;
            } catch { LastError = "提醒音频设置无法读取，已使用默认提示音。"; }
        }
        public void Play(AlertSoundKind kind, bool preview = false)
        {
            Verify(); string path = GetPath(kind); Stop(); LastError = "";
            if (safe || VolumePercent == 0) return;
            if (String.IsNullOrEmpty(path)) { fallback(); return; }
            try { path = ValidatePath(path, true); }
            catch { LastError = "所选音频无法读取，已使用默认提示音。"; fallback(); return; }
            int request = generation; active = kind; previewing = preview;
            deadline = clock().AddSeconds(preview ? 8 : kind == AlertSoundKind.Shutdown ? 10 : 30);
            if (deadlineTimer != null) deadlineTimer.Start();
            try { output.Play(path, VolumePercent / 100.0, delegate { Failed(request); }, delegate { if (request == generation && !disposed) Stop(); }); }
            catch { Failed(request); }
        }
        private void Failed(int request)
        {
            if (request != generation || disposed) return;
            Stop(); LastError = "音频格式无法播放，已使用默认提示音。"; fallback();
        }
        internal void Tick() { Verify(); if (active.HasValue && clock() >= deadline) Stop(); }
        public void StopShutdown() { Verify(); if (active == AlertSoundKind.Shutdown && !previewing) Stop(); }
        public void Stop() { dispatcher.VerifyAccess(); ++generation; active = null; previewing = false; if (deadlineTimer != null) deadlineTimer.Stop(); output.Stop(); }
        private void Verify() { dispatcher.VerifyAccess(); if (disposed) throw new ObjectDisposedException("AlertAudioService"); }
        public void Dispose() { dispatcher.VerifyAccess(); if (disposed) return; Stop(); disposed = true; output.Dispose(); }
    }
}
