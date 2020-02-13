// Draws a quad from a single vertex input.

#version 430 core

layout (points) in;
layout (triangle_strip, max_vertices = 12) out;


uniform float scale;
out vec2 TexCoord;

float points[] = {
	-60.0f,  36.0f, // top-left
	 60.0f,  36.0f, // top-right
	 60.0f, -36.0f, // bottom-right
	-60.0f, -36.0f  // bottom-left
};

void main(void){

	vec3 Pos = gl_in[0].gl_Position.xyz;

    gl_Position = vec4( (points[3]*scale) + Pos.xy, 0, 1.0);
    TexCoord = vec2(0.0, 0.0);
    EmitVertex();

    gl_Position = vec4( (points[0]*scale) + Pos.xy, 0, 1.0);
    TexCoord = vec2(0.0, 1.0);
    EmitVertex();

    gl_Position = vec4( (points[2]*scale) + Pos.xy, 0, 1.0);
    TexCoord = vec2(1.0, 0.0);
    EmitVertex();

    gl_Position = vec4( (points[1]*scale) + Pos.xy, 0, 1.0);
    TexCoord = vec2(1.0, 1.0);
    EmitVertex();

    EndPrimitive();
}