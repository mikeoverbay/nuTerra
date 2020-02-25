// used to render fronds

#version 330 compatibility

layout (location = 0) out vec4 gColor;

uniform sampler2D depthMap;

in vec4 positionSS; // screen space
in vec4 positionWS; // world space
in mat4 matPrjInv; // inverse projection matrix

in vec4 color;
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
    float Depth = texture2D(depthMap, UV).x * 2.0 - 1.0;

    // Calculate Worldposition by recreating it out of the coordinates and depth-sample
    vec4 ScreenPosition;
    ScreenPosition.xy = UV * 2.0 - 1.0;
    ScreenPosition.z = (Depth);
    ScreenPosition.w = 1.0f;
    // Transform position from screen space to world space
    vec4 WorldPosition = matPrjInv * ScreenPosition ;
    WorldPosition.xyz /= WorldPosition.w;
    float rs = length(WorldPosition.xz - ring_center.xz);
    if (rs <= radius) {
        if (rs >= radius - thickness){
            gColor = color;
               //discard;
            } else {
            discard;
            }
    } else {
    discard;
    }
}
