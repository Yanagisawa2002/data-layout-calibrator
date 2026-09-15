#!/usr/bin/env python3
"""
Physical Flow Tests for SimplePH

Includes:
- Poiseuille flow: convergence study with multiple resolutions
- Couette flow: convergence study with multiple resolutions

Error metric:
- Relative L2 norm of the velocity field
  L2 = sqrt( sum |u_num - u_ana|^2 / sum |u_ana|^2 )
- Computed on fluid particles only
- Same norm used for all physical flow tests

VTU output is written into subfolders.
These are long-running physics validation tests.
"""


import os
import sys
import shutil
import pathlib
import math
import numpy as np
import pytest

# Add python/ to path to import SimplePH
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '../python'))
import SimplePH

# Global parameters
# Geometry and material
Lx, Ly = 0.1, 0.1
rho0 = 1000.0
mu = 1.0
rho_fluct = 0.01

# Numerics
kernel = SimplePH.KernelType.WendlandC2
h_factor = 1.3
kappa = 2.0

eos = SimplePH.EOSType.Tait
density_method = SimplePH.DensityMethod.Continuity
integrator = SimplePH.VerletIntegrator()

poiseuille_resolutions = [10, 20, 40]
poiseuille_steps_list = [3000, 5000, 8000]

couette_resolutions = [10, 20, 40]
couette_steps_list = [3000, 5000, 8000]

# body force for Poiseuille flow
bx = 0.024
# wall velocity for Couette flow
vwall = 0.02

poiseuille_errors = {}
couette_errors = {}
poiseuille_profiles = {}
couette_profiles = {}

# Helper functions
def compute_bc_geometry(dx):
    h = h_factor * dx
    rcut = kappa * h
    bcpartcount = math.ceil(rcut / dx)
    fluid_res_y = int(Ly / dx)
    bcresy = 2 * bcpartcount + fluid_res_y
    LyBC = bcresy * dx
    return bcpartcount, bcresy, LyBC, h

def compute_relative_L2_error(particles, analytical_solution):
    """
    Computes the relative L2 error norm of the velocity field.

    L2 = sqrt( sum_i |u_num - u_ana|^2 / sum_i |u_ana|^2 )

    - Fluid particles only (type == 0)
    - Includes both velocity components (vx, vy)
    - Analytical solution is provided as a function u(y)
    """
    fluid = [p for p in particles if p.type == 0]
    err_num = 0.0
    err_den = 0.0
    for p in fluid:
        y = p.x[1] + Ly / 2.0
        u_ana = analytical_solution(y)
        err_num += (p.v[0] - u_ana)**2 + (p.v[1])**2
        err_den += u_ana**2
    if err_den == 0.0:
        return 0.0
    return math.sqrt(err_num / err_den)

def poiseuille_analytical(y):
    """
    Analytical solution for plane Poiseuille flow.
    y in [0, Ly]
    """
    return (rho0 * bx) / (2.0 * mu) * y * (Ly - y)

def couette_analytical(y):
    """
    Analytical solution for plane Couette flow.
    y in [0, Ly]
    """
    return vwall * y / Ly

# Test class
class TestPhysicalFlows:
    
    @pytest.mark.slow
    @pytest.mark.parametrize("res,steps", zip(poiseuille_resolutions, poiseuille_steps_list))
    def test_poiseuille_flow(self, res, steps):
        dx = Ly / res
        bcpartcount, bcresy, LyBC, h = compute_bc_geometry(dx)

        # Solver setup
        vmax = (rho0 * bx * (Ly / 2.0)**2) / (2.0 * mu)
        solver = SimplePH.Solver(h=h, Lx=Lx, Ly=LyBC, dx0=dx, Lref=Ly, vref=vmax, kernel_type=kernel)
        solver.set_viscosity(mu)
        solver.set_density(rho0, rho_fluct)
        solver.set_acceleration([bx, 0.0])
        solver.compute_soundspeed_and_timestep()
        solver.set_eos(eos)
        solver.set_density_method(density_method)
        solver.set_integrator(integrator)

        # Particles
        nx = int(Lx / dx)
        y0 = -Ly/2 - bcpartcount*dx
        x0 = -Lx/2
        particles = []
        for i in range(nx):
            for j in range(bcresy):
                p = SimplePH.Particle()
                p.x = [x0 + dx/2 + i*dx, y0 + dx/2 + j*dx]
                p.v = [0.0, 0.0]
                p.m = rho0 * dx * dx
                p.rho = rho0
                p.p = 0.0
                p.type = 0 if -Ly/2 <= p.x[1] <= Ly/2 else 1
                particles.append(p)
        solver.set_particles(particles)

        solver.set_output_name(f"poiseuille_{res}")

        solver.run(steps=steps, vtk_freq=100, log_freq=100)

        L2_error = compute_relative_L2_error(solver.get_particles(), poiseuille_analytical)
        print(f"[Poiseuille] res={res}, L2 error={L2_error:.4e}")

        particles = solver.get_particles()
        fluid = [p for p in particles if p.type == 0]

        y = np.array([p.x[1] + Ly/2 for p in fluid])
        u = np.array([p.v[0] for p in fluid])

        poiseuille_profiles[res] = (y, u)
        poiseuille_errors[res] = L2_error

        assert L2_error < 0.3

    @pytest.mark.slow
    @pytest.mark.parametrize("res,steps", zip(couette_resolutions, couette_steps_list))
    def test_couette_flow(self, res, steps):
        dx = Ly / res
        bcpartcount, bcresy, LyBC, h = compute_bc_geometry(dx)

        # Solver setup
        solver = SimplePH.Solver(h=h, Lx=Lx, Ly=LyBC, dx0=dx, Lref=Ly, vref=vwall, kernel_type=kernel)
        solver.set_viscosity(mu)
        solver.set_density(rho0, rho_fluct)
        solver.set_acceleration([0.0, 0.0])
        solver.compute_soundspeed_and_timestep()
        solver.set_eos(eos)
        solver.set_density_method(density_method)
        solver.set_integrator(integrator)

        # Particles
        nx = int(Lx / dx)
        y0 = -Ly/2 - bcpartcount*dx
        x0 = -Lx/2
        particles = []
        for i in range(nx):
            for j in range(bcresy):
                p = SimplePH.Particle()
                p.x = [x0 + dx/2 + i*dx, y0 + dx/2 + j*dx]
                p.v = [0.0, 0.0]
                p.m = rho0 * dx * dx
                p.rho = rho0
                p.p = 0.0
                if -Ly/2 <= p.x[1] <= Ly/2:
                    p.type = 0
                else:
                    p.type = 1
                    if p.x[1] > Ly/2:
                        p.v = [vwall, 0.0]
                particles.append(p)
        solver.set_particles(particles)

        solver.set_output_name(f"couette_{res}")

        solver.run(steps=steps, vtk_freq=100, log_freq=0)

        L2_error = compute_relative_L2_error(solver.get_particles(), couette_analytical)
        print(f"[Couette] res={res}, L2 error={L2_error:.4e}")

        particles = solver.get_particles()
        fluid = [p for p in particles if p.type == 0]

        y = np.array([p.x[1] + Ly/2 for p in fluid])
        u = np.array([p.v[0] for p in fluid])

        couette_profiles[res] = (y, u)
        couette_errors[res] = L2_error

        assert L2_error < 0.2


