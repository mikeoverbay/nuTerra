#version 450 core

in vec4 fCol;
in vec3 fWorld;

// Set below 1 for the pass that draws the occluded part of the path. Drawing
// it twice - once depth tested, once not - is what makes the overlay readable:
// solid where the route is actually visible, ghosted where a hill or a building
// is in front of it. One pass alone is either misleading or hides the answer.
uniform float alpha_mul;

// Blanking the stretch the camera is standing on, by distance from the eye
// rather than by position along the route. While flying, the path runs THROUGH
// the eye, so the near part of it lies down the middle of the screen and hides
// the very thing it was drawn to show.
//
// Distance beats cutting the route by arc length: it is one subtract per
// fragment with no index arithmetic, it needs no special case where the loop
// joins, and it also blanks a LATER lap that happens to pass close by - which
// arc length cannot see at all.
//
// hide_from is the eye. Inside hide_near the line is gone; from there it fades
// up, full strength by hide_far. The fade is the point: a hard edge alone
// either leaves the line in your face or deletes so much of it that there is
// nothing left to fly by. Set hide_far <= hide_near to switch the whole thing
// off, which is what happens when the camera is not flying.
uniform vec3 hide_from;
uniform float hide_near;
uniform float hide_far;

out vec4 fragColor;

void main(void)
{
    float d = distance(fWorld, hide_from);
    if (d < hide_near) discard;

    float vis = (hide_far > hide_near) ? smoothstep(hide_near, hide_far, d) : 1.0;

    fragColor = vec4(fCol.rgb, fCol.a * alpha_mul * vis);
}
