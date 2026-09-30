#include "context_actions.h"
#include <mmdeviceapi.h>
#include <endpointvolume.h>
#include <algorithm>
#include <cwctype>

namespace fiContext {
static std::wstring Lower(std::wstring value) {
    std::transform(value.begin(), value.end(), value.begin(), [](wchar_t c){ return (wchar_t)towlower(c); });
    const auto slash = value.find_last_of(L"\\/");
    return slash == std::wstring::npos ? value : value.substr(slash + 1);
}
static std::wstring CleanTitle(const std::wstring& title) {
    std::wstring result; bool gap = false;
    for (wchar_t c : title) {
        if (iswspace(c) || iswcntrl(c) || c == 0x200b || c == 0x202e) { gap = !result.empty(); continue; }
        if (gap) { result += L' '; gap = false; }
        result += (wchar_t)towlower(c);
        if (result.size() >= 160) break;
    }
    return result;
}
static bool SiteSuffix(const std::wstring& value, const std::wstring& suffix) {
    if (value.size() < suffix.size() || value.compare(value.size() - suffix.size(), suffix.size(), suffix) != 0) return false;
    size_t prefix = value.size() - suffix.size();
    while (prefix && iswspace(value[prefix - 1])) --prefix;
    return !prefix || std::wstring(L"-|_—–").find(value[prefix - 1]) != std::wstring::npos;
}
bool IsMediaBrowserTitle(const std::wstring& rawTitle) {
    std::wstring title = CleanTitle(rawTitle);
    const wchar_t* browsers[] = {L"google chrome", L"chrome", L"谷歌浏览器"};
    for (auto suffix : browsers) if (SiteSuffix(title, suffix)) {
        title.erase(title.size() - wcslen(suffix));
        while (!title.empty() && iswspace(title.back())) title.pop_back();
        if (!title.empty() && std::wstring(L"-|—–").find(title.back()) != std::wstring::npos) title.pop_back();
        while (!title.empty() && iswspace(title.back())) title.pop_back();
        break;
    }
    const wchar_t* sites[] = {L"youtube", L"youtube music", L"哔哩哔哩", L"哔哩哔哩_bilibili", L"bilibili", L"腾讯视频", L"优酷", L"爱奇艺", L"芒果tv", L"网易云音乐", L"qq音乐", L"qq 音乐", L"spotify"};
    for (auto site : sites) if (SiteSuffix(title, site)) return true;
    return false;
}
Category Classify(const std::wstring& executableName, const std::wstring& chromeTitle) {
    const std::wstring name = Lower(executableName);
    const wchar_t* media[] = {L"vlc.exe", L"wmplayer.exe", L"potplayer.exe", L"potplayer64.exe", L"potplayermini.exe", L"potplayermini64.exe", L"mpv.exe", L"qqmusic.exe", L"cloudmusic.exe", L"spotify.exe", L"foobar2000.exe", L"kugou.exe", L"music.ui.exe", L"video.ui.exe", L"qqlive.exe", L"qqvideo.exe", L"tencentvideo.exe", L"bilibili.exe", L"youku.exe"};
    for (auto item : media) if (name == item) return Category::Media;
    if (name == L"chrome.exe" && IsMediaBrowserTitle(chromeTitle)) return Category::Media;
    if (name == L"powerpnt.exe" || name == L"wpp.exe") return Category::Presentation;
    if (name == L"winword.exe" || name == L"wps.exe" || name == L"notepad.exe" || name == L"code.exe") return Category::Writing;
    return Category::None;
}
bool CoversMonitor(const RECT& window, const RECT& monitor) {
    return std::abs((long long)window.left - monitor.left) <= 2 && std::abs((long long)window.top - monitor.top) <= 2
        && std::abs((long long)window.right - monitor.right) <= 2 && std::abs((long long)window.bottom - monitor.bottom) <= 2;
}
std::wstring ForegroundExecutable(DWORD ownProcess, bool* fullScreen, std::wstring* chromeTitle) {
    if (fullScreen) *fullScreen = false;
    if (chromeTitle) chromeTitle->clear();
    HWND foreground = GetForegroundWindow(); DWORD pid = 0;
    if (!foreground || !GetWindowThreadProcessId(foreground, &pid) || pid == ownProcess) return L"";
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!process) return L"";
    wchar_t path[32768] = {}; DWORD length = 32768;
    bool ok = QueryFullProcessImageNameW(process, 0, path, &length) != FALSE;
    CloseHandle(process);
    std::wstring name = ok ? Lower(std::wstring(path, length)) : L"";
    if (chromeTitle && name == L"chrome.exe") { wchar_t title[161] = {}; GetWindowTextW(foreground, title, 161); *chromeTitle = CleanTitle(title); }
    if (fullScreen) {
        RECT bounds = {}; MONITORINFO monitor = {}; monitor.cbSize = sizeof(monitor);
        *fullScreen = GetWindowRect(foreground, &bounds) && GetMonitorInfoW(MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST), &monitor)
            && CoversMonitor(bounds, monitor.rcMonitor);
    }
    if (GetForegroundWindow() != foreground) { if (fullScreen) *fullScreen = false; if (chromeTitle) chromeTitle->clear(); return L""; }
    return name;
}
std::wstring Title(Category category) {
    return category == Category::Media ? L"播放器 · 快捷音量" : category == Category::Presentation ? L"演示 · 记录用时" : L"编辑 · 专注一会儿";
}
std::wstring Detail(Category category) {
    return category == Category::Media ? L"本地规则建议 · 拖动调整系统音量" : category == Category::Presentation ? L"本地规则建议 · 点击开始正向计时" : L"本地规则建议 · 点击开始 25 分钟倒计时";
}
static std::wstring SceneKey(const std::wstring& name, const std::wstring& chromeTitle) { return name + (name == L"chrome.exe" ? L"\n" + CleanTitle(chromeTitle) : L""); }
bool Suggestions::Matches(const std::wstring& executable, const std::wstring& chromeTitle) const {
    const std::wstring name = Lower(executable);
    return current_ == Category::Media && Classify(name, chromeTitle) == Category::Media && scene_ == SceneKey(name, chromeTitle);
}
bool Suggestions::Observe(const std::wstring& executable, uint64_t now, bool enabled, bool fullScreen, const std::wstring& chromeTitle) {
    Category before = current_;
    if (!enabled) { current_ = Category::None; executable_.clear(); candidate_.clear(); dismissed_.clear(); return before != current_; }
    if (current_ != Category::None && now >= expires_) Dismiss(now);
    if (fullScreen) { current_ = Category::None; executable_.clear(); candidate_.clear(); stableSince_ = now; return before != current_; }
    // An empty observation represents an unavailable or our own foreground window.
    if (executable.empty()) { candidate_.clear(); stableSince_ = now; return before != current_; }
    const std::wstring name = Lower(executable);
    const std::wstring key = SceneKey(name, chromeTitle);
    if (key != candidate_) { candidate_ = key; stableSince_ = now; if (key != dismissed_) dismissed_.clear(); }
    if (current_ != Category::None && key != scene_) { current_ = Category::None; executable_.clear(); }
    Category category = Classify(name, chromeTitle);
    const auto previous = shownApplications_.find(name);
    bool applicationReady = previous == shownApplications_.end() || (now >= previous->second && now - previous->second >= 1800000);
    if (category == Category::Media && key != dismissed_ && now >= stableSince_ && now - stableSince_ >= 15000
        && (!shown_ || (now >= lastShown_ && now - lastShown_ >= 600000)) && applicationReady && current_ == Category::None) {
        current_ = category; executable_ = name; scene_ = key; title_ = name == L"chrome.exe" ? CleanTitle(chromeTitle) : L""; lastShown_ = now; shown_ = true; expires_ = now + 25000;
        for (auto item = shownApplications_.begin(); item != shownApplications_.end();) {
            if (now >= item->second && now - item->second >= 1800000) item = shownApplications_.erase(item); else ++item;
        }
        shownApplications_[name] = now;
    }
    return before != current_;
}
void Suggestions::Dismiss(uint64_t) { dismissed_ = scene_; current_ = Category::None; executable_.clear(); title_.clear(); }

