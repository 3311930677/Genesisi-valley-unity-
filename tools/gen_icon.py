import math, random
from PIL import Image, ImageDraw, ImageFilter
S=4096; random.seed(7)
img=Image.new('RGBA',(S,S),(0,0,0,0))
sky=Image.new('RGB',(S,S))
d=ImageDraw.Draw(sky)
top=(52,86,150); mid=(236,170,120); low=(255,214,150)
for y in range(S):
    t=y/S
    if t<0.55: a=t/0.55; c=tuple(int(top[i]*(1-a)+mid[i]*a) for i in range(3))
    else: a=min(1,(t-0.55)/0.3); c=tuple(int(mid[i]*(1-a)+low[i]*a) for i in range(3))
    d.line([(0,y),(S,y)],fill=c)
# sun glow
glow=Image.new('L',(S,S),0); gd=ImageDraw.Draw(glow)
cx,cy=int(S*0.62),int(S*0.47)
for r in range(1400,0,-20):
    gd.ellipse([cx-r,cy-r,cx+r,cy+r],fill=int(255*(1-r/1400)**2.2))
sky=Image.composite(Image.new('RGB',(S,S),(255,236,190)),sky,glow)
d=ImageDraw.Draw(sky)
d.ellipse([cx-300,cy-300,cx+300,cy+300],fill=(255,246,220))
def ridge(base,amp,freq,col,seed):
    rnd=random.Random(seed); ph=[rnd.random()*6.28 for _ in range(4)]
    pts=[(0,S)]
    for x in range(0,S+1,16):
        u=x/S
        y=base+amp*(0.55*math.sin(u*freq+ph[0])+0.3*math.sin(u*freq*2.3+ph[1])+0.15*math.sin(u*freq*5.1+ph[2]))
        pts.append((x,y))
    pts.append((S,S)); d.polygon(pts,fill=col)
ridge(S*0.56,S*0.06,5,(150,120,150),1)
ridge(S*0.63,S*0.05,6,(104,118,120),2)
ridge(S*0.70,S*0.045,4,(76,120,82),3)
ridge(S*0.78,S*0.03,3,(58,104,60),4)
# river
pts=[]
for i in range(101):
    t=i/100; y=S*0.66+t*S*0.36
    x=S*0.55+math.sin(t*3.2+0.6)*S*0.16*(0.3+t)
    w=S*(0.006+0.11*t**1.6)
    pts.append((x,y,w))
left=[(x-w,y) for x,y,w in pts]; right=[(x+w,y) for x,y,w in reversed(pts)]
d.polygon(left+right,fill=(120,190,215))
left=[(x-w*0.55,y) for x,y,w in pts]; right=[(x+w*0.15,y) for x,y,w in reversed(pts)]
d.polygon(left+right,fill=(176,222,236))
# trees
def tree(x,y,h,col):
    d.rectangle([x-h*0.05,y-h*0.25,x+h*0.05,y],fill=(70,48,36))
    for k in range(3):
        yy=y-h*0.2-k*h*0.25; w=h*(0.36-k*0.08)
        d.polygon([(x-w,yy),(x+w,yy),(x,yy-h*0.42)],fill=col)
for i in range(14):
    x=random.uniform(0.02,0.4)*S; y=S*(0.80+random.uniform(0,0.15))
    tree(x,y,S*random.uniform(0.09,0.15),(36+random.randint(0,20),86+random.randint(0,25),52))
for i in range(6):
    x=random.uniform(0.82,0.99)*S; y=S*(0.84+random.uniform(0,0.12))
    tree(x,y,S*random.uniform(0.09,0.14),(40,92,56))
# house
hx,hy=S*0.76,S*0.79
d.rectangle([hx-170,hy-200,hx+170,hy],fill=(236,222,196))
d.polygon([(hx-230,hy-190),(hx+230,hy-190),(hx,hy-430)],fill=(176,74,52))
d.rectangle([hx-40,hy-110,hx+40,hy],fill=(110,70,44))
d.rectangle([hx+80,hy-150,hx+140,hy-90],fill=(255,206,110))
img.paste(sky,(0,0))
mask=Image.new('L',(S,S),0); ImageDraw.Draw(mask).rounded_rectangle([0,0,S-1,S-1],radius=int(S*0.18),fill=255)
img.putalpha(mask)
img=img.resize((1024,1024),Image.LANCZOS)
img.save('/data/repo/GenesisValley/Assets/Game/Art/Branding/GV_AppIcon.png')
