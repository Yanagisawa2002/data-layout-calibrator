#include <cstdio>
#include <algorithm>
#include <cmath>
#include <limits>
#include <deque>
#include <chrono>
#include <filesystem>

#include "solver.hpp"
#include "kernel.hpp"
#include "utils.hpp"
#include "integrator.hpp"
#include "euler_integrator.hpp"
#include "verlet_integrator.hpp"
#include "tv_verlet_integrator.hpp"
#include "terminal.hpp"
#include "vtk_writer.hpp"

// include OpenMP for parallelization
#include <omp.h>

// constructor
Solver::Solver(double h_, double Lx_, double Ly_, double dx0_, double Lref_, double vref_, KernelType kernel_type_)
    : h(h_), Lx(Lx_), Ly(Ly_), dx0(dx0_), Lref(Lref_), vref(vref_),
      kernel(kernel_type_, h_), eos{EOSType::Tait, rho0, c, 0.0, 0.0},
      density_calculator(kernel, h_, DensityMethod::Summation), pressure_calculator(eos, rho0),
      boundary_calculator(kernel, h_, Lx_, Ly_), force_calculator(kernel, h_, eos, Lx_, Ly_),
     timestep_calculator(rho0, mu, Lref, vref),
      cell_grid(kernel.get_rcut(h_), Lx_, Ly_)
{
int n_threads = omp_get_max_threads();
printf("[OpenMP] Threads available: %d\n", n_threads);
}

// set particles
void Solver::set_particles(const std::vector<Particle> &particles_)
{
    particles = particles_;
    neighbors.resize(particles.size());
    accel.resize(particles.size(), {0.0, 0.0});
    build_index_lists();
    initialize_particles();
}

// get particles
const std::vector<Particle> &Solver::get_particles() const { return particles; }

// build index lists for particle types
void Solver::build_index_lists()
{
    fluid_indices.clear();
    boundary_indices.clear();
    // solid_indices.clear(); // for future use

    for (int i = 0; i < particles.size(); ++i)
    {
        if (particles[i].type == 0)
            fluid_indices.push_back(i);
        else if (particles[i].type == 1)
            boundary_indices.push_back(i);
        // else if (particles[i].type == 2) // for future use
        //     solid_indices.push_back(i);
    }
}

// initialize particles
void Solver::initialize_particles()
{
    for (auto &p : particles)
    {
        // initialize BC field
        if (p.type == 1)
            p.vf = {0.0, 0.0};
        else
            p.vf = std::nullopt;

        // initialize drho_dt field for continuity density approach
        if (density_calculator.get_method() == DensityMethod::Summation)
            p.drho_dt = std::nullopt;
        else
            p.drho_dt = 0.0;

        // initialize xsph filter field
        if (options.use_xsph_filter)
            p.v_xsph = {0.0, 0.0};
        else
            p.v_xsph = std::nullopt;

        // initialize transport velocity fields
        if (options.use_transport_velocity)
        {
            p.tv = {0.0, 0.0};
            p.bpc = {0.0, 0.0};
        }
        else
        {
            p.tv = std::nullopt;
            p.bpc = std::nullopt;
        }
    }
}

// set viscosity
void Solver::set_viscosity(double mu_) { mu = mu_; }

// get viscosity
double Solver::get_viscosity() const {return mu;}

// set density
void Solver::set_density(double rho0_, double rho_fluct_)
{
    rho0 = rho0_;
    rho_fluct = rho_fluct_;
}

// get density
std::pair<double, double> Solver::get_density() const
{
    return {rho0, rho_fluct};
}

// set body force
void Solver::set_acceleration(const std::array<double, 2> &b_, int damp_timesteps_)
{
    b = b_;
    damp_timesteps = damp_timesteps_;
}

// get body force
std::array<double, 2> Solver::get_acceleration() const
{
    return b;
}

// compute soundspeed and timestep
void Solver::compute_soundspeed_and_timestep()
{
    timestep_calculator.set_params(rho0, mu, Lref, vref);
    c = timestep_calculator.compute_soundspeed(b, rho_fluct);
    dt = timestep_calculator.compute_timestep(b, h);
}

// set EOS
void Solver::set_eos(EOSType eos_type_, double bp_fac_, double tvp_bp_fac_) { eos = EOS(eos_type_, rho0, c, bp_fac_, tvp_bp_fac_); }

// activate artificial viscosity
void Solver::activate_artificial_viscosity(double alpha_)
{
    options.use_artificial_viscosity = true;
    options.alpha = alpha_;

    printf("artificial viscosity activated\n");
    
    double mu_eff = 0.0;
    if (mu==0.0)
    {
        mu_eff = rho0 *  (1.0/8.0) * options.alpha * h * c;
        printf("mu_eff (AV): %.6f\n", mu_eff);
    }
    if (mu>0.0)
    {
        mu_eff = rho0 * (1.0/8.0) * options.alpha * h * c + mu;
        printf("mu_eff (mu+AV): %.6f\n", mu_eff);
    }
    dt = timestep_calculator.compute_timestep_AV(b, h, mu_eff);
}

// activate tensile instability correction
void Solver::activate_tensile_instability_correction(double epsilon_)
{
    options.use_tensile_instability_correction = true;
    options.epsilon = epsilon_;
        printf("tensile instability correction activated\n");
}

