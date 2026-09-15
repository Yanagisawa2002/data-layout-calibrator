#include "boundary_calculator.hpp"
#include "utils.hpp"
#include <omp.h>


BoundaryCalculator::BoundaryCalculator(const Kernel& kernel_,
                                       double h_,
                                       double Lx_,
                                       double Ly_)
    : kernel(kernel_), h(h_), Lx(Lx_), Ly(Ly_) {}


// compute boundary conditions for boundary particles
void BoundaryCalculator::compute(std::vector<Particle>& particles,
                                 const std::vector<int>& boundary_indices,
                                 const std::vector<std::vector<int>>& neighbors,
                                 const EOS& eos,
                                 const std::array<double,2>& b_eff,
                                 double rho0)
{
// only loop over boundary particles
#pragma omp parallel for schedule(static)
    for (int idx = 0; idx < (int)boundary_indices.size(); ++idx)
    {
        int i = boundary_indices[idx];
        Particle& pi = particles[i];

        // reset fictitious velocity
        if (pi.vf.has_value())
            *pi.vf = {0.0, 0.0};

        pi.p = 0.0;

        double vfx = 0.0, vfy = 0.0;
        double pf = 0.0;
        double Wi = 0.0;
        double phx = 0.0, phy = 0.0;
        unsigned int neighbor_count = 0;

        for (int j : neighbors[i])
        {
            const Particle& pj = particles[j];

            // skip other boundary particles
            if (pj.type == 1)
                continue;

            double dx = pi.x[0] - pj.x[0];
            double dy = pi.x[1] - pj.x[1];

            double r = min_image_dist(dx, dy, Lx, Ly);
            double Wij = kernel.getW(r, h);

            vfx -= pj.v[0] * Wij;
            vfy -= pj.v[1] * Wij;
            pf  += pj.p * Wij;

            phx += pj.rho * dx * Wij;
            phy += pj.rho * dy * Wij;

            Wi += Wij;
            ++neighbor_count;
        }

        if (neighbor_count > 0 && Wi > 0.0)
        {
            vfx = vfx / Wi + 2.0 * pi.v[0];
            vfy = vfy / Wi + 2.0 * pi.v[1];

            pf = pf / Wi + b_eff[0] * (phx / Wi) + b_eff[1] * (phy / Wi);

            if (pi.vf.has_value())
            {
                (*pi.vf)[0] = vfx;
                (*pi.vf)[1] = vfy;
            }

            pi.p   = pf;
            pi.rho = eos.density_from_pressure(pf);
        }
        else
        {
            if (pi.vf.has_value())
                *pi.vf = {0.0, 0.0};

            pi.p   = eos.get_bp();
            pi.rho = rho0;
        }
    }
}