static bool WithVolume(int& percent, bool set, bool safe) {
    if (safe) { if (!set) percent = 50; return true; }
    HRESULT init = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    if (FAILED(init) && init != RPC_E_CHANGED_MODE) return false;
    IMMDeviceEnumerator* enumerator = NULL; IMMDevice* device = NULL; IAudioEndpointVolume* endpoint = NULL;
    HRESULT hr = CoCreateInstance(__uuidof(MMDeviceEnumerator), NULL, CLSCTX_INPROC_SERVER, __uuidof(IMMDeviceEnumerator), (void**)&enumerator);
    if (SUCCEEDED(hr)) hr = enumerator->GetDefaultAudioEndpoint(eRender, eMultimedia, &device);
    if (SUCCEEDED(hr)) hr = device->Activate(__uuidof(IAudioEndpointVolume), CLSCTX_INPROC_SERVER, NULL, (void**)&endpoint);
    if (SUCCEEDED(hr)) {
        if (set) hr = endpoint->SetMasterVolumeLevelScalar(std::max(0, std::min(100, percent)) / 100.0f, NULL);
        else { float volume = 0; hr = endpoint->GetMasterVolumeLevelScalar(&volume); percent = (int)(volume * 100 + 0.5f); }
    }
    if (endpoint) endpoint->Release();
    if (device) device->Release();
    if (enumerator) enumerator->Release();
    if (SUCCEEDED(init)) CoUninitialize();
    return SUCCEEDED(hr);
}
bool ReadVolume(int& percent, bool safe) { return WithVolume(percent, false, safe); }
bool SetVolume(int percent, bool safe) { if (percent < 0 || percent > 100) return false; return WithVolume(percent, true, safe); }
bool ToggleMedia(const std::wstring& expected, bool safe, const std::wstring& chromeTitle) {
    if (Classify(expected, chromeTitle) != Category::Media) return false;
    if (safe) return true;
    std::wstring currentTitle;
    if (ForegroundExecutable(GetCurrentProcessId(), nullptr, &currentTitle) != Lower(expected)
        || SceneKey(Lower(expected), currentTitle) != SceneKey(Lower(expected), chromeTitle)) return false;
    INPUT keys[2] = {}; keys[0].type = keys[1].type = INPUT_KEYBOARD;
    keys[0].ki.wVk = keys[1].ki.wVk = VK_MEDIA_PLAY_PAUSE; keys[1].ki.dwFlags = KEYEVENTF_KEYUP;
    return SendInput(2, keys, sizeof(INPUT)) == 2;
}
}
