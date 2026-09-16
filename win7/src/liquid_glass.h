#pragma once
#include <windows.h>
#include <gdiplus.h>
#include <algorithm>
#include <cmath>

// Transparent local material only. Windows 7 does not provide desktop refraction.
// No capture, Aero blur, broad white reflections, or autonomous light animation.
namespace fiGlass {
using namespace Gdiplus;
inline void Path(GraphicsPath& p,float x,float y,float w,float h,float r){
    r=std::max(1.0f,std::min(r,std::min(w,h)/2));
    p.AddArc(x,y,r*2,r*2,180,90);p.AddArc(x+w-r*2,y,r*2,r*2,270,90);
    p.AddArc(x+w-r*2,y+h-r*2,r*2,r*2,0,90);p.AddArc(x,y+h-r*2,r*2,r*2,90,90);p.CloseFigure();
}
inline float Rebound(float progress){
    float t=std::max(0.0f,std::min(1.0f,progress));
    return (1-t)*(1-t)*std::cos(t*4.25f);
}
inline float Unit(int value){return std::max(0,std::min(100,value))/100.0f;}
inline BYTE Alpha(float value){return (BYTE)std::max(0.0f,std::min(245.0f,std::round(value)));}
// Material alpha never applies to separately painted text or its local backing.
inline BYTE TintAlpha(float baseline,int transparency){
    float t=Unit(transparency);
    return Alpha(t<=.65f?235+(baseline-235)*(t/.65f):baseline*(1-t)/.35f);
}
inline BYTE HighlightAlpha(float baseline,int highlight){
    return Alpha(baseline*(.15f+Unit(highlight)*(.85f/.55f)));
}
// Pixel-sized droplets need their own inset rim: the larger surface's nested
// rounded paths would overlap or escape the visible bounds at 3-6 physical px.
inline void PaintDot(Graphics& g,float x,float y,float diameter,int mode,float squeeze=0,
                     int refraction=50,int transparency=65,int highlight=55){
    if(diameter<3)return;
    GraphicsState saved=g.Save();g.SetClip(RectF(x,y,diameter,diameter),CombineModeIntersect);
    if(mode<=0){SolidBrush black(Color(255,0,0,0));g.FillEllipse(&black,x,y,diameter,diameter);g.Restore(saved);return;}
    const float curve=Unit(refraction);
    if(mode==2&&diameter>=6){float compression=.025f*std::max(0.0f,std::min(1.0f,squeeze));g.TranslateTransform(x+diameter/2,y+diameter/2);g.ScaleTransform(1,1-compression);g.TranslateTransform(-x-diameter/2,-y-diameter/2);}
    Color colors[]={Color(TintAlpha(52,transparency),245,249,253),Color(TintAlpha(32,transparency),229,237,246),Color(TintAlpha(40,transparency),234,241,249),Color(TintAlpha(54,transparency),249,252,255)};
    REAL positions[]={0,.32f,.76f,1};
    LinearGradientBrush body(PointF(x,y),PointF(x,y+diameter),colors[0],colors[3]);body.SetInterpolationColors(colors,positions,4);
    g.FillEllipse(&body,x,y,diameter,diameter);
    float stroke=std::min(.45f+.80f*curve,diameter*(.15f+.10f*curve)),inset=stroke/2+.08f;
    LinearGradientBrush rim(PointF(x,y),PointF(x+diameter*(.48f+.28f*curve),y+diameter),Color(HighlightAlpha(mode==2?210:185,highlight),255,255,255),Color(Alpha((mode==2?130:105)*(.75f+.50f*curve)),32,46,65));
    Pen edge(&rim,stroke);g.DrawEllipse(&edge,x+inset,y+inset,diameter-2*inset,diameter-2*inset);
    if(mode==2&&diameter>=6){Pen reflection(Color(HighlightAlpha(170,highlight),255,255,255),.35f+.40f*curve);reflection.SetStartCap(LineCapRound);reflection.SetEndCap(LineCapRound);g.DrawArc(&reflection,x+1,y+1,diameter-2,diameter-2,196.0f,36.0f+32*curve);}
    g.Restore(saved);
}
inline void Paint(Graphics& g,float x,float y,float w,float h,float radius,int mode,
                  float light=.35f,bool accent=false,int refraction=50,int transparency=65,int highlight=55){
    if(w<=2||h<=2)return;
    GraphicsPath shape;
    if(mode<=0){Path(shape,x,y,w,h,radius);SolidBrush flat(accent?Color(255,79,102,232):Color(255,255,255,255));g.FillPath(&flat,&shape);return;}
    const bool water=mode==2;const float curve=Unit(refraction);
    radius=std::max(1.0f,std::min(radius*(.82f+.36f*curve),std::min(w,h)/2));Path(shape,x,y,w,h,radius);
    // This low-alpha neutral tint transmits the actual desktop without blur.
    Color colors[]={Color(TintAlpha(52,transparency),245,249,253),Color(TintAlpha(32,transparency),229,237,246),Color(TintAlpha(40,transparency),234,241,249),Color(TintAlpha(54,transparency),249,252,255)};
    if(accent){colors[1]=Color(TintAlpha(40,transparency),217,231,248);colors[2]=Color(TintAlpha(46,transparency),222,235,250);}
    REAL positions[]={0,.32f,.76f,1};
    LinearGradientBrush body(PointF(x,y),PointF(x,y+h),colors[0],colors[3]);
    body.SetInterpolationColors(colors,positions,4);g.FillPath(&body,&shape);
    light=std::max(0.0f,std::min(1.0f,light));
    LinearGradientBrush rim(PointF(x+w*(water?light*.35f:.12f),y),PointF(x+w*.65f,y+h),
        Color(HighlightAlpha(water?210:185,highlight),255,255,255),Color(Alpha((water?105:85)*(.75f+.50f*curve)),42,58,76));
    Pen edge(&rim,(water?.45f:.40f)+curve*(water?1.10f:.90f));g.DrawPath(&edge,&shape);
    float inset=.65f+1.30f*curve;
    if(w>2*inset+2&&h>2*inset+2){
        GraphicsPath inner;Path(inner,x+inset,y+inset,w-2*inset,h-2*inset,std::max(1.0f,radius-inset));
        LinearGradientBrush thickness(PointF(x,y),PointF(x,y+h),Color(Alpha((water?42:20)*(.45f+1.10f*curve)),25,43,66),Color(HighlightAlpha(water?130:82,highlight),255,255,255));
        Pen inside(&thickness,(water?.40f:.30f)+curve*(water?1.40f:.70f));g.DrawPath(&inside,&inner);
    }
    if(water&&radius>1.4f){
        Pen glint(Color(HighlightAlpha(190,highlight),255,255,255),.45f+.80f*curve);glint.SetStartCap(LineCapRound);glint.SetEndCap(LineCapRound);
        g.DrawArc(&glint,x+1.4f,y+1.4f,radius*2-2.8f,radius*2-2.8f,196.0f,32.0f+32*curve);
    }
}
}
