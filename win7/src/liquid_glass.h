#pragma once
#include <windows.h>
#include <gdiplus.h>
#include <algorithm>
#include <cstring>

// Glass is local vector material. Lite never samples the desktop or starts an animation timer.
namespace fiGlass {
using namespace Gdiplus;
inline void Path(GraphicsPath& p,float x,float y,float w,float h,float r){
    r=std::max(1.0f,std::min(r,std::min(w,h)/2));
    p.AddArc(x,y,r*2,r*2,180,90);p.AddArc(x+w-r*2,y,r*2,r*2,270,90);
    p.AddArc(x+w-r*2,y+h-r*2,r*2,r*2,0,90);p.AddArc(x,y+h-r*2,r*2,r*2,90,90);p.CloseFigure();
}
inline void Paint(Graphics& g,float x,float y,float w,float h,float radius,int mode,
                  float light=.35f,bool accent=false,HRGN blurRegion=NULL){
    if(w<=2||h<=2)return;
    const bool full=mode==2;
    GraphicsPath shape;Path(shape,x,y,w,h,radius);
    if(mode<=0){SolidBrush flat(accent?Color(255,79,102,232):Color(255,255,255,255));g.FillPath(&flat,&shape);return;}
    if(full&&blurRegion){Region region(&shape);HRGN part=region.GetHRGN(&g);if(part){CombineRgn(blurRegion,blurRegion,part,RGN_OR);DeleteObject(part);}}
    if(full){
        for(int i=3;i>=1;--i){GraphicsPath shadow;Path(shadow,x-i*.6f,y+2-i*.4f,w+i*1.2f,h+i*.8f,radius+i*.6f);SolidBrush shade(Color(6,24,43,77));g.FillPath(&shade,&shadow);}
    }
    Color colors[4];REAL positions[]={0,.38f,.72f,1};
    if(accent){colors[0]=Color(235,112,162,255);colors[1]=Color(full?207:239,67,108,232);colors[2]=Color(full?216:245,49,78,194);colors[3]=Color(238,112,145,238);}
    else{colors[0]=Color(full?218:238,253,255,255);colors[1]=Color(full?184:224,230,241,253);colors[2]=Color(full?203:231,224,234,249);colors[3]=Color(full?229:244,250,253,255);}
    LinearGradientBrush body(PointF(x,y),PointF(x+w*.14f,y+h),colors[0],colors[3]);body.SetInterpolationColors(colors,positions,4);g.FillPath(&body,&shape);
    GraphicsState saved=g.Save();g.SetClip(&shape,CombineModeIntersect);
    if(full){
        // Curved light bands simulate a thick refractive rim. They move only with local interaction.
        light=std::max(0.0f,std::min(1.0f,light));
        LinearGradientBrush reflected(PointF(x,y),PointF(x,y+h*.75f),Color(100,255,255,255),Color(0,255,255,255));
        g.FillEllipse(&reflected,x-w*.38f+light*w*.34f,y-h*.68f,w*1.42f,h*1.16f);
        LinearGradientBrush lower(PointF(x,y+h*.70f),PointF(x,y+h),Color(0,207,229,255),Color(100,237,248,255));
        g.FillEllipse(&lower,x+w*.08f,y+h*.77f,w*1.14f,h*.45f);
        GraphicsPath inner;Path(inner,x+2,y+2,w-4,h-4,std::max(1.0f,radius-2));
        LinearGradientBrush innerLight(PointF(x,y),PointF(x,y+h),Color(60,26,55,105),Color(9,255,255,255));Pen lens(&innerLight,1.1f);g.DrawPath(&lens,&inner);
    }
    g.Restore(saved);
    LinearGradientBrush rim(PointF(x,y),PointF(x+w*.22f,y+h),Color(full?250:190,255,255,255),Color(full?105:135,140,167,209));
    Pen edge(&rim,full?1.4f:1.0f);g.DrawPath(&edge,&shape);
    if(full){Pen glint(Color(accent?200:235,255,255,255),1.15f);glint.SetStartCap(LineCapRound);glint.SetEndCap(LineCapRound);g.DrawArc(&glint,x+1.7f,y+1.7f,radius*2-3.4f,radius*2-3.4f,191.0f,63.0f);}
}

// Win7 Aero has a documented blur API. Windows 8+ deliberately uses the local material only.
// Region updates never hide/re-show the window, and never cover gaps between radial choices.
class AeroBlur {
    struct BlurBehind { DWORD flags;BOOL enable;HRGN region;BOOL transition; };
    typedef HRESULT (WINAPI* Enabled)(BOOL*);
    typedef HRESULT (WINAPI* Apply)(HWND,const BlurBehind*);
    HMODULE library=NULL;Enabled enabled=NULL;Apply apply=NULL;HRGN last=NULL;bool active=false;
public:
    AeroBlur(){OSVERSIONINFOW version={};version.dwOSVersionInfoSize=sizeof(version);GetVersionExW(&version);if(version.dwMajorVersion!=6||version.dwMinorVersion!=1)return;
        wchar_t system[MAX_PATH]={};UINT n=GetSystemDirectoryW(system,MAX_PATH);if(!n||n>=MAX_PATH-12)return;wcscat(system,L"\\dwmapi.dll");library=LoadLibraryW(system);
        if(library){FARPROC check=GetProcAddress(library,"DwmIsCompositionEnabled"),paint=GetProcAddress(library,"DwmEnableBlurBehindWindow");static_assert(sizeof(check)==sizeof(enabled)&&sizeof(paint)==sizeof(apply),"Windows function pointer size");std::memcpy(&enabled,&check,sizeof(enabled));std::memcpy(&apply,&paint,sizeof(apply));}}
    ~AeroBlur(){if(last)DeleteObject(last);if(library)FreeLibrary(library);}
    void Invalidate(){if(last){DeleteObject(last);last=NULL;}}
    void Set(HWND window,bool wanted,HRGN region){
        if(!enabled||!apply)return;
        BOOL composition=FALSE;RECT bounds={};int shape=region?GetRgnBox(region,&bounds):NULLREGION;
        bool use=wanted&&(shape==SIMPLEREGION||shape==COMPLEXREGION)&&SUCCEEDED(enabled(&composition))&&composition;
        if(use==active&&(!use||(last&&region&&EqualRgn(last,region))))return;
        BlurBehind value={1|2,use?TRUE:FALSE,region,FALSE};if(FAILED(apply(window,&value)))return;active=use;
        if(last){DeleteObject(last);last=NULL;}if(use&&region){last=CreateRectRgn(0,0,0,0);CombineRgn(last,region,NULL,RGN_COPY);}
    }
};
}