# Plot Results Test
@pytest.mark.plot
def test_plot_results():
    """Generates and saves convergence plots after flow tests."""
    import matplotlib.pyplot as plt

    if not poiseuille_profiles and not couette_profiles:
        pytest.skip("No data available to plot yet.")

    fig, axs = plt.subplots(2, 2, figsize=(12, 8))
    colors = {10: "tab:blue", 20: "tab:orange", 40: "tab:green"}
    y_ana = np.linspace(0.0, Ly, 200)

    # Poiseuille profile
    ax = axs[0, 0]
    for res, (y, u) in poiseuille_profiles.items():
        ax.scatter(y, u, s=8, color=colors[res], label=f"res={res}")
    ax.plot(y_ana, poiseuille_analytical(y_ana), "k-", label="analytical")
    ax.set_title("Poiseuille velocity profile")
    ax.set_xlabel("y")
    ax.set_ylabel("u")
    ax.legend()

    # Couette profile
    ax = axs[0, 1]
    for res, (y, u) in couette_profiles.items():
        ax.scatter(y, u, s=8, color=colors[res], label=f"res={res}")
    ax.plot(y_ana, couette_analytical(y_ana), "k-", label="analytical")
    ax.set_title("Couette velocity profile")
    ax.set_xlabel("y")
    ax.set_ylabel("u")
    ax.legend()

    # Poiseuille error
    ax = axs[1, 0]
    res_vals = np.array(sorted(poiseuille_errors.keys()))
    err_vals = np.array([poiseuille_errors[r] for r in res_vals])
    ax.loglog(res_vals, err_vals, "o-")
    ax.set_title("Poiseuille L2 error")
    ax.set_xlabel("Resolution")
    ax.set_ylabel("L2 error")

    # Couette error
    ax = axs[1, 1]
    res_vals = np.array(sorted(couette_errors.keys()))
    err_vals = np.array([couette_errors[r] for r in res_vals])
    ax.loglog(res_vals, err_vals, "o-")
    ax.set_title("Couette L2 error")
    ax.set_xlabel("Resolution")
    ax.set_ylabel("L2 error")

    plt.tight_layout()
    plt.savefig("convergence_plots.png", dpi=200)
    plt.close(fig)
    print("Convergence plots saved as 'convergence_plots.png'.")

# Direct execution support
if __name__ == "__main__":
    print("Running SimplePH Physical Flow tests...\n")

    total_tests = 0
    passed_tests = 0
    failed_tests = []

    # Define an instance of the test class
    test_instance = TestPhysicalFlows()

    # Parametrized Poiseuille tests
    for res, steps in zip(poiseuille_resolutions, poiseuille_steps_list):
        total_tests += 1
        try:
            test_instance.test_poiseuille_flow(res, steps)
            print(f"test_poiseuille_flow[res={res}] PASSED")
            passed_tests += 1
        except Exception as e:
            failed_tests.append((f"test_poiseuille_flow[res={res}]", e))
            print(f"test_poiseuille_flow[res={res}] FAILED: {e}")

    # Parametrized Couette tests
    for res, steps in zip(couette_resolutions, couette_steps_list):
        total_tests += 1
        try:
            test_instance.test_couette_flow(res, steps)
            print(f"test_couette_flow[res={res}] PASSED")
            passed_tests += 1
        except Exception as e:
            failed_tests.append((f"test_couette_flow[res={res}]", e))
            print(f"test_couette_flow[res={res}] FAILED: {e}")

    # Summary
    print(f"\n{passed_tests}/{total_tests} tests passed")
    if failed_tests:
        print(f"{len(failed_tests)} test(s) failed.")
        for name, e in failed_tests:
            print(f"- {name}: {e}")

    # Create plots if any profiles were computed
    if poiseuille_profiles or couette_profiles:
        test_plot_results()
        print("Plots saved to convergence_plots.png")

    # Exit-Code
    sys.exit(0 if not failed_tests else 1)
