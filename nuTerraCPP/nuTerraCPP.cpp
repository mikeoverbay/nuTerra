#include "pch.h"
#include <utility>
#include <cstring>
#include "nuTerraCPP.h"

#define GL_COMPRESSED_RGB_S3TC_DXT1_EXT   0x83F0
#define GL_COMPRESSED_RGBA_S3TC_DXT1_EXT  0x83F1
#define GL_COMPRESSED_RGBA_S3TC_DXT3_EXT  0x83F2
#define GL_COMPRESSED_RGBA_S3TC_DXT5_EXT  0x83F3


struct DXTColBlock {
    uint16_t col0;
    uint16_t col1;

    uint8_t row[4];
};

struct DXT3AlphaBlock {
    uint16_t row[4];
};

struct DXT5AlphaBlock {
    uint8_t alpha0;
    uint8_t alpha1;

    uint8_t row[6];
};

void flip_dxt5_alpha(DXT5AlphaBlock* block) {
    uint8_t gBits[4][4];

    const uint32_t mask = 0x00000007;          // bits = 00 00 01 11
    uint32_t bits = 0;
    memcpy(&bits, &block->row[0], sizeof(uint8_t) * 3);

    gBits[0][0] = (uint8_t)(bits & mask);
    bits >>= 3;
    gBits[0][1] = (uint8_t)(bits & mask);
    bits >>= 3;
    gBits[0][2] = (uint8_t)(bits & mask);
    bits >>= 3;
    gBits[0][3] = (uint8_t)(bits & mask);
    bits >>= 3;
    gBits[1][0] = (uint8_t)(bits & mask);
    bits >>= 3;
    gBits[1][1] = (uint8_t)(bits & mask);
    bits >>= 3;
    gBits[1][2] = (uint8_t)(bits & mask);
    bits >>= 3;
    gBits[1][3] = (uint8_t)(bits & mask);

    bits = 0;
    memcpy(&bits, &block->row[3], sizeof(uint8_t) * 3);

    gBits[2][0] = (uint8_t)(bits & mask);
    bits >>= 3;
    gBits[2][1] = (uint8_t)(bits & mask);
    bits >>= 3;
    gBits[2][2] = (uint8_t)(bits & mask);
    bits >>= 3;
    gBits[2][3] = (uint8_t)(bits & mask);
    bits >>= 3;
    gBits[3][0] = (uint8_t)(bits & mask);
    bits >>= 3;
    gBits[3][1] = (uint8_t)(bits & mask);
    bits >>= 3;
    gBits[3][2] = (uint8_t)(bits & mask);
    bits >>= 3;
    gBits[3][3] = (uint8_t)(bits & mask);

    uint32_t* pBits = ((uint32_t*)&(block->row[0]));

    *pBits = *pBits | (gBits[3][0] << 0);
    *pBits = *pBits | (gBits[3][1] << 3);
    *pBits = *pBits | (gBits[3][2] << 6);
    *pBits = *pBits | (gBits[3][3] << 9);

    *pBits = *pBits | (gBits[2][0] << 12);
    *pBits = *pBits | (gBits[2][1] << 15);
    *pBits = *pBits | (gBits[2][2] << 18);
    *pBits = *pBits | (gBits[2][3] << 21);

    pBits = ((uint32_t*)&(block->row[3]));

#ifdef MACOS
    * pBits &= 0x000000ff;
#else
    * pBits &= 0xff000000;
#endif

    * pBits = *pBits | (gBits[1][0] << 0);
    *pBits = *pBits | (gBits[1][1] << 3);
    *pBits = *pBits | (gBits[1][2] << 6);
    *pBits = *pBits | (gBits[1][3] << 9);

    *pBits = *pBits | (gBits[0][0] << 12);
    *pBits = *pBits | (gBits[0][1] << 15);
    *pBits = *pBits | (gBits[0][2] << 18);
    *pBits = *pBits | (gBits[0][3] << 21);
}

void flip_blocks_dxtc1(DXTColBlock* line, unsigned int numBlocks) {
    DXTColBlock* curblock = line;

    for (unsigned int i = 0; i < numBlocks; i++) {
        std::swap(curblock->row[0], curblock->row[3]);
        std::swap(curblock->row[1], curblock->row[2]);

        curblock++;
    }
}

void flip_blocks_dxtc3(DXTColBlock* line, unsigned int numBlocks) {
    DXTColBlock* curblock = line;
    DXT3AlphaBlock* alphablock;

    for (unsigned int i = 0; i < numBlocks; i++) {
        alphablock = (DXT3AlphaBlock*)curblock;

        std::swap(alphablock->row[0], alphablock->row[3]);
        std::swap(alphablock->row[1], alphablock->row[2]);

        curblock++;

        std::swap(curblock->row[0], curblock->row[3]);
        std::swap(curblock->row[1], curblock->row[2]);

        curblock++;
    }
}

void flip_blocks_dxtc5(DXTColBlock* line, unsigned int numBlocks) {
    DXTColBlock* curblock = line;
    DXT5AlphaBlock* alphablock;

    for (unsigned int i = 0; i < numBlocks; i++) {
        alphablock = (DXT5AlphaBlock*)curblock;

        flip_dxt5_alpha(alphablock);

        curblock++;

        std::swap(curblock->row[0], curblock->row[3]);
        std::swap(curblock->row[1], curblock->row[2]);

        curblock++;
    }
}

void nuTerraCPP::Utils::FlipDDS(array<System::Byte>^ bytes, System::Int32 format, System::Int32 width, System::Int32 height)
{
    unsigned int linesize;
    void (*flipblocks)(DXTColBlock*, unsigned int);
    unsigned int xblocks = width / 4;
    unsigned int yblocks = height / 4;
    unsigned int blocksize;

    switch (format) {
    case GL_COMPRESSED_RGBA_S3TC_DXT1_EXT:
        blocksize = 8;
        flipblocks = flip_blocks_dxtc1;
        break;
    case GL_COMPRESSED_RGBA_S3TC_DXT3_EXT:
        blocksize = 16;
        flipblocks = flip_blocks_dxtc3;
        break;
    case GL_COMPRESSED_RGBA_S3TC_DXT5_EXT:
        blocksize = 16;
        flipblocks = flip_blocks_dxtc5;
        break;
    default:
        throw gcnew System::NotImplementedException();
    }

    linesize = xblocks * blocksize;
    DXTColBlock* top;
    DXTColBlock* bottom;

    uint8_t* tmp = new uint8_t[linesize];

    pin_ptr<uint8_t> a_ptr = &bytes[0];
    for (unsigned int j = 0; j < (yblocks >> 1); j++) {
        top = (DXTColBlock*)((uint8_t*)a_ptr + j * linesize);
        bottom = (DXTColBlock*)((uint8_t*)a_ptr + (((yblocks - j) - 1) * linesize));

        flipblocks(top, xblocks);
        flipblocks(bottom, xblocks);

        // swap
        memcpy(tmp, bottom, linesize);
        memcpy(bottom, top, linesize);
        memcpy(top, tmp, linesize);
    }

    delete[] tmp;
}
