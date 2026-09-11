#include "core.h"
#include <reason.h>
#include <algorithm>
#include <cmath>
#include <cwctype>
#include <iomanip>
#include <limits>
#include <locale>
#include <map>
#include <set>
#include <sstream>
#include <stdexcept>

namespace fi {
namespace {
const int64_t EpochOffsetMs = 11644473600000LL;
const int64_t DayMs = 86400000LL;
const int64_t LastDateMs = 253402300799999LL;
const size_t MaximumStateBytes = 4 * 1024 * 1024;
const size_t MaximumReminders = 1000;

int64_t FileTimeMs(const FILETIME& ft) {
    ULARGE_INTEGER value;
    value.LowPart = ft.dwLowDateTime;
    value.HighPart = ft.dwHighDateTime;
    return static_cast<int64_t>(value.QuadPart / 10000ULL) - EpochOffsetMs;
}

FILETIME ToFileTime(int64_t ms) {
    if (ms < 0 || ms > LastDateMs) throw std::invalid_argument("Invalid date.");
    ULARGE_INTEGER value;
    value.QuadPart = static_cast<uint64_t>(ms + EpochOffsetMs) * 10000ULL;
    FILETIME result = { value.LowPart, value.HighPart };
    return result;
}

int64_t AsUtcMs(const SYSTEMTIME& st) {
    FILETIME ft;
    if (!SystemTimeToFileTime(&st, &ft)) throw std::invalid_argument("Invalid date.");
    const int64_t ms = FileTimeMs(ft);
    if (ms < 0 || ms > LastDateMs) throw std::invalid_argument("Invalid date.");
    return ms;
}

SYSTEMTIME AsUtcTime(int64_t ms) {
    FILETIME ft = ToFileTime(ms);
    SYSTEMTIME st = {};
    if (!FileTimeToSystemTime(&ft, &st)) throw std::invalid_argument("Invalid date.");
    return st;
}

std::wstring Trim(const std::wstring& value) {
    size_t begin = 0, end = value.size();
    while (begin < end && std::iswspace(value[begin])) ++begin;
    while (end > begin && std::iswspace(value[end - 1])) --end;
    return value.substr(begin, end - begin);
}

std::runtime_error WinError(const char* operation) {
    const DWORD code = GetLastError();
    std::ostringstream text;
    text << operation << " (Windows " << code << ").";
    return std::runtime_error(text.str());
}

void MakeDirectory(const std::wstring& path) {
    if (path.empty()) throw std::invalid_argument("Data directory is empty.");
    DWORD attributes = GetFileAttributesW(path.c_str());
    if (attributes != INVALID_FILE_ATTRIBUTES) {
        if (!(attributes & FILE_ATTRIBUTE_DIRECTORY)) throw std::runtime_error("Data path is not a directory.");
        return;
    }
    const size_t cut = path.find_last_of(L"\\/");
    if (cut != std::wstring::npos && cut > 2) MakeDirectory(path.substr(0, cut));
    if (!CreateDirectoryW(path.c_str(), NULL) && GetLastError() != ERROR_ALREADY_EXISTS)
        throw WinError("Cannot create data directory");
}

uint32_t Checksum(const std::string& value) {
    uint32_t hash = 2166136261U;
    for (size_t i = 0; i < value.size(); ++i) {
        hash ^= static_cast<unsigned char>(value[i]);
        hash *= 16777619U;
    }
    return hash;
}

std::string HexText(const std::wstring& value) {
    const char* digits = "0123456789abcdef";
    std::string result;
    result.reserve(value.size() * 4);
    for (size_t i = 0; i < value.size(); ++i) {
        uint16_t c = static_cast<uint16_t>(value[i]);
        for (int shift = 12; shift >= 0; shift -= 4) result += digits[(c >> shift) & 15];
    }
    return result;
}

bool ReadHex(const std::string& value, std::wstring& result, size_t maxChars) {
    if (value.size() % 4 || value.size() / 4 > maxChars) return false;
    result.clear();
    for (size_t at = 0; at < value.size(); at += 4) {
        unsigned int character = 0;
        for (size_t digit = 0; digit < 4; ++digit) {
            char c = value[at + digit];
            unsigned int v;
            if (c >= '0' && c <= '9') v = c - '0';
            else if (c >= 'a' && c <= 'f') v = c - 'a' + 10;
            else if (c >= 'A' && c <= 'F') v = c - 'A' + 10;
            else return false;
            character = (character << 4) | v;
        }
        if (!character) return false;
        result += static_cast<wchar_t>(character);
    }
    return true;
}

template<typename T> bool Number(const std::string& text, T& value) {
    if (text.empty() || text.size() > 40 || text.find_first_of(" \t\r\n") != std::string::npos) return false;
    std::istringstream stream(text);
    stream.imbue(std::locale::classic());
    stream >> value;
    return stream && stream.peek() == std::char_traits<char>::eof();
}

bool Bool(const std::string& value, bool& target) {
    if (value != "0" && value != "1") return false;
    target = value == "1";
    return true;
}

struct SavedState {
    Settings settings;
    std::vector<Reminder> reminders;
    bool active = false;
    bool running = false;
    int64_t deadline = 0;
    int64_t paused = 0;
};

bool ParseState(const std::string& file, SavedState& state) {
    const std::string header = "FreeIsland-Win7 1\n";
    if (file.compare(0, header.size(), header) != 0) return false;
    const size_t footer = file.rfind("checksum=");
    if (footer == std::string::npos || footer < header.size() || file[footer - 1] != '\n') return false;
    if (file.size() != footer + 18 || file[file.size() - 1] != '\n') return false;
    uint32_t expected = 0;
    std::istringstream hash(file.substr(footer + 9, 8));
    hash >> std::hex >> expected;
    if (!hash || hash.peek() != std::char_traits<char>::eof() || expected != Checksum(file.substr(0, footer))) return false;
    std::istringstream lines(file.substr(header.size(), footer - header.size()));
    std::set<std::string> keys;
    std::set<uint64_t> ids;
    std::string line;
    while (std::getline(lines, line)) {
        const size_t split = line.find('=');
        if (split == std::string::npos || line.size() > 4096) return false;
        const std::string key = line.substr(0, split), value = line.substr(split + 1);
        if (key != "reminder" && !keys.insert(key).second) return false;
        int enumeration = 0;
        if (key == "scene") {
            if (!Number(value, enumeration)) return false;
            state.settings.scene = static_cast<Scene>(enumeration);
        } else if (key == "sceneSelected") { if (!Bool(value, state.settings.sceneSelected)) return false; }
        else if (key == "startup") { if (!Bool(value, state.settings.startup)) return false; }
        else if (key == "sound") { if (!Bool(value, state.settings.sound)) return false; }
        else if (key == "edgeHide") { if (!Bool(value, state.settings.edgeHide)) return false; }
        else if (key == "ballX") { if (!Number(value, state.settings.ballX)) return false; }
        else if (key == "ballY") { if (!Number(value, state.settings.ballY)) return false; }
        else if (key == "dock") {
            if (!Number(value, enumeration)) return false;
            state.settings.dock = static_cast<Dock>(enumeration);
        } else if (key == "anchor") { if (!Number(value, state.settings.anchor)) return false; }
        else if (key == "islandDotSize") {
            if (!Number(value, state.settings.islandDotSize)) state.settings.islandDotSize = 4;
        } else if (key == "islandDotPercent") {
            if (!Number(value, state.settings.islandDotPercent)) state.settings.islandDotPercent = 20;
        } else if (key == "glassMode") {
            if (!Number(value, state.settings.glassMode)) state.settings.glassMode = 1;
        } else if (key == "islandScale") {
            if (!Number(value, state.settings.islandScale)) state.settings.islandScale = 1.0;
        }
        else if (key == "monitor") { if (!ReadHex(value, state.settings.monitor, 260)) return false; }
        else if (key == "countdownActive") { if (!Bool(value, state.active)) return false; }
        else if (key == "countdownRunning") { if (!Bool(value, state.running)) return false; }
        else if (key == "countdownDeadline") { if (!Number(value, state.deadline)) return false; }
        else if (key == "pausedCountdown") { if (!Number(value, state.paused)) return false; }
        else if (key == "reminder") {
            if (state.reminders.size() >= MaximumReminders) return false;
            std::string fields[5];
            size_t begin = 0;
            for (int i = 0; i < 4; ++i) {
                size_t end = value.find('|', begin);
                if (end == std::string::npos) return false;
                fields[i] = value.substr(begin, end - begin);
                begin = end + 1;
            }
            fields[4] = value.substr(begin);
            Reminder reminder;
            if (fields[0].empty() || fields[0][0] == '-' || !Number(fields[0], reminder.id) || !reminder.id ||
                reminder.id == std::numeric_limits<uint64_t>::max() || !ids.insert(reminder.id).second ||
                !Number(fields[1], reminder.dueMs) || reminder.dueMs <= 0 || reminder.dueMs > LastDateMs ||
                !Bool(fields[2], reminder.daily) || !Bool(fields[3], reminder.completed) ||
                !ReadHex(fields[4], reminder.title, 120) || Trim(reminder.title).empty()) return false;
            state.reminders.push_back(reminder);
        } else return false;
    }
    if (!keys.count("islandDotPercent")) {
        const int oldSize = state.settings.islandDotSize;
        state.settings.islandDotPercent = keys.count("islandDotSize") && oldSize >= 3 && oldSize <= 20 && oldSize != 4
            ? ((oldSize - 3) * 100 + 8) / 17 : 20;
    }
    return true;
}

bool ReadState(const std::wstring& path, SavedState& state) {
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) return false;
    LARGE_INTEGER length = {};
    bool success = GetFileSizeEx(file, &length) && length.QuadPart > 0 && length.QuadPart <= MaximumStateBytes;
    std::string bytes;
    if (success) {
        bytes.resize(static_cast<size_t>(length.QuadPart));
        DWORD count = 0;
        success = ReadFile(file, &bytes[0], static_cast<DWORD>(bytes.size()), &count, NULL) && count == bytes.size();
    }
    CloseHandle(file);
    if (!success) return false;
    SavedState parsed;
    if (!ParseState(bytes, parsed)) return false;
    state = parsed;
    return true;
}

void AtomicWrite(const std::wstring& path, const std::string& bytes, bool preserveBackup) {
    std::wostringstream unique;
    unique << path << L"." << GetCurrentProcessId() << L"." << GetTickCount64() << L".tmp";
    const std::wstring temporary = unique.str(), backup = path + L".bak", backupTemporary = temporary + L".bak";
    HANDLE file = CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) throw WinError("Cannot create settings file");
    DWORD written = 0;
    bool success = WriteFile(file, bytes.data(), static_cast<DWORD>(bytes.size()), &written, NULL) && written == bytes.size();
    if (success) success = FlushFileBuffers(file) != FALSE;
    DWORD error = success ? ERROR_SUCCESS : GetLastError();
    CloseHandle(file);
    if (!success) {
        DeleteFileW(temporary.c_str());
        SetLastError(error);
        throw WinError("Cannot write settings");
    }
    try {
        if (!preserveBackup && GetFileAttributesW(path.c_str()) != INVALID_FILE_ATTRIBUTES) {
            if (!CopyFileW(path.c_str(), backupTemporary.c_str(), TRUE)) throw WinError("Cannot create settings backup");
            if (!MoveFileExW(backupTemporary.c_str(), backup.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
                throw WinError("Cannot replace settings backup");
        }
        if (!MoveFileExW(temporary.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
            throw WinError("Cannot save settings");
    } catch (...) {
        DeleteFileW(temporary.c_str());
        DeleteFileW(backupTemporary.c_str());
        throw;
    }
}

int64_t NextDaily(int64_t original, int64_t now) {
    SYSTEMTIME localDue = LocalTime(original), localNow = LocalTime(now);
    const int64_t originalWall = AsUtcMs(localDue);
    localDue.wHour = localDue.wMinute = localDue.wSecond = localDue.wMilliseconds = 0;
    localNow.wHour = localNow.wMinute = localNow.wSecond = localNow.wMilliseconds = 0;
    const int64_t days = std::max<int64_t>(1, (AsUtcMs(localNow) - AsUtcMs(localDue)) / DayMs);
    int64_t nextWall = originalWall + days * DayMs;
    int64_t next = LocalToMs(AsUtcTime(nextWall));
    if (next <= now) next = LocalToMs(AsUtcTime(nextWall + DayMs));
    return next;
}

void ShutdownWindows() {
#ifdef FI_CORE_TESTING
    // Test executables contain no operating-system shutdown implementation.
    throw std::runtime_error("Operating-system shutdown is unavailable in test builds.");
#else
    HANDLE token = NULL;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &token))
        throw WinError("Cannot open shutdown privilege token");
    struct TokenScope {
        HANDLE handle;
        TOKEN_PRIVILEGES previous;
        bool changed;
        ~TokenScope() {
            if (changed) AdjustTokenPrivileges(handle, FALSE, &previous, 0, NULL, NULL);
            CloseHandle(handle);
        }
    } scope = { token, {}, false };
    TOKEN_PRIVILEGES requested = {};
    requested.PrivilegeCount = 1;
    if (!LookupPrivilegeValueW(NULL, SE_SHUTDOWN_NAME, &requested.Privileges[0].Luid))
        throw WinError("Cannot locate shutdown privilege");
    requested.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
    DWORD previousSize = sizeof(scope.previous);
    SetLastError(ERROR_SUCCESS);
    const BOOL adjusted = AdjustTokenPrivileges(token, FALSE, &requested, sizeof(scope.previous),
                                                &scope.previous, &previousSize);
    const DWORD privilegeError = GetLastError();
    scope.changed = adjusted && scope.previous.PrivilegeCount > 0;
    if (!adjusted || privilegeError == ERROR_NOT_ALL_ASSIGNED || privilegeError != ERROR_SUCCESS) {
        SetLastError(privilegeError);
        throw WinError("Cannot enable shutdown privilege");
    }
    // No force flags: applications retain the opportunity to protect unsaved work.
    const DWORD reason = SHTDN_REASON_MAJOR_APPLICATION | SHTDN_REASON_MINOR_OTHER | SHTDN_REASON_FLAG_PLANNED;
    if (!ExitWindowsEx(EWX_POWEROFF, reason)) throw WinError("Windows rejected shutdown request");
#endif
}
} // namespace

int64_t NowMs() {
    FILETIME ft;
    GetSystemTimeAsFileTime(&ft);
    return FileTimeMs(ft);
}

SYSTEMTIME LocalTime(int64_t utcMs) {
    SYSTEMTIME utc = AsUtcTime(utcMs), local = {};
    if (!SystemTimeToTzSpecificLocalTime(NULL, &utc, &local)) throw std::invalid_argument("Cannot convert local date.");
    return local;
}

int64_t LocalToMs(const SYSTEMTIME& local) {
    SYSTEMTIME utc = {};
    if (!TzSpecificLocalTimeToSystemTime(NULL, &local, &utc)) throw std::invalid_argument("Invalid local date.");
    return AsUtcMs(utc);
}

Engine::Engine(const std::wstring& dataDirectory, bool safe, std::function<int64_t()> wallClock,
               std::function<uint64_t()> monotonicClock, std::function<void()> shutdownAction)
    : safe_(safe), wallClock_(wallClock ? wallClock : NowMs),
      monotonicClock_(monotonicClock ? monotonicClock : []() -> uint64_t { return GetTickCount64(); }),
      shutdownAction_(shutdownAction ? shutdownAction : ShutdownWindows) {
    MakeDirectory(dataDirectory);
    statePath_ = dataDirectory + L"\\state.dat";
    lastTick_ = wallClock_();
    lastTickMonotonic_ = monotonicClock_();
    Load();
}

Engine::~Engine() {
    shutdownAt = 0;
    try { Save(); } catch (...) { /* A destructor must not terminate the UI on a disk error. */ }
}

int64_t Engine::StopwatchMs() const {
    const uint64_t now = monotonicClock_();
    const uint64_t elapsed = stopwatchRunning && now >= stopwatchStart_ ? now - stopwatchStart_ : 0;
    const int64_t max = std::numeric_limits<int64_t>::max();
    if (elapsed > static_cast<uint64_t>(max - stopwatchAccumulated_)) return max;
    return stopwatchAccumulated_ + static_cast<int64_t>(elapsed);
}

int64_t Engine::CountdownMs() const {
    if (!countdownActive) return 0;
    return countdownRunning ? std::max<int64_t>(0, countdownDeadline_ - wallClock_()) : pausedCountdown_;
}

void Engine::ToggleStopwatch() {
    if (stopwatchRunning) {
        stopwatchAccumulated_ = StopwatchMs();
        stopwatchRunning = false;
    } else {
        stopwatchStart_ = monotonicClock_();
        stopwatchRunning = true;
    }
}

void Engine::ResetStopwatch() { stopwatchRunning = false; stopwatchAccumulated_ = 0; }

void Engine::StartCountdown(int64_t durationMs) {
    if (durationMs <= 0 || durationMs > MaximumCountdownMs) throw std::invalid_argument("Countdown must be between 1 millisecond and 7 days.");
    const int64_t now = wallClock_();
    if (now > LastDateMs - durationMs) throw std::invalid_argument("Countdown date is out of range.");
    countdownDeadline_ = now + durationMs;
    pausedCountdown_ = durationMs;
    countdownActive = countdownRunning = true;
    Save();
}

void Engine::PauseCountdown() {
    if (!countdownActive) return;
    if (countdownRunning) {
        pausedCountdown_ = CountdownMs();
        if (pausedCountdown_ <= 0) { FinishCountdown(); return; }
        countdownRunning = false;
        countdownDeadline_ = 0;
    } else {
        countdownDeadline_ = wallClock_() + pausedCountdown_;
        countdownRunning = true;
    }
    Save();
}

void Engine::ClearCountdown() {
    countdownActive = countdownRunning = false;
    countdownDeadline_ = pausedCountdown_ = 0;
}

void Engine::CancelCountdown() { ClearCountdown(); Save(); }

void Engine::AddReminder(const std::wstring& title, int64_t dueMs, bool daily) {
    std::wstring text = Trim(title);
    if (text.empty() || text.size() > 120 || text.find(L'\0') != std::wstring::npos)
        throw std::invalid_argument("Reminder title must contain 1 to 120 characters.");
    if (dueMs <= wallClock_() || dueMs > LastDateMs) throw std::invalid_argument("Reminder must use a future date.");
    if (reminders.size() >= MaximumReminders) throw std::invalid_argument("The reminder list is full (1000 items).");
    Reminder reminder;
    reminder.id = nextId_++;
    reminder.title = text;
    reminder.dueMs = dueMs;
    reminder.daily = daily;
    reminders.push_back(reminder);
    Save();
}

void Engine::RemoveReminder(uint64_t id) {
    const size_t count = reminders.size();
    reminders.erase(std::remove_if(reminders.begin(), reminders.end(), [id](const Reminder& r) { return r.id == id; }), reminders.end());
    if (reminders.size() != count) Save();
}

void Engine::ScheduleShutdown(int64_t dueMs) {
    const int64_t now = wallClock_();
    if (dueMs <= now || dueMs > LastDateMs) throw std::invalid_argument("Shutdown must use a future date.");
    shutdownAt = dueMs;
    shutdownWarned_ = false;
    lastTick_ = now;
    lastTickMonotonic_ = monotonicClock_();
}

void Engine::CancelShutdown() { shutdownAt = 0; shutdownWarned_ = false; }

void Engine::Notify(const std::wstring& title, const std::wstring& message, const char* kind, bool urgent) {
    Notice notice;
    notice.title = title;
    notice.message = message;
    notice.kind = kind;
    notice.urgent = urgent;
    notices_.push_back(notice);
}

void Engine::FinishCountdown() {
    ClearCountdown();
    Save();
    Notify(L"倒计时结束", L"时间到了，休息一下或开始下一件事吧。", "countdown", true);
}

void Engine::Tick() {
    const int64_t now = wallClock_();
    const uint64_t monotonicNow = monotonicClock_();
    const int64_t wallGap = std::max<int64_t>(0, now - lastTick_);
    const uint64_t monotonicGap = monotonicNow >= lastTickMonotonic_ ? monotonicNow - lastTickMonotonic_ : 0;
    lastTick_ = now;
    lastTickMonotonic_ = monotonicNow;
    if (countdownActive && countdownRunning && now >= countdownDeadline_) FinishCountdown();
    std::vector<std::wstring> dueTitles;
    for (size_t i = 0; i < reminders.size(); ++i) {
        Reminder& reminder = reminders[i];
        if (reminder.completed || reminder.dueMs > now) continue;
        dueTitles.push_back(reminder.title);
        if (reminder.daily) {
            try { reminder.dueMs = NextDaily(reminder.dueMs, now); }
            catch (const std::exception&) { reminder.completed = true; }
        } else reminder.completed = true;
    }
    if (!dueTitles.empty()) {
        Save(); // Persist acknowledgement before the UI shows each notice.
        for (size_t i = 0; i < dueTitles.size(); ++i) Notify(L"日程提醒", dueTitles[i], "reminder", true);
    }
    if (!shutdownAt) return;
    const int64_t left = shutdownAt - now;
    if (left <= 0) {
        const bool warned = shutdownWarned_;
        CancelShutdown(); // Clear before dispatch; never retry an uncertain shutdown.
        if (wallGap > 120000 || monotonicGap > 120000) {
            Notify(L"已取消过期关机", L"电脑休眠或暂停期间错过了关机时间，计划已取消。", "shutdown", true);
        } else if (!warned) {
            Notify(L"已取消未预警关机", L"关机时间已过，未能提前显示提醒，计划已取消。", "shutdown", true);
        } else if (safe_) {
            Notify(L"已模拟定时关机", L"安全演示模式不会关闭电脑。", "shutdown", true);
        } else {
            try { shutdownAction_(); }
            catch (const std::exception& error) {
                std::string detail(error.what());
                Notify(L"关机未执行", L"Windows 未能执行关机：" + std::wstring(detail.begin(), detail.end()), "shutdown", true);
            } catch (...) { Notify(L"关机未执行", L"Windows 未能执行关机。", "shutdown", true); }
        }
    } else if (left <= 60000 && !shutdownWarned_) {
        shutdownWarned_ = true;
        std::wostringstream message;
        message << L"将在 " << (left + 999) / 1000 << L" 秒内关机，点击取消可停止计划。";
        Notify(L"即将自动关机", message.str(), "shutdown", true);
    }
}

void Engine::NormalizeSettings() {
    if (settings.scene != Scene::Desktop && settings.scene != Scene::Classroom) settings.scene = Scene::Classroom;
    if (settings.dock != Dock::Top && settings.dock != Dock::Left && settings.dock != Dock::Right) settings.dock = Dock::Top;
    if (!std::isfinite(settings.anchor) || settings.anchor < 0 || settings.anchor > 1) settings.anchor = 0.5;
    if (settings.islandDotPercent < 0 || settings.islandDotPercent > 100) settings.islandDotPercent = 20;
    settings.islandDotSize = 3 + (17 * settings.islandDotPercent + 50) / 100;
    if (settings.glassMode < 0 || settings.glassMode > 2) settings.glassMode = 1;
    if (!std::isfinite(settings.islandScale) || settings.islandScale < 0.75 || settings.islandScale > 1.5) settings.islandScale = 1.0;
    if (settings.monitor.size() > 260 || settings.monitor.find(L'\0') != std::wstring::npos) settings.monitor.clear();
}

void Engine::Save() {
    NormalizeSettings();
    std::ostringstream output;
    output.imbue(std::locale::classic());
    output << "FreeIsland-Win7 1\n"
        << "scene=" << static_cast<int>(settings.scene) << '\n'
        << "sceneSelected=" << settings.sceneSelected << '\n'
        << "startup=" << settings.startup << '\n'
        << "sound=" << settings.sound << '\n'
        << "edgeHide=" << settings.edgeHide << '\n'
        << "ballX=" << settings.ballX << '\n'
        << "ballY=" << settings.ballY << '\n'
        << "dock=" << static_cast<int>(settings.dock) << '\n'
        << "anchor=" << std::setprecision(17) << settings.anchor << '\n'
        << "islandDotSize=" << settings.islandDotSize << '\n'
        << "islandDotPercent=" << settings.islandDotPercent << '\n'
        << "glassMode=" << settings.glassMode << '\n'
        << "islandScale=" << settings.islandScale << '\n'
        << "monitor=" << HexText(settings.monitor) << '\n'
        << "countdownActive=" << countdownActive << '\n'
        << "countdownRunning=" << countdownRunning << '\n'
        << "countdownDeadline=" << countdownDeadline_ << '\n'
        << "pausedCountdown=" << pausedCountdown_ << '\n';
    for (size_t i = 0; i < reminders.size(); ++i) {
        const Reminder& reminder = reminders[i];
        output << "reminder=" << reminder.id << '|' << reminder.dueMs << '|' << reminder.daily << '|'
               << reminder.completed << '|' << HexText(reminder.title) << '\n';
    }
    const std::string payload = output.str();
    output << "checksum=" << std::hex << std::setw(8) << std::setfill('0') << Checksum(payload) << '\n';
    AtomicWrite(statePath_, output.str(), recoveredBackup_);
    recoveredBackup_ = false;
}

void Engine::Load() {
    SavedState state;
    if (!ReadState(statePath_, state)) {
        if (!ReadState(statePath_ + L".bak", state)) {
            if (GetFileAttributesW(statePath_.c_str()) != INVALID_FILE_ATTRIBUTES)
                Notify(L"设置已恢复默认", L"保存的数据无法读取，已使用默认设置。", "info", false);
            return;
        }
        recoveredBackup_ = true;
        Notify(L"已恢复备份", L"上次保存的数据无法读取，已恢复最近一份可用备份。", "info", false);
    }
    settings = state.settings;
    NormalizeSettings();
    reminders.swap(state.reminders);
    for (size_t i = 0; i < reminders.size(); ++i) nextId_ = std::max(nextId_, reminders[i].id + 1);
    const int64_t now = wallClock_();
    if (state.active) {
        if (state.running && state.deadline > 0 && state.deadline <= LastDateMs && state.deadline - now <= MaximumCountdownMs) {
            countdownActive = countdownRunning = true;
            countdownDeadline_ = state.deadline;
        } else if (!state.running && state.paused > 0 && state.paused <= MaximumCountdownMs) {
            countdownActive = true;
            pausedCountdown_ = state.paused;
        }
    }
}

std::vector<Notice> Engine::TakeNotices() {
    std::vector<Notice> result;
    result.swap(notices_);
    return result;
}
} // namespace fi
