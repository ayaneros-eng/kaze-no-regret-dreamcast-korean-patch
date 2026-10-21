/* Dreamcast Real Sound ~Kaze no Regret~ Korean subtitle module.
 *
 * Lives at the top of main RAM (copied there by the patched crt section table) and is
 * reached through patched literal-pool slots in 1ST_READ.BIN:
 *   frame completion                 -> kr_frame()      CPU framebuffer / GPU subtitle output
 *   ADXT_StartAfs callers              -> kr_start_afs()  remembers which AFS file a handle plays
 *   ADXT_StartMemIdx caller            -> kr_start_mem()  same for the in-memory ONMEM.ACX menu voices
 *   ADXF_OpenAfs / ADXF_ReadNw callers -> kr_open_afs() / kr_read_nw()  remember which AFS file each
 *                                          preload buffer holds (choice voices, disc prompts)
 *   ADXT_StartMem callers              -> kr_start_buf()  plays such a buffer: file looked up by address
 * Each frame the newest playing handle's ADXT_GetTime picks the cue; a cue change re-renders the
 * 1024x64 ARGB4444 texture straight into VRAM, and the texture is drawn with njDrawTexture.
 * Library addresses come from the linker (--defsym per disc), see dc/tools/build_dc.py.
 */
typedef unsigned int u32;
typedef unsigned short u16;
typedef unsigned char u8;
typedef int s32;
/* game / library entry points (per-disc --defsym) */
extern void ADXT_StartAfs(void *adxt, s32 patid, s32 fid);
extern void ADXT_StartMemIdx(void *adxt, void *acx, s32 no);
extern void ADXT_StartMem(void *adxt, void *adx);
extern void *ADXF_OpenAfs(s32 patid, s32 fid);
extern s32 ADXF_ReadNw(void *adxf, s32 nsct, void *buf);
extern void ADXT_GetTime(void *adxt, s32 *ncount, s32 *tscale);
extern void njDrawTexture(void *vtx, s32 count, s32 tex, s32 trans);
extern s32 njLoadTexture(void *texlist);
extern u8 *TEXMEM_LIST;        /* NJS_TEXMEMLIST array pointer variable */
extern u32 TEXMEM_COUNT;       /* its entry count variable */
extern void GAME_FRAME_BEGIN(void);
extern void GAME_WAIT_VSYNC(void);
extern void *ONMEM_ACX;        /* game's pointer to the loaded ONMEM.ACX */
extern const u8 kr_data[];     /* built by build_dc.py */
#define TEXW 1024
#define TEXH 64
#define GBIX 0x004B5253        /* 'SRK' -- far above the game's own indices */
#define SCRW 640
#define SUBZ 20.0f              /* in front of everything: sprites use <=0.7, the black-screen-mode cover 5.0 */
#define NSLOT 8
struct hdr {
    u32 nglyph, cellw, cellh, lineh;
    u32 off_adv, off_glyph;
    u32 nfile, off_files;          /* per file id: first cue, count */
    u32 off_cues, off_text;
    u32 onmem_base;                /* pseudo file id of ONMEM.ACX entry 0 */
    u32 ypos;                      /* screen y of the texture top */
    u32 ox;                        /* glyph cell x of the pen position */
    u16 pal[16];
};
struct cue { u32 s, e, text; };
/* NJS_TEXINFO for njLoadTexture with NJD_TEXATTR_TYPE_MEMORY (bit 30): raw pixels + surface description
 * (layout from njSetTextureInfo: texaddr, Type<<16, BitDepth, PixelFormat, nWidth, nHeight, ...) */
static u16 tex_px[TEXW * TEXH] __attribute__((aligned(32)));
static struct { void *addr; u32 type, depth, pixfmt, w, h, size, flags, surf, virt, phys; } texinfo;
static struct { void *name; u32 attr; u32 texaddr; } texname;
static struct { void *textures; u32 n; } texlist;
static struct { void *adxt; s32 file; u32 seq; } slot[NSLOT];
#define NMAP 8
static struct { void *key; s32 file; } fmap[NMAP], bmap[NMAP];   /* ADXF handle -> file, buffer -> file */
static u32 fnext, bnext;
static u32 seq, inited;
static s32 shown = -2;         /* cue on the texture; -1 = blank */
static volatile u16 *vram;     /* texture pixels as seen from the CPU */
static float vtx[4][6];
/* Compose in cached main RAM, then commit changed aligned 32-bit words.
 * Byte stores must never target VRAM. Preserve all surrounding photo pixels.
 * Restore before the game advances so subtitles cannot accumulate. */

