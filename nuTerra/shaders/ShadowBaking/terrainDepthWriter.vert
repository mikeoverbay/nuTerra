#version 450 core


layout(location = 0) in vec2 vertexXZ;
layout(location = 1) in float vertexY;
layout(location = 2) in vec2 vertexTexCoord;
layout(location = 3) in vec4 vertexNormal;
layout(location = 4) in vec3 vertexTangent;

uniform mat4 Ortho_Project;
uniform mat4 modelMatrix;
void main(void)
{

    //-------------------------------------------------------
    vec3 vp = vec3(vertexXZ.x, vertexY, vertexXZ.y);
    vp = vec3(modelMatrix * vec4(vp.xyz, 1.0)).xyz;
    //-------------------------------------------------------
    //This is XZY because of ortho projection!
    gl_Position = Ortho_Project * vec4(vp.x, vp.z, vp.y, 1.0);

}
