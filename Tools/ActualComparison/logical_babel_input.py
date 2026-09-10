"""Hash the fixed logical Babel initial arrays without allocating/running a workload.

Reconstructed input identity from the frozen default constants, NOT a Player readback
or a new timing observation. No data is deleted or generated as a substitute workload.
"""
import hashlib
import json
from pathlib import Path
import struct
import sys

if __name__=='__main__':
    destination=Path(sys.argv[1])
    if destination.exists(): raise RuntimeError('Output already exists')
    digest=hashlib.sha256()
    for value in (.1,.2,0.):
        chunk=struct.pack('<d',value)*65536
        for _ in range(33554432//65536): digest.update(chunk)
    result=dict(kind='reconstructed logical input identity, not a runtime readback',
                source='BabelStream 17ab377b0e919e14fd3df2b67268761fdac8abb3 defaults',
                count=33554432,type='IEEE754 binary64 little endian',order='complete a then b then c',
                values=[.1,.2,0.],bytes=33554432*8*3,sha256=digest.hexdigest())
    destination.write_text(json.dumps(result,indent=2))
    print(json.dumps(result))
