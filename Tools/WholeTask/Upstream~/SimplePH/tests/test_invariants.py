#!/usr/bin/env python3
"""
Comprehensive SPH invariants tests for SimplePH with periodic boundaries.

Checks:
- Mass conservation
- Density fluctuations (< rho_fluct)
- Pairwise kernel symmetry (forces)
- Velocity symmetry in homogeneous system
- Particle positions respect periodic boundaries

Supports both pytest and direct python execution.
"""

import sys
import os
import pytest

# Add python/ to path to import SimplePH
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '../python'))
import SimplePH

# Simulation parameters
Lx, Ly = 0.1, 0.1
nx, ny = 20, 20
dx = Lx / nx
rho0 = 1000.0
rho_fluct = 0.01
mu = 1.0
h = 1.5 * dx

# Helpers
def create_solver(kernel, density_method):
    solver = SimplePH.Solver(
        h=h,
        Lx=Lx,
        Ly=Ly,
        dx0=dx,
        Lref=Ly,
        vref=0.01,
        kernel_type=kernel
    )
    solver.set_viscosity(mu)
    solver.set_density(rho0, rho_fluct)
    solver.set_acceleration([0.0, 0.0])
    solver.compute_soundspeed_and_timestep()
    solver.set_eos(SimplePH.EOSType.Tait)
    solver.set_density_method(density_method)
    solver.set_integrator(SimplePH.VerletIntegrator())
    return solver

def create_particle_grid(nx=nx, ny=ny, dx=dx):
    particles = []
    for i in range(nx):
        for j in range(ny):
            p = SimplePH.Particle()
            p.x = [i*dx + dx/2, j*dx + dx/2]
            p.v = [0.0, 0.0]
            p.m = rho0 * dx * dx
            p.rho = rho0
            p.p = 0.0
            p.type = 0  # fluid
            particles.append(p)
    return particles

# Kernels and density methods
kernels = [
    SimplePH.KernelType.CubicSpline,
    SimplePH.KernelType.QuinticSpline,
    SimplePH.KernelType.WendlandC2,
    SimplePH.KernelType.WendlandC4
]

density_methods = [
    SimplePH.DensityMethod.Summation,
    SimplePH.DensityMethod.Continuity
]

class TestInvariants:
    # Parametrized physics invariants test
    @pytest.mark.parametrize("density_method", density_methods)
    @pytest.mark.parametrize("kernel", kernels)
    def test_physics_invariants(self, density_method, kernel):
        solver = create_solver(kernel, density_method)
        particles = create_particle_grid()
        solver.set_particles(particles)

        # Mass conservation
        m_initial = sum(p.m for p in solver.get_particles())
        solver.run(steps=2, vtk_freq=0, log_freq=0)
        m_final = sum(p.m for p in solver.get_particles())
        assert abs(m_final - m_initial) < 1e-12, f"Mass changed: {m_final - m_initial}"

        # Density fluctuation
        for p in solver.get_particles():
            rel_fluct = abs(p.rho - rho0)/rho0
            assert rel_fluct < rho_fluct, f"Density fluctuation too high: {rel_fluct}"

        # Velocity symmetry
        vx_total = sum(p.v[0] for p in solver.get_particles())
        vy_total = sum(p.v[1] for p in solver.get_particles())
        assert abs(vx_total) < 1e-12, f"Total vx != 0: {vx_total}"
        assert abs(vy_total) < 1e-12, f"Total vy != 0: {vy_total}"

        # Periodic boundaries
        for p in solver.get_particles():
            x_mod = p.x[0] % Lx
            y_mod = p.x[1] % Ly
            assert 0.0 <= x_mod <= Lx, f"Particle x out of bounds: {p.x[0]}"
            assert 0.0 <= y_mod <= Ly, f"Particle y out of bounds: {p.x[1]}"

    # Test: Velocity symmetry / pairwise forces (parametrized)
    @pytest.mark.parametrize("density_method", density_methods)
    @pytest.mark.parametrize("kernel", kernels)
    def test_pairwise_force_symmetry_param(self, density_method, kernel):
        solver = create_solver(kernel, density_method)
        particles = create_particle_grid(4, 4, dx)  # only use a small grid for clarity
        solver.set_particles(particles)
        solver.run(steps=1, vtk_freq=0, log_freq=0)

        vx_total = sum(p.v[0] for p in solver.get_particles())
        vy_total = sum(p.v[1] for p in solver.get_particles())
        assert abs(vx_total) < 1e-12, f"Total vx != 0: {vx_total}"
        assert abs(vy_total) < 1e-12, f"Total vy != 0: {vy_total}"

    # Test: Kernel derivative symmetry (parametrized)
    @pytest.mark.parametrize("density_method", density_methods)
    @pytest.mark.parametrize("kernel", kernels)
    def test_kernel_symmetry_param(self,density_method, kernel):
        solver = create_solver(kernel, density_method)
        p1 = SimplePH.Particle(); p1.x=[0.025, 0.05]; p1.v=[0,0]; p1.m=rho0*dx*dx; p1.rho=rho0; p1.type=0
        p2 = SimplePH.Particle(); p2.x=[0.035, 0.05]; p2.v=[0,0]; p2.m=rho0*dx*dx; p2.rho=rho0; p2.type=0
        solver.set_particles([p1, p2])
        solver.run(steps=1, vtk_freq=0, log_freq=0)

        dvx = p1.v[0] + p2.v[0]
        dvy = p1.v[1] + p2.v[1]
        assert abs(dvx) < 1e-12, f"Pairwise dvx not antisymmetric: {dvx}"
        assert abs(dvy) < 1e-12, f"Pairwise dvy not antisymmetric: {dvy}"

# Direct execution support
if __name__ == "__main__":
    print("Running SimplePH SPH invariants tests...\n")

    total_tests = 0
    passed_tests = 0
    failed_tests = []

    # Define an instance of the test class
    test_instance = TestInvariants()

    # Parametrized tests
    for density_method in density_methods:
        for kernel in kernels:
            total_tests += 1
            try:
                test_instance.test_physics_invariants(density_method, kernel)
                print(f"test_physics_invariants[{density_method}-{kernel}] PASSED")
                passed_tests += 1
            except Exception as e:
                failed_tests.append((f"test_physics_invariants[{density_method}-{kernel}]", e))
                print(f"test_physics_invariants[{density_method}-{kernel}] FAILED: {e}")

    # Additional parametrized tests
    for density_method in density_methods:
        for kernel in kernels:
            total_tests += 1
            try:
                test_instance.test_pairwise_force_symmetry_param(density_method, kernel)
                print(f"test_pairwise_force_symmetry_param[{density_method}-{kernel}] PASSED")
                passed_tests += 1
            except Exception as e:
                failed_tests.append((f"test_pairwise_force_symmetry_param[{density_method}-{kernel}]", e))
                print(f"test_pairwise_force_symmetry_param[{density_method}-{kernel}] FAILED: {e}")

            total_tests += 1
            try:
                test_instance.test_kernel_symmetry_param(density_method, kernel)
                print(f"test_kernel_symmetry_param[{density_method}-{kernel}] PASSED")
                passed_tests += 1
            except Exception as e:
                failed_tests.append((f"test_kernel_symmetry_param[{density_method}-{kernel}]", e))
                print(f"test_kernel_symmetry_param[{density_method}-{kernel}] FAILED: {e}")

    print(f"\n{passed_tests}/{total_tests} tests passed")
    if not failed_tests:
        print("All tests passed!")
        sys.exit(0)
    else:
        print(f"{len(failed_tests)} test(s) failed.")
        sys.exit(1)
