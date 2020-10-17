#version 450 core

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec3 gGMF;
layout (location = 3) out vec3 gPosition;
uniform sampler2D imageMap;

in vec2 texCoord;

void main(void)
{
    gColor = texture(imageMap, texCoord);
	gNormal = vec3(0.0);
    gGMF = vec3(0.0, 0.0, 1.0 ); //First to render and we want no lighting
	gPosition = vec3(0.0);
}
