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
inline void Paint(Graphics& g,float x,float y,float w,float h,float radius,int mode,
                  float light=.35f,bool accent=false){
    if(w<=2||h<=2)return;
    GraphicsPath shape;Path(shape,x,y,w,h,radius);
    if(mode<=0){SolidBrush flat(accent?Color(255,79,102,232):Color(255,255,255,255));g.FillPath(&flat,&shape);return;}
    const bool water=mode==2;
    // This low-alpha neutral tint transmits the actual desktop without blur.
    Color colors[]={Color(52,245,249,253),Color(32,229,237,246),Color(40,234,241,249),Color(54,249,252,255)};
    if(accent){colors[1]=Color(40,217,231,248);colors[2]=Color(46,222,235,250);}
    REAL positions[]={0,.32f,.76f,1};
    LinearGradientBrush body(PointF(x,y),PointF(x,y+h),colors[0],colors[3]);
    body.SetInterpolationColors(colors,positions,4);g.FillPath(&body,&shape);
    light=std::max(0.0f,std::min(1.0f,light));
    LinearGradientBrush rim(PointF(x+w*(water?light*.35f:.12f),y),PointF(x+w*.65f,y+h),
        Color(water?210:185,255,255,255),Color(water?105:85,42,58,76));
    Pen edge(&rim,water?1.0f:.85f);g.DrawPath(&edge,&shape);
    GraphicsPath inner;Path(inner,x+1.3f,y+1.3f,w-2.6f,h-2.6f,std::max(1.0f,radius-1.3f));
    LinearGradientBrush thickness(PointF(x,y),PointF(x,y+h),Color(water?42:20,25,43,66),Color(water?130:82,255,255,255));
    Pen inside(&thickness,water?1.1f:.65f);g.DrawPath(&inside,&inner);
    if(water){
        Pen glint(Color(190,255,255,255),.85f);glint.SetStartCap(LineCapRound);glint.SetEndCap(LineCapRound);
        g.DrawArc(&glint,x+1.4f,y+1.4f,radius*2-2.8f,radius*2-2.8f,196.0f,48.0f);
    }
}
}
