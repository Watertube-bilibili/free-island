#define UNICODE
#define _UNICODE
#define NOMINMAX
#define _WIN32_WINNT 0x0601
#include <windows.h>
#include <windowsx.h>
#include <commctrl.h>
#include <shellapi.h>
#include <shlobj.h>
#include <gdiplus.h>
#include <algorithm>
#include <cmath>
#include <memory>
#include <sstream>
#include <fstream>
#include <iomanip>
#include <functional>
#include <map>
#include <stdexcept>
#include "core.h"
#include "liquid_glass.h"
using namespace Gdiplus;
using namespace fi;

namespace {
const Color PAPER(255,246,247,251), WHITE(255,255,255,255), INK(255,24,36,58), MUTED(255,88,101,122), BLUE(255,79,102,232), LINE(255,220,226,235), SELECTED(255,238,241,255);
HINSTANCE instance;
float dpi=1,mainDpi=1;
bool snapshot=false;
std::wstring ExePath(){wchar_t p[32768];GetModuleFileNameW(NULL,p,32768);return p;}
std::wstring ExeDir(){std::wstring p=ExePath();return p.substr(0,p.find_last_of(L"\\/"));}
std::wstring Folder(int csidl){wchar_t p[MAX_PATH];if(FAILED(SHGetFolderPathW(NULL,csidl,NULL,SHGFP_TYPE_CURRENT,p)))throw std::runtime_error("Folder unavailable");return p;}
int D(float n){return (int)std::lround(n*dpi);} int U(float n){return (int)std::lround(n*mainDpi);}
RECT Rect(int x,int y,int w,int h){RECT r={x,y,x+w,y+h};return r;}
int W(const RECT&r){return r.right-r.left;} int H(const RECT&r){return r.bottom-r.top;}
bool Contains(const RECT&r,POINT p){return PtInRect(&r,p)!=FALSE;}
std::wstring Number(int n){return std::to_wstring(n);}
std::wstring Format(int64_t ms,bool hours=false){ms=std::max<int64_t>(0,ms);long long s=ms/1000;wchar_t b[64];if(hours||s>=3600)swprintf(b,64,L"%02lld:%02lld:%02lld",s/3600,s/60%60,s%60);else swprintf(b,64,L"%02lld:%02lld",s/60,s%60);return b;}
std::wstring DateText(int64_t t){SYSTEMTIME s=LocalTime(t);wchar_t b[64];swprintf(b,64,L"%02d月%02d日  %02d:%02d",s.wMonth,s.wDay,s.wHour,s.wMinute);return b;}
void Rounded(GraphicsPath&p,float x,float y,float w,float h,float r){r=std::min(r,std::min(w,h)/2);p.AddArc(x,y,2*r,2*r,180,90);p.AddArc(x+w-2*r,y,2*r,2*r,270,90);p.AddArc(x+w-2*r,y+h-2*r,2*r,2*r,0,90);p.AddArc(x,y+h-2*r,2*r,2*r,90,90);p.CloseFigure();}
void Box(Graphics&g,float x,float y,float w,float h,Color fill,float r=12,bool stroke=false){if(w<=0||h<=0)return;GraphicsPath p;Rounded(p,x,y,w,h,r);SolidBrush b(fill);g.FillPath(&b,&p);if(stroke){Pen pen(LINE,1);g.DrawPath(&pen,&p);}}
void Text(Graphics&g,const std::wstring&t,float x,float y,float w,float h,float size,Color c=INK,bool bold=false,int align=0,bool wrap=false){FontFamily f(L"Microsoft YaHei");Font font(&f,size,bold?FontStyleBold:FontStyleRegular,UnitPixel);SolidBrush b(c);StringFormat sf;sf.SetLineAlignment(StringAlignmentCenter);sf.SetAlignment(align==1?StringAlignmentCenter:align==2?StringAlignmentFar:StringAlignmentNear);sf.SetTrimming(StringTrimmingEllipsisCharacter);if(!wrap)sf.SetFormatFlags(StringFormatFlagsNoWrap);g.DrawString(t.c_str(),-1,&font,RectF(x,y,w,h),&sf,&b);}
void Digits(Graphics&g,const std::wstring&t,float x,float y,float w,float h,float size,Color c=INK){FontFamily f(L"Segoe UI");Font font(&f,size,FontStyleBold,UnitPixel);StringFormat sf;sf.SetAlignment(StringAlignmentCenter);sf.SetLineAlignment(StringAlignmentCenter);sf.SetFormatFlags(StringFormatFlagsNoWrap);SolidBrush b(c);g.DrawString(t.c_str(),-1,&font,RectF(x,y,w,h),&sf,&b);}
void Brand(Graphics&g,float x,float y,float size){SolidBrush b(BLUE);g.FillEllipse(&b,x,y,size,size);Box(g,x+size*5/24,y+size*12.5f/24,size*14/24,size*5/24,WHITE,size*2.5f/24);Box(g,x+size*10/24,y+size*6.5f/24,size*7/24,size*3/24,Color(255,185,205,255),size*1.5f/24);}
void GlassText(Graphics&g,const std::wstring&t,float x,float y,float w,float h,float size,int align=0){FontFamily family(L"Microsoft YaHei");Font font(&family,size,FontStyleRegular,UnitPixel);StringFormat format;format.SetFormatFlags(StringFormatFlagsNoWrap);RectF measured;g.MeasureString(t.c_str(),-1,&font,PointF(0,0),&format,&measured);float width=std::min(w,measured.Width),height=std::min(h,size*1.5f),left=align==1?x+(w-width)/2:x;Box(g,left-2,y+(h-height)/2,width+4,height,Color(168,249,251,254),4);Text(g,t,x,y,w,h,size,INK,false,align);}
void Icon(Graphics&g,int kind,float x,float y,float size,Color c=BLUE,float stroke=1.8f){GraphicsState st=g.Save();g.TranslateTransform(x,y);g.ScaleTransform(size/24,size/24);Pen p(c,stroke);p.SetStartCap(LineCapRound);p.SetEndCap(LineCapRound);p.SetLineJoin(LineJoinRound);auto line=[&](float a,float b,float d,float e){g.DrawLine(&p,a,b,d,e);};
 switch(kind){
 case 0:line(3,10,12,3);line(12,3,21,10);line(5,9,5,21);line(5,21,10,21);line(10,21,10,14);line(10,14,14,14);line(14,14,14,21);line(14,21,19,21);line(19,21,19,9);break;
 case 1:g.DrawEllipse(&p,4.5f,6.0f,15.0f,15.0f);line(9,2,15,2);line(12,2,12,6);line(12,10,12,14);line(12,14,15,16);line(18,6,20,4);break;
 case 2:case 12:g.DrawArc(&p,3.5f,3.5f,17.0f,17.0f,220.0f,315.0f);line(4,3,4,8);line(4,8,9,8);if(kind==2){line(12,7,12,12);line(12,12,16,15);}break;
 case 3:g.DrawRectangle(&p,4.0f,5.0f,16.0f,15.0f);line(4,9,20,9);line(8,2,8,6);line(16,2,16,6);line(8,13,10,13);line(14,13,16,13);line(8,17,10,17);break;
 case 4:g.DrawArc(&p,3.5f,3.5f,17.0f,17.0f,-50.0f,280.0f);line(12,2,12,12);break;
 case 5:{PointF pts[32];for(int n=0;n<32;n++){double a=-1.570796+n*3.141593/16;double r=(n%4==1||n%4==2)?9.5:7.2;pts[n]=PointF(12+float(cos(a)*r),12+float(sin(a)*r));}g.DrawPolygon(&p,pts,32);g.DrawEllipse(&p,9.0f,9.0f,6.0f,6.0f);break;}
 case 6:line(3,4,21,4);g.DrawRectangle(&p,5.0f,4.0f,14.0f,12.0f);line(12,16,12,21);line(8,21,12,18);line(12,18,16,21);line(8,8,16,8);line(8,12,13,12);break;
 case 7:g.DrawRectangle(&p,3.0f,4.0f,18.0f,13.0f);line(12,17,12,21);line(8,21,16,21);break;
 case 8:line(3,8,3,3);line(3,3,8,3);line(16,3,21,3);line(21,3,21,8);line(21,16,21,21);line(21,21,16,21);line(8,21,3,21);line(3,21,3,16);break;
 case 9:line(6,6,18,18);line(18,6,6,18);break;
 case 10:line(12,4,12,20);line(4,12,20,12);break;
 case 11:line(8,5,8,19);line(16,5,16,19);break;
 case 13:{PointF pts[]={PointF(8,4),PointF(19,12),PointF(8,20)};g.DrawPolygon(&p,pts,3);break;}
 case 14:g.DrawRectangle(&p,6.0f,6.0f,12.0f,12.0f);break;
 case 15:line(4,7,20,7);line(8,3,16,3);line(8,3,8,7);line(16,3,16,7);line(6,7,7,21);line(7,21,17,21);line(17,21,18,7);line(10,11,10,17);line(14,11,14,17);break;
 case 16:line(6,9,12,15);line(12,15,18,9);break;
 case 17:line(9,6,15,12);line(15,12,9,18);break;
 case 18:line(15,6,9,12);line(9,12,15,18);break;
 case 19:line(6,15,12,9);line(12,9,18,15);break;
 case 20:case 21:case 22:g.DrawRectangle(&p,4.0f,4.0f,16.0f,16.0f);if(kind==20)line(8,7,16,7);else if(kind==21)line(7,8,7,16);else line(17,8,17,16);break;
 }g.Restore(st);}
void GlassIcon(Graphics&g,int icon,float x,float y,float size){Icon(g,icon,x,y,size,Color(225,255,255,255),3.5f);Icon(g,icon,x,y,size,INK);}
void Quality(Graphics&g){g.SetSmoothingMode(SmoothingModeAntiAlias);g.SetTextRenderingHint(TextRenderingHintAntiAliasGridFit);g.SetPixelOffsetMode(PixelOffsetModeHighQuality);}
RECT WorkAt(POINT p,bool full=false){MONITORINFO m={sizeof(m)};GetMonitorInfoW(MonitorFromPoint(p,MONITOR_DEFAULTTONEAREST),&m);return full?m.rcMonitor:m.rcWork;}
struct MonitorLookup{const std::wstring*n;HMONITOR m;};
BOOL CALLBACK FindMonitor(HMONITOR m,HDC,LPRECT,LPARAM v){MonitorLookup*f=(MonitorLookup*)v;MONITORINFOEXW mi={};mi.cbSize=sizeof(mi);GetMonitorInfoW(m,&mi);if(*f->n==mi.szDevice)f->m=m;return TRUE;}
struct Overlay;
struct ButtonStyle{bool primary=false;int icon=-1;};
struct App {
 std::unique_ptr<Engine> engine;HWND main=NULL,stage=NULL;HANDLE exitEvent=NULL;bool safe=false,ending=false,smoke=false;int page=0,selectedMinutes=10,scroll=0,settingsTab=0;std::vector<HWND> children;std::map<HWND,ButtonStyle> styles;std::vector<int64_t> laps;std::unique_ptr<Overlay> ball,menu,island,handle;HFONT font=NULL;HBRUSH whiteBrush=NULL;NOTIFYICONDATAW tray={};std::wstring feedback;uint64_t feedbackUntil=0;bool priorCount=false,priorStop=false;int64_t priorShutdown=0;std::string activity;Notice lastNotice;uint64_t islandUntil=0;bool islandUrgent=false;float contentX=28,contentY=238,contentW=1000,contentH=500;float baseW=1280,baseH=800;bool classroom=true;std::wstring drafts[3];std::wstring mainFrame,stageFrame,islandFrame;uint64_t mainPaints=0,childPaints=0;
 App(bool s,bool test):safe(s),smoke(test){snapshot=test;std::wstring path=s?ExeDir()+L"\\test-data":Folder(CSIDL_APPDATA)+L"\\FreeIslandWin7";engine.reset(new Engine(path,s));classroom=engine->settings.scene==Scene::Classroom;whiteBrush=CreateSolidBrush(RGB(255,255,255));}
 ~App();void Initialize(bool silent);void Layout();void Paint(Graphics&g);void PaintStage(Graphics&g,int w,int h);void Command(int id);void Tick();void Navigate(int p);void OpenMain(int p=0);void OpenStage();void OpenMenu();void CloseMenu();void ShowIsland(const std::string&kind);void ShowNotice(Notice n);void CollapseIsland();void PositionIsland();void DockIsland(POINT p);void ApplyScene();void Notify(const std::wstring&t){feedback=t;feedbackUntil=GetTickCount64()+5500;InvalidateRect(main,NULL,FALSE);}void Save(){engine->Save();}void Close();void SetStartup(bool on);bool StartupEnabled();void PositionBall(bool restore=false);void TuckBall();void RevealBall();void SnapBall();void PopupTray();void DrawButton(DRAWITEMSTRUCT*ds);HWND Button(int id,const wchar_t*t,float x,float y,float w,float h,bool primary=false,int icon=-1);HWND Edit(int id,const std::wstring&t,float x,float y,float w,float h,bool numeric=false);HWND Date(int id,float x,float y,float w,float h,bool time=false);HWND Child(int id){return GetDlgItem(main,id);}int64_t InputTime(int dateId,int timeId);std::wstring Value(int id);void Capture(const std::wstring&name,HWND win);void RunSmoke();void CheckDotMaterials();void CheckPaintStability();void UpdateGlass(bool persist);void RefreshGlass();void CheckGlassControls();
};
App* app=NULL;
LRESULT CALLBACK MainProc(HWND,UINT,WPARAM,LPARAM);LRESULT CALLBACK OverlayProc(HWND,UINT,WPARAM,LPARAM);LRESULT CALLBACK StageProc(HWND,UINT,WPARAM,LPARAM);
struct Overlay{HWND hwnd=NULL;int kind=0,edge=0;bool tucked=false,down=false,dragged=false,closing=false;POINT start={},origin={};DWORD touchId=0;bool touching=false;uint64_t opened=0,lastUse=0;std::vector<std::pair<RECT,int>> hits;int width=0,height=0;float contentScale=0,opening=1;POINT dockFrom={},dockTo={};uint64_t dockStart=0,lastRender=0,lightAt=0,renderCount=0;float light=.35f;int pressedTarget=-2;POINT lastPointer={};float squeeze=0,stretch=0,stretchAngle=0,releaseSqueeze=0,releaseStretch=0;uint64_t released=0;bool materialDirty=false;
 Overlay(int k):kind(k){DWORD ex=WS_EX_LAYERED|WS_EX_TOOLWINDOW|WS_EX_TOPMOST|(k==1?0:WS_EX_NOACTIVATE);hwnd=CreateWindowExW(ex,L"FreeIslandWin7.Overlay",k==0?L"浮岛 Win7 · 悬浮球":k==1?L"浮岛 Win7 · 环形菜单":k==2?L"浮岛 Win7 · 灵动岛":L"浮岛 Win7 · 小黑点",WS_POPUP,0,0,100,100,NULL,NULL,instance,this);if(!hwnd)throw std::runtime_error("Cannot create overlay window");RegisterTouchWindow(hwnd,0);lastUse=GetTickCount64();}
 ~Overlay(){if(hwnd)DestroyWindow(hwnd);}void Size(int w,int h){width=w;height=h;SetWindowPos(hwnd,HWND_TOPMOST,0,0,w,h,SWP_NOMOVE|SWP_NOACTIVATE);}
 void At(int x,int y){SetWindowPos(hwnd,HWND_TOPMOST,x,y,0,0,SWP_NOSIZE|SWP_NOACTIVATE);}
 void ResetMaterial(){pressedTarget=-2;squeeze=stretch=0;released=0;materialDirty=false;}
 void Show(){closing=false;ResetMaterial();opened=GetTickCount64();opening=(snapshot||kind==3)?1:0;ShowWindow(hwnd,kind==1?SW_SHOWNORMAL:SW_SHOWNOACTIVATE);Render();}
 void Hide(){ShowWindow(hwnd,SW_HIDE);closing=false;ResetMaterial();}
 bool Visible(){return IsWindowVisible(hwnd)!=FALSE;}RECT Bounds(){RECT r;GetWindowRect(hwnd,&r);return r;}
 void End(){if(closing||!Visible())return;closing=true;if(snapshot){Hide();if(kind==2)app->PositionIsland();if(kind==1){app->ball->Show();app->ball->lastUse=GetTickCount64();}}else{closing=true;opened=GetTickCount64();}}
 void Render();void Draw(Graphics&g);void Material(Graphics&g,float x,float y,float w,float h,float radius,int target=-1,bool accent=false);void FinishPress();void Tick();void Press(POINT p);void Move(POINT p);void Release(POINT p);void PointerLight(POINT p);void AnimateDock(POINT from,POINT to){dockFrom=from;dockTo=to;dockStart=GetTickCount64();if(snapshot||kind==3){At(to.x,to.y);dockStart=0;}}
};

HWND App::Button(int id,const wchar_t*t,float x,float y,float w,float h,bool primary,int icon){HWND b=CreateWindowW(L"BUTTON",t,WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_OWNERDRAW,U(x),U(y),U(w),U(h),main,(HMENU)(INT_PTR)id,instance,NULL);children.push_back(b);ButtonStyle st;st.primary=primary;st.icon=icon;styles[b]=st;SendMessageW(b,WM_SETFONT,(WPARAM)font,TRUE);return b;}
HWND App::Edit(int id,const std::wstring&t,float x,float y,float w,float h,bool numeric){HWND b=CreateWindowExW(WS_EX_CLIENTEDGE,L"EDIT",t.c_str(),WS_CHILD|WS_VISIBLE|WS_TABSTOP|ES_AUTOHSCROLL|(numeric?ES_NUMBER:0),U(x),U(y),U(w),U(h),main,(HMENU)(INT_PTR)id,instance,NULL);SendMessageW(b,EM_SETLIMITTEXT,id==1001&&page==3?100:12,0);SendMessageW(b,WM_SETFONT,(WPARAM)font,TRUE);SendMessageW(b,EM_SETMARGINS,EC_LEFTMARGIN|EC_RIGHTMARGIN,MAKELPARAM(U(10),U(8)));children.push_back(b);return b;}
HWND App::Date(int id,float x,float y,float w,float h,bool time){HWND b=CreateWindowExW(0,DATETIMEPICK_CLASSW,L"",WS_CHILD|WS_VISIBLE|WS_TABSTOP|(time?DTS_TIMEFORMAT|DTS_UPDOWN:DTS_SHORTDATEFORMAT),U(x),U(y),U(w),U(h),main,(HMENU)(INT_PTR)id,instance,NULL);SYSTEMTIME s=LocalTime(NowMs()+3600000);DateTime_SetSystemtime(b,GDT_VALID,&s);SendMessageW(b,WM_SETFONT,(WPARAM)font,TRUE);SendMessageW(b,DTM_SETFORMATW,0,(LPARAM)(time?L"HH':'mm":L"yyyy'-'MM'-'dd"));children.push_back(b);return b;}
std::wstring App::Value(int id){HWND h=Child(id);int n=GetWindowTextLengthW(h);std::wstring s(n+1,L'\0');GetWindowTextW(h,&s[0],n+1);s.resize(n);return s;}
void SyncDotControls(bool fromSlider){if(!app||app->page!=5||!app->Child(1005))return;int percent=-1;try{if(fromSlider){percent=(int)SendMessageW(app->Child(1007),TBM_GETPOS,0,0);std::wstring value=Number(percent);if(app->Value(1005)!=value)SetWindowTextW(app->Child(1005),value.c_str());}else{std::wstring value=app->Value(1005);if(!value.empty()&&value.find_first_not_of(L"0123456789")==std::wstring::npos)percent=std::stoi(value);}}catch(...){}if(percent>=0&&percent<=100){if(app->Child(1007)&&(int)SendMessageW(app->Child(1007),TBM_GETPOS,0,0)!=percent)SendMessageW(app->Child(1007),TBM_SETPOS,TRUE,percent);int pixels=3+(int)std::lround(17*percent/100.0);SetWindowTextW(app->Child(1008),(Number(percent)+L"% · "+Number(pixels)+L" px").c_str());}else SetWindowTextW(app->Child(1008),L"0–100%");}
void App::RefreshGlass(){
 for(Overlay*overlay:{ball.get(),menu.get(),island.get(),handle.get()})if(overlay){overlay->ResetMaterial();if(overlay->Visible())overlay->Render();}
}
void App::UpdateGlass(bool persist){
 if(page!=5||settingsTab!=1)return;
 int*values[]={&engine->settings.glassRefraction,&engine->settings.glassTransparency,&engine->settings.glassHighlight};bool changed=false;
 for(int i=0;i<3;i++){HWND slider=Child(1010+i);if(!slider)continue;int value=(int)SendMessageW(slider,TBM_GETPOS,0,0);value=std::max(0,std::min(100,value));changed|=*values[i]!=value;*values[i]=value;std::wstring label=Number(value)+L"%";if(Value(1013+i)!=label)SetWindowTextW(Child(1013+i),label.c_str());}
 if(changed)RefreshGlass();
 if(persist)Save();
}
int64_t App::InputTime(int d,int t){SYSTEMTIME date={},time={};if(DateTime_GetSystemtime(Child(d),&date)!=GDT_VALID||DateTime_GetSystemtime(Child(t),&time)!=GDT_VALID)throw std::runtime_error("Invalid date");date.wHour=time.wHour;date.wMinute=time.wMinute;date.wSecond=0;date.wMilliseconds=0;return LocalToMs(date);}
void App::Navigate(int p){page=p;scroll=0;drafts[0]=L"";drafts[1]=L"";drafts[2]=L"";Layout();InvalidateRect(main,NULL,FALSE);}
void App::Layout(){if(!main)return;RECT available;GetClientRect(main,&available);mainDpi=std::max(0.25f,std::min(dpi,std::min(W(available)/1000.0f,H(available)/(classroom?800.0f:700.0f))));for(HWND h:children)DestroyWindow(h);children.clear();styles.clear();if(font)DeleteObject(font);font=CreateFontW(-U(classroom?20:14),0,0,0,FW_NORMAL,FALSE,FALSE,FALSE,DEFAULT_CHARSET,OUT_DEFAULT_PRECIS,CLIP_DEFAULT_PRECIS,CLEARTYPE_QUALITY,DEFAULT_PITCH,L"Microsoft YaHei");RECT r;GetClientRect(main,&r);baseW=W(r)/mainDpi;baseH=H(r)/mainDpi;float bh=classroom?54:42;
 Button(10,L"教室触屏",146,16,132,46,classroom,6);Button(11,L"电脑桌面",286,16,132,46,!classroom,7);
 const wchar_t*names[]={L"工作台",L"正向计时",L"倒计时",L"日程提醒",L"定时关机",L"设置"};
 if(classroom){float nw=(baseW-48)/6;for(int i=0;i<6;i++)Button(100+i,names[i],24+nw*i,87,nw-8,54,page==i,i);contentX=28;contentY=236;contentW=baseW-56;}
 else{for(int i=0;i<6;i++)Button(100+i,names[i],12,102+i*58,146,46,page==i,i);contentX=194;contentY=176;contentW=baseW-222;}
 contentH=baseH-contentY-42;Button(40,L"大屏展示",baseW-188,classroom?164:102,160,bh,true,8);float x=contentX,y=contentY,w=contentW,h=contentH;
 if(page==0){float task=w>760?w-306:w;float top=y+std::min(230.0f,h-190);for(int i=0;i<4;i++)Button(200+i,(std::to_wstring((int[]){5,10,25,45}[i])+L" 分钟").c_str(),x+24+i*(task-48)/4,top,(task-64)/4,bh,selectedMinutes==(int[]){5,10,25,45}[i]);Button(210,engine->countdownActive?(engine->countdownRunning?L"暂停":L"继续"):L"开始倒计时",x+24,top+bh+14,task-48,bh,true);if(engine->countdownActive)Button(211,L"结束倒计时",x,top+2*bh+40,185,bh,false,14);else{Button(101,L"正向计时",x,top+2*bh+40,160,bh,false,1);Button(103,L"日程提醒",x+172,top+2*bh+40,160,bh,false,3);}if(w>760)Button(103,L"添加日程",x+task+34,y+178,248,bh,false,10);Button(102,L"自定义时长",x+task-172,y+12,146,44,false);}
 if(page==1){float actions=y+std::min(234.0f,h-180);Button(220,engine->stopwatchRunning?L"暂停计时":engine->StopwatchMs()>0?L"继续计时":L"开始计时",x+std::max(20.0f,(w-550)/2),actions,200,bh,true,engine->stopwatchRunning?11:13);Button(221,L"记录分段",x+std::max(20.0f,(w-550)/2)+214,actions,160,bh);Button(222,L"重置",x+std::max(20.0f,(w-550)/2)+388,actions,140,bh,false,12);}
 if(page==2){float timerH=std::min(196.0f,h-240);int preset[]={1,5,10,25,40,45};for(int i=0;i<6;i++)Button(230+i,(Number(preset[i])+L" 分钟").c_str(),x+i*w/6,y+timerH+38,w/6-8,bh);float iy=y+timerH+bh+88;Edit(1001,L"0",x,iy,92,bh,true);Edit(1002,L"25",x+106,iy,92,bh,true);Edit(1003,L"0",x+212,iy,92,bh,true);Button(240,L"开始倒计时",x+328,iy,190,bh,true,13);if(engine->countdownActive){Button(241,engine->countdownRunning?L"暂停":L"继续",x+w-310,y+timerH-64,140,bh,true,engine->countdownRunning?11:13);Button(242,L"结束",x+w-158,y+timerH-64,134,bh,false,14);}}
 if(page==3){HWND reminderInput=Edit(1001,L"",x+24,y+60,w-48,bh);SendMessageW(reminderInput,EM_SETCUEBANNER,TRUE,(LPARAM)L"例如：课间休息、准备下一节课");Date(1002,x+24,y+bh+118,194,bh);Date(1003,x+234,y+bh+118,124,bh,true);HWND ck=CreateWindowW(L"BUTTON",L"每天重复",WS_CHILD|WS_VISIBLE|WS_TABSTOP|BS_AUTOCHECKBOX,U(x+376),U(y+bh+118),U(160),U(bh),main,(HMENU)1004,instance,NULL);SendMessageW(ck,WM_SETFONT,(WPARAM)font,TRUE);children.push_back(ck);Button(250,L"添加提醒",x+w-212,y+bh+118,188,bh,true,10);int visible=std::max(1,(int)((h-(2*bh+162))/76));for(int i=0;i<visible&&i+scroll<(int)engine->reminders.size();i++){int idx=i+scroll;Button(10000+idx,L"删除",x+w-110,y+2*bh+184+i*76,100,48,false,15);}if((int)engine->reminders.size()>visible){Button(260,L"上一页",x+w-246,y+h-54,112,48);Button(261,L"下一页",x+w-124,y+h-54,112,48);}}
 if(page==4){int delay[]={30,60,120,180};for(int i=0;i<4;i++)Button(270+i,(Number(delay[i])+L" 分钟后").c_str(),x+i*174,y+144,164,bh);Date(1001,x,y+bh+196,200,bh);Date(1002,x+218,y+bh+196,130,bh,true);Button(280,L"确认预约关机",x+w-210,y+h-92,190,bh,true,4);if(engine->shutdownAt)Button(281,L"取消关机预约",x+w-220,y+34,196,bh,false,9);}
 if(page==5&&settingsTab==1){
  Button(331,L"返回设置",x,y,138,44,false);
  const wchar_t*materials[]={L"关闭",L"轻量",L"水滴动效"};for(int i=0;i<3;i++)Button(320+i,materials[i],x+i*w/3,y+58,w/3-8,44,engine->settings.glassMode==i);
  const wchar_t*labels[]={L"曲面厚度",L"透明度",L"高光强度"};int values[]={engine->settings.glassRefraction,engine->settings.glassTransparency,engine->settings.glassHighlight};
  for(int i=0;i<3;i++){float row=y+132+i*88;
   HWND slider=CreateWindowExW(0,TRACKBAR_CLASSW,labels[i],WS_CHILD|WS_VISIBLE|WS_TABSTOP|TBS_HORZ|TBS_NOTICKS|TBS_FIXEDLENGTH,U(x+20),U(row+30),U(w-40),U(44),main,(HMENU)(INT_PTR)(1010+i),instance,NULL);
   SendMessageW(slider,TBM_SETRANGE,TRUE,MAKELPARAM(0,100));SendMessageW(slider,TBM_SETLINESIZE,0,1);SendMessageW(slider,TBM_SETPAGESIZE,0,10);SendMessageW(slider,TBM_SETTHUMBLENGTH,U(32),0);SendMessageW(slider,TBM_SETPOS,TRUE,values[i]);children.push_back(slider);
   HWND reading=CreateWindowW(L"STATIC",(Number(values[i])+L"%").c_str(),WS_CHILD|WS_VISIBLE|SS_RIGHT|SS_CENTERIMAGE,U(x+w-104),U(row),U(80),U(28),main,(HMENU)(INT_PTR)(1013+i),instance,NULL);SendMessageW(reading,WM_SETFONT,(WPARAM)font,TRUE);children.push_back(reading);
  }
  Button(332,L"恢复推荐",x,y+h-54,180,44,false,12);
 }
 if(page==5&&settingsTab==0){
  Button(330,L"调整玻璃外观",x+w-196,y+2,176,44,false);
  const wchar_t*pos[]={L"顶部",L"左侧",L"右侧"};for(int i=0;i<3;i++)Button(290+i,pos[i],x+20+i*(w-40)/3,y+48,(w-56)/3,bh,(int)engine->settings.dock==i,20+i);
  float sy=y+bh+76,col=(w-16)/2,right=x+col+16;bool on[]={engine->settings.startup,engine->settings.sound,engine->settings.edgeHide};
  for(int i=0;i<3;i++)Button(300+i,on[i]?L"已开启":L"已关闭",x+col-136,sy+i*62+10,112,44,on[i]);
  const wchar_t*materials[]={L"关闭",L"轻量",L"水滴动效"};for(int i=0;i<3;i++)Button(320+i,materials[i],x+20+i*(col-40)/3,sy+238,(col-52)/3,44,engine->settings.glassMode==i);
  HWND slider=CreateWindowExW(0,TRACKBAR_CLASSW,L"收起点大小百分比",WS_CHILD|WS_VISIBLE|WS_TABSTOP|TBS_HORZ|TBS_NOTICKS,U(right+20),U(sy+40),U(col-40),U(44),main,(HMENU)1007,instance,NULL);
  SendMessageW(slider,TBM_SETRANGE,TRUE,MAKELPARAM(0,100));SendMessageW(slider,TBM_SETLINESIZE,0,1);SendMessageW(slider,TBM_SETPAGESIZE,0,10);SendMessageW(slider,TBM_SETTHUMBLENGTH,U(32),0);SendMessageW(slider,TBM_SETPOS,TRUE,engine->settings.islandDotPercent);children.push_back(slider);
  Button(310,L"−",right+20,sy+92,44,44);Edit(1005,Number(engine->settings.islandDotPercent),right+74,sy+92,72,44,true);Button(311,L"+",right+156,sy+92,44,44);
  HWND reading=CreateWindowW(L"STATIC",(Number(engine->settings.islandDotPercent)+L"% · "+Number(engine->settings.islandDotSize)+L" px").c_str(),WS_CHILD|WS_VISIBLE|SS_CENTERIMAGE,U(right+210),U(sy+92),U(col-226),U(44),main,(HMENU)1008,instance,NULL);SendMessageW(reading,WM_SETFONT,(WPARAM)font,TRUE);children.push_back(reading);
  Button(312,L"−",right+20,sy+184,44,44);Edit(1006,Number((int)std::lround(engine->settings.islandScale*100)),right+74,sy+184,80,44,true);Button(313,L"+",right+164,sy+184,44,44);
  Button(314,L"应用大小",right+20,sy+246,(col-52)/2,44,true);Button(315,L"恢复默认",right+32+(col-52)/2,sy+246,(col-52)/2,44);
  Button(303,L"找回悬浮球",x,y+h-54,196,bh,true,0);Button(304,L"预览灵动岛",x+210,y+h-54,196,bh,false,8);
 }
 InvalidateRect(main,NULL,FALSE);
}
void App::Paint(Graphics&g){Quality(g);g.ScaleTransform(mainDpi,mainDpi);g.Clear(PAPER);Box(g,0,0,baseW,78,WHITE,0.1f);Brand(g,26,20,36);Text(g,L"浮岛",74,13,70,48,25,INK,true);SYSTEMTIME now;GetLocalTime(&now);wchar_t clock[60];swprintf(clock,60,L"%d月%d日   %02d:%02d",now.wMonth,now.wDay,now.wHour,now.wMinute);Text(g,clock,baseW-244,17,216,40,16,MUTED,false,2);
 if(!classroom){Box(g,0,79,170,baseH-79,WHITE,0.1f);Text(g,L"关闭窗口后\n任务仍会继续",24,490,128,64,12,MUTED,false,0,true);}
 const wchar_t*title[]={classroom?L"课堂工作台":L"桌面工作台",L"正向计时",L"倒计时",L"日程提醒",L"定时关机",L"设置"};const wchar_t*caption[]={L"设置计时，或查看接下来的日程。",L"开始、暂停或记录分段；需要时切换大屏展示。",L"选择常用时长，或输入小时、分钟和秒。",L"设置一次提醒，或每天在同一时间重复。",L"明确预约时间；关机前会提醒，也可随时取消。",L"调整灵动岛的位置、大小和后台运行方式。"};Text(g,page==5&&settingsTab==1?L"玻璃外观":title[page],contentX,classroom?155:94,contentW-184,46,classroom?28:25,INK,true);Text(g,page==5&&settingsTab==1?L"调整曲面、透明度和高光，即时预览材质。":caption[page],contentX,classroom?200:141,contentW-184,28,classroom?16:13,MUTED);
 float x=contentX,y=contentY,w=contentW,h=contentH,bh=classroom?54:42,sz=classroom?20:14,small=classroom?16:12;
 if(page==0){float task=w>760?w-306:w;float top=y+std::min(230.0f,h-190);Box(g,x,y,task,top-y+2*bh+42,WHITE,12,true);Text(g,L"倒计时",x+24,y+16,220,40,24,INK,true);Digits(g,engine->countdownActive?Format(engine->CountdownMs()):Number(selectedMinutes)+L":00",x+24,y+66,task-48,top-y-114,classroom?108:80);Text(g,engine->countdownActive?(engine->countdownRunning?L"正在倒计时，到时自动提醒":L"计时已暂停"):L"选择时长后开始",x+24,top-50,task-48,30,small,MUTED,false,1);if(w>760){Text(g,L"接下来的日程",x+task+34,y,248,44,22,INK,true);const Reminder*next=NULL;for(const auto&r:engine->reminders)if(!r.completed&&(!next||r.dueMs<next->dueMs))next=&r;Icon(g,3,x+task+34,y+64,28,MUTED);Text(g,next?next->title:L"还没有日程提醒",x+task+34,y+106,248,32,sz);Text(g,next?DateText(next->dueMs):L"添加课程、会议或休息提醒",x+task+34,y+144,248,26,small,MUTED);}}
 else if(page==1){float actions=y+std::min(234.0f,h-180);Box(g,x,y,w,actions-y+bh+28,WHITE,12,true);Digits(g,Format(engine->StopwatchMs(),true),x+20,y+26,w-40,actions-y-84,classroom?104:76);Text(g,engine->stopwatchRunning?L"正在计时":engine->StopwatchMs()>0?L"已暂停，可随时继续":L"尚未开始",x+20,actions-52,w-40,28,small,MUTED,false,1);Text(g,L"分段记录",x,actions+bh+46,w,32,sz,INK,true);if(laps.empty())Text(g,L"点击「记录分段」保存当前累计时间。",x,actions+bh+86,w,30,small,MUTED);else for(int i=(int)laps.size()-1,n=0;i>=0&&n<3;i--,n++){Text(g,L"分段 "+Number(i+1),x,actions+bh+88+n*40,180,32,sz,MUTED);Text(g,Format(laps[i],true),x+w-220,actions+bh+88+n*40,220,32,sz,INK,true,2);}}
 else if(page==2){float th=std::min(196.0f,h-240);Box(g,x,y,w,th,WHITE,12,true);Digits(g,engine->countdownActive?Format(engine->CountdownMs()):L"25:00",x+20,y+10,w-40,th-60,classroom?102:76);Text(g,engine->countdownActive?(engine->countdownRunning?L"正在倒计时":L"已暂停"):L"选择时长后开始",x+24,y+th-48,w-348,32,small,MUTED);Text(g,L"常用时长",x,y+th+6,w,28,small,MUTED);float iy=y+th+bh+88;Text(g,L"小时",x,iy-32,90,26,small,MUTED);Text(g,L"分钟",x+106,iy-32,90,26,small,MUTED);Text(g,L"秒",x+212,iy-32,90,26,small,MUTED);}
 else if(page==3){Box(g,x,y,w,2*bh+146,WHITE,12,true);Text(g,L"提醒内容",x+24,y+18,w-48,30,sz,INK,true);Box(g,x+22,y+58,w-44,bh+4,WHITE,5,true);Text(g,L"日期",x+24,y+bh+82,190,28,small,MUTED);Text(g,L"时间",x+234,y+bh+82,128,28,small,MUTED);float start=y+2*bh+174;Text(g,L"提醒列表",x,start-18,w,32,sz,INK,true);if(engine->reminders.empty())Text(g,L"还没有日程。添加提醒后会在这里显示。",x,start+40,w,36,small,MUTED);int visible=std::max(1,(int)((h-(2*bh+162))/76));for(int i=0;i<visible&&i+scroll<(int)engine->reminders.size();i++){const auto&r=engine->reminders[i+scroll];Text(g,r.title,x,start+22+i*76,w-140,32,sz);Text(g,DateText(r.dueMs)+(r.daily?L" · 每天":r.completed?L" · 已提醒":L" · 一次"),x,start+56+i*76,w-140,26,small,MUTED);}}
 else if(page==4){Box(g,x,y,w,118,WHITE,12,true);Text(g,engine->shutdownAt?DateText(engine->shutdownAt)+L" 关机":L"尚未预约关机",x+24,y+20,w-270,42,26,INK,true);Text(g,engine->shutdownAt?L"距离关机还有 "+Format(engine->shutdownAt-NowMs(),true):L"选择下方时间，确认后预约才会生效。",x+24,y+68,w-48,30,small,MUTED);Text(g,L"日期",x,y+bh+160,180,28,small,MUTED);Text(g,L"时间",x+218,y+bh+160,130,28,small,MUTED);Box(g,x,y+h-120,w,110,Color(255,255,245,228),12);Text(g,L"预约前请保存正在进行的工作。",x+20,y+h-108,w-254,32,sz,Color(255,128,84,10),true);Text(g,safe?L"安全预览：只演示预约，不执行系统关机。":L"提前 60 秒提醒，可取消；不会强制关闭应用。",x+20,y+h-69,w-254,40,small,Color(255,128,84,10),false,0,true);}
 else if(page==5&&settingsTab==0){
  Box(g,x,y,w,bh+62,WHITE,12,true);Text(g,L"灵动岛出现位置",x+20,y+6,w-40,36,23,INK,true);
  float sy=y+bh+76,col=(w-16)/2,right=x+col+16,panelH=std::min(326.0f,h-2*bh-94);Box(g,x,sy,col,panelH,WHITE,12,true);Box(g,right,sy,col,panelH,WHITE,12,true);
  const wchar_t*a[]={L"开机自启动",L"提醒声音",L"悬浮球靠边隐藏"};const wchar_t*b[]={L"登录后静默运行。",L"计时或日程到时响铃。",L"靠边收起为小箭头。"};
  for(int i=0;i<3;i++){Text(g,a[i],x+20,sy+4+i*62,col-168,28,sz,INK,true);Text(g,b[i],x+20,sy+32+i*62,col-168,24,small,MUTED);}
  Text(g,L"清透材质",x+20,sy+196,col-40,32,sz,INK,true);Text(g,L"柔和形变 · Win7 不提供桌面折射",x+20,sy+287,col-40,25,small,MUTED);
  Box(g,right+72,sy+90,76,48,WHITE,5,true);Box(g,right+72,sy+182,84,48,WHITE,5,true);Text(g,L"收起点大小 · 0–100%",right+20,sy+6,col-40,30,sz,INK,true);
  Text(g,L"展开的灵动岛（比例）",right+20,sy+147,col-40,30,sz,INK,true);Text(g,L"75–150%",right+222,sy+184,col-242,44,small,MUTED);
 }
 else if(page==5){
  Text(g,L"即时预览，松手后保存",x+156,y,w-156,44,small,MUTED);
  Text(g,L"曲面厚度调整折射外观；Win7 不采样桌面",x,y+105,w,24,small,MUTED);
  Box(g,x,y+128,w,266,WHITE,12,true);
  const wchar_t*labels[]={L"曲面厚度",L"透明度",L"高光强度"};
  for(int i=0;i<3;i++)Text(g,labels[i],x+20,y+132+i*88,w-136,28,sz,INK,true);
  Text(g,L"仅调整材质，文字保持清晰",x+198,y+h-54,w-198,44,small,MUTED);
 }
 Text(g,feedback,contentX,baseH-36,contentW,28,small,BLUE);
}
void App::DrawButton(DRAWITEMSTRUCT*ds){++childPaints;auto found=styles.find(ds->hwndItem);if(found==styles.end())return;const ButtonStyle st=found->second;int width=W(ds->rcItem),height=H(ds->rcItem);Bitmap bmp(width,height,PixelFormat32bppPARGB);Graphics g(&bmp);Quality(g);g.Clear(PAPER);g.ScaleTransform(mainDpi,mainDpi);float w=width/mainDpi,h=height/mainDpi;bool down=(ds->itemState&ODS_SELECTED)!=0,disabled=(ds->itemState&ODS_DISABLED)!=0;Color fill=st.primary?(down?Color(255,52,72,186):BLUE):(down?Color(255,221,229,245):WHITE);if(disabled)fill=LINE;Box(g,1,1,w-2,h-2,fill,10,!st.primary);wchar_t b[180];GetWindowTextW(ds->hwndItem,b,180);float fs=classroom?20:14;if(w<140&&classroom)fs=17;Color ink=st.primary?WHITE:INK;if(disabled)ink=MUTED;int len=(int)wcslen(b);float estimated=len*fs;float group=estimated+(st.icon>=0?32:0),sx=std::max(10.0f,(w-group)/2);if(st.icon>=0){Icon(g,st.icon,sx,(h-22)/2,22,ink);Text(g,b,sx+32,0,w-sx-40,h,fs,ink);}else Text(g,b,8,0,w-16,h,fs,ink,false,1);if(ds->itemState&ODS_FOCUS){GraphicsPath p;Rounded(p,3,3,w-6,h-6,8);Pen focus(st.primary?WHITE:Color(255,21,45,158),2);g.DrawPath(&focus,&p);}Graphics out(ds->hDC);out.DrawImage(&bmp,0,0);}
void App::Command(int id){try{
 if(id>=100&&id<=105){Navigate(id-100);return;}if(id==10||id==11){engine->settings.scene=id==10?Scene::Classroom:Scene::Desktop;engine->settings.sceneSelected=true;Save();ApplyScene();return;}if(id==40){OpenStage();return;}
 if(id>=200&&id<=203){int m[]={5,10,25,45};selectedMinutes=m[id-200];Layout();return;}
 if(id==210){if(engine->countdownActive)engine->PauseCountdown();else engine->StartCountdown(selectedMinutes*60000LL);ShowIsland("countdown");Layout();}
 if(id==211||id==242){engine->CancelCountdown();Layout();}
 if(id==220){engine->ToggleStopwatch();ShowIsland("stopwatch");Layout();}
 if(id==221){if(engine->StopwatchMs()>0)laps.push_back(engine->StopwatchMs());InvalidateRect(main,NULL,FALSE);}
 if(id==222){engine->ResetStopwatch();laps.clear();Layout();}
 if(id>=230&&id<=235){int m[]={1,5,10,25,40,45};engine->StartCountdown(m[id-230]*60000LL);ShowIsland("countdown");Layout();}
 if(id==240){long long h=0,m=0,s=0;auto read=[&](int n){std::wstring v=Value(n);if(v.empty()||v.find_first_not_of(L"0123456789")!=std::wstring::npos)throw std::runtime_error("duration");return std::stoll(v);};h=read(1001);m=read(1002);s=read(1003);if(h>168||m>59||s>59||h*3600+m*60+s<=0||h*3600+m*60+s>604800)throw std::runtime_error("duration");engine->StartCountdown((h*3600+m*60+s)*1000);ShowIsland("countdown");Layout();}
 if(id==241){engine->PauseCountdown();Layout();}
 if(id==250){std::wstring title=Value(1001);if(title.empty()){Notify(L"请先填写提醒内容。");SetFocus(Child(1001));return;}int64_t due=InputTime(1002,1003);bool daily=SendMessageW(Child(1004),BM_GETCHECK,0,0)==BST_CHECKED;if(due<=NowMs()&&daily){SYSTEMTIME picked=LocalTime(due),next=LocalTime(NowMs());next.wHour=picked.wHour;next.wMinute=picked.wMinute;next.wSecond=0;next.wMilliseconds=0;due=LocalToMs(next);if(due<=NowMs()){FILETIME ft={};if(!SystemTimeToFileTime(&next,&ft))throw std::runtime_error("Invalid reminder date");ULARGE_INTEGER day;day.LowPart=ft.dwLowDateTime;day.HighPart=ft.dwHighDateTime;day.QuadPart+=864000000000ULL;ft.dwLowDateTime=day.LowPart;ft.dwHighDateTime=day.HighPart;FileTimeToSystemTime(&ft,&next);due=LocalToMs(next);}}if(due<=NowMs()&&!daily){Notify(L"提醒时间已经过去，请选择之后的时间。");return;}engine->AddReminder(title,due,daily);Layout();Notify(L"提醒已添加，到时会自动弹出。");}
 if(id>=10000){int n=id-10000;if(n<(int)engine->reminders.size())engine->RemoveReminder(engine->reminders[n].id);Layout();}
 if(id==260){scroll=std::max(0,scroll-3);Layout();}if(id==261){scroll=std::min(std::max(0,(int)engine->reminders.size()-1),scroll+3);Layout();}
 if(id>=270&&id<=273){int m[]={30,60,120,180};SYSTEMTIME s=LocalTime(NowMs()+m[id-270]*60000LL);DateTime_SetSystemtime(Child(1001),GDT_VALID,&s);DateTime_SetSystemtime(Child(1002),GDT_VALID,&s);}
 if(id==280){int64_t due=InputTime(1001,1002);if(due<NowMs()+60000){Notify(L"请预约至少 1 分钟之后的关机时间。");return;}engine->ScheduleShutdown(due);ShowIsland("shutdown");Layout();Notify(safe?L"已创建安全预览预约，不会执行关机。":L"关机已预约，可随时取消。");}
 if(id==281){engine->CancelShutdown();islandUrgent=false;Layout();Notify(L"关机预约已取消。");}
 if(id>=290&&id<=292){engine->settings.dock=(Dock)(id-290);Save();PositionIsland();ShowIsland("");Layout();}
 if(id==300){SetStartup(!engine->settings.startup);Save();Layout();}if(id==301){engine->settings.sound=!engine->settings.sound;Save();Layout();}if(id==302){engine->settings.edgeHide=!engine->settings.edgeHide;Save();RevealBall();SnapBall();Layout();}if(id==303)PositionBall(true);if(id==304)ShowIsland("");
 if(id>=310&&id<=313){bool dot=id<=311;int input=dot?1005:1006;int current=dot?engine->settings.islandDotPercent:(int)std::lround(engine->settings.islandScale*100);try{current=std::stoi(Value(input));}catch(...){}current+=((id==310||id==312)?-1:1)*(dot?1:5);current=std::max(dot?0:75,std::min(dot?100:150,current));SetWindowTextW(Child(input),Number(current).c_str());}
 if(id==314||id==315){int dot=20,percent=100;if(id==314){auto read=[&](int input){std::wstring v=Value(input);if(v.empty()||v.find_first_not_of(L"0123456789")!=std::wstring::npos)throw std::runtime_error("size");return std::stoi(v);};try{dot=read(1005);percent=read(1006);}catch(...){Notify(L"请输入整数：黑点 0–100%，灵动岛 75–150%。");return;}if(dot<0||dot>100||percent<75||percent>150){Notify(L"黑点范围 0–100%，灵动岛范围 75–150%。");return;}}engine->settings.islandDotPercent=dot;engine->settings.islandScale=percent/100.0;Save();SetWindowTextW(Child(1005),Number(dot).c_str());SetWindowTextW(Child(1006),Number(percent).c_str());SyncDotControls(false);PositionIsland();if(island->Visible())island->Render();else handle->Render();Notify(L"大小已保存；默认黑点 20%，展开比例 100%。");}
 if(id==330||id==331){settingsTab=id==330?1:0;Layout();return;}
 if(id==332){engine->settings.glassRefraction=50;engine->settings.glassTransparency=65;engine->settings.glassHighlight=55;Save();int values[]={50,65,55};for(int i=0;i<3;i++){if(Child(1010+i))SendMessageW(Child(1010+i),TBM_SETPOS,TRUE,values[i]);if(Child(1013+i))SetWindowTextW(Child(1013+i),(Number(values[i])+L"%").c_str());}RefreshGlass();Notify(L"已恢复推荐：曲面 50%、透明度 65%、高光 55%。");return;}
 if(id>=320&&id<=322){engine->settings.glassMode=id-320;Save();for(int i=0;i<3;i++){HWND control=Child(320+i);if(control){styles[control].primary=i==engine->settings.glassMode;InvalidateRect(control,NULL,FALSE);}}RefreshGlass();Notify(id==320?L"玻璃已关闭。":id==321?L"轻量：清透材质，无持续动画。":L"清透材质与柔和形变；Win7不提供桌面折射。");}
 }catch(const std::exception&){Notify(page==2?L"时长需大于零且不超过 7 天；分钟和秒请输入 0–59。":L"操作未完成，请检查输入或本地目录是否可写。");}Tick();}
bool App::StartupEnabled(){HKEY key;std::wstring expected=L"\""+ExePath()+L"\" --silent";wchar_t val[32768]={};DWORD bytes=sizeof(val)-sizeof(wchar_t),type=0;if(RegOpenKeyExW(HKEY_CURRENT_USER,L"Software\\Microsoft\\Windows\\CurrentVersion\\Run",0,KEY_QUERY_VALUE,&key)!=ERROR_SUCCESS)return false;LSTATUS r=RegQueryValueExW(key,L"FreeIslandWin7",NULL,&type,(BYTE*)val,&bytes);RegCloseKey(key);return r==ERROR_SUCCESS&&type==REG_SZ&&_wcsicmp(val,expected.c_str())==0;}
void App::SetStartup(bool on){if(!safe){HKEY key;if(RegCreateKeyExW(HKEY_CURRENT_USER,L"Software\\Microsoft\\Windows\\CurrentVersion\\Run",0,NULL,0,KEY_SET_VALUE,NULL,&key,NULL)!=ERROR_SUCCESS)throw std::runtime_error("startup");LSTATUS r;if(on){std::wstring v=L"\""+ExePath()+L"\" --silent";r=RegSetValueExW(key,L"FreeIslandWin7",0,REG_SZ,(BYTE*)v.c_str(),(DWORD)((v.size()+1)*2));}else r=RegDeleteValueW(key,L"FreeIslandWin7");RegCloseKey(key);if(r!=ERROR_SUCCESS&&r!=ERROR_FILE_NOT_FOUND)throw std::runtime_error("startup");}engine->settings.startup=on;}

void Overlay::Render(){if(width<=0||height<=0)return;++renderCount;lastRender=GetTickCount64();Bitmap bmp(width,height,PixelFormat32bppPARGB);Graphics g(&bmp);Quality(g);g.Clear(Color(0,0,0,0));SolidBrush hit(Color(1,255,255,255));if(kind!=1)g.FillRectangle(&hit,0,0,width,height);Draw(g);HDC screen=GetDC(NULL),mem=CreateCompatibleDC(screen);BITMAPINFO bi={};bi.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);bi.bmiHeader.biWidth=width;bi.bmiHeader.biHeight=-height;bi.bmiHeader.biPlanes=1;bi.bmiHeader.biBitCount=32;bi.bmiHeader.biCompression=BI_RGB;void*bits=NULL;HBITMAP dib=CreateDIBSection(screen,&bi,DIB_RGB_COLORS,&bits,NULL,0);HGDIOBJ old=SelectObject(mem,dib);BitmapData data={};Gdiplus::Rect rr(0,0,width,height);bmp.LockBits(&rr,ImageLockModeRead,PixelFormat32bppPARGB,&data);for(int y=0;y<height;y++)memcpy((BYTE*)bits+y*width*4,(BYTE*)data.Scan0+y*data.Stride,width*4);bmp.UnlockBits(&data);RECT pos=Bounds();POINT dst={pos.left,pos.top},src={0,0};SIZE sz={width,height};BLENDFUNCTION bf={AC_SRC_OVER,0,(BYTE)(closing?std::max(0.0f,1-opening)*255:255),AC_SRC_ALPHA};UpdateLayeredWindow(hwnd,screen,&dst,&sz,mem,&src,0,&bf,ULW_ALPHA);materialDirty=false;SelectObject(mem,old);DeleteObject(dib);DeleteDC(mem);ReleaseDC(NULL,screen);}
void Overlay::Material(Graphics&g,float x,float y,float w,float h,float radius,int target,bool accent){
 GraphicsState state=g.Save();float cx=x+w/2,cy=y+h/2;g.TranslateTransform(cx,cy);
 float entrance=snapshot?1:(closing?1-.035f*opening:1-.04f*pow(1-opening,3));g.ScaleTransform(entrance,entrance);
 if(app->engine->settings.glassMode==2&&pressedTarget==target){
  g.RotateTransform(stretchAngle);g.ScaleTransform(1+stretch,1-stretch*.45f);g.RotateTransform(-stretchAngle);
  g.ScaleTransform(1+squeeze*.009f,1-squeeze*.025f);
 }
 g.TranslateTransform(-cx,-cy);fiGlass::Paint(g,x,y,w,h,radius,app->engine->settings.glassMode,light,accent,app->engine->settings.glassRefraction,app->engine->settings.glassTransparency,app->engine->settings.glassHighlight);g.Restore(state);
}
void Overlay::Draw(Graphics&g){
 if(kind==3){hits.clear();int diameter=app->engine->settings.islandDotSize;bool top=app->engine->settings.dock==Dock::Top,left=app->engine->settings.dock==Dock::Left;int x=top?(width-diameter)/2:(left?2:width-diameter-2),y=top?2:(height-diameter)/2;fiGlass::PaintDot(g,(float)x,(float)y,(float)diameter,app->engine->settings.glassMode,squeeze,app->engine->settings.glassRefraction,app->engine->settings.glassTransparency,app->engine->settings.glassHighlight);return;}
 float scale=(kind==2&&contentScale>0)?contentScale:dpi*(app->classroom?1.5f:1),w=width/scale,h=height/scale;
 g.ScaleTransform(scale,scale);hits.clear();int mode=app->engine->settings.glassMode;
 auto hit=[&](float x,float y,float ww,float hh,int id){hits.push_back({Rect((int)(x*scale),(int)(y*scale),(int)(ww*scale),(int)(hh*scale)),id});};
 auto brand=[&](float x,float y,float size){
  if(mode==0){Brand(g,x,y,size);return;}
  Material(g,x,y,size,size,size/2,-1,true);
  float ax=x+size*5/24,ay=y+size*12.5f/24,aw=size*14/24,ah=size*5/24;
  Box(g,ax-.75f,ay-.75f,aw+1.5f,ah+1.5f,Color(230,255,255,255),size*2.5f/24+.75f);Box(g,ax,ay,aw,ah,INK,size*2.5f/24);
  ax=x+size*10/24;ay=y+size*6.5f/24;aw=size*7/24;ah=size*3/24;
  Box(g,ax-.75f,ay-.75f,aw+1.5f,ah+1.5f,Color(230,255,255,255),size*1.5f/24+.75f);Box(g,ax,ay,aw,ah,INK,size*1.5f/24);
 };
 if(kind==0&&!tucked){float size=app->classroom?54:48;float x=(w-size)/2,y=(h-size)/2;if(mode==0)for(int i=6;i>0;i--)Box(g,x-i,y+3-i,size+2*i,size+2*i,Color(3,25,39,79),(size+2*i)/2);brand(x,y,size);}
 else if(kind==0){int e=edge;bool side=e==1||e==2;float bw=side?9:26,bh=side?26:9,x=side?(e==1?0:w-bw):(w-bw)/2,y=side?(h-bh)/2:(e==3?0:h-bh);Box(g,x,y,bw,bh,SELECTED,5);Icon(g,e==1?17:e==2?18:e==3?16:19,x+(bw-9)/2,y+(bh-9)/2,9);}
 else if(kind==1){
  int order[]={1,2,3,4,5,0};const wchar_t*labels[]={L"正向计时",L"倒计时",L"日程提醒",L"定时关机",L"设置",L"控制中心"};
  for(int i=0;i<6;i++){float a=(-90+i*60)*3.141593f/180;float t=std::min(1.0f,std::max(0.0f,opening*1.32f-i*.06f));float radius=116-(snapshot?0:28*pow(1-t,3));float x=177+cos(a)*radius-40,y=177+sin(a)*radius-38;
   if(mode==0)Box(g,x,y,80,76,Color(255,241,243,250),12);else Material(g,x,y,80,76,18,order[i]);
   if(mode==0){Icon(g,order[i],x+27,y+12,26);Text(g,labels[i],x+3,y+45,74,28,12,INK,false,1);}
   else{GlassIcon(g,order[i],x+27,y+12,26);GlassText(g,labels[i],x+3,y+45,74,28,12,1);}hit(x,y,80,76,order[i]);
  }brand(150,146,54);hit(140,135,74,96,-1);
 }
 else if(kind==2){
  if(mode==0){for(int i=7;i>0;i--)Box(g,10-i,14-i,w-20+2*i,h-24+2*i,Color(3,25,39,79),32+i);Box(g,10,10,w-20,h-24,WHITE,32);}else Material(g,10,10,w-20,h-24,32);
  std::wstring title,detail;int icon=3;
  if(app->activity=="countdown"){title=L"倒计时  "+Format(app->engine->CountdownMs(),true);detail=app->engine->countdownRunning?L"正在计时 · 点击查看":L"已暂停 · 点击继续";icon=2;}
  else if(app->activity=="stopwatch"){title=L"正向计时  "+Format(app->engine->StopwatchMs(),true);detail=app->engine->stopwatchRunning?L"正在计时 · 点击查看":L"已暂停";icon=1;}
  else if(app->activity=="shutdown"&&app->engine->shutdownAt){title=L"关机倒计时  "+Format(app->engine->shutdownAt-NowMs(),true);detail=app->safe?L"安全预览 · 不会实际关机":L"请保存工作 · 随时可以取消";icon=4;}
  else{title=app->lastNotice.title.empty()?L"浮岛已就绪":app->lastNotice.title;detail=app->lastNotice.message.empty()?L"点击悬浮球选择功能 · 拖动调整位置":app->lastNotice.message;}
  if(mode==0){Icon(g,icon,32,34,28);Text(g,title,78,20,w-220,30,17,INK,true);Text(g,detail,78,50,w-220,28,13,INK);}
  else{GlassIcon(g,icon,32,34,28);Box(g,70,18,w-204,61,Color(215,249,251,254),10);Text(g,title,78,20,w-220,30,17,INK,true);Text(g,detail,78,50,w-220,28,13,INK);}
  Box(g,w-120,30,60,38,mode?Color(50,235,242,252):SELECTED,12);
  std::wstring action=app->activity=="shutdown"?L"取消":app->activity.empty()?L"知道了":L"查看";
  if(mode)GlassText(g,action,w-120,30,60,38,12,1);else Text(g,action,w-120,30,60,38,12,INK,false,1);
  Box(g,w-52,30,32,38,mode?Color(34,242,248,255):Color(255,242,244,249),12);
  if(mode)GlassIcon(g,9,w-45,40,18);else Icon(g,9,w-45,40,18,MUTED);
  hit(w-120,30,60,38,1);hit(w-52,30,32,38,2);
 }
}
void Overlay::PointerLight(POINT p){
 if(kind==3||tucked||!Visible()||app->engine->settings.glassMode!=2)return;
 uint64_t now=GetTickCount64();if(now-lightAt<34)return;RECT bounds=Bounds();
 float next=std::max(0.0f,std::min(1.0f,(p.x-bounds.left)/(float)std::max(1,width)));
 if(fabs(next-light)<.015f)return;
 light=next;lightAt=now;materialDirty=true;
}
void Overlay::Tick(){
 if(!Visible())return;
 uint64_t now=GetTickCount64();float duration=closing?140.0f:300.0f,previous=opening;
 opening=(snapshot||kind==3)?1:std::min(1.0f,(now-opened)/duration);bool wasDocking=dockStart!=0;
 if(dockStart){float t=std::min(1.0f,(now-dockStart)/220.0f);float e=1-pow(1-t,3);At((int)(dockFrom.x+(dockTo.x-dockFrom.x)*e),(int)(dockFrom.y+(dockTo.y-dockFrom.y)*e));if(t>=1)dockStart=0;}
 if(released){float t=std::min(1.0f,(now-released)/250.0f);float rebound=fiGlass::Rebound(t);squeeze=releaseSqueeze*rebound;stretch=releaseStretch*rebound;materialDirty=true;if(t>=1){released=0;squeeze=stretch=0;pressedTarget=-2;}}
 if(closing&&opening>=1){Hide();if(kind==2)app->PositionIsland();if(kind==1){app->ball->Show();app->ball->lastUse=now;}return;}
 if(opening<1||previous<1||wasDocking)materialDirty=true;
 if(materialDirty&&now-lastRender>=34)Render();
 POINT p;GetCursorPos(&p);bool hover=Contains(Bounds(),p);if(hover)lastUse=now;
 if(kind==0&&!tucked&&edge&&app->engine->settings.edgeHide&&!down&&!hover&&now-lastUse>1100)app->TuckBall();
 if(kind==2&&!down&&!hover&&!app->islandUrgent&&now>=app->islandUntil)app->CollapseIsland();
}
void Overlay::Press(POINT p){
 PointerLight(p);dockStart=0;down=true;dragged=false;start=lastPointer=p;RECT r=Bounds();origin={r.left,r.top};lastUse=GetTickCount64();
 if((kind!=3||app->engine->settings.islandDotSize>=6)&&!tucked&&app->engine->settings.glassMode==2){pressedTarget=kind==1?-2:-1;if(kind==1){POINT local={p.x-r.left,p.y-r.top};for(auto target:hits)if(Contains(target.first,local)){pressedTarget=target.second;break;}}
  if(pressedTarget!=-2){squeeze=1;stretch=0;released=0;materialDirty=true;}}
}
void Overlay::Move(POINT p){
 PointerLight(p);if(!down)return;int dx=p.x-start.x,dy=p.y-start.y;if(abs(dx)+abs(dy)>D(5))dragged=true;
 if(dragged&&kind!=1){
  if(kind!=3&&!tucked&&app->engine->settings.glassMode==2){float vx=(float)(p.x-lastPointer.x),vy=(float)(p.y-lastPointer.y),distance=sqrt(vx*vx+vy*vy);
   if(distance>.5f){stretchAngle=atan2(vy,vx)*180.0f/3.14159265f;stretch=std::min(.04f,distance/std::max(1.0f,36*dpi)*.04f);squeeze=0;materialDirty=true;}}
  At(origin.x+dx,origin.y+dy);
 }
 lastPointer=p;
}
void Overlay::FinishPress(){
 if(!down)return;
 down=false;
 if(pressedTarget!=-2&&app->engine->settings.glassMode==2){releaseSqueeze=squeeze;releaseStretch=stretch;released=GetTickCount64();materialDirty=true;}
}
void Overlay::Release(POINT p){if(!down)return;Move(p);FinishPress();lastUse=GetTickCount64();if(dragged&&kind!=1){if(kind==0){if(tucked)app->RevealBall();app->SnapBall();}else{RECT r=Bounds();app->DockIsland(POINT{r.left+W(r)/2,r.top+H(r)/2});}return;}RECT r=Bounds();POINT local={p.x-r.left,p.y-r.top};if(kind==0){if(tucked)app->RevealBall();else app->OpenMenu();}else if(kind==3)app->ShowIsland("");else if(kind==1){for(auto h:hits)if(Contains(h.first,local)){app->CloseMenu();if(h.second>=0)app->OpenMain(h.second);break;}}else if(kind==2){for(auto h:hits)if(Contains(h.first,local)){if(h.second==1){if(app->activity=="shutdown"){app->engine->CancelShutdown();app->islandUrgent=false;}else if(!app->activity.empty())app->OpenMain(app->activity=="countdown"?2:1);}app->CollapseIsland();break;}app->islandUntil=GetTickCount64()+7000;}}
void App::PositionBall(bool restore){if(!ball)return;if(ball->tucked)RevealBall();int size=D(classroom?104:68);ball->Size(size,size);POINT p={engine->settings.ballX,engine->settings.ballY};if(restore||p.x==-99999){GetCursorPos(&p);RECT wa=WorkAt(p);p={wa.right-size-D(12),wa.top+H(wa)*58/100};}RECT wa=WorkAt(p);p.x=std::max(wa.left,std::min(p.x,wa.right-size));p.y=std::max(wa.top,std::min(p.y,wa.bottom-size));ball->At(p.x,p.y);ball->edge=0;ball->Show();SnapBall();}
void App::SnapBall(){RECT r=ball->Bounds();POINT center={r.left+W(r)/2,r.top+H(r)/2};RECT wa=WorkAt(center);ball->edge=0;if(engine->settings.edgeHide){if(r.left<=wa.left+D(22))ball->edge=1;else if(r.right>=wa.right-D(22))ball->edge=2;else if(r.top<=wa.top+D(18))ball->edge=3;else if(r.bottom>=wa.bottom-D(18))ball->edge=4;}int x=std::max(wa.left,std::min(r.left,wa.right-W(r))),y=std::max(wa.top,std::min(r.top,wa.bottom-H(r)));if(ball->edge==1)x=wa.left;if(ball->edge==2)x=wa.right-W(r);if(ball->edge==3)y=wa.top;if(ball->edge==4)y=wa.bottom-H(r);ball->At(x,y);engine->settings.ballX=x;engine->settings.ballY=y;Save();ball->AnimateDock(POINT{r.left,r.top},POINT{x,y});ball->lastUse=GetTickCount64();}
void App::TuckBall(){if(ball->tucked||!ball->edge)return;RECT r=ball->Bounds(),wa=WorkAt(POINT{r.left+W(r)/2,r.top+H(r)/2});bool side=ball->edge<3;int thin=D(classroom?44:10),longer=D(classroom?64:28);ball->tucked=true;ball->Size(side?thin:longer,side?longer:thin);int x=side?(ball->edge==1?wa.left:wa.right-ball->width):r.left+W(r)/2-ball->width/2,y=side?r.top+H(r)/2-ball->height/2:(ball->edge==3?wa.top:wa.bottom-ball->height);ball->At(x,y);ball->Render();}
void App::RevealBall(){if(!ball->tucked)return;RECT r=ball->Bounds(),wa=WorkAt(POINT{r.left,r.top});ball->tucked=false;int size=D(classroom?104:68);ball->Size(size,size);ball->At(std::max(wa.left,std::min(r.left+W(r)/2-size/2,wa.right-size)),std::max(wa.top,std::min(r.top+H(r)/2-size/2,wa.bottom-size)));ball->Show();ball->lastUse=GetTickCount64();}
void App::OpenMenu(){if(menu->Visible()){CloseMenu();return;}RevealBall();RECT br=ball->Bounds();RECT wa=WorkAt(POINT{br.left,br.top});int size=D(classroom?531:354);menu->Size(size,size);menu->At(std::max(wa.left,std::min(br.left+W(br)/2-size/2,wa.right-size)),std::max(wa.top,std::min(br.top+H(br)/2-size/2,wa.bottom-size)));ball->Hide();menu->Show();SetForegroundWindow(menu->hwnd);}
void App::CloseMenu(){menu->End();}
void App::PositionIsland(){if(!island||!handle)return;POINT p;GetCursorPos(&p);if(!engine->settings.monitor.empty()){MonitorLookup f={&engine->settings.monitor,NULL};EnumDisplayMonitors(NULL,NULL,FindMonitor,(LPARAM)&f);if(f.m){MONITORINFO mi={sizeof(mi)};GetMonitorInfoW(f.m,&mi);p={mi.rcWork.left+1,mi.rcWork.top+1};}}
 RECT wa=WorkAt(p);float s=(float)(dpi*(classroom?1.5f:1)*engine->settings.islandScale);s=std::min(s,std::min(W(wa)/440.0f,H(wa)/102.0f));island->contentScale=s;island->Size((int)(440*s),(int)(102*s));bool top=engine->settings.dock==Dock::Top,left=engine->settings.dock==Dock::Left;int ix=top?(int)(wa.left+W(wa)*engine->settings.anchor-island->width/2):(left?wa.left:wa.right-island->width),iy=top?wa.top:(int)(wa.top+H(wa)*engine->settings.anchor-island->height/2);island->At(std::max<int>(wa.left,std::min<int>(ix,wa.right-island->width)),std::max<int>(wa.top,std::min<int>(iy,wa.bottom-island->height)));int touchSize=D(classroom?44:24);handle->Size(touchSize,touchSize);int hx=top?(int)(wa.left+W(wa)*engine->settings.anchor-handle->width/2):(left?wa.left:wa.right-handle->width),hy=top?wa.top:(int)(wa.top+H(wa)*engine->settings.anchor-handle->height/2);handle->At(std::max<int>(wa.left,std::min<int>(hx,wa.right-handle->width)),std::max<int>(wa.top,std::min<int>(hy,wa.bottom-handle->height)));if(island->Visible())handle->Hide();else handle->Show();}
