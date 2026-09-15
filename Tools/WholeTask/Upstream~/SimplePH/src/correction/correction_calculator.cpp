#include "correction_calculator.hpp"
#include "utils.hpp"
#include <omp.h>

// compute xsph velocity correction
void CorrectionCalculator::compute_xsph_velocity_correction(
    std::vector<Particle> &particles,
    const std::vector<int> &fluid_indices,
    const std::vector<std::vector<int>> &neighbors,
    const Kernel &kernel,
    double h,
    double Lx,
    double Ly,
    double eta
)
{
// only loop over fluid particles
#pragma omp parallel for schedule(static)
    for (int idx = 0; idx < (int)fluid_indices.size(); ++idx)
    {
        int i = fluid_indices[idx];
        Particle &pi = particles[i];

        if (pi.v_xsph.has_value())
        {
            *pi.v_xsph = {0.0, 0.0};
        }

        double Wi = 0.0;

        double vx_xsph = 0.0;
        double vy_xsph = 0.0;

        unsigned int neighbor_count = 0;

        for (int j : neighbors[i])
        {
            Particle &pj = particles[j];

            double dx = pi.x[0] - pj.x[0];
            double dy = pi.x[1] - pj.x[1];

            double r = min_image_dist(dx, dy, Lx, Ly);
            double Wij = kernel.getW(r, h);

            double rho_j = pj.rho;
            vx_xsph += (pj.v[0] - pi.v[0]) * (pj.m / rho_j) * Wij;
            vy_xsph += (pj.v[1] - pi.v[1]) * (pj.m / rho_j) * Wij;

            Wi += Wij;
            ++neighbor_count;
        }

        if (neighbor_count > 0 && Wi > 0.0)
        {
            (*pi.v_xsph)[0] = pi.v[0] + eta * vx_xsph / Wi;
            (*pi.v_xsph)[1] = pi.v[1] + eta * vy_xsph / Wi;
        }
        else
        {
            (*pi.v_xsph)[0] = pi.v[0];
            (*pi.v_xsph)[1] = pi.v[1];
        }
    }
}
