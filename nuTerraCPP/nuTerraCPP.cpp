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

nuTerraCPP::QuadtreeWrap::QuadtreeWrap(System::Int32 size, System::Int32 level)
{
	Rect r{};
	r.m_x = 0;
	r.m_y = 0;
	r.m_width = size;
	r.m_height = size;
	this->quadtree_impl = new Quadtree(r, level);
}

nuTerraCPP::QuadtreeWrap::~QuadtreeWrap()
{
	delete this->quadtree_impl;
}

void nuTerraCPP::QuadtreeWrap::Add(System::UInt32 request, System::Int32 mapping)
{
	this->quadtree_impl->add(request, mapping);
}

void nuTerraCPP::QuadtreeWrap::Remove(System::UInt32 request)
{
	this->quadtree_impl->remove(request);
}

void nuTerraCPP::QuadtreeWrap::Write(array<System::UInt16, 2>^ data, System::Int32 miplevel)
{
	pin_ptr<uint16_t> buf = &data[0, 0];
	this->quadtree_impl->write(buf, data->GetLength(0), miplevel);
}
