#version 450 core

in vec2 vUV;
in vec3 vWorld;
in vec3 vNormal;

uniform sampler2D uTex;
uniform int  uMode;      // 0 textured, 1 flat colour, 2 UV debug, 3 normals, 4 wireframe
uniform vec3 uTint;
uniform int  uAlphaTest;

out vec4 FragColor;

void main(void)
{
    // wireframe: flat unlit colour, no texture, no cutout
    if (uMode == 4) {
        FragColor = vec4(uTint, 1.0);
        return;
    }

    // The file stores a normal per vertex, packed as three unsigned bytes at
    // stride-8. Fall back to the geometric normal if it could not be decoded.
    vec3 n = vNormal;
    if (dot(n, n) < 0.001) {
        n = cross(dFdx(vWorld), dFdy(vWorld));
    }
    n = normalize(n);

    if (uMode == 3) {
        FragColor = vec4(n * 0.5 + 0.5, 1.0);
        return;
    }

    vec4 c = texture(uTex, vUV);

    if (uAlphaTest == 1 && c.a < 0.5) {
        discard;
    }

    // two sided: leaf cards and bark shells are both seen from either face
    float d = abs(dot(n, normalize(vec3(0.4, 0.8, 0.35))));
    float light = 0.35 + 0.65 * d;

    vec3 rgb;
    if (uMode == 0)      rgb = c.rgb * uTint;
    else if (uMode == 1) rgb = uTint;
    else                 rgb = vec3(fract(vUV), 0.0);

    FragColor = vec4(rgb * light, 1.0);
}
