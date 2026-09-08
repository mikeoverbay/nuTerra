"""Build the lamp-glass patch nuTerra applies to the game's own normal map.

    python tools/make_lamp_patch.py

WHY A PATCH AND NOT A TEXTURE. The cut lives in the RED channel of
env_19_08_StreetLamp_01-02_ANM.dds, which is Wargaming's art. Shipping a copy
of it - however wrapped - is shipping their texture, and the repo's own
.gitignore says not to. So nuTerra reads the pristine file out of the player's
installed packages and applies OUR change to it at load.

WHAT IS IN THE FILE. Only the DXT5 colour halves of the blocks the cut touches
- a few hundred of them, 8 bytes each. On its own it is a few kilobytes of
endpoint pairs with no dimensions, no header and no picture. It is useless
without the game file it patches, which is the point: our work ships, theirs
does not.

WHY BLOCKS AND NOT A MASK. A mask would need the encoder re-implemented in VB
and run at every load. The blocks are the encoder's OUTPUT: nuTerra copies
bytes into place and is done, and the result is bit-identical to what was
verified here.

The source file is fingerprinted. If Wargaming re-exports the texture the
offsets would land on different pixels, so a patch whose fingerprint does not
match is refused rather than applied to something it was not built for.
"""
import hashlib
import io
import os
import struct
import sys
import zipfile

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

P = "C:/Games/World_of_Tanks_NA/res/packages/"
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                   "..", "nuTerra", "assets", "lamp_glass.ntpx")

TARGETS = {
    "hd": (P + "shared_content_sandbox_hd-part1.pkg",
           "content/environment/env_19_08_streetlamp_01-02/env_19_08_streetlamp_01-02_anm_hd.dds"),
    "sd": (P + "shared_content_sandbox-part1.pkg",
           "content/environment/env_19_08_streetlamp_01-02/env_19_08_streetlamp_01-02_anm.dds"),
}
AM = (P + "shared_content_sandbox_hd-part1.pkg",
      "content/Environment/env_19_08_StreetLamp_01-02/env_19_08_StreetLamp_01-02_AM_hd.dds")
GMM = (P + "shared_content_sandbox_hd-part2.pkg",
       "content/Environment/env_19_08_StreetLamp_01-02/env_19_08_StreetLamp_01-02_GMM_hd.dds")
LAMPS = [
    ("01", P + "19_monastery.pkg",
     "content/Environment/env_19_08_StreetLamp_01-02/normal/lod0/env_19_08_StreetLamp01.primitives_processed", 0.35),
    ("02", P + "shared_content_sandbox-part1.pkg",
     "content/Environment/env_19_08_StreetLamp_01-02/normal/lod0/env_19_08_StreetLamp02.primitives_processed", 0.50),
]
CAMPATH = "C:/nuTerra/nuTerra/cam_paths/19_monastery.campath"

MAGIC = b"NTPX"
VERSION = 1
KEY = b"nuTerra lamp glass v1"      # obfuscation, NOT encryption - see the doc

AM_LUM, GMM_GLOSS, FEATHER, SS = 140.0, 190.0, 1.1, 4
W_ERR = np.array([1.0, 4.0, 0.25])


def read(pkg, name):
    with zipfile.ZipFile(pkg) as z:
        try:
            return z.read(name)
        except KeyError:
            for n in z.namelist():
                if n.lower() == name.lower():
                    return z.read(n)
            raise


def img(pkg, name):
    return Image.open(io.BytesIO(read(pkg, name))).convert("RGBA")


# ------------------------------------------------------------------ the mask
am_img = img(*AM)
W, H = am_img.size
am = np.array(am_img)[:, :, :3].astype(np.float64)
gmm = np.array(img(*GMM).resize((W, H), Image.BILINEAR))[:, :, 0].astype(np.float64)
cam = open(CAMPATH, "rb").read()

