#pragma once
#include <vector>
#include <array>
#include "particle.hpp"

class Integrator
{
public:
    virtual ~Integrator() = default;

    // Phase 1
    virtual void step1(
        std::vector<Particle> &particles,
        const std::vector<int> &fluid_indices,
        const std::vector<std::array<double,2>> &accel,
        double dt, double Lx, double Ly
    ) = 0;


    // Phase 2
    virtual void step2(
        std::vector<Particle> &particles,
        const std::vector<int> &fluid_indices,
        const std::vector<std::array<double,2>> &accel,
        double dt, double Lx, double Ly
    ) = 0;
    
};
