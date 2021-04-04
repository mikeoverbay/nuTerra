#version 450 core

#extension GL_ARB_shading_language_include : require

layout(early_fragment_tests) in;

#define USE_VISIBLES_SSBO
#include "common.h" //! #include "../common.h"

layout (location = 0) uniform int numAfterFrustum;

in GS_OUT {
    flat uint objid;
} fs_in;

void main()
{
    if (fs_in.objid >= numAfterFrustum) {
        visibles_dbl_sided[fs_in.objid - numAfterFrustum] = 1;
    } else {
        visibles[fs_in.objid] = 1;
    }
}
