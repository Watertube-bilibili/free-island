// Standalone console harness. Only creates files below the supplied workspace
// test root and unique test IPC objects; never calls Install or registry writers.
#include "../src/installer.cpp"
#include <iostream>
#include <stdexcept>

static void Require(bool value, const char* message) {
    if (!value) throw std::runtime_error(message);
}
static void Put(const wstring& path, const char* value) {
    WriteBytes(path, value, static_cast<DWORD>(strlen(value)), false);
}
static std::string Read(const wstring& path) {
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL);
    Require(file != INVALID_HANDLE_VALUE, "read failed");
    char buffer[128] = {}; DWORD count = 0;
    BOOL ok = ReadFile(file, buffer, sizeof(buffer), &count, NULL); CloseHandle(file);
    Require(ok != FALSE, "read incomplete");
    return std::string(buffer, count);
}
static DWORD WINAPI UnlockLater(void* handle) { Sleep(400); CloseHandle(handle); return 0; }
static void Seed(const wstring& folder) {
    MakeDirectory(folder);
    Put(Join(folder, kAppName), "old-app");
    Put(Join(folder, kUninstaller), "old-uninstaller");
    Put(Join(folder, kRuntimeLicense), "old-license");
    Put(Join(folder, L"user-data.txt"), "user-state");
    SetFileAttributesW(Join(folder, kAppName).c_str(), FILE_ATTRIBUTE_READONLY);
}
static void Stage(PayloadTransaction& transaction, const wstring& folder) {
    transaction.Add(Join(folder, kAppName), "new-app", 7);
    transaction.Add(Join(folder, kUninstaller), "new-uninstaller", 15);
    transaction.Add(Join(folder, kRuntimeLicense), "new-license", 11);
}
static void CheckOld(const wstring& folder) {
    Require(Read(Join(folder, kAppName)) == "old-app", "original application not restored");
    Require(Read(Join(folder, kUninstaller)) == "old-uninstaller", "original uninstaller changed");
    Require(Read(Join(folder, kRuntimeLicense)) == "old-license", "original license changed");
    Require((GetFileAttributesW(Join(folder, kAppName).c_str()) & FILE_ATTRIBUTE_READONLY) != 0,
        "original read-only attribute not restored");
    Require(Read(Join(folder, L"user-data.txt")) == "user-state", "user state changed");
}
int wmain(int argc, wchar_t** argv) {
    if (argc == 5 && wstring(argv[1]) == L"--hold-test-app") {
        HANDLE mutex = CreateMutexW(NULL, TRUE, argv[2]);
        HANDLE event = CreateEventW(NULL, FALSE, FALSE, argv[3]);
        Put(argv[4], "ready");
        DWORD waited = WaitForSingleObject(event, 10000);
        ReleaseMutex(mutex); CloseHandle(mutex); CloseHandle(event);
        // Reproduce the real bug: mutex is released while the image remains mapped.
        Sleep(800);
        return waited == WAIT_OBJECT_0 ? 0 : 1;
    }
    try {
        Require(argc == 2, "pass a workspace-only test output directory");
        const wstring root = FullPath(argv[1]);
        Require(!Exists(root), "test directory must be new");
        MakeDirectory(root);
        {
            wstring folder = Join(root, L"transient-lock"); Seed(folder);
            SetFileAttributesW(Join(folder, kRuntimeLicense).c_str(), FILE_ATTRIBUTE_READONLY);
            PayloadTransaction transaction(folder); Stage(transaction, folder);
            HANDLE locked = CreateFileW(Join(folder, kAppName).c_str(), GENERIC_READ, FILE_SHARE_READ,
                NULL, OPEN_EXISTING, 0, NULL);
            Require(locked != INVALID_HANDLE_VALUE, "cannot create transient lock");
            HANDLE worker = CreateThread(NULL, 0, UnlockLater, locked, 0, NULL);
            Require(worker != NULL, "cannot create unlock worker");
            transaction.Apply(); Require(transaction.Commit().empty(), "successful upgrade cleanup failed");
            WaitForSingleObject(worker, INFINITE); CloseHandle(worker);
            Require(Read(Join(folder, kAppName)) == "new-app", "new application missing");
            Require(Read(Join(folder, kUninstaller)) == "new-uninstaller", "new uninstaller missing");
            Require(Read(Join(folder, kRuntimeLicense)) == "new-license", "new license missing");
            Require(Read(Join(folder, L"user-data.txt")) == "user-state", "user state changed");
            std::cout << "PASS: transient sharing lock retried; read-only payloads upgraded; user data preserved\n";
        }
        {
            wstring folder = Join(root, L"rollback"); Seed(folder);
            PayloadTransaction transaction(folder); Stage(transaction, folder);
            HANDLE locked = CreateFileW(Join(folder, kUninstaller).c_str(), GENERIC_READ, FILE_SHARE_READ,
                NULL, OPEN_EXISTING, 0, NULL);
            Require(locked != INVALID_HANDLE_VALUE, "cannot lock second destination");
            bool failed = false;
            try { transaction.Apply(); } catch (const wstring&) { failed = true; }
            CloseHandle(locked);
            Require(failed, "permanent second-payload lock was not detected");
            Require(transaction.Rollback().empty(), "rollback failed"); CheckOld(folder);
            std::cout << "PASS: second-payload failure restores first payload and original attributes\n";
        }
        {
            wstring folder = Join(root, L"staging-failure"); Seed(folder);
            bool failed = false;
            {
                PayloadTransaction transaction(folder);
                transaction.Add(Join(folder, kAppName), "new-app", 7);
                try { transaction.Add(Join(folder, kUninstaller), NULL, 0, Join(root, L"missing-source")); }
                catch (const wstring&) { failed = true; }
            }
            Require(failed, "missing staging source accepted"); CheckOld(folder);
            std::cout << "PASS: staging failure leaves all installed payloads unchanged\n";
        }
        {
            wstring folder = Join(root, L"hardlink"); MakeDirectory(folder);
            Put(Join(folder, L"user-data.txt"), "user-state");
            Require(CreateHardLinkW(Join(folder, kAppName).c_str(), Join(folder, L"user-data.txt").c_str(), NULL) != FALSE,
                "cannot create hardlink safety fixture");
            bool failed = false;
            try { PayloadTransaction transaction(folder); transaction.Add(Join(folder, kAppName), "bad", 3); }
            catch (const wstring&) { failed = true; }
            Require(failed, "hard-linked payload accepted");
            Require(Read(Join(folder, L"user-data.txt")) == "user-state", "hardlink modified user file");
            std::cout << "PASS: hard-linked destination rejected without modifying linked user file\n";
        }
        {
            wstring folder = Join(root, L"process-exit"); MakeDirectory(folder);
            wstring executable = Join(folder, kAppName), ready = Join(folder, L"ready.txt");
            Require(CopyFileW(ModulePath().c_str(), executable.c_str(), TRUE) != FALSE, "cannot copy process fixture");
            wstring prefix = L"Local\\FreeIsland.Win7.UpgradeTest." + std::to_wstring(GetCurrentProcessId());
            wstring mutexName = prefix + L".Instance", eventName = prefix + L".Exit";
            wstring command = Quote(executable) + L" --hold-test-app " + Quote(mutexName) + L" " + Quote(eventName) + L" " + Quote(ready);
            std::vector<wchar_t> buffer(command.begin(), command.end()); buffer.push_back(0);
            STARTUPINFOW startup = {}; startup.cb = sizeof(startup); PROCESS_INFORMATION child = {};
            Require(CreateProcessW(executable.c_str(), buffer.data(), NULL, NULL, FALSE, CREATE_NO_WINDOW,
                NULL, folder.c_str(), &startup, &child) != FALSE, "cannot launch process fixture");
            CloseHandle(child.hThread);
            ULONGLONG deadline = GetTickCount64() + 5000;
            while (!Exists(ready) && GetTickCount64() < deadline) Sleep(25);
            Require(Exists(ready), "process fixture did not start");
            ULONGLONG start = GetTickCount64();
            {
                AppExitGuard guard(executable, mutexName.c_str(), eventName.c_str());
                Require(GetTickCount64() - start >= 700, "guard only waited for mutex, not process exit");
                Require(WaitForSingleObject(child.hProcess, 0) == WAIT_OBJECT_0, "process still mapped after guard");
                HANDLE singleton = OpenMutexW(SYNCHRONIZE, FALSE, mutexName.c_str());
                Require(singleton != NULL, "guard did not keep singleton object alive"); CloseHandle(singleton);
            }
            CloseHandle(child.hProcess);
            HANDLE released = OpenMutexW(SYNCHRONIZE, FALSE, mutexName.c_str());
            if (released) CloseHandle(released);
            Require(released == NULL, "guard leaked singleton handle");
            std::cout << "PASS: full process exit awaited after early mutex release; singleton guard released afterward\n";
        }
        Put(Join(root, L"result.txt"), "PASS: transient lock, read-only payloads, rollback, staging failure, hardlink rejection, full process exit, user data preservation\r\n");
        return 0;
    } catch (const wstring& error) { std::wcerr << L"FAIL: " << error << L"\n"; }
      catch (const std::exception& error) { std::cerr << "FAIL: " << error.what() << "\n"; }
    return 1;
}
