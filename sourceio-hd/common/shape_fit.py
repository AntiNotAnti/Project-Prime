"""Fit connected Source limbs with rigid rotations, retaining original lengths.

Native joint positions are bend hints and animation pivots. Source geometry is
not stretched to the DS proportions. The weapon endpoint remains authoritative.
"""
import math
from mathutils import Matrix,Vector

def fit_chains(cfg,source_bones,meshes,mapping,global_matrix,heads,native_low,frames,report,source_muzzle,native_muzzle):
 scale=global_matrix.to_3x3().col[0].length
 def source_point(native):return source_bones[cfg['fit'][native]['anchor']].head_local.copy()
 def place(native,origin,target,rotation):
  matrix=rotation.to_4x4()@global_matrix
  matrix.translation+=target-matrix@origin
  frames[native]=matrix
  report[native]={'sourceAnchor':cfg['fit'][native]['anchor'],'bakedSourceToNative':[list(r) for r in matrix],'sourceLimbShapePreserved':True,'anchorRequired':False,'anchorError':(target-heads[native]).length,'nativePivotOffset':list(target-heads[native])}
 def solve(root,middle,end,source_end,target,bend_hint,strict=False):
  a=source_point(root);b=source_point(middle);c=source_end
  start=global_matrix@a;v1=global_matrix.to_3x3()@(b-a);v2=global_matrix.to_3x3()@(c-b);l1=v1.length;l2=v2.length
  d=target-start;distance=d.length;axis=d.normalized()
  low=abs(l1-l2)+1e-6;high=l1+l2-1e-6
  root_shift=Vector()
  if strict and distance>high:
   root_shift=axis*(distance-high);start+=root_shift;distance=high
  elif strict and distance<low:
   root_shift=-axis*(low-distance);start+=root_shift;distance=low
  else:
   distance=max(low,min(high,distance));target=start+axis*distance
  along=(l1*l1-l2*l2+distance*distance)/(2*distance);height=math.sqrt(max(0,l1*l1-along*along))
  bend=bend_hint-start;side=bend-axis*bend.dot(axis)
  if side.length<1e-5:
   side=v1-axis*v1.dot(axis)
  if side.length<1e-5:side=axis.orthogonal()
  elbow=start+axis*along+side.normalized()*height
  r1=v1.normalized().rotation_difference((elbow-start).normalized()).to_matrix()
  r2=v2.normalized().rotation_difference((target-elbow).normalized()).to_matrix()
  place(root,a,start,r1);place(middle,b,elbow,r2)
  if end:place(end,c,target,Matrix.Identity(3))
  report[root]['chainRootShift']=list(root_shift)
  report[middle]['chainEndpointError']=(frames[middle]@c-target).length
  return target,r2
 for side in ['L','R']:
  root=side+'_hip';middle=side+'_knee';end=side+'_ankle';foot=source_point(end)
  # Keep the entire Source foot/claw upright; derive ankle height from its
  # actual sole instead of using the much shorter DS foot's ankle height.
  points=[v.co for o in meshes for v in o.data.vertices if mapping[o.vertex_groups[max(v.groups,key=lambda g:g.weight).group].name]==end]
  target=heads[end].copy();target.z=(global_matrix@foot).z+native_low-min((global_matrix@v).z for v in points)
  solve(root,middle,end,foot,target,heads[middle])
  root=side+'_shoulder';middle=side+'_elbow'
  if side=='R':
   solve(root,middle,None,source_muzzle,native_muzzle,heads[middle],strict=True)
  elif 'endSource' in cfg['fit'][middle]:
   end=side+'_wrist' if side+'_wrist' in cfg['fit'] else None
   source_end=source_bones[cfg['fit'][middle]['endSource']].head_local.copy()
   target,rotation=solve(root,middle,None,source_end,heads[cfg['fit'][middle]['endNative']],heads[middle])
   if end:place(end,source_end,target,rotation)
  else:
   a=source_point(root);b=source_point(middle);start=global_matrix@a
   direction=global_matrix.to_3x3()@(b-a);rotation=direction.normalized().rotation_difference((heads[middle]-heads[root]).normalized()).to_matrix()
   place(root,a,start,rotation);place(middle,b,start+rotation@direction,rotation)
  helper=side+'_varias2_SDK'
  if helper in frames:
   # Shoulder armor follows the core silhouette. Its native helper remains
   # a runtime animation joint, without a separate rest-fit translation.
   frames[helper]=global_matrix.copy();origin=global_matrix@source_point(helper)
   report[helper]={'sourceAnchor':cfg['fit'][helper]['anchor'],'bakedSourceToNative':[list(r) for r in frames[helper]],'sourceShapePreserved':True,'nativePivotOffset':list(origin-heads[helper]),'anchorRequired':False,'anchorError':(origin-heads[helper]).length}
 for name,matrix in frames.items():
  if report[name].get('sourceLimbShapePreserved'):
   normalized=matrix.to_3x3()*(1/scale);identity=normalized.transposed()@normalized
   assert max(abs(identity[r][c]-(r==c)) for r in range(3) for c in range(3))<1e-5,'Limb shape was stretched: '+name
