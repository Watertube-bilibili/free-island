#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#define WINVER 0x0601
#define _WIN32_WINNT 0x0601
#include <windows.h>
#include <windowsx.h>
#include <commctrl.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <objbase.h>
#include <wincrypt.h>
#include <gdiplus.h>
#include <tlhelp32.h>
#include <string>
#include <vector>
#include <cwchar>
#include <algorithm>

using std::wstring;
static const wchar_t* kAppName = L"FreeIslandWin7.exe";
static const wchar_t* kUninstaller = L"FreeIslandWin7.Uninstall.exe";
static const wchar_t* kMarker = L"freeisland-win7.install";
static const wchar_t* kRuntimeLicense = L"COPYING.MinGW-w64-runtime.txt";
static const char kMarkerText[] = "FreeIsland.Win7.Install.v1\r\n";
static const wchar_t* kRunKey = L"Software\\Microsoft\\Windows\\CurrentVersion\\Run";
static const wchar_t* kUninstallKey = L"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\FreeIslandWin7";
static HINSTANCE gInstance;
static HWND gWindow, gInstall, gClose, gStartup, gDesktop, gProgress;
static HFONT gBodyFont, gTitleFont, gSmallFont;
static HBRUSH gPaperBrush, gWhiteBrush;
static wstring gInstallDir, gStatus;
static bool gBusy = false, gDone = false, gStartupWanted = true, gDesktopWanted = true;
static const UINT WM_INSTALL_FINISH = WM_APP + 1, WM_INSTALL_PROGRESS = WM_APP + 2;

