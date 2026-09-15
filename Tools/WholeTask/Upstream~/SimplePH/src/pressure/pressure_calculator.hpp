#pragma once
#include <vector>
#include "particle.hpp"
#include "eos.hpp"

class PressureCalculator
{
public:
    PressureCalculator(const EOS& eos_, double rho0_);

    void compute(std::vector<Particle>& particles,
                 const std::vector<int>& fluid_indices,
                 bool use_negative_pressure_truncation) const;

private:
    const EOS& eos;
    double rho0;
};
