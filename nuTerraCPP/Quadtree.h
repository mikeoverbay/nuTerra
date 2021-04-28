#pragma once

// Rect
struct Rect
{
	int minX() const
	{
		return m_x;
	}

	int minY() const
	{
		return m_y;
	}

	int maxX() const
	{
		return m_x + m_width;
	}

	int maxY() const
	{
		return m_y + m_height;
	}

	bool contains(const int x, const int y) const
	{
		return x >= minX() && y >= minY() && x < maxX() && y < maxY();
	}

	int m_x, m_y, m_width, m_height;
};

// Quadtree
struct Quadtree
{
	Quadtree(Rect _rect, int _level);
	~Quadtree();

	void             add(uint32_t request, int mapping);
	void             remove(uint32_t request);
	void             write(uint16_t* image, int size, int miplevel);
	Rect			 getRectangle(int index);

	void		     write(Quadtree* node, uint16_t* image, const int size, const int miplevel);
	static Quadtree* findPage(Quadtree* node, uint32_t request, int* index);

	Rect      m_rectangle;
	int		  m_level;
	int       m_mapping;
	Quadtree* m_children[4];
};
