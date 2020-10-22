#version 450 core

out vec4 fragColor;
uniform sampler2D imageMap;
uniform vec4 color;
uniform int mask;
in vec2 texCoord;

void main(void)
{
    if (mask==1)
    {
        fragColor.a=1.0;
        vec4 co = texture(imageMap, texCoord) * color;
        fragColor.rgb = co.rgb * co.a;
        fragColor.rgb += vec3(0.3 ,0.3, 0.3)* (1.0-co.a);
    }else{
        fragColor = texture(imageMap, texCoord) * color;
}
}