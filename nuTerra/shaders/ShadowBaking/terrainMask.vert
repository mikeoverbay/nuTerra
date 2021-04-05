#version 450 core


layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 vertexTexCoord;

uniform mat4 Ortho_Project;
uniform mat4 shadowProjection;
uniform mat4 model;

out vec2 uv;
out vec4 ShadowCoord;

void main(void)
{
    uv = vertexTexCoord;
    gl_Position = Ortho_Project * vec4(vertexPosition, 1.0);

    ShadowCoord = shadowProjection * vec4(vertexPosition, 1.0);
//    ShadowCoord =  model * ShadowCoord;

    //ShadowCoord = ShadowCoord;

}
