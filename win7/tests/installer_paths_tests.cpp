// Isolated path/ownership harness: no Install, UninstallKnown, registry writes,
// desktop shortcuts, or recursive deletion. Fixtures remain under a new root.
#include "../src/installer.cpp"
#include <winioctl.h>
#include <iostream>
#include <stdexcept>
#include <functional>

static void Require(bool value, const char* message) { if(!value) throw std::runtime_error(message); }
static void Reject(const std::function<void()>& operation, const char* message) {
    bool rejected=false; try { operation(); } catch(const wstring&) { rejected=true; }
    Require(rejected,message);
}
static void Put(const wstring& path, const char* text) { WriteBytes(path,text,static_cast<DWORD>(strlen(text)),false); }
static std::string Read(const wstring& path) {
    HANDLE h=CreateFileW(path.c_str(),GENERIC_READ,FILE_SHARE_READ,NULL,OPEN_EXISTING,0,NULL);
    Require(h!=INVALID_HANDLE_VALUE,"fixture read failed");
    char bytes[256]={}; DWORD size=0; BOOL ok=ReadFile(h,bytes,sizeof(bytes),&size,NULL); CloseHandle(h);
    Require(ok!=FALSE,"fixture read incomplete"); return std::string(bytes,size);
}
static void Seed(const wstring& directory) {
    MakeDirectory(directory); Put(Join(directory,kMarker),kMarkerText);
    Put(Join(directory,kAppName),"fixture-app"); Put(Join(directory,kUninstaller),"fixture-uninstaller");
    Put(Join(directory,L"user-data.txt"),"preserved-user-data");
}
static void FixtureShortcut(const wstring& filename,const wstring& directory,const wchar_t* arguments,const wchar_t* description) {
    IShellLinkW* link=NULL; IPersistFile* file=NULL;
    HRESULT hr=CoCreateInstance(CLSID_ShellLink,NULL,CLSCTX_INPROC_SERVER,IID_IShellLinkW,reinterpret_cast<void**>(&link));
    if(SUCCEEDED(hr)) hr=link->SetPath(Join(directory,kAppName).c_str());
    if(SUCCEEDED(hr)) hr=link->SetWorkingDirectory(directory.c_str());
    if(SUCCEEDED(hr)) hr=link->SetArguments(arguments);
    if(SUCCEEDED(hr)) hr=link->SetDescription(description);
    if(SUCCEEDED(hr)) hr=link->SetIconLocation(Join(directory,kAppName).c_str(),0);
    if(SUCCEEDED(hr)) hr=link->QueryInterface(IID_IPersistFile,reinterpret_cast<void**>(&file));
    if(SUCCEEDED(hr)) hr=file->Save(filename.c_str(),TRUE);
    if(file) file->Release(); if(link) link->Release(); Require(SUCCEEDED(hr),"fixture shortcut failed");
}
static void Junction(const wstring& source,const wstring& target) {
    MakeDirectory(source);
    HANDLE h=CreateFileW(source.c_str(),GENERIC_WRITE,0,NULL,OPEN_EXISTING,FILE_FLAG_BACKUP_SEMANTICS|FILE_FLAG_OPEN_REPARSE_POINT,NULL);
    Require(h!=INVALID_HANDLE_VALUE,"junction fixture open failed");
    wstring substitute=L"\\??\\"+target;
    struct JunctionData { DWORD tag; WORD dataLength,reserved; WORD subOffset,subLength,printOffset,printLength; wchar_t paths[2*MAX_PATH+16]; } data={};
    data.tag=IO_REPARSE_TAG_MOUNT_POINT;
    data.subLength=static_cast<WORD>(substitute.size()*sizeof(wchar_t));
    data.printOffset=static_cast<WORD>(data.subLength+sizeof(wchar_t));
    data.printLength=static_cast<WORD>(target.size()*sizeof(wchar_t));
    memcpy(data.paths,substitute.c_str(),data.subLength+sizeof(wchar_t));
    memcpy(reinterpret_cast<BYTE*>(data.paths)+data.printOffset,target.c_str(),data.printLength+sizeof(wchar_t));
    data.dataLength=static_cast<WORD>(8+data.printOffset+data.printLength+sizeof(wchar_t));
    DWORD returned=0; BOOL ok=DeviceIoControl(h,FSCTL_SET_REPARSE_POINT,&data,data.dataLength+8,NULL,0,&returned,NULL); CloseHandle(h);
    Require(ok!=FALSE,"junction fixture creation failed");
}
int wmain(int argc,wchar_t** argv) {
    HRESULT com=CoInitializeEx(NULL,COINIT_APARTMENTTHREADED);
    try {
        Require(argc==2,"pass a new workspace-only fixture directory");
        wstring workspace=ParentDirectory(ParentDirectory(ParentDirectory(ModulePath())));
        const wstring root=ValidateInstallDirectory(argv[1]);
        Require(WithinPath(root,workspace) && !EqualPath(root,workspace),"fixtures must remain inside the executable workspace");
        Require(!Exists(root),"fixture directory must be new"); MakeDirectory(root);
        const wchar_t* invalid[]={L"relative",L"C:relative",L"\\rooted",L"\\\\server\\share",L"\\\\?\\C:\\folder",L"C:\\a:stream",L"C:\\bad.\\x",L"C:\\bad \\x",L"C:\\NUL.txt",L"C:\\COM1",L"C:\\a?b"};
        for(const wchar_t* input:invalid) Reject([&] { FullPath(input); },"invalid raw path accepted");
        Reject([&] { ValidateInstallDirectory(root.substr(0,3)); },"disk root accepted");
        wchar_t windows[MAX_PATH]={},system[MAX_PATH]={}; GetWindowsDirectoryW(windows,MAX_PATH); GetSystemDirectoryW(system,MAX_PATH);
        Reject([&] { ValidateInstallDirectory(Join(windows,L"FreeIslandFixture")); },"Windows descendant accepted");
        Reject([&] { ValidateInstallDirectory(system); },"system directory accepted");
        Reject([&] { ValidateInstallDirectory(SpecialFolder(CSIDL_PROFILE)); },"profile root accepted");
        std::cout<<"PASS: relative/network/device/reserved paths and protected roots rejected\n";

        wstring custom=Join(root,L"自定义 目录\\应用"), fallback=Join(root,L"fallback");
        Require(EqualPath(ValidateInstallDirectory(Join(root,L"intermediate\\..\\自定义 目录\\应用")),custom),"nested canonical path mismatch");
        wstring slash=custom; std::replace(slash.begin(),slash.end(),L'\\',L'/');
        Require(EqualPath(ValidateInstallDirectory(slash),custom),"forward slash path mismatch");
        CheckOwnedInstallAt(custom,true); Require(!Exists(custom),"validation created destination");
        Require(EqualPath(RememberedInstallDirectory(custom,fallback),custom),"custom registration not remembered");
        Require(EqualPath(RememberedInstallDirectory(L"invalid",fallback),fallback),"invalid registration did not fall back");
        Require(EqualPath(RememberedInstallDirectory(L"",fallback),fallback),"first install fallback wrong");
        std::cout<<"PASS: Unicode/spaces/new subdirectories and remembered installation path\n";

        wstring foreign=Join(root,L"foreign"); MakeDirectory(foreign); Put(Join(foreign,L"user.txt"),"foreign-data");
        Reject([&] { CheckOwnedInstallAt(foreign,true); },"nonempty foreign directory accepted");
        Require(Read(Join(foreign,L"user.txt"))=="foreign-data","foreign file changed");
        wstring empty=Join(root,L"empty"); MakeDirectory(empty); CheckOwnedInstallAt(empty,true);
        Reject([&] { CheckOwnedInstallAt(empty,false); },"unmarked empty directory allowed for removal");
        Seed(custom); CheckOwnedInstallAt(custom,false);
        wstring payloadDirectory=Join(root,L"payload-directory"); MakeDirectory(payloadDirectory); Put(Join(payloadDirectory,kMarker),kMarkerText); MakeDirectory(Join(payloadDirectory,kAppName));
        Reject([&] { CheckOwnedInstallAt(payloadDirectory,false); },"directory in payload slot accepted");
        wstring hard=Join(root,L"hardlink"); MakeDirectory(hard); Put(Join(hard,kMarker),kMarkerText); Put(Join(hard,L"original"),"linked");
        Require(CreateHardLinkW(Join(hard,kAppName).c_str(),Join(hard,L"original").c_str(),NULL)!=FALSE,"hardlink fixture failed");
        Reject([&] { CheckOwnedInstallAt(hard,false); },"hardlinked payload accepted");
        wstring junction=Join(root,L"junction"); Junction(junction,custom);
        Reject([&] { ValidateInstallDirectory(junction); },"junction accepted");
        Reject([&] { ValidateInstallDirectory(Join(junction,L"new-child")); },"junction ancestor accepted");
        std::cout<<"PASS: foreign files preserved; marker/normal file/hardlink/junction validation\n";

        wstring trusted=TrustedRegisteredDirectory(custom,Quote(Join(custom,kUninstaller))+L" --uninstall");
        Require(EqualPath(trusted,custom),"owned registered installation not recognized");
        Require(TrustedRegisteredDirectory(custom,L"foreign-command").empty(),"foreign uninstall command trusted");
        Require(TrustedRegisteredDirectory(foreign,Quote(Join(foreign,kUninstaller))+L" --uninstall").empty(),"missing marker trusted");
        gInstallDir=Join(root,L"new-install"); Seed(gInstallDir); gRegisteredDir=trusted;
        Require(OwnedRun(Quote(Join(custom,kAppName))+L" --silent",true),"old owned startup not migratable");
        Require(!OwnedRun(Quote(Join(custom,kAppName))+L" --silent"),"old startup considered owned by new uninstall");
        Require(!OwnedRun(Quote(Join(custom,kAppName))+L" --custom",true),"custom startup accepted");
        Require(OwnedRun(Quote(Join(gInstallDir,kAppName))+L" --silent"),"new startup target mismatch");
        std::cout<<"PASS: only marker-verified registered installation can migrate startup\n";

        Require(SUCCEEDED(com),"COM initialization failed");
        wstring link=Join(root,L"owned-old.lnk"),customLink=Join(root,L"custom.lnk"),argsLink=Join(root,L"custom-args.lnk"),newLink=Join(root,L"owned-new.lnk");
        FixtureShortcut(link,custom,L"",L"浮岛 Win7：课堂与桌面的时间助手");
        FixtureShortcut(customLink,custom,L"",L"My custom shortcut");
        FixtureShortcut(argsLink,custom,L"--custom",L"浮岛 Win7：课堂与桌面的时间助手");
        FixtureShortcut(newLink,gInstallDir,L"",L"浮岛 Win7：课堂与桌面的时间助手");
        Require(ShortcutOwned(link,true) && !ShortcutOwned(link),"old owned shortcut migration/removal ownership wrong");
        Require(!ShortcutOwned(customLink,true) && !ShortcutOwned(argsLink,true),"custom same-target shortcut accepted");
        Require(ShortcutOwned(newLink),"new owned shortcut not recognized");
        std::cout<<"PASS: old owned shortcuts migrate; custom shortcuts and new-install links protected\n";

        Require(EqualPath(RemovalDirectory(custom,Join(custom,kUninstaller)),custom),"custom uninstall directory wrong");
        Require(EqualPath(ParentDirectory(Join(custom,kUninstaller)),custom),"uninstall image directory not used");
        Reject([&] { RemovalDirectory(custom,Join(gInstallDir,kUninstaller)); },"mismatched parent accepted");
        Reject([&] { RemovalDirectory(foreign,Join(foreign,kUninstaller)); },"unmarked removal accepted");
        Require(Read(Join(custom,kAppName))=="fixture-app" && Read(Join(custom,L"user-data.txt"))=="preserved-user-data","old installation or data changed");
        Require(Read(Join(gInstallDir,L"user-data.txt"))=="preserved-user-data","new installation data changed");
        std::cout<<"PASS: uninstall bound to verified original image plus marker; old/new files preserved\n";
        if(SUCCEEDED(com)) CoUninitialize(); return 0;
    } catch(const std::exception& error) { std::cerr<<"FAIL: "<<error.what()<<"\n"; }
      catch(const wstring& error) { std::wcerr<<L"FAIL: "<<error<<L"\n"; }
    if(SUCCEEDED(com)) CoUninitialize(); return 1;
}
