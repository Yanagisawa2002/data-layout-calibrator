#include "density_calculator.hpp"
#include "kernel.hpp"
#include "utils.hpp"
#include <omp.h>

DensityCalculator::DensityCalculator(const Kernel& kernel_,
                                     double h_,
                                     DensityMethod method_)
    : kernel(kernel_), h(h_), method(method_) {}

// compute density summation (rho_i = sum_j m_j W(r_ij))
void DensityCalculator::compute_summation(
    std::vector<Particle>& particles,
    const std::vector<int>& fluid_indices,
    const std::vector<std::vector<int>>& neighbors,
    double Lx,
    double Ly)
{
// only loop over fluid particles
#pragma omp parallel for schedule(static)
    for (int idx = 0; idx < (int)fluid_indices.size(); ++idx)
    {
        int i = fluid_indices[idx];
        Particle& pi = particles[i];
        double rho = 0.0;

        for (int j : neighbors[i])
        {
            const Particle& pj = particles[j];

            double dx = pi.x[0] - pj.x[0];
            double dy = pi.x[1] - pj.x[1];
            double r  = min_image_dist(dx, dy, Lx, Ly);
            rho += pj.m * kernel.getW(r, h);
        }
        // self-contribution
        rho += pi.m * kernel.getW(0.0, h);
        pi.rho = rho;
    }
}

// compute density using continuity equation (d rho / dt)
void DensityCalculator::compute_continuity(std::vector<Particle> &particles,
                                            const std::vector<int>& fluid_indices,
                                           const std::vector<std::vector<int>> &neighbors,
                                           double dt,
                                           double Lx,
                                           double Ly)
{
    // only loop over fluid particles
#pragma omp parallel for schedule(static)
    for (int idx = 0; idx < (int)fluid_indices.size(); ++idx)
    {
        int i = fluid_indices[idx];
        Particle &pi = particles[i];
        pi.drho_dt = 0.0;

        double drho_dt_i = 0.0;
        const double vx_i = pi.v[0];
        const double vy_i = pi.v[1];
        const double rho_i = pi.rho;

        for (int j : neighbors[i])
        {
            Particle &pj = particles[j];

            double dx = pi.x[0] - pj.x[0];
            double dy = pi.x[1] - pj.x[1];
            double r = min_image_dist(dx, dy, Lx, Ly);

            double dW = kernel.getdW(r, h) / r; 

            const double vx_j = (pj.type == 1 && pj.vf.has_value()) ? (*pj.vf)[0] : pj.v[0];
            const double vy_j = (pj.type == 1 && pj.vf.has_value()) ? (*pj.vf)[1] : pj.v[1];

            const double dvx = vx_i - vx_j;
            const double dvy = vy_i - vy_j;

            drho_dt_i += (pj.m / pj.rho) * (dvx * dW * dx + dvy * dW * dy);
        }
        drho_dt_i *= rho_i;
        pi.drho_dt = drho_dt_i;
        pi.rho = rho_i + drho_dt_i * dt;
    }
}
