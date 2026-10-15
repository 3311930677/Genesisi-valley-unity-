import cairo, math
S=128
def icon(name, draw):
    surf=cairo.ImageSurface(cairo.FORMAT_ARGB32,S,S); c=cairo.Context(surf)
    c.set_source_rgba(1,1,1,1); c.set_line_cap(cairo.LINE_CAP_ROUND); c.set_line_join(cairo.LINE_JOIN_ROUND); c.set_line_width(9)
    draw(c); surf.write_to_png(f'/data/stage/icons/Icon_{name}.png')
def mountain(c, y0=100):
    c.move_to(10,y0); c.line_to(48,44); c.line_to(66,66); c.line_to(82,50); c.line_to(118,y0); c.close_path(); c.fill()
def arrow(c,x,y1,y2,head=14):
    c.move_to(x,y1); c.line_to(x,y2); c.stroke()
    d=1 if y2>y1 else -1
    c.move_to(x-head,y2-d*head); c.line_to(x,y2); c.line_to(x+head,y2-d*head); c.stroke()
def raise_(c): mountain(c,112); c.set_source_rgba(1,1,1,1); arrow(c,96,44,8)
def lower(c):
    c.move_to(8,60); c.line_to(40,60); c.line_to(64,100); c.line_to(88,60); c.line_to(120,60); c.line_to(120,116); c.line_to(8,116); c.close_path(); c.fill(); arrow(c,64,10,46)
def flatten(c):
    c.rectangle(10,84,108,24); c.fill()
    c.move_to(30,64); c.line_to(98,64); c.stroke(); c.move_to(44,44); c.line_to(84,44); c.stroke()
    c.move_to(64,14); c.line_to(64,30); c.stroke()
def river(c):
    for y in (36,64,92):
        c.move_to(10,y)
        for i in range(4): c.curve_to(10+i*27+7,y-14,10+i*27+20,y+14,10+(i+1)*27,y)
        c.stroke()
def tree(c, x=64, s=1.0):
    c.move_to(x,8*s+0); c.line_to(x+38*s,62); c.line_to(x+18*s,62); c.line_to(x+44*s,96); c.line_to(x-44*s,96); c.line_to(x-18*s,62); c.line_to(x-38*s,62); c.close_path(); c.fill()
    c.rectangle(x-8,94,16,26); c.fill()
def forest(c):
    c.save(); c.translate(-22,14); c.scale(0.8,0.8); tree(c,64); c.restore()
    c.save(); c.translate(30,0); c.scale(0.85,0.95); tree(c,64); c.restore()
def person(c,x=64,y=0,s=1.0):
    c.arc(x,y+30*s,17*s,0,2*math.pi); c.fill()
    c.move_to(x-34*s,y+118*s); c.curve_to(x-34*s,y+62*s,x+34*s,y+62*s,x+34*s,y+118*s); c.close_path(); c.fill()
def summon(c):
    person(c,54,4,0.9)
    for a in range(8):
        r1,r2=10,20; ang=a*math.pi/4
        c.move_to(102+math.cos(ang)*6,30+math.sin(ang)*6); c.line_to(102+math.cos(ang)*(r2 if a%2==0 else 14),30+math.sin(ang)*(r2 if a%2==0 else 14)); c.stroke()
def bridge(c):
    c.set_line_width(10); c.move_to(6,58); c.line_to(122,58); c.stroke()
    c.move_to(14,104); c.curve_to(34,58,94,58,114,104); c.stroke()
    for x in (34,50,64,78,94): c.move_to(x,58); c.line_to(x,74+abs(x-64)*0.5); c.stroke()
    c.set_line_width(6); c.move_to(6,112);
    for i in range(6): c.curve_to(6+i*20+5,106,6+i*20+15,118,6+(i+1)*20,112)
    c.stroke()
def undo(c):
    c.set_line_width(11); c.arc_negative(70,72,36,math.radians(80),math.radians(200)); c.stroke()
    c.move_to(14,38); c.line_to(36,62); c.line_to(52,30); c.close_path(); c.fill()
def observe(c):
    c.move_to(8,64); c.curve_to(36,24,92,24,120,64); c.curve_to(92,104,36,104,8,64); c.close_path(); c.stroke()
    c.arc(64,64,18,0,2*math.pi); c.fill()
def food(c):
    c.set_line_width(7); c.move_to(64,122); c.line_to(64,30); c.stroke()
    for i,y in enumerate((36,56,76)):
        for d in (-1,1):
            c.save(); c.translate(64+d*12,y); c.rotate(d*0.5); c.scale(1,1.9); c.arc(0,0,9,0,2*math.pi); c.restore(); c.fill()
    c.save(); c.translate(64,18); c.scale(1,1.8); c.arc(0,0,8,0,2*math.pi); c.restore(); c.fill()
