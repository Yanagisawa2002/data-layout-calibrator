#pragma once
#include <vector>
#include "particle.hpp"
#include "kernel.hpp"

class CorrectionCalculator
{
public:
    CorrectionCalculator() = default;

    void compute_xsph_velocity_correction(
        std::vector<Particle> &particles,
        const std::vector<int> &fluid_indices,
        const std::vector<std::vector<int>> &neighbors,
        const Kernel &kernel,
        double h,
        double Lx,
        double Ly,
        double eta
    );
};