static u8 photo_backup[TEXH][SCRW * 3] __attribute__((aligned(32)));
static u8 photo_composed[TEXH][SCRW * 3] __attribute__((aligned(32)));
extern void GAME_SQ_COPY(void *dst, const void *src, u32 blocks);
static volatile u8 *photo_target;
static volatile u8 *photo_next;
static u32 photo_saved;
static volatile u32 photo_probe[10] __attribute__((used));
static void restore_photo(void)
{
    if (!photo_saved) return;
    u32 i;
    for (i = 0; i < TEXH * SCRW * 3 / 4; i++)
        if (((u32 *)photo_composed)[i] != ((u32 *)photo_backup)[i])
            ((volatile u32 *)photo_target)[i] = ((u32 *)photo_backup)[i];
    photo_saved = 0;
}
void kr_begin_frame(void)
{
    restore_photo();
    photo_next = 0;
    GAME_FRAME_BEGIN();
}
void kr_photo_copy(void *dst, const void *src, u32 blocks)
{
    GAME_SQ_COPY(dst, src, blocks);
    photo_next = (volatile u8 *)dst;
}

static const struct hdr *H(void) { return (const struct hdr *)kr_data; }
static void init(void)
{
    u32 i;
    for (i = 0; i < NSLOT; i++) { slot[i].adxt = 0; slot[i].file = -1; }
    for (i = 0; i < NMAP; i++) { fmap[i].key = bmap[i].key = 0; fmap[i].file = bmap[i].file = -1; }
    fnext = bnext = 0;
    for (i = 0; i < TEXW * TEXH; i++) tex_px[i] = 0;
    texinfo.addr = tex_px; texinfo.type = 0x0902 << 16;          /* NJD_TEXFMT_RECTANGLE | NJD_TEXFMT_ARGB_4444 */
    texinfo.w = TEXW; texinfo.h = TEXH;
    texname.name = &texinfo; texname.attr = 0x40800000; texname.texaddr = GBIX;
    texlist.textures = &texname; texlist.n = 1;
    seq = 0; shown = -2; vram = 0;
    inited = 0x4B52;
}
static void remember(void *adxt, s32 file)
{
    u32 i, k = 0;
    for (i = 0; i < NSLOT; i++) if (slot[i].adxt == adxt) { k = i; goto set; }
    for (i = 0; i < NSLOT; i++) if (slot[i].adxt == 0) { k = i; goto set; }
    for (i = 1; i < NSLOT; i++) if (slot[i].seq < slot[k].seq) k = i;
set:
    slot[k].adxt = adxt; slot[k].file = file; slot[k].seq = ++seq;
}
void kr_start_afs(void *adxt, s32 patid, s32 fid)
{
    if (inited != 0x4B52) init();
    ADXT_StartAfs(adxt, patid, fid);
    remember(adxt, fid);
}
void kr_start_mem(void *adxt, void *acx, s32 no)
{
    if (inited != 0x4B52) init();
    ADXT_StartMemIdx(adxt, acx, no);
    remember(adxt, acx == ONMEM_ACX ? (s32)(H()->onmem_base + no) : -1);
}
static void map_set(void *m_, u32 *next, void *key, s32 file)
{
    struct { void *key; s32 file; } *m = m_;
    u32 i;
    for (i = 0; i < NMAP; i++) if (m[i].key == key) { m[i].file = file; return; }
    m[*next].key = key; m[*next].file = file; *next = (*next + 1) % NMAP;
}
static s32 map_get(void *m_, void *key)
{
    struct { void *key; s32 file; } *m = m_;
    u32 i;
    for (i = 0; i < NMAP; i++) if (m[i].key == key) return m[i].file;
    return -1;
}
void *kr_open_afs(s32 patid, s32 fid)
{
    void *h;
    if (inited != 0x4B52) init();
    h = ADXF_OpenAfs(patid, fid);
    if (h) map_set(fmap, &fnext, h, fid);
    return h;
}
s32 kr_read_nw(void *adxf, s32 nsct, void *buf)
{
    s32 r;
    if (inited != 0x4B52) init();
    r = ADXF_ReadNw(adxf, nsct, buf);
    map_set(bmap, &bnext, buf, map_get(fmap, adxf));
    return r;
}
void kr_start_buf(void *adxt, void *adx)
{
    if (inited != 0x4B52) init();
    ADXT_StartMem(adxt, adx);
    remember(adxt, map_get(bmap, adx));
}
/* find our texture in Ninja's texture memory list; load it if it is gone */
static volatile u16 *texture(void)
{
    u32 i, n;
    u8 *e;
    for (n = 0; n < 2; n++) {
        e = TEXMEM_LIST;
        for (i = 0; i < TEXMEM_COUNT; i++, e += 68)
            if (*(u32 *)e == GBIX && *(u32 *)(e + 64)) {
                u32 a = *(u32 *)(e + 52) & 0x00FFFFFF;          /* surface address in texture memory */
                return (volatile u16 *)(0xA4000000 | a);
            }
        if (n == 0 && njLoadTexture(&texlist) != 1) return 0;
    }
    return 0;
}
static void put(volatile u16 *row, s32 x, u16 c) { if ((u32)x < SCRW) row[x] = c; }
static void render(s32 c)
{
    const struct hdr *h = H();
    const u8 *adv = kr_data + h->off_adv;
    const u8 *gl = kr_data + h->off_glyph;
    u32 gsz = h->cellw * h->cellh / 2, y, x, pass;
    volatile u16 *v = vram;
    for (y = 0; y < TEXH; y++)
        for (x = 0; x < SCRW; x += 2) *(volatile u32 *)(v + y * TEXW + x) = 0;
    if (c < 0) return;
    {
        const struct cue *q = (const struct cue *)(kr_data + h->off_cues) + c;
        const u16 *t = (const u16 *)(kr_data + h->off_text + q->text), *p;
        u32 nl = 1, line = 0;
        for (p = t; *p != 0xFFFE; p++) if (*p == 0xFFFF) nl++;
        line = 2 - nl;                         /* a single line sits on the lower row */
        for (p = t; ; ) {
            const u16 *s = p;
            s32 w = 0, x0;
            for (; *p != 0xFFFF && *p != 0xFFFE; p++) w += adv[*p];
            x0 = (SCRW - w) >> 1;
            for (pass = 0; pass < 2; pass++) {
                const u16 *g; s32 xx = x0;
                for (g = s; g < p; g++) {
                    const u8 *b = gl + gsz * *g;
                    u32 gy, gx;
                    for (gy = 0; gy < h->cellh; gy++) {
                        volatile u16 *row = v + (line * h->lineh + gy) * TEXW;
                        for (gx = 0; gx < h->cellw; gx++) {
                            u32 k = b[(gy * h->cellw + gx) >> 1];
                            k = (gx & 1) ? k >> 4 : k & 15;
                            if (k && (pass == 0 || k >= 7)) put(row, xx + (s32)gx - (s32)h->ox, h->pal[k]);
                        }
                    }
                    xx += adv[*g];
                }
            }
            line++;
            if (*p == 0xFFFE) break;
            p++;
        }
    }
}
/* The PVR read registers describe the displayed 32-bit VRAM mapping.
 * A photo is packed BGR888. Interlaced fields have separate bases. */
