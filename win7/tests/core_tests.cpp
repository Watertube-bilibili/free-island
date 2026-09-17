#include "../src/core.h"
#include <cmath>
#include <cstdio>
#include <functional>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
std::wstring testRoot;
std::vector<std::wstring> directories;
int passed = 0;

struct Clock {
    int64_t wall = 1904630400000LL; // 2030-05-10 08:00 UTC.
    uint64_t mono = 100000;
    int calls = 0;
    void Advance(int64_t milliseconds) { wall += milliseconds; mono += milliseconds; }
    std::function<int64_t()> Wall() { return [this]() { return wall; }; }
    std::function<uint64_t()> Mono() { return [this]() { return mono; }; }
    std::function<void()> Shutdown() { return [this]() { ++calls; }; }
};

void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); }

void Equal(int64_t expected, int64_t actual, const char* message) {
    if (expected != actual) {
        std::cerr << "Expected " << expected << ", actual " << actual << std::endl;
        throw std::runtime_error(message);
    }
}

void Reject(const std::function<void()>& operation) {
    try { operation(); } catch (const std::invalid_argument&) { return; }
    throw std::runtime_error("Invalid input was accepted.");
}

std::wstring Directory() {
    wchar_t id[32];
    swprintf(id, 32, L"\\case-%u", static_cast<unsigned int>(directories.size() + 1));
    std::wstring path = testRoot + id;
    Check(CreateDirectoryW(path.c_str(), NULL) != FALSE, "Cannot create test directory.");
    directories.push_back(path);
    return path;
}

std::vector<fi::Notice> Notices(fi::Engine& engine, const std::string& kind) {
    std::vector<fi::Notice> all = engine.TakeNotices(), selected;
    for (size_t i = 0; i < all.size(); ++i) if (all[i].kind == kind) selected.push_back(all[i]);
    return selected;
}

void WriteBytes(const std::wstring& path, const std::string& text) {
    HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    Check(file != INVALID_HANDLE_VALUE, "Cannot create fixture.");
    DWORD count = 0;
    BOOL success = WriteFile(file, text.data(), static_cast<DWORD>(text.size()), &count, NULL);
    CloseHandle(file);
    Check(success && count == text.size(), "Cannot write fixture.");
}

std::string ReadBytes(const std::wstring& path) {
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    Check(file != INVALID_HANDLE_VALUE, "Cannot open fixture.");
    DWORD length = GetFileSize(file, NULL), count = 0;
    std::string bytes(length, '\0');
    BOOL success = ReadFile(file, &bytes[0], length, &count, NULL);
    CloseHandle(file);
    Check(success && count == length, "Cannot read fixture.");
    return bytes;
}

void SettingsFixture(const std::wstring& path, const std::string& fields) {
    std::string payload = "FreeIsland-Win7 1\n" + fields;
    uint32_t hash = 2166136261U;
    for (size_t i = 0; i < payload.size(); ++i) hash = (hash ^ static_cast<unsigned char>(payload[i])) * 16777619U;
    char checksum[32];
    snprintf(checksum, sizeof(checksum), "checksum=%08x\n", static_cast<unsigned int>(hash));
    WriteBytes(path + L"\\state.dat", payload + checksum);
}