static wstring Join(const wstring& a, const wstring& b) { return a + L"\\" + b; }
static wstring Quote(const wstring& s) { return L"\"" + s + L"\""; }
static bool EqualPath(const wstring& a, const wstring& b) { return _wcsicmp(a.c_str(), b.c_str()) == 0; }
static void Fail(const wstring& s) { throw s; }
static wstring ErrorText(const wstring& s) {
    DWORD e = GetLastError();
    wchar_t b[512] = {};
    FormatMessageW(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, NULL, e, 0, b, 511, NULL);
    return s + L"\n" + b + L"（" + std::to_wstring(e) + L"）";
}
static wstring FullPath(const wstring& p) {
    wchar_t b[MAX_PATH] = {};
    DWORD n = GetFullPathNameW(p.c_str(), MAX_PATH, b, NULL);
    if (!n || n >= MAX_PATH) Fail(L"路径过长或无效，请使用较短的本地路径。");
    wstring s(b);
    while (s.size() > 3 && s.back() == L'\\') s.pop_back();
    if (s.size() < 3 || s[1] != L':' || s[2] != L'\\' || s.find(L':', 2) != wstring::npos)
        Fail(L"安装和验证只能使用本地磁盘路径。");
    return s;
}
// Reject junctions/symlinks in every existing parent and hard-linked destination files.
static void CheckPath(const wstring& original) {
    wstring p = FullPath(original);
    for (size_t i = 3; i <= p.size(); ++i) {
        if (i != p.size() && p[i] != L'\\') continue;
        wstring part = p.substr(0, i);
        DWORD a = GetFileAttributesW(part.c_str());
        if (a == INVALID_FILE_ATTRIBUTES) {
            DWORD e = GetLastError();
            if (e != ERROR_FILE_NOT_FOUND && e != ERROR_PATH_NOT_FOUND) Fail(ErrorText(L"无法检查路径：" + part));
            continue;
        }
        if (a & FILE_ATTRIBUTE_REPARSE_POINT) Fail(L"为保护文件，不能使用链接或重解析点：\n" + part);
        if (i != p.size() && !(a & FILE_ATTRIBUTE_DIRECTORY)) Fail(L"路径的上级不是文件夹：\n" + part);
        if (i == p.size() && !(a & FILE_ATTRIBUTE_DIRECTORY)) {
            HANDLE h = CreateFileW(part.c_str(), FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                NULL, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT, NULL);
            if (h == INVALID_HANDLE_VALUE) Fail(ErrorText(L"无法检查目标文件"));
            BY_HANDLE_FILE_INFORMATION info = {};
            BOOL ok = GetFileInformationByHandle(h, &info); CloseHandle(h);
            if (!ok || info.nNumberOfLinks != 1) Fail(L"目标文件无法安全替换（可能是硬链接）：\n" + part);
        }
    }
}
static bool Exists(const wstring& p) { return GetFileAttributesW(p.c_str()) != INVALID_FILE_ATTRIBUTES; }
static void MakeDirectory(const wstring& path) {
    wstring p = FullPath(path); CheckPath(p);
    for (size_t i = 3; i <= p.size(); ++i) {
        if (i != p.size() && p[i] != L'\\') continue;
        wstring part = p.substr(0, i);
        if (!CreateDirectoryW(part.c_str(), NULL) && GetLastError() != ERROR_ALREADY_EXISTS)
            Fail(ErrorText(L"无法创建文件夹：" + part));
        DWORD a = GetFileAttributesW(part.c_str());
        if (a == INVALID_FILE_ATTRIBUTES || !(a & FILE_ATTRIBUTE_DIRECTORY) || (a & FILE_ATTRIBUTE_REPARSE_POINT))
            Fail(L"目标不是安全的普通文件夹：\n" + part);
    }
}
static wstring SpecialFolder(int id) {
    wchar_t p[MAX_PATH] = {};
    if (FAILED(SHGetFolderPathW(NULL, id, NULL, SHGFP_TYPE_CURRENT, p))) Fail(L"无法获取当前账户的文件夹。");
    return FullPath(p);
}
static wstring ModulePath() {
    wchar_t p[MAX_PATH] = {};
    DWORD n = GetModuleFileNameW(NULL, p, MAX_PATH);
    if (!n || n >= MAX_PATH) Fail(L"安装程序所在路径过长。");
    return FullPath(p);
}
static void WriteBytes(const wstring& p, const void* data, DWORD length, bool replace) {
    CheckPath(p);
    HANDLE h = CreateFileW(p.c_str(), GENERIC_WRITE, 0, NULL, replace ? CREATE_ALWAYS : CREATE_NEW,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OPEN_REPARSE_POINT, NULL);
    if (h == INVALID_HANDLE_VALUE) Fail(ErrorText(L"无法写入文件：" + p));
    DWORD written = 0;
    BOOL ok = WriteFile(h, data, length, &written, NULL);
    if (ok) ok = FlushFileBuffers(h);
    DWORD error = GetLastError(); CloseHandle(h); SetLastError(error);
    if (!ok || written != length) Fail(ErrorText(L"文件没有完整写入：" + p));
}
static bool MarkerMatches() {
    wstring p = Join(gInstallDir, kMarker); CheckPath(p);
    HANDLE h = CreateFileW(p.c_str(), GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h == INVALID_HANDLE_VALUE) return false;
    char b[128] = {}; DWORD read = 0;
    BOOL ok = ReadFile(h, b, sizeof(b), &read, NULL); CloseHandle(h);
    return ok && read == sizeof(kMarkerText) - 1 && memcmp(b, kMarkerText, read) == 0;
}
static void CheckOwnedInstall(bool allowNew) {
    CheckPath(gInstallDir);
    if (Exists(gInstallDir)) {
        if (!MarkerMatches()) {
            WIN32_FIND_DATAW fd = {};
            HANDLE f = FindFirstFileW(Join(gInstallDir, L"*").c_str(), &fd);
            bool empty = true;
            if (f != INVALID_HANDLE_VALUE) {
                do { if (wcscmp(fd.cFileName, L".") && wcscmp(fd.cFileName, L"..")) empty = false; } while (FindNextFileW(f, &fd));
                FindClose(f);
            }
            if (!allowNew || !empty) Fail(L"此目录没有有效的浮岛 Win7 安装标记。为保护已有文件，已停止操作。\n" + gInstallDir);
        }
    } else if (!allowNew) Fail(L"没有找到浮岛 Win7 的安装目录。");
    const wchar_t* names[] = {kAppName, kUninstaller, kMarker, kRuntimeLicense, L"FreeIslandWin7.exe.new", L"FreeIslandWin7.Uninstall.exe.new"};
    for (unsigned i = 0; i < sizeof(names) / sizeof(names[0]); ++i) CheckPath(Join(gInstallDir, names[i]));
}
struct Payload { const BYTE* bytes; DWORD size; };
static Payload VerifyPayload() {
    HRSRC r = FindResourceW(gInstance, MAKEINTRESOURCEW(201), RT_RCDATA);
    HRSRC hr = FindResourceW(gInstance, MAKEINTRESOURCEW(202), RT_RCDATA);
    if (!r || !hr) Fail(L"安装包缺少程序或校验信息，请重新下载安装包。");
    Payload p = {static_cast<const BYTE*>(LockResource(LoadResource(gInstance, r))), SizeofResource(gInstance, r)};
    const char* expected = static_cast<const char*>(LockResource(LoadResource(gInstance, hr)));
    DWORD hashSize = SizeofResource(gInstance, hr);
    if (!p.bytes || p.size < 64 || !expected || hashSize < 64 || p.bytes[0] != 'M' || p.bytes[1] != 'Z') Fail(L"安装包内容无效。");
    HCRYPTPROV provider = 0; HCRYPTHASH hash = 0;
    if (!CryptAcquireContextW(&provider, NULL, NULL, PROV_RSA_AES, CRYPT_VERIFYCONTEXT)) Fail(ErrorText(L"无法初始化 SHA-256 校验"));
    BYTE digest[32]; DWORD n = sizeof(digest);
    BOOL ok = CryptCreateHash(provider, CALG_SHA_256, 0, 0, &hash);
    if (ok) ok = CryptHashData(hash, p.bytes, p.size, 0);
    if (ok) ok = CryptGetHashParam(hash, HP_HASHVAL, digest, &n, 0);
    if (hash) CryptDestroyHash(hash);
    CryptReleaseContext(provider, 0);
    if (!ok || n != 32) Fail(L"无法完成程序完整性校验。");
    const char* hex = "0123456789abcdef";
    for (unsigned i = 0; i < 32; ++i)
        if (expected[i*2] != hex[digest[i] >> 4] || expected[i*2+1] != hex[digest[i] & 15]) Fail(L"程序校验失败，请重新下载安装包。");
    return p;
}
// Keep the singleton object alive through replacement so a second launch cannot
// reopen the old image. Its mutex can be released before CRT/process teardown;
// capture real process handles first and also wait for those to become signaled.
class AppExitGuard {
    HANDLE mutex_ = NULL;
    bool owned_ = false;
    std::vector<HANDLE> processes_;
    void Close() {
        for (HANDLE process : processes_) CloseHandle(process);
        processes_.clear();
        if (mutex_) { if (owned_) ReleaseMutex(mutex_); CloseHandle(mutex_); mutex_ = NULL; }
    }
public:
    explicit AppExitGuard(const wstring& appPath,
        const wchar_t* mutexName = L"Local\\FreeIsland.Win7.Instance",
        const wchar_t* eventName = L"Local\\FreeIsland.Win7.Exit") {
        try {
            mutex_ = CreateMutexW(NULL, FALSE, mutexName);
            if (!mutex_) Fail(ErrorText(L"无法确认浮岛是否已退出"));
            HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == INVALID_HANDLE_VALUE) Fail(ErrorText(L"无法检查运行中的浮岛"));
            PROCESSENTRY32W item = {}; item.dwSize = sizeof(item);
            wstring name = appPath.substr(appPath.find_last_of(L"\\") + 1);
            if (Process32FirstW(snapshot, &item)) do {
                if (_wcsicmp(item.szExeFile, name.c_str()) || item.th32ProcessID == GetCurrentProcessId()) continue;
                HANDLE process = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, item.th32ProcessID);
                if (!process) continue; // A process in another account is not ours to stop.
                wchar_t path[MAX_PATH] = {}; DWORD length = MAX_PATH;
                if (QueryFullProcessImageNameW(process, 0, path, &length) && EqualPath(path, appPath))
                    processes_.push_back(process);
                else CloseHandle(process);
            } while (Process32NextW(snapshot, &item));
            CloseHandle(snapshot);
            ULONGLONG deadline = GetTickCount64() + 12000;
            do {
                // A just-starting app may create its exit event after its mutex.
                HANDLE event = OpenEventW(EVENT_MODIFY_STATE, FALSE, eventName);
                if (event) { SetEvent(event); CloseHandle(event); }
                DWORD wait = WaitForSingleObject(mutex_, 100);
                if (wait == WAIT_OBJECT_0 || wait == WAIT_ABANDONED) { owned_ = true; break; }
                if (wait == WAIT_FAILED) Fail(ErrorText(L"无法等待浮岛退出"));
            } while (GetTickCount64() < deadline);
            if (!owned_) Fail(L"浮岛仍在运行。请从系统托盘退出浮岛，然后重试。没有强制结束程序。");
            for (HANDLE process : processes_) {
                ULONGLONG now = GetTickCount64();
                DWORD remaining = now < deadline ? static_cast<DWORD>(deadline - now) : 0;
                if (WaitForSingleObject(process, remaining) != WAIT_OBJECT_0)
                    Fail(L"浮岛正在保存并退出。请稍后重试；原安装文件尚未更改。");
            }
        } catch (...) { Close(); throw; }
    }
    ~AppExitGuard() { Close(); }
    AppExitGuard(const AppExitGuard&) = delete;
    AppExitGuard& operator=(const AppExitGuard&) = delete;
};
static wstring RegString(const wchar_t* key, const wchar_t* name) {
    HKEY h; if (RegOpenKeyExW(HKEY_CURRENT_USER, key, 0, KEY_QUERY_VALUE, &h) != ERROR_SUCCESS) return L"";
    wchar_t text[2048] = {}; DWORD type = 0, cb = sizeof(text) - sizeof(wchar_t);
    LONG e = RegQueryValueExW(h, name, NULL, &type, reinterpret_cast<BYTE*>(text), &cb); RegCloseKey(h);
    if (e != ERROR_SUCCESS || type != REG_SZ || cb % sizeof(wchar_t)) return L"";
    return text;
}
static bool OwnedRun(const wstring& s) {
    wstring app = Join(gInstallDir, kAppName);
    return EqualPath(s, Quote(app) + L" --silent") || EqualPath(s, Quote(app));
}
static void SetRegistryString(HKEY h, const wchar_t* n, const wstring& value) {
    LONG e = RegSetValueExW(h, n, 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()), static_cast<DWORD>((value.size()+1)*sizeof(wchar_t)));
    if (e != ERROR_SUCCESS) { SetLastError(e); Fail(ErrorText(L"无法保存安装信息")); }
}
static void RegisterUninstall() {
    HKEY h; LONG e = RegCreateKeyExW(HKEY_CURRENT_USER, kUninstallKey, 0, NULL, 0, KEY_SET_VALUE, NULL, &h, NULL);
    if (e != ERROR_SUCCESS) { SetLastError(e); Fail(ErrorText(L"无法创建卸载信息")); }
    try {
        SetRegistryString(h, L"DisplayName", L"浮岛 Win7 · Free Island");
        SetRegistryString(h, L"DisplayVersion", L"1.0.1");
        SetRegistryString(h, L"Publisher", L"Free Island");
        SetRegistryString(h, L"InstallLocation", gInstallDir);
        SetRegistryString(h, L"DisplayIcon", Quote(Join(gInstallDir, kAppName)) + L",0");
        SetRegistryString(h, L"UninstallString", Quote(Join(gInstallDir, kUninstaller)) + L" --uninstall");
        DWORD one = 1;
        RegSetValueExW(h, L"NoModify", 0, REG_DWORD, reinterpret_cast<BYTE*>(&one), sizeof(one));
        RegSetValueExW(h, L"NoRepair", 0, REG_DWORD, reinterpret_cast<BYTE*>(&one), sizeof(one));
    } catch (...) { RegCloseKey(h); throw; }
    RegCloseKey(h);
}
static void SetStartup(bool enabled) {
    wstring old = RegString(kRunKey, L"FreeIslandWin7");
    if (!old.empty() && !OwnedRun(old)) Fail(L"同名的开机启动项属于其他位置，已保留。请在浮岛设置中检查开机启动。");
    HKEY h; LONG e = RegCreateKeyExW(HKEY_CURRENT_USER, kRunKey, 0, NULL, 0, KEY_SET_VALUE, NULL, &h, NULL);
    if (e != ERROR_SUCCESS) { SetLastError(e); Fail(ErrorText(L"无法设置开机启动")); }
    try {
        if (enabled) SetRegistryString(h, L"FreeIslandWin7", Quote(Join(gInstallDir, kAppName)) + L" --silent");
        else if (OwnedRun(old)) RegDeleteValueW(h, L"FreeIslandWin7");
    } catch (...) { RegCloseKey(h); throw; }
    RegCloseKey(h);
}
static wstring ShortcutPath(int folder) { return Join(SpecialFolder(folder), L"浮岛 Win7.lnk"); }
static bool ShortcutOwned(const wstring& p) {
    if (!Exists(p)) return false;
    CheckPath(p);
    IShellLinkW* link = NULL; IPersistFile* file = NULL;
    bool owned = false;
    if (SUCCEEDED(CoCreateInstance(CLSID_ShellLink, NULL, CLSCTX_INPROC_SERVER, IID_IShellLinkW, reinterpret_cast<void**>(&link)))) {
        if (SUCCEEDED(link->QueryInterface(IID_IPersistFile, reinterpret_cast<void**>(&file)))) {
            if (SUCCEEDED(file->Load(p.c_str(), STGM_READ))) {
                wchar_t target[MAX_PATH] = {};
                if (SUCCEEDED(link->GetPath(target, MAX_PATH, NULL, SLGP_RAWPATH))) owned = EqualPath(target, Join(gInstallDir, kAppName));
            }
            file->Release();
        }
        link->Release();
    }
    return owned;
}
static void WriteShortcut(int folder) {
    wstring p = ShortcutPath(folder); CheckPath(p);
    if (Exists(p) && !ShortcutOwned(p)) Fail(L"已有同名快捷方式，已保留：\n" + p);
    IShellLinkW* link = NULL; IPersistFile* file = NULL;
    HRESULT hr = CoCreateInstance(CLSID_ShellLink, NULL, CLSCTX_INPROC_SERVER, IID_IShellLinkW, reinterpret_cast<void**>(&link));
    if (SUCCEEDED(hr)) hr = link->SetPath(Join(gInstallDir, kAppName).c_str());
    if (SUCCEEDED(hr)) hr = link->SetWorkingDirectory(gInstallDir.c_str());
    if (SUCCEEDED(hr)) hr = link->SetDescription(L"浮岛 Win7：课堂与桌面的时间助手");
    if (SUCCEEDED(hr)) hr = link->SetIconLocation(Join(gInstallDir, kAppName).c_str(), 0);
    if (SUCCEEDED(hr)) hr = link->QueryInterface(IID_IPersistFile, reinterpret_cast<void**>(&file));
    if (SUCCEEDED(hr)) hr = file->Save(p.c_str(), TRUE);
    if (file) file->Release();
    if (link) link->Release();
    if (FAILED(hr)) Fail(L"无法创建快捷方式：\n" + p);
}
static void DeleteKnownFile(const wstring& p) {
    CheckPath(p);
    if (Exists(p) && !DeleteFileW(p.c_str())) Fail(ErrorText(L"无法删除文件：" + p));
}
template<typename Operation> static void RetryFileOperation(Operation operation, const wstring& message) {
    ULONGLONG deadline = GetTickCount64() + 3000;
    for (;;) {
        if (operation()) return;
        DWORD error = GetLastError();
        if ((error != ERROR_SHARING_VIOLATION && error != ERROR_LOCK_VIOLATION && error != ERROR_ACCESS_DENIED) ||
            GetTickCount64() >= deadline) { SetLastError(error); Fail(ErrorText(message)); }
        Sleep(75);
    }
}
static DWORD OwnedFileAttributes(const wstring& path) {
    CheckPath(path);
    DWORD attributes = GetFileAttributesW(path.c_str());
    if (attributes == INVALID_FILE_ATTRIBUTES) {
        DWORD error = GetLastError();
        if (error != ERROR_FILE_NOT_FOUND && error != ERROR_PATH_NOT_FOUND) Fail(ErrorText(L"无法检查文件：" + path));
    } else if (attributes & FILE_ATTRIBUTE_DIRECTORY) Fail(L"安装文件的位置被文件夹占用，已保留：\n" + path);
    return attributes;
}
static void SetOwnedAttributes(const wstring& path, DWORD attributes) {
    CheckPath(path);
    RetryFileOperation([&]() { return SetFileAttributesW(path.c_str(), attributes); }, L"无法更新安装文件属性：" + path);
}
static void RemoveOwnedFile(const wstring& path) {
    DWORD attributes = OwnedFileAttributes(path);
    if (attributes == INVALID_FILE_ATTRIBUTES) return;
    if (attributes & FILE_ATTRIBUTE_READONLY) SetOwnedAttributes(path, attributes & ~FILE_ATTRIBUTE_READONLY);
    try {
        RetryFileOperation([&]() { CheckPath(path); return DeleteFileW(path.c_str()); }, L"无法移除安装文件：" + path);
    } catch (...) {
        if (Exists(path)) SetFileAttributesW(path.c_str(), attributes);
        throw;
    }
}
static void MoveOwnedFile(const wstring& from, const wstring& to) {
    RetryFileOperation([&]() {
        OwnedFileAttributes(from); CheckPath(to);
        // Never replace an unexpected backup or destination file.
        return MoveFileExW(from.c_str(), to.c_str(), MOVEFILE_WRITE_THROUGH);
    }, L"无法替换安装文件，请关闭占用文件的程序后重试：" + to);
}
class PayloadTransaction {
    struct Entry {
        wstring destination, staged, backup;
        DWORD attributes = INVALID_FILE_ATTRIBUTES;
        bool backedUp = false, installed = false;
    };
    wstring directory_;
    std::vector<Entry> entries_;
    bool finished_ = false;
    wstring Cleanup() {
        wstring errors;
        for (Entry& item : entries_) {
            try { RemoveOwnedFile(item.staged); RemoveOwnedFile(item.backup); }
            catch (const wstring& error) { errors += error + L"\n"; }
        }
        if (errors.empty() && !RemoveDirectoryW(directory_.c_str()) && GetLastError() != ERROR_PATH_NOT_FOUND)
            errors += L"临时安装文件夹已保留：" + directory_ + L"\n";
        return errors;
    }
public:
    explicit PayloadTransaction(const wstring& root) {
        for (unsigned attempt = 0; attempt < 100; ++attempt) {
            directory_ = Join(root, L".freeisland-upgrade-" + std::to_wstring(GetCurrentProcessId()) + L"-" +
                std::to_wstring(GetTickCount64()) + L"-" + std::to_wstring(attempt));
            CheckPath(directory_);
            if (CreateDirectoryW(directory_.c_str(), NULL)) return;
            if (GetLastError() != ERROR_ALREADY_EXISTS) Fail(ErrorText(L"无法创建安装暂存目录"));
        }
        Fail(L"无法创建独立安装暂存目录，请稍后重试。");
    }
    void Add(const wstring& destination, const void* data, DWORD size, const wstring& copyFrom = L"") {
        Entry entry;
        entry.destination = destination;
        entry.staged = Join(directory_, std::to_wstring(entries_.size()) + L".new");
        entry.backup = Join(directory_, std::to_wstring(entries_.size()) + L".old");
        entry.attributes = OwnedFileAttributes(destination);
        entries_.push_back(entry);
        if (copyFrom.empty()) WriteBytes(entry.staged, data, size, false);
        else {
            RetryFileOperation([&]() { CheckPath(entry.staged); return CopyFileW(copyFrom.c_str(), entry.staged.c_str(), TRUE); }, L"无法暂存卸载程序");
            DWORD attributes = OwnedFileAttributes(entry.staged);
            if (attributes & FILE_ATTRIBUTE_READONLY) SetOwnedAttributes(entry.staged, attributes & ~FILE_ATTRIBUTE_READONLY);
        }
    }
    void Apply() {
        for (Entry& item : entries_) {
            // Recheck immediately before mutation, including hard links and directories.
            item.attributes = OwnedFileAttributes(item.destination);
            if (item.attributes != INVALID_FILE_ATTRIBUTES) {
                if (item.attributes & FILE_ATTRIBUTE_READONLY)
                    SetOwnedAttributes(item.destination, item.attributes & ~FILE_ATTRIBUTE_READONLY);
                try { MoveOwnedFile(item.destination, item.backup); }
                catch (...) { SetFileAttributesW(item.destination.c_str(), item.attributes); throw; }
                item.backedUp = true;
            }
            MoveOwnedFile(item.staged, item.destination);
            item.installed = true;
        }
    }
    wstring Rollback() {
        wstring errors;
        for (auto iterator = entries_.rbegin(); iterator != entries_.rend(); ++iterator) {
            Entry& item = *iterator;
            try {
                if (item.installed) { RemoveOwnedFile(item.destination); item.installed = false; }
                if (item.backedUp) {
                    MoveOwnedFile(item.backup, item.destination); item.backedUp = false;
                    SetOwnedAttributes(item.destination, item.attributes);
                }
            } catch (const wstring& error) { errors += error + L"\n"; }
        }
        finished_ = true;
        if (errors.empty()) return Cleanup();
        return errors + L"原文件备份已保留，请勿删除此目录：\n" + directory_ + L"\n";
    }
    wstring Commit() { finished_ = true; return Cleanup(); }
    ~PayloadTransaction() { if (!finished_) { try { Rollback(); } catch (...) {} } }
    PayloadTransaction(const PayloadTransaction&) = delete;
    PayloadTransaction& operator=(const PayloadTransaction&) = delete;
};
static void Progress(int n) { PostMessageW(gWindow, WM_INSTALL_PROGRESS, n, 0); }
static wstring Install() {
    Payload p = VerifyPayload(); Progress(15);
    HRSRC licenseResource = FindResourceW(gInstance, MAKEINTRESOURCEW(203), RT_RCDATA);
    const void* license = licenseResource ? LockResource(LoadResource(gInstance, licenseResource)) : NULL;
    DWORD licenseSize = licenseResource ? SizeofResource(gInstance, licenseResource) : 0;
    if (!license || !licenseSize) Fail(L"安装包缺少运行库许可信息，请重新下载安装包。");
    CheckOwnedInstall(true);
    wstring registered = RegString(kUninstallKey, L"InstallLocation");
    if (!registered.empty() && !EqualPath(registered, gInstallDir)) Fail(L"同名卸载信息属于其他安装位置，已停止操作。");
    AppExitGuard appExit(Join(gInstallDir, kAppName)); Progress(30);
    MakeDirectory(gInstallDir);
    // A recognizable marker makes an interrupted installation safely retryable.
    if (!MarkerMatches()) WriteBytes(Join(gInstallDir, kMarker), kMarkerText, sizeof(kMarkerText)-1, false);
    PayloadTransaction transaction(gInstallDir);
    wstring warnings;
    try {
        transaction.Add(Join(gInstallDir, kAppName), p.bytes, p.size);
        transaction.Add(Join(gInstallDir, kUninstaller), NULL, 0, ModulePath());
        transaction.Add(Join(gInstallDir, kRuntimeLicense), license, licenseSize);
        Progress(55);
        transaction.Apply();
    } catch (const wstring& error) {
        wstring rollback = transaction.Rollback();
        Fail(error + (rollback.empty() ? L"\n原安装文件已恢复，可稍后重试。" : L"\n" + rollback));
    }
    warnings += transaction.Commit();
    // Payloads are now complete. Registration/shortcut failures are actionable
    // warnings, rather than reporting failure after leaving a working upgrade.
    try { RegisterUninstall(); } catch (const wstring& s) { warnings += s + L"\n"; }
    Progress(75);
    try { SetStartup(gStartupWanted); } catch (const wstring& s) { warnings += s + L"\n"; }
    try { WriteShortcut(CSIDL_PROGRAMS); } catch (const wstring& s) { warnings += s + L"\n"; }
    if (gDesktopWanted) { try { WriteShortcut(CSIDL_DESKTOPDIRECTORY); } catch (const wstring& s) { warnings += s + L"\n"; } }
    else { try { wstring shortcut = ShortcutPath(CSIDL_DESKTOPDIRECTORY); if (ShortcutOwned(shortcut)) DeleteKnownFile(shortcut); } catch (const wstring& s) { warnings += s + L"\n"; } }
    Progress(100); return warnings;
}
struct WorkResult { bool ok; wstring message; };
static DWORD WINAPI InstallThread(void*) {
    CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    WorkResult* r = new WorkResult();
    try { r->message = Install(); r->ok = true; }
    catch (const wstring& s) { r->ok = false; r->message = s; }
    catch (...) { r->ok = false; r->message = L"安装没有完成，请重新运行安装包。"; }
    CoUninitialize(); PostMessageW(gWindow, WM_INSTALL_FINISH, 0, reinterpret_cast<LPARAM>(r)); return 0;
}
static void UninstallKnown() {
    CheckOwnedInstall(false); AppExitGuard appExit(Join(gInstallDir, kAppName));
    // Keep the installed uninstaller until all operations that can fail are complete.
    DeleteKnownFile(Join(gInstallDir, L"FreeIslandWin7.exe.new"));
    DeleteKnownFile(Join(gInstallDir, L"FreeIslandWin7.Uninstall.exe.new"));
    const int folders[] = {CSIDL_PROGRAMS, CSIDL_DESKTOPDIRECTORY};
    for (unsigned i = 0; i < 2; ++i) { wstring s = ShortcutPath(folders[i]); if (ShortcutOwned(s)) DeleteKnownFile(s); }
    DeleteKnownFile(Join(gInstallDir, kRuntimeLicense));
    DeleteKnownFile(Join(gInstallDir, kAppName));
    wstring run = RegString(kRunKey, L"FreeIslandWin7");
    if (OwnedRun(run)) {
        HKEY h; if (RegOpenKeyExW(HKEY_CURRENT_USER, kRunKey, 0, KEY_SET_VALUE, &h) == ERROR_SUCCESS) {
            RegDeleteValueW(h, L"FreeIslandWin7"); RegCloseKey(h);
        }
    }
    if (EqualPath(RegString(kUninstallKey, L"InstallLocation"), gInstallDir) &&
        EqualPath(RegString(kUninstallKey, L"UninstallString"), Quote(Join(gInstallDir, kUninstaller)) + L" --uninstall"))
        RegDeleteKeyW(HKEY_CURRENT_USER, kUninstallKey);
    DeleteKnownFile(Join(gInstallDir, kUninstaller));
    DeleteKnownFile(Join(gInstallDir, kMarker));
    // RemoveDirectory only succeeds when empty; user-added files are preserved.
    RemoveDirectoryW(gInstallDir.c_str());
}
static void BeginUninstall() {
    if (!EqualPath(ModulePath(), Join(gInstallDir, kUninstaller))) Fail(L"请运行安装目录中的卸载程序，或使用控制面板卸载浮岛 Win7。");
    CheckOwnedInstall(false);
    if (MessageBoxW(NULL, L"卸载浮岛 Win7？\n\n浮岛会正常退出。本地设置和日程将保留，方便以后重新安装。", L"卸载浮岛 Win7", MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2) != IDYES) return;
    wchar_t temp[MAX_PATH] = {}, target[MAX_PATH] = {};
    DWORD n = GetTempPathW(MAX_PATH, temp);
    if (!n || n >= MAX_PATH) Fail(L"无法获取临时目录。");
    CheckPath(temp);
    if (!GetTempFileNameW(temp, L"FI7", 0, target)) Fail(ErrorText(L"无法创建临时卸载程序"));
    wstring exe = wstring(target) + L".exe";
    bool copied = false;
    try {
        CheckPath(exe);
        if (!CopyFileW(ModulePath().c_str(), exe.c_str(), TRUE)) Fail(ErrorText(L"无法复制临时卸载程序"));
        copied = true;
        DeleteKnownFile(target);
        wstring command = Quote(exe) + L" --remove " + std::to_wstring(GetCurrentProcessId());
        std::vector<wchar_t> buffer(command.begin(), command.end()); buffer.push_back(0);
        STARTUPINFOW startup = {}; startup.cb = sizeof(startup); PROCESS_INFORMATION pi = {};
        if (!CreateProcessW(exe.c_str(), buffer.data(), NULL, NULL, FALSE, 0, NULL, temp, &startup, &pi)) Fail(ErrorText(L"无法启动卸载程序"));
        CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
    } catch (...) { DeleteFileW(target); if (copied) DeleteFileW(exe.c_str()); throw; }
}
static void RoundFill(Gdiplus::Graphics& g, float x, float y, float w, float h, float r, Gdiplus::Color c) {
    Gdiplus::GraphicsPath p; float d = r*2;
    p.AddArc(x,y,d,d,180,90); p.AddArc(x+w-d,y,d,d,270,90); p.AddArc(x+w-d,y+h-d,d,d,0,90); p.AddArc(x,y+h-d,d,d,90,90); p.CloseFigure();
    Gdiplus::SolidBrush b(c); g.FillPath(&b,&p);
}
static void PaintText(HDC dc, const wstring& text, int x, int y, int w, int h, HFONT font, COLORREF color, UINT flags = DT_LEFT | DT_WORDBREAK) {
    HFONT old = static_cast<HFONT>(SelectObject(dc, font)); SetTextColor(dc, color); SetBkMode(dc, TRANSPARENT);
    RECT r = {x,y,x+w,y+h}; DrawTextW(dc,text.c_str(),-1,&r,flags); SelectObject(dc,old);
}
static void PaintWindow(HWND hwnd) {
    PAINTSTRUCT ps; HDC dc = BeginPaint(hwnd,&ps); RECT client; GetClientRect(hwnd,&client);
    HDC mem = CreateCompatibleDC(dc); HBITMAP bitmap = CreateCompatibleBitmap(dc,client.right,client.bottom); HGDIOBJ old = SelectObject(mem,bitmap);
    FillRect(mem,&client,gPaperBrush);
    { Gdiplus::Graphics g(mem); g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
      RoundFill(g,32,153,576,190,16,Gdiplus::Color(255,255,255,255));
      RoundFill(g,496,48,106,36,18,Gdiplus::Color(255,79,102,232));
      RoundFill(g,523,94,66,14,7,Gdiplus::Color(255,185,205,255));
    }
    PaintText(mem,gDone ? L"浮岛已准备好" : L"把时间，放在手边。",36,43,450,53,gTitleFont,RGB(24,36,58));
    PaintText(mem,gDone ? L"从悬浮球开始，课堂与桌面随时切换。" : L"浮岛 Win7 · 课堂与桌面的时间助手",38,104,460,34,gBodyFont,RGB(88,101,122));
    PaintText(mem,L"安装到当前账户",52,173,530,28,gBodyFont,RGB(24,36,58));
    PaintText(mem,gInstallDir,52,211,530,25,gSmallFont,RGB(88,101,122),DT_LEFT | DT_SINGLELINE | DT_PATH_ELLIPSIS);
    PaintText(mem,gStatus,36,351,568,42,gSmallFont,gDone ? RGB(39,105,86) : RGB(88,101,122));
    PaintText(mem,L"Windows 7 · 32 / 64 位",36,421,250,22,gSmallFont,RGB(88,101,122));
    BitBlt(dc,0,0,client.right,client.bottom,mem,0,0,SRCCOPY);
    SelectObject(mem,old); DeleteObject(bitmap); DeleteDC(mem); EndPaint(hwnd,&ps);
}
static LRESULT CALLBACK WindowProc(HWND hwnd, UINT message, WPARAM wp, LPARAM lp) {
    switch(message) {
    case WM_CREATE: {
        gWindow=hwnd;
        gStartup=CreateWindowW(L"BUTTON",L"登录后静默启动",WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_AUTOCHECKBOX,52,242,510,44,hwnd,reinterpret_cast<HMENU>(101),gInstance,NULL);
        gDesktop=CreateWindowW(L"BUTTON",L"创建桌面快捷方式",WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_AUTOCHECKBOX,52,289,510,44,hwnd,reinterpret_cast<HMENU>(102),gInstance,NULL);
        gInstall=CreateWindowW(L"BUTTON",L"安装浮岛",WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,384,407,224,50,hwnd,reinterpret_cast<HMENU>(103),gInstance,NULL);
        gClose=CreateWindowW(L"BUTTON",L"关闭",WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,276,407,92,50,hwnd,reinterpret_cast<HMENU>(104),gInstance,NULL);
        gProgress=CreateWindowExW(0,PROGRESS_CLASSW,L"",WS_CHILD|PBS_SMOOTH,36,390,568,6,hwnd,NULL,gInstance,NULL);
        SendMessageW(gProgress,PBM_SETRANGE32,0,100);
        SendMessageW(gStartup,BM_SETCHECK,gStartupWanted ? BST_CHECKED : BST_UNCHECKED,0);
        SendMessageW(gDesktop,BM_SETCHECK,gDesktopWanted ? BST_CHECKED : BST_UNCHECKED,0);
        HWND all[] = {gStartup,gDesktop,gInstall,gClose};
        for (unsigned i=0;i<4;++i) SendMessageW(all[i],WM_SETFONT,reinterpret_cast<WPARAM>(gBodyFont),TRUE);
        SetFocus(gInstall); return 0;
    }
    case WM_PAINT: PaintWindow(hwnd); return 0;
    case WM_ERASEBKGND: return 1;
    case WM_CTLCOLORBTN:
    case WM_CTLCOLORSTATIC: SetBkMode(reinterpret_cast<HDC>(wp),TRANSPARENT); SetTextColor(reinterpret_cast<HDC>(wp),RGB(24,36,58)); return reinterpret_cast<LRESULT>(gWhiteBrush);
    case WM_DRAWITEM: {
        DRAWITEMSTRUCT* d=reinterpret_cast<DRAWITEMSTRUCT*>(lp); bool main=d->CtlID==103;
        FillRect(d->hDC,&d->rcItem,gPaperBrush);
        Gdiplus::Graphics g(d->hDC); g.SetSmoothingMode(Gdiplus::SmoothingModeAntiAlias);
        Gdiplus::Color c=main ? Gdiplus::Color(255,79,102,232) : Gdiplus::Color(255,237,241,249);
        if (d->itemState&ODS_SELECTED) c=main ? Gdiplus::Color(255,52,72,186) : Gdiplus::Color(255,221,229,245);
        if (d->itemState&ODS_DISABLED) c=Gdiplus::Color(255,220,226,235);
        RoundFill(g,0,0,static_cast<float>(d->rcItem.right),static_cast<float>(d->rcItem.bottom),13,c);
        wchar_t text[80]; GetWindowTextW(d->hwndItem,text,80);
        PaintText(d->hDC,text,0,0,d->rcItem.right,d->rcItem.bottom,gBodyFont,(d->itemState&ODS_DISABLED) ? RGB(88,101,122) : (main ? RGB(255,255,255) : RGB(24,36,58)),DT_CENTER|DT_VCENTER|DT_SINGLELINE);
        if (d->itemState&ODS_FOCUS) { RECT f=d->rcItem; InflateRect(&f,-5,-5); DrawFocusRect(d->hDC,&f); }
        return TRUE;
    }
    case WM_COMMAND:
        if (LOWORD(wp)==104) { if(!gBusy) DestroyWindow(hwnd); return 0; }
        if (LOWORD(wp)==103 && !gBusy) {
            if (gDone) {
                HINSTANCE r=ShellExecuteW(hwnd,L"open",Join(gInstallDir,kAppName).c_str(),NULL,gInstallDir.c_str(),SW_SHOWNORMAL);
                if (reinterpret_cast<INT_PTR>(r)<=32) MessageBoxW(hwnd,L"无法打开浮岛，请从开始菜单重试。",L"浮岛 Win7",MB_OK|MB_ICONERROR);
                else DestroyWindow(hwnd);
                return 0;
            }
            gStartupWanted=SendMessageW(gStartup,BM_GETCHECK,0,0)==BST_CHECKED;
            gDesktopWanted=SendMessageW(gDesktop,BM_GETCHECK,0,0)==BST_CHECKED;
            gBusy=true; EnableWindow(gInstall,FALSE); EnableWindow(gClose,FALSE); EnableWindow(gStartup,FALSE); EnableWindow(gDesktop,FALSE);
            SetWindowTextW(gInstall,L"正在安装…"); gStatus=L"正在校验并安装，请稍候。"; ShowWindow(gProgress,SW_SHOW); InvalidateRect(hwnd,NULL,FALSE);
            HANDLE worker=CreateThread(NULL,0,InstallThread,NULL,0,NULL);
            if (worker) CloseHandle(worker);
            else { WorkResult* r=new WorkResult(); r->ok=false; r->message=L"无法启动安装，请关闭后重试。"; PostMessageW(hwnd,WM_INSTALL_FINISH,0,reinterpret_cast<LPARAM>(r)); }
            return 0;
        }
        break;
    case WM_INSTALL_PROGRESS: SendMessageW(gProgress,PBM_SETPOS,wp,0); return 0;
    case WM_INSTALL_FINISH: {
        WorkResult* r=reinterpret_cast<WorkResult*>(lp); gBusy=false; gDone=r->ok;
        EnableWindow(gInstall,TRUE); EnableWindow(gClose,TRUE);
        EnableWindow(gStartup,!gDone); EnableWindow(gDesktop,!gDone);
        SetWindowTextW(gInstall,gDone ? L"打开浮岛" : L"重新安装");
        gStatus=gDone ? L"安装完成。下次登录时，浮岛会按你的选择启动。" : L"安装未完成。处理提示后可以重新安装。";
        ShowWindow(gProgress,SW_HIDE); InvalidateRect(hwnd,NULL,FALSE);
        if (!r->message.empty()) MessageBoxW(hwnd,r->message.c_str(),gDone ? L"安装完成，部分选项需要处理" : L"安装未完成",MB_OK|(gDone ? MB_ICONWARNING : MB_ICONERROR));
        delete r; SetFocus(gInstall); return 0;
    }
    case WM_CLOSE: if(!gBusy) DestroyWindow(hwnd); return 0;
    case WM_DESTROY: PostQuitMessage(0); return 0;
    }
    return DefWindowProcW(hwnd,message,wp,lp);
}
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE,LPWSTR,int show) {
    gInstance=instance;
    SetProcessDPIAware();
    CoInitializeEx(NULL,COINIT_APARTMENTTHREADED);
    int result=0; HANDLE setupMutex=NULL; ULONG_PTR gdiplus=0;
    try {
        int argc=0; LPWSTR* argv=CommandLineToArgvW(GetCommandLineW(),&argc);
        std::vector<wstring> args; if(!argv) Fail(L"无法读取启动参数。");
        for(int i=1;i<argc;++i) args.push_back(argv[i]);
        LocalFree(argv);
        gInstallDir=Join(Join(SpecialFolder(CSIDL_LOCAL_APPDATA),L"Programs"),L"FreeIslandWin7");
        if (args.size()==2 && args[0]==L"--verify-payload") {
            Payload p=VerifyPayload(); wstring folder=FullPath(args[1]); MakeDirectory(folder);
            // Verification never modifies startup, shortcuts, app data, or installation state.
            WriteBytes(Join(folder,kAppName),p.bytes,p.size,true);
        } else if (args.size()==2 && args[0]==L"--remove") {
            wchar_t* end=NULL; unsigned long parent=wcstoul(args[1].c_str(),&end,10);
            if (!parent || !end || *end || parent==GetCurrentProcessId()) Fail(L"卸载参数无效。");
            // A detached temporary copy waits for the installed uninstaller to exit.
            HANDLE h=OpenProcess(SYNCHRONIZE,FALSE,static_cast<DWORD>(parent));
            if(h) { DWORD wait=WaitForSingleObject(h,8000); CloseHandle(h); if(wait!=WAIT_OBJECT_0) Fail(L"原卸载程序尚未退出，请稍后重试。"); }
            setupMutex=CreateMutexW(NULL,TRUE,L"Local\\FreeIsland.Win7.Setup");
            if(!setupMutex || GetLastError()==ERROR_ALREADY_EXISTS) Fail(L"另一个安装或卸载任务正在运行，请稍后再试。");
            UninstallKnown(); MessageBoxW(NULL,L"浮岛 Win7 已卸载。\n本地设置和日程已保留。",L"卸载完成",MB_OK|MB_ICONINFORMATION);
        } else if ((args.size()==1 && args[0]==L"--uninstall") || (args.empty() && EqualPath(ModulePath(),Join(gInstallDir,kUninstaller)))) {
            BeginUninstall();
        } else if (!args.empty()) {
            Fail(L"不支持的启动参数。");
        } else {
            setupMutex=CreateMutexW(NULL,TRUE,L"Local\\FreeIsland.Win7.Setup");
            if(!setupMutex || GetLastError()==ERROR_ALREADY_EXISTS) Fail(L"另一个安装或卸载任务正在运行，请稍后再试。");
            if (MarkerMatches()) {
                gStartupWanted = OwnedRun(RegString(kRunKey, L"FreeIslandWin7"));
                gDesktopWanted = ShortcutOwned(ShortcutPath(CSIDL_DESKTOPDIRECTORY));
            }
            Gdiplus::GdiplusStartupInput input;
            if(Gdiplus::GdiplusStartup(&gdiplus,&input,NULL)!=Gdiplus::Ok) Fail(L"无法初始化 Windows 图形组件。");
            INITCOMMONCONTROLSEX common={sizeof(common),ICC_PROGRESS_CLASS}; InitCommonControlsEx(&common);
            gBodyFont=CreateFontW(-18,0,0,0,FW_NORMAL,FALSE,FALSE,FALSE,DEFAULT_CHARSET,OUT_DEFAULT_PRECIS,CLIP_DEFAULT_PRECIS,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Microsoft YaHei");
            gTitleFont=CreateFontW(-31,0,0,0,FW_SEMIBOLD,FALSE,FALSE,FALSE,DEFAULT_CHARSET,OUT_DEFAULT_PRECIS,CLIP_DEFAULT_PRECIS,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Microsoft YaHei");
            gSmallFont=CreateFontW(-14,0,0,0,FW_NORMAL,FALSE,FALSE,FALSE,DEFAULT_CHARSET,OUT_DEFAULT_PRECIS,CLIP_DEFAULT_PRECIS,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Microsoft YaHei");
            gPaperBrush=CreateSolidBrush(RGB(246,247,251)); gWhiteBrush=CreateSolidBrush(RGB(255,255,255));
            gStatus=L"无需管理员权限。安装后即可开始使用。";
            WNDCLASSEXW wc={}; wc.cbSize=sizeof(wc); wc.hInstance=instance; wc.lpfnWndProc=WindowProc;
            wc.lpszClassName=L"FreeIslandWin7Setup"; wc.hCursor=LoadCursorW(NULL,IDC_ARROW); wc.hIcon=LoadIconW(instance,MAKEINTRESOURCEW(101)); wc.hIconSm=wc.hIcon;
            if(!RegisterClassExW(&wc)) Fail(L"无法创建安装窗口。");
            RECT r={0,0,640,480}; DWORD style=WS_OVERLAPPED|WS_CAPTION|WS_SYSMENU|WS_MINIMIZEBOX; AdjustWindowRectEx(&r,style,FALSE,0);
            int w=r.right-r.left,h=r.bottom-r.top;
            HWND window=CreateWindowExW(0,wc.lpszClassName,L"浮岛 Win7 安装",style,(GetSystemMetrics(SM_CXSCREEN)-w)/2,(GetSystemMetrics(SM_CYSCREEN)-h)/2,w,h,NULL,NULL,instance,NULL);
            if(!window) Fail(L"无法显示安装窗口。");
            ShowWindow(window,show); UpdateWindow(window);
            MSG message;
            while(GetMessageW(&message,NULL,0,0)>0) { if(!IsDialogMessageW(window,&message)) { TranslateMessage(&message); DispatchMessageW(&message); } }
        }
    } catch(const wstring& e) { MessageBoxW(NULL,e.c_str(),L"浮岛 Win7",MB_OK|MB_ICONERROR); result=1; }
      catch(...) { MessageBoxW(NULL,L"操作未完成，请重新运行安装程序。",L"浮岛 Win7",MB_OK|MB_ICONERROR); result=2; }
    if(setupMutex) { ReleaseMutex(setupMutex); CloseHandle(setupMutex); }
    if(gBodyFont) DeleteObject(gBodyFont);
    if(gTitleFont) DeleteObject(gTitleFont);
    if(gSmallFont) DeleteObject(gSmallFont);
    if(gPaperBrush) DeleteObject(gPaperBrush);
    if(gWhiteBrush) DeleteObject(gWhiteBrush);
    if(gdiplus) Gdiplus::GdiplusShutdown(gdiplus);
    CoUninitialize(); return result;
}
