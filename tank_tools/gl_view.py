"""GPU drawing for the ray studio: textures for the map, one VBO for the rest.

WHY. The viewer was pure pygame - CPU surfaces and one draw call per line.
Measured on a real frame with a 3,858 point tree:

    block layer scale   14.6 ms      <- the whole map rescaled every frame
    map slice scale      2.1 ms
    tree lines           2.5 ms      (3,858 separate calls)
    rings                1.7 ms

The owner: "anything but single line render calls." Right, and the biggest
single cost was not even the lines - it was rescaling a 1400 square RGBA
surface on the CPU sixty times a second to draw a picture that had not
changed. On the GPU that is a texture and a quad, and it costs nothing until
the data itself changes.

WebGL is a browser thing; the equivalent here is OpenGL through PyOpenGL,
which is already a dependency because Flight Studio's GLView uses it. This
follows that file's pattern deliberately - GL 3.3 core, compileProgram, a HUD
program that draws a texture in pixel space - so there is one way of doing
this in the project rather than two.

WHAT STAYS ON THE CPU: the panels. Text in raw GL means a glyph atlas and it
would be a day's work to be worse than pygame's font module. The panels are
drawn to a pygame surface exactly as before and uploaded as one texture, which
is 580x1000 rather than the whole window.
"""

import numpy as np
from OpenGL import GL
from OpenGL.GL import shaders as glshaders

# Everything is drawn in PIXELS with the origin top left, so the shaders take
# the window size and do the projection themselves. No matrix juggling for a
# 2-D view, and it matches how the pygame version addressed the screen.
_QUAD_VERT = """
#version 330 core
layout(location = 0) in vec2 a_pos;
layout(location = 1) in vec2 a_uv;
uniform vec2 u_win;
out vec2 v_uv;
void main() {
    vec2 p = vec2(a_pos.x / u_win.x * 2.0 - 1.0,
                  1.0 - a_pos.y / u_win.y * 2.0);
    gl_Position = vec4(p, 0.0, 1.0);
    v_uv = a_uv;
}
"""

_QUAD_FRAG = """
#version 330 core
in vec2 v_uv;
uniform sampler2D u_tex;
uniform float u_alpha;
out vec4 frag;
void main() {
    vec4 c = texture(u_tex, v_uv);
    frag = vec4(c.rgb, c.a * u_alpha);
}
"""

_LINE_VERT = """
#version 330 core
layout(location = 0) in vec2 a_pos;
layout(location = 1) in vec4 a_col;
uniform vec2 u_win;
out vec4 v_col;
void main() {
    vec2 p = vec2(a_pos.x / u_win.x * 2.0 - 1.0,
                  1.0 - a_pos.y / u_win.y * 2.0);
    gl_Position = vec4(p, 0.0, 1.0);
    v_col = a_col;
}
"""

_LINE_FRAG = """
#version 330 core
in vec4 v_col;
out vec4 frag;
void main() { frag = v_col; }
"""