void App::DockIsland(POINT p){Overlay*o=island->Visible()?island.get():handle.get();RECT from=o->Bounds(),wa=WorkAt(p);int t=abs(p.y-wa.top),l=abs(p.x-wa.left),r=abs(wa.right-p.x);engine->settings.dock=t<=l&&t<=r?Dock::Top:l<=r?Dock::Left:Dock::Right;engine->settings.anchor=std::max(0.0,std::min(1.0,engine->settings.dock==Dock::Top?(p.x-wa.left)/(double)W(wa):(p.y-wa.top)/(double)H(wa)));MONITORINFOEXW mi={};mi.cbSize=sizeof(mi);GetMonitorInfoW(MonitorFromPoint(p,MONITOR_DEFAULTTONEAREST),&mi);engine->settings.monitor=mi.szDevice;Save();PositionIsland();RECT to=o->Bounds();o->AnimateDock(POINT{from.left,from.top},POINT{to.left,to.top});islandUntil=GetTickCount64()+7000;}
void App::ShowIsland(const std::string&kind){if(islandUrgent&&engine->shutdownAt)return;activity=kind;if(kind.empty()){if(engine->shutdownAt)activity="shutdown";else if(engine->countdownActive)activity="countdown";else if(engine->StopwatchMs()>0)activity="stopwatch";}islandUrgent=false;islandUntil=GetTickCount64()+7000;PositionIsland();handle->Hide();island->Show();}
void App::ShowNotice(Notice n){if(islandUrgent&&engine->shutdownAt&&n.kind!="shutdown")return;lastNotice=n;activity=n.urgent&&n.kind=="shutdown"&&engine->shutdownAt?"shutdown":"";islandUrgent=!activity.empty();islandUntil=GetTickCount64()+20000;PositionIsland();handle->Hide();island->Show();if(engine->settings.sound)MessageBeep(MB_ICONASTERISK);tray.uFlags=NIF_INFO;wcsncpy(tray.szInfoTitle,n.title.c_str(),63);wcsncpy(tray.szInfo,n.message.c_str(),255);tray.dwInfoFlags=n.urgent?NIIF_WARNING:NIIF_INFO;Shell_NotifyIconW(NIM_MODIFY,&tray);}
void App::CollapseIsland(){islandUrgent=false;island->End();}

