#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_GLOBAL_UBO
#define USE_LIGHT_SSBO
#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 outColor;

layout(binding = 0) uniform sampler2D gColor;
layout(binding = 1) uniform sampler2D gNormal;
layout(binding = 2) uniform sampler2D gGMF;
layout(binding = 3) uniform sampler2D gPosition;
layout(binding = 4) uniform samplerCube cubeMap;
layout(binding = 5) uniform lowp sampler2D lut;
layout(binding = 6) uniform lowp sampler2D env_brdf_lut;

uniform float mapMaxHeight;
uniform float mapMinHeight;
uniform float MEAN;

uniform mat4 ProjectionMatrix;

uniform vec3 LightPos;
uniform vec3 fog_tint;

uniform float AMBIENT;
uniform float BRIGHTNESS;
uniform float SPECULAR;
uniform float GRAY_LEVEL;
uniform float GAMMA_LEVEL;
uniform float fog_level;

uniform vec3 ambientColorForward;
uniform vec3 sunColor;
uniform int  light_count;

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
            
            //Mix in our water color
            //color_in.rgb = mix(color_in.rgb, waterColor, color_in.a);

            //fog level... this should be on the controller
            float fog_alpha = 0.5;

            vec3 GM_in = texture(gGMF, fs_in.UV).xya;

            //water overides GM values
            GM_in.rg = mix(GM_in.rg,vec2(0.4,0.8), color_in.a);

            vec3 LightPosModelView = LightPos.xyz;
           
            vec3 L = normalize(LightPosModelView-Position.xyz); // light direction

            vec3 N = normalize(texture(gNormal, fs_in.UV).xyz);

            float POWER;
            float INTENSITY;

            float metal = GM_in.r;

            if (FLAG == 64 || FLAG == 128) {
                //---------------------------------------------
                // Poor mans PBR :)
                // how shinny this is
                POWER = max(GM_in.r * 30.0, 3.0);
                INTENSITY = GM_in.g;
                // How metalic his is
                color_in.rgb = mix(color_in.rgb,
                                   color_in.rgb * vec3(0.04), max( metal * 0.25 , 0.00) );
                //---------------------------------------------

            }
            vec4 final_color = vec4(0.25, 0.25, 0.25, 1.0) * color_in ;

            vec4 Ambient_level = color_in * vec4(AMBIENT * 3.0);

            Ambient_level.rgb *= ambientColorForward;

            float dist = length(LightPosModelView - Position);
            float cutoff = 10000.0;
            vec4 color = mix(vec4(sunColor,0.0),vec4(0.5),0.6);

            vec4 t_cam = view * vec4(cameraPos,1.0);
            vec3 V = normalize(t_cam.xyz-Position);

            float perceptualRoughness = 0.2;
            
            //create a up facing normal that translates properly.
            vec3 blank_n = mat3(inverse(transpose(view))) * normalize(vec3(0.0, 1.0, 0.0));

            float water_mix = color_in.a;

            // Only light whats in range
            if (dist < cutoff) {
                // kill the terrian normals where there is water
                N = mix(N, blank_n, water_mix*0.7);
                vec3 R = reflect(-L,N);

                float lambertTerm = pow(max(dot(N, L),0.001),GM_in.g);

                float water_spec = max(pow(dot(V,R), 120.0 ),0.0001) * SPECULAR;

                final_color.xyz += max(lambertTerm * color_in.xyz * color.xyz ,0.0);



                vec3 halfwayDir = normalize(L + V);

                float spec = max(pow(dot(V,R), POWER ),0.0000) * SPECULAR * INTENSITY;
   
                R.xz *= -1.0;

                vec4 brdf = SRGBtoLINEAR( texture2D( env_brdf_lut,
                            vec2(1.0-lambertTerm * 0.25, 1.0-metal) ));
                vec3 specular =  (vec3(spec) * brdf.x + brdf.y);


                vec4 prefilteredColor = SRGBtoLINEAR(textureLod(cubeMap, R,
                                        max(8.0-GM_in.g *4.0, 0.0)));
                // GM_in.b is the alpha channel.
                prefilteredColor.rgb = mix(vec3(specular), prefilteredColor.rgb +
                                       specular, GM_in.b*0.2*(1.0-color_in.a));

                vec4 W_prefilteredColor = SRGBtoLINEAR(textureLod(cubeMap, R,
                                          max(8.0-water_mix *5.0, 0.0)));

                vec4 G_prefilteredColor = SRGBtoLINEAR(textureLod(cubeMap, R,
                                          max(3.0-GM_in.z *3.0, 0.0)))*GM_in.z*spec*4.0;

                vec3 water_reflect = vec3(water_mix*ambientColorForward) * vec3(water_spec)*1.5 * W_prefilteredColor.rgb;

                final_color.xyz += clamp(water_reflect+specular+G_prefilteredColor.xyz,0.0,1.0);
                //final_color.xyz += spec;
                // Fade to ambient over distance

                final_color = mix(final_color,Ambient_level,dist/cutoff) * BRIGHTNESS;
                final_color = lut_color_correction( final_color );

            } else {
                final_color = Ambient_level * BRIGHTNESS;
            }
            //final_color.r = color_in.a;
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
            vec4 ts_cam = view * vec4(cameraPos,1.0);
            vec4 p = inverse(view) * vec4(Position.xyz,1.0);
            float viewDistance = length(ts_cam.xyz - Position);
            float z = viewDistance*0.75 ; 
   
            float height = 0.0;
           
            if( p.y <= MEAN ){
            
            height = 1.0-(p.y + -mapMinHeight) / (-mapMinHeight + MEAN);
            height = sin(1.5708*height); // change to a curve to improve depth.
            }

            const float LOG2 = 1.442695;


            //if (flag ==160) {z*=0.75;}//cut fog level down if this is water.
            float fog_density = 0.005;

            float density = (fog_density * height ) * 0.75;
            float fogFactor = exp2(-density * density * z * z * LOG2);
            fogFactor = clamp(fogFactor, 0.0, 1.0);

            vec4 f_color =  vec4(fog_tint,0.0) * 1.5 * fog_alpha;


            final_color = mix(final_color, f_color,(1.0- fogFactor)*fog_level);
            //final_color.r = outColor.a;
            /*===================================================================*/
            // Small Map Lights
           if (light_count >1000){

                //final_color*=0.5;
                vec4 summed_lights;

                for (int i = 0; i < light_count; i++){

                    vec4 lp = view * vec4(lights[i].location,1.0);

                    float dist = length(lp.rgb - Position);
                    float radius = 10.0;
                    
                    if (dist < radius) {
                 
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
            outColor =  correct(final_color,1.4,1.2)*1.6;
            //outColor.a = fogFactor;
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