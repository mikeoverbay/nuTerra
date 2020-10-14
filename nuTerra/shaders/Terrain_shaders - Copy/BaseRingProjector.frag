
#version 430 core

layout (location = 0) out vec4 gColor;

uniform sampler2D depthMap;

in mat4 inverseProject;
in vec4 positionSS;

uniform vec4 color;
uniform vec3 ring_center;
uniform float radius;
uniform float thickness;

vec2 postProjToScreen(vec4 position)
    {
    vec2 screenPos = position.xy / position.w;
    return 0.5 * (vec2(screenPos.x, screenPos.y) + 1);
    }

void main (void)
{
    vec2 UV = postProjToScreen(positionSS);
    float Depth = texture(depthMap, UV).x;

    // Calculate Worldposition by recreating it out of the coordinates and depth-sample
    vec4 ScreenPosition;
    ScreenPosition.xy = UV * 2.0 - 1.0;
    ScreenPosition.z = (Depth);
    ScreenPosition.w = 1.0f;
    // Transform position from screen space to world space
    vec4 WorldPosition = inverseProject * ScreenPosition ;
    WorldPosition.xyz /= WorldPosition.w;

    float rs = length(WorldPosition.xz - ring_center.xz);
    float t = 1.0+ smoothstep(radius, radius+thickness, rs) 
                  - smoothstep(radius-thickness, radius, rs);
    gColor = color;
    gColor.a = 1.0-t;
    if (gColor.a <0.0) {
    discard;
    }
}