lant = np.zeros((H, W), np.float64)
for tag, mpkg, mname, radius in LAMPS:
    raw = read(mpkg, mname)
    ts = struct.unpack_from("<I", raw, len(raw) - 4)[0]
    p = len(raw) - 4 - ts
    sec, off = {}, 4
    while p < len(raw) - 4:
        size = struct.unpack_from("<I", raw, p)[0]
        p += 20
        nl = struct.unpack_from("<I", raw, p)[0]
        p += 4
        nm = raw[p:p + nl].decode()
        p += nl + ((4 - nl % 4) if nl % 4 else 0)
        sec[nm] = (off, size)
        off += size + ((4 - size % 4) if size % 4 else 0)
    voff, _ = sec["vertices"]
    nv = struct.unpack_from("<I", raw, voff + 132)[0]
    body = voff + 136
    verts = np.empty((nv, 3)); uvs = np.empty((nv, 2))
    for i in range(nv):
        x, y, z = struct.unpack_from("<3f", raw, body + i * 32)
        verts[i] = (-x, y, z)
        uvs[i] = struct.unpack_from("<2f", raw, body + i * 32 + 16)
    ioff, _ = sec["indices"]
    nidx = struct.unpack_from("<I", raw, ioff + 64)[0]
    idx = np.array(struct.unpack_from("<%dH" % nidx, raw, ioff + 72), np.int64)
    tris = idx.reshape(-1, 3)[:, [1, 0, 2]]

    key = ("env_19_08_StreetLamp%s.primitives" % tag).encode()
    at = cam.find(key)
    st = cam.rfind(b"content/", 0, at)
    bulb = np.array(struct.unpack_from("<3f", cam, st + 164))
    near = np.linalg.norm(verts - bulb, axis=1)[tris].min(1) < radius

    lay = Image.new("L", (W * SS, H * SS), 0)
    dl = ImageDraw.Draw(lay)
    for t in np.nonzero(near)[0]:
        dl.polygon([(float(uvs[i][0] % 1.0) * W * SS, float(uvs[i][1] % 1.0) * H * SS) for i in tris[t]], fill=255)
    lant = np.maximum(lant, np.array(lay.resize((W, H), Image.BOX)).astype(np.float64) / 255.0)
    print("lamp %s: %d/%d triangles inside %.2f m" % (tag, near.sum(), len(tris), radius))

hard = (lant > 0.5) & (am.mean(2) > AM_LUM) & (gmm > GMM_GLOSS)
m = Image.fromarray((hard * 255).astype(np.uint8))
m = m.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.MaxFilter(3))
cleaned = np.array(m) > 127
cov = np.array(Image.fromarray((cleaned * 255).astype(np.uint8))
               .filter(ImageFilter.GaussianBlur(FEATHER))).astype(np.float64) / 255.0
cov *= np.clip(lant * 1.5, 0.0, 1.0)
target_red = np.clip((1.0 - cov) * 255.0, 0, 255)
print("glass mask: %d texels (%d after speckle removal)" % (hard.sum(), cleaned.sum()))


# --------------------------------------------------------------- the encoder
def rgb565(r, g, b):
    return ((int(r) >> 3) << 11) | ((int(g) >> 2) << 5) | (int(b) >> 3)


def unrgb565(c):
    return np.array([((c >> 11) & 31) * 255.0 / 31.0,
                     ((c >> 5) & 63) * 255.0 / 63.0,
                     (c & 31) * 255.0 / 31.0])


def encode_block(target):
    pts = target * W_ERR
    mean = pts.mean(0)
    d = pts - mean
    _, _, vt = np.linalg.svd(d, full_matrices=False)
    axis = vt[0]
    t = d @ axis
    lo, hi = t.min(), t.max()
    if hi - lo < 1e-6:
        c = rgb565(*target[0])
        return c, c, 0
    best = None
    for _ in range(3):
        c0 = rgb565(*np.clip((mean + axis * hi) / W_ERR, 0, 255))
        c1 = rgb565(*np.clip((mean + axis * lo) / W_ERR, 0, 255))
        p0, p1 = unrgb565(c0), unrgb565(c1)
        pal = np.stack([p0, p1, (2 * p0 + p1) / 3.0, (p0 + 2 * p1) / 3.0])
        err = (((target[:, None, :] - pal[None, :, :]) * W_ERR) ** 2).sum(2)
        ids = err.argmin(1)
        tot = err[np.arange(16), ids].sum()
        if best is None or tot < best[0]:
            best = (tot, c0, c1, ids)
        w = np.array([0.0, 1.0, 1.0 / 3.0, 2.0 / 3.0])[ids]
        A = np.stack([1.0 - w, w], 1)
        try:
            sol, *_ = np.linalg.lstsq(A * W_ERR.mean(), target, rcond=None)
        except np.linalg.LinAlgError:
            break
        mean = ((sol[0] + sol[1]) / 2.0) * W_ERR
        av = (sol[0] - sol[1]) * W_ERR
        n = np.linalg.norm(av)
        if n < 1e-6:
            break
        axis = av / n
        hi, lo = n / 2.0, -n / 2.0
    _, c0, c1, ids = best
    packed = 0
    for i in range(16):
        packed |= int(ids[i]) << (2 * i)
    return c0, c1, packed