class GLView(object):
    """A 2-D GPU surface: textured quads and one batched line/point buffer."""

    def __init__(self):
        self.quad = glshaders.compileProgram(
            glshaders.compileShader(_QUAD_VERT, GL.GL_VERTEX_SHADER),
            glshaders.compileShader(_QUAD_FRAG, GL.GL_FRAGMENT_SHADER))
        self.line = glshaders.compileProgram(
            glshaders.compileShader(_LINE_VERT, GL.GL_VERTEX_SHADER),
            glshaders.compileShader(_LINE_FRAG, GL.GL_FRAGMENT_SHADER))
        self.u_quad_win = GL.glGetUniformLocation(self.quad, "u_win")
        self.u_quad_alpha = GL.glGetUniformLocation(self.quad, "u_alpha")
        self.u_line_win = GL.glGetUniformLocation(self.line, "u_win")

        # A core profile refuses to draw without a bound VAO, and one is
        # enough here: every buffer below has the same two attributes.
        self.vao = GL.glGenVertexArrays(1)
        self.quad_vbo = GL.glGenBuffers(1)
        self.line_vbo = GL.glGenBuffers(1)
        self._line_cap = 0
        self.textures = {}
        self._fbos = {}
        self._max_line_w = None
        GL.glEnable(GL.GL_BLEND)
        GL.glBlendFunc(GL.GL_SRC_ALPHA, GL.GL_ONE_MINUS_SRC_ALPHA)
        GL.glDisable(GL.GL_DEPTH_TEST)

    # ---------------------------------------------------------------- textures
    @property
    def max_line_w(self):
        """The widest GL_LINES this driver will actually draw.

        glLineWidth above the ceiling does not fail - it silently clamps - so
        a pick pass that asks for 14 and gets 1 would look like picking simply
        does not work on that machine. Measured here: 10.0 on this one.
        """
        if self._max_line_w is None:
            self._max_line_w = float(
                GL.glGetFloatv(GL.GL_ALIASED_LINE_WIDTH_RANGE)[1])
        return self._max_line_w

    def upload(self, name, arr):
        """RGB or RGBA numpy array -> a texture, kept under `name`.

        Only called when the DATA changes. That is the whole win: the block
        layer used to be rescaled on the CPU every frame whether it had
        changed or not, and now a pan or a zoom costs nothing at all.
        """
        h, w = arr.shape[:2]
        if arr.shape[2] == 3:
            arr = np.dstack([arr, np.full((h, w, 1), 255, np.uint8)])
        arr = np.ascontiguousarray(arr)
        tex = self.textures.get(name)
        if tex is None:
            tex = GL.glGenTextures(1)
            self.textures[name] = tex
        GL.glBindTexture(GL.GL_TEXTURE_2D, tex)
        # NEAREST, not linear. This is a picture of CELLS and the question
        # asked of it is whether a ray fits through a gap; blurred, that is
        # unanswerable at exactly the moment it matters.
        GL.glTexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MIN_FILTER, GL.GL_NEAREST)
        GL.glTexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MAG_FILTER, GL.GL_NEAREST)
        GL.glTexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_S, GL.GL_CLAMP_TO_EDGE)
        GL.glTexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_T, GL.GL_CLAMP_TO_EDGE)
        GL.glTexImage2D(GL.GL_TEXTURE_2D, 0, GL.GL_RGBA8, w, h, 0,
                        GL.GL_RGBA, GL.GL_UNSIGNED_BYTE, arr.tobytes())
        return tex

    def has(self, name):
        return name in self.textures

    # ------------------------------------------------------------------ frame
    def begin(self, win_w, win_h, clear=(0.04, 0.04, 0.05)):
        self.win = (float(win_w), float(win_h))
        GL.glViewport(0, 0, win_w, win_h)
        GL.glClearColor(clear[0], clear[1], clear[2], 1.0)
        GL.glClear(GL.GL_COLOR_BUFFER_BIT)
        GL.glBindVertexArray(self.vao)

    def blit(self, name, dst, src_uv=(0.0, 0.0, 1.0, 1.0), alpha=1.0):
        """Draw a texture into a pixel rect, optionally a sub-rectangle of it.

        src_uv is (u0, v0, u1, v1) so the caller can show the same slice of
        the map the CPU version used to subsurface out - the difference being
        that this costs one quad instead of a rescale of the whole image.
        """
        tex = self.textures.get(name)
        if tex is None:
            return
        x, y, w, h = dst
        u0, v0, u1, v1 = src_uv
        verts = np.array([
            x, y, u0, v0,  x + w, y, u1, v0,  x + w, y + h, u1, v1,
            x, y, u0, v0,  x + w, y + h, u1, v1,  x, y + h, u0, v1,
        ], dtype=np.float32)
        GL.glUseProgram(self.quad)
        GL.glUniform2f(self.u_quad_win, self.win[0], self.win[1])
        GL.glUniform1f(self.u_quad_alpha, float(alpha))
        GL.glActiveTexture(GL.GL_TEXTURE0)
        GL.glBindTexture(GL.GL_TEXTURE_2D, tex)
        GL.glBindBuffer(GL.GL_ARRAY_BUFFER, self.quad_vbo)
        GL.glBufferData(GL.GL_ARRAY_BUFFER, verts.nbytes, verts, GL.GL_STREAM_DRAW)
        GL.glEnableVertexAttribArray(0)
        GL.glVertexAttribPointer(0, 2, GL.GL_FLOAT, False, 16, GL.ctypes.c_void_p(0))
        GL.glEnableVertexAttribArray(1)
        GL.glVertexAttribPointer(1, 2, GL.GL_FLOAT, False, 16, GL.ctypes.c_void_p(8))
        GL.glDrawArrays(GL.GL_TRIANGLES, 0, 6)

    def draw(self, mode, verts, cols, width=1.0):
        """One draw call for every line, or every point, in the frame.

        verts is (N,2) float pixels and cols is (N,4) float 0..1. The caller
        builds them with numpy - which is the point: 3,858 pygame.draw.line
        calls become one buffer upload and one glDrawArrays.
        """
        n = len(verts)
        if n == 0:
            return
        data = np.empty((n, 6), dtype=np.float32)
        data[:, :2] = verts
        data[:, 2:] = cols
        GL.glUseProgram(self.line)
        GL.glUniform2f(self.u_line_win, self.win[0], self.win[1])
        GL.glBindBuffer(GL.GL_ARRAY_BUFFER, self.line_vbo)
        if data.nbytes > self._line_cap:
            GL.glBufferData(GL.GL_ARRAY_BUFFER, data.nbytes, None, GL.GL_STREAM_DRAW)
            self._line_cap = data.nbytes
        GL.glBufferSubData(GL.GL_ARRAY_BUFFER, 0, data.nbytes, data)
        GL.glEnableVertexAttribArray(0)
        GL.glVertexAttribPointer(0, 2, GL.GL_FLOAT, False, 24, GL.ctypes.c_void_p(0))
        GL.glEnableVertexAttribArray(1)
        GL.glVertexAttribPointer(1, 4, GL.GL_FLOAT, False, 24, GL.ctypes.c_void_p(8))
        if mode == "lines":
            GL.glLineWidth(width)
            GL.glDrawArrays(GL.GL_LINES, 0, n)
        else:
            GL.glPointSize(width)
            GL.glDrawArrays(GL.GL_POINTS, 0, n)

    def surface_texture(self, name, surface):
        """A pygame surface -> a texture. Used for the panels, text and all."""
        import pygame
        raw = pygame.image.tostring(surface, "RGBA", False)
        w, h = surface.get_size()
        arr = np.frombuffer(raw, np.uint8).reshape(h, w, 4)
        return self.upload(name, arr)

    # ------------------------------------------------------------------ fbo
    def fbo(self, name, w, h):
        """An offscreen colour buffer of this size, created or resized.

        The map is drawn into one of these and then put on a single quad in
        the centre panel. Two reasons it is worth the extra pass: the map can
        be rendered at the PANEL's aspect instead of a square that leaves black
        bars when the window is wide, and nothing drawn into it can spill into
        the panels, because it is physically a different surface.
        """
        w, h = max(1, int(w)), max(1, int(h))
        cur = self._fbos.get(name)
        if cur and cur[1] == (w, h):
            return cur[0]
        if cur:
            GL.glDeleteFramebuffers(1, [cur[0]])
            GL.glDeleteTextures([self.textures.pop(name, 0)])
        fb = GL.glGenFramebuffers(1)
        tex = GL.glGenTextures(1)
        GL.glBindTexture(GL.GL_TEXTURE_2D, tex)
        GL.glTexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MIN_FILTER, GL.GL_NEAREST)
        GL.glTexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_MAG_FILTER, GL.GL_NEAREST)
        GL.glTexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_S, GL.GL_CLAMP_TO_EDGE)
        GL.glTexParameteri(GL.GL_TEXTURE_2D, GL.GL_TEXTURE_WRAP_T, GL.GL_CLAMP_TO_EDGE)
        GL.glTexImage2D(GL.GL_TEXTURE_2D, 0, GL.GL_RGBA8, w, h, 0,
                        GL.GL_RGBA, GL.GL_UNSIGNED_BYTE, None)
        GL.glBindFramebuffer(GL.GL_FRAMEBUFFER, fb)
        GL.glFramebufferTexture2D(GL.GL_FRAMEBUFFER, GL.GL_COLOR_ATTACHMENT0,
                                  GL.GL_TEXTURE_2D, tex, 0)
        GL.glBindFramebuffer(GL.GL_FRAMEBUFFER, 0)
        self._fbos[name] = (fb, (w, h))
        self.textures[name] = tex
        return fb

    def begin_fbo(self, name, w, h, clear=(0.04, 0.04, 0.05)):
        """Draw into that buffer. Coordinates become 0..w, 0..h within it."""
        fb = self.fbo(name, w, h)
        GL.glBindFramebuffer(GL.GL_FRAMEBUFFER, fb)
        GL.glViewport(0, 0, int(w), int(h))
        GL.glClearColor(clear[0], clear[1], clear[2], 1.0)
        GL.glClear(GL.GL_COLOR_BUFFER_BIT)
        self.win = (float(w), float(h))

    def end_fbo(self, win_w, win_h):
        """Back to the window, and back to window coordinates."""
        GL.glBindFramebuffer(GL.GL_FRAMEBUFFER, 0)
        GL.glViewport(0, 0, int(win_w), int(win_h))
        self.win = (float(win_w), float(win_h))

    def pick(self, name, x, y):
        """The colour at one pixel of an offscreen buffer, as (r, g, b).

        SELECTION BY COLOUR. Draw the pickable things into their own buffer,
        each in a flat colour that encodes what it is and which one it is, and
        one pixel read then answers both questions at once. No hit-test
        geometry to keep in step with the drawing, no picking a line by
        distance to a segment, and a thing is grabbable exactly where it is
        visible - or wider, if it is drawn wider in the pick pass than on
        screen, which is how a small dot gets a big target.

        x and y are in the same TOP-LEFT-origin pixel coordinates everything
        is drawn in. glReadPixels counts rows from the bottom, hence the flip;
        without it the buffer reads upside down and every pick lands on
        whatever is mirrored about the middle of the panel.
        """
        cur = self._fbos.get(name)
        if cur is None:
            return (0, 0, 0)
        fb, (w, h) = cur
        x, y = int(x), int(y)
        if not (0 <= x < w and 0 <= y < h):
            return (0, 0, 0)
        GL.glBindFramebuffer(GL.GL_FRAMEBUFFER, fb)
        GL.glPixelStorei(GL.GL_PACK_ALIGNMENT, 1)
        raw = GL.glReadPixels(x, h - 1 - y, 1, 1,
                              GL.GL_RGBA, GL.GL_UNSIGNED_BYTE)
        GL.glBindFramebuffer(GL.GL_FRAMEBUFFER, 0)
        d = np.frombuffer(bytes(raw), dtype=np.uint8)
        return (int(d[0]), int(d[1]), int(d[2]))
