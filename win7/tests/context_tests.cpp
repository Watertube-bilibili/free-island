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
 Check(!rules.Observe(L"vlc.exe",3099,true),"Dwell boundary");
 Check(rules.Observe(L"vlc.exe",3100,true)&&rules.Current()==Category::Media,"Stable player produces suggestion");
 Check(!rules.Observe(L"",4000,true)&&rules.Current()==Category::Media,"Own UI does not discard suggestion");
 Check(rules.Observe(L"unknown.exe",5000,true)&&rules.Current()==Category::None,"Other app clears stale card");
 rules.Observe(L"notepad.exe",6000,true);
 Check(!rules.Observe(L"notepad.exe",9000,true),"Global cooldown blocks repeated interruptions");
 Check(rules.Observe(L"notepad.exe",63100,true)&&rules.Current()==Category::Writing,"Cooldown completes");
 rules.Dismiss(64000);
 Check(!rules.Observe(L"notepad.exe",124000,true)&&rules.Current()==Category::None,"Dismiss remains until context changes");
 rules.Observe(L"powerpnt.exe",125000,true);
 Check(rules.Observe(L"powerpnt.exe",128000,true),"Another context can produce new suggestion");
 Check(rules.Observe(L"",153000,true)&&rules.Current()==Category::None,"Idle own app still expires stale suggestion");
 Check(!rules.Observe(L"powerpnt.exe",213000,true),"Expired same app does not spam");
 rules.Observe(L"vlc.exe",214000,true);rules.Observe(L"vlc.exe",217000,true);
 Check(rules.Observe(L"",218000,false)&&rules.Current()==Category::None,"Disabling clears live suggestion");
 int volume=-1;Check(ReadVolume(volume,true)&&volume==50,"Safe injected volume read");
 Check(SetVolume(100,true)&&SetVolume(0,true),"Safe volume endpoints");
 Check(!SetVolume(-1,true)&&!SetVolume(101,true),"Out of bounds volume blocked");
 Check(ToggleMedia(L"vlc.exe",true),"Safe media click accepted");
 Check(!ToggleMedia(L"cmd.exe",true),"Unsupported media target blocked");
 std::cout<<"PASS: "<<passed<<" native context checks. No real audio, keys, foreground scan or system changes.\n";return 0;
 }catch(const std::exception&e){std::cerr<<"FAIL: "<<e.what()<<'\n';return 1;}}
