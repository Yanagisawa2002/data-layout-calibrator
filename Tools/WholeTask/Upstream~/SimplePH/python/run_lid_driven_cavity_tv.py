import SimplePH
import numpy as np
import math
import os
import pathlib
import argparse

parser = argparse.ArgumentParser(description="SimplePH LDC TV")

parser.add_argument("-np", "--num-threads", type=int, default=1, help="Number of OpenMP threads")
parser.add_argument("-res", "--resolution", type=int, default=50,help="Resolution")
parser.add_argument("-re", "--reynolds-number", type=float, default=1000.0, help="Reynolds number")
parser.add_argument("-steps", "--steps", type=int, default=20001, help="Number of steps")
parser.add_argument("-v", "--verbose", action="store_true")

args = parser.parse_args()

num_threads = args.num_threads
steps = args.steps
SimplePH.set_omp_threads(num_threads)
if args.verbose:
    print(f"Setting {num_threads} OpenMP threads")

# create particle layout
def create_particles():
    parts = []
    for i in range(bcresx):
        for j in range(bcresy):
            p = SimplePH.Particle()

            # set position
            p.x = [
                -bcLx/2 + dx/2 + i * dx,
                -bcLy/2 + dx/2 + j * dx
            ]

            # init velocity
            p.v = [0.0, 0.0]

            # mass and density
            p.m = m
            p.rho = rho

            # set type
            p.type = 0
            if  p.x[0] > Lx/2 or p.x[0] < -Lx/2 or p.x[1] > Ly/2 or p.x[1] < -Ly/2:
                p.type = 1
            # set wall velocity of upper wall
            if p.x[1] > Ly/2:
                p.v = [1.0, 0.0]

            parts.append(p)
    return parts


# geometry
Lx = 1.0
Ly = 1.0

# resolution
res = args.resolution
dx = Lx / res

if args.verbose:
    print("res:", res)
    print("dx:", dx)

# kernel params
kappa = 2.0
h = 1.7 * dx
rcut = kappa * h

if args.verbose:
    print("h:", h)
    print("rcut:", rcut)

# boundary layer size
bcpartcount = math.ceil(rcut / dx)
bcresy = 2 * bcpartcount + res
bcresx = bcresy
bcLy = bcresy * dx
bcLx= bcLy

if args.verbose:
    print("res:", res)
    print("bcresx:", bcresx)
    print("bcresy:", bcresy)
    print("bcpartcount:", bcpartcount)
    print("bcresy:", bcresy)
    print("bcLy:", bcLy)

# material params
rho = 1.0
V = dx * dx
m = rho * V

# Reynolds number
# Re = (rho * vref * Lref) / mu
Re = args.reynolds_number

if args.verbose:
    print("Re:", Re)
    print("m:", m)
    print("V:", V)

# body force
b = [0.0, 0.0]

# reference values
vmax = 1.0
# vref = (2/3) * vmax
# Lref = Lx / 2
vref = vmax
Lref = Ly
if args.verbose:
    print("vref:", vref)

# viscosity
mu = (rho * vref * Lref) / Re
if args.verbose:   
    print("mu:", mu)

# density fluctuation
rho_fluct = 0.05

# solver setup
solver = SimplePH.Solver(
    h=h,
    Lx=bcLx,
    Ly=bcLy,
    dx0=dx,
    Lref=Lref,
    vref=vref,
    kernel_type=SimplePH.KernelType.WendlandC4
)

# set viscosity
solver.set_viscosity(mu)

# set density params
solver.set_density(rho, rho_fluct)

# set body force
solver.set_acceleration(b)

# compute soundspeed and timestep
solver.compute_soundspeed_and_timestep()

# equation of state and back pressure factor (bp_fac*rho0*c^2)
solver.set_eos(SimplePH.EOSType.Linear, 0.0, 1.0)

# use artificial viscosity
# solver.activate_artificial_viscosity(0.01)

# use artificial viscosity
# solver.activate_tensile_instability_correction()

# # use xsph filter
# solver.activate_xsph_filter(0.1)

# use transport velocity formulation
solver.activate_transport_velocity()

# set particles
solver.set_particles(create_particles())

# integrator
# solver.set_integrator(SimplePH.VerletIntegrator())
solver.set_integrator(SimplePH.TransportVelocityVerletIntegrator())
# solver.set_integrator(SimplePH.EulerIntegrator())

# density method
# solver.set_density_method(SimplePH.DensityMethod.Continuity)
solver.set_density_method(SimplePH.DensityMethod.Summation)

# set output name
output_name = f"lid_driven_cavity_tv_re{Re}_res{res}"
solver.set_output_name(output_name)

# run simulation
solver.run(steps, vtk_freq=500, log_freq=100)