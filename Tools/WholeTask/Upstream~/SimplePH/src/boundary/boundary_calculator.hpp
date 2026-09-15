#pragma once
#include <vector>
#include <array>
#include "particle.hpp"
#include "kernel.hpp"
#include "eos.hpp"

class BoundaryCalculator
{
public:
    BoundaryCalculator(const Kernel& kernel_,
                       double h_,
                       double Lx_,
                       double Ly_);

    void compute(std::vector<Particle>& particles,
                 const std::vector<int>& boundary_indices,
                 const std::vector<std::vector<int>>& neighbors,
                 const EOS& eos,
                 const std::array<double,2>& b_eff,
                 double rho0);

private:
    const Kernel& kernel;

    double h;
    double Lx, Ly;
};