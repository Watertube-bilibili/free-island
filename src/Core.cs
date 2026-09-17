using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;

namespace FreeIsland
{
    [DataContract]
    public enum IslandPlacement
    {
        [EnumMember] Top,
        [EnumMember] Left,
        [EnumMember] Right
    }

    [DataContract]
    public enum UsageScene
    {
        [EnumMember] Desktop,
        [EnumMember] Classroom
    }

    [DataContract]
    public sealed class AppSettings
    {
        [DataMember] public bool AutoStart { get; set; }
        [DataMember] public bool SoundEnabled { get; set; }
        [DataMember] public bool EdgeHide { get; set; }
        [DataMember] public IslandPlacement Placement { get; set; }
        [DataMember] public double IslandAnchor { get; set; }
        [DataMember] public string IslandScreen { get; set; }
        [DataMember] public int IslandDotSize { get; set; }
        [DataMember] public int ActiveIslandSize { get; set; }
        [DataMember] public int ActiveIslandSizeVersion { get; set; }
        [DataMember] public bool AutoUpdate { get; set; }
        [DataMember] public bool ShutdownRecurringEnabled { get; set; }
        [DataMember] public int ShutdownRepeatDays { get; set; }
        [DataMember] public int ShutdownRepeatHour { get; set; }
        [DataMember] public int ShutdownRepeatMinute { get; set; }
        private int islandDotPercent;
        private bool hasSavedDotPercent;
        [DataMember] public int IslandDotPercent
        {
            get { return islandDotPercent; }
            set { islandDotPercent = value; hasSavedDotPercent = true; }
        }
        [DataMember] public int GlassMode { get; set; }
        [DataMember] public int GlassRefraction { get; set; }
        [DataMember] public int GlassTransparency { get; set; }
        [DataMember] public int GlassHighlight { get; set; }
        [DataMember] public double IslandScale { get; set; }
        [DataMember] public UsageScene Scene { get; set; }
        [DataMember] public bool SceneSelected { get; set; }
        public double BallX { get; set; }
        public double BallY { get; set; }

        // JSON has no NaN literal. Unpositioned coordinates are stored as null.
        [DataMember(Name = "BallX", EmitDefaultValue = false)]
        private double? SavedBallX
        {
            get { return double.IsNaN(BallX) || double.IsInfinity(BallX) ? (double?)null : BallX; }
            set { BallX = value ?? double.NaN; }
        }
        [DataMember(Name = "BallY", EmitDefaultValue = false)]
        private double? SavedBallY
        {
            get { return double.IsNaN(BallY) || double.IsInfinity(BallY) ? (double?)null : BallY; }
            set { BallY = value ?? double.NaN; }
        }

        public AppSettings() { SetDefaults(); }
        [OnDeserializing] private void BeforeRead(StreamingContext context)
        {
            SetDefaults();
            ActiveIslandSizeVersion = 0;
            IslandDotSize = 4; // An absent old field used the former four-pixel default.
        }
        [OnDeserialized] private void AfterRead(StreamingContext context)
        {
            if (ActiveIslandSizeVersion < 2 && ActiveIslandSize >= 30 && ActiveIslandSize <= 50) ActiveIslandSize = 0;
            ActiveIslandSizeVersion = 2;
            if (!hasSavedDotPercent)
                IslandDotPercent = IslandDotSize >= 3 && IslandDotSize <= 20 && IslandDotSize != 4
                    ? ((IslandDotSize - 3) * 100 + 8) / 17 : 20;
        }
        private void SetDefaults()
        {
            AutoStart = true;
            SoundEnabled = true;
            EdgeHide = true;
            Placement = IslandPlacement.Top;
            IslandAnchor = 0.5;
            IslandScreen = null;
            islandDotPercent = 20;
            hasSavedDotPercent = false;
            IslandDotSize = 6;
            ActiveIslandSize = 0;
            ActiveIslandSizeVersion = 2;
            AutoUpdate = true;
            ShutdownRecurringEnabled = false;
            ShutdownRepeatDays = 31;
            ShutdownRepeatHour = 17;
            ShutdownRepeatMinute = 0;
            GlassMode = 1;
            GlassRefraction = 50;
            GlassTransparency = 65;
            GlassHighlight = 55;
            IslandScale = 1.0;
            Scene = UsageScene.Classroom;
            SceneSelected = false;
            BallX = double.NaN;
            BallY = double.NaN;
        }
    }

