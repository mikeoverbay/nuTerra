#version 450 core
out vec4 fragColour;
uniform vec3 colour;
// Flat and unlit on purpose: a base ring is a MARKING, not a surface, and
// shading it would make it read as painted geometry that a tank might climb.
void main(void) { fragColour = vec4(colour, 1.0); }
