//Basic Shader for testing shit

#version 330 compatibility

uniform sampler2D colorMap;
out vec4 gColor;
varying  vec2 UV;
in vec4 color;
in vec3 n;
void main (void)
{
    vec4 text_color = texture2D(colorMap, UV);
    //text_color.xyz += n.xyz;
    text_color.xy += UV.xy;
    gColor =  text_color;//+color*0.2;
    gColor.a = 1.0;
}
