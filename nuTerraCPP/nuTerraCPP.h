#pragma once

using namespace System;

namespace nuTerraCPP {
	public ref class Utils
	{
	public:
		static void FlipDDS(array<System::Byte>^ bytes, System::Int32 format, System::Int32 width, System::Int32 height);
	};
}
