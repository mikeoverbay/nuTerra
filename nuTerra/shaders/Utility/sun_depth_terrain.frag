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

// ---- THE FLIGHT BAKE'S KEY CHANNEL -----------------------------------------
//
// Terrain is kind 0, and it has to SAY SO rather than leave the channel alone.
//
// The bake clears the key attachment to zero, which is terrain's own key, so
// writing nothing here looks like it should be free. It is not: a fragment
// shader that does not write an output for an ACTIVE draw buffer leaves that
// value UNDEFINED, not unchanged. On this driver the undefined value came back
// as the depth the shader had just computed, so three quarters of the map -
// every texel where bare ground was the topmost thing - carried a height in
// the key channel instead of a zero, correlated with terrain height at -0.996.
//
// The models and trees never showed it because they do write theirs.
layout(location = 1) out vec4 bake_key;

// ---- THE FLIGHT BAKE'S ID CHANNEL ------------------------------------------
//
// Location 2, the third attachment: WHICH object is on top, where the key
// channel says only what KIND it is. The owner's ask - "render ids so we know
// what is what on the map, colors is not enough" - and the reason Path Studio's
// route signature has to split a kind mask into components today: two adjacent
// buildings are one blob of key 1 and it cannot tell them apart.
//
// Zero means nothing was drawn here, so every id is BIASED BY ONE. The same
// bias ModelPicker already uses on its own pick buffer, for the same reason.
//
// Terrain writes zero for the same reason it writes a zero key: an output a
// fragment shader leaves alone on an ACTIVE draw buffer is UNDEFINED, not
// unchanged, and this driver has already been caught handing back the depth it
// had just computed. Ground is not an object and has no id.
layout(location = 2) out uvec4 bake_id;

void main(void)
{
    float z  = gl_FragCoord.z;
    float z2 = z * z;
    moments = vec4(z, z2, z2 * z, z2 * z2);
    bake_key = vec4(0.0, 0.0, 0.0, 1.0);
    bake_id = uvec4(0u);
}
