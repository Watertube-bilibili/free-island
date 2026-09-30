#pragma once
#include <windows.h>
#include <string>
#include <cstdint>
#include <map>

namespace fiContext {
enum class Category { None, Media, Presentation, Writing };
Category Classify(const std::wstring& executableName, const std::wstring& chromeTitle = L"");
bool IsMediaBrowserTitle(const std::wstring& title);
std::wstring ForegroundExecutable(DWORD ownProcess, bool* fullScreen = nullptr, std::wstring* chromeTitle = nullptr);
bool CoversMonitor(const RECT& window, const RECT& monitor);
std::wstring Title(Category category);
std::wstring Detail(Category category);
// Only Chrome's title is read for local media matching; no page or document content.
class Suggestions {
public:
    Category Current() const { return current_; }
    const std::wstring& Executable() const { return executable_; }
    const std::wstring& WindowTitle() const { return title_; }
    bool Observe(const std::wstring& executable, uint64_t now, bool enabled, bool fullScreen = false, const std::wstring& chromeTitle = L"");
    bool Matches(const std::wstring& executable, const std::wstring& chromeTitle) const;
    void Dismiss(uint64_t now);
private:
    Category current_ = Category::None;
    std::wstring executable_, candidate_, dismissed_, scene_, title_;
    uint64_t stableSince_ = 0, lastShown_ = 0, expires_ = 0;
    bool shown_ = false;
    std::map<std::wstring, uint64_t> shownApplications_;
};
bool ReadVolume(int& percent, bool safe);
bool SetVolume(int percent, bool safe);
bool ToggleMedia(const std::wstring& expectedExecutable, bool safe, const std::wstring& chromeTitle = L"");
}