// activate xsph filter
void Solver::activate_xsph_filter(double eta_)
{
    options.use_xsph_filter = true;
    options.eta = eta_;
        printf("XSph filter activated (only use with velocity Verlet integrator)\n");
}

// activate negative pressure truncation
void Solver::activate_negative_pressure_truncation()
{
    options.use_negative_pressure_truncation = true;
        printf("negative pressure truncation activated\n");
}

// activate transport velocity
void Solver::activate_transport_velocity()
{
    options.use_transport_velocity = true;
        printf("transport velocity activated\n");
}

// set output name
void Solver::set_output_name(const std::string &output_name_)
{
    output_name = output_name_;
    std::filesystem::create_directories(output_name_);
}

// solve one timestep
void Solver::step(int timestep)
{
    // compute effective body force
    this->b_eff = compute_effective_body_force(timestep);

    // integration phase 1
    integrator->step1(particles, fluid_indices, accel, dt, Lx, Ly);

    // update neighbors
    cell_grid.update_neighbors(particles, neighbors);

    // compute densities
    if (density_calculator.get_method() == DensityMethod::Summation)
        density_calculator.compute_summation(particles, fluid_indices, neighbors, Lx, Ly);

    // compute pressures
    pressure_calculator.compute(particles, fluid_indices, options.use_negative_pressure_truncation);

    // compute boundary conditions
    boundary_calculator.compute(particles, boundary_indices, neighbors, eos, b_eff, rho0);

    // correction (xsph etc.)
    if (options.use_xsph_filter)
        correction_calculator.compute_xsph_velocity_correction(particles, fluid_indices, neighbors, kernel, h, Lx, Ly, options.eta);
    
    // compute forces
    force_calculator.compute(particles, fluid_indices, neighbors, options, mu, b_eff, dx0, c, accel);
    
    // density update for continuity method
    if (density_calculator.get_method() == DensityMethod::Continuity)
        density_calculator.compute_continuity(particles, fluid_indices, neighbors, dt, Lx, Ly);

    // integration phase 2
    integrator->step2(particles, fluid_indices, accel, dt, Lx, Ly);
}

// run simulation
void Solver::run(int steps, int vtk_freq, int log_freq)
{
    std::cout << "\n===== Start simulation =====" << std::endl;

    bool first_log = true;
    constexpr int STATUS_LINES = 2;

    if (vtk_freq <= 0)
        vtk_freq = 1;
    if (log_freq < 0)
        log_freq = 0;

    int vtk_counter = 0, log_counter = 0;
    auto start_time = std::chrono::high_resolution_clock::now();

    const int eta_window = 50;
    std::deque<double> step_times;

    for (int i = 0; i < steps; ++i)
    {
        auto step_start = std::chrono::high_resolution_clock::now();

        if (vtk_counter == 0)
            VTKWriter::write(particles, i, output_name);
        vtk_counter = (vtk_counter + 1) % vtk_freq;
        if (log_freq > 0)
            log_counter = (log_counter + 1) % log_freq;
        step(i);

        auto step_end = std::chrono::high_resolution_clock::now();
        double step_time = std::chrono::duration<double>(step_end - step_start).count();

        step_times.push_back(step_time);
        if (step_times.size() > eta_window)
            step_times.pop_front();

        double avg_step_time = 0.0;
        for (double t : step_times)
            avg_step_time += t;
        avg_step_time /= step_times.size();

        int remaining_steps = steps - (i + 1);
        double eta_sec = remaining_steps * avg_step_time;

        auto current_time = std::chrono::high_resolution_clock::now();
        double elapsed = std::chrono::duration<double>(current_time - start_time).count();

        if (log_freq > 0 && log_counter == 0)
        {
            if (!first_log)
                terminal::clear_lines(STATUS_LINES);
            first_log = false;

            double progress = 100.0 * (i + 1) / steps;

            std::cout
                << "Step " << (i + 1) << " / " << steps
                << " (" << std::fixed << std::setprecision(1) << progress << "%)\n"
                << "iter: " << std::setprecision(4) << step_time << " s"
                << " | avg: " << avg_step_time << " s"
                << " | ETA: " << format_time(eta_sec) << "\n";

            terminal::flush();
        }
    }

    auto end_time = std::chrono::high_resolution_clock::now();
    total_elapsed_time = std::chrono::duration<double>(end_time - start_time).count();
    std::cout << "\n=== Simulation completed ===" << std::endl;
    std::cout << "Total time: " << total_elapsed_time << "s" << std::endl;
    std::cout << "Total steps: " << steps << std::endl;
    std::cout << "Average time per iter: " << (total_elapsed_time / steps) << "s" << std::endl;
}

std::array<double, 2> Solver::compute_effective_body_force(int timestep) const
{
    std::array<double, 2> b_eff_local = b; // Basis-Body-Force
    if (damp_timesteps > 0 && timestep < damp_timesteps)
    {
        double t = static_cast<double>(timestep);
        double m_damptime = static_cast<double>(damp_timesteps);
        double scalar = 0.5 * (std::sin(M_PI * (-0.5 + t / m_damptime)) + 1.0);
        b_eff_local[0] *= scalar;
        b_eff_local[1] *= scalar;
    }
    return b_eff_local;
}