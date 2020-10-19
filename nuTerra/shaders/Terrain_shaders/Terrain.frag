#version 450 core

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec3 gGMF;
layout (location = 3) out vec3 gPosition;
layout (location = 4) out uint gPick;

layout (std140, binding = 0 ) uniform Layers {
    vec4 layer0UT1;
    vec4 layer1UT1;
    vec4 layer2UT1;
    vec4 layer3UT1;

    vec4 layer0UT2;
    vec4 layer1UT2;
    vec4 layer2UT2;
    vec4 layer3UT2;

    vec4 layer0VT1;
    vec4 layer1VT1;
    vec4 layer2VT1;
    vec4 layer3VT1;

    vec4 layer0VT2;
    vec4 layer1VT2;
    vec4 layer2VT2;
    vec4 layer3VT2;

    float used_1;
    float used_2;
    float used_3;
    float used_4;
    float used_5;
    float used_6;
    float used_7;
    float used_8;
};

layout(binding = 1 ) uniform sampler2D layer_1T1;
layout(binding = 2 ) uniform sampler2D layer_2T1;
layout(binding = 3 ) uniform sampler2D layer_3T1;
layout(binding = 4 ) uniform sampler2D layer_4T1;

layout(binding = 5 ) uniform sampler2D layer_1T2;
layout(binding = 6 ) uniform sampler2D layer_2T2;
layout(binding = 7 ) uniform sampler2D layer_3T2;
layout(binding = 8 ) uniform sampler2D layer_4T2;

layout(binding = 9 ) uniform sampler2D n_layer_1T1;
layout(binding = 10) uniform sampler2D n_layer_2T1;
layout(binding = 11) uniform sampler2D n_layer_3T1;
layout(binding = 12) uniform sampler2D n_layer_4T1;

layout(binding = 13) uniform sampler2D n_layer_1T2;
layout(binding = 14) uniform sampler2D n_layer_2T2;
layout(binding = 15) uniform sampler2D n_layer_3T2;
layout(binding = 16) uniform sampler2D n_layer_4T2;

layout(binding = 17) uniform sampler2D mixtexture1;
layout(binding = 18) uniform sampler2D mixtexture2;
layout(binding = 19) uniform sampler2D mixtexture3;
layout(binding = 20) uniform sampler2D mixtexture4;

layout(binding = 21) uniform sampler2D tex_0;
layout(binding = 22) uniform sampler2D tex_1;
layout(binding = 23) uniform sampler2D tex_2;
layout(binding = 24) uniform sampler2D tex_3;

layout(binding = 25) uniform sampler2D tex_4;
layout(binding = 26) uniform sampler2D tex_5;
layout(binding = 27) uniform sampler2D tex_6;
layout(binding = 28) uniform sampler2D tex_7;

layout(binding = 29) uniform sampler2D global_AM;
layout(binding = 30) uniform sampler2D normalMap;

uniform int show_test;

in VS_OUT {
    mat3 TBN;
    vec4 Vertex;
    vec3 worldPosition;
    vec2 tuv4, tuv4_2, tuv3, tuv3_2;
    vec2 tuv2, tuv2_2, tuv1, tuv1_2;
    vec2 UV;
    vec2 Global_UV;
    float ln;
    //flat bool is_hole;
} fs_in;

