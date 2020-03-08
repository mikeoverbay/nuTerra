// gWriter fragment Shader. We will use this as a template for other shaders
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

uniform sampler2D colorMap;
uniform sampler2D normalMap;

uniform int nMap_type = 1;

uniform vec2 map_size;
uniform vec2 map_pos;

in float ln;
in vec4 mask_1;
in vec4 mask_2;

in mat3 TBN;
in vec3 worldPosition;
in vec4 Vertex;

in vec2 UV;
in vec2 Global_UV;
in vec3 normal;//temp for debuging lighting
flat in uint is_hole;


/*===========================================================*/
vec4 add_norms (in vec4 n1 , in vec4 n2){
    n1.xyz += n2.xyz;
    return (n1);   
}
vec4 getNormal()
{
    vec3 n;
    if (nMap_type == 1 ) {
        // GA map
        // We must clamp and max these to -1.0 to 1.0 to stop artifacts!
        n.xy = clamp(texture(normalMap, UV).ag*2.0-1.0, -1.0 ,1.0);
        n.y = max(sqrt(1.0 - (n.x*n.x + n.y *n.y)),0.0);
        n.xyz = n.xzy;
    } else {
        // RGB map
        n = texture(normalMap, UV).rgb*2.0-1.0;
    }
    n = normalize(TBN * n);
    return vec4(n,1.0);
}
/*===========================================================*/


