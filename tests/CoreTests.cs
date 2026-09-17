using System;
using System.Collections.Generic;
using System.IO;
using FreeIsland;

internal static class CoreTests
{
    private static int passed;
    private static string testRoot;

    private static int Main()
    {
        testRoot = Path.Combine(Path.GetTempPath(), "FreeIsland.CoreTests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        try
        {
            Run("defaults and JSON persistence", DefaultsAndPersistence);
            Run("island docking settings compatibility and validation", IslandDockingSettings);
            Run("island sizing defaults, migration and persistence", IslandSizingSettings);
            Run("island sizing validation before save and after load", IslandSizingValidation);
            Run("glass parameters migrate old settings and persist independently", GlassParameterSettings);
            Run("glass parameters clamp on save and load", GlassParameterValidation);
            Run("active island size defaults, persistence and validation", ActiveIslandSizing);
            Run("usage scene persistence, old settings and invalid scene", UsageSceneSettings);
            Run("duration and future-date validation", Validation);
            Run("stopwatch pause and reset", Stopwatch);
            Run("countdown pause, restart and single completion", Countdown);
            Run("countdown sleep and restart expiry", CountdownRestartExpiry);
            Run("task snapshots include paused timers and preserve stable ordering", IslandTaskSnapshots);
            Run("countdown progress survives restart and migrates old files", CountdownProgressPersistence);
            Run("shutdown appears only at ten seconds and stays quiet while distant", ShutdownTaskBoundary);
            Run("one-time and daily reminders", Reminders);
            Run("safe shutdown, prewarning and cancellation", SafeShutdown);
            Run("normal shutdown dispatch uses injected test action once", ShutdownDispatch);
            Run("unwarned expiry cancels even a short missed deadline", UnwarnedShutdown);
            Run("missed shutdown after sleep is cancelled", MissedShutdown);
            Run("shutdown plan is never restored", ShutdownNotPersistent);
            Run("recurring weekday calendar skips past days and validates selection", RecurringCalendar);
            Run("recurring DST gaps skipped and repeated hour occurs only once", RecurringDaylightSaving);
            Run("recurring dispatch, failure and missed warning window", RecurringDispatch);
            Run("corrupt state recovers atomic backup", CorruptBackup);
            Console.WriteLine("PASS: " + passed + " core tests. No registry or shutdown commands were executed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
        finally
        {
            // The unique, directly created test directory is the only cleanup target.
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static string NewDirectory()
    {
        string path = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static DateTime Start { get { return new DateTime(2030, 5, 10, 8, 0, 0, DateTimeKind.Utc); } }
    private static CoreEngine Engine(string path, Func<DateTime> clock)
    {
        return new CoreEngine(path, true, clock, delegate { throw new Exception("Safe mode executed shutdown action!"); });
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal(TimeSpan expected, TimeSpan actual, string message)
    {
        Check(Math.Abs((expected - actual).TotalMilliseconds) < 1, message + ": expected " + expected + ", got " + actual);
    }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new Exception("Expected ArgumentException: " + message);
    }

    private static void DefaultsAndPersistence()
    {
        DateTime now = Start;
        string path = NewDirectory();
        using (CoreEngine engine = Engine(path, delegate { return now; }))
        {
            Check(engine.Settings.AutoStart && engine.Settings.SoundEnabled && engine.Settings.EdgeHide, "Default flags");
            Check(double.IsNaN(engine.Settings.BallX), "Unpositioned ball");
            Check(engine.Settings.IslandAnchor == 0.5 && engine.Settings.IslandScreen == null, "Default island docking");
            Check(engine.Settings.IslandDotPercent == 20 && engine.Settings.IslandDotSize == 6 && engine.Settings.IslandScale == 1.0 && engine.Settings.GlassMode == 1, "Default island sizing and lite glass");
            Check(engine.Settings.Scene == UsageScene.Classroom && !engine.Settings.SceneSelected, "Classroom default awaits scene choice");
            engine.SaveSettings();
            Check(!File.ReadAllText(Path.Combine(path, "state.json")).Contains("NaN"), "Strict JSON must not contain NaN");
            engine.Settings.AutoStart = false;
            engine.Settings.Placement = IslandPlacement.Right;
            engine.Settings.BallX = 1740;
            engine.Settings.BallY = -210;
            engine.Settings.IslandAnchor = 0.7;
            engine.Settings.IslandScreen = @"\\.\DISPLAY1";
            engine.AddReminder("  会议  ", now.AddHours(1).ToLocalTime(), false);
            engine.SaveSettings();
        }
        using (CoreEngine restored = Engine(path, delegate { return now; }))
        {
            Check(!restored.Settings.AutoStart && restored.Settings.Placement == IslandPlacement.Right, "Settings restored");
            Check(restored.Settings.BallX == 1740 && restored.Settings.BallY == -210, "Coordinates restored");
            Check(restored.Settings.IslandAnchor == 0.7 && restored.Settings.IslandScreen == @"\\.\DISPLAY1", "Island docking restored");
            Check(restored.Reminders.Count == 1 && restored.Reminders[0].Title == "会议", "Reminder restored and trimmed");
            Check(restored.Reminders[0].DueAt.ToUniversalTime() == now.AddHours(1), "Reminder time restored");
        }
    }

    private static void Validation()
    {
        DateTime now = Start;
        using (CoreEngine engine = Engine(NewDirectory(), delegate { return now; }))
        {
            Reject(delegate { engine.StartCountdown(TimeSpan.Zero); }, "zero countdown");
            Reject(delegate { engine.StartCountdown(TimeSpan.FromSeconds(-1)); }, "negative countdown");
            Reject(delegate { engine.StartCountdown(TimeSpan.FromDays(7).Add(TimeSpan.FromTicks(1))); }, "over seven days");
            Reject(delegate { engine.AddReminder(" ", now.AddMinutes(1), false); }, "blank title");
            Reject(delegate { engine.AddReminder("过去", now, false); }, "past reminder");
            Reject(delegate { engine.ScheduleShutdown(now.AddSeconds(-1)); }, "past shutdown");
            engine.StartCountdown(TimeSpan.FromDays(7));
            Check(engine.CountdownActive, "Seven days accepted");
        }
    }

    private static void IslandDockingSettings()
    {
        DateTime now = Start;
        string path = NewDirectory();
        // Existing version-one installations have neither docking field.
        File.WriteAllText(Path.Combine(path, "state.json"), "{\"Version\":1,\"Settings\":{\"AutoStart\":false,\"Placement\":1}}");
        using (CoreEngine engine = Engine(path, delegate { return now; }))
        {
            Check(engine.Settings.IslandAnchor == 0.5 && engine.Settings.IslandScreen == null, "Missing docking fields use defaults");
            Check(!engine.Settings.AutoStart && engine.Settings.Placement == IslandPlacement.Left, "Old settings retained");
            foreach (double invalid in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, -0.1, 1.1 })
            {
                engine.Settings.IslandAnchor = invalid;
                engine.SaveSettings();
                Check(engine.Settings.IslandAnchor == 0.5, "Invalid anchor reset before saving");
            }
            foreach (double endpoint in new[] { 0.0, 1.0 })
            {
                engine.Settings.IslandAnchor = endpoint;
                engine.SaveSettings();
                Check(engine.Settings.IslandAnchor == endpoint, "Valid endpoint remains unchanged");
            }
        }
        File.WriteAllText(Path.Combine(path, "state.json"), "{\"Version\":1,\"Settings\":{\"IslandAnchor\":2}}");
        using (CoreEngine engine = Engine(path, delegate { return now; }))
            Check(engine.Settings.IslandAnchor == 0.5, "Out-of-range loaded anchor reset");
    }

    private static void IslandSizingSettings()
    {
        DateTime now = Start;
        string path = NewDirectory();
        File.WriteAllText(Path.Combine(path, "state.json"), "{\"Version\":1,\"Settings\":{\"AutoStart\":false,\"Placement\":1,\"IslandAnchor\":0.7}}");
        using (CoreEngine engine = Engine(path, delegate { return now; }))
        {
            Check(engine.Settings.IslandDotPercent == 20 && engine.Settings.IslandDotSize == 6 && engine.Settings.IslandScale == 1.0 && engine.Settings.GlassMode == 1,
                "Absent sizing and glass fields use twenty percent and lite defaults");
            Check(!engine.Settings.AutoStart && engine.Settings.Placement == IslandPlacement.Left && engine.Settings.IslandAnchor == 0.7,
                "Size migration preserves existing preferences");
            engine.SaveSettings();
        }
        using (CoreEngine restored = Engine(path, delegate { return now; }))
            Check(restored.Settings.IslandDotPercent == 20 && restored.Settings.IslandDotSize == 6 && restored.Settings.GlassMode == 1,
                "Migrated defaults survive restart");

        int[] percentages = { 0, 100, 24, 50 };
        int[] dotSizes = { 3, 20, 7, 12 };
        double[] scales = { 0.75, 1.5, 1.2, 1.0 };
        for (int index = 0; index < percentages.Length; index++)
        {
            using (CoreEngine engine = Engine(path, delegate { return now; }))
            {
                engine.Settings.IslandDotPercent = percentages[index];
                engine.Settings.IslandDotSize = 4; // The percentage must remain authoritative.
                engine.Settings.IslandScale = scales[index];
                engine.Settings.GlassMode = index % 3;
                engine.SaveSettings();
                Check(engine.Settings.IslandDotPercent == percentages[index] && engine.Settings.IslandDotSize == dotSizes[index],
                    "Dot percentage maps to nearest physical pixel, including upward half-pixel rounding");
            }
            using (CoreEngine restored = Engine(path, delegate { return now; }))
                Check(restored.Settings.IslandDotPercent == percentages[index] && restored.Settings.IslandDotSize == dotSizes[index] &&
                    restored.Settings.IslandScale == scales[index] && restored.Settings.GlassMode == index % 3,
                    "Dot percentages, physical sizes, scale and all glass modes survive restart");
        }

        int[] legacySizes = { 4, 3, 20, 7, 6 };
        int[] migratedPercentages = { 20, 0, 100, 24, 18 };
        int[] migratedPixels = { 6, 3, 20, 7, 6 };
        for (int index = 0; index < legacySizes.Length; index++)
        {
            File.WriteAllText(Path.Combine(path, "state.json"), "{\"Version\":1,\"Settings\":{\"AutoStart\":false,\"IslandDotSize\":" + legacySizes[index] + "}}");
            using (CoreEngine engine = Engine(path, delegate { return now; }))
            {
                Check(!engine.Settings.AutoStart && engine.Settings.IslandDotPercent == migratedPercentages[index] && engine.Settings.IslandDotSize == migratedPixels[index],
                    "Legacy default becomes twenty percent while custom pixel sizes retain their appearance");
                engine.SaveSettings();
            }
            using (CoreEngine restored = Engine(path, delegate { return now; }))
                Check(restored.Settings.IslandDotPercent == migratedPercentages[index] && restored.Settings.IslandDotSize == migratedPixels[index],
                    "Migrated percentage is persisted");
        }
        File.WriteAllText(Path.Combine(path, "state.json"), "{\"Version\":1,\"Settings\":{\"IslandDotSize\":4,\"IslandDotPercent\":80,\"GlassMode\":2}}");
        using (CoreEngine engine = Engine(path, delegate { return now; }))
            Check(engine.Settings.IslandDotPercent == 80 && engine.Settings.IslandDotSize == 17 && engine.Settings.GlassMode == 2,
                "Explicit percentage overrides conflicting legacy size during load");
    }

    private static void GlassParameterSettings()
    {
        DateTime now = Start;
        string path = NewDirectory();
        File.WriteAllText(Path.Combine(path, "state.json"), "{\"Version\":1,\"Settings\":{\"AutoStart\":false,\"GlassMode\":2,\"IslandDotPercent\":80,\"Placement\":2},\"Reminders\":[{\"Id\":\"preserved\",\"Title\":\"Lesson\",\"DueAt\":\"\\/Date(4102444800000)\\/\",\"Daily\":true,\"Completed\":false}]}");
        using (CoreEngine engine = Engine(path, delegate { return now; }))
        {
            Check(engine.Settings.GlassRefraction == 50 && engine.Settings.GlassTransparency == 65 && engine.Settings.GlassHighlight == 55, "Old files receive recommended glass defaults");
            Check(!engine.Settings.AutoStart && engine.Settings.GlassMode == 2 && engine.Settings.IslandDotPercent == 80 && engine.Settings.Placement == IslandPlacement.Right && engine.Reminders.Count == 1, "Migration preserves unrelated preferences and reminder");
            engine.Settings.GlassRefraction = 0; engine.Settings.GlassTransparency = 100; engine.Settings.GlassHighlight = 17;
            engine.SaveSettings();
        }
        using (CoreEngine restored = Engine(path, delegate { return now; }))
        {
            Check(restored.Settings.GlassRefraction == 0 && restored.Settings.GlassTransparency == 100 && restored.Settings.GlassHighlight == 17, "Explicit zero, hundred and custom parameters survive restart");
            restored.Settings.GlassMode = 0; restored.SaveSettings();
        }
        using (CoreEngine restored = Engine(path, delegate { return now; }))
            Check(restored.Settings.GlassMode == 0 && restored.Settings.GlassRefraction == 0 && restored.Settings.GlassTransparency == 100 && restored.Settings.GlassHighlight == 17, "Disabling material preserves parameters");
        File.WriteAllText(Path.Combine(path, "state.json"), "{\"Version\":1,\"Settings\":{\"GlassTransparency\":0}}");
        using (CoreEngine restored = Engine(path, delegate { return now; }))
            Check(restored.Settings.GlassRefraction == 50 && restored.Settings.GlassTransparency == 0 && restored.Settings.GlassHighlight == 55, "Missing fields default independently of explicitly saved zero");
    }

    private static void GlassParameterValidation()
    {
        DateTime now = Start;
        string path = NewDirectory();
        foreach (int invalid in new[] { int.MinValue, -1, 101, int.MaxValue })
        {
            int expected = invalid < 0 ? 0 : 100;
            using (CoreEngine engine = Engine(path, delegate { return now; }))
            {
                engine.Settings.GlassRefraction = engine.Settings.GlassTransparency = engine.Settings.GlassHighlight = invalid;
                engine.SaveSettings();
                Check(engine.Settings.GlassRefraction == expected && engine.Settings.GlassTransparency == expected && engine.Settings.GlassHighlight == expected, "Out-of-range values clamp before save");
            }
            File.WriteAllText(Path.Combine(path, "state.json"), "{\"Version\":1,\"Settings\":{\"GlassRefraction\":" + invalid + ",\"GlassTransparency\":" + invalid + ",\"GlassHighlight\":" + invalid + "}}");
            using (CoreEngine restored = Engine(path, delegate { return now; }))
                Check(restored.Settings.GlassRefraction == expected && restored.Settings.GlassTransparency == expected && restored.Settings.GlassHighlight == expected, "Out-of-range values clamp after load");
        }
    }

    private static void IslandSizingValidation()
    {
        DateTime now = Start;
        string path = NewDirectory();
        using (CoreEngine engine = Engine(path, delegate { return now; }))
        {
            foreach (int invalid in new[] { int.MinValue, -1, 101, int.MaxValue })
            {
                engine.Settings.IslandDotPercent = invalid;
                engine.Settings.IslandScale = 1.2;
                engine.SaveSettings();
                Check(engine.Settings.IslandDotPercent == 20 && engine.Settings.IslandDotSize == 6 && engine.Settings.IslandScale == 1.2,
                    "Invalid percentage resets before save while valid scale is retained");
                using (CoreEngine restored = Engine(path, delegate { return now; }))
                    Check(restored.Settings.IslandDotPercent == 20 && restored.Settings.IslandDotSize == 6, "Normalized dot percentage persisted");
            }
            foreach (int invalid in new[] { int.MinValue, -1, 3, int.MaxValue })
            {
                engine.Settings.IslandDotPercent = 24;
                engine.Settings.GlassMode = invalid;
                engine.SaveSettings();
                Check(engine.Settings.GlassMode == 1 && engine.Settings.IslandDotPercent == 24 && engine.Settings.IslandDotSize == 7,
                    "Invalid glass mode resets to lite without changing dot percentage");
            }
            foreach (double invalid in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity, -1.0, 0.749, 1.501 })
            {
                engine.Settings.IslandDotPercent = 24;
                engine.Settings.IslandScale = invalid;
                engine.SaveSettings();
                Check(engine.Settings.IslandDotSize == 7 && engine.Settings.IslandScale == 1.0,
                    "Invalid scale resets before save while valid dot percentage is retained");
                string json = File.ReadAllText(Path.Combine(path, "state.json"));
                Check(!json.Contains("NaN") && !json.Contains("Infinity"), "Nonfinite scale must not enter saved JSON");
                Check(json.Contains("IslandDotPercent") && json.Contains("IslandDotSize") && json.Contains("GlassMode"), "Both new settings and derived pixels are persisted");
                using (CoreEngine restored = Engine(path, delegate { return now; }))
                    Check(restored.Settings.IslandDotPercent == 24 && restored.Settings.IslandDotSize == 7 && restored.Settings.IslandScale == 1.0, "Normalized scale persisted");
            }
        }
        foreach (string invalid in new[] { "-2147483648", "-1", "101", "2147483647" })
        {
            File.WriteAllText(Path.Combine(path, "state.json"), "{\"Version\":1,\"Settings\":{\"AutoStart\":false,\"IslandDotSize\":20,\"IslandDotPercent\":" + invalid + ",\"GlassMode\":3,\"IslandScale\":1.2}}");
            using (CoreEngine engine = Engine(path, delegate { return now; }))
                Check(!engine.Settings.AutoStart && engine.Settings.IslandDotPercent == 20 && engine.Settings.IslandDotSize == 6 && engine.Settings.GlassMode == 1 && engine.Settings.IslandScale == 1.2,
                    "Invalid loaded percentage and glass reset without losing other preferences");
        }
        foreach (string invalid in new[] { "-2147483648", "2", "21", "2147483647" })
        {
            File.WriteAllText(Path.Combine(path, "state.json"), "{\"Version\":1,\"Settings\":{\"AutoStart\":false,\"IslandDotSize\":" + invalid + "}}");
            using (CoreEngine engine = Engine(path, delegate { return now; }))
                Check(!engine.Settings.AutoStart && engine.Settings.IslandDotPercent == 20 && engine.Settings.IslandDotSize == 6,
                    "Invalid legacy size migrates to the new default");
        }
        foreach (string invalid in new[] { "-1", "0.749", "1.501" })
        {
            File.WriteAllText(Path.Combine(path, "state.json"), "{\"Version\":1,\"Settings\":{\"AutoStart\":false,\"IslandDotSize\":7,\"IslandScale\":" + invalid + "}}");
            using (CoreEngine engine = Engine(path, delegate { return now; }))
                Check(!engine.Settings.AutoStart && engine.Settings.IslandDotPercent == 24 && engine.Settings.IslandDotSize == 7 && engine.Settings.IslandScale == 1.0,
                    "Invalid loaded scale resets without losing custom dot appearance");
        }
    }
    private static void Stopwatch()
    {
        DateTime now = Start;
        using (CoreEngine engine = Engine(NewDirectory(), delegate { return now; }))
        {
            engine.ToggleStopwatch();
            now = now.AddSeconds(12);
            Equal(TimeSpan.FromSeconds(12), engine.StopwatchElapsed, "Running stopwatch");
            engine.ToggleStopwatch();
            now = now.AddMinutes(2);
            Equal(TimeSpan.FromSeconds(12), engine.StopwatchElapsed, "Paused stopwatch");
            engine.ToggleStopwatch();
            now = now.AddSeconds(8);
            engine.ToggleStopwatch();
            Equal(TimeSpan.FromSeconds(20), engine.StopwatchElapsed, "Resumed stopwatch");
            engine.ResetStopwatch();
            Check(!engine.StopwatchRunning, "Reset stops stopwatch");
            Equal(TimeSpan.Zero, engine.StopwatchElapsed, "Reset clears stopwatch");
        }
    }

    private static void ActiveIslandSizing()
    {
        DateTime now = Start;
        string path = NewDirectory();
        File.WriteAllText(Path.Combine(path, "state.json"), "{\"Version\":1,\"Settings\":{\"AutoStart\":false}}");
        using (CoreEngine engine = Engine(path, delegate { return now; }))
        {
            Check(engine.Settings.ActiveIslandSize == 0, "Old settings select automatic active size");
            Check(engine.Settings.AutoUpdate, "Old settings enable automatic updates by default");
            foreach (int invalid in new[] { -1, 1, 39, 161, int.MaxValue })
            {
                engine.Settings.ActiveIslandSize = invalid;
                engine.SaveSettings();
                Check(engine.Settings.ActiveIslandSize == 0, "Invalid active size returns to automatic");
            }
            foreach (int size in new[] { 40, 88, 160 })
            {
                engine.Settings.ActiveIslandSize = size;
                engine.SaveSettings();
                Check(engine.Settings.ActiveIslandSize == size, "Range endpoints remain selectable");
            }
        }
        using (CoreEngine engine = Engine(path, delegate { return now; }))
            Check(engine.Settings.ActiveIslandSize == 160 && !engine.Settings.AutoStart, "Custom active size persists independently");
        File.WriteAllText(Path.Combine(path, "state.json"), "{\"Version\":1,\"Settings\":{\"ActiveIslandSize\":999}}");
        using (CoreEngine engine = Engine(path, delegate { return now; }))
            Check(engine.Settings.ActiveIslandSize == 0, "Invalid loaded active size normalizes");
        foreach (int oldSize in new[] { 30, 36, 40, 48, 50 })
        {
            File.WriteAllText(Path.Combine(path, "state.json"), "{\"Version\":1,\"Settings\":{\"ActiveIslandSize\":" + oldSize + ",\"IslandDotPercent\":20,\"AutoUpdate\":false}}");
            using (CoreEngine engine = Engine(path, delegate { return now; })) Check(engine.Settings.ActiveIslandSize == 0 && engine.Settings.IslandDotPercent == 20 && !engine.Settings.AutoUpdate, "Old small task ball migrates independently from idle dot and update preference");
        }
        File.WriteAllText(Path.Combine(path, "state.json"), "{\"Version\":1,\"Settings\":{\"ActiveIslandSize\":40,\"ActiveIslandSizeVersion\":2,\"AutoUpdate\":false}}");
        using (CoreEngine engine = Engine(path, delegate { return now; })) { Check(engine.Settings.ActiveIslandSize == 40 && !engine.Settings.AutoUpdate, "New intentional small selection and disabled updater persist"); engine.SaveSettings(); }
    }

    private static void IslandTaskSnapshots()
    {
        DateTime now = Start;
        using (CoreEngine engine = Engine(NewDirectory(), delegate { return now; }))
        {
            Check(engine.GetIslandTasks().Count == 0 && !engine.StopwatchActive, "Idle engine has no task");
            engine.AddReminder("稍后提醒", now.AddHours(2), false);
            engine.ScheduleShutdown(now.AddHours(3));
            Check(engine.GetIslandTasks().Count == 0, "Future reminder and distant shutdown do not occupy island");
            engine.ToggleStopwatch();
            engine.ToggleStopwatch();
            IList<IslandTaskInfo> pausedZero = engine.GetIslandTasks();
            Check(engine.StopwatchActive && pausedZero.Count == 1 && !pausedZero[0].Running && pausedZero[0].Time == TimeSpan.Zero,
                "Stopwatch paused at zero still counts as a task");
            Check(pausedZero[0].Progress == -1, "Stopwatch has no fake completion fraction");
            engine.StartCountdown(TimeSpan.FromSeconds(100));
            now = now.AddSeconds(25);
            engine.PauseResumeCountdown();
            IList<IslandTaskInfo> tasks = engine.GetIslandTasks();
            Check(tasks.Count == 2 && tasks[0].Kind == "countdown" && tasks[1].Kind == "stopwatch", "Two paused tasks appear in deterministic order");
            Check(!tasks[0].Running && Math.Abs(tasks[0].Progress - 0.75) < 0.0001, "Paused countdown retains remaining fraction");
            string countdownId = tasks[0].Id;
            string stopwatchId = tasks[1].Id;
            now = now.AddMinutes(5);
            engine.PauseResumeCountdown();
            engine.ToggleStopwatch();
            Check(engine.GetIslandTasks()[0].Id == countdownId && engine.GetIslandTasks()[1].Id == stopwatchId, "Task identities remain stable across pause and resume");
            Check(!tasks[0].Running && tasks[0].Time == TimeSpan.FromSeconds(75), "Earlier snapshot is immutable as engine changes");
            bool readOnly = false;
            try { tasks.Clear(); } catch (NotSupportedException) { readOnly = true; }
            Check(readOnly, "Snapshot collection cannot mutate engine state");
            engine.ScheduleShutdown(now.AddSeconds(10));
            tasks = engine.GetIslandTasks();
            Check(tasks.Count == 3 && tasks[0].Kind == "shutdown" && tasks[1].Kind == "countdown", "Urgent shutdown leads the task stack");
            engine.CancelShutdown();
            engine.ResetStopwatch();
            Check(!engine.StopwatchActive && engine.GetIslandTasks().Count == 1, "Reset removes stopwatch task");
            now = now.AddSeconds(75);
            Check(engine.GetIslandTasks().Count == 0, "Expired countdown disappears even before completion tick");
            engine.Tick();
            Check(engine.CountdownDuration == TimeSpan.Zero, "Finished countdown clears progress baseline");
        }
    }

    private static void CountdownProgressPersistence()
    {
        DateTime now = Start;
        string path = NewDirectory();
        using (CoreEngine engine = Engine(path, delegate { return now; }))
        {
            engine.StartCountdown(TimeSpan.FromSeconds(100));
            now = now.AddSeconds(25);
            engine.PauseResumeCountdown();
        }
        using (CoreEngine engine = Engine(path, delegate { return now; }))
        {
            Equal(TimeSpan.FromSeconds(100), engine.CountdownDuration, "Original duration survives paused restart");
            Check(Math.Abs(engine.GetIslandTasks()[0].Progress - 0.75) < 0.0001, "Paused progress survives restart");
            engine.PauseResumeCountdown();
        }
        now = now.AddSeconds(25);
        using (CoreEngine engine = Engine(path, delegate { return now; }))
        {
            Equal(TimeSpan.FromSeconds(100), engine.CountdownDuration, "Original duration survives running restart");
            Check(Math.Abs(engine.GetIslandTasks()[0].Progress - 0.5) < 0.0001, "Running progress advances while closed");
            engine.CancelCountdown();
            Equal(TimeSpan.Zero, engine.CountdownDuration, "Cancel clears original duration");
        }
        File.WriteAllText(Path.Combine(path, "state.json"), "{\"Version\":1,\"CountdownActive\":true,\"CountdownRunning\":false,\"PausedCountdownTicks\":750000000}");
        using (CoreEngine engine = Engine(path, delegate { return now; }))
        {
            Equal(TimeSpan.FromSeconds(75), engine.CountdownDuration, "Old paused countdown migrates to available baseline");
            Check(engine.GetIslandTasks()[0].Progress == 1, "Migration has a valid ring baseline");
        }
        File.WriteAllText(Path.Combine(path, "state.json"), "{\"Version\":1,\"CountdownActive\":true,\"CountdownRunning\":false,\"PausedCountdownTicks\":750000000,\"CountdownDurationTicks\":1}");
        using (CoreEngine engine = Engine(path, delegate { return now; }))
        {
            Equal(TimeSpan.FromSeconds(75), engine.CountdownDuration, "Malformed baseline cannot be less than remaining time");
            Check(engine.GetIslandTasks()[0].Progress == 1, "Malformed baseline cannot overflow ring");
        }
    }

    private static void ShutdownTaskBoundary()
    {
        DateTime now = Start;
        using (CoreEngine engine = Engine(NewDirectory(), delegate { return now; }))
        {
            int changed = 0, notices = 0;
            engine.ScheduleShutdown(now.AddSeconds(70));
            engine.Changed += delegate { changed++; };
            engine.Notice += delegate(object sender, IslandNoticeEventArgs e) { if (e.Kind == "shutdown") notices++; };
            now = now.AddSeconds(10);
            engine.Tick();
            Check(changed == 0 && notices == 0 && engine.GetIslandTasks().Count == 0, "Sixty seconds does not display or repaint shutdown");
            now = Start.AddMilliseconds(59999);
            engine.Tick();
            Check(changed == 0 && notices == 0 && engine.GetIslandTasks().Count == 0, "10.001 seconds remains hidden");
            Equal(TimeSpan.FromMilliseconds(10001), engine.ShutdownRemaining.Value, "Shutdown remaining uses injected clock");
            now = Start.AddSeconds(60);
            engine.Tick();
            Check(notices == 1 && changed == 1 && engine.GetIslandTasks().Count == 1, "Exactly ten seconds shows task and warns");
            Check(engine.GetIslandTasks()[0].Progress == 1, "Urgent countdown ring begins full");
            engine.Tick();
            Check(notices == 1, "Warning is emitted only once");
            now = Start.AddSeconds(65);
            Check(engine.GetIslandTasks()[0].Progress == 0.5, "Urgent shutdown progress follows final ten seconds");
            engine.CancelShutdown();
            Check(engine.ShutdownRemaining == null && engine.GetIslandTasks().Count == 0, "Cancellation clears urgent task and remaining time");
        }
    }

    private static void UsageSceneSettings()
    {
        DateTime now = Start;
        string path = NewDirectory();
        File.WriteAllText(Path.Combine(path, "state.json"), "{\"Version\":1,\"Settings\":{\"AutoStart\":false,\"Placement\":1}}");
        using (CoreEngine engine = Engine(path, delegate { return now; }))
        {
            Check(engine.Settings.Scene == UsageScene.Classroom && !engine.Settings.SceneSelected, "Old settings default to unselected classroom scene");
            Check(!engine.Settings.AutoStart && engine.Settings.Placement == IslandPlacement.Left, "Scene migration preserves existing preferences");
            engine.Settings.Scene = UsageScene.Desktop;
            engine.Settings.SceneSelected = true;
            engine.SaveSettings();
        }
        using (CoreEngine engine = Engine(path, delegate { return now; }))
        {
            Check(engine.Settings.Scene == UsageScene.Desktop && engine.Settings.SceneSelected, "Selected desktop scene survives restart");
            engine.Settings.Scene = (UsageScene)99;
            engine.SaveSettings();
            Check(engine.Settings.Scene == UsageScene.Classroom, "Invalid scene corrected before saving");
        }
        File.WriteAllText(Path.Combine(path, "state.json"), "{\"Version\":1,\"Settings\":{\"Scene\":-1,\"SceneSelected\":true}}");
        using (CoreEngine engine = Engine(path, delegate { return now; }))
            Check(engine.Settings.Scene == UsageScene.Classroom && engine.Settings.SceneSelected, "Invalid loaded scene corrected while selection flag retained");
    }

    private static void Countdown()
    {
        DateTime now = Start;
        string path = NewDirectory();
        using (CoreEngine engine = Engine(path, delegate { return now; }))
        {
            engine.StartCountdown(TimeSpan.FromMinutes(2));
            now = now.AddSeconds(30);
            engine.PauseResumeCountdown();
            Check(!engine.CountdownRunning && engine.CountdownActive, "Countdown paused");
            now = now.AddHours(1);
            Equal(TimeSpan.FromSeconds(90), engine.CountdownRemaining, "Paused time stable");
        }
        using (CoreEngine engine = Engine(path, delegate { return now; }))
        {
            Check(engine.CountdownActive && !engine.CountdownRunning, "Pause survives restart");
            Equal(TimeSpan.FromSeconds(90), engine.CountdownRemaining, "Paused duration restored");
            int completed = 0;
            engine.Notice += delegate(object sender, IslandNoticeEventArgs e) { if (e.Kind == "countdown") completed++; };
            engine.PauseResumeCountdown();
            now = now.AddSeconds(89);
            engine.Tick();
            Check(completed == 0, "Not completed early");
            now = now.AddSeconds(1);
            engine.Tick();
            engine.Tick();
            Check(completed == 1 && !engine.CountdownActive, "Completion exactly once");
        }
    }

    private static void CountdownRestartExpiry()
    {
        DateTime now = Start;
        string path = NewDirectory();
        using (CoreEngine engine = Engine(path, delegate { return now; })) engine.StartCountdown(TimeSpan.FromMinutes(1));
        now = now.AddHours(3);
        using (CoreEngine engine = Engine(path, delegate { return now; }))
        {
            int notices = 0;
            engine.Notice += delegate(object sender, IslandNoticeEventArgs e) { if (e.Kind == "countdown") notices++; };
            Equal(TimeSpan.Zero, engine.CountdownRemaining, "Expired restored countdown");
            engine.Tick();
            Check(notices == 1 && !engine.CountdownActive, "Overdue restart notifies");
        }
    }

    private static void Reminders()
    {
        DateTime now = Start;
        string path = NewDirectory();
        using (CoreEngine engine = Engine(path, delegate { return now; }))
        {
            engine.AddReminder("一次提醒", now.AddMinutes(1), false);
            engine.AddReminder("每日提醒", now.AddMinutes(2), true);
            int notices = 0;
            engine.Notice += delegate(object sender, IslandNoticeEventArgs e) { if (e.Kind == "reminder") notices++; };
            now = now.AddMinutes(1);
            engine.Tick();
            engine.Tick();
            Check(notices == 1 && engine.Reminders[0].Completed, "One-time reminder once");
            now = now.AddDays(3).AddMinutes(2);
            engine.Tick();
            Check(notices == 2 && !engine.Reminders[1].Completed, "Missed daily reminders coalesced");
            Check(engine.Reminders[1].DueAt.ToUniversalTime() > now, "Daily recurrence advanced beyond now");
            Check(engine.Reminders[1].DueAt.TimeOfDay == Start.AddMinutes(2).ToLocalTime().TimeOfDay, "Daily local wall time preserved");
            engine.RemoveReminder(engine.Reminders[0].Id);
            Check(engine.Reminders.Count == 1, "Reminder removal");
        }
        using (CoreEngine engine = Engine(path, delegate { return now; }))
            Check(engine.Reminders.Count == 1 && !engine.Reminders[0].Completed, "Reminder mutation persisted");
    }

    private static void SafeShutdown()
    {
        DateTime now = Start;
        using (CoreEngine engine = Engine(NewDirectory(), delegate { return now; }))
        {
            List<string> titles = new List<string>();
            engine.Notice += delegate(object sender, IslandNoticeEventArgs e) { if (e.Kind == "shutdown") titles.Add(e.Title); };
            engine.ScheduleShutdown(now.AddSeconds(70));
            engine.Tick();
            Check(titles.Count == 0, "No early prewarning");
            now = now.AddSeconds(60);
            engine.Tick();
            engine.Tick();
            Check(titles.Count == 1 && titles[0] == "即将自动关机", "Single 10-second prewarning");
            engine.CancelShutdown();
            now = now.AddMinutes(1);
            engine.Tick();
            Check(titles.Count == 1 && !engine.ShutdownAt.HasValue, "Cancellation effective");
            engine.ScheduleShutdown(now.AddSeconds(1));
            engine.Tick();
            Check(titles.Count == 2 && titles[1] == "即将自动关机", "Short plan warns on first tick");
            now = now.AddSeconds(1);
            engine.Tick();
            Check(titles.Count == 3 && titles[2] == "已模拟定时关机", "Safe mode simulates");
            Check(!engine.ShutdownAt.HasValue, "Completed plan cleared");
        }
    }

    private static void MissedShutdown()
    {
        DateTime now = Start;
        using (CoreEngine engine = Engine(NewDirectory(), delegate { return now; }))
        {
            string title = null;
            engine.Notice += delegate(object sender, IslandNoticeEventArgs e) { if (e.Kind == "shutdown") title = e.Title; };
            engine.ScheduleShutdown(now.AddMinutes(5));
            now = now.AddHours(2);
            engine.Tick();
            Check(title == "已取消过期关机" && !engine.ShutdownAt.HasValue, "Missed shutdown cancelled after sleep");
        }
    }

    private static void ShutdownDispatch()
    {
        DateTime now = Start;
        int calls = 0;
        using (CoreEngine engine = new CoreEngine(NewDirectory(), false, delegate { return now; }, delegate { calls++; }))
        {
            engine.ScheduleShutdown(now.AddSeconds(10));
            now = now.AddSeconds(9);
            engine.Tick();
            Check(calls == 0, "Shutdown action must not run early");
            now = now.AddSeconds(1);
            engine.Tick();
            engine.Tick();
            Check(calls == 1 && !engine.ShutdownAt.HasValue, "Injected shutdown action runs exactly once");
            engine.ScheduleShutdown(now.AddSeconds(10));
            engine.CancelShutdown();
            now = now.AddSeconds(20);
            engine.Tick();
            Check(calls == 1, "Cancelled normal plan never dispatches");
        }
    }

    private static void UnwarnedShutdown()
    {
        DateTime now = Start;
        int calls = 0;
        using (CoreEngine engine = new CoreEngine(NewDirectory(), false, delegate { return now; }, delegate { calls++; }))
        {
            string title = null;
            engine.Notice += delegate(object sender, IslandNoticeEventArgs e) { if (e.Kind == "shutdown") title = e.Title; };
            engine.ScheduleShutdown(now.AddSeconds(90));
            engine.Tick();
            now = now.AddSeconds(91);
            engine.Tick();
            Check(calls == 0 && !engine.ShutdownAt.HasValue, "Gap under two minutes must not bypass warning");
            Check(title == "已取消未预警关机", "Missed prewarning cancellation explained");
            engine.ScheduleShutdown(now.AddSeconds(1));
            now = now.AddSeconds(2);
            engine.Tick();
            Check(calls == 0 && !engine.ShutdownAt.HasValue, "Short plan cannot execute without an earlier warning tick");
        }
    }

    private static void ShutdownNotPersistent()
    {
        DateTime now = Start;
        string path = NewDirectory();
        using (CoreEngine engine = Engine(path, delegate { return now; }))
        {
            engine.ScheduleShutdown(now.AddHours(1));
            engine.SaveSettings();
        }
        using (CoreEngine engine = Engine(path, delegate { return now; })) Check(!engine.ShutdownAt.HasValue, "Shutdown must not survive restart");
    }

    private static void CorruptBackup()
    {
        DateTime now = Start;
        string path = NewDirectory();
        using (CoreEngine engine = Engine(path, delegate { return now; }))
        {
            engine.Settings.Placement = IslandPlacement.Left;
            engine.SaveSettings();
            engine.SaveSettings();
        }
        File.WriteAllText(Path.Combine(path, "state.json"), "{ broken");
        using (CoreEngine restored = Engine(path, delegate { return now; }))
        {
            Check(restored.Settings.Placement == IslandPlacement.Left, "Previous atomic backup restored");
            int info = 0;
            restored.Notice += delegate(object sender, IslandNoticeEventArgs e) { if (e.Kind == "info") info++; };
            restored.Tick();
            Check(info == 1, "Backup recovery reported");
        }
    }

    private static void RecurringCalendar()
    {
        DateTime sunday = new DateTime(2026, 9, 20, 18, 0, 0, DateTimeKind.Utc);
        Check(CoreEngine.NextRecurringShutdownUtc(sunday, 17, 0, 31, TimeZoneInfo.Utc) == new DateTime(2026, 9, 21, 17, 0, 0, DateTimeKind.Utc), "Workdays advance Sunday to Monday");
        Check(CoreEngine.NextRecurringShutdownUtc(sunday, 19, 0, 64, TimeZoneInfo.Utc) == sunday.AddHours(1), "Sunday uses bit six");
        Check(CoreEngine.NextRecurringShutdownUtc(sunday, 17, 0, 64, TimeZoneInfo.Utc) == sunday.Date.AddDays(7).AddHours(17), "Past Sunday advances a full week");
        Check(CoreEngine.NextRecurringShutdownUtc(sunday, 18, 0, 127, TimeZoneInfo.Utc) == sunday.AddDays(1), "Exact deadline is never immediately restored");
        Reject(delegate { CoreEngine.NextRecurringShutdownUtc(sunday, 24, 0, 127, TimeZoneInfo.Utc); }, "Invalid recurring hour");
        Reject(delegate { CoreEngine.NextRecurringShutdownUtc(sunday, 17, 60, 127, TimeZoneInfo.Utc); }, "Invalid recurring minute");
        Reject(delegate { CoreEngine.NextRecurringShutdownUtc(sunday, 17, 0, 0, TimeZoneInfo.Utc); }, "No weekdays selected");
        Reject(delegate { CoreEngine.NextRecurringShutdownUtc(sunday, 17, 0, 128, TimeZoneInfo.Utc); }, "Invalid weekday mask");
    }
    private static void RecurringDispatch()
    {
        DateTime now = Start;
        DateTime local = now.ToLocalTime().AddHours(1);
        int calls = 0;
        using (CoreEngine engine = new CoreEngine(NewDirectory(), false, delegate { return now; }, delegate { calls++; }))
        {
            engine.SetRecurringShutdown(local.Hour, local.Minute, 127);
            DateTime due = engine.ShutdownAt.Value.ToUniversalTime();
            now = due.AddSeconds(-10);
            int warnings = 0;
            engine.Notice += delegate(object sender, IslandNoticeEventArgs e) { if (e.Title == "即将自动关机") warnings++; };
            engine.Tick();
            Check(warnings == 1 && calls == 0, "Recurring warning starts at ten seconds");
            for (int second = 0; second < 10; second++) { now = now.AddSeconds(1); engine.Tick(); }
            engine.Tick();
            Check(calls == 1 && engine.ShutdownRecurringEnabled && engine.ShutdownAt.Value.ToUniversalTime() > due, "Injected action occurs once and next day is armed");
            due = engine.ShutdownAt.Value.ToUniversalTime();
            now = due.AddSeconds(-5); engine.Tick();
            Check(calls == 1 && engine.ShutdownAt.Value.ToUniversalTime() > due, "Resume inside the final ten seconds skips current occurrence");
            due = engine.ShutdownAt.Value.ToUniversalTime();
            now = due; engine.Tick();
            Check(calls == 1 && engine.ShutdownAt.Value.ToUniversalTime() > due, "Unwarned exact deadline skips and advances");
            now = now.Date.AddDays(1).AddHours(local.ToUniversalTime().Hour).AddSeconds(-5);
            DateTime closeLocal = now.AddSeconds(5).ToLocalTime();
            engine.SetRecurringShutdown(closeLocal.Hour, closeLocal.Minute, 127);
            Check(engine.ShutdownRemaining.Value > TimeSpan.FromSeconds(10), "Arming five seconds before occurrence skips insufficient warning time");
        }
        now = Start;
        using (CoreEngine engine = new CoreEngine(NewDirectory(), false, delegate { return now; }, delegate { calls++; throw new InvalidOperationException("fixture failure"); }))
        {
            engine.SetRecurringShutdown(local.Hour, local.Minute, 127);
            DateTime due = engine.ShutdownAt.Value.ToUniversalTime();
            now = due.AddSeconds(-10); engine.Tick();
            now = due; engine.Tick(); engine.Tick();
            Check(calls == 2 && engine.ShutdownRecurringEnabled && engine.ShutdownAt.Value.ToUniversalTime() > due, "Failed action is not retried and future occurrences remain");
        }
    }

    private static void RecurringDaylightSaving()
    {
        var spring = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday);
        var autumn = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2030, 1, 1), new DateTime(2030, 12, 31), TimeSpan.FromHours(1), spring, autumn);
        var zone = TimeZoneInfo.CreateCustomTimeZone("FreeIsland fixture eastern", TimeSpan.FromHours(-5), "Fixture", "Standard", "Daylight", new[] { rule });
        Check(CoreEngine.NextRecurringShutdownUtc(new DateTime(2030, 3, 10, 6, 0, 0, DateTimeKind.Utc), 2, 30, 64, zone) == new DateTime(2030, 3, 17, 6, 30, 0, DateTimeKind.Utc), "Spring nonexistent time skips that selected day");
        Check(CoreEngine.NextRecurringShutdownUtc(new DateTime(2030, 11, 3, 5, 0, 0, DateTimeKind.Utc), 1, 30, 64, zone) == new DateTime(2030, 11, 3, 5, 30, 0, DateTimeKind.Utc), "Autumn ambiguous hour chooses earliest occurrence");
        Check(CoreEngine.NextRecurringShutdownUtc(new DateTime(2030, 11, 3, 5, 31, 0, DateTimeKind.Utc), 1, 30, 64, zone) == new DateTime(2030, 11, 10, 6, 30, 0, DateTimeKind.Utc), "Autumn repeated hour cannot trigger a second occurrence");
    }
}
