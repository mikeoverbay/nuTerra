//BillBoardBasic
#version 450 compatibility

uniform sampler2D colorMap;
uniform vec3 color;
in vec2 texCoord;

layout (location = 0) out vec4 gColor;

void main(void)
{
    vec4 Tcolor = textureLod(colorMap, texCoord,0.0);
    float l = length(Tcolor.xyz);
    //clip most of it off for testing
    if ( l < 0.9 ) discard;
    gColor.rgb = Tcolor.rgb* color;
    gColor.a = 1.0;
}
