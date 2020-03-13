﻿// gWriter fragment Shader. We will use this as a template for other shaders
#version 430 core

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec3 gGMF;
layout (location = 3) out vec3 gPosition;

uniform sampler2D layer_1T1;
uniform sampler2D layer_2T1;
uniform sampler2D layer_3T1;
uniform sampler2D layer_4T1;

uniform sampler2D layer_1T2;
uniform sampler2D layer_2T2;
uniform sampler2D layer_3T2;
uniform sampler2D layer_4T2;

uniform sampler2D n_layer_1T1;
uniform sampler2D n_layer_2T1;
uniform sampler2D n_layer_3T1;
uniform sampler2D n_layer_4T1;

uniform sampler2D n_layer_1T2;
uniform sampler2D n_layer_2T2;
uniform sampler2D n_layer_3T2;
uniform sampler2D n_layer_4T2;

uniform sampler2D mixtexture1;
uniform sampler2D mixtexture2;
uniform sampler2D mixtexture3;
uniform sampler2D mixtexture4;

uniform vec4 layer0UT1;
uniform vec4 layer1UT1;
uniform vec4 layer2UT1;
uniform vec4 layer3UT1;

uniform vec4 layer0UT2;
uniform vec4 layer1UT2;
uniform vec4 layer2UT2;
uniform vec4 layer3UT2;


uniform vec4 layer0VT1;
uniform vec4 layer1VT1;
uniform vec4 layer2VT1;
uniform vec4 layer3VT1;

uniform vec4 layer0VT2;
uniform vec4 layer1VT2;
uniform vec4 layer2VT2;
uniform vec4 layer3VT2;

uniform float used_1;
uniform float used_2;
uniform float used_3;
uniform float used_4;

uniform float used_5;
uniform float used_6;
uniform float used_7;
uniform float used_8;

uniform int pbs_1;
uniform int pbs_2;
uniform int pbs_3;
uniform int pbs_4;

uniform int pbs_5;
uniform int pbs_6;
uniform int pbs_7;
uniform int pbs_8;


uniform sampler2D colorMap;
uniform sampler2D normalMap;
uniform sampler2D domTexture;

uniform int nMap_type = 1;

