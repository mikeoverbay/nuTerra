// gWriter fragment Shader. We will use this as a template for other shaders
#version 430 core

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec3 gGMF;
layout (location = 3) out vec3 gPosition;

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

layout(location = 24) uniform int show_test;
in float ln;
in mat3 TBN;
in vec3 worldPosition;
in vec4 Vertex;

in vec2 UV;
in vec2 Global_UV;
in vec3 normal;//temp for debuging lighting
flat in uint is_hole;

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
    vec2 tv4, tv4_2, tv3, tv3_2;
    vec2 tv2, tv2_2, tv1, tv1_2;
    float aoc_0, aoc_1, aoc_2, aoc_3;
    float  aoc_4, aoc_5, aoc_6, aoc_7;
    vec2 mix_coords;

    mix_coords = UV;
    mix_coords.x = 1.0 - mix_coords.x;
    vec2 UVs = UV;

    // create UV projections
    tv4 = -vec2(dot(-layer3UT1, Vertex), dot(-layer3VT1, Vertex)) ;
    tv4_2 = -vec2(dot(-layer3UT2, Vertex), dot(-layer3VT2, Vertex)) ;

    tv3 = -vec2(dot(-layer2UT1, Vertex), dot(-layer2VT1, Vertex)) ;
    tv3_2 = -vec2(dot(-layer2UT2, Vertex), dot(-layer2VT2, Vertex)) ;

    tv2 = -vec2(dot(-layer1UT1, Vertex), dot(-layer1VT1, Vertex));
    tv2_2 = -vec2(dot(-layer1UT2, Vertex), dot(-layer1VT2, Vertex));

    tv1 = -vec2(dot(-layer0UT1, Vertex), dot(-layer0VT1, Vertex)) ;
    tv1_2 = -vec2(dot(-layer0UT2, Vertex), dot(-layer0VT2, Vertex)) ;

    // Get AM maps and Test Texture maps
    t4 = texture(layer_4T1, tv4 );
    vec4 tex6 = texture(tex_6, tv4);

    t4_2 = texture(layer_4T2, tv4_2);
    vec4 tex7 = texture(tex_7, tv4_2);

    t3 = texture(layer_3T1, tv3);
    vec4 tex4 = texture(tex_4, tv3);

    t3_2 = texture(layer_3T2, tv3_2);
    vec4 tex5 = texture(tex_5, tv3_2);

    t2 = texture(layer_2T1, tv2);
    vec4 tex2 = texture(tex_2, tv2);

    t2_2 = texture(layer_2T2, tv2_2);
    vec4 tex3 = texture(tex_3, tv2_2);

    t1 = texture(layer_1T1, tv1);
    vec4 tex0 = texture(tex_0, tv1);
 
    t1_2 = texture(layer_1T2, tv1_2);
    vec4 tex1 = texture(tex_1, tv1_2);

    // ambient occusion is in blue channel of the normal maps.
    // Specular is in the red channel. Green and Alpha are normal values.
    // We must get the Ambient Occlusion before converting so it isn't lost.

    // Get and convert normal maps. Save ambient occlusion value.
    n4 = texture(n_layer_4T1, tv4);
    aoc_6 = n4.b;
    n4 = convertNormal(n4) + layer3UT1;

    n4_2 = texture(n_layer_4T2, tv4_2);
    aoc_7 = n4_2.b;
    n4_2 = convertNormal(n4_2) + layer3UT2;

    n3 = texture(n_layer_3T1, tv3);
    aoc_4 = n3.b;
    n3 = convertNormal(n3) + layer2UT1;

    n3_2 = texture(n_layer_3T2, tv3_2);
    aoc_5 = n3_2.b;
    n3_2 = convertNormal(n3_2) + layer2UT2;

    n2 = texture(n_layer_2T1, tv2);
    aoc_2 = n2.b;
    n2 = convertNormal(n2) + layer1UT1;

    n2_2 = texture(n_layer_2T2, tv2_2);
    aoc_3 = n2_2.b;
    n2_2 = convertNormal(n2_2) + layer1UT2;

    n1 = texture(n_layer_1T1, tv1);
    aoc_0 = n1.b;
    n1 = convertNormal(n1) + layer0UT1;

    n1_2 = texture(n_layer_1T2, tv1_2);
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
    n4.rgb = TBN * normalize(n4.rgb) * MixLevel4.r * used_7;
    n4_2.rgb = TBN * normalize(n4_2.rgb) * MixLevel4.g * used_8;

    // Mix group 3
    n3.rgb = TBN * normalize(n3.rgb) * MixLevel3.r * used_5;
    n3_2.rgb = TBN * normalize(n3_2.rgb) * MixLevel3.g * used_6;

    // Mix group 2
    n2.rgb = TBN * normalize(n2.rgb) * MixLevel2.r * used_3;
    n2_2.rgb = TBN * normalize(n2_2.rgb) * MixLevel2.g * used_4;

    // Mix group 1
    n1.rgb = TBN * normalize(n1.rgb) * MixLevel1.r * used_1;
    n1_2.rgb = TBN * normalize(n1_2.rgb) * MixLevel1.g * used_2;


    //flip X axis. Everything is flipped on X including texture rotations.

    n1.x *= -1.0;
    n2.x *= -1.0;
    n3.x *= -1.0;
    n4.x *= -1.0;

    n1_2.x *= -1.0;
    n2_2.x *= -1.0;
    n3_2.x *= -1.0;
    n4_2.x *= -1.0;

    //-------------------------------------------------------------

    // This is needed to light the global_AM.
    vec4 g_nm = texture(normalMap, UV);
    vec4 n_tex = vec4(0.0);
    n_tex.xyz = normalize(TBN * vec3(convertNormal(g_nm).xyz));
    n_tex.x*=-1.0;
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

    // This blends between low and highrez by distance

    // This blends the layered colors/normals and the global_AM/normalMaps over distance.
    // The blend stats at 100 and ends at 400. This has been changed for debug
    // Replace ln with 1.0 to show only layered terrain.

    vec4 global = texture(global_AM, Global_UV);
    global.a = 1.0;

    base = mix(global, base, ln);

    out_n = mix(n_tex, out_n, ln) ;

    // The obvious
    gColor = base;
    gColor.a = 1.0;

    gNormal.xyz = normalize(out_n.xyz);
    gGMF.rgb = vec3(0.0, 0.0, 128.0/255.0);

    gPosition = worldPosition;
}
