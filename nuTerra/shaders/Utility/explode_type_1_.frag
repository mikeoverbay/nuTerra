#version 450 core

//BillBoardBasic

uniform sampler2D colorMap;

layout (binding = 0) uniform sampler2D alpha_LUT;

in vec2 texCoord;
flat in float alpha;

layout (location = 0) out vec4 gColor;

void main(void)
{

    vec4 Tcolor = texture(colorMap, texCoord);

    gColor = Tcolor;
    gColor.a *= alpha;
}
