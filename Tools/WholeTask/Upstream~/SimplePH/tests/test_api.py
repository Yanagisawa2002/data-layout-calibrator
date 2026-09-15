#!/usr/bin/env python3
"""
Unit tests for SimplePH solver.

Tests verify basic functionality:
- Solver initialization and parameter configuration
- Material property setters (viscosity, density, acceleration)
- Physics model selection (EOS, integrator, density method)
- Particle creation and manipulation
- Stability parameter computation
- Basic simulation execution without crashes
"""

import sys
import os

# Add python/ to path to import SimplePH
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '../python'))

import SimplePH


class TestSolverInitialization:
    """Test basic solver creation."""

    def test_create_solver_cubic_spline(self):
        """Verify solver creation with CubicSpline kernel."""
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=1.0, kernel_type=SimplePH.KernelType.CubicSpline
        )
        assert solver is not None

    def test_create_solver_quintic_spline(self):
        """Verify solver creation with QuinticSpline kernel."""
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=1.0, kernel_type=SimplePH.KernelType.QuinticSpline
        )
        assert solver is not None

    def test_create_solver_wendlandC2(self):
        """Verify solver creation with WendlandC2 kernel."""
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=1.0, kernel_type=SimplePH.KernelType.WendlandC2
        )
        assert solver is not None

    def test_create_solver_wendlandC4(self):
        """Verify solver creation with WendlandC4 kernel."""
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=1.0, kernel_type=SimplePH.KernelType.WendlandC4
        )
        assert solver is not None

class TestMaterialProperties:
    """Test material property configuration."""

    def test_set_viscosity(self):
        """Verify viscosity setter."""
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=1.0, kernel_type=SimplePH.KernelType.CubicSpline
        )
        mu = 1.0
        solver.set_viscosity(mu)
        assert solver.get_viscosity() == mu

    def test_set_density(self):
        """Verify density setter."""
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=1.0, kernel_type=SimplePH.KernelType.CubicSpline
        )
        rho0 = 1000.0
        rho_fluct = 0.01
        solver.set_density(rho0, rho_fluct)
        assert solver.get_density() == (rho0, rho_fluct)

    def test_set_acceleration(self):
        """Verify body force setter."""
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=1.0, kernel_type=SimplePH.KernelType.CubicSpline
        )
        b = [0.0024, 0.0]
        solver.set_acceleration(b)
        acc = solver.get_acceleration()
        assert abs(acc[0] - b[0]) < 1e-12
        assert abs(acc[1] - b[1]) < 1e-12


class TestPhysicsConfiguration:
    """Test physics model configuration."""

    def test_set_tait_eos(self):
        """Verify Tait EOS can be set."""
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=1.0, kernel_type=SimplePH.KernelType.CubicSpline
        )
        solver.set_eos(SimplePH.EOSType.Tait)

    def test_set_linear_eos(self):
        """Verify Linear EOS can be set."""
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=1.0, kernel_type=SimplePH.KernelType.CubicSpline
        )
        solver.set_eos(SimplePH.EOSType.Linear)

    def test_set_euler_integrator(self):
        """Verify Euler integrator can be set."""
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=1.0, kernel_type=SimplePH.KernelType.CubicSpline
        )
        solver.set_integrator(SimplePH.EulerIntegrator())

    def test_set_verlet_integrator(self):
        """Verify Verlet integrator can be set."""
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=1.0, kernel_type=SimplePH.KernelType.CubicSpline
        )
        solver.set_integrator(SimplePH.VerletIntegrator())

    def test_set_transport_velocity_verlet_integrator(self):
        """Verify Transport Velocity Verlet integrator can be set."""
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=1.0, kernel_type=SimplePH.KernelType.CubicSpline
        )
        solver.set_integrator(SimplePH.TransportVelocityVerletIntegrator())

    def test_set_summation_density_method(self):
        """Verify summation density method can be set."""
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=1.0, kernel_type=SimplePH.KernelType.CubicSpline
        )
        solver.set_density_method(SimplePH.DensityMethod.Summation)

    def test_set_continuity_density_method(self):
        """Verify continuity density method can be set."""
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=1.0, kernel_type=SimplePH.KernelType.CubicSpline
        )
        solver.set_density_method(SimplePH.DensityMethod.Continuity)


