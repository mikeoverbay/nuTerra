
#version 430 core

layout(location = 0) in vec2 vertexXZ;
layout(location = 1) in float vertexY;
layout(location = 2) in vec2 vertexTexCoord;
layout(location = 3) in vec4 vertexNormal;

uniform mat4 viewModel;
uniform mat4 projection;
uniform mat3 normalMatrix;
uniform vec2 me_location;
uniform vec2 map_size;
uniform vec2 map_center;

uniform vec3 cam_position;

uniform int layer_mask;

out vec4 mask_1;
out vec4 mask_2;
out vec4 Vertex;
out float ln;
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
    
    is_hole = (vertexNormal.w == 1.0f) ? 1 : 0;


    vec3 vertexPosition = vec3(vertexXZ.x, vertexY, vertexXZ.y);
    Vertex = vec4(vertexXZ.x, vertexY, vertexXZ.y, 1.0);

    vec3 tangent;
    // NOTE: vertexNormal is already normalized in the VBO.
    vec3 c1 = cross(vertexNormal.xyz, vec3(0.0, 0.0, 1.0));
    vec3 c2 = cross(vertexNormal.xyz, vec3(0.0, 1.0, 0.0));

    if( length(c1) > length(c2) )
        {
            tangent = c1;
        }
        else
        {
            tangent = c2;
        }

    tangent = tangent - dot(vertexNormal.xyz, tangent) * vertexNormal.xyz;

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
    vec3 point = vec3(viewModel * vec4(vertexXZ.x, cam_position.y, vertexXZ.y, 1.0f));
    // Create the mask.  Used to cancel ant transform of tex_color;\
    float on = 1.0;
    float off = 0.0;
    mask_1 = vec4(1.0 ,1.0 ,1.0 ,1.0);
    mask_2 = vec4(1.0 ,1.0 ,1.0 ,1.0);
    if ( layer_mask > 128 ) mask_2.r = 0.0; 
    if ( layer_mask > 64 ) mask_2.g = 0.0;
    if ( layer_mask > 32 ) mask_2.b = 0.0;
    if ( layer_mask > 16 ) mask_2.a = 0.0;
    if ( layer_mask > 8  ) mask_1.r = 0.0;
    if ( layer_mask > 4  ) mask_1.g = 0.0;
    if ( layer_mask > 2  ) mask_1.b = 0.0;
    if ( layer_mask >= 1  ) mask_1.a = 0.0; // this should always be set

    // This is the cut off distance for bumpping the surface.
    vec3 cp = cam_position;
    //cp.x*= -1.0;
    ln = distance( point.xyz,cp.xyz );
    if (ln<200.0) { ln = 1.0 - ln/200.0;} //Creates sine curve.
    else {ln = 0.0;}
}
