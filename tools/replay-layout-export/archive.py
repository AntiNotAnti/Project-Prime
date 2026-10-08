import json,re,subprocess,hashlib,sys
from pathlib import Path
root=Path(sys.argv[1]); base=[json.loads((root/(n+'.json')).read_text()) for n in ['56640679','a96ab5b6','eab6f234','current'] if (root/(n+'.json')).exists()]; c=base[-1]
lookup={tuple(f[:2]):f for j in base for fs in j['Objects'].values() for f in fs}; types={t:n for j in base for t,n in j['Types'].items()};commits=subprocess.check_output(['git','log','--format=%h','--','src/MphRead/Mods/Replay/ReplayWorldSchemas.cs'],text=True).split();commits+=['56640679','a96ab5b6','eab6f234']
lookup[('MphRead.Entities.PlayerEntity','_modSpawnProtectionReleaseReports')]=['MphRead.Entities.PlayerEntity','_modSpawnProtectionReleaseReports','System.Byte']
profiles=[]
for commit in dict.fromkeys(commits):
 s=subprocess.check_output(['git','show',commit+':src/MphRead/Mods/Replay/ReplayWorldSchemas.cs'],text=True); names={t:re.findall(r'"([^"]+)"',fs) for t,fs in re.findall(r'\["([^"]+)"\]\s*=\s*\[(.*?)\]',s,re.S)};objs={}
 for t,fs in names.items():
  chain=list(dict.fromkeys(f[0] for f in next(j['Objects'][t] for j in reversed(base) if t in j['Objects'])))
  if not chain: chain=[t]
  objs[t]=[lookup[(p,n)] for p in chain if p in names for n in names[p]]
  if t=='MphRead.Mods.Network.PlayerReplicationBridge' and 'MphRead.Mods.Network.NetInputEdgeSender' not in names:
   for i,f in enumerate(objs[t]):
    if f[1]=='_pressHistory':
     bridge=subprocess.check_output(['git','show',commit+':src/MphRead/Mods/Network/PlayerReplicationBridge.cs'],text=True)
     objs[t][i]=[t,'_pressHistory','MphRead.Mods.Network.PressHistoryBuffer' if 'private PressHistoryBuffer' in bridge else 'System.UInt32[]']
 if 'ModContinuousNetworkTarget' not in names.get('MphRead.Entities.PlayerEntity',[]):
  for t,n in [('MphRead.Entities.PlayerEntity','_modPendingHomingTarget'),('MphRead.Mods.Network.PlayerReplicationBridge','_latchedHomingTarget')]:
   objs[t]=[[f[0],f[1],'System.Byte'] if f[1]==n else f for f in objs[t]]
 values={t:v for j in base for t,v in j['Values'].items()}
 for t,path,marker in [('MphRead.Mods.Network.ContinuousWeaponPhase+Clock','ContinuousWeaponPhase.cs','public uint SourceTick'),('MphRead.Mods.Network.FormReconciliation','FormReconciliation.cs','public uint EpisodeId'),('MphRead.Mods.Network.ShotKey','NetShotDiagnostics.cs','uint ShotId')]:
  src=subprocess.check_output(['git','show',commit+':src/MphRead/Mods/Network/'+path],text=True)
  if marker not in src:values[t]=base[0]['Values'][t]
 alltypes=set()
 def add(t):
  if t in alltypes:return
  alltypes.add(t)
  if t.endswith(']'):
   if re.search(r'\[[,]*\]$',t):add(re.sub(r'\[[,]*\]$','',t))
   elif '`' in t:
    args=t[t.index('[')+1:-1];level=0;start=0
    for i,x in enumerate(args+','):
     if x=='[':level+=1
     if x==']':level-=1
     if x==',' and level==0:add(args[start:i]);start=i+1
   else:add(t[:t.rindex('[')])
  if t in values:
   for f in values[t]:add(f[2])
 for t,fs in objs.items():
  add(t)
  for f in fs:add(f[2])
 for t in ['MphRead.ModelInstance','MphRead.WeaponInfo','System.Int32','System.UInt32','System.Single','System.Boolean','System.String','OpenTK.Mathematics.Vector3','MphRead.Entities.EntityBase']:add(t)
 vals={t:v for t,v in values.items() if t in alltypes}
 N=lambda t:types[t]; schema='world-codec-1|'+'|'.join(t+':'+','.join(f[0]+'.'+f[1]+':'+f[2] for f in fs) for t,fs in sorted(objs.items(),key=lambda p:N(p[0])))+'|'.join(t+':'+','.join(f[1]+':'+f[2] for f in fs) for t,fs in sorted(vals.items(),key=lambda p:N(p[0])))
 legacy='world-codec-1|'+'|'.join(N(t)+':'+','.join(N(f[0])+'.'+f[1]+':'+f[2] for f in fs) for t,fs in sorted(objs.items(),key=lambda p:N(p[0])))+'|'.join(N(t)+':'+','.join(f[1]+':'+f[2] for f in fs) for t,fs in sorted(vals.items(),key=lambda p:N(p[0])))
 codec=subprocess.check_output(['git','show',commit+':src/MphRead/Mods/Replay/ReplayWorldCheckpoint.cs'],text=True)
 profile={'Commit':commit,'BuildBound':'ReplayStateHash.BuildId + "|"' in codec,'Contract':hashlib.sha256(schema.encode()).hexdigest().upper(),'LegacySchema':legacy,'Objects':objs,'Values':vals,'Types':{t:N(t) for t in alltypes},'ObjectTypes':sorted(t for t in alltypes if t in objs or t.endswith(']') and (t.endswith('[]') or t.endswith('[,]') or t.startswith(('System.Collections.Generic.List`','System.Collections.Generic.Queue`','System.Collections.Generic.LinkedList`'))) or t in ['MphRead.ModelInstance','MphRead.WeaponInfo'])}
 profiles.append(profile)

# Deduplicate positional field descriptions while retaining each historical ID table.
unique={(j['Contract'],j['BuildBound']):j for j in profiles}
fields=[];field_ids={};sets=[];set_ids={};layouts=[];type_names={}
def intern(fs):
 refs=[]
 for f in fs:
  key=tuple(f)
  if key not in field_ids:field_ids[key]=len(fields);fields.append(f)
  refs.append(field_ids[key])
 key=tuple(refs)
 if key not in set_ids:set_ids[key]=len(sets);sets.append(refs)
 return set_ids[key]
for j in sorted(unique.values(),key=lambda x:x['Commit']):
 type_names.update(sorted(j['Types'].items()))
 layouts.append({'Commit':j['Commit'],'Contract':j['Contract'],'BuildBound':j['BuildBound'],
  'Objects':{t:intern(fs) for t,fs in j['Objects'].items()},'Values':{t:intern(fs) for t,fs in j['Values'].items()},
  'ObjectTypes':j['ObjectTypes'],'Types':sorted(j['Types'])})
catalog={'Types':type_names,'FieldSets':sets,'Layouts':layouts,'Fields':fields}
s=json.dumps(catalog,indent=2)
s=re.sub(r'\[\s*((?:"[^"\n]*",?\s*){3})\]',lambda m:'['+', '.join(re.findall(r'"[^"\n]*"',m.group(1)))+']',s)
s=re.sub(r'\[\s*((?:\d+,?\s*)+)\]',lambda m:'['+', '.join(re.findall(r'\d+',m.group(1)))+']',s)
Path(sys.argv[2]).write_text(s+'\n')
print('Archived',len(layouts),'world layouts.')