/*===========================================================*/
// Used to add normals together. Could be better.
vec4 add_norms (in vec4 n1, in vec4 n2) {
    n1.xyz += n2.xyz;
    return n1;
}
//-------------------------------------------------------------

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
    //if (is_hole > 0) discard; // Early discard to avoid wasting draw time.

    vec4 t1, t2, t3, t4;
    vec4 t1_2, t2_2, t3_2, t4_2;
    vec4 n1, n2, n3, n4;
    vec4 n1_2, n2_2, n3_2, n4_2;
    vec2 MixLevel1, MixLevel2, MixLevel3, MixLevel4;
    vec3 PN1, PN2, PN3, PN4;
    float aoc_0, aoc_1, aoc_2, aoc_3;
    float  aoc_4, aoc_5, aoc_6, aoc_7;
    vec2 mix_coords;

    mix_coords = fs_in.UV;
    mix_coords.x = 1.0 - mix_coords.x;
    vec2 UVs = fs_in.UV;

    // create UV projections


    // Get AM maps and Test Texture maps
    t4 = texture(layer_4T1, fs_in.tuv4);
    vec4 tex6 = texture(tex_6, fs_in.tuv4);

    t4_2 = texture(layer_4T2, fs_in.tuv4_2);
    vec4 tex7 = texture(tex_7, fs_in.tuv4_2);

    t3 = texture(layer_3T1, fs_in.tuv3);
    vec4 tex4 = texture(tex_4, fs_in.tuv3);

    t3_2 = texture(layer_3T2, fs_in.tuv3_2);
    vec4 tex5 = texture(tex_5, fs_in.tuv3_2);

    t2 = texture(layer_2T1, fs_in.tuv2);
    vec4 tex2 = texture(tex_2, fs_in.tuv2);

    t2_2 = texture(layer_2T2, fs_in.tuv2_2);
    vec4 tex3 = texture(tex_3, fs_in.tuv2_2);

    t1 = texture(layer_1T1, fs_in.tuv1);
    vec4 tex0 = texture(tex_0, fs_in.tuv1);
 
    t1_2 = texture(layer_1T2, fs_in.tuv1_2);
    vec4 tex1 = texture(tex_1, fs_in.tuv1_2);

    // ambient occusion is in blue channel of the normal maps.
    // Specular OR Parallax is in the red channel. Green and Alpha are normal values.
    // We must get the Ambient Occlusion before converting so it isn't lost.

    // Get and convert normal maps. Save ambient occlusion value.
    n4 = texture(n_layer_4T1, fs_in.tuv4);
    aoc_6 = n4.b;
    n4 = convertNormal(n4) + layer3UT1;

    n4_2 = texture(n_layer_4T2, fs_in.tuv4_2);
    aoc_7 = n4_2.b;
    n4_2 = convertNormal(n4_2) + layer3UT2;

    n3 = texture(n_layer_3T1, fs_in.tuv3);
    aoc_4 = n3.b;
    n3 = convertNormal(n3) + layer2UT1;

    n3_2 = texture(n_layer_3T2, fs_in.tuv3_2);
    aoc_5 = n3_2.b;
    n3_2 = convertNormal(n3_2) + layer2UT2;

    n2 = texture(n_layer_2T1, fs_in.tuv2);
    aoc_2 = n2.b;
    n2 = convertNormal(n2) + layer1UT1;

    n2_2 = texture(n_layer_2T2, fs_in.tuv2_2);
    aoc_3 = n2_2.b;
    n2_2 = convertNormal(n2_2) + layer1UT2;

    n1 = texture(n_layer_1T1, fs_in.tuv1);
    aoc_0 = n1.b;
    n1 = convertNormal(n1) + layer0UT1;

    n1_2 = texture(n_layer_1T2, fs_in.tuv1_2);
    aoc_1 =  n1_2.b;
    n1_2 = convertNormal(n1_2) + layer0UT2;
    
    //Get the mix values from the mix textures 1-4 and move to vec2. 
    MixLevel1.rg = texture(mixtexture1, mix_coords.xy).ag;
    MixLevel2.rg = texture(mixtexture2, mix_coords.xy).ag;
    MixLevel3.rg = texture(mixtexture3, mix_coords.xy).ag;
    MixLevel4.rg = texture(mixtexture4, mix_coords.xy).ag;

    // Uniforms used_1 thru used_8 are either 0 or 1
    // depending on if the slot is used.
    // It is used to clamp unused values to 0 so
    // they have no affect on shading.

    // If we want to show the test textures, do it now.
    if (show_test == 1){
        float lv = 0.5;
        t1 = (t1*0.1)+ vec4(lv);
        t2 = (t2*0.1)+ vec4(lv);
        t3 = (t3*0.1)+ vec4(lv);
        t4 = (t4*0.1)+ vec4(lv);
        t1_2 = (t1_2*0.1)+ vec4(lv);
        t2_2 = (t2_2*0.1)+ vec4(lv);
        t3_2 = (t3_2*0.1)+ vec4(lv);
        t4_2 = (t4_2*0.1)+ vec4(lv);

        t4 = t4 * tex6 * used_7;
        t4_2 = t4_2 * tex7 * used_8;

        t3 = t3 * tex4 * used_5;
        t3_2 = t3_2 * tex5 * used_6;

        t2 = t2 * tex2 * used_3;
        t2_2 = t2_2 * tex3 * used_4;

        t1 = t1 * tex0 * used_1;
        t1_2 = t1_2 * tex1 * used_2;
    }

    vec4 base = vec4(0.0);  
    vec4 empty = vec4(0.0);

    // Mix our textures in to base and
    // apply Ambient Occlusion.
    // Mix group 4
    base += t4 * aoc_6 * MixLevel4.r * used_7;
    base += t4_2 * aoc_7 * MixLevel4.g * used_8;

    // Mix group 3
    base += t3 * aoc_4 * MixLevel3.r * used_5;
    base += t3_2 * aoc_5 * MixLevel3.g * used_6;

    // Mix group 2
    base += t2 * aoc_2 * MixLevel2.r * used_3;
    base += t2_2 * aoc_3 * MixLevel2.g * used_4;

    // Mix group 1
    base += t1 * aoc_0 * MixLevel1.r * used_1;
    base += t1_2 * aoc_1 * MixLevel1.g * used_2;
    
    //Get our normal maps. Same mixing and clamping as AM maps above

    // Mix group 4
    n4.rgb = fs_in.TBN * normalize(n4.rgb) * MixLevel4.r * used_7;
    n4_2.rgb = fs_in.TBN * normalize(n4_2.rgb) * MixLevel4.g * used_8;

    // Mix group 3
    n3.rgb = fs_in.TBN * normalize(n3.rgb) * MixLevel3.r * used_5;
    n3_2.rgb = fs_in.TBN * normalize(n3_2.rgb) * MixLevel3.g * used_6;

    // Mix group 2
    n2.rgb = fs_in.TBN * normalize(n2.rgb) * MixLevel2.r * used_3;
    n2_2.rgb = fs_in.TBN * normalize(n2_2.rgb) * MixLevel2.g * used_4;

    // Mix group 1
    n1.rgb = fs_in.TBN * normalize(n1.rgb) * MixLevel1.r * used_1;
    n1_2.rgb = fs_in.TBN * normalize(n1_2.rgb) * MixLevel1.g * used_2;


    //flip X axis. Everything is flipped on X including texture rotations.

    //n1.x *= -1.0;
    //n2.x *= -1.0;
    //n3.x *= -1.0;
    //n4.x *= -1.0;

    //n1_2.x *= -1.0;
    //n2_2.x *= -1.0;
    //n3_2.x *= -1.0;
    //n4_2.x *= -1.0;

    //-------------------------------------------------------------

    // This is needed to light the global_AM.
    vec4 g_nm = texture(normalMap, fs_in.UV);
    vec4 n_tex = vec4(0.0);
    n_tex.xyz = normalize(fs_in.TBN * vec3(convertNormal(g_nm).xyz));
    //n_tex.x*=-1.0;
    vec4 out_n = vec4(0.0);
    // Add up our normal values.
    out_n = add_norms(out_n, n1);
    out_n = add_norms(out_n, n1_2);
    out_n = add_norms(out_n, n2);
    out_n = add_norms(out_n, n2_2);
    out_n = add_norms(out_n, n3);
    out_n = add_norms(out_n, n3_2);
    out_n = add_norms(out_n, n4);
    out_n = add_norms(out_n, n4_2);

    
    // Mix in the global_AM color using global_AM's alpha channel.
    // I think this is used for wetness on the map.
    vec4 global = texture(global_AM, fs_in.Global_UV);
    base.rgb = mix(base.rgb,global.rgb,global.a);
    
    // This blends between low and highrez by distance

    // This blends the layered colors/normals and the global_AM/normalMaps over distance.
    // The blend stats at 100 and ends at 400. This has been changed for debug
    // Replace ln with 1.0 to show only layered terrain.
    
    //base = mix(base,vec4(MixLevel1.xy, 0.0 ,1.0), 0.4);

    
    base = mix(global, base, fs_in.ln);

    out_n = mix(n_tex, out_n, fs_in.ln) ;

    // The obvious
    gColor = base;
    gColor.a = 1.0;

    gNormal.xyz = normalize(out_n.xyz);
    gGMF.rgb = vec3(global.a+0.2, 0.0, 64.0/255.0);

    gPosition = fs_in.worldPosition;
    gPick = 0;
}
