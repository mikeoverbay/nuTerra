#version 450 core

layout (triangles) in;
layout (line_strip, max_vertices = 21) out;

uniform bool show_wireframe;
uniform int mode;
uniform float prj_length;

in VS_OUT
{
    vec3 n;
    vec3 t;
    vec3 b;
    mat4 matrix;
} gs_in[];

out GS_OUT {
    flat vec4 color;
} gs_out;

void main()
{
    int i;
    if (mode == 1) {
        vec4 sumV = (gl_in[0].gl_Position + gl_in[1].gl_Position + gl_in[2].gl_Position ) / 3.0f;
        vec3 sumN;

        // Normal
        gs_out.color = vec4(1.0f, 0.0f, 0.0f, 1.0f);
        sumN = (gs_in[0].n + gs_in[1].n + gs_in[2].n) / 3.0f;
        gl_Position = gs_in[0].matrix * sumV;
        EmitVertex();
        gl_Position = gs_in[0].matrix * (sumV + vec4(sumN * prj_length, 0.0f));
        EmitVertex();
        EndPrimitive();

        // Tangent
        gs_out.color = vec4(0.0f, 1.0f, 0.0f, 1.0f);
        sumN = (gs_in[0].t + gs_in[1].t + gs_in[2].t) / 3.0f;
        gl_Position = gs_in[0].matrix * sumV;
        EmitVertex();
        gl_Position = gs_in[0].matrix * (sumV + vec4(sumN * prj_length, 0.0f));
        EmitVertex();
        EndPrimitive();

        //biTangent
        gs_out.color = vec4(0.0f, 0.0f, 1.0f, 1.0f);
        sumN = (gs_in[0].b + gs_in[1].b + gs_in[2].b) / 3.0f;
        gl_Position = gs_in[0].matrix * sumV;
        EmitVertex();
        gl_Position = gs_in[0].matrix * (sumV + vec4(sumN * prj_length, 0.0f));
        EmitVertex();
        EndPrimitive();
    }
    else if (mode == 2) {
        // normal
        gs_out.color = vec4(1.0f, 0.0f, 0.0f, 1.0f);
        for(i = 0; i < gl_in.length(); i++) {
            gl_Position = gs_in[0].matrix * gl_in[i].gl_Position;
            EmitVertex();
            gl_Position = gs_in[0].matrix * (gl_in[i].gl_Position + vec4(gs_in[i].n * prj_length, 0.0f));
            EmitVertex();
            EndPrimitive();
        }
        // Tangent
        gs_out.color = vec4(0.0f, 1.0f, 0.0f, 1.0f);
        for(i = 0; i < gl_in.length(); i++) {
            gl_Position = gs_in[0].matrix * gl_in[i].gl_Position;
            EmitVertex();
            gl_Position = gs_in[0].matrix * (gl_in[i].gl_Position + vec4(gs_in[i].t * prj_length, 0.0f));
            EmitVertex();
            EndPrimitive();
        }
        // biTangent
        gs_out.color = vec4(0.0f, 0.0f, 1.0f, 1.0f);
        for(i = 0; i < gl_in.length(); i++) {
            gl_Position = gs_in[0].matrix * gl_in[i].gl_Position;
            EmitVertex();
            gl_Position = gs_in[0].matrix * (gl_in[i].gl_Position + vec4(gs_in[i].b * prj_length, 0.0f));
            EmitVertex();
            EndPrimitive();
        }
    }

    if (show_wireframe) {
        gs_out.color = vec4(1.0f, 1.0f, 0.0f, 1.0f);
        for (i = 0; i < gl_in.length(); i++) {
            gl_Position = gs_in[0].matrix *  gl_in[i].gl_Position;
            EmitVertex();
        }
        EndPrimitive();
    }
}
