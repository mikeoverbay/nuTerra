#version 450 core

layout (binding = 0) uniform sampler2D colorMap;
layout (location = 2) uniform vec2 viewportSize;

layout (location=0) in VS_OUT {
    vec2 TexCoords;
} fs_in;

layout (location=0) out vec4 fragColor;


void main(void)
{
    float alpha = texture(colorMap, fs_in.TexCoords.xy).a;
    vec2 texCoordOffset = vec2(1.0, 1.0)/viewportSize.xy;

    float R_fxaaSpanMax = 6.0;
    float R_fxaaReduceMul = 1.0/8.0;
    float R_fxaaReduceMin = 1.0/128.0;

    vec3 luma = vec3(0.299, 0.587, 0.114);  
    float lumaTL = dot(luma, texture(colorMap, fs_in.TexCoords.xy + (vec2(-1.0, -1.0) * texCoordOffset)).xyz);
    float lumaTR = dot(luma, texture(colorMap, fs_in.TexCoords.xy + (vec2(1.0, -1.0) * texCoordOffset)).xyz);
    float lumaBL = dot(luma, texture(colorMap, fs_in.TexCoords.xy + (vec2(-1.0, 1.0) * texCoordOffset)).xyz);
    float lumaBR = dot(luma, texture(colorMap, fs_in.TexCoords.xy + (vec2(1.0, 1.0) * texCoordOffset)).xyz);
    float lumaM  = dot(luma, texture(colorMap, fs_in.TexCoords.xy).xyz);

    vec2 dir;
    dir.x = -((lumaTL + lumaTR) - (lumaBL + lumaBR));
    dir.y = ((lumaTL + lumaBL) - (lumaTR + lumaBR));

    float dirReduce = max((lumaTL + lumaTR + lumaBL + lumaBR) * (R_fxaaReduceMul * 0.25), R_fxaaReduceMin);
    float inverseDirAdjustment = 1.0/(min(abs(dir.x), abs(dir.y)) + dirReduce);

    dir = min(vec2(R_fxaaSpanMax, R_fxaaSpanMax), 
        max(vec2(-R_fxaaSpanMax, -R_fxaaSpanMax), dir * inverseDirAdjustment)) * texCoordOffset;

    vec3 result1 = (1.0/2.0) * (
        texture(colorMap, fs_in.TexCoords.xy + (dir * vec2(1.0/3.0 - 0.5))).xyz +
        texture(colorMap, fs_in.TexCoords.xy + (dir * vec2(2.0/3.0 - 0.5))).xyz);

    vec3 result2 = result1 * (1.0/2.0) + (1.0/4.0) * (
        texture(colorMap, fs_in.TexCoords.xy + (dir * vec2(0.0/3.0 - 0.5))).xyz +
        texture(colorMap, fs_in.TexCoords.xy + (dir * vec2(3.0/3.0 - 0.5))).xyz);

    float lumaMin = min(lumaM, min(min(lumaTL, lumaTR), min(lumaBL, lumaBR)));
    float lumaMax = max(lumaM, max(max(lumaTL, lumaTR), max(lumaBL, lumaBR)));
    float lumaResult2 = dot(luma, result2);

    if (lumaResult2 < lumaMin || lumaResult2 > lumaMax) {
        fragColor = vec4(result1, texture(colorMap, fs_in.TexCoords.xy).a);
    } else {
        fragColor = vec4(result2, texture(colorMap, fs_in.TexCoords.xy).a);
    }
    fragColor.a = alpha;
}
