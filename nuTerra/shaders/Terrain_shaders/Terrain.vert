
#version 430 core

layout(location = 0) in vec2 vertexXZ;
layout(location = 1) in float vertexY;
layout(location = 2) in vec2 vertexTexCoord;
layout(location = 3) in uint holes; 

uniform mat4 viewModel;
uniform mat4 projection;
uniform mat3 normalMatrix;
uniform sampler2D t_normalMap;
uniform vec2 me_location;
uniform vec2 map_size;
uniform vec2 map_center;

out mat3 TBN;
out vec3 worldPosition;
out vec2 UV;
out vec2 Global_UV;
flat out uint is_hole;

void main(void)
{
    UV =  vertexTexCoord;
    vec2 uv_g;
    vec2 scaled = UV / map_size;
    vec2 m_s = vec2(1.0)/map_size;

    uv_g.x = ((( (me_location.x )-50.0)/100.0)+map_center.x) * m_s.x ;
    uv_g.y = ((( (me_location.y )-50.0)/100.0)-map_center.y) * m_s.y ;

    Global_UV = scaled + uv_g;
    Global_UV.xy = 1.0 - Global_UV.xy;
    
    is_hole = holes;

    vec3 n;
    vec2 uv = UV;

    uv.x = 1.0-uv.x;

    n.xy = texture(t_normalMap,uv).ag;

    n.xy = clamp(n.xy * 2.0 - 1.0, -1.0, 1.0);

    n.z = max(sqrt(1.0 - (n.x*n.x + n.y *n.y)),0.0);

    vec3 vertexNormal = normalize(n.xzy);
    vertexNormal.x*= -1.0;
    vec3 vertexPosition = vec3(vertexXZ.x, vertexY, vertexXZ.y);
    vec3 tangent;

    vec3 c1 = cross(vertexNormal.xyz, vec3(0.0, 0.0, 1.0));
    vec3 c2 = cross(vertexNormal.xyz, vec3(0.0, 1.0, 0.0));

    if( length(c1) > length(c2) )
        {
            tangent = normalize(c1);
        }
        else
        {
            tangent = normalize(c2);
        }

    tangent = normalize(tangent - dot(vertexNormal.xyz, tangent) * vertexNormal.xyz);

    vec3 bitangent = cross(tangent, vertexNormal.xyz);

    worldPosition = vec3(viewModel * vec4(vertexPosition, 1.0f));

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
    gl_Position = projection * viewModel * vec4(vertexPosition, 1.0f);
}