void DefaultsAndPersistence() {
    Clock time;
    const std::wstring path = Directory();
    const std::wstring title = L"会议 | = \n 中文";
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        Check(e.settings.startup && e.settings.sound && e.settings.edgeHide, "Default flags.");
        Check(e.settings.scene == fi::Scene::Classroom && !e.settings.sceneSelected, "Default classroom awaits selection.");
        Check(e.settings.dock == fi::Dock::Top && e.settings.anchor == 0.5 && e.settings.monitor.empty(), "Default docking.");
        Check(e.settings.islandDotPercent == 20 && e.settings.islandDotSize == 6 && e.settings.islandScale == 1.0 && e.settings.glassMode == 1,
              "Default island sizing and lite glass.");
        Check(e.settings.ballX == -99999 && e.settings.ballY == -99999, "Unpositioned ball.");
        e.settings.startup = false;
        e.settings.sound = false;
        e.settings.edgeHide = false;
        e.settings.scene = fi::Scene::Desktop;
        e.settings.sceneSelected = true;
        e.settings.ballX = 1740;
        e.settings.ballY = -210;
        e.settings.dock = fi::Dock::Right;
        e.settings.anchor = 0.7;
        e.settings.islandDotPercent = 24;
        e.settings.glassMode = 2;
        e.settings.islandScale = 1.25;
        e.settings.monitor = L"\\\\.\\DISPLAY1";
        e.AddReminder(L"  " + title + L"  ", time.wall + 3600000, false);
        e.Save();
    }
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        Check(!e.settings.startup && !e.settings.sound && !e.settings.edgeHide, "Flags restored.");
        Check(e.settings.scene == fi::Scene::Desktop && e.settings.sceneSelected, "Selected scene restored.");
        Check(e.settings.ballX == 1740 && e.settings.ballY == -210, "Negative screen coordinates restored.");
        Check(e.settings.dock == fi::Dock::Right, "Dock side restored.");
        Check(std::fabs(e.settings.anchor - 0.7) < 0.000000001, "Dock anchor restored.");
        Check(e.settings.monitor == L"\\\\.\\DISPLAY1", "Dock monitor restored.");
        Check(e.settings.islandDotPercent == 24 && e.settings.islandDotSize == 7 && e.settings.islandScale == 1.25 && e.settings.glassMode == 2,
              "Custom island sizing and glass restored.");
        Check(e.reminders.size() == 1 && e.reminders[0].title == title, "Unicode and delimiters roundtrip.");
        Equal(time.wall + 3600000, e.reminders[0].dueMs, "Reminder UTC retained.");
    }
}

void SettingsValidation() {
    Clock time;
    fi::Engine e(Directory(), true, time.Wall(), time.Mono(), time.Shutdown());
    double invalid[] = { std::numeric_limits<double>::quiet_NaN(), std::numeric_limits<double>::infinity(), -0.1, 1.1 };
    for (size_t i = 0; i < sizeof(invalid) / sizeof(invalid[0]); ++i) {
        e.settings.anchor = invalid[i]; e.Save();
        Check(e.settings.anchor == 0.5, "Invalid anchor normalized.");
    }
    e.settings.anchor = 0; e.Save(); Check(e.settings.anchor == 0, "Zero anchor preserved.");
    e.settings.anchor = 1; e.Save(); Check(e.settings.anchor == 1, "One anchor preserved.");
    e.settings.scene = static_cast<fi::Scene>(99);
    e.settings.dock = static_cast<fi::Dock>(99);
    e.Save();
    Check(e.settings.scene == fi::Scene::Classroom && e.settings.dock == fi::Dock::Top, "Invalid enum normalized.");
}

