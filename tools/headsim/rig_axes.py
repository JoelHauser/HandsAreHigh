import UnityPy, sys, math
env = UnityPy.load(sys.argv[1])
tr = {}; go = {}
for o in env.objects:
    if o.type.name == "GameObject": go[o.path_id] = o.read().m_Name
for o in env.objects:
    if o.type.name == "Transform":
        t = o.read()
        tr[o.path_id] = t
def qmul(a,b):
    ax,ay,az,aw=a; bx,by,bz,bw=b
    return (aw*bx+ax*bw+ay*bz-az*by, aw*by-ax*bz+ay*bw+az*bx, aw*bz+ax*by-ay*bx+az*bw, aw*bw-ax*bx-ay*by-az*bz)
def rot(q,v):
    x,y,z,w=q; vx,vy,vz=v
    # q*v*q^-1
    ix= w*vx+y*vz-z*vy; iy= w*vy+z*vx-x*vz; iz= w*vz+x*vy-y*vx; iw=-x*vx-y*vy-z*vz
    return (ix*w+iw*-x+iy*-z-iz*-y, iy*w+iw*-y+iz*-x-ix*-z, iz*w+iw*-z+ix*-y-iy*-x)
byname = {}
parent = {}
for pid,t in tr.items():
    n = go.get(t.m_GameObject.path_id)
    byname[n] = pid
    parent[pid] = t.m_Father.path_id if t.m_Father else 0
def world(pid):
    q=(0,0,0,1); p=(0,0,0)
    chain=[]
    while pid in tr:
        chain.append(pid); pid=parent[pid]
    for c in reversed(chain):
        t=tr[c]; lr=t.m_LocalRotation; lq=(lr.x,lr.y,lr.z,lr.w); lp=t.m_LocalPosition
        p = tuple(a+b for a,b in zip(p, rot(q,(lp.x,lp.y,lp.z))))
        q = qmul(q,lq)
    return q,p
def f(v): return "(%+.2f %+.2f %+.2f)"%v
for n in ["Root_Joint","Base HumanPelvis","Base HumanSpine1","Base HumanSpine3","Base HumanRibcage","Base HumanNeck","Base HumanHead","Base HumanRCollarbone"]:
    if n not in byname: print("missing",n); continue
    q,p = world(byname[n])
    print("%-22s pos %s  x %s y %s z %s"%(n, f(p), f(rot(q,(1,0,0))), f(rot(q,(0,1,0))), f(rot(q,(0,0,1)))))
