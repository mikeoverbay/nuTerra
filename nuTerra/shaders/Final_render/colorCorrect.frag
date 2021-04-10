 #version 450 core

 // from : https://defold.com/tutorials/grading/
 out vec4 color_out;

 uniform lowp sampler2D colorMap;
 uniform lowp sampler2D lut; // <1>

 #define MAXCOLOR 15.0
 #define COLORS 16.0
 #define WIDTH 256.0
 #define HEIGHT 16.0

 in VS_OUT {
    vec2 UV;
} fs_in;

 void main()
 {
     vec4 px = texture2D(colorMap, fs_in.UV);

    float cell = px.b * MAXCOLOR;

    float cell_l = floor(cell); // <1>
    float cell_h = ceil(cell);

    float half_px_x = 0.5 / WIDTH;
    float half_px_y = 0.5 / HEIGHT;
    float r_offset = half_px_x + px.r / COLORS * (MAXCOLOR / COLORS);
    float g_offset = half_px_y + px.g * (MAXCOLOR / COLORS);

    vec2 lut_pos_l = vec2(cell_l / COLORS + r_offset, g_offset); // <2>
    vec2 lut_pos_h = vec2(cell_h / COLORS + r_offset, g_offset);

    vec4 graded_color_l = texture2D(lut, lut_pos_l); // <3>
    vec4 graded_color_h = texture2D(lut, lut_pos_h);

    // <4>
    vec4 graded_color = mix(graded_color_l, graded_color_h, fract(cell));

    color_out = graded_color; // <9>
 
 }