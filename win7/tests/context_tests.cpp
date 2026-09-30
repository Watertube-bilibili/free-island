#include "../src/context_actions.h"
#include "../src/core.h"
#include <iostream>
#include <stdexcept>

static int passed=0;
static void Check(bool value,const char* message){if(!value)throw std::runtime_error(message);++passed;}
int main(){try{
 using namespace fiContext;
 Check(!fi::Settings().contextShortcuts,"Rules must require opt in");
 Check(Classify(L"VLC.EXE")==Category::Media,"Case insensitive exact process name");
 Check(Classify(L"C:\\Program Files\\PotPlayer\\PotPlayerMini64.exe")==Category::Media,"Path basename only");
 Check(Classify(L"powerpnt.exe")==Category::Presentation,"Presentation category");
 Check(Classify(L"code.exe")==Category::Writing,"Writing category");
 Check(Classify(L"chrome.exe")==Category::None,"Browser is ambiguous without reading content");
 Check(Classify(L"vlc.exe.malware.exe")==Category::None,"No process substring matching");
 Suggestions rules;
 Check(!rules.Observe(L"vlc.exe",100,false)&&rules.Current()==Category::None,"Disabled does not observe");
 Check(!rules.Observe(L"vlc.exe",100,true),"First sample not enough");
 Check(!rules.Observe(L"vlc.exe",15099,true),"No suggestion before fifteen second dwell");
 Check(rules.Observe(L"vlc.exe",15100,true)&&rules.Current()==Category::Media,"Stable player produces suggestion at fifteen seconds");
 Check(!rules.Observe(L"",16000,true)&&rules.Current()==Category::Media,"Own UI does not discard suggestion");
 Check(rules.Observe(L"unknown.exe",17000,true)&&rules.Current()==Category::None,"Other app clears stale card");
 rules.Observe(L"notepad.exe",20000,true);
 Check(!rules.Observe(L"notepad.exe",615099,true),"Global cooldown blocks suggestions before ten minutes");
 Check(rules.Observe(L"notepad.exe",615100,true)&&rules.Current()==Category::Writing,"Global cooldown completes at ten minutes");
 rules.Dismiss(616000);
 Check(!rules.Observe(L"notepad.exe",2500000,true)&&rules.Current()==Category::None,"Dismissed app remains quiet until context changes");
 Suggestions repeated;
 repeated.Observe(L"vlc.exe",100,true);repeated.Observe(L"vlc.exe",15100,true);
 repeated.Observe(L"unknown.exe",17000,true);repeated.Observe(L"VLC.EXE",20000,true);
 Check(!repeated.Observe(L"vlc.exe",615100,true),"Switching away and back does not bypass application cooldown");
 Check(!repeated.Observe(L"vlc.exe",1815099,true),"Same application stays quiet for thirty minutes");
 Check(repeated.Observe(L"vlc.exe",1815100,true),"Application becomes eligible at thirty minutes");
 Check(repeated.Observe(L"",1840100,true)&&repeated.Current()==Category::None,"Idle own app still expires stale suggestion");
 Check(!repeated.Observe(L"vlc.exe",4000000,true),"Expired same app does not spam");
 repeated.Observe(L"powerpnt.exe",4010000,true);repeated.Observe(L"powerpnt.exe",4025000,true);
 Check(repeated.Observe(L"",4026000,false)&&repeated.Current()==Category::None,"Disabling clears live suggestion");
 Suggestions fullscreen;
 Check(!fullscreen.Observe(L"vlc.exe",100,true,true),"Fullscreen does not begin dwell");
 Check(!fullscreen.Observe(L"vlc.exe",600000,true,true)&&fullscreen.Current()==Category::None,"Fullscreen stays quiet indefinitely");
 Check(!fullscreen.Observe(L"vlc.exe",600001,true),"Leaving fullscreen starts a fresh dwell");
 Check(!fullscreen.Observe(L"vlc.exe",615000,true),"Leaving fullscreen needs full dwell interval");
 Check(fullscreen.Observe(L"vlc.exe",615001,true),"Windowed app can suggest after full dwell");
 Check(fullscreen.Observe(L"vlc.exe",616000,true,true)&&fullscreen.Current()==Category::None,"Entering fullscreen clears a stale suggestion");
 Suggestions interrupted;
 interrupted.Observe(L"vlc.exe",100,true);interrupted.Observe(L"",14000,true);
 Check(!interrupted.Observe(L"vlc.exe",15100,true),"Unavailable foreground resets dwell");
 Check(interrupted.Observe(L"vlc.exe",30100,true),"Returned foreground needs fifteen stable seconds");
 RECT monitor={-1920,0,0,1080},exact=monitor,tolerated={-1922,-2,2,1082},windowed={-1900,0,0,1040};
 Check(CoversMonitor(exact,monitor)&&CoversMonitor(tolerated,monitor),"Fullscreen supports negative monitor coordinates and two-pixel tolerance");
 Check(!CoversMonitor(windowed,monitor),"Window within work area is not fullscreen");
 int volume=-1;Check(ReadVolume(volume,true)&&volume==50,"Safe injected volume read");
 Check(SetVolume(100,true)&&SetVolume(0,true),"Safe volume endpoints");
 Check(!SetVolume(-1,true)&&!SetVolume(101,true),"Out of bounds volume blocked");
 Check(ToggleMedia(L"vlc.exe",true),"Safe media click accepted");
 Check(!ToggleMedia(L"cmd.exe",true),"Unsupported media target blocked");
 std::cout<<"PASS: "<<passed<<" native context checks. No real audio, keys, foreground scan or system changes.\n";return 0;
 }catch(const std::exception&e){std::cerr<<"FAIL: "<<e.what()<<'\n';return 1;}}
