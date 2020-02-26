#version 430 core

uniform vec2 center;
uniform float radius;
uniform float thickness;
uniform vec4 color;

out vec4 fragColor;

in vec2 texCoord;

void main(void)
{
  vec2 uv = texCoord.xy;
 
  // Offset uv with the center of the circle.
  uv += center.xy;

  float dist =  sqrt(dot(uv, uv));

  float t = 1.0 + smoothstep(radius, radius+thickness, dist) 
                - smoothstep(radius-thickness, radius, dist);
  fragColor.a = 1.0-t;
  fragColor.xyz = color.xyz;
}
