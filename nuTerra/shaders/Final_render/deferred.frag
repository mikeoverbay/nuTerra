#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_LIGHT_SSBO
#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

out vec4 outColor;

uniform sampler2D gColor;
uniform sampler2D gNormal;
uniform sampler2D gGMF;
uniform sampler2D gPosition;
uniform sampler2D gDepth;
uniform samplerCube cubeMap;
uniform lowp sampler2D lut;
uniform lowp sampler2D env_brdf_lut;

uniform float mapMaxHeight;
uniform float mapMinHeight;
uniform float MEAN;

uniform mat4 ProjectionMatrix;

uniform vec3 LightPos;

uniform float AMBIENT;
uniform float BRIGHTNESS;
uniform float SPECULAR;
uniform float GRAY_LEVEL;
uniform float GAMMA_LEVEL;

uniform vec3 ambientColorForward;
uniform vec3 sunColor;
uniform int light_count ;

#define MAXCOLOR 15.0
#define COLORS 16.0
#define WIDTH 256.0
#define HEIGHT 16.0

layout (std140, binding = 7) buffer Lights {
    light lights[250];
};


in VS_OUT {
    vec2 UV;
} fs_in;


/*========================== FUNCTIONS =============================*/
// This is  atest for making sure we actually are getting the depth.
float linearDepth(float depthSample)
{
    float f = 5000.0;
    float n = 1.0;
    return  (2.0 * n) / (f + n - depthSample * (f - n));
}
// This helps to even out overall levels of brightness and adjusts gamma.
vec4 correct(in vec4 hdrColor, in float exposure, in float gamma_level){  
    // Exposure tone mapping
    vec3 mapped = vec3(1.0) - exp(-hdrColor.rgb * exposure);
    // Gamma correction 
    mapped.rgb = pow(mapped.rgb, vec3(1.0 / gamma_level));  
    mapped.rgb = pow(mapped.rgb, vec3(1.0 / GAMMA_LEVEL*0.5));  
    return vec4 (mapped, hdrColor.a);
}

 // https://defold.com/tutorials/grading/
 vec4 lut_color_correction(in vec4 px)
 {
    float cell = px.b * MAXCOLOR;

    float cell_l = floor(cell);
    float cell_h = ceil(cell);

    float half_px_x = 0.5 / WIDTH;
    float half_px_y = 0.5 / HEIGHT;
    float r_offset = half_px_x + px.r / COLORS * (MAXCOLOR / COLORS);
    float g_offset = half_px_y + px.g * (MAXCOLOR / COLORS);

    vec2 lut_pos_l = vec2(cell_l / COLORS + r_offset, g_offset);
    vec2 lut_pos_h = vec2(cell_h / COLORS + r_offset, g_offset);

    vec4 graded_color_l = textureLod(lut, lut_pos_l, 0);
    vec4 graded_color_h = textureLod(lut, lut_pos_h, 0);

    vec4 graded_color = mix(graded_color_l, graded_color_h, fract(cell));

    return graded_color;
 
 }
/*===================================================================*/
#define MANUAL_SRGB ;
vec4 SRGBtoLINEAR(vec4 srgbIn)
{
    #ifdef MANUAL_SRGB
    #ifdef SRGB_FAST_APPROXIMATION
    vec3 linOut = pow(srgbIn.xyz,vec3(2.2));
    #else //SRGB_FAST_APPROXIMATION
    vec3 bLess = step(vec3(0.04045),srgbIn.xyz);
    vec3 linOut = mix( srgbIn.xyz/vec3(12.92), pow((srgbIn.xyz+vec3(0.055))/vec3(1.055),vec3(2.4)), bLess );
    #endif //SRGB_FAST_APPROXIMATION
    return vec4(linOut,srgbIn.w);
    ;
    #else //MANUAL_SRGB
    return srgbIn;
    #endif //MANUAL_SRGB
}


