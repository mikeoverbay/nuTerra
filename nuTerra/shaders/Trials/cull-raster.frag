#version 450 core

#extension GL_ARB_shading_language_include : require

layout(early_fragment_tests) in;

#define USE_VISIBLES_SSBO
#include "common.h" //! #include "../common.h"

flat in int objid;

void main()
{
    visibles[objid] = 1;
}