def wood(c):
    for (x,y) in ((40,88),(88,88),(64,48)):
        c.arc(x,y,24,0,2*math.pi); c.fill()
        c.set_operator(cairo.OPERATOR_CLEAR); c.set_line_width(4); c.arc(x,y,13,0,2*math.pi); c.stroke(); c.arc(x,y,4,0,2*math.pi); c.fill(); c.set_operator(cairo.OPERATOR_OVER)
def water(c):
    c.move_to(64,8); c.curve_to(80,40,104,62,104,82); c.curve_to(104,106,86,122,64,122); c.curve_to(42,122,24,106,24,82); c.curve_to(24,62,48,40,64,8); c.close_path(); c.fill()
    c.set_operator(cairo.OPERATOR_CLEAR); c.set_line_width(7); c.arc(64,84,24,math.radians(100),math.radians(170)); c.stroke(); c.set_operator(cairo.OPERATOR_OVER)
def population(c):
    c.save(); person(c,38,22,0.72); person(c,90,22,0.72); c.restore(); person(c,64,6,0.95)
def house(c):
    c.move_to(8,62); c.line_to(64,14); c.line_to(120,62); c.close_path(); c.fill()
    c.rectangle(22,58,84,62); c.fill()
    c.set_operator(cairo.OPERATOR_CLEAR); c.rectangle(54,82,22,38); c.fill(); c.set_operator(cairo.OPERATOR_OVER)
def bed(c):
    c.rectangle(10,30,12,84); c.fill(); c.rectangle(10,70,108,22); c.fill(); c.rectangle(106,70,12,44); c.fill()
    c.save(); c.translate(42,58); c.scale(1.6,1); c.arc(0,0,11,0,2*math.pi); c.restore(); c.fill()
    c.rectangle(58,52,60,18); c.fill()
def storage(c):
    c.rectangle(12,24,104,84); c.set_line_width(10); c.stroke()
    c.move_to(14,26); c.line_to(114,106); c.stroke(); c.move_to(114,26); c.line_to(14,106); c.stroke()
def sun(c):
    c.arc(64,64,24,0,2*math.pi); c.fill()
    for i in range(8):
        a=i*math.pi/4; c.move_to(64+math.cos(a)*38,64+math.sin(a)*38); c.line_to(64+math.cos(a)*54,64+math.sin(a)*54); c.stroke()
def moon(c):
    c.arc(64,64,46,0,2*math.pi); c.fill(); c.set_operator(cairo.OPERATOR_CLEAR); c.arc(86,46,40,0,2*math.pi); c.fill(); c.set_operator(cairo.OPERATOR_OVER)
def pause(c): c.rectangle(30,22,24,84); c.fill(); c.rectangle(74,22,24,84); c.fill()
def tri(c,x,w=40): c.move_to(x,22); c.line_to(x+w,64); c.line_to(x,106); c.close_path(); c.fill()
def play(c): tri(c,38,56)
def fast(c): tri(c,18,46); tri(c,66,46)
def faster(c): tri(c,4,38); tri(c,44,38); tri(c,84,38)
def save(c):
    c.move_to(14,14); c.line_to(96,14); c.line_to(114,32); c.line_to(114,114); c.line_to(14,114); c.close_path(); c.fill()
    c.set_operator(cairo.OPERATOR_CLEAR); c.rectangle(32,14,52,30); c.fill(); c.rectangle(30,66,68,48); c.fill(); c.set_operator(cairo.OPERATOR_OVER)
    c.rectangle(68,20,10,20); c.fill()
def load(c):
    c.move_to(10,36); c.line_to(48,36); c.line_to(58,48); c.line_to(118,48); c.line_to(118,112); c.line_to(10,112); c.close_path(); c.fill()
def gear(c):
    for i in range(8):
        a=i*math.pi/4; c.save(); c.translate(64,64); c.rotate(a); c.rectangle(-10,-58,20,24); c.restore(); c.fill()
    c.arc(64,64,38,0,2*math.pi); c.fill(); c.set_operator(cairo.OPERATOR_CLEAR); c.arc(64,64,16,0,2*math.pi); c.fill(); c.set_operator(cairo.OPERATOR_OVER)
def menu(c):
    for y in (32,64,96): c.move_to(20,y); c.line_to(108,y); c.stroke()
def close(c): c.set_line_width(12); c.move_to(28,28); c.line_to(100,100); c.stroke(); c.move_to(100,28); c.line_to(28,100); c.stroke()
def warning(c):
    c.move_to(64,10); c.line_to(122,112); c.line_to(6,112); c.close_path(); c.fill()
    c.set_operator(cairo.OPERATOR_CLEAR); c.rectangle(58,42,12,40); c.fill(); c.arc(64,96,7,0,2*math.pi); c.fill(); c.set_operator(cairo.OPERATOR_OVER)
