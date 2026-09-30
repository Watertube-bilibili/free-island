#pragma once
#include <windows.h>
#include <string>
#include <cstdint>
#include <map>

namespace fiContext {
enum class Category { None, Media, Presentation, Writing };
Category Classify(const std::wstring& executableName);
std::wstring ForegroundExecutable(DWORD ownProcess, bool* fullScreen = nullptr);
bool CoversMonitor(const RECT& window, const RECT& monitor);
std::wstring Title(Category category);
std::wstring Detail(Category category);
// Process names only: no window title, screenshot, document or clipboard access.
class Suggestions {
public:
    Category Current() const { return current_; }
    const std::wstring& Executable() const { return executable_; }
    bool Observe(const std::wstring& executable, uint64_t now, bool enabled, bool fullScreen = false);
    void Dismiss(uint64_t now);
private:
    Category current_ = Category::None;
    std::wstring executable_, candidate_, dismissed_;
    uint64_t stableSince_ = 0, lastShown_ = 0, expires_ = 0;
    bool shown_ = false;
    std::map<std::wstring, uint64_t> shownApplications_;
};
bool ReadVolume(int& percent, bool safe);
bool SetVolume(int percent, bool safe);
bool ToggleMedia(const std::wstring& expectedExecutable, bool safe);
}
