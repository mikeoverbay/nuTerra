// ChannelMute_vertex.glsl
// Masks out color channels
//
#version 450

layout (binding = 0) uniform sampler2D colorMap;

uniform uint mask;
uniform int isNormal;
in vec2 texCoord;
out vec4 colorOut;
void main()
    {
    vec4 color = texture(colorMap,texCoord).rgba;
    float r = float(mask & 1) ;
    float g = float((mask & 2) / 2);
    float b = float((mask & 4) / 4);
    float a = float((mask & 8) / 8);

    vec4 MASK = vec4(r,g,b,a);

    if (r+g+b == 0.0)
        {
            if (a == 1.0 )
            {
            color.rgb = vec3(color.a);
            a=0.0;
            MASK = vec4(1.0);
            }
        }
    if (isNormal == 1){
        vec3 n = color.rgb;
        if ( length(n) < .01 ) discard;
        color.xyz = normalize(n*0.5+0.5);
    }

    colorOut = color * MASK; // Mask the textures color channels
    }