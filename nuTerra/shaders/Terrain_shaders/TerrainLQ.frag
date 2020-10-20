//Low Quality Terrain
#version 450 core

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec3 gGMF;
layout (location = 3) out vec3 gPosition;
layout (location = 4) out uint gPick;

layout(binding = 0) uniform sampler2D global_AM;
layout(binding = 1) uniform sampler2D normalMap;

in VS_OUT {
    vec4 Vertex;
    mat3 TBN;
    vec3 worldPosition;
    vec2 UV;
    vec2 Global_UV;
    flat bool is_hole;
} fs_in;

// Converion from AG map to RGB vector.
vec4 convertNormal(vec4 norm){
        vec3 n;
        n.xy = clamp(norm.ag*2.0-1.0, -1.0 ,1.0);
        n.z = max(sqrt(1.0 - (n.x*n.x - n.y *n.y)),0.0);
        return vec4(n,0.0);
}

/*===========================================================*/

void main(void)
{
    // Remmed so I dont go insane.
    if (fs_in.is_hole) discard; // Early discard to avoid wasting draw time.

    vec4 global = texture(global_AM, fs_in.Global_UV);
    // This is needed to light the global_AM.
    vec4 g_nm = texture(normalMap, fs_in.UV);
    vec4 n = vec4(0.0);
    n.xyz = normalize(fs_in.TBN * vec3(convertNormal(g_nm).xyz));
    //n.x*=-1.0;
  
    // The obvious
    gColor = global;
    gColor.a = 1.0;

    gNormal.xyz = normalize(n.xyz);
    gGMF.rgb = vec3(global.a+0.2, 0.0, 64.0/255.0);

    gPosition = fs_in.worldPosition;
    gPick = 0;
}
