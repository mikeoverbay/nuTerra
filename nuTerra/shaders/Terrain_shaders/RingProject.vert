//water shader.. very basic
layout(location = 0) in vec3 vertexPosition;

#version 330 compatibility

uniform vec3 ring_center;
uniform float thickness;

uniform mat4 projMatrix;
uniform mat4 viewMatrix
uniform mat4 modelMatrix;
uniform vec4 color_in;
out mat4 matPrjInv;
out vec4 positionSS;
out vec4 positionWS;
out vec4 color;

void main(void)
{
    color = color_in;
    vec4 local = vertexPosition;
    local.xyz  += ring_center.xyz;
    
    gl_Position = projMatrix * viewMatrix * modelMatrix * local;
    positionSS = gl_Position;

    positionWS = local;
    matPrjInv = inverse(projMatrix * viewMatrix * modelMatrix);

}
