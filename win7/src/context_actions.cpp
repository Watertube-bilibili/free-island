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
Category Classify(const std::wstring& executableName) {
    const std::wstring name = Lower(executableName);
    const wchar_t* media[] = {L"vlc.exe", L"wmplayer.exe", L"potplayer.exe", L"potplayermini.exe", L"potplayermini64.exe", L"mpv.exe", L"qqmusic.exe", L"cloudmusic.exe", L"spotify.exe", L"foobar2000.exe", L"kugou.exe", L"music.ui.exe", L"video.ui.exe"};
    for (auto item : media) if (name == item) return Category::Media;
    if (name == L"powerpnt.exe" || name == L"wpp.exe") return Category::Presentation;
    if (name == L"winword.exe" || name == L"wps.exe" || name == L"notepad.exe" || name == L"code.exe") return Category::Writing;
    return Category::None;
}
std::wstring ForegroundExecutable(DWORD ownProcess) {
    HWND foreground = GetForegroundWindow(); DWORD pid = 0;
    if (!foreground || !GetWindowThreadProcessId(foreground, &pid) || pid == ownProcess) return L"";
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!process) return L"";
    wchar_t path[32768] = {}; DWORD length = 32768;
    bool ok = QueryFullProcessImageNameW(process, 0, path, &length) != FALSE;
    CloseHandle(process);
    return ok ? Lower(std::wstring(path, length)) : L"";
}
std::wstring Title(Category category) {
    return category == Category::Media ? L"播放器 · 快捷音量" : category == Category::Presentation ? L"演示 · 记录用时" : L"编辑 · 专注一会儿";
}
std::wstring Detail(Category category) {
    return category == Category::Media ? L"本地规则建议 · 拖动调整系统音量" : category == Category::Presentation ? L"本地规则建议 · 点击开始正向计时" : L"本地规则建议 · 点击开始 25 分钟倒计时";
}
bool Suggestions::Observe(const std::wstring& executable, uint64_t now, bool enabled) {
    Category before = current_;
    if (!enabled) { current_ = Category::None; executable_.clear(); candidate_.clear(); dismissed_.clear(); return before != current_; }
    if (current_ != Category::None && now >= expires_) Dismiss(now);
    // An empty observation represents an unavailable or our own foreground window.
    if (executable.empty()) return before != current_;
    const std::wstring name = Lower(executable);
    if (name != candidate_) { candidate_ = name; stableSince_ = now; if (name != dismissed_) dismissed_.clear(); }
    if (current_ != Category::None && name != executable_) { current_ = Category::None; executable_.clear(); }
    Category category = Classify(name);
    if (category != Category::None && name != dismissed_ && now - stableSince_ >= 3000 && (!shown_ || now - lastShown_ >= 60000) && current_ == Category::None) {
        current_ = category; executable_ = name; lastShown_ = now; shown_ = true; expires_ = now + 25000;
    }
    return before != current_;
}
void Suggestions::Dismiss(uint64_t) { dismissed_ = executable_; current_ = Category::None; executable_.clear(); }

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
bool ToggleMedia(const std::wstring& expected, bool safe) {
    if (Classify(expected) != Category::Media) return false;
    if (safe) return true;
    if (ForegroundExecutable(GetCurrentProcessId()) != Lower(expected)) return false;
    INPUT keys[2] = {}; keys[0].type = keys[1].type = INPUT_KEYBOARD;
    keys[0].ki.wVk = keys[1].ki.wVk = VK_MEDIA_PLAY_PAUSE; keys[1].ki.dwFlags = KEYEVENTF_KEYUP;
    return SendInput(2, keys, sizeof(INPUT)) == 2;
}
}
