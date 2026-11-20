import numpy as np, math
def norm(v): return v/np.linalg.norm(v)
def cross(a,b): return np.cross(a,b)  # same formula as Unity Vector3.Cross
def look(f,u):
    z=norm(f); x=norm(cross(u,z)); y=cross(z,x); return np.column_stack([x,y,z])
def axis_angle(axis, deg):
    # Unity AngleAxis in a left-handed frame == right-handed rotation formula applied to the same components with angle negated? check against known: AngleAxis(90, up)*forward = right in Unity
    a=norm(axis); t=math.radians(deg); K=np.array([[0,-a[2],a[1]],[a[2],0,-a[0]],[-a[1],a[0],0]])
    return np.eye(3)+math.sin(t)*K+(1-math.cos(t))*K@K
up=np.array([0,1.,0]); fwd=np.array([0,0,1.])
assert np.allclose(axis_angle(up,90)@fwd,[1,0,0]), axis_angle(up,90)@fwd   # Unity: +90 about up turns forward to right
FACE=np.array([0,1.,0]); UPL=np.array([-1.,0,0])
RIGINV=np.linalg.inv(look(FACE,UPL))
def headrot(face,u): return look(face,u)@RIGINV
def signed(a,b,n):
    a2=a-n*np.dot(a,n); b2=b-n*np.dot(b,n)
    ang=math.degrees(math.atan2(np.linalg.norm(np.cross(a2,b2)), np.dot(a2,b2)))
    # Unity SignedAngle sign: sign(dot(n, cross(a,b)))
    return ang*(1 if np.dot(n,np.cross(a2,b2))>=0 else -1)
def angles(R, bodyF):
    f=R@FACE; u=R@UPL; flat=f.copy(); flat[1]=0
    yaw=signed(bodyF,flat,up); pitch=-math.degrees(math.asin(np.clip(norm(f)[1],-1,1)))
    upright=up-f*np.dot(up,f); crown=u-f*np.dot(u,f)
    roll=-signed(upright,crown,f)
    return yaw,pitch,roll
# rest pose check
R=headrot(fwd,up); print("rest: local X ->",np.round(R@[1,0,0],3)," Y ->",np.round(R@[0,1,0],3)," Z ->",np.round(R@[0,0,1],3))
bodyF=norm(np.array([0.3,0,1.]))
worst=0
for yaw in range(-70,71,10):
    for pitch in range(-20,41,10):
        face=axis_angle(up,yaw)@bodyF; side=cross(up,face); face=axis_angle(side,pitch)@face
        y,p,r=angles(headrot(face,up),bodyF)
        worst=max(worst,abs(y-yaw),abs(p-pitch),abs(r))
print("max error over yaw -70..70, pitch -20..40 (deg):", round(worst,6))
# roll sign: tilt crown toward his right by 10 deg
face=bodyF; R=headrot(face,up); tilt=axis_angle(face,-10)@R  # rotate about face axis
print("10 deg tilt reads roll", round(angles(tilt,bodyF)[2],2))
print("+30 yaw reads", [round(v,2) for v in angles(headrot(axis_angle(up,30)@bodyF,up),bodyF)], "(+ = his right)")

print("\n--- old 0.13-0.17 ApplyHead, axis measured while the game's free-look had the head turned ---")
def old_apply(anim, hfl, bodyF, tyaw, tpitch):
    hf=anim@hfl; flat=hf.copy(); flat[1]=0
    cy=signed(bodyF,flat,up); cp=-math.degrees(math.asin(np.clip(norm(hf)[1],-1,1)))
    yaw=tyaw-cy; pitch=tpitch-cp
    right=cross(up,norm(flat))
    q=axis_angle(up,yaw)@axis_angle(right,pitch)
    return q@anim
bodyF=fwd
neutral=headrot(bodyF,up)           # animated head looking straight, upright
for fl in (15,25,40):
    measured_anim=axis_angle(up,fl)@neutral          # free-look turned the head fl deg on the measuring frame
    hfl=np.linalg.inv(measured_anim)@bodyF           # HeadFwdLocal = Inverse(head.rotation) * body forward
    for tp in (0,10):
        res=old_apply(neutral, hfl, bodyF, 0, tp)    # later: free-look back to 0, captor straight ahead
        y,p,r=angles(res,bodyF)
        print(f"free-look {fl:>2} deg when measured, captor ahead, target pitch {tp:>2}: face yaw {y:+.1f}, pitch {p:+.1f}, roll {r:+.1f}")
