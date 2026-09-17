#pragma once
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0601
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <cstdint>
#include <functional>
#include <string>
#include <vector>

namespace fi {

// All domain deadlines are Unix UTC milliseconds; display conversion is explicit.
int64_t NowMs();
SYSTEMTIME LocalTime(int64_t utcMs);
int64_t LocalToMs(const SYSTEMTIME& local);

enum class Scene { Desktop, Classroom };
enum class Dock { Top, Left, Right };

struct Settings {
    Scene scene = Scene::Classroom;
    bool sceneSelected = false;
    bool startup = true;
    bool sound = true;
    bool edgeHide = true;
    int ballX = -99999;
    int ballY = -99999;
    Dock dock = Dock::Top;
    double anchor = 0.5;
    int islandDotPercent = 20;
    int islandDotSize = 6; // Derived physical pixels, independent of monitor DPI.
    int activeDotDesktop = 36;
    int activeDotClassroom = 48;
    int glassMode = 1; // 0 = off, 1 = lite, 2 = water motion (no desktop refraction).
    int glassRefraction = 50; // Curved rim thickness/appearance on Windows 7.
    int glassTransparency = 65;
    int glassHighlight = 55;
    double islandScale = 1.0;
    std::wstring monitor;
};

struct Reminder {
    uint64_t id = 0;
    std::wstring title;
    int64_t dueMs = 0;
    bool daily = false;
    bool completed = false;
};

struct Notice {
    std::wstring title;
    std::wstring message;
    std::string kind;
    bool urgent = false;
};

// UI-thread service. Stopwatch uses monotonic time, deadlines use wall time.
class Engine {
public:
    Engine(const std::wstring& dataDirectory, bool safe,
           std::function<int64_t()> wallClock = std::function<int64_t()>(),
           std::function<uint64_t()> monotonicClock = std::function<uint64_t()>(),
           std::function<void()> shutdownAction = std::function<void()>());
    ~Engine();
    Engine(const Engine&) = delete;
    Engine& operator=(const Engine&) = delete;

    Settings settings;
    std::vector<Reminder> reminders;
    bool stopwatchRunning = false;
    bool countdownActive = false;
    bool countdownRunning = false;
    int64_t shutdownAt = 0; // Intentionally never persisted.

    int64_t StopwatchMs() const;
    int64_t CountdownMs() const;
    int64_t CountdownDurationMs() const { return countdownDuration_; }
    bool ShutdownVisible() const { const int64_t left = shutdownAt - wallClock_(); return shutdownAt && left > 0 && left <= 10000; }
    void ToggleStopwatch();
    void ResetStopwatch();
    void StartCountdown(int64_t durationMs);
    void PauseCountdown(); // Toggles pause/resume of the active countdown.
    void CancelCountdown();
    void AddReminder(const std::wstring& title, int64_t dueMs, bool daily);
    void RemoveReminder(uint64_t id);
    void ScheduleShutdown(int64_t dueMs);
    void CancelShutdown();
    void Tick();
    void Save();
    std::vector<Notice> TakeNotices();
    bool SafeMode() const { return safe_; }
    static const int64_t MaximumCountdownMs = 7LL * 24 * 60 * 60 * 1000;

private:
    std::wstring statePath_;
    bool safe_;
    bool shutdownWarned_ = false;
    bool recoveredBackup_ = false;
    uint64_t stopwatchStart_ = 0;
    int64_t stopwatchAccumulated_ = 0;
    int64_t countdownDeadline_ = 0;
    int64_t pausedCountdown_ = 0;
    int64_t countdownDuration_ = 0;
    int64_t lastTick_ = 0;
    uint64_t lastTickMonotonic_ = 0;
    uint64_t nextId_ = 1;
    std::function<int64_t()> wallClock_;
    std::function<uint64_t()> monotonicClock_;
    std::function<void()> shutdownAction_;
    std::vector<Notice> notices_;

    void NormalizeSettings();
    void Load();
    void ClearCountdown();
    void FinishCountdown();
    void Notify(const std::wstring& title, const std::wstring& message,
                const char* kind, bool urgent);
};
} // namespace fi