    [DataContract]
    public sealed class ReminderItem
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Title { get; set; }
        [DataMember] public DateTime DueAt { get; set; }
        [DataMember] public bool Daily { get; set; }
        [DataMember] public bool Completed { get; set; }
    }

    public sealed class IslandNoticeEventArgs : EventArgs
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public string Kind { get; set; }
        public bool Urgent { get; set; }
    }

    /// <summary>A stable snapshot of one task shown in the collapsed island or task stack.</summary>
    public sealed class IslandTaskInfo
    {
        public string Id { get; internal set; }
        public string Kind { get; internal set; }
        public string Title { get; internal set; }
        public TimeSpan Time { get; internal set; }
        public bool Running { get; internal set; }
        // Remaining fraction for deadlines; -1 means an open-ended stopwatch.
        public double Progress { get; internal set; }
    }

    [DataContract]
    internal sealed class SavedState
    {
        [DataMember] public int Version { get; set; }
        [DataMember] public AppSettings Settings { get; set; }
        [DataMember] public List<ReminderItem> Reminders { get; set; }
        [DataMember] public bool CountdownActive { get; set; }
        [DataMember] public bool CountdownRunning { get; set; }
        [DataMember] public DateTime? CountdownDeadlineUtc { get; set; }
        [DataMember] public long PausedCountdownTicks { get; set; }
        [DataMember] public long CountdownDurationTicks { get; set; }
    }

    /// <summary>UI-thread domain service. Tick regularly; deadlines use UTC and survive sleep.</summary>
    public sealed class CoreEngine : IDisposable
    {
        private readonly string statePath;
        private readonly Func<DateTime> clock;
        private readonly Action shutdownAction;
        private readonly System.Diagnostics.Stopwatch monotonicStopwatch;
        private readonly List<ReminderItem> reminders = new List<ReminderItem>();
        private readonly Queue<IslandNoticeEventArgs> pendingNotices = new Queue<IslandNoticeEventArgs>();
        private DateTime stopwatchStartedUtc;
        private TimeSpan stopwatchAccumulated;
        private DateTime? countdownDeadlineUtc;
        private TimeSpan pausedCountdown;
        private DateTime? shutdownUtc;
        private bool shutdownWarningSent;
        private DateTime lastTickUtc;
        private bool disposed;
        private static readonly TimeSpan MaximumCountdown = TimeSpan.FromDays(7);

        public CoreEngine(string dataDirectory, bool safeMode)
            : this(dataDirectory, safeMode, delegate { return DateTime.UtcNow; }, null)
        {
            // Stopwatch timing must not jump when Windows corrects the wall clock.
            // The injected-clock overload remains deterministic for unit tests.
            monotonicStopwatch = new System.Diagnostics.Stopwatch();
        }

        // The optional clock/action make deadline behavior testable without an OS shutdown.
        public CoreEngine(string dataDirectory, bool safeMode, Func<DateTime> now, Action shutdownAction)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentException("数据目录不能为空。", "dataDirectory");
            if (now == null) throw new ArgumentNullException("now");
            Directory.CreateDirectory(dataDirectory);
            statePath = Path.Combine(Path.GetFullPath(dataDirectory), "state.json");
            IsSafeMode = safeMode;
            clock = now;
            this.shutdownAction = shutdownAction ?? ShutdownWindows;
            Settings = new AppSettings();
            lastTickUtc = UtcNow();
            LoadState();
        }

        public AppSettings Settings { get; private set; }
        public IList<ReminderItem> Reminders { get { return reminders.AsReadOnly(); } }
        public bool IsSafeMode { get; private set; }
        public bool StopwatchRunning { get; private set; }
        public bool StopwatchActive { get; private set; }
        public TimeSpan StopwatchElapsed
        {
            get
            {
                if (monotonicStopwatch != null) return monotonicStopwatch.Elapsed;
                return stopwatchAccumulated + (StopwatchRunning ? Positive(UtcNow() - stopwatchStartedUtc) : TimeSpan.Zero);
            }
        }
        public bool CountdownActive { get; private set; }
        public bool CountdownRunning { get; private set; }
        public TimeSpan CountdownDuration { get; private set; }
        public TimeSpan CountdownRemaining
        {
            get
            {
                if (!CountdownActive) return TimeSpan.Zero;
                return CountdownRunning && countdownDeadlineUtc.HasValue
                    ? Positive(countdownDeadlineUtc.Value - UtcNow()) : pausedCountdown;
            }
        }
        public DateTime? ShutdownAt { get { return shutdownUtc.HasValue ? shutdownUtc.Value.ToLocalTime() : (DateTime?)null; } }
        public TimeSpan? ShutdownRemaining { get { return shutdownUtc.HasValue ? Positive(shutdownUtc.Value - UtcNow()) : (TimeSpan?)null; } }
        public bool ShutdownRecurringEnabled { get { return Settings.ShutdownRecurringEnabled; } }
        public int ShutdownRepeatDays { get { return Settings.ShutdownRepeatDays; } }
        public int ShutdownRepeatHour { get { return Settings.ShutdownRepeatHour; } }
        public int ShutdownRepeatMinute { get { return Settings.ShutdownRepeatMinute; } }
        public bool ShutdownBlocksAutoUpdate { get { return shutdownUtc.HasValue && (!ShutdownRecurringEnabled || shutdownUtc.Value - UtcNow() <= TimeSpan.FromMinutes(5)); } }

        public static DateTime NextRecurringShutdownUtc(DateTime nowUtc, int hour, int minute, int weekdayMask, TimeZoneInfo zone)
        {
            if (hour < 0 || hour > 23 || minute < 0 || minute > 59 || weekdayMask < 1 || weekdayMask > 127)
                throw new ArgumentException("请选择有效的关机时间和至少一个星期。");
            if (zone == null) throw new ArgumentNullException("zone");
            nowUtc = ToUtc(nowUtc);
            DateTime today = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone).Date;
            for (int day = 0; day <= 14; day++)
            {
                DateTime candidate = DateTime.SpecifyKind(today.AddDays(day).AddHours(hour).AddMinutes(minute), DateTimeKind.Unspecified);
                int weekday = ((int)candidate.DayOfWeek + 6) % 7;
                if ((weekdayMask & (1 << weekday)) == 0 || zone.IsInvalidTime(candidate)) continue;
                DateTime utc;
                if (zone.IsAmbiguousTime(candidate))
                {
                    TimeSpan[] offsets = zone.GetAmbiguousTimeOffsets(candidate);
                    TimeSpan firstOffset = offsets[0] > offsets[1] ? offsets[0] : offsets[1];
                    utc = DateTime.SpecifyKind(candidate - firstOffset, DateTimeKind.Utc);
                }
                else utc = TimeZoneInfo.ConvertTimeToUtc(candidate, zone);
                if (utc > nowUtc) return utc;
            }
            throw new InvalidOperationException("无法计算下次重复关机时间。");
        }
        private void ArmRecurringShutdown(DateTime now)
        {
            shutdownWarningSent = false;
            shutdownUtc = NextRecurringShutdownUtc(now.AddSeconds(10).AddTicks(-1), ShutdownRepeatHour, ShutdownRepeatMinute, ShutdownRepeatDays, TimeZoneInfo.Local);
        }
        public void SetRecurringShutdown(int hour, int minute, int weekdayMask)
        {
            EnsureNotDisposed();
            DateTime now = UtcNow();
            DateTime next = NextRecurringShutdownUtc(now.AddSeconds(10).AddTicks(-1), hour, minute, weekdayMask, TimeZoneInfo.Local);
            Settings.ShutdownRecurringEnabled = true; Settings.ShutdownRepeatHour = hour;
            Settings.ShutdownRepeatMinute = minute; Settings.ShutdownRepeatDays = weekdayMask;
            shutdownUtc = next; shutdownWarningSent = false; lastTickUtc = now;
            SaveState(); OnChanged();
        }

        public IList<IslandTaskInfo> GetIslandTasks()
        {
            DateTime now = UtcNow();
            List<IslandTaskInfo> tasks = new List<IslandTaskInfo>(3);
            if (shutdownUtc.HasValue)
            {
                TimeSpan left = shutdownUtc.Value - now;
                if (left > TimeSpan.Zero && left <= TimeSpan.FromSeconds(10))
                    tasks.Add(new IslandTaskInfo { Id = "shutdown", Kind = "shutdown", Title = "即将关机", Time = left, Running = true, Progress = left.TotalSeconds / 10.0 });
            }
            TimeSpan countdownLeft = CountdownRunning && countdownDeadlineUtc.HasValue
                ? Positive(countdownDeadlineUtc.Value - now) : pausedCountdown;
            if (CountdownActive && countdownLeft > TimeSpan.Zero)
                tasks.Add(new IslandTaskInfo { Id = "countdown", Kind = "countdown", Title = "倒计时", Time = countdownLeft, Running = CountdownRunning,
                    Progress = CountdownDuration > TimeSpan.Zero ? Math.Min(1, countdownLeft.TotalSeconds / CountdownDuration.TotalSeconds) : 1 });
            if (StopwatchActive)
                tasks.Add(new IslandTaskInfo { Id = "stopwatch", Kind = "stopwatch", Title = "正计时", Time = StopwatchElapsed, Running = StopwatchRunning, Progress = -1 });
            return tasks.AsReadOnly();
        }

        public event EventHandler Changed;
        public event EventHandler<IslandNoticeEventArgs> Notice;

        public void ToggleStopwatch()
        {
            EnsureNotDisposed();
            StopwatchActive = true;
            if (StopwatchRunning)
            {
                if (monotonicStopwatch != null) monotonicStopwatch.Stop();
                stopwatchAccumulated += Positive(UtcNow() - stopwatchStartedUtc);
                StopwatchRunning = false;
            }
            else
            {
                if (monotonicStopwatch != null) monotonicStopwatch.Start();
                stopwatchStartedUtc = UtcNow();
                StopwatchRunning = true;
            }
            OnChanged();
        }

        public void ResetStopwatch()
        {
            EnsureNotDisposed();
            stopwatchAccumulated = TimeSpan.Zero;
            if (monotonicStopwatch != null) monotonicStopwatch.Reset();
            StopwatchRunning = false;
            StopwatchActive = false;
            OnChanged();
        }

        public void StartCountdown(TimeSpan duration)
        {
            EnsureNotDisposed();
            if (duration <= TimeSpan.Zero || duration > MaximumCountdown)
                throw new ArgumentException("倒计时时长须大于 0，且不超过 7 天。", "duration");
            countdownDeadlineUtc = UtcNow().Add(duration);
            pausedCountdown = duration;
            CountdownDuration = duration;
            CountdownActive = true;
            CountdownRunning = true;
            SaveState();
            OnChanged();
        }

        public void PauseResumeCountdown()
        {
            EnsureNotDisposed();
            if (!CountdownActive) return;
            if (CountdownRunning)
            {
                pausedCountdown = CountdownRemaining;
                if (pausedCountdown <= TimeSpan.Zero) { FinishCountdown(); return; }
                CountdownRunning = false;
                countdownDeadlineUtc = null;
            }
            else
            {
                countdownDeadlineUtc = UtcNow().Add(pausedCountdown);
                CountdownRunning = true;
            }
            SaveState();
            OnChanged();
        }

        public void CancelCountdown()
        {
            EnsureNotDisposed();
            ClearCountdown();
            SaveState();
            OnChanged();
        }

        public void AddReminder(string title, DateTime due, bool daily)
        {
            EnsureNotDisposed();
            title = (title ?? string.Empty).Trim();
            if (title.Length == 0 || title.Length > 120)
                throw new ArgumentException("提醒标题须为 1 至 120 个字符。", "title");
            DateTime dueUtc = ToUtc(due);
            if (dueUtc <= UtcNow()) throw new ArgumentException("请选择未来的提醒时间。", "due");
            reminders.Add(new ReminderItem
            {
                Id = Guid.NewGuid().ToString("N"), Title = title,
                DueAt = dueUtc.ToLocalTime(), Daily = daily, Completed = false
            });
            SaveState();
            OnChanged();
        }

        public void RemoveReminder(string id)
        {
            EnsureNotDisposed();
            if (reminders.RemoveAll(delegate(ReminderItem item) { return item.Id == id; }) == 0) return;
            SaveState();
            OnChanged();
        }

        public void ScheduleShutdown(DateTime localDue)
        {
            EnsureNotDisposed();
            DateTime due = ToUtc(localDue);
            if (due <= UtcNow()) throw new ArgumentException("请选择未来的关机时间。", "localDue");
            Settings.ShutdownRecurringEnabled = false;
            shutdownUtc = due;
            shutdownWarningSent = false;
            lastTickUtc = UtcNow();
            SaveState();
            OnChanged();
        }

        public void CancelShutdown()
        {
            EnsureNotDisposed();
            shutdownUtc = null;
            shutdownWarningSent = false;
            Settings.ShutdownRecurringEnabled = false;
            SaveState();
            OnChanged();
        }

        public void Tick()
        {
            if (disposed) return;
            DateTime now = UtcNow();
            TimeSpan gap = Positive(now - lastTickUtc);
            lastTickUtc = now;
            bool changed = StopwatchRunning || CountdownRunning ||
                (shutdownUtc.HasValue && shutdownUtc.Value - now <= TimeSpan.FromSeconds(10));

            while (pendingNotices.Count > 0) Publish(pendingNotices.Dequeue());

            if (CountdownActive && CountdownRunning && countdownDeadlineUtc.HasValue && now >= countdownDeadlineUtc.Value)
            {
                FinishCountdown();
                changed = true;
            }

            List<IslandNoticeEventArgs> dueNotices = new List<IslandNoticeEventArgs>();
            bool remindersChanged = false;
            foreach (ReminderItem item in reminders)
            {
                if (item.Completed || ToUtc(item.DueAt) > now) continue;
                dueNotices.Add(NewNotice("日程提醒", item.Title, "reminder", true));
                if (item.Daily)
                {
                    DateTime next = item.DueAt;
                    DateTime localNow = now.ToLocalTime();
                    int days = Math.Max(1, (localNow.Date - next.Date).Days);
                    next = next.AddDays(days);
                    if (ToUtc(next) <= now) next = next.AddDays(1);
                    item.DueAt = next;
                }
                else item.Completed = true;
                remindersChanged = true;
            }
            if (remindersChanged)
            {
                // Persist acknowledgement before notifying so a restart does not re-fire it.
                SaveState();
                foreach (IslandNoticeEventArgs notice in dueNotices) Publish(notice);
                changed = true;
            }

            if (shutdownUtc.HasValue)
            {
                TimeSpan left = shutdownUtc.Value - now;
                if (ShutdownRecurringEnabled && left > TimeSpan.Zero && left < TimeSpan.FromSeconds(10) && gap > TimeSpan.FromSeconds(2))
                {
                    ArmRecurringShutdown(now); SaveState();
                    Publish(NewNotice("已跳过本次关机", "未能完整预留最后 10 秒提醒，本次已跳过；重复计划继续保留。", "shutdown", true));
                    OnChanged(); return;
                }
                if (left <= TimeSpan.Zero)
                {
                    bool wasWarned = shutdownWarningSent;
                    bool repeating = ShutdownRecurringEnabled;
                    shutdownUtc = null;
                    shutdownWarningSent = false;
                    if (repeating) { ArmRecurringShutdown(now); SaveState(); }
                    if (gap > TimeSpan.FromMinutes(2))
                    {
                        Publish(NewNotice(repeating ? "已跳过本次关机" : "已取消过期关机", repeating ? "电脑休眠或暂停期间错过了关机时间，本次已跳过；重复计划继续保留。" : "电脑休眠或暂停期间错过了关机时间，计划已取消。", "shutdown", true));
                    }
                    else if (!wasWarned)
                    {
                        Publish(NewNotice(repeating ? "已跳过本次关机" : "已取消未预警关机", repeating ? "本次未能提前显示关机提醒，已跳过；重复计划继续保留。" : "关机时间已过，未能提前显示提醒，计划已取消。", "shutdown", true));
                    }
                    else if (IsSafeMode)
                    {
                        Publish(NewNotice("已模拟定时关机", "安全演示模式不会关闭电脑。", "shutdown", true));
                    }
                    else
                    {
                        try { shutdownAction(); }
                        catch (Exception ex)
                        {
                            Publish(NewNotice("关机未执行", "Windows 未能执行关机：" + ex.Message, "shutdown", true));
                        }
                    }
                    changed = true;
                }
                else if (left <= TimeSpan.FromSeconds(10) && !shutdownWarningSent)
                {
                    shutdownWarningSent = true;
                    Publish(NewNotice("即将自动关机", "将在 " + Math.Ceiling(left.TotalSeconds).ToString(CultureInfo.InvariantCulture) + " 秒内关机，点击取消可停止计划。", "shutdown", true));
                }
            }
            if (changed) OnChanged();
        }

        public void SaveSettings()
        {
            EnsureNotDisposed();
            NormalizeSettings();
            SaveState();
            OnChanged();
        }

        public void Dispose()
        {
            if (disposed) return;
            SaveState();
            shutdownUtc = null;
            disposed = true;
        }

        private void FinishCountdown()
        {
            ClearCountdown();
            SaveState();
            Publish(NewNotice("倒计时结束", "时间到了，休息一下或开始下一件事吧。", "countdown", true));
            OnChanged();
        }

        private void ClearCountdown()
        {
            CountdownActive = false;
            CountdownRunning = false;
            countdownDeadlineUtc = null;
            pausedCountdown = TimeSpan.Zero;
            CountdownDuration = TimeSpan.Zero;
        }

        private void SaveState()
        {
            NormalizeSettings();
            SavedState state = new SavedState
            {
                Version = 1, Settings = Settings, Reminders = reminders,
                CountdownActive = CountdownActive, CountdownRunning = CountdownRunning,
                CountdownDeadlineUtc = countdownDeadlineUtc, PausedCountdownTicks = pausedCountdown.Ticks,
                CountdownDurationTicks = CountdownDuration.Ticks
            };
            string temporary = statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(SavedState)).WriteObject(stream, state);
                    stream.Flush(true);
                }
                if (File.Exists(statePath)) File.Replace(temporary, statePath, statePath + ".bak", true);
                else File.Move(temporary, statePath);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private void LoadState()
        {
            SavedState state;
            if (!TryReadState(statePath, out state))
            {
                if (!TryReadState(statePath + ".bak", out state))
                {
                    if (File.Exists(statePath)) pendingNotices.Enqueue(NewNotice("设置已恢复默认", "保存的数据无法读取，已使用默认设置。", "info", false));
                    return;
                }
                pendingNotices.Enqueue(NewNotice("已恢复备份", "上次保存的数据无法读取，已恢复最近一份可用备份。", "info", false));
            }
            Settings = state.Settings ?? new AppSettings();
            NormalizeSettings();
            if (ShutdownRecurringEnabled) ArmRecurringShutdown(UtcNow());
            if (state.Reminders != null)
            {
                HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (ReminderItem item in state.Reminders)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.Title) || item.DueAt == DateTime.MinValue) continue;
                    if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id))
                    {
                        item.Id = Guid.NewGuid().ToString("N");
                        ids.Add(item.Id);
                    }
                    item.DueAt = ToUtc(item.DueAt).ToLocalTime();
                    reminders.Add(item);
                }
            }
            if (state.CountdownActive)
            {
                if (state.CountdownRunning && state.CountdownDeadlineUtc.HasValue)
                {
                    DateTime deadline = ToUtc(state.CountdownDeadlineUtc.Value);
                    if (deadline - UtcNow() <= MaximumCountdown)
                    {
                        CountdownActive = true;
                        CountdownRunning = true;
                        countdownDeadlineUtc = deadline;
                    }
                }
                else if (!state.CountdownRunning && state.PausedCountdownTicks > 0 && state.PausedCountdownTicks <= MaximumCountdown.Ticks)
                {
                    CountdownActive = true;
                    CountdownRunning = false;
                    pausedCountdown = TimeSpan.FromTicks(state.PausedCountdownTicks);
                }
                if (CountdownActive)
                {
                    // Old files kept the original duration until the first pause. Thereafter
                    // the remaining duration is the best available migration baseline.
                    long baseline = state.CountdownDurationTicks > 0 && state.CountdownDurationTicks <= MaximumCountdown.Ticks
                        ? state.CountdownDurationTicks
                        : (state.PausedCountdownTicks > 0 && state.PausedCountdownTicks <= MaximumCountdown.Ticks ? state.PausedCountdownTicks : 0);
                    CountdownDuration = TimeSpan.FromTicks(Math.Max(baseline, CountdownRemaining.Ticks));
                }
            }
        }

        private static bool TryReadState(string path, out SavedState state)
        {
            state = null;
            if (!File.Exists(path)) return false;
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    state = new DataContractJsonSerializer(typeof(SavedState)).ReadObject(stream) as SavedState;
                return state != null && state.Version == 1;
            }
            catch (IOException) { return false; }
            catch (SerializationException) { return false; }
            catch (ArgumentException) { return false; }
            catch (FormatException) { return false; }
        }

        private void NormalizeSettings()
        {
            if (Settings.ShutdownRepeatHour < 0 || Settings.ShutdownRepeatHour > 23 || Settings.ShutdownRepeatMinute < 0 || Settings.ShutdownRepeatMinute > 59 || Settings.ShutdownRepeatDays < 1 || Settings.ShutdownRepeatDays > 127)
            {
                Settings.ShutdownRecurringEnabled = false; Settings.ShutdownRepeatHour = 17;
                Settings.ShutdownRepeatMinute = 0; Settings.ShutdownRepeatDays = 31;
            }
            if (!Enum.IsDefined(typeof(IslandPlacement), Settings.Placement)) Settings.Placement = IslandPlacement.Top;
            if (!Enum.IsDefined(typeof(UsageScene), Settings.Scene)) Settings.Scene = UsageScene.Classroom;
            if (double.IsNaN(Settings.IslandAnchor) || double.IsInfinity(Settings.IslandAnchor) || Settings.IslandAnchor < 0 || Settings.IslandAnchor > 1)
                Settings.IslandAnchor = 0.5;
            if (Settings.IslandDotPercent < 0 || Settings.IslandDotPercent > 100) Settings.IslandDotPercent = 20;
            Settings.IslandDotSize = 3 + (17 * Settings.IslandDotPercent + 50) / 100;
            if (Settings.ActiveIslandSize < 40 || Settings.ActiveIslandSize > 160) Settings.ActiveIslandSize = 0;
            Settings.ActiveIslandSizeVersion = 2;
            if (Settings.GlassMode < 0 || Settings.GlassMode > 2) Settings.GlassMode = 1;
            Settings.GlassRefraction = Math.Max(0, Math.Min(100, Settings.GlassRefraction));
            Settings.GlassTransparency = Math.Max(0, Math.Min(100, Settings.GlassTransparency));
            Settings.GlassHighlight = Math.Max(0, Math.Min(100, Settings.GlassHighlight));
            if (double.IsNaN(Settings.IslandScale) || double.IsInfinity(Settings.IslandScale) || Settings.IslandScale < 0.75 || Settings.IslandScale > 1.5)
                Settings.IslandScale = 1.0;
        }

        private static void ShutdownWindows()
        {
#if FI_CORE_TESTING
            throw new InvalidOperationException("System shutdown is excluded from core test builds.");
#else
            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe"),
                Arguments = "/s /t 0", UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (Process process = Process.Start(info))
            {
                if (process == null) throw new InvalidOperationException("关机命令未启动。");
                if (process.WaitForExit(3000) && process.ExitCode != 0)
                    throw new InvalidOperationException("关机命令返回错误码 " + process.ExitCode.ToString(CultureInfo.InvariantCulture) + "。");
            }
#endif
        }

        private DateTime UtcNow() { return ToUtc(clock()); }
        private static DateTime ToUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        }
        private static TimeSpan Positive(TimeSpan value) { return value < TimeSpan.Zero ? TimeSpan.Zero : value; }
        private void EnsureNotDisposed() { if (disposed) throw new ObjectDisposedException("CoreEngine"); }
        private void OnChanged() { EventHandler handler = Changed; if (handler != null) handler(this, EventArgs.Empty); }
        private void Publish(IslandNoticeEventArgs notice)
        {
            EventHandler<IslandNoticeEventArgs> handler = Notice;
            if (handler != null) handler(this, notice);
        }
        private static IslandNoticeEventArgs NewNotice(string title, string message, string kind, bool urgent)
        {
            return new IslandNoticeEventArgs { Title = title, Message = message, Kind = kind, Urgent = urgent };
        }
    }

    public static class StartupRegistration
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "FreeIsland";

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                if (key == null) return false;
                string value = key.GetValue(ValueName) as string;
                return string.Equals(value, StartupCommand(), StringComparison.OrdinalIgnoreCase);
            }
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key == null) throw new InvalidOperationException("无法访问当前用户的启动设置。");
                if (enabled) key.SetValue(ValueName, StartupCommand(), RegistryValueKind.String);
                else key.DeleteValue(ValueName, false);
            }
        }

        private static string StartupCommand()
        {
            Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            return "\"" + Path.GetFullPath(assembly.Location) + "\" --silent";
        }
    }
}