void App::PaintStage(Graphics&g,int ww,int hh){Quality(g);g.Clear(PAPER);float w=ww/dpi,h=hh/dpi;g.ScaleTransform(dpi,dpi);Brand(g,36,24,40);Text(g,L"课堂计时",92,22,280,46,30,INK,true);Box(g,w-220,24,184,58,WHITE,12,true);Icon(g,9,w-204,42,22,INK);Text(g,L"退出展示  Esc",w-169,24,126,58,18);bool active=engine->countdownActive||engine->stopwatchRunning||engine->StopwatchMs()>0;std::wstring t=engine->countdownActive?Format(engine->CountdownMs()):active?Format(engine->StopwatchMs(),true):L"00:00";float fontSize=std::min((h-270)*.84f,(w-100)/(t.size()*.59f));Digits(g,t,30,106,w-60,std::max(90.0f,h-332),fontSize,BLUE);Text(g,engine->countdownActive?(engine->countdownRunning?L"倒计时":L"倒计时已暂停"):active?(engine->stopwatchRunning?L"正向计时":L"计时已暂停"):lastNotice.kind=="countdown"?L"时间到":L"准备开始",36,h-216,w-72,42,28,MUTED,false,1);
 if(active){Box(g,w/2-206,h-138,202,68,BLUE,14);Icon(g,engine->countdownRunning||engine->stopwatchRunning?11:13,w/2-186,h-116,24,WHITE);Text(g,engine->countdownRunning||engine->stopwatchRunning?L"暂停计时":L"继续计时",w/2-148,h-138,130,68,22,WHITE);Box(g,w/2+12,h-138,184,68,WHITE,14,true);Icon(g,14,w/2+30,h-116,24,INK);Text(g,L"结束计时",w/2+66,h-138,116,68,22);}else{const wchar_t*names[]={L"5 分钟",L"10 分钟",L"40 分钟",L"正向计时"};for(int i=0;i<4;i++){Box(g,w/2-368+i*188,h-138,176,68,i==0?BLUE:WHITE,14,i!=0);Text(g,names[i],w/2-368+i*188,h-138,176,68,22,i==0?WHITE:INK,false,1);}}
 Text(g,L"空格键暂停 / 继续 · Esc 退出展示",36,h-56,w-72,30,16,MUTED,false,1);
}
void App::OpenStage(){CloseMenu();POINT p;GetCursorPos(&p);RECT r=WorkAt(p,true);SetWindowPos(stage,HWND_TOP,r.left,r.top,W(r),H(r),SWP_SHOWWINDOW);SetForegroundWindow(stage);InvalidateRect(stage,NULL,FALSE);}
void App::OpenMain(int p){CloseMenu();Navigate(p);ShowWindow(main,IsIconic(main)?SW_RESTORE:SW_SHOWNORMAL);SetForegroundWindow(main);}
void App::ApplyScene(){classroom=engine->settings.scene==Scene::Classroom;RECT r;GetWindowRect(main,&r);RECT work=WorkAt(POINT{r.left,r.top});RECT ideal=Rect(0,0,D(classroom?1280:1000),D(classroom?820:720));AdjustWindowRectEx(&ideal,WS_OVERLAPPEDWINDOW,FALSE,0);SetWindowPos(main,NULL,std::max(work.left,std::min(r.left,work.right-W(ideal))),std::max(work.top,std::min(r.top,work.bottom-H(ideal))),std::min(W(ideal),W(work)),std::min(H(ideal),H(work)),SWP_NOZORDER|SWP_NOACTIVATE);Layout();PositionBall();PositionIsland();}
void App::PopupTray(){HMENU m=CreatePopupMenu();AppendMenuW(m,MF_STRING,400,L"打开控制中心");AppendMenuW(m,MF_STRING,401,L"找回悬浮球");AppendMenuW(m,MF_STRING,402,L"显示当前计时");AppendMenuW(m,MF_STRING,403,L"取消预约关机");AppendMenuW(m,MF_SEPARATOR,0,NULL);AppendMenuW(m,MF_STRING,404,L"退出浮岛");POINT p;GetCursorPos(&p);SetForegroundWindow(main);int cmd=TrackPopupMenu(m,TPM_RETURNCMD|TPM_RIGHTBUTTON,p.x,p.y,0,main,NULL);DestroyMenu(m);if(cmd==400)OpenMain();if(cmd==401)PositionBall(true);if(cmd==402)ShowIsland("");if(cmd==403){engine->CancelShutdown();islandUrgent=false;Notify(L"关机预约已取消。");}if(cmd==404)Close();PostMessageW(main,WM_NULL,0,0);}
void App::Initialize(bool silent){WNDCLASSEXW wc={sizeof(wc)};wc.hInstance=instance;wc.hCursor=LoadCursor(NULL,IDC_ARROW);wc.hIcon=LoadIconW(instance,MAKEINTRESOURCEW(101));wc.hIconSm=wc.hIcon;wc.lpfnWndProc=MainProc;wc.lpszClassName=L"FreeIslandWin7.Main";wc.style=CS_HREDRAW|CS_VREDRAW;RegisterClassExW(&wc);wc.lpfnWndProc=OverlayProc;wc.lpszClassName=L"FreeIslandWin7.Overlay";wc.style=0;RegisterClassExW(&wc);wc.lpfnWndProc=StageProc;wc.lpszClassName=L"FreeIslandWin7.Stage";RegisterClassExW(&wc);if(!safe)engine->settings.startup=StartupEnabled();
 RECT work;SystemParametersInfoW(SPI_GETWORKAREA,0,&work,0);RECT desired=Rect(0,0,D(classroom?1280:1000),D(classroom?820:720));AdjustWindowRectEx(&desired,WS_OVERLAPPEDWINDOW,FALSE,0);int width=std::min(W(work),W(desired)),height=std::min(H(work),H(desired));main=CreateWindowExW(0,L"FreeIslandWin7.Main",L"浮岛 · Win7 原生版",WS_OVERLAPPEDWINDOW|WS_CLIPCHILDREN,work.left+(W(work)-width)/2,work.top+(H(work)-height)/2,width,height,NULL,NULL,instance,NULL);stage=CreateWindowExW(0,L"FreeIslandWin7.Stage",L"浮岛 Win7 · 课堂计时",WS_POPUP,0,0,900,600,NULL,NULL,instance,NULL);
 if(!main||!stage)throw std::runtime_error("Cannot create application windows");ball.reset(new Overlay(0));menu.reset(new Overlay(1));island.reset(new Overlay(2));handle.reset(new Overlay(3));Layout();PositionBall();PositionIsland();exitEvent=CreateEventW(NULL,FALSE,FALSE,safe?L"Local\\FreeIsland.Win7.Test.Exit":L"Local\\FreeIsland.Win7.Exit");tray.cbSize=sizeof(tray);tray.hWnd=main;tray.uID=1;tray.uFlags=NIF_ICON|NIF_TIP|NIF_MESSAGE;tray.hIcon=LoadIconW(instance,MAKEINTRESOURCEW(101));tray.uCallbackMessage=WM_APP+1;wcscpy(tray.szTip,L"浮岛 Win7 · 安静待命");Shell_NotifyIconW(NIM_ADD,&tray);RegisterHotKey(main,8137,MOD_CONTROL|MOD_ALT|MOD_NOREPEAT,VK_SPACE);SetTimer(main,1,16,NULL);if(!silent)ShowWindow(main,SW_SHOWNORMAL);if(smoke)PostMessageW(main,WM_APP+3,0,0);
}
void App::Tick(){
 if(ending)return;if(exitEvent&&WaitForSingleObject(exitEvent,0)==WAIT_OBJECT_0){Close();return;}
 static uint64_t coreTick=0;uint64_t now=GetTickCount64();
 if(now-coreTick>=200){
  coreTick=now;engine->Tick();
  if(engine->countdownRunning&&!priorCount)ShowIsland("countdown");if(engine->stopwatchRunning&&!priorStop)ShowIsland("stopwatch");if(engine->shutdownAt&&engine->shutdownAt!=priorShutdown)ShowIsland("shutdown");
  priorCount=engine->countdownRunning;priorStop=engine->stopwatchRunning;priorShutdown=engine->shutdownAt;if(!engine->shutdownAt)islandUrgent=false;
  auto notices=engine->TakeNotices();for(auto n:notices)ShowNotice(n);if(now>feedbackUntil&&!feedback.empty())feedback.clear();
  // Native child controls are excluded from parent paint. Repaint only when visible content changes.
  std::wstring timer=Format(engine->CountdownMs())+L"|"+Format(engine->StopwatchMs(),true)+L"|"+std::to_wstring(engine->countdownActive)+std::to_wstring(engine->countdownRunning)+std::to_wstring(engine->stopwatchRunning);
  std::wstring shutdown=engine->shutdownAt?Format(engine->shutdownAt-NowMs(),true):L"";
  std::wstring nextMain=std::to_wstring(NowMs()/60000)+L"|"+std::to_wstring(page)+L"|"+feedback+L"|"+std::to_wstring(engine->reminders.size());
  if(page<=2)nextMain+=timer;if(page==4)nextMain+=shutdown;for(const auto&r:engine->reminders)nextMain+=std::to_wstring(r.completed)+std::to_wstring(r.dueMs);
  if(nextMain!=mainFrame){mainFrame=nextMain;if(IsWindowVisible(main)&&!IsIconic(main))InvalidateRect(main,NULL,FALSE);}
  if(timer!=stageFrame){stageFrame=timer;if(IsWindowVisible(stage))InvalidateRect(stage,NULL,FALSE);}
  std::wstring nextIsland=activity=="countdown"||activity=="stopwatch"?timer:activity=="shutdown"?shutdown:lastNotice.title+lastNotice.message;
  if(nextIsland!=islandFrame){islandFrame=nextIsland;if(island->Visible())island->Render();}
 }
 ball->Tick();menu->Tick();island->Tick();handle->Tick();
}
void App::Close(){if(ending)return;ending=true;try{engine->Save();}catch(...){}KillTimer(main,1);UnregisterHotKey(main,8137);Shell_NotifyIconW(NIM_DELETE,&tray);ShowWindow(main,SW_HIDE);if(stage)DestroyWindow(stage);DestroyWindow(main);main=NULL;PostQuitMessage(0);}
App::~App(){if(!ending)Close();if(exitEvent)CloseHandle(exitEvent);if(font)DeleteObject(font);if(whiteBrush)DeleteObject(whiteBrush);}
void App::Capture(const std::wstring&name,HWND win){RECT r;GetClientRect(win,&r);int w=W(r),h=H(r);Bitmap bmp(w,h,PixelFormat32bppARGB);Graphics g(&bmp);if(win==main)Paint(g);else if(win==stage)PaintStage(g,w,h);else{Overlay*o=(Overlay*)GetWindowLongPtrW(win,GWLP_USERDATA);g.Clear(Color(0,0,0,0));o->opening=1;o->Draw(g);}if(win==main){HDC dc=g.GetHDC();for(HWND child:children){RECT cr;GetWindowRect(child,&cr);POINT pt={cr.left,cr.top};ScreenToClient(main,&pt);int saved=SaveDC(dc);SetViewportOrgEx(dc,pt.x,pt.y,NULL);SendMessageW(child,WM_PRINT,(WPARAM)dc,PRF_CLIENT|PRF_NONCLIENT|PRF_CHILDREN|PRF_ERASEBKGND);RestoreDC(dc,saved);}g.ReleaseHDC(dc);}UINT size=0,count=0;GetImageEncodersSize(&count,&size);std::vector<BYTE> memory(size);ImageCodecInfo*info=(ImageCodecInfo*)memory.data();GetImageEncoders(count,size,info);CLSID encoder={};for(UINT i=0;i<count;i++)if(wcscmp(info[i].MimeType,L"image/png")==0)encoder=info[i].Clsid;std::wstring dir=ExeDir()+L"\\smoke-artifacts";CreateDirectoryW(dir.c_str(),NULL);if(bmp.Save((dir+L"\\"+name+L".png").c_str(),&encoder,NULL)!=Ok)throw std::runtime_error("capture");}
void App::CheckGlassControls(){
 if(!safe)throw std::runtime_error("Appearance checks require safe mode");
 Navigate(5);Command(330);if(settingsTab!=1)throw std::runtime_error("Appearance settings route");
 const int savedMode=engine->settings.glassMode;Command(322);ShowIsland("");HWND sameIsland=island->hwnd;
 auto pixels=[&](Overlay*o){Bitmap b(o->width,o->height,PixelFormat32bppARGB);{Graphics g(&b);Quality(g);g.Clear(Color(0,0,0,0));o->Draw(g);}std::vector<DWORD> result;BitmapData data={};Gdiplus::Rect area(0,0,o->width,o->height);if(b.LockBits(&area,ImageLockModeRead,PixelFormat32bppARGB,&data)!=Ok)throw std::runtime_error("Cannot read appearance pixels");for(int y=0;y<o->height;y++){const DWORD*row=(const DWORD*)((const BYTE*)data.Scan0+y*data.Stride);result.insert(result.end(),row,row+o->width);}b.UnlockBits(&data);return result;};
 for(int index=0;index<3;index++){
  HWND slider=Child(1010+index);RECT bounds={};GetWindowRect(slider,&bounds);if(!slider||H(bounds)<U(44))throw std::runtime_error("Appearance slider target is too short");
  SetFocus(slider);if(GetFocus()!=slider)throw std::runtime_error("Appearance slider lacks keyboard focus");
  SendMessageW(slider,TBM_SETPOS,TRUE,0);SendMessageW(main,WM_HSCROLL,TB_THUMBTRACK,(LPARAM)slider);
  auto low=pixels(island.get());uint64_t prior=island->renderCount;
  SendMessageW(slider,TBM_SETPOS,TRUE,100);SendMessageW(main,WM_HSCROLL,TB_ENDTRACK,(LPARAM)slider);
  auto high=pixels(island.get());if(low==high||island->hwnd!=sameIsland||!island->Visible()||island->renderCount<=prior||Value(1013+index)!=L"100%")throw std::runtime_error("Appearance slider did not update the visible material and percentage");
  unsigned glyphs=0;for(size_t i=0;i<low.size();i++)if(low[i]==INK.GetValue()){++glyphs;if(high[i]!=low[i])throw std::runtime_error("Material preference faded or moved text");}if(!glyphs)throw std::runtime_error("No stable text pixels to verify");
  if(engine->settings.islandDotSize!=6)throw std::runtime_error("Material preference changed collapsed dot size");
  Command(332);
 }
 // Verify the transparency control changes interior alpha, not just a label.
 auto centerAlpha=[&](int transparency){Bitmap b(80,60,PixelFormat32bppARGB);{Graphics g(&b);Quality(g);g.Clear(Color(0,0,0,0));fiGlass::Paint(g,4,4,72,52,18,2,.35f,false,50,transparency,55);}Color c;b.GetPixel(40,30,&c);return c.GetA();};
 if(centerAlpha(0)<=centerAlpha(65)||centerAlpha(65)<=centerAlpha(100))throw std::runtime_error("Transparency does not change material interior alpha");
 // Off ignores material tuning and keeps its original pixels.
 Command(320);auto off=pixels(island.get());for(int i=0;i<3;i++){SendMessageW(Child(1010+i),TBM_SETPOS,TRUE,100);SendMessageW(main,WM_HSCROLL,TB_ENDTRACK,(LPARAM)Child(1010+i));}if(pixels(island.get())!=off)throw std::runtime_error("Off mode changed with appearance tuning");
 Command(332);if(engine->settings.glassRefraction!=50||engine->settings.glassTransparency!=65||engine->settings.glassHighlight!=55||engine->settings.glassMode!=0)throw std::runtime_error("Recommended values changed the selected material mode");
 for(int i=0;i<3;i++)if(Value(1013+i)!=(Number((int[]){50,65,55}[i])+L"%"))throw std::runtime_error("Recommended percentage label is stale");
 Command(320+savedMode);Capture((classroom?L"classroom-":L"desktop-")+std::wstring(L"glass-appearance-settings"),main);CollapseIsland();Command(331);
}
void App::CheckDotMaterials(){
 if(!safe)throw std::runtime_error("Dot checks require safe mode");
 int savedPercent=engine->settings.islandDotPercent,savedMode=engine->settings.glassMode;Dock savedDock=engine->settings.dock;
 auto pump=[&](DWORD duration){uint64_t end=GetTickCount64()+duration;while(GetTickCount64()<end){MSG msg;while(PeekMessageW(&msg,NULL,0,0,PM_REMOVE)){if(msg.message==WM_QUIT)throw std::runtime_error("Unexpected UI exit");TranslateMessage(&msg);DispatchMessageW(&msg);}MsgWaitForMultipleObjectsEx(0,NULL,20,QS_ALLINPUT,MWMO_INPUTAVAILABLE);}};
 const int percentages[]={0,20,100},diameters[]={3,6,20};
 for(int size=0;size<3;size++){
  engine->settings.islandDotPercent=percentages[size];Save();if(engine->settings.islandDotSize!=diameters[size])throw std::runtime_error("Dot diameter mapping changed");
  for(int dock=0;dock<3;dock++){
   engine->settings.dock=(Dock)dock;PositionIsland();HWND original=handle->hwnd;
   int touch=D(classroom?44:24),diameter=diameters[size];if(handle->width!=touch||handle->height!=touch)throw std::runtime_error("Dot touch area changed with material");
   int left=dock==0?(touch-diameter)/2:dock==1?2:touch-diameter-2,top=dock==0?2:(touch-diameter)/2;
   uint64_t hashes[3]={};
   for(int mode=0;mode<3;mode++){
    uint64_t prior=handle->renderCount;Command(320+mode);
    if(!handle->Visible()||handle->hwnd!=original||handle->renderCount<=prior)throw std::runtime_error("Visible dot did not update its material immediately");
    Bitmap frame(touch,touch,PixelFormat32bppARGB);{Graphics g(&frame);Quality(g);g.Clear(Color(0,0,0,0));handle->Draw(g);}
    BitmapData data={};Gdiplus::Rect area(0,0,touch,touch);if(frame.LockBits(&area,ImageLockModeRead,PixelFormat32bppARGB,&data)!=Ok)throw std::runtime_error("Cannot inspect dot pixels");
    bool outside=false,colored=false,black=true;unsigned count=0,maxAlpha=0;float lightContrast=0,darkContrast=0;uint64_t hash=1469598103934665603ULL;
    for(int y=0;y<touch;y++){const DWORD*row=(const DWORD*)((const BYTE*)data.Scan0+y*data.Stride);for(int x=0;x<touch;x++){
     DWORD pixel=row[x];hash=(hash^pixel)*1099511628211ULL;unsigned alpha=pixel>>24;if(!alpha)continue;
     if(x<left||x>=left+diameter||y<top||y>=top+diameter)outside=true;
     ++count;maxAlpha=std::max(maxAlpha,alpha);unsigned rgb=pixel&0xffffff;colored|=rgb!=0;black&=rgb==0;
     float luminance=((pixel>>16&255)*.2126f+(pixel>>8&255)*.7152f+(pixel&255)*.0722f),coverage=alpha/255.0f;
     lightContrast=std::max(lightContrast,(255-luminance)*coverage);darkContrast=std::max(darkContrast,std::fabs(luminance-16)*coverage);
    }}
    frame.UnlockBits(&data);hashes[mode]=hash;
    if(outside||count<3||maxAlpha<30)throw std::runtime_error("Dot escaped its physical size or became invisible");
    if(mode==0&&(!black||maxAlpha!=255))throw std::runtime_error("Off mode must retain the black dot");
    if(mode!=0&&(!colored||maxAlpha>=250||lightContrast<12||darkContrast<12))throw std::runtime_error("Glass dot lacks transparency or light/dark visibility");
    if(dock==0){
     Capture((classroom?L"classroom-":L"desktop-")+std::wstring(L"dot-")+Number(diameter)+L"-mode-"+Number(mode),handle->hwnd);
     RECT bounds=handle->Bounds();POINT center={bounds.left+touch/2,bounds.top+touch/2};handle->Press(center);
     bool shouldCompress=mode==2&&diameter>=6;if((handle->squeeze!=0)!=shouldCompress)throw std::runtime_error("Tiny dot interaction did not respect size/mode");
     handle->FinishPress();pump(320);if(handle->released||handle->squeeze||handle->stretch||handle->dockStart)throw std::runtime_error("Dot material did not settle");
     uint64_t idle=handle->renderCount;pump(240);if(handle->renderCount!=idle)throw std::runtime_error("Dot has an idle material repaint loop");
    }
   }
   if(hashes[0]==hashes[1]||hashes[1]==hashes[2])throw std::runtime_error("Dot material modes render identically");
  }
 }
 engine->settings.islandDotPercent=savedPercent;engine->settings.dock=savedDock;Save();Command(320+savedMode);PositionIsland();
}
void App::CheckPaintStability(){
 if(!safe)throw std::runtime_error("Paint checks require safe mode");
 auto pump=[&](DWORD duration){uint64_t end=GetTickCount64()+duration;while(GetTickCount64()<end){MSG msg;while(PeekMessageW(&msg,NULL,0,0,PM_REMOVE)){if(msg.message==WM_QUIT)throw std::runtime_error("Unexpected UI exit");TranslateMessage(&msg);DispatchMessageW(&msg);}MsgWaitForMultipleObjectsEx(0,NULL,25,QS_ALLINPUT,MWMO_INPUTAVAILABLE);}};
 bool priorEdge=engine->settings.edgeHide;engine->settings.edgeHide=false;
 engine->CancelCountdown();engine->ResetStopwatch();engine->CancelShutdown();CollapseIsland();Navigate(5);ShowWindow(main,SW_SHOWNORMAL);UpdateWindow(main);pump(450);
 uint64_t startMain=mainPaints,startChildren=childPaints;pump(2200);uint64_t idleMain=mainPaints-startMain,idleChildren=childPaints-startChildren;
 if(idleMain>2||idleChildren>2)throw std::runtime_error("Idle control window continuously repaints");
 uint64_t materialPaints=0;for(int mode=1;mode<=2;mode++){Command(320+mode);pump(350);uint64_t before=ball->renderCount+menu->renderCount+island->renderCount+handle->renderCount;pump(650);uint64_t change=ball->renderCount+menu->renderCount+island->renderCount+handle->renderCount-before;if(change>1)throw std::runtime_error("Glass material has an idle repaint loop");materialPaints+=change;}
 // Exercise the same material geometry used by the visible controls without
 // screen sampling; opaque glyph pixels must stay fixed through deformation.
 auto checkPress=[&](Overlay* overlay,POINT point,int target){
  Bitmap before(overlay->width,overlay->height,PixelFormat32bppARGB),after(overlay->width,overlay->height,PixelFormat32bppARGB);
  {Graphics g(&before);Quality(g);g.Clear(Color(0,0,0,0));overlay->Draw(g);}
  overlay->Press(point);if(overlay->pressedTarget!=target||overlay->squeeze!=1)throw std::runtime_error("Water press target not isolated");
  {Graphics g(&after);Quality(g);g.Clear(Color(0,0,0,0));overlay->Draw(g);}
  BitmapData first={},second={};Gdiplus::Rect area(0,0,overlay->width,overlay->height);
  if(before.LockBits(&area,ImageLockModeRead,PixelFormat32bppARGB,&first)!=Ok)throw std::runtime_error("Cannot inspect material");
  if(after.LockBits(&area,ImageLockModeRead,PixelFormat32bppARGB,&second)!=Ok){before.UnlockBits(&first);throw std::runtime_error("Cannot inspect pressed material");}
  unsigned glyphs=0,changes=0;bool stable=true;
  for(int y=0;y<area.Height;y++){const DWORD*a=(const DWORD*)((const BYTE*)first.Scan0+y*first.Stride);const DWORD*b=(const DWORD*)((const BYTE*)second.Scan0+y*second.Stride);for(int x=0;x<area.Width;x++){if(a[x]==INK.GetValue()){++glyphs;if(b[x]!=a[x])stable=false;}if(a[x]!=b[x])++changes;}}
  before.UnlockBits(&first);after.UnlockBits(&second);
  if(!stable||!glyphs||!changes)throw std::runtime_error("Press must deform material while keeping glyphs fixed");
  overlay->FinishPress();pump(340);if(overlay->released||overlay->squeeze||overlay->stretch)throw std::runtime_error("Water rebound did not settle");
  uint64_t frames=overlay->renderCount;pump(380);if(overlay->renderCount!=frames)throw std::runtime_error("Water rebound continued after settling");
 };
 RECT br=ball->Bounds();POINT bp={br.left+W(br)/2,br.top+H(br)/2};checkPress(ball.get(),bp,-1);
 ball->Press(bp);ball->Move(POINT{bp.x+D(48),bp.y+D(22)});if(ball->stretch<=0||ball->stretch>.04001f||ball->squeeze!=0)throw std::runtime_error("Water drag exceeded its geometry bound");ball->FinishPress();pump(340);ball->At(br.left,br.top);
 ShowIsland("");RECT ir=island->Bounds();checkPress(island.get(),POINT{ir.left+island->width/2,ir.top+island->height/2},-1);CollapseIsland();
 OpenMenu();auto firstTarget=menu->hits.front();RECT mr=menu->Bounds();checkPress(menu.get(),POINT{mr.left+(firstTarget.first.left+firstTarget.first.right)/2,mr.top+(firstTarget.first.top+firstTarget.first.bottom)/2},firstTarget.second);CloseMenu();
 Command(321);ball->Press(bp);if(ball->squeeze||ball->stretch||ball->released)throw std::runtime_error("Lite mode animated its material");ball->FinishPress();
 Navigate(2);engine->StartCountdown(30000);pump(450);startMain=mainPaints;startChildren=childPaints;pump(2200);uint64_t activeMain=mainPaints-startMain,activeChildren=childPaints-startChildren;
 if(activeMain<1||activeMain>4||activeChildren>2)throw std::runtime_error("Timer repaints native controls or redraws too frequently");
 engine->CancelCountdown();engine->settings.edgeHide=priorEdge;Save();// Counts are written without touching production data.
 std::wstring path=ExeDir()+L"\\smoke-artifacts\\paint-stability.txt";HANDLE file=CreateFileW(path.c_str(),GENERIC_WRITE,0,NULL,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,NULL);
 std::ostringstream result;result<<"PASS: idle 2200ms main="<<idleMain<<", child buttons="<<idleChildren<<"; active timer 2200ms main="<<activeMain<<", child buttons="<<activeChildren<<"; lite/water idle material paints="<<materialPaints<<".\r\nPASS: water press changes material only; opaque glyphs remain fixed; 250ms rebound settles with no later paints; drag stretch <=4%; Lite has no material deformation.\r\n";
 std::string text=result.str();DWORD written;if(file==INVALID_HANDLE_VALUE)throw std::runtime_error("Cannot write paint report");WriteFile(file,text.data(),(DWORD)text.size(),&written,NULL);CloseHandle(file);
}
void App::RunSmoke(){try{
 engine->settings.islandDotPercent=20;engine->settings.glassMode=1;engine->settings.glassRefraction=50;engine->settings.glassTransparency=65;engine->settings.glassHighlight=55;engine->settings.islandScale=1.0;Save();
 for(int scene=0;scene<2;scene++){
  engine->settings.scene=scene?Scene::Classroom:Scene::Desktop;ApplyScene();const wchar_t*pages[]={L"home",L"stopwatch",L"countdown",L"reminders",L"shutdown",L"settings"};
  std::wstring prefix=scene?L"classroom-":L"desktop-";
  for(int i=0;i<6;i++){Navigate(i);UpdateWindow(main);Capture(prefix+pages[i],main);}
  engine->settings.dock=Dock::Top;PositionIsland();Capture(prefix+L"dot",handle->hwnd);
  for(int mode=0;mode<3;mode++){
   Navigate(5);Command(320+mode);if(engine->settings.glassMode!=mode)throw std::runtime_error("glass mode selection");
   Capture(prefix+L"glass-"+Number(mode)+L"-settings",main);RevealBall();Capture(prefix+L"glass-"+Number(mode)+L"-ball",ball->hwnd);
   ShowIsland("");Capture(prefix+L"glass-"+Number(mode)+L"-island",island->hwnd);
   OpenMenu();HWND before=menu->hwnd;float opening=menu->opening;Command(320+mode);
   if(!menu->Visible()||menu->hwnd!=before||menu->opening!=opening)throw std::runtime_error("material update recreated or hid popup");
   Capture(prefix+L"glass-"+Number(mode)+L"-radial",menu->hwnd);CloseMenu();CollapseIsland();
  }
  Navigate(5);Command(321);OpenMenu();Capture(prefix+L"radial",menu->hwnd);CloseMenu();CheckDotMaterials();CheckGlassControls();
 }
 Navigate(5);SetWindowTextW(Child(1005),L"100");SetWindowTextW(Child(1006),L"150");
 if(SendMessageW(Child(1007),TBM_GETPOS,0,0)!=100)throw std::runtime_error("percentage edit/slider sync");
 Command(314);if(engine->settings.islandDotPercent!=100||engine->settings.islandDotSize!=20||engine->settings.islandScale!=1.5)throw std::runtime_error("island percentage apply");
 Capture(L"dot-custom",handle->hwnd);ShowIsland("");Capture(L"island-custom",island->hwnd);CollapseIsland();
 SetWindowTextW(Child(1005),L"101");Command(314);if(engine->settings.islandDotPercent!=100||engine->settings.islandDotSize!=20)throw std::runtime_error("invalid percentage accepted");
 Command(315);if(engine->settings.islandDotPercent!=20||engine->settings.islandDotSize!=6||engine->settings.islandScale!=1.0)throw std::runtime_error("island percentage reset");
 SendMessageW(Child(1007),TBM_SETPOS,TRUE,35);SendMessageW(main,WM_HSCROLL,TB_THUMBPOSITION,(LPARAM)Child(1007));
 if(Value(1005)!=L"35"||engine->settings.islandDotPercent!=20)throw std::runtime_error("slider must synchronize draft without applying");
 Command(315);
 engine->ResetStopwatch();Navigate(1);Command(220);if(!engine->stopwatchRunning)throw std::runtime_error("stopwatch start");Command(220);if(engine->stopwatchRunning)throw std::runtime_error("stopwatch pause");Command(222);
 Navigate(2);SetWindowTextW(Child(1001),L"0");SetWindowTextW(Child(1002),L"0");SetWindowTextW(Child(1003),L"30");Command(240);if(!engine->countdownRunning||engine->CountdownMs()>30000)throw std::runtime_error("countdown start");Command(241);if(engine->countdownRunning)throw std::runtime_error("countdown pause");Command(241);if(!engine->countdownRunning)throw std::runtime_error("countdown resume");Command(242);
 Navigate(3);size_t n=engine->reminders.size();SetWindowTextW(Child(1001),L"原生版自动验证");Command(250);if(engine->reminders.size()!=n+1)throw std::runtime_error("reminder");engine->RemoveReminder(engine->reminders.back().id);
 Navigate(4);Command(280);if(!engine->shutdownAt)throw std::runtime_error("shutdown schedule");Command(281);if(engine->shutdownAt)throw std::runtime_error("shutdown cancel");
 engine->StartCountdown(300000);OpenStage();Capture(L"classroom-presentation",stage);ShowWindow(stage,SW_HIDE);ShowIsland("countdown");Capture(L"island",island->hwnd);CollapseIsland();
 for(int i=0;i<3;i++){engine->settings.dock=(Dock)i;PositionIsland();if(!handle->Visible())throw std::runtime_error("handle");}
 OpenMenu();Capture(L"radial",menu->hwnd);CloseMenu();RECT wa=WorkAt(POINT{0,0});DockIsland(POINT{wa.left+3,wa.top+H(wa)/2});if(engine->settings.dock!=Dock::Left)throw std::runtime_error("dock left");DockIsland(POINT{wa.right-3,wa.top+H(wa)/2});if(engine->settings.dock!=Dock::Right)throw std::runtime_error("dock right");
 if(handle->dockStart)throw std::runtime_error("black dot must not animate");
 for(int e=1;e<=4;e++){RevealBall();ball->edge=e;TuckBall();if(!ball->tucked)throw std::runtime_error("ball edge");}RevealBall();engine->CancelCountdown();engine->settings.dock=Dock::Top;Save();CheckPaintStability();
 std::wstring out=ExeDir()+L"\\smoke-artifacts\\result.txt";HANDLE f=CreateFileW(out.c_str(),GENERIC_WRITE,0,NULL,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,NULL);
 const char*pass="PASS: three appearance sliders update live pixels and percentages; transparency changes material alpha, glyphs stay stable, recommended reset preserves mode and size.\r\nPASS: 12 native control pages; three glass modes on ball, six independent radial choices and island in both scenes.\r\nPASS: percentage slider/edit draft sync, 100 percent = 20 px, invalid 101 rejected, reset 20 percent = 6 px, expanded scale reset 100 percent.\r\nPASS: material updates preserve visible windows; 3/6/20px dots keep bounds and touch areas across all three modes/docks, remain visible on light/dark backgrounds, update immediately and never repaint while idle; stopwatch, countdown, reminder, safe shutdown/cancel, three docks and four edge handles.\r\nNo .NET, registry changes, desktop capture or actual shutdown executed.\r\n";
 DWORD written;WriteFile(f,pass,(DWORD)strlen(pass),&written,NULL);CloseHandle(f);Close();
 }catch(const std::exception&e){std::wstring out=ExeDir()+L"\\smoke-error.txt";HANDLE f=CreateFileW(out.c_str(),GENERIC_WRITE,0,NULL,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,NULL);DWORD written;WriteFile(f,e.what(),(DWORD)strlen(e.what()),&written,NULL);CloseHandle(f);Close();PostQuitMessage(2);}}


