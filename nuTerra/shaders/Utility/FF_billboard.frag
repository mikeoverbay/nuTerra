//BillBoardBasic
#version 450 compatibility

uniform sampler2D colorMap;
uniform vec3 color;
in vec2 texCoord;

layout (location = 0) out vec4 gColor;

void main(void)
{
    vec4 Tcolor = texture2D(colorMap, texCoord);
    gColor.rgb = Tcolor.rgb* color;
    gColor.a = 1.0;
}
