#!python

import sys
from pathlib import Path
from struct import unpack, calcsize
from ctypes import Structure, c_uint32
from zipfile import ZipFile


class DDS_HEADER(Structure):
	_fields_ = [
		('dwSize',              c_uint32      ),
		('dwFlags',             c_uint32      ),
		('dwHeight',            c_uint32      ),
		('dwWidth',             c_uint32      ),
		('dwPitchOrLinearSize', c_uint32      ),
		('dwDepth',             c_uint32      ),
		('dwMipMapCount',       c_uint32      ),
		('dwReserved1',         c_uint32 * 11 ),
		('pf_Size',             c_uint32      ),
		('pf_Flags',            c_uint32      ),
		('pf_FourCC',           c_uint32      ),
		('pf_RGBBitCount',      c_uint32      ),
		('pf_RBitMask',         c_uint32      ),
		('pf_GBitMask',         c_uint32      ),
		('pf_BBitMask',         c_uint32      ),
		('pf_ABitMask',         c_uint32      ),
		('dwCaps',              c_uint32      ),
		('dwCaps2',             c_uint32      ),
		('dwCaps3',             c_uint32      ),
		('dwCaps4',             c_uint32      ),
		('dwReserved2',         c_uint32      ),
		]


def unpack_blend_textures(fr):
    print('=== unpack blend textures ===')

    header = fr.read(4)
    assert header == b'bwb\x00', header
    print('header:', header)

    section_count = unpack('<I', fr.read(4))[0]
    section_sizes = unpack(f'<4I', fr.read(4*4))
    print('section_sizes:', section_sizes)

    for i in range(section_count):
        header = fr.read(4)
        assert header == b'bwt\x00', header
        print('header:', header)

        version, xsize, ysize, always19, tex_cnt, padding = unpack('<IHHHHQ', fr.read(calcsize('<IHHHHQ')))
        print('version:', version)
        print('xsize:', xsize)
        print('ysize:', ysize)
        print('tex_cnt:', tex_cnt)

        assert version == 2
        assert always19 == 19
        assert padding == 0

        for j in range(tex_cnt):
            name_size = unpack('<I', fr.read(4))[0]
            name = fr.read(name_size)
            print('name:', name)

        new_header = DDS_HEADER()
        new_header.dwSize = 124
        new_header.dwHeight = ysize
        new_header.dwWidth = xsize
        new_header.dwMipMapCount = 0
        new_header.pf_Flags = 4 # FourCCFlag
        new_header.pf_FourCC = int.from_bytes(b'DXT5', 'little')

        with Path(f'blend_texture {i}.dds').open('wb') as fw:
            fw.write(b'DDS ')
            fw.write(bytes(new_header))
            fw.write(fr.read(xsize * ysize))


def unpack_layers(fr):
    print('=== unpack layers ===')

    header = fr.read(4)
    assert header == b'blb\x00', header
    print('header:', header)

    map_count = unpack('<I', fr.read(4))[0]
    section_sizes = unpack('<8I', fr.read(8 * 4))
    print('map_count:', map_count)
    print('section_sizes:', section_sizes)

    for i in range(map_count):
        header = fr.read(4)
        assert header == b'bld\x00', header
        print('header:', header)

        width, height, count = unpack('<3I', fr.read(3 * 4))
        print('width:', width)
        print('height:', height)
        print('count:', count) # bpp?

        uProjection = unpack('<4f', fr.read(4 * 4))
        print('uProjection:', uProjection)

        vProjection = unpack('<4f', fr.read(4 * 4))
        print('vProjection:', vProjection)

        flags = unpack('<I', fr.read(4))[0]
        assert flags == 59

        padding = unpack('<3I', fr.read(3 * 4))
        assert padding == (0, 0, 0), padding

        # Displacement
        row0 = unpack('<4f', fr.read(4 * 4))
        print('row0:', row0)

        row1 = unpack('<4f', fr.read(4 * 4))
        print('row1:', row1)

        row2 = unpack('<4f', fr.read(4 * 4))
        print('row2:', row2)

        sz = unpack('<I', fr.read(4))[0]
        print(fr.read(sz + 1))


def unpack_heights(fr):
    print('=== unpack heights ===')

    header = fr.read(4)
    assert header == b'hmp\x00', header
    print('header:', header)

    width, height, comp, version, min, max, crap = unpack('<4I2fI', fr.read(7 * 4))
    print('width:', width)
    print('height:', height)
    print('comp:', comp)
    print('version:', version)
    print('min:', min)
    print('max:', max)
    print('crap:', crap)

    fr.seek(36)
    with Path('heights.png').open('wb') as fw:
        fw.write(fr.read())


def main(path):
    with ZipFile(sys.argv[1]) as cdata:
        with cdata.open('terrain2/blend_textures') as f:
            unpack_blend_textures(f)
        with cdata.open('terrain2/layers') as f:
            unpack_layers(f)
        with cdata.open('terrain2/heights') as f:
            unpack_heights(f)


if __name__ == '__main__':
    main(sys.argv[1])