void IslandSizingSettings() {
    Clock time;
    const std::wstring path = Directory();
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        const int invalidDots[] = { -1, 101, std::numeric_limits<int>::min(), std::numeric_limits<int>::max() };
        for (size_t i = 0; i < sizeof(invalidDots) / sizeof(invalidDots[0]); ++i) {
            e.settings.islandDotPercent = invalidDots[i];
            e.Save();
            Check(e.settings.islandDotPercent == 20 && e.settings.islandDotSize == 6, "Invalid island dot percentage resets to twenty percent.");
        }
        const double invalidScales[] = { std::numeric_limits<double>::quiet_NaN(),
            std::numeric_limits<double>::infinity(), -std::numeric_limits<double>::infinity(), 0.7499, 1.5001 };
        for (size_t i = 0; i < sizeof(invalidScales) / sizeof(invalidScales[0]); ++i) {
            e.settings.islandScale = invalidScales[i];
            e.Save();
            Check(e.settings.islandScale == 1.0, "Invalid island scale resets to one.");
        }
        e.settings.islandDotPercent = 0;
        e.settings.islandScale = 0.75;
        e.Save();
        Check(e.settings.islandDotSize == 3 && e.settings.islandScale == 0.75, "Minimum island sizing remains valid.");
    }
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        Check(e.settings.islandDotSize == 3 && e.settings.islandScale == 0.75, "Minimum island sizing survives restart.");
        e.settings.islandDotPercent = 100;
        e.settings.islandScale = 1.5;
        e.Save();
    }
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        Check(e.settings.islandDotSize == 20 && e.settings.islandScale == 1.5, "Maximum island sizing survives restart.");
    }
    // Version-one fixtures intentionally omit new keys or contain invalid values.
    WriteBytes(path + L"\\state.dat", "FreeIsland-Win7 1\nscene=0\nstartup=0\ndock=1\nanchor=0.75\nmonitor=\nchecksum=9ad87d7c\n");
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        Check(e.settings.islandDotPercent == 20 && e.settings.islandDotSize == 6 && e.settings.islandScale == 1.0 && e.settings.glassMode == 1,
              "Legacy missing sizing and glass keys use defaults.");
        Check(e.settings.scene == fi::Scene::Desktop && !e.settings.startup && e.settings.dock == fi::Dock::Left && e.settings.anchor == 0.75,
              "Legacy migration preserves prior preferences.");
        Check(e.TakeNotices().empty(), "Legacy file loads without backup recovery.");
    }
    WriteBytes(path + L"\\state.dat", "FreeIsland-Win7 1\nscene=0\nislandDotSize=2\nislandScale=nan\nchecksum=da5e0fb5\n");
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        Check(e.settings.scene == fi::Scene::Desktop && e.settings.islandDotSize == 6 && e.settings.islandScale == 1.0,
              "Loaded invalid dot and NaN scale use defaults without discarding settings.");
        Check(e.TakeNotices().empty(), "Invalid sizing is normalized instead of restoring unrelated backup.");
    }
    WriteBytes(path + L"\\state.dat", "FreeIsland-Win7 1\nscene=0\nislandDotSize=21\nislandScale=1.6\nchecksum=a7dabce2\n");
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        Check(e.settings.islandDotSize == 6 && e.settings.islandScale == 1.0, "Loaded sizing above maximum uses defaults.");
        Check(e.TakeNotices().empty(), "Numeric sizing bounds normalize during load.");
    }
}

void DotPercentAndGlassMigration() {
    Clock time;
    const std::wstring path = Directory();
    const int oldSizes[] = { 4, 3, 20, 7, 6 };
    const int percentages[] = { 20, 0, 100, 24, 18 };
    const int pixels[] = { 6, 3, 20, 7, 6 };
    for (size_t i = 0; i < sizeof(oldSizes) / sizeof(oldSizes[0]); ++i) {
        SettingsFixture(path, "scene=0\nstartup=0\nislandDotSize=" + std::to_string(oldSizes[i]) + "\n");
        {
            fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
            Check(e.settings.islandDotPercent == percentages[i] && e.settings.islandDotSize == pixels[i], "Legacy pixel size maps to percentage.");
            Check(e.settings.scene == fi::Scene::Desktop && !e.settings.startup && e.settings.glassMode == 1, "Migration preserves preferences and defaults glass to lite.");
            Check(e.TakeNotices().empty(), "Pixel migration uses primary state.");
            e.Save();
        }
        fi::Engine restored(path, true, time.Wall(), time.Mono(), time.Shutdown());
        Check(restored.settings.islandDotPercent == percentages[i] && restored.settings.islandDotSize == pixels[i], "Migrated percentage persists.");
    }
    SettingsFixture(path, "scene=0\nislandDotSize=4\nislandDotPercent=80\nglassMode=2\n");
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        Check(e.settings.islandDotPercent == 80 && e.settings.islandDotSize == 17 && e.settings.glassMode == 2,
              "Explicit percentage overrides conflicting legacy pixels.");
        for (int mode = 0; mode <= 2; ++mode) {
            e.settings.glassMode = mode;
            e.settings.islandDotPercent = 50;
            e.settings.islandDotSize = 3;
            e.Save();
            fi::Engine restored(path, true, time.Wall(), time.Mono(), time.Shutdown());
            Check(restored.settings.glassMode == mode && restored.settings.islandDotPercent == 50 && restored.settings.islandDotSize == 12,
                  "All glass modes persist and half-pixel mapping rounds upward.");
        }
        const int invalidModes[] = { -1, 3, std::numeric_limits<int>::min(), std::numeric_limits<int>::max() };
        for (size_t i = 0; i < sizeof(invalidModes) / sizeof(invalidModes[0]); ++i) {
            e.settings.glassMode = invalidModes[i];
            e.Save();
            Check(e.settings.glassMode == 1 && e.settings.islandDotPercent == 50, "Invalid glass mode resets without changing dot percentage.");
        }
    }
    SettingsFixture(path, "scene=0\nislandDotSize=20\nislandDotPercent=101\nglassMode=-1\n");
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        Check(e.settings.scene == fi::Scene::Desktop && e.settings.islandDotPercent == 20 && e.settings.islandDotSize == 6 && e.settings.glassMode == 1,
              "Loaded invalid percentage and glass mode normalize independently of legacy pixels.");
        Check(e.TakeNotices().empty(), "Invalid new preference values do not discard valid settings.");
    }
}

