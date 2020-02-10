#version 430 core

layout (triangles) in;
layout (line_strip, max_vertices = 18) out;

uniform int mode;
uniform float prj_length;
uniform mat4 MVPM;

in vec3 n[3];
in vec3 t[3];
in vec3 b[3];

out vec4 color;

void main()
{
    vec4 sumV;
    vec4 sumN;
    if (mode == 1) {
        sumV = (gl_in[0].gl_Position + gl_in[1].gl_Position + gl_in[2].gl_Position ) / 3.0;

        //Normal
            color = vec4(1.0,0.0,0.0,1.0);
            sumN.xyz = (n[0].xyz + n[1].xyz + n[2].xyz) / 3.0;
            sumN.w = 0.0;
            gl_Position = MVPM * sumV;
            EmitVertex();
            gl_Position = MVPM * (sumV + (sumN * prj_length));
            EmitVertex();
            EndPrimitive();
        //Tangent
            color = vec4(0.0,1.0,0.0,1.0);
            sumN.xyz = (t[0].xyz + t[1].xyz + t[2].xyz) / 3.0;
            sumN.w = 0.0;
            gl_Position = MVPM * sumV;
            EmitVertex();
            gl_Position = MVPM * (sumV + (sumN * prj_length));
            EmitVertex();
            EndPrimitive();
        //biTangent
            color = vec4(0.0,0.0,1.0,1.0);
            sumN.xyz = (b[0].xyz + b[1].xyz + b[2].xyz) / 3.0;
            sumN.w = 0.0;
            gl_Position = MVPM * sumV;
            EmitVertex();
            gl_Position = MVPM * (sumV + (sumN * prj_length));
            EmitVertex();
            EndPrimitive();
    }
    else {
        int i;
        // normal
        color = vec4(1.0,0.0,0.0,1.0);
        for(i = 0; i < gl_in.length(); i++)
        {
            gl_Position = MVPM * gl_in[i].gl_Position;
            EmitVertex();
            gl_Position = MVPM * (gl_in[i].gl_Position + (vec4(n[i], 0) * prj_length));
            EmitVertex();
            EndPrimitive();
        }
        // Tangent
        color = vec4(0.0,1.0,0.0,1.0);
        for(i = 0; i < gl_in.length(); i++)
        {
            gl_Position = MVPM * gl_in[i].gl_Position;
            EmitVertex();
            gl_Position = MVPM * (gl_in[i].gl_Position + (vec4(t[i], 0) * prj_length));
            EmitVertex();
            EndPrimitive();
        }
        // biTangent
            color = vec4(0.0,0.0,1.0,1.0);
        for(i = 0; i < gl_in.length(); i++)
        {
            gl_Position = MVPM * gl_in[i].gl_Position;
            EmitVertex();
            gl_Position = MVPM * (gl_in[i].gl_Position + (vec4(b[i], 0) * prj_length));
            EmitVertex();
            EndPrimitive();
        }
    } // mode
} //
