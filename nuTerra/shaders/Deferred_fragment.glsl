//
// Deferred lighting fragment shader.
//

#version 330 compatibility

out vec4 outColor;

uniform sampler2D gColor;
uniform sampler2D gNormal;
uniform sampler2D gGMF;
uniform sampler2D gDepth;

uniform mat4 ModelMatrix;
uniform vec3 LightPos;

in vec3 CameraPos;
in vec2 UV;

in mat4 projMatrixInv;
in mat4 ModelMatrixInv;
// Functions ///////////////////////////////////////

float linearDepth(float depthSample)
{
    float f = 2500.0;
    float n = 0.5;
    
    //depthSample = 2.0 * depthSample - 1.0;
    //float zLinear = 2.0 * zNear * zFar / (zFar + zNear - depthSample * (zFar - zNear));
    return  (2.0 * n) / (f + n - depthSample * (f - n));
}

// this is supposed to get the world position from the depth buffer
vec3 WorldPosFromDepth(float depth) {
 
    vec4 clipSpacePosition = vec4(UV * 2.0 - 1.0, depth, 1.0);
    vec4 viewSpacePosition = projMatrixInv * clipSpacePosition;

    // Perspective division
    viewSpacePosition /= viewSpacePosition.w;

    vec4 worldSpacePosition = ModelMatrixInv * viewSpacePosition;

    return worldSpacePosition.xyz;
}
////////////////////////////////////////////////////


void main (void)
{
    float depth = texture2D(gDepth, UV).x*2.0-1.0;
    vec3 Position = WorldPosFromDepth( depth );
    if (depth == 1.0) discard;

    vec3 vd = normalize(-Position);

    vec4 tex01_color  = texture2D(gColor, UV);

    vec3 LightPosModelView = vec3(ModelMatrixInv * vec4(LightPos.xyz,1.0));

    //lighting caculations
    vec3 N = normalize(texture2D(gNormal,UV).xyz*2.0-1.0);

    vec3 L = normalize(LightPosModelView-Position.xyz);

    vec4 final_color = vec4(0.2, 0.2, 0.2, 1.0) * tex01_color;

    float dist = length(LightPosModelView - Position);
    float cutoff = 1560.0;
    //only light whats in range
    if (dist < cutoff) {

        float lambertTerm = dot(N, L);
            final_color += max(lambertTerm * tex01_color*1.0,0.0);

            vec3 halfwayDir = normalize(L + vd);

            float specular = pow(max(dot(N, halfwayDir), 0.0), 10.0) * 0.3;
            final_color += specular;  
        
    }
    float d = linearDepth(depth);
    //final_color.xyz = final_color.xyz*0.01+d;
    final_color.xyz = tex01_color.xyz*0.01+(Position);
    //final_color.xyz = tex01_color.xyz*0.1+(LightPosModelView/212.0)*0.5+0.5;
    outColor =  final_color;//+color*0.2;
    outColor.a = 1.0;
}
