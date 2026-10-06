#!/usr/bin/env python3
import hashlib
import json
from pathlib import Path
import sys
metadata = json.loads(Path(sys.argv[1]).read_text())
library = Path(sys.argv[2])
metadata.update(target=sys.argv[3], features=sys.argv[4], library=library.name,
                sha256=hashlib.sha256(library.read_bytes()).hexdigest(),
                fault_injection='prime-fault-injection' in sys.argv[4].split(','))
(library.parent/'PRIME-WGPU.json').write_text(json.dumps(metadata, indent=2)+'\n')
