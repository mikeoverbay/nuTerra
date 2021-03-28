#version 450 core

#extension GL_ARB_shading_language_include : require

layout(early_fragment_tests) in;

#define USE_VISIBLES_SSBO
#include "common.h" //! #include "../common.h"

layout (location = 0) uniform int numAfterFrustum;

flat in int objid;

void main()
{
    if (objid >= numAfterFrustum) {
        visibles_dbl_sided[objid - numAfterFrustum] = 1;
    } else {
        visibles[objid] = 1;
    }
}
