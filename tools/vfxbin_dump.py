import zipfile, struct
MARK = 0x50505050
def u32(d,o): return struct.unpack_from("<I",d,o)[0]
def f32(d,o): return struct.unpack_from("<f",d,o)[0]

def tracks(d, start, end):
    """[u32 count]['PPPP'][count times][count*stride values].
    stride is 1 for scalar tracks and 4 for colour (RGBA); it is resolved by
    checking that the assumed end lands on the next count+marker pair, or on
    the end of the block."""
    out=[]; o=start
    # find the first count+marker pair
    while o+8<=end and not (u32(d,o+4)==MARK and u32(d,o)<=64): o+=4
    while o+8<=end and u32(d,o+4)==MARK:
        n=u32(d,o); base=o+8
        chosen=None
        for stride in (1,4):
            nxt = base + 4*n + 4*n*stride
            if nxt==end: chosen=stride; break
            if nxt+8<=end and u32(d,nxt+4)==MARK and u32(d,nxt)<=64:
                chosen=stride; break
        if chosen is None: break
        t=[f32(d,base+4*i) for i in range(n)]
        v=[[f32(d,base+4*n+4*(i*chosen+k)) for k in range(chosen)] for i in range(n)]
        out.append((n,chosen,t,v))
        o = base + 4*n + 4*n*chosen
    return out

def emitters(d):
    out=[]; o=0
    while o+8<=len(d):
        if u32(d,o)==1001:
            s=u32(d,o+4)
            if 16<=s<=len(d)-o:
                raw=d[o+28:o+92]; z=raw.find(b'\0')
                out.append((raw[:z].decode('ascii','replace'), o+28+64, o+s)); o+=s; continue
        o+=4
    return out

def block999(d,s,e):
    o=s
    while o+8<=e:
        if u32(d,o)==999 and u32(d,o+4)==2: return o
        o+=4
    return None

if __name__=="__main__":
    z=zipfile.ZipFile(r"C:\Games\World_of_Tanks_NA\res\packages\particles.pkg")
    for fn in ("Big",):
        d=z.read('particles/content_deferred/PFX/Environment/Buildings/Bld_19_01_Vhouse_05_Smoke_%s.vfxbin'%fn)
        for nm,s,e in emitters(d):
            p=block999(d,s,e)
            if p is None: continue
            print("=== %s / %s ===" % (fn, nm))
            for i,(n,stride,t,v) in enumerate(tracks(d,p,e)):
                kind = "rgba " if stride==4 else "float"
                vs = ", ".join("(%s)"%" ".join("%.3f"%x for x in k) if stride==4 else "%.4g"%k[0] for k in v)
                print("  track %d  n=%-2d %s  t=[%s]" % (i,n,kind,", ".join("%.3g"%x for x in t)))
                print("            %sv=[%s]" % (" "*10, vs))
            print()
