#pragma once
#include <windows.h>
#include <string>
#include <cstdint>

namespace fiContext {
enum class Category { None, Media, Presentation, Writing };
Category Classify(const std::wstring& executableName);
std::wstring ForegroundExecutable(DWORD ownProcess);
std::wstring Title(Category category);
std::wstring Detail(Category category);
// Process names only: no window title, screenshot, document or clipboard access.
class Suggestions {
public:
    Category Current() const { return current_; }
    const std::wstring& Executable() const { return executable_; }
    bool Observe(const std::wstring& executable, uint64_t now, bool enabled);
    void Dismiss(uint64_t now);
private:
    Category current_ = Category::None;
    std::wstring executable_, candidate_, dismissed_;
    uint64_t stableSince_ = 0, lastShown_ = 0, expires_ = 0;
    bool shown_ = false;
};
bool ReadVolume(int& percent, bool safe);
bool SetVolume(int percent, bool safe);
bool ToggleMedia(const std::wstring& expectedExecutable, bool safe);
}
