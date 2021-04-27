#include <utility>
#include <cstring>
#include "nuTerraCPP.h"
#include "dxt.h"

void nuTerraCPP::Utils::CompressDXT5(array<System::Byte>^ in, array<System::Byte>^ out, System::Int32 width, System::Int32 height)
{
	pin_ptr<uint8_t> inBuf = &in[0];
	pin_ptr<uint8_t> outBuf = &out[0];

	int out_bytes = 0;
	CompressImageDXT5(inBuf, outBuf, width, height, out_bytes);
}
