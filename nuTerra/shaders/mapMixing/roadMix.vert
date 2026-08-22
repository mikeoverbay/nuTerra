#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

// Road patches baked into a virtual texture page. The geometry is world space
// in game coordinates, and the page's projection is top down, so world x and z
// become the page's x and y - the same .xzyw swizzle the chunk pass uses.
layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec3 vertexNormal;
layout(location = 2) in vec2 vertexTexCoord;
layout(location = 3) in vec4 vertexColour;
layout(location = 4) in uvec2 vertexTexHandle;
layout(location = 5) in uvec2 vertexNrmHandle;

uniform mat4 Ortho_Project;

out VS_OUT {
    vec2 UV;
    vec4 colour;
    flat uvec2 texHandle;
    flat uvec2 nrmHandle;
} vs_out;

void main(void)
{
    vs_out.UV = vertexTexCoord;
    vs_out.colour = vertexColour;
    vs_out.texHandle = vertexTexHandle;
    vs_out.nrmHandle = vertexNrmHandle;

    gl_Position = Ortho_Project * vec4(vertexPosition, 1.0).xzyw;
}
