import numpy as np, cv2, math
out='/data/stage/gen/'
rng=np.random.default_rng(7)
def periodic_noise(n, octaves, seed):
    r=np.random.default_rng(seed); acc=np.zeros((n,n),np.float32); amp=1; tot=0
    for o in range(octaves):
        f=4*2**o
        g=r.standard_normal((f,f)).astype(np.float32)
        up=cv2.resize(np.tile(g,(3,3)),(3*n,3*n),interpolation=cv2.INTER_CUBIC)[n:2*n,n:2*n]
        acc+=up*amp; tot+=amp; amp*=0.5
    return acc/tot
# --- water ripple normal (tileable)
n=512
h=np.zeros((n,n),np.float32)
yy,xx=np.mgrid[0:n,0:n]/n
for i in range(40):
    kx,ky=rng.integers(-9,10,2)
    if kx==0 and ky==0: continue
    ph=rng.uniform(0,2*math.pi); a=1/math.hypot(kx,ky)**1.2
    h+=a*np.sin(2*math.pi*(kx*xx+ky*yy)+ph)
h+=periodic_noise(n,4,3)*0.8
h=(h-h.min())/(h.max()-h.min())
dx=(np.roll(h,-1,1)-np.roll(h,1,1))*n*0.018; dy=(np.roll(h,-1,0)-np.roll(h,1,0))*n*0.018
nz=np.ones_like(h); l=np.sqrt(dx*dx+dy*dy+1)
nrm=np.dstack([(-dx/l)*0.5+0.5,(-dy/l)*0.5+0.5,(nz/l)*0.5+0.5])  # RGB
cv2.imwrite(out+'T_Water_Ripple_N.png',(nrm[:,:,::-1]*255).astype(np.uint8))
# --- particle textures (white RGBA, alpha carries shape)
def save_rgba(name,a,rgb=None):
    a=np.clip(a,0,1); s=a.shape[0]
    if rgb is None: rgb=np.ones((s,s,3),np.float32)
    img=np.dstack([rgb[:,:,2],rgb[:,:,1],rgb[:,:,0],a]); cv2.imwrite(out+name,(img*255).astype(np.uint8))
s=128; y,x=np.mgrid[-1:1:s*1j,-1:1:s*1j]; r=np.sqrt(x*x+y*y)
save_rgba('T_FX_SoftDot.png',(1-np.clip(r,0,1))**2)
puff=np.zeros((s,s),np.float32)
for i in range(14):
    cx,cy=rng.uniform(-0.35,0.35,2); rr=rng.uniform(0.25,0.5)
    puff=np.maximum(puff,np.clip(1-np.sqrt((x-cx)**2+(y-cy)**2)/rr,0,1)**1.5)
nz=cv2.resize(rng.random((16,16)).astype(np.float32),(s,s),interpolation=cv2.INTER_CUBIC)
save_rgba('T_FX_Smoke.png',np.clip(puff*(0.7+0.5*nz)*(1-np.clip(r,0,1)),0,1))
star=np.clip(1-r*3,0,1)+np.clip(1-np.abs(x)*18,0,1)*np.clip(1-np.abs(y),0,1)**3+np.clip(1-np.abs(y)*18,0,1)*np.clip(1-np.abs(x),0,1)**3
save_rgba('T_FX_Spark.png',np.clip(star,0,1))
leaf=((x/0.55)**2+(y/0.95)**2<1).astype(np.float32)*(np.abs(x)>0.04).astype(np.float32)*0.9+((np.abs(x)<0.04)&(np.abs(y)<0.95))*1.0
leaf=cv2.GaussianBlur(leaf,(3,3),0); save_rgba('T_FX_Leaf.png',leaf)
chip=((np.abs(x)<0.35)&(np.abs(y)<0.8)).astype(np.float32); chip=cv2.GaussianBlur(chip,(5,5),0); save_rgba('T_FX_Chip.png',chip)
fl=np.clip(1-np.sqrt(x*x*2.2+(y+0.25)**2*0.9),0,1)**1.6*(y<0.6)
save_rgba('T_FX_Flame.png',cv2.GaussianBlur(fl.astype(np.float32),(9,9),0))
# --- overlay textures: selection ring and brush ring (u around, v radial handled in mesh) -> radial textures
s=256; y,x=np.mgrid[-1:1:s*1j,-1:1:s*1j]; r=np.sqrt(x*x+y*y)
ring=np.exp(-((r-0.86)/0.05)**2)+np.clip(1-r,0,1)**0.5*0.18*(r<0.86)
save_rgba('T_UI_SelectRing.png',np.clip(ring,0,1)*(r<1))
brush=np.exp(-((r-0.95)/0.025)**2)*1.0+(r<0.95)*(0.12+0.25*(1-r)**2)
ticks=(np.abs(np.sin(np.arctan2(y,x)*24))<0.12)*(r>0.88)*(r<0.97)*0.8
save_rgba('T_UI_Brush.png',np.clip(brush+ticks,0,1)*(r<1))
# dashed line texture for previews (u along)
line=np.zeros((32,128),np.float32); line[:, :80]=1; line=cv2.GaussianBlur(line,(5,1),0)
edge=np.clip(1-np.abs(np.linspace(-1,1,32))**4,0,1)[:,None]; a=line*edge
img=np.dstack([np.ones_like(a),np.ones_like(a),np.ones_like(a),a]); cv2.imwrite(out+'T_UI_Dash.png',(img*255).astype(np.uint8))
print('ok')
