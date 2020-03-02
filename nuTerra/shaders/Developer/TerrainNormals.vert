// Normal shader .. shows the normal,tangent and biNormal vectors and wire overlay
#version 430 core

layout(location = 0) in vec2 vertexXZ;
layout(location = 1) in float vertexY;
layout(location = 2) in vec2 vertexTexCoord;
layout(location = 3) in int hole;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

uniform sampler2D t_normalMap;

out vec3 n;

void main(void)
{
    vec2 uv = vertexTexCoord; 
    uv.y*= -1.0;

    vec4 norm;
    norm.xy = texture(t_normalMap,-uv).ag;

    norm.xy = clamp(norm.xy * 2.0 - 1.0, -1.0, 1.0);

    norm.z = max(sqrt(1.0 - (norm.x*norm.x + norm.y*norm.y)),0.0);

    vec3 vertexNormal = normalize(norm.xzy);
    vertexNormal.x *= -1.0;

    vec3 vertexPosition = vec3(vertexXZ.x, vertexY, vertexXZ.y);
    

    // Calculate vertex position in clip coordinates
    gl_Position = projection * view * model * vec4(vertexPosition, 1.0);

    mat3 normalMatrix = mat3(transpose(inverse(view * model)));

    n = normalize(vec3(projection * vec4(normalMatrix * vertexNormal.xyz, 0.0f)));
}
