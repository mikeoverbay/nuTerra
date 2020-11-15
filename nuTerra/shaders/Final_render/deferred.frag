#version 450 core

out vec4 outColor;

uniform sampler2D gColor;
uniform sampler2D gNormal;
uniform sampler2D gGMF;
uniform sampler2D gPosition;
uniform sampler2D gDepth;
uniform samplerCube cubeMap;
uniform lowp sampler2D lut;
uniform lowp sampler2D env_brdf_lut;

uniform mat4 ProjectionMatrix;

uniform vec3 LightPos;

uniform float AMBIENT;
uniform float BRIGHTNESS;
uniform float SPECULAR;
uniform float GRAY_LEVEL;
uniform float GAMMA_LEVEL;

uniform vec3 ambientColorForward;
uniform vec3 sunColor;

#define MAXCOLOR 15.0
#define COLORS 16.0
#define WIDTH 256.0
#define HEIGHT 16.0

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

            vec3 GM_in = texture(gGMF, fs_in.UV).xya;
        
            vec3 LightPosModelView = LightPos.xyz;

            // lighting calculations
            vec3 vd = normalize(-Position);//view direction
            vec3 L = normalize(LightPosModelView-Position.xyz); // light direction

            vec3 N = normalize(texture(gNormal, fs_in.UV).xyz);
            float POWER;
            float INTENSITY;
            if (FLAG == 64 || FLAG == 128) {
                //---------------------------------------------
                // Poor mans PBR :)
                // how shinny this is
                POWER = max(GM_in.r *60.0,5.0);
                INTENSITY = max(GM_in.r ,0.0);
                // How metalic his is
                color_in.rgb = mix(color_in.rgb,
                                   color_in.rgb * vec3(0.04), max(GM_in.g*0.25,0.00) );
                //---------------------------------------------

            }
            vec4 final_color = vec4(0.25, 0.25, 0.25, 1.0) * color_in ;

            vec4 Ambient_level = color_in * vec4(AMBIENT * 3.0);

            Ambient_level.rgb *= ambientColorForward;

            float dist = length(LightPosModelView - Position);
            float cutoff = 10000.0;
            vec4 color = vec4(0.36, 0.36, 0.36, 1.0);

            vec3 V = normalize(-Position);
            float perceptualRoughness = 0.6;

            // Only light whats in range
            if (dist < cutoff) {

                float lambertTerm = pow(max(dot(N, L),0.0),1.0)*1.0;
                final_color.xyz += max(lambertTerm * color_in.xyz * color.xyz * sunColor,0.0);



                vec3 halfwayDir = normalize(L + vd);

                float spec = max(pow(dot(N, halfwayDir), POWER ),0.0001) * SPECULAR * INTENSITY;
   
                vec3 R = reflect(-V,N);
                R.xz *= -1.0;

                vec4 brdf = SRGBtoLINEAR( texture2D( env_brdf_lut, vec2(lambertTerm*1.0, (1.0 - perceptualRoughness)*0.75) ));
                vec3 specular =  (vec3(spec) * brdf.x + brdf.y);


                vec4 prefilteredColor = SRGBtoLINEAR(textureLod(cubeMap, R,  max(5.0-GM_in.g *5, 0.0)));
                // GM_in.b is the alpha channel.
                prefilteredColor.rgb = mix(vec3(specular), prefilteredColor.rgb + specular, GM_in.b*0.3);
                vec3 refection = prefilteredColor.rgb;

   
                final_color.xyz += refection;
                final_color = lut_color_correction( final_color );
                // Fade to ambient over distance
                final_color = mix(final_color,Ambient_level,dist/cutoff) * BRIGHTNESS;
            } else {
                final_color = Ambient_level * BRIGHTNESS;
            }

            /*===================================================================*/
            // Gray level
            vec3 luma = vec3(0.299, 0.587, 0.114);
            vec3 co = vec3(dot(luma, final_color.rgb));
            vec3 c = mix(co, final_color.rgb, GRAY_LEVEL);
            final_color.rgb = c;
            /*===================================================================*/


            /*===================================================================*/
            // Final Output
            outColor =  correct(final_color,1.9,0.9)*2.25;
            outColor.a = 1.0;
            /*===================================================================*/
        //if flag != 128
        }else{
            outColor = texture(gColor, fs_in.UV) * BRIGHTNESS;
        }
    // if flag != 0
    } else {
        outColor = texture(gColor, fs_in.UV) * BRIGHTNESS;
    }

    outColor.a = 1.0;
}