class TestParticles:
    """Test particle creation and retrieval."""

    def test_create_fluid_particle(self):
        """Verify fluid particle can be created."""
        p = SimplePH.Particle()
        p.x = [0.05, 0.05]
        p.v = [0.0, 0.0]
        p.m = 1.0
        p.rho = 1000.0
        p.p = 0.0
        p.type = 0
        
        assert p.type == 0
        assert abs(p.x[0] - 0.05) < 1e-10

    def test_create_boundary_particle(self):
        """Verify boundary particle can be created."""
        p = SimplePH.Particle()
        p.x = [0.0, 0.05]
        p.type = 1
        
        assert p.type == 1

    def test_set_and_get_particles(self):
        """Verify particles can be set and retrieved."""
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=1.0, kernel_type=SimplePH.KernelType.CubicSpline
        )
        
        # Create 3 fluid particles
        particles = []
        for i in range(3):
            p = SimplePH.Particle()
            p.x = [0.05 + i * 0.01, 0.05]
            p.v = [0.0, 0.0]
            p.m = 1.0
            p.rho = 1000.0
            p.p = 0.0
            p.type = 0
            particles.append(p)
        
        solver.set_particles(particles)
        retrieved = solver.get_particles()
        
        assert len(retrieved) == 3
        assert abs(retrieved[0].x[0] - 0.05) < 1e-10


class TestStabilityParameters:
    """Test stability parameter computation."""

    def test_compute_soundspeed_and_timestep(self):
        """Verify soundspeed and timestep is computed (should be positive)."""
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=0.006, kernel_type=SimplePH.KernelType.CubicSpline
        )
        solver.set_density(rho0=1000.0, rho_fluct=0.01)
        solver.set_acceleration([0.0024, 0.0])

        solver.compute_soundspeed_and_timestep()
        # No exception = test passes

class TestAdvancedSolverFeatures:
    """Test advanced solver features like AV, tensile correction, XSPH filter, TVF and OpenMP threads."""

    def test_activate_artificial_viscosity(self):
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=0.01,
            kernel_type=SimplePH.KernelType.CubicSpline
        )
        solver.set_density(1000.0, 0.01)
        solver.set_viscosity(1.0)
        solver.compute_soundspeed_and_timestep()
        solver.activate_artificial_viscosity(alpha=1.5)  # Test AV activation

    def test_activate_tensile_instability_correction(self):
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=0.01,
            kernel_type=SimplePH.KernelType.CubicSpline
        )
        solver.activate_tensile_instability_correction(epsilon=0.3)

    def test_activate_xsph_filter(self):
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=0.01,
            kernel_type=SimplePH.KernelType.CubicSpline
        )
        solver.activate_xsph_filter(eta=0.1)

    def test_activate_transport_velocity_formulation(self):
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=0.01,
            kernel_type=SimplePH.KernelType.CubicSpline
        )
        solver.activate_transport_velocity()

    def test_set_omp_threads(self):
        SimplePH.set_omp_threads(2)  # Test setting OpenMP threads

class TestSimulationExecution:
    """Test that simulations can run without errors."""

    def test_run_minimal_simulation(self):
        """Verify a minimal simulation can run without crashing."""
        solver = SimplePH.Solver(
            h=0.01, Lx=0.1, Ly=0.1, dx0=0.005,
            Lref=0.1, vref=0.006, kernel_type=SimplePH.KernelType.CubicSpline
        )
        
        # Configure physics
        solver.set_viscosity(mu=1.0)
        solver.set_density(rho0=1000.0, rho_fluct=0.01)
        solver.set_acceleration([0.0024, 0.0])
        solver.set_eos(SimplePH.EOSType.Tait)
        solver.set_density_method(SimplePH.DensityMethod.Continuity)
        solver.set_integrator(SimplePH.VerletIntegrator())
        
        solver.compute_soundspeed_and_timestep()
        
        # Create 3 test particles
        particles = []
        for i in range(3):
            p = SimplePH.Particle()
            p.x = [0.05 + i * 0.01, 0.05]
            p.v = [0.0, 0.0]
            p.m = 1.0
            p.rho = 1000.0
            p.p = 0.0
            p.type = 0
            particles.append(p)
        
        solver.set_particles(particles)
        
        # Run for 3 steps without VTK output
        solver.run(steps=3, vtk_freq=0, log_freq=0)
        
        # Verify particles were updated
        final = solver.get_particles()
        assert len(final) == 3

# Direct execution support
if __name__ == '__main__':
    print("Running SimplePH unit tests...\n")
    
    test_classes = [
        TestSolverInitialization,
        TestMaterialProperties,
        TestPhysicsConfiguration,
        TestParticles,
        TestStabilityParameters,
        TestAdvancedSolverFeatures,
        TestSimulationExecution,
    ]
    
    total_tests = 0
    passed_tests = 0
    
    for test_class in test_classes:
        test_instance = test_class()
        test_methods = [m for m in dir(test_instance) if m.startswith('test_')]
        
        for method_name in test_methods:
            total_tests += 1
            try:
                method = getattr(test_instance, method_name)
                method()
                print(f"{test_class.__name__}.{method_name}")
                passed_tests += 1
            except Exception as e:
                print(f"{test_class.__name__}.{method_name}: {e}")
    
    print(f"\n{passed_tests}/{total_tests} tests passed")
    
    if passed_tests == total_tests:
        print("All tests passed!")
        sys.exit(0)
    else:
        print(f"{total_tests - passed_tests} test(s) failed!")
        sys.exit(1)
