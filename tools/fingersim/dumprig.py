import UnityPy, sys
env = UnityPy.load(sys.argv[1])
tr = {}; go = {}
for o in env.objects:
    if o.type.name == "GameObject": go[o.path_id] = o.read().m_Name
for o in env.objects:
    if o.type.name == "Transform": tr[o.path_id] = o.read()
byname = {}
for pid, t in tr.items(): byname.setdefault(go.get(t.m_GameObject.path_id), pid)
for side in "LR":
    for n in ["Base Human%sPalm" % side] + ["Base Human%sDigit%d%d" % (side, d, j) for d in range(1,6) for j in (1,2,3)]:
        if n not in byname: print("missing", n); continue
        t = tr[byname[n]]; p = t.m_LocalPosition; r = t.m_LocalRotation
        print(n, "%.5f %.5f %.5f" % (p.x, p.y, p.z), "%.5f %.5f %.5f %.5f" % (r.x, r.y, r.z, r.w))
