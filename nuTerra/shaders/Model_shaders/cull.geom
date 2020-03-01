// based on http://rastergrid.com/blog/2010/02/instance-culling-using-geometry-shaders/
#version 430 core

layout(points) in;
layout(points, max_vertices = 1) out;

in mat4 OrigPosition[1];
flat in int objectVisible[1];

out mat4 CulledModelView;

void main()
{
    if ( objectVisible[0] == 1 )
    {
        CulledModelView = OrigPosition[0];
        EmitVertex();
        EndPrimitive();
    }
}
