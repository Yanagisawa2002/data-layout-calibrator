#pragma once
#include <vector>
#include <array>
#include "particle.hpp"
#include "kernel.hpp"
#include "eos.hpp"
#include "solver_options.hpp"

struct SolverOptions; // forward declaration

class ForceCalculator
{
public:
    ForceCalculator(const Kernel& kernel_, double h_, const EOS& eos_, double Lx_, double Ly_);

    // compute all particle forces
    void compute(std::vector<Particle>& particles,
                 const std::vector<int>& fluid_indices,
                 const std::vector<std::vector<int>>& neighbors,
                 const SolverOptions& options,
                 double mu,
                 const std::array<double,2>& b_eff,
                 double dx0,
                 double c,
                 std::vector<std::array<double,2>>& accel);

private:
    const Kernel& kernel;
    const EOS& eos;
    double h;
    double Lx, Ly;
};
