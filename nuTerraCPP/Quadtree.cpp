#include <algorithm>
#include "Quadtree.h"

Quadtree::Quadtree(Rect _rect, int _level)
	: m_rectangle(_rect)
	, m_level(_level)
{
	for (int i = 0; i < 4; ++i)
	{
		m_children[i] = nullptr;
	}
}

Quadtree::~Quadtree()
{
	for (int i = 0; i < 4; ++i)
	{
		if (m_children[i] != nullptr)
		{
			delete m_children[i];
		}
	}
}

void Quadtree::add(uint32_t request, int mapping)
{
	const int reqMip = request & 0xFF;
	const int reqY = (request >> 8) & 0xFFF;
	const int reqX = (request >> 20) & 0xFFF;

	int scale = 1 << reqMip; // Same as pow( 2, mip )
	int x = reqX * scale;
	int y = reqY * scale;

	Quadtree* node = this;

	while (reqMip < node->m_level)
	{
		for (int i = 0; i < 4; ++i)
		{
			auto rect = node->getRectangle(i);
			if (rect.contains(x, y))
			{
				// Create a new one if needed
				if (node->m_children[i] == nullptr)
				{
					node->m_children[i] = new Quadtree(rect, node->m_level - 1);
					node = node->m_children[i];
					break;
				}
				// Otherwise traverse the tree
				else
				{
					node = node->m_children[i];
					break;
				}
			}
		}
	}

	// We have created the correct node, now set the mapping
	node->m_mapping = mapping;
}

void Quadtree::remove(uint32_t request)
{
	int  index;
	auto node = findPage(this, request, &index);

	if (node != nullptr)
	{
		delete node->m_children[index];
		node->m_children[index] = nullptr;
	}
}

void Quadtree::write(uint16_t* data, int size, int miplevel)
{
	write(this, data, size, miplevel);
}

Rect Quadtree::getRectangle(int index)
{
	int x = m_rectangle.m_x;
	int y = m_rectangle.m_y;
	int w = m_rectangle.m_width / 2;
	int h = m_rectangle.m_width / 2;

	switch (index)
	{
	case 0: return { x    , y    , w, h };
	case 1: return { x + w, y    , w, h };
	case 2: return { x + w, y + h, w, h };
	case 3: return { x    , y + h, w, h };
	default: break;
	}

	return { 0, 0, 0, 0 };
}

void Quadtree::write(Quadtree* node, uint16_t* data, const int size, const int miplevel)
{
	if (node->m_level >= miplevel)
	{
		const int rx = node->m_rectangle.m_x >> miplevel;
		const int ry = node->m_rectangle.m_y >> miplevel;
		const int rw = (node->m_rectangle.m_width >> miplevel);
		const int rh = ry + (node->m_rectangle.m_width >> miplevel);

		const uint16_t value = (node->m_mapping << 5) | node->m_level;
		for (int i = ry; i < rh; ++i)
		{
			uint16_t* ptr = data + i * size + rx;
			const uint16_t* end = ptr + rw;
			for (; ptr < end; ++ptr)
			{
				*ptr = value;
			}
		}

		for (int i = 0; i < 4; ++i)
		{
			auto child = node->m_children[i];
			if (child != nullptr)
			{
				Quadtree::write(child, data, size, miplevel);
			}
		}
	}
}

Quadtree* Quadtree::findPage(Quadtree* node, uint32_t request, int* index)
{
	const int reqMip = request & 0xFF;
	const int reqY = (request >> 8) & 0xFFF;
	const int reqX = (request >> 20) & 0xFFF;

	int scale = 1 << reqMip; // Same as pow( 2, mip )
	int x = reqX * scale;
	int y = reqY * scale;

	// Find the parent of the child we want to remove
	bool exitloop = false;
	while (!exitloop)
	{
		exitloop = true;
		for (int i = 0; i < 4; ++i)
		{
			if (node->m_children[i] != nullptr && node->m_children[i]->m_rectangle.contains(x, y))
			{
				// We found it
				if (reqMip == node->m_level - 1)
				{
					*index = i;
					return node;
				}
				// Check the children
				else
				{
					node = node->m_children[i];
					exitloop = false;
				}
			}
		}
	}

	// We couldn't find it so it must not exist anymore
	*index = -1;
	return nullptr;
}
