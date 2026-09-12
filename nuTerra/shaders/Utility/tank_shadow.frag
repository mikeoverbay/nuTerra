#version 450 core

// Nothing to write. The depth attachment is the whole output - the framebuffer
// has no colour buffer at all (NamedFramebufferDrawBuffer None), so a colour
// output here would be written nowhere.
void main(void) { }
