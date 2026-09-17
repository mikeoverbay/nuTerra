#version 450 core

// Straight through, alpha and all. The scope is a HUD: it is not lit, not
// fogged and not depth-tested, and anything done to it here would have to be
// undone in the eye of whoever is reading it.
in vec4 colour;
out vec4 fragColour;

void main(void)
{
    fragColour = colour;
}
