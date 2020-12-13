#version 450 core


layout(location = 0) in vec2 vertexXZ;
layout(location = 1) in float vertexY;
layout(location = 2) in vec2 vertexTexCoord;

uniform mat4 Ortho_Project;
uniform mat4 shadowProjection;
uniform mat4 model;

out vec2 uv;
out vec4 ShadowCoord;

void main(void)
{
    uv = vertexTexCoord;
    vec4 vp = vec4(vertexXZ.x, vertexY, vertexXZ.y, 1.0);
    gl_Position = Ortho_Project * vec4(vp.x, vp.z, vp.y, 1.0);

    ShadowCoord = shadowProjection * vec4(vp.x, vp.y, vp.z, 1.0);
//    ShadowCoord =  model * ShadowCoord;

    //ShadowCoord = ShadowCoord;

}
