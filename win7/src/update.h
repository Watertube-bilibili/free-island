#pragma once
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0601
#endif
#ifndef WINVER
#define WINVER 0x0601
#endif
#include <windows.h>
#include <functional>
#include <memory>
#include <string>
#include <vector>

namespace fiUpdate {
enum class Phase { Idle, Checking, Downloading, Current, Ready, Applying, Failed, Disabled };
struct Status { Phase phase=Phase::Idle; int percent=0; std::wstring message=L"启动后自动检查 GitHub 更新。", version, file, digest; unsigned long long size=0; bool elevation=false,autoBlocked=false; };
struct Release { std::wstring version, assetUrl, digest, manifestUrl, filename; unsigned long long size=0; };
using Sink=std::function<void(const unsigned char*,size_t)>;
using Fetcher=std::function<void(const std::wstring&,size_t,const Sink&)>;
bool Newer(const std::wstring& candidate,const std::wstring& current);
Release ParseRelease(const std::string& json,const std::wstring& current);
std::wstring ManifestHash(const std::string& text,const std::wstring& filename);
bool AllowedUrl(const std::wstring& url,bool apiOnly=false);
bool CanApply(bool controlVisible,bool timerActive,bool shutdownReserved,bool installed);
bool ProcessElevated();
std::wstring FileVersion(const std::wstring& path);
std::wstring RegisteredDirectory(const std::wstring& executable);
bool VerifyFile(const std::wstring& path,const std::wstring& digest,unsigned long long expected);
class Service {
    struct State;
    std::shared_ptr<State> state_;
    HANDLE worker_=NULL;
    mutable HANDLE installer_=NULL;
    static DWORD WINAPI Worker(void*);
public:
    Service(const std::wstring& dataDirectory,const std::wstring& currentVersion,bool safe,Fetcher fixture=Fetcher());
    ~Service();
    bool Check();
    void Cancel();
    Status Get() const;
    bool Launch(HWND owner,const std::wstring& registeredDirectory,bool userRequested);
    Service(const Service&)=delete;
    Service& operator=(const Service&)=delete;
};
}
