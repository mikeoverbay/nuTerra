#version 450 core

// Four power moments of this fragment's depth, for the Moment Shadow Map path.
//
// Written unconditionally. When MSM is off the bake FBO has no colour
// attachment and DrawBuffer is None, so these writes are discarded by the
// pipeline and the cost is a few ALU during a once-per-load pass.
//
// Depth arrives in 0..1 - the engine runs ClipDepthMode.ZeroToOne - and the
// moments are taken on it directly. deferred.frag biases toward the moments of
// a uniform distribution on 0..1 when it reconstructs.
//
// The depth test still decides which fragment lands here, so these are the
// moments of the nearest occluder, which is what MSM wants. Filtering across
// neighbours happens afterwards, in msm_blur and the mip chain.
layout(location = 0) out vec4 moments;

void main(void)
{
    float z  = gl_FragCoord.z;
    float z2 = z * z;
    moments = vec4(z, z2, z2 * z, z2 * z2);
}
