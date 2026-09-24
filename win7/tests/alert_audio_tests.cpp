#include "../src/alert_audio.h"
#include <iostream>
#include <stdexcept>
#include <memory>

static int checks = 0;
static void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); ++checks; }
static void Put(const std::wstring& path, const std::string& content) { HANDLE h = CreateFileW(path.c_str(), GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL); Check(h != INVALID_HANDLE_VALUE, "Fixture file opened"); DWORD written = 0; bool ok = WriteFile(h, content.data(), static_cast<DWORD>(content.size()), &written, NULL) && written == content.size(); CloseHandle(h); Check(ok, "Fixture file written"); }
struct Output : fi::AlertAudioOutput {
    int starts = 0, stops = 0, volume = 0; bool fail = false, playing = false; std::wstring path;
    bool Play(const std::wstring& p, int v) override { ++starts; path = p; volume = v; playing = !fail; return !fail; }
    void SetVolume(int v) override { volume = v; }
    void Stop() override { ++stops; playing = false; }
    bool Playing() override { return playing; }
};
int wmain(int argc, wchar_t** argv) {
    try {
        Check(argc == 2, "Provide fresh isolated fixture directory"); std::wstring root = argv[1]; Check(CreateDirectoryW(root.c_str(), NULL) != FALSE, "Unique fixture root");
        std::wstring wav = root + L"\\课堂铃声.wav", mp3 = root + L"\\提醒音频.MP3"; Put(wav, "fixture, no real decoder"); Put(mp3, "fixture, no real decoder");
        using Kind = fi::AlertSoundKind; int fallback = 0; ULONGLONG now = 100000; Output* out = new Output();
        fi::AlertAudio audio(root, false, std::unique_ptr<fi::AlertAudioOutput>(out), [&] { ++fallback; }, [&] { return now; });
        Check(audio.VolumePercent() == 70 && audio.Path(Kind::Countdown).empty(), "Defaults"); audio.Play(Kind::Countdown); Check(fallback == 1 && !out->starts, "Default sound once");
        for (const wchar_t* p : {L"https://example.test/a.wav", L"\\\\server\\share\\a.wav", L"\\\\?\\C:\\a.wav", L"C:\\a.wav:evil.mp3", L"C:\\a.exe", L"C:\\x\r\nvolume=100.wav"}) Check(!audio.SetPath(Kind::Countdown, p), "Unsupported or injected path rejected");
        Check(!audio.SetPath(Kind::Countdown, root + L"\\missing.wav"), "Missing file rejected");
        Check(audio.SetPath(Kind::Countdown, wav) && audio.SetPath(Kind::Reminder, mp3) && audio.SetPath(Kind::Shutdown, wav), "Three event choices"); audio.SetVolumePercent(42); Check(audio.Save(), "Atomic configuration save");
        { fi::AlertAudio restored(root, true); Check(restored.Path(Kind::Countdown) == wav && restored.Path(Kind::Reminder) == mp3 && restored.Path(Kind::Shutdown) == wav && restored.VolumePercent() == 42, "Unicode paths and volume restored"); }
        audio.Play(Kind::Countdown); Check(out->starts == 1 && out->path == wav && out->volume == 42 && audio.IsPlaying(), "Custom countdown playback");
        audio.Play(Kind::Reminder); Check(out->starts == 2 && out->path == mp3, "New event replaces previous audio"); audio.StopShutdown(); Check(audio.IsPlaying(), "Cancel shutdown preserves unrelated reminder");
        now += 29999; audio.Tick(); Check(audio.IsPlaying(), "Ordinary alert before cap"); ++now; audio.Tick(); Check(!audio.IsPlaying() && !out->playing, "Ordinary alert capped at 30 seconds");
        audio.Play(Kind::Shutdown); now += 10000; audio.Tick(); Check(!audio.IsPlaying(), "Shutdown capped at 10 seconds"); audio.Play(Kind::Shutdown); audio.StopShutdown(); Check(!audio.IsPlaying(), "Shutdown cancel/fire stops warning");
        audio.Play(Kind::Reminder, true); now += 8000; audio.Tick(); Check(!audio.IsPlaying(), "Preview capped at 8 seconds");
        audio.Play(Kind::Reminder); out->playing = false; audio.Tick(); Check(!audio.IsPlaying(), "Ended media released without looping");
        out->fail = true; audio.Play(Kind::Reminder); Check(fallback == 2 && !audio.IsPlaying() && !audio.LastError().empty(), "Decoder failure fallback"); out->fail = false;
        DeleteFileW(mp3.c_str()); audio.Play(Kind::Reminder); Check(fallback == 3 && !audio.IsPlaying() && audio.Path(Kind::Reminder) == mp3, "Missing file fallback retains choice");
        audio.Play(Kind::Countdown); audio.SetVolumePercent(0); int starts = out->starts; audio.Play(Kind::Shutdown); Check(!audio.IsPlaying() && out->starts == starts && fallback == 3, "Mute stops and suppresses sound");
        audio.SetVolumePercent(200); Check(audio.VolumePercent() == 100, "Volume upper bound"); audio.SetVolumePercent(-1); Check(audio.VolumePercent() == 0, "Volume lower bound"); Check(audio.SetPath(Kind::Countdown, L"") && audio.Path(Kind::Countdown).empty(), "Reset selection");
        { Output* safeOut = new Output(); int safeFallback = 0; fi::AlertAudio safe(root, true, std::unique_ptr<fi::AlertAudioOutput>(safeOut), [&] { ++safeFallback; }, [&] { return now; }); safe.Play(Kind::Countdown); safe.Play(Kind::Shutdown, true); Check(!safeOut->starts && !safeFallback, "Safe mode suppresses real/default/preview audio"); }
        std::cout << "PASS: " << checks << " native alert audio checks; injected playback only, no real audio.\n"; return 0;
    } catch (const std::exception& e) { std::cerr << "FAIL: " << e.what() << "\n"; return 1; }
}
