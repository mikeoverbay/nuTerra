//BillBoardBasic
#version 450 compatibility

uniform sampler2D colorMap;
uniform vec3 color;
in vec2 texCoord;
in float fade;

layout (location = 0) out vec4 gColor;

void main(void)
{
    vec4 Tcolor = textureLod(colorMap, texCoord,0.0);

    gColor = Tcolor;
    gColor.a *= fade;
}