void GlassAppearancePersistence() {
    Clock time;
    const std::wstring path = Directory();
    SettingsFixture(path, "scene=0\nstartup=0\nglassMode=2\nislandDotPercent=20\n");
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        Check(e.settings.glassRefraction == 50 && e.settings.glassTransparency == 65 && e.settings.glassHighlight == 55,
              "Legacy settings receive recommended material values.");
        Check(e.settings.glassMode == 2 && e.settings.scene == fi::Scene::Desktop && !e.settings.startup &&
              e.settings.islandDotSize == 6 && e.TakeNotices().empty(), "Migration preserves scene, mode and dot size.");
        const int values[][3] = {{0,100,0},{100,0,100},{37,81,24}};
        for (const auto& value : values) {
            e.settings.glassRefraction=value[0];e.settings.glassTransparency=value[1];e.settings.glassHighlight=value[2];e.Save();
            fi::Engine restored(path, true, time.Wall(), time.Mono(), time.Shutdown());
            Check(restored.settings.glassRefraction==value[0] && restored.settings.glassTransparency==value[1] &&
                  restored.settings.glassHighlight==value[2], "Independent material values and endpoints survive restart.");
            Check(restored.settings.glassMode==2 && restored.settings.islandDotSize==6, "Appearance edits leave mode and hit-size preference intact.");
        }
        const int invalid[] = {-1,101,std::numeric_limits<int>::min(),std::numeric_limits<int>::max()};
        for(int value : invalid) {
            e.settings.glassRefraction=value;e.settings.glassTransparency=value;e.settings.glassHighlight=value;e.Save();
            Check(e.settings.glassRefraction==50 && e.settings.glassTransparency==65 && e.settings.glassHighlight==55,
                  "Out-of-range in-memory material values return to recommendations.");
        }
    }
    const char* invalid[] = {"-1","101","1.5","nan","invalid","9999999999999999999999"};
    for(const char* value : invalid) {
        SettingsFixture(path,std::string("scene=0\nglassMode=2\nglassRefraction=")+value+"\nglassTransparency=71\nglassHighlight=29\n");
        fi::Engine e(path,true,time.Wall(),time.Mono(),time.Shutdown());
        Check(e.settings.glassRefraction==50 && e.settings.glassTransparency==71 && e.settings.glassHighlight==29 &&
              e.settings.scene==fi::Scene::Desktop && e.settings.glassMode==2 && e.TakeNotices().empty(),
              "Malformed material input normalizes independently without losing unrelated preferences.");
        SettingsFixture(path,std::string("glassRefraction=31\nglassTransparency=")+value+"\nglassHighlight="+value+"\n");
        fi::Engine restored(path,true,time.Wall(),time.Mono(),time.Shutdown());
        Check(restored.settings.glassRefraction==31 && restored.settings.glassTransparency==65 && restored.settings.glassHighlight==55,
              "Malformed transparency and highlight safely use recommendations.");
    }
}

void Validation() {
    Clock time;
    fi::Engine e(Directory(), true, time.Wall(), time.Mono(), time.Shutdown());
    Reject([&]() { e.StartCountdown(0); });
    Reject([&]() { e.StartCountdown(-1); });
    Reject([&]() { e.StartCountdown(fi::Engine::MaximumCountdownMs + 1); });
    Reject([&]() { e.AddReminder(L" \t\n ", time.wall + 1, false); });
    Reject([&]() { e.AddReminder(std::wstring(121, L'x'), time.wall + 1, false); });
    Reject([&]() { e.AddReminder(L"Past", time.wall, false); });
    Reject([&]() { e.ScheduleShutdown(time.wall - 1); });
    e.StartCountdown(fi::Engine::MaximumCountdownMs);
    Equal(fi::Engine::MaximumCountdownMs, e.CountdownMs(), "Seven days accepted.");
}