void main (void)
{
    float depth = texture(gDepth, fs_in.UV).x;

    //if (depth == 1.0) discard;// nothing there

    const uint FLAG = uint( texture(gGMF, fs_in.UV).b * 255.0);
    // Writen as a float in shaders as f = Flag_value/255.0
    // or just 0.0 to mask any shading.
    //
    // If FLAG = 0 we want NO shading done to the color.
    // Models = 64
    // Dome = 255
    // Just output gColor to outColor;
    if (FLAG != 000) {
        // FLAG VALUES WILL BE DECIDED AS WE NEED THEM BUT..
        // ZERO = JUST PASS THE COLOR TO OUTPUT
        if (FLAG != 255) {
            vec3 Position = texture(gPosition, fs_in.UV).xyz;

            vec4 color_in = texture(gColor, fs_in.UV);
            
            float fog_alpha = color_in.a;

            vec3 GM_in = texture(gGMF, fs_in.UV).xya;
        
            vec3 LightPosModelView = LightPos.xyz;

            // lighting calculations
            vec3 vd = normalize(-Position);//view direction
            vec3 L = normalize(LightPosModelView-Position.xyz); // light direction

            vec3 N = normalize(texture(gNormal, fs_in.UV).xyz);
            float POWER;
            float INTENSITY;

            float metal = GM_in.g;

            if (FLAG == 64 || FLAG == 128) {
                //---------------------------------------------
                // Poor mans PBR :)
                // how shinny this is
                POWER = max(GM_in.r* 60.0,0.5);
                INTENSITY = max(GM_in.r * GM_in.g  ,0.0);
                // How metalic his is
                color_in.rgb = mix(color_in.rgb,
                                   color_in.rgb * vec3(0.04), max( metal * 0.25 , 0.01) );
                //---------------------------------------------

            }
            vec4 final_color = vec4(0.25, 0.25, 0.25, 1.0) * color_in ;

            vec4 Ambient_level = color_in * vec4(AMBIENT * 3.0);

            Ambient_level.rgb *= ambientColorForward;

            float dist = length(LightPosModelView - Position);
            float cutoff = 10000.0;
            vec4 color = vec4(0.36, 0.36, 0.36, 1.0);

            vec3 V = normalize(-Position);

            float perceptualRoughness = 0.2;

            // Only light whats in range
            if (dist < cutoff) {

                float lambertTerm = pow(max(dot(N, L),0.001),GM_in.r);
                final_color.xyz += max(lambertTerm * color_in.xyz * color.xyz * sunColor,0.0);



                vec3 halfwayDir = normalize(L + vd);

                float spec = max(pow(dot(N, halfwayDir), POWER ),0.0001) * SPECULAR * INTENSITY;
   
                vec3 R = reflect(-V,N);
                R.xz *= -1.0;

                vec4 brdf = SRGBtoLINEAR( texture2D( env_brdf_lut, vec2(1.0-lambertTerm*0.45, 1.0-metal) ));
                vec3 specular =  (vec3(spec) * brdf.x + brdf.y);


                vec4 prefilteredColor = SRGBtoLINEAR(textureLod(cubeMap, R,  max(4.0-GM_in.g *4, 0.0)));
                // GM_in.b is the alpha channel.
                prefilteredColor.rgb = mix(vec3(specular), prefilteredColor.rgb + specular, GM_in.b*0.2);
                vec3 refection = prefilteredColor.rgb;

   
                final_color.xyz += refection;
                final_color = lut_color_correction( final_color );
                // Fade to ambient over distance

                final_color = mix(final_color,Ambient_level,dist/cutoff) * BRIGHTNESS;

            } else {
                final_color = Ambient_level * BRIGHTNESS;
            }

            /*===================================================================*/
            /*===================================================================*/
            // Gray level
            vec3 luma = vec3(0.299, 0.587, 0.114);
            vec3 co = vec3(dot(luma, final_color.rgb));
            vec3 c = mix(co, final_color.rgb, GRAY_LEVEL);
            final_color.rgb = c;
            /*===================================================================*/

            // FOG calculation... using distance from camera and height on map.
            // It's a more natural height based fog than plastering the screen with it.
            vec4 t_cam = view * vec4(cameraPos,1.0);
            vec4 p = inverse(view) * vec4(Position.xyz,1.0);
            float viewDistance = length(t_cam.xyz - Position);
            float z = viewDistance ; 
   
            float height = 0.0;
           
            if( p.y <= MEAN ){
            
            height = 1.0-(p.y + -mapMinHeight) / (-mapMinHeight + MEAN);
            height = sin(1.5708*height); // change to a curve to improve depth.
            }

            const float LOG2 = 1.442695;


            //if (flag ==160) {z*=0.75;}//cut fog level down if this is water.
            float fog_density = 0.005;
            vec4 fog_color = vec4 (0.5,0.5,0.7,1.0);

            float density = (fog_density * height ) * 0.75;
            float fogFactor = exp2(-density * density * z * z * LOG2);
            fogFactor = clamp(fogFactor, 0.0, 1.0);
            vec4 f_color =  fog_color * AMBIENT*3.0*fog_alpha;

            vec4 sColor = final_color;

            final_color = mix(f_color, final_color, fogFactor);
            final_color = mix(final_color, sColor, fogFactor);
            final_color.a = fogFactor;
            /*===================================================================*/
            // Small Map Lights
           if (light_count >0){

                //final_color*=0.5;
                vec4 summed_lights;

                for (int i = 0; i < light_count; i++){

                    vec4 lp = view * vec4(lights[i].location,1.0);

                    float dist = length(lp.rgb - Position);
                    float radius = 10.0;
                    
                    if (dist < radius) {
                    //dist *=lights[i].level;
                    vec3 L = normalize(LightPosModelView-Position.xyz); // light direction

                    float lambertTerm = pow(max(dot(N, L),0.001),GM_in.r);
                    summed_lights.rgb = max(lambertTerm * lights[i].color.xyz, 0.0);
                    // Mix in this light by distance
                  
                    float att = clamp(1.0 - dist/radius, 0.0, 1.0); att *= att;
                    final_color.rgb = mix(final_color.rgb, summed_lights.rgb, att);

                    }
                }

            }

            /*===================================================================*/
            // Final Output
            outColor =  correct(final_color,1.9,0.9)*2.25;
            outColor.a = fogFactor;
            /*===================================================================*/
        //if flag != 128
        }else{
            outColor = texture(gColor, fs_in.UV) * BRIGHTNESS;
        }
    // if flag != 0
    } else {
        outColor = texture(gColor, fs_in.UV) * BRIGHTNESS;
    }

    //outColor.a = 1.0;
}
