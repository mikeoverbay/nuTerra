#version 450 core

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gGMF;

uniform vec2 bb_tr;
uniform vec2 bb_bl;
uniform float g_size;
uniform int show_border;
uniform int show_chunks;
uniform int show_grid;

in vec2 uv;
in vec2 V;
void main (void)
{
    int flag = 0;

if (show_chunks==1){
    if (uv.x < 0.005 || uv.x > 0.995 || uv.y < 0.005 || uv.y > 0.995)
    {
        gColor = vec4(0.9,0.9,0.9,0.8);
        flag = 1;
    }
}//show chunks

if (show_grid==1){
    if (V.x+0.38 >= bb_bl.x && V.x+0.38 <= bb_tr.x)
    {
    if (V.y+0.38>= bb_bl.y && V.y+0.38 <= bb_tr.y)
    {
        if (fract(V.x/g_size+0.013) < 0.013){
             gColor = vec4(0.95,0.95,0.0,0.95);
            flag = 1;
        }
        if (fract(V.y/g_size+.013) < 0.013){
            gColor = vec4(0.95,0.95,0.0,0.95);
            flag = 1;
        }
    }
    }
}//show grid

if(show_border==1){
    //X border
    if (V.y +0.28 < bb_tr.y && V.y+1.28 > bb_bl.y){
        if (V.x +0.28 < bb_bl.x && V.x +1.28 > bb_bl.x){
                gColor = vec4(1.0,0.0,0.0,0.95);
                flag = 1;
        }
        if (V.x +0.28 < bb_tr.x && V.x+1.28 > bb_tr.x){
                gColor = vec4(1.0,0.0,0.0,0.95);
                flag = 1;
        }
    }
    //Y border
    if (V.x +0.28 < bb_tr.x && V.x+1.28 > bb_bl.x){
        if (V.y +0.28 < bb_bl.y && V.y+1.28 > bb_bl.y){
                gColor = vec4(1.0,0.0,0.0,0.95);
                flag = 1;
        }
        if (V.y +0.28 < bb_tr.y && V.y+1.28 > bb_tr.y){
               gColor = vec4(1.0,0.0,0.0,0.95);
                flag = 1;
        }
    }

}//show border
 if (flag == 0) {discard;}// nothing to draw so discard.
 gGMF = vec3(0.0);
}// main