flat in int dom_id;

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
vec4 add_norms (in vec4 n1 , in vec4 n2){
    n1.xyz += n2.xyz;
    return (n1);   
}
//-------------------------------------------------------------
// Conversion from AG to RGB normal depending on nMap_type.
vec4 getNormal()
{
    vec3 n;
    if (nMap_type == 1 ) {
        // GA map
        // We must clamp and max these to -1.0 to 1.0 to stop artifacts!
        n.xy = clamp(texture(normalMap, UV).ag*2.0-1.0, -1.0 ,1.0);
        n.z = max(sqrt(1.0 - (n.x*n.x + n.y *n.y)),0.0);
        //n.xyz = n.xzy;
        //n.x*= -1.0;
    } else {
        // RGB map
        n = texture(normalMap, UV).rgb*2.0-1.0;
    }
    n = normalize(TBN * n);
    return vec4(n,1.0);
}
//-------------------------------------------------------------
// Converion from AG map to RGB vector.
vec4 convertNormal(vec4 norm, int pbs){
//PBS GA map?
if (pbs == 1)
    {
        vec3 n;
        n.xy = clamp(norm.ag*2.0-1.0, -1.0 ,1.0);
        n.z = max(sqrt(1.0 - (n.x*n.x + n.y *n.y)),0.0);
        n.xyz = n.xzy;
        return vec4(n,1.0);
    }else{
        return norm;
    }
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

    vec2 mix_coords;

    mix_coords = UV;
    mix_coords.x = 1.0 - mix_coords.x;
    vec2 UVs = UV;
    // Rotate the AM and NM textures. The order here is not important but I am sticking to protocol.
    // Layer 4 ---------------------------------------------
    // 7
    tv4 = vec2(dot(-layer2UT1, Vertex), dot(layer2VT1, Vertex));
    t4 = texture(layer_4T1, -tv4 + 0.5 );
    n4 = texture(n_layer_4T1, -tv4 + 0.5);
    n4 = convertNormal(n4, pbs_7)+layer3UT1;
    // 8
    tv4_2 = vec2(dot(-layer2UT2, Vertex), dot(layer2VT2, Vertex));
    t4_2 = texture(layer_4T2, -tv4_2 + 0.5);
    n4_2 = texture(n_layer_4T2, -tv4_2 + 0.5);
    n4_2 = convertNormal(n4_2, pbs_8)+layer3UT2;

    // layer 3 ---------------------------------------------
    // 5
    tv3 = vec2(dot(-layer1UT1, Vertex), dot(layer1VT1, Vertex));
    t3 = texture(layer_3T1, -tv3 + 0.5);
    n3 = texture(n_layer_3T1, -tv3 + 0.5);
    n3 = convertNormal(n3, pbs_5)+layer3UT1;
    // 6
    tv3_2 = vec2(dot(-layer1UT2, Vertex), dot(layer1VT2, Vertex));
    t3_2 = texture(layer_3T2, -tv3_2 + 0.5);
    n3_2 = texture(n_layer_3T2, -tv3_2 + 0.5);
    n3_2 = convertNormal(n3_2, pbs_6)+layer3UT2;

    // layer 2 ---------------------------------------------
    // 3
    tv2 = vec2(dot(-layer3UT1, Vertex), dot(layer3VT1, Vertex));
    t2 = texture(layer_2T1, -tv2 + 0.5);
    n2 = texture(n_layer_2T1, -tv2 + 0.5);
    n2 = convertNormal(n2, pbs_3)+layer1UT1;
    // 4
    tv2_2 = vec2(dot(-layer3UT2, Vertex), dot(layer3VT2, Vertex));
    t2_2 = texture(layer_2T2, -tv2_2 + 0.5);
    n2_2 = texture(n_layer_2T2, -tv2_2 + 0.5);
    n2_2 = convertNormal(n2_2, pbs_4)+layer1UT2;

    // layer 1 ---------------------------------------------
    // 1
    tv1 = vec2(dot(-layer0UT1, Vertex), dot(layer0VT1, Vertex));
    t1 = texture(layer_1T1, -tv1 + 0.5);
    n1 = texture(n_layer_1T1, -tv1 + 0.5);
    n1 = convertNormal(n1, pbs_1)+layer0UT1;
    // 2
    tv1_2 = vec2(dot(-layer0UT2, Vertex), dot(layer0VT2, Vertex));
    t1_2 = texture(layer_1T2, -tv1_2 + 0.5);
    n1_2 = texture(n_layer_1T2, -tv1_2 + 0.5);
    n1_2 = convertNormal(n1_2, pbs_2)+layer0UT2;
    //
   
    // Hoping this works
    UVs.x = 1.0 - UVs.x;

    //switch (dom_id){
    //case 1:
    //      t1 = texture(layer_1T1, UVs + 0.5);
    //      n1 = texture(n_layer_1T1, UVs + 0.5);
    //      n1 = convertNormal(n1, pbs_1);
    //case 2:
    //      t1_2 = texture(layer_1T2, UVs + 0.5);
    //      n1_2 = texture(n_layer_1T2, UVs + 0.5);
    //      n1_2 = convertNormal(n1_2, pbs_2);
    //case 3:
    //      t2 = texture(layer_2T1, UVs + 0.5);
    //      n2 = texture(n_layer_2T1, UVs + 0.5);
    //      n2 = convertNormal(n2, pbs_3);
    //case 4:
    //      t2_2 = texture(layer_2T2, UVs + 0.5);
    //      n2_2 = texture(n_layer_2T2, UVs + 0.5);
    //      n2_2 = convertNormal(n2_2, pbs_4);
    //case 5:
    //      t3 = texture(layer_3T1, UVs + 0.5);
    //      n3 = texture(n_layer_3T1, UVs + 0.5);
    //      n3 = convertNormal(n3, pbs_5);
    //case 6:
    //      t3_2 = texture(layer_3T2, UVs + 0.5);
    //      n3_2 = texture(n_layer_3T2, UVs + 0.5);
    //      n3_2 = convertNormal(n3_2, pbs_6);
    //case 7:
    //      t4 = texture(layer_4T1, UVs + 0.5);
    //      n4 = texture(n_layer_4T1, UVs + 0.5);
    //      n4 = convertNormal(n4, pbs_7);
    //case 8:
    //      t4_2 = texture(layer_4T2, UVs + 0.5);
    //      n4_2 = texture(n_layer_4T2, UVs + 0.5);
    //      n4_2 = convertNormal(n4_2, pbs_8);
    //}


    //Get the mix values from the mix textures 1-4 and move to vec2. 
    MixLevel1.rg = texture(mixtexture1, mix_coords.xy).ag;
    MixLevel2.rg = texture(mixtexture2, mix_coords.xy).ag;
    MixLevel3.rg = texture(mixtexture3, mix_coords.xy).ag;
    MixLevel4.rg = texture(mixtexture4, mix_coords.xy).ag;
   
    int domVal = int(texture(domTexture,UV).r*255.0)>>4;


    vec4 base = vec4(0.0);  
    vec4 empty = vec4(0.0);


    // used_1 to used_8 are either 0 or 1 depending on if the slot is used. Used to clamp 0.0, 1.0

    // mix our textures in to base.
    // Mix group 4
    base += t4 * MixLevel4.r * used_7;
    base += t4_2 * MixLevel4.g * used_8;

    // Mix group 3
    base += t3 * MixLevel3.r * used_5;
    base += t3_2 * MixLevel3.g * used_6;

    // Mix group 2
    base += t2 * MixLevel2.r * used_3;
    base += t2_2 * MixLevel2.g * used_4;

    // Mix group 1
    base += t1 * MixLevel1.r * used_1;
    base += t1_2 * MixLevel1.g * used_2;
    

    //Get our normal maps. Same mixing and clamping as AM maps above

    // Mix group 4
    n4.rgb = normalize(2.0 * n4.rgb - 1.0) * MixLevel4.r * used_7;
    n4_2.rgb = normalize(2.0 * n4_2.rgb - 1.0) * MixLevel4.g * used_8;

    // Mix group 3
    n3.rgb = normalize(2.0 * n3.rgb - 1.0) * MixLevel3.r * used_5;
    n3_2.rgb = normalize(2.0 * n3_2.rgb - 1.0) * MixLevel3.g * used_6;

    // Mix group 2
    n2.rgb = normalize(2.0 * n2.rgb - 1.0) * MixLevel2.r * used_3;
    n2_2.rgb = normalize(2.0 * n2_2.rgb - 1.0) * MixLevel2.g * used_4;

    // Mix group 1
    n1.rgb = normalize(2.0 * n1.rgb - 1.0) * MixLevel1.r * used_1;
    n1_2.rgb = normalize(2.0 * n1_2.rgb - 1.0) * MixLevel1.g * used_2;


    //flip X axis. Everything is flipped on X including texture rotations.

    n1.x *= -1.0;
    n2.x *= -1.0;
    n3.x *= -1.0;
    n4.x *= -1.0;

    n1_2.x *= -1.0;
    n2_2.x *= -1.0;
    n3_2.x *= -1.0;
    n4_2.x *= -1.0;


    // Transform normals by TBN. Clamp again using used_ values just to make sure.
    n4.rgb = (TBN * n4.rgb) * used_7;
    n4_2.rgb = (TBN * n4_2.rgb) * used_8;

    n3.rgb = (TBN * n3.rgb) * used_5;
    n3_2.rgb = (TBN * n3_2.rgb) * used_6;

    n2.rgb = (TBN * n2.rgb) * used_3;
    n2_2.rgb = (TBN * n2_2.rgb) * used_4;

    n1.rgb = (TBN * n1.rgb) * used_1;
    n1_2.rgb = (TBN * n1_2.rgb) * used_2;

    //-------------------------------------------------------------

    // This is needed to light the global_AM.
    vec4 n_tex = getNormal();
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
    vec4 dom = texture(domTexture,UV);
    vec4 global = texture(colorMap, Global_UV);
    global.a = 1.0;
    base = mix(global, base, ln) ;
    //base.rgb = mix(dom.rgb, base.rgb, ln) ;
    out_n = mix(n_tex, out_n, ln) ;
    //base.rgb *= global.rgb*global.a;
    // The obvious
    gColor = base;
    gColor.a = 1.0;

    gNormal.xyz = normalize(out_n.xyz);
    gGMF.rgb = vec3(0.0, 0.0, 128.0/255.0);

    gPosition = worldPosition;
}
