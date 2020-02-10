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

in vec3 CameraPos;
in vec2 UV;

in mat4 projMatrixInv;

// Functions ///////////////////////////////////////

float linearDepth(float depthSample)
{
    float f = 5000.0;
    float n = 1.0;
    
    //depthSample = 2.0 * depthSample - 1.0;
    //float zLinear = 2.0 * zNear * zFar / (zFar + zNear - depthSample * (zFar - zNear));
    return  (2.0 * n) / (f + n - depthSample * (f - n));
}

////////////////////////////////////////////////////


void main (void)
{
    float depth = texture(gDepth, UV).x*2.0-1.0;

    //--------------------------------------------------------------------
    if (depth == 1.0) discard;// nothing there


    vec3 Position = texture(gPosition, UV).xyz;

	vec4 tex01_color = texture(gColor, UV);

    vec3 LightPosModelView = LightPos.xyz;

    //lighting calculations
    vec3 vd = normalize(-Position);//view direction
    vec3 L = normalize(LightPosModelView-Position.xyz); // light direction

    vec3 N = normalize(texture(gNormal,UV).xyz);

    float abm = 0.55;
    vec4 final_color = vec4(abm, abm, abm, 1.0) * tex01_color;
    vec4 Ambient = final_color;

    float dist = length(LightPosModelView - Position);
    float cutoff = 2000.0;
    vec4 color = vec4(1.0, 0.4, 0.4, 1.0);
    float specular;
    //only light whats in range
    if (dist < cutoff) {

		    float lambertTerm = max(dot(N, L),0.0);
            final_color.xyz += max(lambertTerm * tex01_color.xyz*color.xyz,0.0)*3.0;;

            vec3 halfwayDir = normalize(L + vd);

            specular = pow(max(dot(N, halfwayDir), 0.0), 60.0) * 0.3;
            final_color.xyz += specular;

            // Fade out over distince
            final_color = mix(final_color,Ambient,dist/cutoff);
        
    }
    float d = linearDepth(depth);
    //-------------------------------------------------------------------
    // test crap..
    //final_color.xyz = final_color.xyz*0.01+d;
    //final_color.xyz = final_color.xyz*0.01+(Position);
    //final_color.xyz = tex01_color.xyz*0.1+N*0.5+0.5;
    //-------------------------------------------------------------------
    outColor =  final_color;//+color*0.2;
    outColor.a = 1.0;
}
