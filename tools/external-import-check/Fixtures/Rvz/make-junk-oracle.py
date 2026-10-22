# Independent synthetic RVZ junk oracle: ring recurrence, not donor buffer passes.
from pathlib import Path
import hashlib,json,struct
root=Path(__file__).resolve().parent
seed=bytes(range(68));state=list(struct.unpack('>17I',seed))
for i in range(17,521):state.append(((state[i-17]<<23)^(state[i-16]>>9)^state[i-1])&0xffffffff)
index=0
def advance_words(count):
 global index
 for _ in range(count):
  state[index]^=state[(index+489)%521]
  index=(index+1)%521
advance_words(4*521)
result=bytearray()
while len(result)<131072:
 for i in range(521):
  word=state[(index+i)%521]
  result.extend((word>>shift)&255 for shift in (24,18,8,0))
 advance_words(521)
result=result[:131072]
(root/'junk-oracle.bin').write_bytes(result)
(root/'junk-oracle.json').write_text(json.dumps({'scope':'synthetic own seed; independent Python ring recurrence from recorded donor format description, no original-media data','seedHex':seed.hex(),'bytes':len(result),'sha256':hashlib.sha256(result).hexdigest(),'first64Hex':result[:64].hex(),'boundary32ByteHex':{str(at):result[at:at+32].hex() for at in [2076,4160,32760,65528]}},indent=2)+'\n')
print(hashlib.sha256(result).hexdigest())
