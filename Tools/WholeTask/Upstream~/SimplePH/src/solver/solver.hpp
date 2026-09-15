#pragma once
#include <vector>
#include <memory>
#include <string>
#include <array>

#include "particle.hpp"
#include "kernel.hpp"
#include "cell_grid.hpp"
#include "eos.hpp"
#include "solver_options.hpp"
#include "density_calculator.hpp"
#include "pressure_calculator.hpp"
#include "boundary_calculator.hpp"
#include "correction_calculator.hpp"
#include "force_calculator.hpp"
#include "timestep_calculator.hpp"

class Integrator; // forward declaration

class Solver
{
public:
    // constructor
    Solver(double h, double Lx, double Ly, double dx0, double Lref, double vref, KernelType kernel_type);

    // setter functions
    void set_particles(const std::vector<Particle> &particles_);
    void set_viscosity(double mu_);
    void set_density(double rho0_, double rho_fluct_);
    void set_acceleration(const std::array<double, 2> &b_, int damp_timesteps_ = 0);
    void set_eos(EOSType eos_type_, double bp_fac_, double tvp_bp_fac_ = 0.0);
    void set_integrator(std::shared_ptr<Integrator> integrator_) { this->integrator = integrator_;}
    void set_output_name(const std::string &output_name_);
    void set_density_method(DensityMethod dm) { density_calculator.set_method(dm); }

    // compute soundspeed and timestep
    void compute_soundspeed_and_timestep();

    // feature activation
    void activate_artificial_viscosity(double alpha_ = 1.0);
    void activate_tensile_instability_correction(double epsilon_);
    void activate_xsph_filter(double eta_);
    void activate_negative_pressure_truncation();
    void activate_transport_velocity();

    // simulation functions
    void step(int timestep);
    void run(int steps, int vtk_freq, int log_freq);

    // getter functions
    const std::vector<Particle> &get_particles() const;
    double get_viscosity() const;
    std::pair<double, double> get_density() const;
    std::array<double, 2> get_acceleration() const;

private:
    // geometry
    double h;
    double Lx, Ly;
    double dx0;

    // scale parameters
    double Lref;
    double vref;

    // time and stability parameters
    double c;
    double dt;

    // physical parameters
    double mu;
    double rho0;
    double rho_fluct;

    // body force
    std::array<double, 2> b = {0.0, 0.0};
    // effective body force with damping
    int damp_timesteps;
    std::array<double, 2> b_eff = {0.0, 0.0};

    std::string output_name = "unlabeled_output_internal";

    // solver options
    SolverOptions options;

    // kernel, cell grid, eos
    Kernel kernel;
    CellGrid cell_grid;
    EOS eos;

    // calculator instances
    TimeStepCalculator timestep_calculator;
    DensityCalculator density_calculator;
    PressureCalculator pressure_calculator;
    BoundaryCalculator boundary_calculator;
    ForceCalculator force_calculator;
    CorrectionCalculator correction_calculator;

    // integrator
    std::shared_ptr<Integrator> integrator;

    // particle data, neighbors per particle, particle acceleration
    std::vector<Particle> particles;
    std::vector<std::vector<int>> neighbors;
    std::vector<std::array<double, 2>> accel;

    // particle indices by type
    std::vector<int> fluid_indices;
    std::vector<int> boundary_indices;
    // std::vector<int> solid_indices; // for future use

    // total simulation time in seconds
    double total_elapsed_time = 0.0;

    // internal computation functions
    void build_index_lists();
    void initialize_particles();
    std::array<double, 2> compute_effective_body_force(int timestep) const;
};