void StopwatchMonotonic() {
    Clock time;
    fi::Engine e(Directory(), true, time.Wall(), time.Mono(), time.Shutdown());
    e.ToggleStopwatch();
    time.Advance(12000);
    Equal(12000, e.StopwatchMs(), "Running stopwatch.");
    time.wall += 3600000;
    Equal(12000, e.StopwatchMs(), "Wall clock correction does not change stopwatch.");
    e.ToggleStopwatch();
    time.Advance(120000);
    Equal(12000, e.StopwatchMs(), "Paused stopwatch.");
    e.ToggleStopwatch();
    time.Advance(8000);
    time.wall -= 7200000;
    e.ToggleStopwatch();
    Equal(20000, e.StopwatchMs(), "Resumed stopwatch ignores wall clock rollback.");
    e.ResetStopwatch();
    Check(!e.stopwatchRunning, "Reset stops stopwatch.");
    Equal(0, e.StopwatchMs(), "Reset clears stopwatch.");
}

void CountdownPauseRestore() {
    Clock time;
    const std::wstring path = Directory();
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        e.StartCountdown(120000);
        time.Advance(30000);
        e.PauseCountdown();
        time.Advance(3600000);
        Check(e.countdownActive && !e.countdownRunning, "Countdown paused.");
        Equal(90000, e.CountdownMs(), "Paused countdown stable.");
    }
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        Check(e.countdownActive && !e.countdownRunning, "Pause restored.");
        Equal(90000, e.CountdownMs(), "Paused remaining restored.");
        e.PauseCountdown(); time.Advance(89000); e.Tick();
        Check(Notices(e, "countdown").empty(), "No early countdown completion.");
        time.Advance(1000); e.Tick(); e.Tick();
        Check(Notices(e, "countdown").size() == 1 && !e.countdownActive, "Countdown completes once.");
    }
}

void CountdownReplaceCancelAndExpiry() {
    Clock time;
    const std::wstring path = Directory();
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        e.StartCountdown(5000);
        e.StartCountdown(10000);
        time.Advance(5000); e.Tick();
        Check(Notices(e, "countdown").empty(), "Starting again replaces previous countdown.");
        Equal(5000, e.CountdownMs(), "Single active countdown remaining.");
        e.CancelCountdown(); time.Advance(5000); e.Tick();
        Check(!e.countdownActive && Notices(e, "countdown").empty(), "Cancellation does not notify.");
        e.StartCountdown(60000);
    }
    time.Advance(10800000);
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        Equal(0, e.CountdownMs(), "Overdue countdown remaining is zero.");
        e.Tick(); e.Tick();
        Check(Notices(e, "countdown").size() == 1 && !e.countdownActive, "Restart catches expiry once.");
    }
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        e.Tick(); Check(Notices(e, "countdown").empty(), "Acknowledged expiry remains cleared after restart.");
    }
}

void Reminders() {
    Clock time;
    const std::wstring path = Directory();
    uint64_t firstId = 0;
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        e.AddReminder(L"一次提醒", time.wall + 60000, false);
        e.AddReminder(L"每日提醒", time.wall + 120000, true);
        firstId = e.reminders[0].id;
        SYSTEMTIME originalDaily = fi::LocalTime(e.reminders[1].dueMs);
        time.Advance(60000); e.Tick(); e.Tick();
        Check(Notices(e, "reminder").size() == 1 && e.reminders[0].completed, "One-time reminder fires once.");
        time.Advance(3LL * 86400000 + 120000); e.Tick(); e.Tick();
        Check(Notices(e, "reminder").size() == 1 && !e.reminders[1].completed, "Daily catchup coalesced.");
        Check(e.reminders[1].dueMs > time.wall, "Daily moved beyond now.");
        SYSTEMTIME nextDaily = fi::LocalTime(e.reminders[1].dueMs);
        Check(nextDaily.wHour == originalDaily.wHour && nextDaily.wMinute == originalDaily.wMinute, "Daily local time preserved.");
    }
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        e.Tick(); Check(Notices(e, "reminder").empty(), "Restart does not repeat acknowledged reminders.");
        e.RemoveReminder(firstId);
        Check(e.reminders.size() == 1 && !e.reminders[0].completed, "Reminder removal.");
        e.AddReminder(L"第三条", time.wall + 60000, false);
        Check(e.reminders[0].id != e.reminders[1].id && e.reminders[1].id != firstId, "Reminder IDs remain unique after restart.");
    }
}

