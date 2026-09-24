#pragma once
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0601
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <functional>
#include <memory>
#include <string>

namespace fi {
enum class AlertSoundKind { Countdown, Reminder, Shutdown };
class AlertAudioOutput {
public:
    virtual ~AlertAudioOutput() {}
    virtual bool Play(const std::wstring& path, int volume) = 0;
    virtual void SetVolume(int volume) = 0;
    virtual void Stop() = 0;
    virtual bool Playing() = 0;
};
// UI-thread owned; caller invokes Tick from its existing timer. No worker/timer is created.
class AlertAudio {
public:
    AlertAudio(const std::wstring& dataDirectory, bool safe,
        std::unique_ptr<AlertAudioOutput> output = std::unique_ptr<AlertAudioOutput>(),
        std::function<void()> fallback = std::function<void()>(),
        std::function<ULONGLONG()> clock = std::function<ULONGLONG()>());
    ~AlertAudio();
    AlertAudio(const AlertAudio&) = delete;
    AlertAudio& operator=(const AlertAudio&) = delete;
    const std::wstring& Path(AlertSoundKind kind) const;
    bool SetPath(AlertSoundKind kind, const std::wstring& path);
    int VolumePercent() const { return volume_; }
    void SetVolumePercent(int value);
    bool Save();
    void Play(AlertSoundKind kind, bool preview = false);
    void Stop();
    void StopShutdown();
    void Tick();
    bool IsPlaying() const { return active_; }
    const std::wstring& LastError() const { return error_; }
    static bool ValidatePath(const std::wstring& value, std::wstring& normalized, bool mustExist);
private:
    void Load();
    std::wstring directory_, file_, paths_[3], error_;
    int volume_ = 70;
    bool safe_, active_ = false;
    AlertSoundKind kind_ = AlertSoundKind::Countdown;
    ULONGLONG deadline_ = 0;
    std::unique_ptr<AlertAudioOutput> output_;
    std::function<void()> fallback_;
    std::function<ULONGLONG()> clock_;
};
}
