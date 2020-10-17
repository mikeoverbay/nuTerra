//Decals color pass.
#version 450 core

layout (location = 0) out vec4 gColor;


uniform sampler2D gFlag;
uniform sampler2D depthMap;
uniform sampler2D colorMap;
uniform vec3 color_in;


in vec4 positionSS; // screen space
in mat4 inverseProject;
in mat4 inverseModel;
in vec4 worldPos;

const vec3 tr = vec3 (0.5 ,0.5 , 0.5);
const vec3 bl = vec3(-0.5, -0.5, -0.5);

void clip(vec3 v){
    if (v.x > tr.x || v.x < bl.x ) discard;
    if (v.y > tr.y || v.y < bl.y ) discard;
    if (v.z > tr.z || v.z < bl.z ) discard;
}


vec2 postProjToScreen(vec4 position)
{
    vec2 screenPos = position.xy / position.w;
    return 0.5 * (vec2(screenPos.x, screenPos.y) + 1);
}


void main(){
    // Calculate UVs
    vec2 UV = postProjToScreen(positionSS);
    /*==================================================*/
      int flag = int(texture(gFlag, UV.xy).r * 255);
     if (flag == 64)  { discard;}

     //if (flag == 96) { discard;}

     //if (flag != 128)  { discard;}
       
    /*==================================================*/
    // sample the Depth from the Depthsampler
    float Depth = texture(depthMap, UV).x;

    // Calculate Worldposition by recreating it out of the coordinates and depth-sample
    vec4 ScreenPosition;
    ScreenPosition.xy = UV * 2.0 - 1.0;
    ScreenPosition.z = (Depth);
    ScreenPosition.w = 1.0f;

    // Transform position from screen space to world space
    vec4 WorldPosition = inverseProject * ScreenPosition ;
    WorldPosition.xyz /= WorldPosition.w;
    WorldPosition.w = 1.0f;
    // trasform to decal original and size.
    // 1 x 1 x 1
    WorldPosition = inverseModel * WorldPosition;
    clip (WorldPosition.xyz);


    /*==================================================*/
    //Get texture UVs
    WorldPosition.xy += 0.5;
    WorldPosition.y *= 1.0;

    vec4 color = texture(colorMap, WorldPosition.xy);
	color.xyz += color_in;
    if (color.a < 0.05) { discard; }
    gColor = color;

    }