def build(dds):
    h, w = struct.unpack_from("<2I", dds, 12)
    mips = max(1, struct.unpack_from("<I", dds, 28)[0])
    assert dds[84:88] == b"DXT5"
    decoded = np.array(Image.open(io.BytesIO(dds)).convert("RGBA"))
    patches = []
    pos = 128
    for lvl in range(mips):
        lw, lh = max(1, w >> lvl), max(1, h >> lvl)
        bx, by = max(1, (lw + 3) // 4), max(1, (lh + 3) // 4)
        tred = np.array(Image.fromarray(target_red.astype(np.uint8))
                        .resize((lw, lh), Image.BILINEAR)).astype(np.float64)
        src_rgb = np.array(Image.fromarray(decoded[:, :, :3]).resize((lw, lh), Image.BILINEAR)).astype(np.float64) \
            if (lw, lh) != (w, h) else decoded[:, :, :3].astype(np.float64)
        for byi in range(by):
            for bxi in range(bx):
                o = pos + (byi * bx + bxi) * 16
                blk = tred[byi * 4:byi * 4 + 4, bxi * 4:bxi * 4 + 4]
                if blk.size == 0 or (blk > 250).all():
                    continue
                if (blk < 5).all():
                    c0 = struct.unpack_from("<H", dds, o + 8)[0] & 0x07FF
                    c1 = struct.unpack_from("<H", dds, o + 10)[0] & 0x07FF
                    packed = struct.unpack_from("<I", dds, o + 12)[0]
                else:
                    src = src_rgb[byi * 4:byi * 4 + 4, bxi * 4:bxi * 4 + 4]
                    tgt = np.zeros((16, 3))
                    for k in range(16):
                        ry, rx = divmod(k, 4)
                        tgt[k] = ([blk[ry, rx], src[ry, rx, 1], src[ry, rx, 2]]
                                  if ry < src.shape[0] and rx < src.shape[1] else [255.0, 0.0, 0.0])
                    c0, c1, packed = encode_block(tgt)
                patches.append((o + 8, struct.pack("<HHI", c0, c1, packed)))
        pos += bx * by * 16
    assert pos == len(dds)
    return patches


def scramble(buf, salt):
    """Obfuscation, not encryption. Keeps the file from being a DDS anyone can
    open; it does not and cannot keep a determined reader out, because the
    unwrap is in this repo."""
    k = hashlib.sha256(KEY + salt).digest()
    out = bytearray(buf)
    for i in range(len(out)):
        out[i] ^= k[i % len(k)]
    return bytes(out)


body = bytearray()
n_entries = 0
for tag, (pkg, name) in TARGETS.items():
    src = read(pkg, name)
    patches = build(src)
    nm = name.encode("utf-8")
    body += struct.pack("<H", len(nm)) + nm
    body += struct.pack("<I", len(src)) + hashlib.md5(src).digest()
    body += struct.pack("<I", len(patches))
    for off, payload in patches:
        body += struct.pack("<I", off) + payload
    n_entries += 1
    print("%s: %s  %d blocks patched (%d bytes)" % (tag, os.path.basename(name), len(patches), len(patches) * 12))

head = MAGIC + struct.pack("<II", VERSION, n_entries)
salt = hashlib.md5(head).digest()[:8]
out = head + salt + scramble(bytes(body), salt)

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "wb") as f:
    f.write(out)
print("\nwrote %s  (%d bytes)" % (os.path.normpath(OUT), len(out)))