void SafeShutdown() {
    Clock time;
    fi::Engine e(Directory(), true, time.Wall(), time.Mono(), time.Shutdown());
    Check(e.SafeMode(), "Safe mode accessor.");
    e.ScheduleShutdown(time.wall + 70000); e.Tick();
    Check(Notices(e, "shutdown").empty(), "No early shutdown warning.");
    time.Advance(59999); e.Tick();
    Check(Notices(e, "shutdown").empty() && !e.ShutdownVisible(), "No warning or island at 10.001 seconds.");
    time.Advance(1); e.Tick(); e.Tick();
    std::vector<fi::Notice> warning = Notices(e, "shutdown");
    Check(warning.size() == 1 && warning[0].title == L"即将自动关机" && e.ShutdownVisible(), "Single final-10-second warning.");
    e.CancelShutdown(); time.Advance(60000); e.Tick();
    Check(!e.shutdownAt && Notices(e, "shutdown").empty() && time.calls == 0, "Cancellation takes effect.");
    e.ScheduleShutdown(time.wall + 1000); e.Tick();
    Check(Notices(e, "shutdown").size() == 1, "Short schedule warns immediately.");
    time.Advance(1000); e.Tick(); e.Tick();
    std::vector<fi::Notice> simulated = Notices(e, "shutdown");
    Check(simulated.size() == 1 && simulated[0].title == L"已模拟定时关机", "Safe mode simulates once.");
    Check(time.calls == 0 && !e.shutdownAt, "Safe mode never calls shutdown action.");
}

void ShutdownDispatchAndFailure() {
    Clock time;
    fi::Engine e(Directory(), false, time.Wall(), time.Mono(), time.Shutdown());
    e.ScheduleShutdown(time.wall + 10000);
    time.Advance(9000); e.Tick();
    Check(time.calls == 0, "Normal shutdown not early.");
    time.Advance(1000); e.Tick(); e.Tick();
    Check(time.calls == 1 && !e.shutdownAt, "Normal injected action exactly once.");
    e.ScheduleShutdown(time.wall + 10000); e.CancelShutdown();
    time.Advance(20000); e.Tick();
    Check(time.calls == 1, "Cancelled normal plan never dispatches.");
    int failedCalls = 0;
    fi::Engine failed(Directory(), false, time.Wall(), time.Mono(), [&]() { ++failedCalls; throw std::runtime_error("Injected failure"); });
    failed.ScheduleShutdown(time.wall + 1000); failed.Tick(); failed.TakeNotices();
    time.Advance(1000); failed.Tick(); failed.Tick();
    std::vector<fi::Notice> notices = Notices(failed, "shutdown");
    Check(failedCalls == 1 && !failed.shutdownAt && notices.size() == 1 && notices[0].title == L"关机未执行", "Failure is reported without retry.");
}

void UnwarnedShutdown() {
    Clock time;
    fi::Engine e(Directory(), false, time.Wall(), time.Mono(), time.Shutdown());
    e.ScheduleShutdown(time.wall + 90000); e.Tick();
    time.Advance(91000); e.Tick();
    std::vector<fi::Notice> notices = Notices(e, "shutdown");
    Check(time.calls == 0 && !e.shutdownAt && notices.size() == 1 && notices[0].title == L"已取消未预警关机", "Missed warning cancels even with short gap.");
    e.ScheduleShutdown(time.wall + 1000); time.Advance(2000); e.Tick();
    Check(time.calls == 0 && !e.shutdownAt, "Short plan cannot bypass warning.");
}

