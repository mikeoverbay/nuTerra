#version 450 core
#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_MATERIALS_SSBO
#include "common.h" //! #include "../common.h"

out vec4 co;
in VS_OUT
{
flat in uint model_id;
in vec2 uv;
}fs_in;

void main(void){

	MaterialProperties thisMaterial = material[fs_in.model_id];

	if (thisMaterial.alphaTestEnable){
		float alpha = texture(thisMaterial.maps[1],fs_in.uv).r;
		if (alpha < thisMaterial.alphaReference) { discard; }
	}
	co = vec4(0.0);

}