"""Independent full-particle transient Poiseuille physical check, outside timing."""
import argparse
import json
import math
import hashlib
from pathlib import Path
import struct


def check(directory):
    result=json.loads((directory/"result.json").read_text())
    if result["mode"] != "correctness":
        raise ValueError("Full-precision physical gate requires correctness mode")
    native=result["native"]
    t=native["steps"]*native["dt"]
    height,nu=.1,.001
    relaxation=height**2/(math.pi**2*nu)
    body=24*native["re"]/(1000**2*.1**3)
    data=(directory/"state.bin").read_bytes()
    offset=0; error=0.; exact=0.; count=0
    for _ in range(native["count"]):
        x,y,vx,vy,rho,p,m,kind=struct.unpack_from("<7dI",data,offset); offset+=60
        for width in (1,2,2,2,2):
            present=data[offset]; offset+=1+present*width*8
        if kind == 0:
            z=y+height/2
            u=4*body*height**2/(nu*math.pi**3)*math.fsum(
                math.sin(k*math.pi*z/height)/k**3 * (-math.expm1(-nu*(k*math.pi/height)**2*t)) for k in range(1,400,2))
            error+=(vx-u)**2+vy**2; exact+=u*u; count+=1
    relative=math.sqrt(error/exact) if exact else None
    passed=relative is not None and math.isfinite(relative) and relative < .3 and t >= 5*relaxation and count == native["resolution"]**2
    return dict(passed=passed, fullParticleVelocityL2=relative, maximumAllowedL2=.3, simulatedSeconds=t,
                executableSha256=result["executableSha256"],datasetHash=result["datasetHash"],
                validatorSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                requiredRelaxationMultiples=5, actualRelaxationMultiples=t/relaxation, fluidParticles=count,
                note="Declared long Poiseuille acceptance: full fluid vx/vy against transient analytical solution at each actual y; L2<0.3 after five relaxation times. Upstream public tests use 0.3 for a different SPH configuration; this is a separately declared acceptance, not those tests being rerun.")


if __name__ == "__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--directory",type=Path,required=True)
    parser.add_argument("--output",type=Path,required=True)
    args=parser.parse_args()
    result=check(args.directory)
    args.output.write_text(json.dumps(result,indent=2)+"\n")
    print(json.dumps(result))
    raise SystemExit(0 if result["passed"] else 1)
