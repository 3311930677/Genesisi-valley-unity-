"""Builds the curated Assets/ThirdParty tree (CC0 packs) with downscaled, Unity-ready textures."""
import os, shutil, sys
from PIL import Image
import numpy as np

X = '/data/dl/x'
V = f'{X}/village/Medieval Village MegaKit[Standard]'
N = f'{X}/nature'
C = f'{X}/chars/Universal Base Characters[Standard]'
O = f'{X}/outfits/Modular Character Outfits - Fantasy[Standard]'
A = f'{X}/anim/Universal Animation Library[Standard]'
OUT = sys.argv[1] if len(sys.argv) > 1 else '/data/repo/GenesisValley/Assets/ThirdParty'
Q = f'{OUT}/Quaternius'

def mk(p): os.makedirs(p, exist_ok=True); return p

def img(path, size=None, mode=None):
    im = Image.open(path)
    if mode: im = im.convert(mode)
    if size and max(im.size) > size:
        r = size / max(im.size)
        im = im.resize((max(1, round(im.size[0] * r)), max(1, round(im.size[1] * r))), Image.LANCZOS)
    return im

def jpg(src, dst, size, q=90):
    img(src, size, 'RGB').save(dst, quality=q, optimize=True, subsampling=0)

def png(src, dst, size, mode=None):
    im = img(src, size, mode)
    im.save(dst, optimize=True)

def normal(src, dst, size):
    png(src, dst, size, 'RGB')

def maskmap(dst, size, orm=None, rough=None, smooth_default=0.2, metal=0.0, ao_strength=1.0):
    """URP Lit packing used by the material builder: R metallic, G occlusion, B 0, A smoothness."""
    if orm:
        a = np.asarray(img(orm, size, 'RGB')).astype(np.float32) / 255.0
        ao, r, m = a[..., 0], a[..., 1], a[..., 2]
    elif rough:
        r = np.asarray(img(rough, size, 'L')).astype(np.float32) / 255.0
        ao = np.ones_like(r); m = np.zeros_like(r)
    else:
        r = np.full((size, size), 1 - smooth_default, np.float32); ao = np.ones_like(r); m = np.zeros_like(r)
    ao = 1 - (1 - ao) * ao_strength
    m = np.maximum(m, metal)
    out = np.stack([m, ao, np.zeros_like(r), 1 - r], -1)
    Image.fromarray((np.clip(out, 0, 1) * 255 + 0.5).astype(np.uint8), 'RGBA').save(dst, optimize=True)

def copy_fbx(srcdir, dstdir, names=None):
    mk(dstdir); n = 0
    for f in sorted(os.listdir(srcdir)):
        if not f.lower().endswith('.fbx'): continue
        if names is not None and os.path.splitext(f)[0] not in names: continue
        shutil.copy2(os.path.join(srcdir, f), os.path.join(dstdir, f)); n += 1
    return n

if os.path.exists(OUT): shutil.rmtree(OUT)

# ---------------- Medieval Village MegaKit ----------------
vt = mk(f'{Q}/MedievalVillage/Textures')
print('village fbx', copy_fbx(f'{V}/FBX', f'{Q}/MedievalVillage/Models'))
T = f'{V}/Textures'; TN = f'{T}/Normals Godot-Unity'
for m in ['Brick', 'Plaster', 'RedBrick', 'RockTrim', 'RoundTiles', 'UnevenBrick', 'WoodTrim']:
    jpg(f'{T}/T_{m}_BaseColor.png', f'{vt}/T_{m}_BaseColor.jpg', 2048 if m in ('Plaster', 'WoodTrim', 'RoundTiles') else 1024)
for m in ['Brick', 'Plaster', 'RockTrim', 'RoundTiles', 'UnevenBrick', 'WoodTrim']:
    normal(f'{TN}/T_{m}_Normal.png', f'{vt}/T_{m}_Normal.png', 1024)
maskmap(f'{vt}/T_Plaster_MaskMap.png', 1024, orm=f'{T}/T_Plaster_ORM.png')
maskmap(f'{vt}/T_RockTrim_MaskMap.png', 1024, orm=f'{T}/T_RockTrim_ORM.png')
maskmap(f'{vt}/T_WoodTrim_MaskMap.png', 1024, orm=f'{T}/T_WoodTrim_ORM.png')
for m in ['Brick', 'RoundTiles', 'UnevenBrick']:
    maskmap(f'{vt}/T_{m}_MaskMap.png', 1024, rough=f'{T}/T_{m}_Roughness.png')
png(f'{T}/T_VineLeaf.png', f'{vt}/T_VineLeaf.png', 512, 'RGBA')
png(f'{T}/T_WindowGradient.png', f'{vt}/T_WindowGradient.png', 256, 'RGB')
shutil.copy2(f'{V}/License_Standard.txt', f'{Q}/MedievalVillage/License.txt')

# ---------------- Stylized Nature MegaKit ----------------
nt = mk(f'{Q}/StylizedNature/Textures')
print('nature fbx', copy_fbx(f'{N}/FBX (Unity)', f'{Q}/StylizedNature/Models'))
NT = f'{N}/Textures'
for b in ['Bark_NormalTree', 'Bark_DeadTree', 'Bark_TwistedTree']:
    jpg(f'{NT}/{b}.png', f'{nt}/{b}.jpg', 1024)
    normal(f'{NT}/{b}_Normal.png', f'{nt}/{b}_Normal.png', 1024)