void main(void)
{

    if (is_hole > 0) discard; // Early discard to avoid wasting draw time.

    vec4 t1, t2, t3, t4;
    vec4 t1_2, t2_2, t3_2, t4_2;
    vec4 n1, n2, n3, n4;
    vec4 n1_2, n2_2, n3_2, n4_2;
    vec2 MixLevel1, MixLevel2, MixLevel3, MixLevel4;
    vec4 Nmix1, Coutmix, Noutmix;
    vec2 mix_coords;
    vec3 PN1, PN2, PN3, PN4;
    vec2 tv4, tv4_2, tv3, tv3_2;
    vec2 tv2, tv2_2, tv1, tv1_2;


    mix_coords = UV;
    mix_coords.x = 1.0 - mix_coords.x;

    tv4 = vec2(dot(-layer3UT1, Vertex), dot(layer3VT1, Vertex));

    t4 = texture2D(layer_4T1, -tv4 + 0.5 );


    n4 = texture2D(n_layer_4T1, -tv4 + .5);
    //
    tv4_2 = vec2(dot(-layer3UT2, Vertex), dot(layer3VT2, Vertex));
    t4_2 = texture2D(layer_4T2, -tv4_2 + .5);
    n4_2 = texture2D(n_layer_4T2, -tv4_2 + .5);

    // layer 3 ---------------------------------------------
    tv3 = vec2(dot(-layer2UT1, Vertex), dot(layer2VT1, Vertex));
    t3 = texture2D(layer_3T1, -tv3 + .5);
    n3 = texture2D(n_layer_3T1, -tv3 + .5);
    //
    tv3_2 = vec2(dot(-layer2UT2, Vertex), dot(layer2VT2, Vertex));
    t3_2 = texture2D(layer_3T2, -tv3_2 + .5);
    n3_2 = texture2D(n_layer_3T2, -tv3_2 + .5);

    // layer 2 ---------------------------------------------
    tv2 = vec2(dot(-layer1UT1, Vertex), dot(layer1VT1, Vertex));
    t2 = texture2D(layer_2T1, -tv2 + .5);
    n2 = texture2D(n_layer_2T1, -tv2 + .5);
    //
    tv2_2 = vec2(dot(-layer1UT2, Vertex), dot(layer1VT2, Vertex));
    t2_2 = texture2D(layer_2T2, -tv2_2 + .5);
    n2_2 = texture2D(n_layer_2T2, -tv2_2 + .5);

    // layer 1 ---------------------------------------------
    tv1 = vec2(dot(-layer0UT1, Vertex), dot(layer0VT1, Vertex));
    t1 = texture2D(layer_1T1, -tv1 + .5);
    n1 = texture2D(n_layer_1T1, -tv1 + .5);
    //
    tv1_2 = vec2(dot(-layer0UT2, Vertex), dot(layer0VT2, Vertex));
    t1_2 = texture2D(layer_1T2, -tv1_2 + .5);
    n1_2 = texture2D(n_layer_1T2, -tv1_2 + .5);
    //

     
    MixLevel1.rg = texture2D(mixtexture1, mix_coords.xy).ag;
    MixLevel2.rg = texture2D(mixtexture2, mix_coords.xy).ag;
    MixLevel3.rg = texture2D(mixtexture3, mix_coords.xy).ag;
    MixLevel4.rg = texture2D(mixtexture4, mix_coords.xy).ag;
   
    vec4 empty = vec4(0.0);

    //mask out empty textures.

    t1 = mix(t1, empty, mask_1.a);
    t1_2 = mix(t1_2, empty, mask_1.b);
    t2 = mix(t2, empty, mask_1.g);
    t2_2 = mix(t2_2, empty, mask_1.r);

    t3 = mix(t3, empty, mask_2.a);
    t3_2 = mix(t3_2, empty, mask_2.b);
    t4 = mix(t4, empty, mask_2.g);
    t4_2 = mix(t4_2, empty, mask_2.r);


    //Now we mix our color
    vec4 base = vec4(0.0);  

    base += t4 * MixLevel4.r ;
    base += t4_2 * MixLevel4.g ;

    base += t3 * MixLevel3.r ;
    base += t3_2 * MixLevel3.g ;

    base += t2 * MixLevel2.r ;
    base += t2_2 * MixLevel2.g ;

    base += t1 * MixLevel1.r ;
    base += t1_2 * MixLevel1.g ;
   


        //Get our normal maps.
    n1.rgb = normalize(2.0 * n1.rgb - 1.0) * MixLevel1.r;
    n1_2.rgb = normalize(2.0 * n1_2.rgb - 1.0) * MixLevel1.g;

    n2.rgb = normalize(2.0 * n2.rgb - 1.0) * MixLevel2.r;
    n2_2.rgb = normalize(2.0 * n2_2.rgb - 1.0) * MixLevel2.g;

    n3.rgb = normalize(2.0 * n3.rgb - 1.0) * MixLevel3.r;
    n3_2.rgb = normalize(2.0 * n3_2.rgb - 1.0) * MixLevel3.g;

    n4.rgb = normalize(2.0 * n4.rgb - 1.0) * MixLevel4.r;
    n4_2.rgb = normalize(2.0 * n4_2.rgb - 1.0) * MixLevel4.g;
    //flip Y axis


    n1.rgb = (TBN * n1.rgb) * MixLevel1.r;
    n1_2.rgb = (TBN * n1_2.rgb) * MixLevel1.g;

    n2.rgb = (TBN * n2.rgb) * MixLevel2.r;
    n2_2.rgb = (TBN * n2_2.rgb) * MixLevel2.g;

    n3.rgb = (TBN * n3.rgb) * MixLevel3.r;
    n3_2.rgb = (TBN * n3_2.rgb) * MixLevel3.g;

    n4.rgb = (TBN * n4.rgb) * MixLevel4.r;
    n4_2.rgb = (TBN * n4_2.rgb) * MixLevel4.g;

    //-------------------------------------------------------------

    //It just looks better with the noise normal
    vec4 out_n = getNormal();

    out_n = add_norms(out_n, mix(n1, empty, mask_1.a));
    out_n = add_norms(out_n, mix(n1_2, empty, mask_1.b));
    out_n = add_norms(out_n, mix(n2, empty, mask_1.g));
    out_n = add_norms(out_n, mix(n2_2, empty, mask_1.r));
    out_n = add_norms(out_n, mix(n3, empty, mask_2.a));
    out_n = add_norms(out_n, mix(n3_2, empty, mask_2.b));
    out_n = add_norms(out_n, mix(n4, empty, mask_2.g));
    out_n = add_norms(out_n, mix(n4_2, empty, mask_2.r));

    //This blends between low and highrez by distance

    //DISABLED UNTIL WE SORT THE REST OUT!
    base.rgb = mix(texture2D(colorMap, Global_UV).rgb, base.rgb, ln*2.0) ;

    gColor = base;
    gColor.a = 1.0;

    gNormal.xyz = normalize(out_n.xyz);
    gGMF.rgb = vec3(0.0, 0.0, 128.0/255.0);

    gPosition = worldPosition;
}
