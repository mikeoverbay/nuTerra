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
    //This is XZY because of ortho projection!
    vec3 vertexPosition = vec3(vertexXZ.x, vertexXZ.y, vertexY);
    
    //-------------------------------------------------------

    // Calculate vertex position in clip coordinates
    gl_Position = modelMatrix * Ortho_Project  * vec4(vertexPosition, 1.0f);

}
