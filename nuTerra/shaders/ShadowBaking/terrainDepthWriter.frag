#version 450 core

out vec4 color;
in vec2 uv;
layout(binding = 0) uniform sampler2DArray textArrayC;
uniform int map_id;

void main(void){

    vec4 ArrayTextureC = texture(textArrayC, vec3(uv, map_id) );

    if (uv.x < 0.005 || uv.x > 0.995 || uv.y < 0.005 || uv.y > 0.995)
        { ArrayTextureC = vec4(0.9,0.9,0.9,0.0); }

    color = ArrayTextureC;
}