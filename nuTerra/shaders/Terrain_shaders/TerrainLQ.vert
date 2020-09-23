﻿//Low Quality Terrain
#version 430 core

layout(location = 0) in vec2 vertexXZ;
layout(location = 1) in float vertexY;
layout(location = 2) in vec2 vertexTexCoord;
layout(location = 3) in vec4 vertexNormal;

//uniforms
layout(location = 5) uniform mat4 viewMatrix;
layout(location = 6) uniform mat4 projMatrix;

layout(location = 7) uniform vec2 map_size;
layout(location = 8) uniform vec2 map_center;
layout(location = 9) uniform vec3 cam_position;

layout(location = 10) uniform mat4 modelMatrix;
layout(location = 11) uniform mat3 normalMatrix;
layout(location = 12) uniform vec2 me_location;

out vec4 Vertex;
out mat3 TBN;
out vec3 worldPosition;
out vec2 UV;
out vec2 Global_UV;

flat out uint is_hole;

void main(void)
{

     UV =  vertexTexCoord;
     // calculate tex coords for global_AM
    vec2 uv_g;
    vec2 scaled = UV / map_size;
    vec2 m_s = vec2(1.0)/map_size;
    uv_g.x = ((( (me_location.x )-50.0)/100.0)+map_center.x) * m_s.x ;
    uv_g.y = ((( (me_location.y )-50.0)/100.0)-map_center.y) * m_s.y ;
    Global_UV = scaled + uv_g;
    Global_UV.xy = 1.0 - Global_UV.xy;
    
    is_hole = (vertexNormal.w == 1.0f) ? 1 : 0;
    
    vec3 vertexPosition = vec3(vertexXZ.x, vertexY, vertexXZ.y);
    Vertex = vec4(vertexPosition, 1.0) * 1.0;
    Vertex.x *= -1.0;

    //-------------------------------------------------------
    //Calculate tangent and biNormal
    vec3 tangent;
    // NOTE: vertexNormal is already normalized in the VBO.
    vec3 c1 = cross(vertexNormal.xyz, vec3(0.0, 0.0, 1.0));
    vec3 c2 = cross(vertexNormal.xyz, vec3(0.0, 1.0, 0.0));
    if( length(c1) > length(c2) )
        {  tangent = c1;  }
        else
        {   tangent = c2; }
    tangent = tangent - dot(vertexNormal.xyz, tangent) * vertexNormal.xyz;
    vec3 bitangent = cross(tangent, vertexNormal.xyz);

    //vertex --> world pos
    worldPosition = vec3(viewMatrix * modelMatrix * vec4(vertexPosition, 1.0f));

    // Tangent, biNormal and Normal must be trasformed by the normal Matrix.
    vec3 worldNormal = normalMatrix * vertexNormal.xyz;
    vec3 worldTangent = normalMatrix * tangent;
    vec3 worldbiNormal = normalMatrix * bitangent;

    //make perpendicular
    worldTangent = normalize(worldTangent - dot(worldNormal, worldTangent) * worldNormal);
    worldbiNormal = normalize(worldbiNormal - dot(worldNormal, worldbiNormal) * worldNormal);

    // Create the Tangent, BiNormal, Normal Matrix for transforming the normalMap.
    TBN = mat3( normalize(worldTangent), normalize(worldbiNormal), normalize(worldNormal));

    // Calculate vertex position in clip coordinates
    gl_Position = projMatrix * viewMatrix * modelMatrix * vec4(vertexPosition, 1.0f);
   
}