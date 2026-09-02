#version 450 core

in vec4 fCol;

// Set below 1 for the pass that draws the occluded part of the path. Drawing
// it twice - once depth tested, once not - is what makes the overlay readable:
// solid where the route is actually visible, ghosted where a hill or a building
// is in front of it. One pass alone is either misleading or hides the answer.
uniform float alpha_mul;

out vec4 fragColor;

void main(void)
{
    fragColor = vec4(fCol.rgb, fCol.a * alpha_mul);
}