def info(c):
    c.arc(64,64,56,0,2*math.pi); c.fill(); c.set_operator(cairo.OPERATOR_CLEAR); c.rectangle(57,54,14,44); c.fill(); c.arc(64,36,8,0,2*math.pi); c.fill(); c.set_operator(cairo.OPERATOR_OVER)
def star(c):
    c.move_to(64,8)
    for i in range(1,11):
        a=-math.pi/2+i*math.pi/5; r=56 if i%2==0 else 24; c.line_to(64+math.cos(a)*r,66+math.sin(a)*r)
    c.close_path(); c.fill()
def check(c): c.set_line_width(14); c.move_to(18,66); c.line_to(50,98); c.line_to(110,30); c.stroke()
def scroll(c):
    c.rectangle(26,14,76,100); c.fill(); c.set_operator(cairo.OPERATOR_CLEAR); c.set_line_width(7)
    for y in (40,60,80): c.move_to(40,y); c.line_to(88,y); c.stroke()
    c.set_operator(cairo.OPERATOR_OVER); c.arc(26,22,12,0,2*math.pi); c.fill(); c.arc(102,106,12,0,2*math.pi); c.fill()
def camera(c):
    c.rectangle(10,36,108,74); c.fill(); c.rectangle(40,22,40,18); c.fill(); c.set_operator(cairo.OPERATOR_CLEAR); c.arc(64,73,24,0,2*math.pi); c.fill(); c.set_operator(cairo.OPERATOR_OVER); c.arc(64,73,13,0,2*math.pi); c.fill()
def hammer(c):
    c.save(); c.translate(64,64); c.rotate(math.radians(-40)); c.rectangle(-7,-20,14,80); c.fill(); c.rectangle(-34,-46,68,28); c.fill(); c.restore()
def axe(c):
    c.save(); c.translate(64,64); c.rotate(math.radians(35)); c.rectangle(-6,-46,12,104); c.fill()
    c.move_to(4,-48); c.line_to(30,-58); c.curve_to(46,-40,46,-14,30,2); c.line_to(4,-12); c.close_path(); c.fill(); c.restore()
def fish(c):
    c.save(); c.translate(56,64); c.scale(1.6,1); c.arc(0,0,30,0,2*math.pi); c.restore(); c.fill()
    c.move_to(96,64); c.line_to(122,38); c.line_to(122,90); c.close_path(); c.fill()
    c.set_operator(cairo.OPERATOR_CLEAR); c.arc(28,58,6,0,2*math.pi); c.fill(); c.set_operator(cairo.OPERATOR_OVER)
def heart(c):
    c.move_to(64,116); c.curve_to(4,72,10,14,64,40); c.curve_to(118,14,124,72,64,116); c.fill()
def zzz(c):
    c.set_line_width(8)
    for (x,y,s) in ((12,78,36),(62,50,26),(98,24,18)):
        c.move_to(x,y); c.line_to(x+s,y); c.line_to(x,y+s); c.line_to(x+s,y+s); c.stroke()
def target(c):
    c.arc(64,64,50,0,2*math.pi); c.stroke(); c.arc(64,64,26,0,2*math.pi); c.stroke(); c.arc(64,64,8,0,2*math.pi); c.fill()
def exit_(c):
    c.rectangle(16,14,56,100); c.set_line_width(9); c.stroke(); c.move_to(56,64); c.line_to(118,64); c.stroke(); c.move_to(100,46); c.line_to(118,64); c.line_to(100,82); c.stroke()
def sound(c):
    c.move_to(14,48); c.line_to(38,48); c.line_to(66,22); c.line_to(66,106); c.line_to(38,80); c.line_to(14,80); c.close_path(); c.fill()
    c.arc(66,64,26,-0.9,0.9); c.stroke(); c.arc(66,64,46,-0.8,0.8); c.stroke()
def follow(c):
    person(c,64,20,0.7); c.set_line_width(7); c.arc(64,64,58,math.radians(200),math.radians(340)); c.stroke()
for n,f in dict(Observe=observe,Raise=raise_,Lower=lower,Flatten=flatten,River=river,Forest=forest,Summon=summon,Bridge=bridge,Undo=undo,
  Food=food,Wood=wood,Water=water,Population=population,House=house,Bed=bed,Storage=storage,Sun=sun,Moon=moon,Pause=pause,Play=play,Fast=fast,Faster=faster,
  Save=save,Load=load,Gear=gear,Menu=menu,Close=close,Warning=warning,Info=info,Star=star,Check=check,Scroll=scroll,Camera=camera,Hammer=hammer,Axe=axe,
  Fish=fish,Heart=heart,Sleep=zzz,Target=target,Exit=exit_,Sound=sound,Follow=follow,Tree=lambda c: tree(c)).items(): icon(n,f)
print('icons ok')
