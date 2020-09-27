#version 450 core

layout (points) in;
layout (line_strip, max_vertices = 24) out;

uniform mat4 projection;
uniform mat4 view;

struct ModelInstance
{
    mat4 model_matrix;
    vec3 bmin;
    uint offset;
    vec3 bmax;
    uint count;
    uint prim_groups_count;
    uint reserved;
};

layout (binding = 0, std430) readonly buffer MODEL_MATRIX_BLOCK
{
    ModelInstance model_matrix[];
};

void main(void)
{
    const mat4 MVP = projection * view * model_matrix[gl_PrimitiveIDIn].model_matrix;
    const vec3 bmin = model_matrix[gl_PrimitiveIDIn].bmin;
    const vec3 bmax = model_matrix[gl_PrimitiveIDIn].bmax;

    gl_Position = MVP * vec4(bmin, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmin.xy, bmax.z, 1.0);
    EmitVertex();

    EndPrimitive(); // 1

    gl_Position = MVP * vec4(bmin.xy, bmax.z, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmin.x, bmax.yz, 1.0);
    EmitVertex();

    EndPrimitive(); // 2

    gl_Position = MVP * vec4(bmin.x, bmax.yz, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmin.x, bmax.y, bmin.z, 1.0);
    EmitVertex();

    EndPrimitive(); // 3

    gl_Position = MVP * vec4(bmin.x, bmax.y, bmin.z, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmin, 1.0);
    EmitVertex();

    EndPrimitive(); // 4

    gl_Position = MVP * vec4(bmax.x, bmin.yz, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmax.x, bmin.y, bmax.z, 1.0);
    EmitVertex();

    EndPrimitive(); // 5

    gl_Position = MVP * vec4(bmax.x, bmin.y, bmax.z, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmax, 1.0);
    EmitVertex();

    EndPrimitive(); // 6

    gl_Position = MVP * vec4(bmax, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmax.x, bmax.y, bmin.z, 1.0);
    EmitVertex();

    EndPrimitive(); // 7

    gl_Position = MVP * vec4(bmax.x, bmax.y, bmin.z, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmax.x, bmin.yz, 1.0);
    EmitVertex();

    EndPrimitive(); // 8

    gl_Position = MVP * vec4(bmin, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmax.x, bmin.yz, 1.0);
    EmitVertex();

    EndPrimitive(); // 9

    gl_Position = MVP * vec4(bmin.xy, bmax.z, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmax.x, bmin.y, bmax.z, 1.0);
    EmitVertex();

    EndPrimitive(); // 10

    gl_Position = MVP * vec4(bmin.x, bmax.yz, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmax, 1.0);
    EmitVertex();

    EndPrimitive(); // 11

    gl_Position = MVP * vec4(bmin.x, bmax.y, bmin.z, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmax.xy, bmin.z, 1.0);
    EmitVertex();

    EndPrimitive(); // 12
}
