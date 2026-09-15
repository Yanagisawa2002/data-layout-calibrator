
#include "force_calculator.hpp"
#include "utils.hpp"
#include <omp.h>
#include <cmath>
#include <algorithm>

ForceCalculator::ForceCalculator(const Kernel& kernel_, double h_, const EOS& eos_, double Lx_, double Ly_)
    : kernel(kernel_), h(h_), eos(eos_), Lx(Lx_), Ly(Ly_) {}

// compute particle forces (pressure ( + tensile instability correction ) ( + artificial viscosity ) ( + transport velocity) + viscous terms)
void ForceCalculator::compute(std::vector<Particle>& particles,
                 const std::vector<int>& fluid_indices,
                 const std::vector<std::vector<int>>& neighbors,
                 const SolverOptions& options,
                 double mu,
                 const std::array<double,2>& b_eff,
                 double dx0,
                 double c,
                 std::vector<std::array<double,2>>& accel)
{
#pragma omp parallel for schedule(static)
    for (int i = 0; i < (int)accel.size(); ++i)
        accel[i] = {0.0, 0.0};

    // only loop over fluid particles
#pragma omp parallel for schedule(static)
    for (int idx = 0; idx < (int)fluid_indices.size(); ++idx)
    {
        int i = fluid_indices[idx];
        Particle &pi = particles[i];

        double fx = 0.0, fy = 0.0;
        const double vx_i = pi.v[0];
        const double vy_i = pi.v[1];
        const double p_i = pi.p;
        const double rho_i = pi.rho;
        const double V_i = pi.m / rho_i;

        if (options.use_transport_velocity && pi.bpc.has_value())
        {
            (*pi.bpc)[0] = 0.0;
            (*pi.bpc)[1] = 0.0;
        }

        // only necessary for transport velocity calculation
        const double Ai11 = options.use_transport_velocity ? rho_i * vx_i * ((*pi.tv)[0] - vx_i) : 0.0;
        const double Ai12 = options.use_transport_velocity ? rho_i * vx_i * ((*pi.tv)[1] - vy_i) : 0.0;
        const double Ai21 = options.use_transport_velocity ? rho_i * vy_i * ((*pi.tv)[0] - vx_i) : 0.0;
        const double Ai22 = options.use_transport_velocity ? rho_i * vy_i * ((*pi.tv)[1] - vy_i) : 0.0;

        for (int j : neighbors[i])
        {
            Particle &pj = particles[j];

            const double vx_j = (pj.type == 1 && pj.vf.has_value()) ? (*pj.vf)[0] : pj.v[0];
            const double vy_j = (pj.type == 1 && pj.vf.has_value()) ? (*pj.vf)[1] : pj.v[1];
            const double p_j = pj.p;

            const double rho_j = pj.rho;
            const double V_j = pj.m / rho_j;

            double dx = pi.x[0] - pj.x[0];
            double dy = pi.x[1] - pj.x[1];
            double r = min_image_dist(dx, dy, Lx, Ly);
            double dW = kernel.getdW(r, h) / r;

            const double V_ij_sqr = V_i * V_i + V_j * V_j;

            // pressure force
            const double p_fac = V_ij_sqr * (rho_j * p_i + rho_i * p_j) / (rho_i + rho_j);
            fx -= p_fac * dW * dx;
            fy -= p_fac * dW * dy;

            const double dvx = vx_i - vx_j;
            const double dvy = vy_i - vy_j;

            // using tensile instability correction if enabled
            if (options.use_tensile_instability_correction && pj.type == 0)
            {
                // tensile instability correction (Monaghan 2000)
                double Wij = kernel.getW(r, h);
                double W_dp = kernel.getW(dx0, h);
                double fij = Wij / W_dp;
                double fij_fourp = fij * fij * fij * fij;

                double tilde_pi = (p_i >= 0.0) ? 0.01 * p_i : options.epsilon * std::abs(p_i);
                double tilde_pj = (p_j >= 0.0) ? 0.01 * p_j : options.epsilon * std::abs(p_j);

                double tensile_p_fac = V_ij_sqr * (rho_j * tilde_pi + rho_i * tilde_pj) / (rho_i + rho_j);
                tensile_p_fac *= fij_fourp;

                fx -= tensile_p_fac * dW * dx;
                fy -= tensile_p_fac * dW * dy;
            }

            // using articial viscosity if enabled
            if (options.use_artificial_viscosity and pj.type == 0)
            {
                // avoid division by zero when r is extremely small
                const double r_sqr = r * r + 0.01 * h * h;

                // artificial viscosity (Monaghan 1992)
                double vr = dvx * dx + dvy * dy;
                if (vr < 0.0)
                {
                    double artvisc_fac = -(pi.m * pj.m * options.alpha * h * c * vr) / (((rho_i + rho_j) / 2) * r_sqr);
                    fx -= artvisc_fac * dW * dx;
                    fy -= artvisc_fac * dW * dy;
                }
            }

            // using transport velocity correction if enabled
            if (options.use_transport_velocity)
            {
                const double Aj11 = (pj.type == 0) ? rho_j * vx_j * ((*pj.tv)[0] - vx_j) : 0.0;
                const double Aj12 = (pj.type == 0) ? rho_j * vx_j * ((*pj.tv)[1] - vy_j) : 0.0;
                const double Aj21 = (pj.type == 0) ? rho_j * vy_j * ((*pj.tv)[0] - vx_j) : 0.0;
                const double Aj22 = (pj.type == 0) ? rho_j * vy_j * ((*pj.tv)[1] - vy_j) : 0.0;

                const double tv_fac = 0.5 * V_ij_sqr;
                fx += tv_fac * ((Ai11 + Aj11) * dW * dx + (Ai12 + Aj12) * dW * dy);
                fy += tv_fac * ((Ai21 + Aj21) * dW * dx + (Ai22 + Aj22) * dW * dy);

                const double tv_bpc_fac = V_ij_sqr / pi.m;
                (*pi.bpc)[0] -= tv_bpc_fac * eos.get_tvp_bp() * dW * dx;
                (*pi.bpc)[1] -= tv_bpc_fac * eos.get_tvp_bp() * dW * dy;
            }

            // viscous force
            const double visc_fac = V_ij_sqr * mu;
            fx += visc_fac * dW * dvx;
            fy += visc_fac * dW * dvy;
        }

        // compute acceleration and add (possibly damped) body force
        accel[i][0] = fx / pi.m + b_eff[0];
        accel[i][1] = fy / pi.m + b_eff[1];
    }
}
