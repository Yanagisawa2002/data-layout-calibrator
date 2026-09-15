import SimplePH
import numpy as np
import math
import os
import pathlib
import sys
import argparse

parser = argparse.ArgumentParser(description="SimplePH couette flow")

parser.add_argument("-np", "--num-threads", type=int, default=1, help="Number of OpenMP threads")
parser.add_argument('-Re', type=float, default=0.1, help='Reynolds number')
parser.add_argument("-res", "--resolution", type=int, default=40, help="Resolution")
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
    for i in range(res):
        for j in range(bcresy):
            p = SimplePH.Particle()

            # set position
            p.x = [
                -Lx/2 + dx/2 + i * dx,
                -bcLy/2 + dx/2 + j * dx
            ]

            # init velocity
            p.v = [0.0, 0.0]

            # mass and density
            p.m = m
            p.rho = rho

            # set type
            p.type = 0
            if p.x[1] > Ly/2 or p.x[1] < -Ly/2:
                p.type = 1
            # set wall velocity of upper wall
            if p.x[1] > Ly/2:
                p.v = [vwall, 0.0]

            parts.append(p)
    return parts


# geometry
Lx = 0.1
Ly = 0.1

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
bcLy = bcresy * dx

if args.verbose:
    print("bcpartcount:", bcpartcount)
    print("bcresy:", bcresy)
    print("bcLy:", bcLy)

# material params
rho = 1000.0
V = dx * dx
m = rho * V
mu = 1.0

if args.verbose:
    print("m:", m)
    print("V:", V)

# body force
b = [0.0, 0.0]

# Calculate upper wall velocity for given Re
lref = Ly
Re = args.Re
vwall = (2.0 * mu * Re) / (rho * lref)
if args.verbose:
    print("vwall:", vwall)

# reference values
vmax = vwall
vref = vmax
Lref = Ly

if args.verbose:
    print("vref:", vref)

# characteristic velocity and length (mean velocity and channel height)
vchar = vmax / 2.0
Lchar = Ly
# Reynolds number
Re = (rho * vchar * Lchar) / mu
if args.verbose:
    print("Re:", Re)

# density fluctuation
rho_fluct = 0.01

# solver setup
solver = SimplePH.Solver(
    h=h,
    Lx=Lx,
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
solver.set_acceleration(b,0)

# compute soundspeed and timestep
solver.compute_soundspeed_and_timestep()

# equation of state
solver.set_eos(SimplePH.EOSType.Tait)

# use artificial viscosity
# solver.activate_artificial_viscosity(0.01)

# use tensile instability correction
# solver.activate_tensile_instability_correction()

# # use xsph filter
# solver.activate_xsph_filter(0.1)

# # use transport velocity formulation
# solver.activate_transport_velocity()

# set particles
solver.set_particles(create_particles())

# integrator
solver.set_integrator(SimplePH.VerletIntegrator())
# solver.set_integrator(SimplePH.EulerIntegrator())
# solver.set_integrator(SimplePH.TransportVelocityVerletIntegrator())

# density method
# solver.set_density_method(SimplePH.DensityMethod.Continuity)
solver.set_density_method(SimplePH.DensityMethod.Summation)

# set output name
output_name = f"couette_flow_re{Re}_res{res}"
solver.set_output_name(output_name)

# run simulation
solver.run(steps, vtk_freq=100, log_freq=50)