void MissedShutdown() {
    Clock time;
    fi::Engine e(Directory(), false, time.Wall(), time.Mono(), time.Shutdown());
    e.ScheduleShutdown(time.wall + 300000); time.Advance(7200000); e.Tick();
    std::vector<fi::Notice> notices = Notices(e, "shutdown");
    Check(time.calls == 0 && !e.shutdownAt && notices.size() == 1 && notices[0].title == L"已取消过期关机", "Sleep expiry cancels.");
    e.ScheduleShutdown(time.wall + 1000); e.Tick(); e.TakeNotices();
    time.wall += 1000; time.mono += 3600000; e.Tick();
    notices = Notices(e, "shutdown");
    Check(time.calls == 0 && notices.size() == 1 && notices[0].title == L"已取消过期关机", "Monotonic sleep gap cancels despite corrected wall clock.");
}

void ShutdownAndStopwatchNeverRestored() {
    Clock time;
    const std::wstring path = Directory();
    {
        fi::Engine e(path, false, time.Wall(), time.Mono(), time.Shutdown());
        e.ScheduleShutdown(time.wall + 1000); e.Tick();
        e.ToggleStopwatch(); time.Advance(500); e.Save();
        Check(ReadBytes(path + L"\\state.dat").find("shutdown") == std::string::npos, "No shutdown plan stored.");
    }
    time.Advance(2000);
    {
        fi::Engine e(path, false, time.Wall(), time.Mono(), time.Shutdown());
        e.Tick();
        Check(!e.shutdownAt && time.calls == 0, "Shutdown never restored.");
        Check(!e.stopwatchRunning && e.StopwatchMs() == 0, "Stopwatch starts clean.");
    }
}

void CorruptBackupAndChecksum() {
    Clock time;
    const std::wstring path = Directory();
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        e.settings.dock = fi::Dock::Left; e.Save(); e.Save();
    }
    std::string corrupted = ReadBytes(path + L"\\state.dat");
    const size_t anchor = corrupted.find("anchor=0.5");
    Check(anchor != std::string::npos, "Fixture has anchor.");
    corrupted[anchor + 9] = '9'; // Valid numeric text, invalid checksum.
    WriteBytes(path + L"\\state.dat", corrupted);
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        Check(e.settings.dock == fi::Dock::Left && e.settings.anchor == 0.5, "Checksum corruption recovered from backup.");
        std::vector<fi::Notice> notices = Notices(e, "info");
        Check(notices.size() == 1 && notices[0].title == L"已恢复备份", "Recovery is reported once.");
        e.Save();
    }
    WriteBytes(path + L"\\state.dat", "truncated");
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        Check(e.settings.dock == fi::Dock::Left, "Recovery did not overwrite good backup with corrupt data.");
    }
    WriteBytes(path + L"\\state.dat", "broken");
    WriteBytes(path + L"\\state.dat.bak", "also broken");
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        Check(e.settings.scene == fi::Scene::Classroom && e.settings.dock == fi::Dock::Top, "Both corrupt files use defaults.");
        std::vector<fi::Notice> notices = Notices(e, "info");
        Check(notices.size() == 1 && notices[0].title == L"设置已恢复默认", "Corrupt files reported.");
    }
}

void LocalTimeRoundtrip() {
    Clock time;
    SYSTEMTIME local = fi::LocalTime(time.wall);
    Equal(time.wall, fi::LocalToMs(local), "Local and UTC dates roundtrip.");
    Check(fi::NowMs() > 1700000000000LL, "System clock uses Unix milliseconds.");
    SYSTEMTIME bad = local;
    bad.wMonth = 13;
    Reject([&]() { fi::LocalToMs(bad); });
}

void TaskThumbnailState() {
    Clock time; const std::wstring path = Directory();
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        Check(e.settings.activeDotDesktop == 36 && e.settings.activeDotClassroom == 48, "Per-scene thumbnail defaults.");
        e.settings.activeDotDesktop = 30; e.settings.activeDotClassroom = 50;
        e.StartCountdown(120000); time.Advance(30000); e.PauseCountdown();
        Equal(120000, e.CountdownDurationMs(), "Ring denominator keeps original duration after pause.");
        Equal(90000, e.CountdownMs(), "Ring numerator is paused remaining time.");
    }
    {
        fi::Engine e(path, true, time.Wall(), time.Mono(), time.Shutdown());
        Check(e.settings.activeDotDesktop == 30 && e.settings.activeDotClassroom == 50, "Thumbnail settings survive restart.");
        Equal(120000, e.CountdownDurationMs(), "Original duration survives restart.");
        e.settings.activeDotDesktop = 29; e.settings.activeDotClassroom = 51; e.Save();
        Check(e.settings.activeDotDesktop == 36 && e.settings.activeDotClassroom == 48, "Invalid sizes restore scene defaults.");
        e.PauseCountdown(); time.Advance(10000); Equal(120000, e.CountdownDurationMs(), "Resume does not reset ring.");
        e.StartCountdown(20000); Equal(20000, e.CountdownDurationMs(), "Replacement resets original duration.");
        e.CancelCountdown(); Equal(0, e.CountdownDurationMs(), "Cancel clears ring state.");
    }
    const std::wstring legacy = Directory();
    SettingsFixture(legacy, "countdownActive=1\ncountdownRunning=0\npausedCountdown=15000\n");
    fi::Engine old(legacy, true, time.Wall(), time.Mono(), time.Shutdown());
    Equal(15000, old.CountdownDurationMs(), "Legacy countdown establishes a valid ring denominator.");
}