static s32 photo_layout(void)
{
    volatile u32 *r = (volatile u32 *)0xA05F8000;
    u32 ctrl = r[0x44 / 4], size = r[0x5C / 4];
    u32 xb = ((size & 1023) + 1) * 4;
    u32 stride = ((size & 1023) + ((size >> 20) & 1023)) * 4;
    u32 fields = (r[0xD0 / 4] & 16) ? 2 : 1;
    u32 height = (((size >> 10) & 1023) + 1) * fields;
    u32 base0 = r[0x50 / 4] & 0x7FFFFF;
    u32 base1 = r[0x54 / 4] & 0x7FFFFF;
    u32 y;



    if (!(ctrl & 1) || ((ctrl >> 2) & 3) != 2 || xb != SCRW * 3 ||
            stride < xb || H()->ypos + TEXH > height) return 0;
    if (fields == 2 ? (base1 != base0 + xb || stride != 2 * xb) : stride != xb)
        return 0;
    if (photo_next) {
        photo_target = photo_next + H()->ypos * xb;
        return 1;
    }
    y = base0 + H()->ypos * xb;
    if (y > 0x800000 - TEXH * SCRW * 3) return 0;
    photo_target = (volatile u8 *)(0xA5000000 | y);
    return 1;
}
static void blend_photo(void)
{
    u32 y, x, ch;
    for (x = 0; x < TEXH * SCRW * 3 / 4; x++) {
        u32 c = ((volatile u32 *)photo_target)[x];
        ((u32 *)photo_backup)[x] = c;
        ((u32 *)photo_composed)[x] = c;
    }
    for (y = 0; y < TEXH; y++) {
        u8 *dst = photo_composed[y];
        for (x = 0; x < SCRW; x++) {
            u32 c = tex_px[y * TEXW + x], a = c >> 12;
            if (!a) continue;
            for (ch = 0; ch < 3; ch++) {
                u32 pos = x * 3 + ch, bg = dst[pos];
                u32 fg = ((c >> (ch * 4)) & 15) * 17;
                dst[pos] = (fg * a + bg * (15 - a) + 7) / 15;
            }
        }
    }
    for (x = 0; x < TEXH * SCRW * 3 / 4; x++)
        if (((u32 *)photo_composed)[x] != ((u32 *)photo_backup)[x])
            ((volatile u32 *)photo_target)[x] = ((u32 *)photo_composed)[x];
    photo_saved = photo_next == 0;
}

