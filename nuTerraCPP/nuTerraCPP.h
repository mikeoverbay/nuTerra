#pragma once

using namespace System;

namespace nuTerraCPP {
	public ref class Utils
	{
	public:
		static void CompressDXT5(array<System::Byte>^ in, array<System::Byte>^ out, System::Int32 width, System::Int32 height);
	};
}
