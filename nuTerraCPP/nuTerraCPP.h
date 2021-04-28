#pragma once
#include "Quadtree.h"

using namespace System;

namespace nuTerraCPP {
	public ref class Utils
	{
	public:
		static void CompressDXT5(array<System::Byte>^ in, array<System::Byte>^ out, System::Int32 width, System::Int32 height);
	};

	public ref class QuadtreeWrap
	{
		Quadtree *quadtree_impl;

	public:
		QuadtreeWrap(System::Int32 size, System::Int32 level);
		void Add(System::UInt32 request, System::Int32 mapping);
		void Remove(System::UInt32 request);
		void Write(array<System::UInt16, 2>^ data, System::Int32 miplevel);
	};
}
