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
  bool lessthan = dist > radius+thickness;
  bool greaterthan = dist < radius-thickness;
  if(lessthan || greaterthan){
    discard;
    }else{
    fragColor = color;
    }
}
