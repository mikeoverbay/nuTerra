#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec2 vertexXZ;
layout(location = 1) in float vertexY;
layout(location = 2) in vec2 vertexTexCoord;
layout(location = 3) in vec4 vertexNormal;
layout(location = 4) in vec3 vertexTangent;

layout (std140, binding = TERRAIN_LAYERS_UBO_BASE) uniform Layers {
    vec4 U1;
    vec4 U2;
    vec4 U3;
    vec4 U4;

    vec4 U5;
    vec4 U6;
    vec4 U7;
    vec4 U8;

    vec4 V1;
    vec4 V2;
    vec4 V3;
    vec4 V4;

    vec4 V5;
    vec4 V6;
    vec4 V7;
    vec4 V8;

    vec4 r1_1;
    vec4 r1_2;
    vec4 r1_3;
    vec4 r1_4;
    vec4 r1_5;
    vec4 r1_6;
    vec4 r1_7;
    vec4 r1_8;

    vec4 r2_1;
    vec4 r2_2;
    vec4 r2_3;
    vec4 r2_4;
    vec4 r2_5;
    vec4 r2_6;
    vec4 r2_7;
    vec4 r2_8;

    float used_1;
    float used_2;
    float used_3;
    float used_4;
    float used_5;
    float used_6;
    float used_7;
    float used_8;
};
uniform vec2 map_size;
uniform vec2 map_center;
uniform vec2 me_location;
uniform mat4 Ortho_Project;
uniform mat3 normalMatrix;

out VS_OUT {
    vec2 tuv1, tuv2, tuv3, tuv4, tuv5, tuv6, tuv7, tuv8; 
    vec2 UV;
    vec2 Global_UV;
    flat float is_hole;
} vs_out;

vec2 get_transformed_uv(in vec4 Row0, in vec4 Row2, in vec4 Row3, in vec2 _uv) {

    mat4 rs;
    rs[0] = vec4(Row0.x, Row0.y, Row0.z, 0.0);
    rs[1] = vec4(0.0,    1.0,    0.0,    0.0);
    rs[2] = vec4(Row2.x, Row2.y, Row2.z, 0.0);
    rs[3] = vec4(Row3.x, 0.0,    Row3.y, 1.0);
    rs[3] = vec4(0.0,    0.0,    0.0,    1.0);
    vec4 tv = rs * vec4(_uv.x, 0.0, _uv.y, 1.0);   
    return vec2(tv.x+0.5, tv.z+0.5);
    }


void main(void)
{

    vs_out.UV =  vertexTexCoord;
    // calculate tex coords for global_AM
    vec2 uv_g;
    vec2 scaled = vs_out.UV / map_size;
    vec2 m_s = vec2(1.0)/map_size;
    uv_g.x = ((( (me_location.x )-50.0)/100.0)+map_center.x) * m_s.x ;
    uv_g.y = ((( (me_location.y )-50.0)/100.0)-map_center.y) * m_s.y ;
    vs_out.Global_UV = scaled + uv_g;
    vs_out.Global_UV.xy = 1.0 - vs_out.Global_UV.xy;
    
    vs_out.is_hole = vertexNormal.w ;
    //-------------------------------------------------------
    //This is XZY because of ortho projection!
    vec3 vertexPosition = vec3(vertexXZ.x, vertexXZ.y, vertexY);
    vec4 Vertex = vec4(vertexPosition, 1.0) * 1.0;
    Vertex.x *= -1.0;
    
    //-------------------------------------------------------
    vec2 scaled_uv = vec2(-vertexPosition.x,vertexPosition.z);
    //-------------------------------------------------------

    vs_out.tuv1 = get_transformed_uv(U1, V1, r1_1, scaled_uv); 
    vs_out.tuv2 = get_transformed_uv(U2, V2, r1_2, scaled_uv);

    vs_out.tuv3 = get_transformed_uv(U3, V3, r1_3, scaled_uv); 
    vs_out.tuv4 = get_transformed_uv(U4, V4, r1_4, scaled_uv);

    vs_out.tuv5 = get_transformed_uv(U5, V5, r1_5, scaled_uv); 
    vs_out.tuv6 = get_transformed_uv(U6, V6, r1_6, scaled_uv);

    vs_out.tuv7 = get_transformed_uv(U7, V7, r1_7, scaled_uv);
    vs_out.tuv8 = get_transformed_uv(U8, V8, r1_8, scaled_uv);

    //-------------------------------------------------------

    // Calculate vertex position in clip coordinates
    gl_Position = Ortho_Project  * vec4(vertexPosition, 1.0f);

}
