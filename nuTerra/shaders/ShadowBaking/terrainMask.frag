#version 450 core

out vec4 mask;

in vec2 uv;
in vec4 ShadowCoord;

layout(binding = 0) uniform sampler2D shadow_map;

uniform int map_id;
vec4 ShadowCoordPostW;

float chebyshevUpperBound( float distance)
{
    // this clips off the depth map edge artifact.
    if (ShadowCoordPostW.x >0.997) return 1.0;
    if (ShadowCoordPostW.x <0.003) return 1.0;
    if (ShadowCoordPostW.y >0.997) return 1.0;
    if (ShadowCoordPostW.y <0.003) return 1.0;
   
   vec2 moments = texture(shadow_map,ShadowCoordPostW.xy).rg;

    // Surface is fully lit. as the current fragment is before the light occluder
    if (distance <= moments.x)
        return 1.0 ;

    // The fragment is either in shadow or penumbra.
    // We now use chebyshev's upperBound to check
    // How likely this pixel is to be lit (p_max)
    float variance = moments.y - (moments.x*moments.x);
    variance = max(variance,0.1);

    float d = distance - moments.x;
    //float p_max =  smoothstep(0.00, 1.0   , variance / (variance + d*d));
    float p_max =   variance / (variance + d*d);
    p_max = max(p_max,0.0);
    return p_max ;
}


void main(void){

    vec4 full = vec4(1.0);
        ShadowCoordPostW = ShadowCoord / ShadowCoord.w;
    // Depth was scaled up in the depth shader so we scale it up here too.
    // This fixes precision issues.
    float shadow = chebyshevUpperBound(ShadowCoordPostW.z*5000.0);

    mask.r =  max(abs(shadow)+0.4,0.1);
}