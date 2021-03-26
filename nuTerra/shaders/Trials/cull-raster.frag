#version 450 core

#extension GL_ARB_shading_language_include : require

layout(early_fragment_tests) in;

#define USE_VISIBLES_SSBO
#include "common.h" //! #include "../common.h"

flat in int objid;

void main()
{
#ifdef DBL_SIDED
    visibles_dbl_sided[objid] = 1;
#else
    visibles[objid] = 1;
#endif
}