void Run(const char* name, const std::function<void()>& test) {
    test(); ++passed; std::cout << "PASS " << name << std::endl;
}

void Cleanup() {
    // Only the unique child directories created by this process are touched.
    for (size_t i = 0; i < directories.size(); ++i) {
        if (directories[i].compare(0, testRoot.size() + 1, testRoot + L"\\") != 0) continue;
        WIN32_FIND_DATAW entry = {};
        HANDLE search = FindFirstFileW((directories[i] + L"\\*").c_str(), &entry);
        if (search != INVALID_HANDLE_VALUE) {
            do {
                if (!(entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY))
                    DeleteFileW((directories[i] + L"\\" + entry.cFileName).c_str());
            } while (FindNextFileW(search, &entry));
            FindClose(search);
        }
        RemoveDirectoryW(directories[i].c_str());
    }
    if (!testRoot.empty()) RemoveDirectoryW(testRoot.c_str());
}
} // namespace

int main() {
    int status = 0;
    try {
        wchar_t temporary[MAX_PATH + 1] = {}, suffix[100] = {};
        DWORD count = GetTempPathW(MAX_PATH, temporary);
        Check(count && count < MAX_PATH, "Cannot find temp path.");
        swprintf(suffix, 100, L"FreeIsland.Win7.CoreTests.%lu.%llu", GetCurrentProcessId(), static_cast<unsigned long long>(GetTickCount64()));
        testRoot = std::wstring(temporary) + suffix;
        Check(CreateDirectoryW(testRoot.c_str(), NULL) != FALSE, "Cannot create unique test root.");
        Run("defaults and Unicode persistence", DefaultsAndPersistence);
        Run("scene, docking and numeric validation", SettingsValidation);
        Run("island dot and scale persistence, bounds and legacy compatibility", IslandSizingSettings);
        Run("dot percentage and glass modes, migration and normalization", DotPercentAndGlassMigration);
        Run("glass appearance persistence, legacy defaults and independent validation", GlassAppearancePersistence);
        Run("duration and future-date validation", Validation);
        Run("monotonic stopwatch pause, resume, reset and clock corrections", StopwatchMonotonic);
        Run("countdown pause and restart", CountdownPauseRestore);
        Run("single countdown replacement, cancellation and restart expiry", CountdownReplaceCancelAndExpiry);
        Run("one-time and daily local-time reminders", Reminders);
        Run("safe shutdown prewarning and cancellation", SafeShutdown);
        Run("normal injected shutdown once and failure handling", ShutdownDispatchAndFailure);
        Run("unwarned shutdown always cancels", UnwarnedShutdown);
        Run("missed shutdown cancellation with both clocks", MissedShutdown);
        Run("shutdown and stopwatch never restored", ShutdownAndStopwatchNeverRestored);
        Run("checksummed atomic backup recovery", CorruptBackupAndChecksum);
        Run("native local/UTC time conversion", LocalTimeRoundtrip);
        Run("thumbnail scene sizing, persistence, countdown ring pause and migration", TaskThumbnailState);
        std::cout << "PASS: " << passed << " native core tests. No registry or OS shutdown commands executed." << std::endl;
    } catch (const std::exception& error) {
        std::cerr << "FAIL: " << error.what() << std::endl;
        status = 1;
    }
    Cleanup();
    return status;
}
