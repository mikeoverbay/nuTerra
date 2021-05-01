#version 450 core

out vec4 fragColor;
layout(binding = 0) uniform sampler2DArray imageMap;

in flat int id;
in vec2 texCoord;

void main(void)
{
    fragColor = texture( imageMap, vec3( texCoord,float(id) ) );
}