for l, s in [('Leaves_NormalTree_C', 1024), ('Leaf_Pine_C', 1024), ('Leaves_TwistedTree_C', 1024), ('Leaves', 1024), ('Flowers', 1024), ('Grass', 512)]:
    png(f'{NT}/{l}.png', f'{nt}/{l}.png', s, 'RGBA')
for d in ['Mushrooms', 'PathRocks_Diffuse', 'Rocks_Diffuse']:
    jpg(f'{NT}/{d}.png', f'{nt}/{d}.jpg', 1024)
shutil.copy2(f'{N}/License_Standard.txt', f'{Q}/StylizedNature/License.txt')

# ---------------- Characters ----------------
ch = mk(f'{Q}/Characters')
ct = mk(f'{ch}/Textures')
copy_fbx(f'{C}/Base Characters/Unity', f'{ch}/Base')
copy_fbx(f'{C}/Hairstyles/Rigged to Head Bone/FBX (Unity)', f'{ch}/Hair')
print('outfit parts', copy_fbx(f'{O}/Exports/FBX (Unity)/Modular Parts', f'{ch}/Outfits'))
BT = f'{C}/Base Characters/Textures'
jpg(f'{BT}/T_Superhero_Male_Dark.png', f'{ct}/T_Superhero_Male_BaseColor.jpg', 1024)
jpg(f'{BT}/T_Superhero_Female_Dark_BaseColor.png', f'{ct}/T_Superhero_Female_BaseColor.jpg', 1024)
for g in ['Male', 'Female']:
    normal(f'{BT}/Normals Unity - Godot/T_Superhero_{g}_Normal.png', f'{ct}/T_Superhero_{g}_Normal.png', 1024)
    maskmap(f'{ct}/T_Superhero_{g}_MaskMap.png', 512, rough=f'{BT}/T_Superhero_{g}_Roughness.png')
png(f'{BT}/T_Eye_Brown.png', f'{ct}/T_Eye_Brown.png', 256, 'RGB')
HT = f'{C}/Hairstyles/Textures'
for h in ['1', '2']:
    jpg(f'{HT}/T_Hair_{h}_BaseColor.png', f'{ct}/T_Hair_{h}_BaseColor.jpg', 1024)
    normal(f'{HT}/Normals Unity - Godot/T_Hair_{h}_Normal.png', f'{ct}/T_Hair_{h}_Normal.png', 1024)
OT = f'{O}/Textures'
for g in ['Male', 'Female']:
    jpg(f'{OT}/Base/T_Regular_{g}_Dark_BaseColor.png', f'{ct}/T_Regular_{g}_BaseColor.jpg', 1024)
    normal(f'{OT}/Base/T_Regular_{g}_Normal.png', f'{ct}/T_Regular_{g}_Normal.png', 1024)
    maskmap(f'{ct}/T_Regular_{g}_MaskMap.png', 512, rough=f'{OT}/Base/T_Regular_{g}_Roughness.png')
for o, variants in [('Peasant', ['', '_2']), ('Ranger', ['', '_3'])]:
    for v in variants:
        jpg(f'{OT}/{o}/T_{o}{v}_BaseColor.png', f'{ct}/T_{o}{v}_BaseColor.jpg', 2048, 88)
    normal(f'{OT}/{o}/T_{o}_Normal.png', f'{ct}/T_{o}_Normal.png', 1024)
    maskmap(f'{ct}/T_{o}_MaskMap.png', 1024, orm=f'{OT}/{o}/T_{o}_ORM.png')
shutil.copy2(f'{C}/License_Standard.txt', f'{ch}/License_BaseCharacters.txt')
shutil.copy2(f'{O}/License_Standard.txt', f'{ch}/License_Outfits.txt')

# ---------------- Animations ----------------
an = mk(f'{Q}/Animations')
shutil.copy2(f'{A}/Unity/UAL1_Standard.fbx', f'{an}/UAL1_Standard.fbx')
lic = [f for f in os.listdir(A) if f.lower().startswith('licen')]
if lic: shutil.copy2(f'{A}/{lic[0]}', f'{an}/License.txt')
print('done')

# ---------------- recoloured leaf variants (same UV layout as the source atlas) ----------------
def recolor(src, dst, hue_deg, sat_mul, val_mul):
    im = Image.open(src).convert('RGBA'); a = np.asarray(im)
    hsv = np.asarray(Image.fromarray(a[..., :3]).convert('HSV')).astype(np.float32)
    mask = a[..., 3] > 8
    hsv[..., 0] = (hue_deg / 360.0 * 255.0)
    # keep a little of the per-texel hue spread for richness
    src_h = np.asarray(Image.fromarray(a[..., :3]).convert('HSV')).astype(np.float32)[..., 0]
    spread = ((src_h - np.median(src_h[mask])) + 128) % 256 - 128
    hsv[..., 0] = (hsv[..., 0] + spread * 0.5) % 256
    hsv[..., 1] = np.clip(hsv[..., 1] * sat_mul, 0, 255)
    hsv[..., 2] = np.clip(hsv[..., 2] * val_mul, 0, 255)
    rgb = np.asarray(Image.fromarray(hsv.astype(np.uint8), 'HSV').convert('RGB'))
    out = np.concatenate([rgb, a[..., 3:4]], -1)
    Image.fromarray(out, 'RGBA').save(dst, optimize=True)
    m = out[mask][:, :3].mean(0).round(); print('recolor', os.path.basename(dst), m)

recolor(f'{NT}/Leaves_TwistedTree_C.png', f'{nt}/Leaves_TwistedTree_Olive.png', 60, 0.70, 0.86)
recolor(f'{NT}/Leaves_TwistedTree_C.png', f'{nt}/Leaves_Bush.png', 88, 0.85, 0.85)
