#pragma once
#include "integrator.hpp"
#include "utils.hpp"

#include <omp.h>

class EulerIntegrator : public Integrator
{
public:
    void step1(
        std::vector<Particle> &particles,
        const std::vector<int> &fluid_indices,
        const std::vector<std::array<double, 2>> &accel,
        double dt,
        double Lx,
        double Ly) override
    {
        // EulerIntegrator does not have step 1
    }

    void step2(
        std::vector<Particle> &particles,
        const std::vector<int> &fluid_indices,
        const std::vector<std::array<double, 2>> &accel,
        double dt,
        double Lx,
        double Ly) override
    {

// only integrate fluid particles
#pragma omp parallel for schedule(static)
        for (int idx = 0; idx < (int)fluid_indices.size(); ++idx)
        {
            int i = fluid_indices[idx];
            Particle &pi = particles[i];

            pi.v[0] += accel[i][0] * dt;
            pi.v[1] += accel[i][1] * dt;

            pi.x[0] += pi.v[0] * dt;
            pi.x[1] += pi.v[1] * dt;

            // periodic BC
            if (pi.x[0] >= Lx / 2)
                pi.x[0] -= Lx;
            if (pi.x[0] < -Lx / 2)
                pi.x[0] += Lx;
            if (pi.x[1] >= Ly / 2)
                pi.x[1] -= Ly;
            if (pi.x[1] < -Ly / 2)
                pi.x[1] += Ly;

            // check for NaN or Inf
            abort_if_not_finite(pi.x[0], pi.x[1], i);

        }
    }
};
