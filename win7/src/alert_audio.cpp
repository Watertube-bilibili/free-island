#include "alert_audio.h"
#include <mmsystem.h>
#include <digitalv.h>
#include <algorithm>
#include <cwctype>
#include <stdexcept>
#include <vector>

namespace fi {
namespace {
int Index(AlertSoundKind kind) { int n = static_cast<int>(kind); if (n < 0 || n > 2) throw std::invalid_argument("Invalid alert kind"); return n; }
const wchar_t* Keys[] = { L"countdown", L"reminder", L"shutdown" };
class MciAlertAudioOutput : public AlertAudioOutput {
    MCIDEVICEID device_ = 0;
public:
    ~MciAlertAudioOutput() { Stop(); }
    bool Play(const std::wstring& path, int volume) override {
        Stop(); MCI_OPEN_PARMSW open = {}; open.lpstrElementName = path.c_str(); open.lpstrDeviceType = L"mpegvideo";
        // Typed parameters keep the file path out of MCI's command language.
        if (mciSendCommandW(0, MCI_OPEN, MCI_OPEN_ELEMENT | MCI_OPEN_TYPE | MCI_WAIT, reinterpret_cast<DWORD_PTR>(&open))) return false;
        device_ = open.wDeviceID; SetVolume(volume);
        MCI_PLAY_PARMS play = {}; if (mciSendCommandW(device_, MCI_PLAY, 0, reinterpret_cast<DWORD_PTR>(&play))) { Stop(); return false; }
        return true;
    }
    void SetVolume(int volume) override {
        if (!device_) return;
        MCI_DGV_SETAUDIO_PARMS audio = {}; audio.dwItem = MCI_DGV_SETAUDIO_VOLUME; audio.dwValue = static_cast<DWORD>(volume * 10);
        mciSendCommandW(device_, MCI_SETAUDIO, MCI_DGV_SETAUDIO_ITEM | MCI_DGV_SETAUDIO_VALUE, reinterpret_cast<DWORD_PTR>(&audio));
    }
    void Stop() override { if (device_) { MCIDEVICEID old = device_; device_ = 0; MCI_GENERIC_PARMS p = {}; mciSendCommandW(old, MCI_CLOSE, MCI_WAIT, reinterpret_cast<DWORD_PTR>(&p)); } }
    bool Playing() override { if (!device_) return false; MCI_STATUS_PARMS p = {}; p.dwItem = MCI_STATUS_MODE; return !mciSendCommandW(device_, MCI_STATUS, MCI_STATUS_ITEM, reinterpret_cast<DWORD_PTR>(&p)) && p.dwReturn == MCI_MODE_PLAY; }
};
}

AlertAudio::AlertAudio(const std::wstring& dataDirectory, bool safe, std::unique_ptr<AlertAudioOutput> output, std::function<void()> fallback, std::function<ULONGLONG()> clock)
    : directory_(dataDirectory), file_(dataDirectory + L"\\alert-audio.ini"), safe_(safe), output_(std::move(output)), fallback_(fallback), clock_(clock) {
    if (!output_) output_.reset(new MciAlertAudioOutput());
    if (!fallback_) fallback_ = [] { MessageBeep(MB_ICONASTERISK); };
    if (!clock_) clock_ = [] { return GetTickCount64(); };
    Load();
}
AlertAudio::~AlertAudio() { Stop(); }
const std::wstring& AlertAudio::Path(AlertSoundKind kind) const { return paths_[Index(kind)]; }
bool AlertAudio::ValidatePath(const std::wstring& value, std::wstring& normalized, bool mustExist) {
    normalized.clear(); if (value.empty()) return true;
    if (value.size() < 4 || value.size() > 259 || !iswalpha(value[0]) || value[1] != L':' || (value[2] != L'\\' && value[2] != L'/')) return false;
    for (size_t i = 0; i < value.size(); ++i) if (value[i] < 32 || value[i] == L'"' || value[i] == L'|' || value[i] == L'<' || value[i] == L'>' || (value[i] == L':' && i != 1)) return false;
    wchar_t full[260]; DWORD n = GetFullPathNameW(value.c_str(), 260, full, NULL); if (!n || n >= 260) return false;
    normalized.assign(full); size_t dot = normalized.find_last_of(L'.'); if (dot == std::wstring::npos) return false;
    std::wstring extension = normalized.substr(dot); std::transform(extension.begin(), extension.end(), extension.begin(), towlower);
    if (extension != L".wav" && extension != L".mp3") return false;
    if (mustExist) { WIN32_FILE_ATTRIBUTE_DATA info; if (!GetFileAttributesExW(full, GetFileExInfoStandard, &info) || (info.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) || info.nFileSizeHigh || !info.nFileSizeLow || info.nFileSizeLow > 100 * 1024 * 1024) return false; }
    return true;
}
bool AlertAudio::SetPath(AlertSoundKind kind, const std::wstring& path) {
    std::wstring full; if (!ValidatePath(path, full, true)) { error_ = L"请选择本地磁盘中不超过 100 MB 的 WAV 或 MP3 文件。"; return false; }
    paths_[Index(kind)] = full; error_.clear(); return true;
}
void AlertAudio::SetVolumePercent(int value) { volume_ = std::max(0, std::min(100, value)); if (!volume_) Stop(); else output_->SetVolume(volume_); }
void AlertAudio::Load() {
    WIN32_FILE_ATTRIBUTE_DATA info; if (!GetFileAttributesExW(file_.c_str(), GetFileExInfoStandard, &info)) return;
    if (info.nFileSizeHigh || info.nFileSizeLow > 65536) { error_ = L"提醒音频设置无法读取，已使用默认提示音。"; return; }
    wchar_t text[512]; std::wstring loaded[3];
    for (int i = 0; i < 3; ++i) { GetPrivateProfileStringW(L"audio", Keys[i], L"", text, 512, file_.c_str()); if (!ValidatePath(text, loaded[i], false)) { error_ = L"提醒音频设置无法读取，已使用默认提示音。"; return; } }
    for (int i = 0; i < 3; ++i) paths_[i] = loaded[i];
    volume_ = std::max(0, std::min(100, static_cast<int>(GetPrivateProfileIntW(L"audio", L"volume", 70, file_.c_str()))));
}
bool AlertAudio::Save() {
    if (!CreateDirectoryW(directory_.c_str(), NULL) && GetLastError() != ERROR_ALREADY_EXISTS) { error_ = L"无法保存提醒音频设置。"; return false; }
    wchar_t temporary[32768]; if (!GetTempFileNameW(directory_.c_str(), L"fia", 0, temporary)) { error_ = L"无法保存提醒音频设置。"; return false; }
    std::wstring contents = L"[audio]\r\nvolume=" + std::to_wstring(volume_) + L"\r\n";
    for (int i = 0; i < 3; ++i) contents += std::wstring(Keys[i]) + L"=" + paths_[i] + L"\r\n";
    HANDLE h = CreateFileW(temporary, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL); bool ok = h != INVALID_HANDLE_VALUE;
    if (ok) { WORD bom = 0xfeff; DWORD written = 0, bytes = static_cast<DWORD>(contents.size() * sizeof(wchar_t)); ok = WriteFile(h, &bom, 2, &written, NULL) && written == 2 && WriteFile(h, contents.data(), bytes, &written, NULL) && written == bytes && FlushFileBuffers(h); CloseHandle(h); }
    if (ok) ok = MoveFileExW(temporary, file_.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != FALSE;
    if (!ok) { DeleteFileW(temporary); error_ = L"无法保存提醒音频设置。"; } else { WritePrivateProfileStringW(NULL, NULL, NULL, file_.c_str()); error_.clear(); }
    return ok;
}
void AlertAudio::Play(AlertSoundKind kind, bool preview) {
    const std::wstring path = Path(kind); Stop(); error_.clear(); if (safe_ || !volume_) return;
    if (path.empty()) { fallback_(); return; }
    std::wstring normalized; if (!ValidatePath(path, normalized, true)) { error_ = L"所选音频无法读取，已使用默认提示音。"; fallback_(); return; }
    if (!output_->Play(normalized, volume_)) { error_ = L"音频格式无法播放，已使用默认提示音。"; fallback_(); return; }
    kind_ = kind; active_ = true; deadline_ = clock_() + (preview ? 8000 : kind == AlertSoundKind::Shutdown ? 10000 : 30000);
}
void AlertAudio::Stop() { active_ = false; deadline_ = 0; output_->Stop(); }
void AlertAudio::StopShutdown() { if (active_ && kind_ == AlertSoundKind::Shutdown) Stop(); }
void AlertAudio::Tick() { if (active_ && (clock_() >= deadline_ || !output_->Playing())) Stop(); }
}