LRESULT CALLBACK MainProc(HWND h,UINT msg,WPARAM wp,LPARAM lp){try{switch(msg){case WM_CREATE:return 0;case WM_ERASEBKGND:return 1;case WM_SIZE:if(app&&app->main==h&&wp!=SIZE_MINIMIZED)app->Layout();return 0;case WM_GETMINMAXINFO:{MINMAXINFO*m=(MINMAXINFO*)lp;MONITORINFO monitor={sizeof(monitor)};GetMonitorInfoW(MonitorFromWindow(h,MONITOR_DEFAULTTONEAREST),&monitor);m->ptMinTrackSize={std::min(D(860),W(monitor.rcWork)),std::min(D(690),H(monitor.rcWork))};return 0;}case WM_PAINT:{if(app)++app->mainPaints;PAINTSTRUCT ps;HDC dc=BeginPaint(h,&ps);RECT r;GetClientRect(h,&r);if(W(r)>0&&H(r)>0){Bitmap b(W(r),H(r),PixelFormat32bppPARGB);Graphics g(&b);if(app)app->Paint(g);Graphics out(dc);out.DrawImage(&b,0,0);}EndPaint(h,&ps);return 0;}case WM_DRAWITEM:if(app)app->DrawButton((DRAWITEMSTRUCT*)lp);return TRUE;case WM_COMMAND:if(app&&HIWORD(wp)==BN_CLICKED)app->Command(LOWORD(wp));else if(app&&app->page==5&&LOWORD(wp)==1005&&HIWORD(wp)==EN_CHANGE)SyncDotControls(false);return 0;case WM_HSCROLL:if(app&&app->page==5){if((HWND)lp==app->Child(1007))SyncDotControls(true);else if(lp&&GetDlgCtrlID((HWND)lp)>=1010&&GetDlgCtrlID((HWND)lp)<=1012)app->UpdateGlass(LOWORD(wp)!=TB_THUMBTRACK);}return 0;case WM_CTLCOLOREDIT:case WM_CTLCOLORSTATIC:SetBkColor((HDC)wp,RGB(255,255,255));SetTextColor((HDC)wp,RGB(24,36,58));return (LRESULT)app->whiteBrush;case WM_TIMER:if(app)app->Tick();return 0;case WM_HOTKEY:if(wp==8137){app->PositionBall(true);app->OpenMenu();}return 0;case WM_APP+1:if(lp==WM_RBUTTONUP||lp==WM_CONTEXTMENU)app->PopupTray();else if(lp==WM_LBUTTONDBLCLK)app->OpenMain();return 0;case WM_APP+2:app->OpenMain();return 0;case WM_APP+3:app->RunSmoke();return 0;case WM_DISPLAYCHANGE:app->PositionBall();app->PositionIsland();return 0;case WM_CLOSE:ShowWindow(h,SW_HIDE);return 0;case WM_QUERYENDSESSION:return TRUE;case WM_ENDSESSION:if(wp)app->Close();return 0;case WM_KEYDOWN:if(wp==VK_ESCAPE)ShowWindow(h,SW_HIDE);return 0;}}catch(...){if(app)app->Notify(L"操作未完成，请重试。");}return DefWindowProcW(h,msg,wp,lp);}
LRESULT CALLBACK OverlayProc(HWND h,UINT msg,WPARAM wp,LPARAM lp){Overlay*o=(Overlay*)GetWindowLongPtrW(h,GWLP_USERDATA);if(msg==WM_NCCREATE){o=(Overlay*)((CREATESTRUCT*)lp)->lpCreateParams;SetWindowLongPtrW(h,GWLP_USERDATA,(LONG_PTR)o);}if(!o)return DefWindowProcW(h,msg,wp,lp);try{switch(msg){case WM_ERASEBKGND:return 1;case WM_MOUSEACTIVATE:return o->kind==1?MA_ACTIVATE:MA_NOACTIVATE;case WM_LBUTTONDOWN:{POINT p;GetCursorPos(&p);o->Press(p);SetCapture(h);return 0;}case WM_MOUSEMOVE:{POINT p={GET_X_LPARAM(lp),GET_Y_LPARAM(lp)};ClientToScreen(h,&p);o->PointerLight(p);if(o->down)o->Move(p);return 0;}case WM_DWMCOMPOSITIONCHANGED:case WM_THEMECHANGED:if(o->Visible())o->Render();return 0;case WM_LBUTTONUP:{POINT p;GetCursorPos(&p);o->Release(p);ReleaseCapture();return 0;}case WM_CAPTURECHANGED:if(!o->touching)o->FinishPress();return 0;case WM_TOUCH:{UINT count=LOWORD(wp);std::vector<TOUCHINPUT>input(count);if(GetTouchInputInfo((HTOUCHINPUT)lp,count,input.data(),sizeof(TOUCHINPUT))){for(auto&t:input){POINT p={TOUCH_COORD_TO_PIXEL(t.x),TOUCH_COORD_TO_PIXEL(t.y)};if(t.dwFlags&TOUCHEVENTF_DOWN){if(!o->touching){o->touching=true;o->touchId=t.dwID;o->Press(p);}}else if(o->touching&&t.dwID==o->touchId){if(t.dwFlags&TOUCHEVENTF_UP){o->Release(p);o->touching=false;}else o->Move(p);}}}CloseTouchInputHandle((HTOUCHINPUT)lp);return 0;}case WM_KEYDOWN:if(wp==VK_ESCAPE&&o->kind==1)app->CloseMenu();return 0;case WM_ACTIVATE:if(o->kind==1&&LOWORD(wp)==WA_INACTIVE&&o->Visible())o->End();return 0;}}catch(...){o->down=false;o->touching=false;}return DefWindowProcW(h,msg,wp,lp);}
LRESULT CALLBACK StageProc(HWND h,UINT msg,WPARAM wp,LPARAM lp){try{switch(msg){case WM_ERASEBKGND:return 1;case WM_PAINT:{PAINTSTRUCT ps;HDC dc=BeginPaint(h,&ps);RECT r;GetClientRect(h,&r);Bitmap b(std::max(1,W(r)),std::max(1,H(r)),PixelFormat32bppPARGB);Graphics g(&b);app->PaintStage(g,W(r),H(r));Graphics out(dc);out.DrawImage(&b,0,0);EndPaint(h,&ps);return 0;}case WM_CLOSE:ShowWindow(h,SW_HIDE);return 0;case WM_KEYDOWN:if(wp==VK_ESCAPE){ShowWindow(h,SW_HIDE);return 0;}if(wp==VK_SPACE){if(app->engine->countdownActive)app->engine->PauseCountdown();else app->engine->ToggleStopwatch();InvalidateRect(h,NULL,FALSE);return 0;}break;case WM_LBUTTONUP:{RECT r;GetClientRect(h,&r);float x=GET_X_LPARAM(lp)/dpi,y=GET_Y_LPARAM(lp)/dpi,w=W(r)/dpi,hh=H(r)/dpi;if(x>w-220&&y<84){ShowWindow(h,SW_HIDE);return 0;}if(y>=hh-138&&y<=hh-70){bool active=app->engine->countdownActive||app->engine->stopwatchRunning||app->engine->StopwatchMs()>0;if(active){if(x>=w/2-206&&x<w/2-4){if(app->engine->countdownActive)app->engine->PauseCountdown();else app->engine->ToggleStopwatch();}else if(x>=w/2+12&&x<w/2+196){if(app->engine->countdownActive)app->engine->CancelCountdown();else{app->engine->ResetStopwatch();app->laps.clear();}}}else{int n=(int)((x-(w/2-368))/188);if(x>=w/2-368&&n<4){if(n==3)app->engine->ToggleStopwatch();else{int m[]={5,10,40};app->engine->StartCountdown(m[n]*60000LL);}}}InvalidateRect(h,NULL,FALSE);}return 0;}}}catch(...){if(app)app->Notify(L"操作未完成，请重试。");return 0;}return DefWindowProcW(h,msg,wp,lp);}
} // namespace
int WINAPI wWinMain(HINSTANCE hi,HINSTANCE,LPWSTR,int){instance=hi;SetProcessDPIAware();HDC dc=GetDC(NULL);dpi=GetDeviceCaps(dc,LOGPIXELSX)/96.0f;ReleaseDC(NULL,dc);INITCOMMONCONTROLSEX cc={sizeof(cc),ICC_STANDARD_CLASSES|ICC_DATE_CLASSES|ICC_BAR_CLASSES};InitCommonControlsEx(&cc);int argc=0;LPWSTR*argv=CommandLineToArgvW(GetCommandLineW(),&argc);bool safe=false,smoke=false,silent=false,exit=false;for(int i=1;i<argc;i++){std::wstring a=argv[i];if(a==L"--test-ui")safe=true;if(a==L"--smoke-test")safe=smoke=true;if(a==L"--silent")silent=true;if(a==L"--exit")exit=true;}LocalFree(argv);if(exit){HANDLE e=OpenEventW(EVENT_MODIFY_STATE,FALSE,L"Local\\FreeIsland.Win7.Exit");if(e){SetEvent(e);CloseHandle(e);}return 0;}HANDLE mutex=CreateMutexW(NULL,TRUE,safe?L"Local\\FreeIsland.Win7.Test.Instance":L"Local\\FreeIsland.Win7.Instance");if(GetLastError()==ERROR_ALREADY_EXISTS){if(!silent){HWND w=FindWindowW(L"FreeIslandWin7.Main",L"浮岛 · Win7 原生版");if(w)PostMessageW(w,WM_APP+2,0,0);}CloseHandle(mutex);return 0;}GdiplusStartupInput input;ULONG_PTR token;GdiplusStartup(&token,&input,NULL);int code=0;try{app=new App(safe,smoke);app->Initialize(silent);MSG msg={};while(GetMessageW(&msg,NULL,0,0)>0){if(app->main&&IsDialogMessageW(app->main,&msg))continue;TranslateMessage(&msg);DispatchMessageW(&msg);}code=(int)msg.wParam;delete app;app=NULL;}catch(const std::exception& error){if(smoke){std::wstring path=ExeDir()+L"\\smoke-error.txt";HANDLE log=CreateFileW(path.c_str(),GENERIC_WRITE,0,NULL,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,NULL);if(log!=INVALID_HANDLE_VALUE){DWORD written=0;WriteFile(log,error.what(),(DWORD)strlen(error.what()),&written,NULL);CloseHandle(log);}}else MessageBoxW(NULL,L"浮岛启动未完成，请检查应用目录是否可访问。",L"浮岛 Win7",MB_ICONERROR);if(app){delete app;app=NULL;}code=1;}GdiplusShutdown(token);ReleaseMutex(mutex);CloseHandle(mutex);return code;}
