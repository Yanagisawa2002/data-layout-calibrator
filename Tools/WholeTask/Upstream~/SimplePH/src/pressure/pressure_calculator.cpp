#include "pressure_calculator.hpp"

PressureCalculator::PressureCalculator(const EOS& eos_, double rho0_)
    : eos(eos_), rho0(rho0_)
{
}

// compute pressure
void PressureCalculator::compute(std::vector<Particle>& particles,
                                 const std::vector<int>& fluid_indices, 
                                 bool use_negative_pressure_truncation) const
{
// only loop over fluid particles
#pragma omp parallel for schedule(static)
    for (int idx = 0; idx < (int)fluid_indices.size(); ++idx)
    {
        int i = fluid_indices[idx];
        Particle &pi = particles[i];
        double p = eos.pressure_from_density(pi.rho);
        if (use_negative_pressure_truncation)
            p = std::max(p, 0.0);
        pi.p = p;
    }
}
