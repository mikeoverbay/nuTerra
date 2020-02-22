// Deferred lighting fragment shader.
#version 430 core

out vec4 outColor;

uniform sampler2D gColor;
uniform sampler2D gNormal;
uniform sampler2D gGMF;
uniform sampler2D gPosition;
uniform sampler2D gDepth;

uniform mat4 ProjectionMatrix;

uniform vec3 LightPos;

uniform float AMBIENT;
uniform float BRIGHTNESS;
uniform float SPECULAR;
uniform float GRAY_LEVEL;
uniform float GAMMA_LEVEL;

in vec3 CameraPos;
in vec2 UV;

in mat4 projMatrixInv;

/*========================== FUNCTIONS =============================*/
// This is  atest for making sure we actually are getting the depth.
float linearDepth(float depthSample)
{
    float f = 5000.0;
    float n = 1.0;
    return  (2.0 * n) / (f + n - depthSample * (f - n));
}
// This helps to even out overall levels of brightness and adjusts gamma.
vec4 correct(in vec4 hdrColor, in float exposure){  
    // Exposure tone mapping
    vec3 mapped = vec3(1.0) - exp(-hdrColor.rgb * exposure);
    // Gamma correction 
    mapped.rgb = pow(mapped.rgb, vec3(1.0 / GAMMA_LEVEL));  
    return vec4 (mapped, 1.0);
}
/*===================================================================*/


void main (void)
{
    float depth = texture(gDepth, UV).x*2.0-1.0;

    //if (depth == 1.0) discard;// nothing there

    int FLAG = int( texture(gGMF, UV).b * 255.0);
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
    vec3 Position = texture(gPosition, UV).xyz;

    vec4 color_in = texture(gColor, UV);

    vec3 LightPosModelView = LightPos.xyz;

    // lighting calculations
    vec3 vd = normalize(-Position);//view direction
    vec3 L = normalize(LightPosModelView-Position.xyz); // light direction

    vec3 N = normalize(texture(gNormal,UV).xyz);

    vec4 final_color = vec4(0.5, 0.5, 0.5, 1.0) * color_in;
    vec4 Ambient_level = color_in * vec4(AMBIENT);

    float dist = length(LightPosModelView - Position);
    float cutoff = 2000.0;
    vec4 color = vec4(0.5, 0.5, 0.5, 1.0);
    // Only light whats in range
    if (dist < cutoff) {

            float lambertTerm = max(dot(N, L),0.0);
            final_color.xyz += max(lambertTerm * color_in.xyz*color.xyz,0.0)*3.0;;

            vec3 halfwayDir = normalize(L + vd);

            final_color.xyz += pow(max(dot(N, halfwayDir), 0.0), 60.0) * SPECULAR;

            // Fade to ambient over distance
            final_color = mix(final_color,Ambient_level,dist/cutoff) * BRIGHTNESS;

    }else{
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
    outColor =  correct(final_color,4.0);
    outColor.a = 1.0;
    /*===================================================================*/
	 //if flag != 128
		}else{
	    outColor = texture(gColor, UV) * BRIGHTNESS;
		}
     // if flag != 0
    }else{
    outColor = texture(gColor, UV);
    }

    outColor.a = 1.0;
}
