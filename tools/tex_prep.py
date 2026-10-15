import cv2, numpy as np, sys
# name -> (out name, target mean sRGB (B,G,R order handled), saturation mult, contrast)
specs = {
 'leafy_grass':        ('T_Ground_Grass',     (48,112,82),  1.0, 1.0),
 'forrest_ground_01':  ('T_Ground_Meadow',    (60,136,116), 0.95, 1.0),
 'brown_mud_leaves_01':('T_Ground_ForestFloor',(46,70,80),  0.85, 1.0),
 'rocky_trail':        ('T_Ground_Path',      (92,122,146), 0.85, 1.0),
 'aerial_rocks_02':    ('T_Ground_Cliff',     (100,118,118),0.85, 1.0),
 'river_small_rocks':  ('T_Ground_Riverbank', (98,112,120), 0.9,  1.0),
 'muddy_tracks':       ('T_Ground_Soil',      (40,62,88),   0.9,  1.0),
}
def lin(x): return np.where(x<=0.04045, x/12.92, ((x+0.055)/1.055)**2.4)
def srgb(x): return np.where(x<=0.0031308, x*12.92, 1.055*np.power(np.clip(x,0,None),1/2.4)-0.055)
for src,(dst,target,sat,con) in specs.items():
    im = cv2.imread(f'{src}_diff_2k.jpg').astype(np.float32)/255.0
    im = cv2.resize(im,(1024,1024),interpolation=cv2.INTER_AREA)
    L = lin(im)
    mean = L.reshape(-1,3).mean(0)
    tgt = lin(np.array(target,np.float32)/255.0)
    L = L * (tgt/np.maximum(mean,1e-4))
    g = L.mean(2,keepdims=True)
    L = g + (L-g)*sat
    out = np.clip(srgb(np.clip(L,0,1)),0,1)
    cv2.imwrite(f'/data/stage/tex/{dst}_D.jpg',(out*255+0.5).astype(np.uint8),[cv2.IMWRITE_JPEG_QUALITY,93])
    n = cv2.imread(f'{src}_nor_gl_2k.jpg'); n = cv2.resize(n,(1024,1024),interpolation=cv2.INTER_AREA)
    cv2.imwrite(f'/data/stage/tex/{dst}_N.jpg', n, [cv2.IMWRITE_JPEG_QUALITY,95])
    print(dst, (srgb(L.reshape(-1,3).mean(0))*255).astype(int)[::-1])
