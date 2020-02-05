﻿// Deferred lighting fragment shader.
#version 430 compatibility

out vec4 outColor;

uniform sampler2D gColor;
uniform sampler2D gNormal;
uniform sampler2D gGMF;
uniform sampler2D gDepth;

uniform mat4 ModelMatrix;
uniform mat4 ProjectionMatrix;

uniform vec3 LightPos;

uniform vec2 viewport;

in vec3 CameraPos;
in vec2 UV;

in mat4 projMatrixInv;
in mat4 ModelMatrixInv;

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

    // viewport <---This is the render target size, i.e. what you feed into glViewport
    //--------------------------------------------------------------------
    // Get the world position from the depth buffer and screen poition.
    vec2 screen;
    screen.x = ( gl_FragCoord.x / viewport.x ) * 2.0-1.0;
    screen.y = ( gl_FragCoord.y / viewport.y ) * 2.0-1.0;
       
    vec4 WorldPos = projMatrixInv * vec4( screen.x, screen.y, depth, 1.0);
    WorldPos.xyz /= WorldPos.w;

    vec3 Position = WorldPos.xyz ;
    //--------------------------------------------------------------------
    if (depth == 1.0) discard;// nothing there


    vec4 tex01_color = texture(gColor, UV);

    vec3 LightPosModelView = vec3(ModelMatrix * vec4(LightPos.xyz,1.0));

    //lighting caculations
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
    //final_color.xyz = tex01_color.xyz*0.01+(Position);
    //final_color.xyz = tex01_color.xyz*0.1+N*0.5+0.5;
    //-------------------------------------------------------------------
    outColor =  final_color;//+color*0.2;
    outColor.a = 1.0;
}