static s32 find_cue(s32 file, s32 ms)
{
    const struct hdr *h = H();
    const u32 *f;
    const struct cue *q;
    s32 i, best = -1;
    if (file < 0 || (u32)file >= h->nfile) return -1;
    f = (const u32 *)(kr_data + h->off_files) + 2 * file;
    q = (const struct cue *)(kr_data + h->off_cues);
    for (i = f[0]; i < (s32)(f[0] + f[1]); i++) {
        if ((s32)q[i].s > ms) break;
        if (ms < (s32)q[i].e) best = i;
    }
    return best;
}
void kr_frame(void)
{
    u32 i, k = NSLOT;
    s32 c = -1;
    volatile u16 *v;
    if (inited != 0x4B52) init();
    for (i = 0; i < NSLOT; i++) {
        u8 st;
        if (!slot[i].adxt || slot[i].file < 0) continue;
        st = ((volatile u8 *)slot[i].adxt)[1];                 /* ADXT_GetStat */
        if (st < 3 || st > 4) continue;                         /* PLAYING / DECEND */
        if (k == NSLOT || slot[i].seq > slot[k].seq) k = i;
    }
    if (k < NSLOT) {
        s32 n = 0, ts = 1;
        ADXT_GetTime(slot[k].adxt, &n, &ts);
        if (ts > 0) c = find_cue(slot[k].file, (s32)((float)n / (float)ts * 1000.0f));
    }
    if (c < 0 && shown < 0) return;
    if (photo_layout()) {
        if (c < 0) { shown = -1; vram = 0; return; }
        v = tex_px;
        if (v != vram) { vram = v; shown = -2; }
        if (c != shown) { render(c); shown = c; }
        blend_photo();
        return;
    }
    v = texture();
    if (!v) return;
    if (v != vram) { vram = v; shown = -2; }
    if (c != shown) { render(c); shown = c; }
    if (c < 0) return;
    {
        float y0 = (float)H()->ypos, y1 = y0 + TEXH, u1 = (float)SCRW / TEXW;
        float *p = &vtx[0][0];
        u32 *col;
        p[0] = 0;    p[1] = y0; p[2] = SUBZ; p[3] = 0;  p[4] = 0; col = (u32 *)&p[5]; *col = 0xFFFFFFFF;
        p[6] = SCRW; p[7] = y0; p[8] = SUBZ; p[9] = u1; p[10] = 0; col = (u32 *)&p[11]; *col = 0xFFFFFFFF;
        p[12] = 0;   p[13] = y1; p[14] = SUBZ; p[15] = 0; p[16] = 1; col = (u32 *)&p[17]; *col = 0xFFFFFFFF;
        p[18] = SCRW; p[19] = y1; p[20] = SUBZ; p[21] = u1; p[22] = 1; col = (u32 *)&p[23]; *col = 0xFFFFFFFF;
        njDrawTexture(vtx, 4, GBIX, 1);
    }
}

void kr_frame_early(void) {}
void kr_wait_vsync(void) { kr_frame(); GAME_WAIT_VSYNC(); }
