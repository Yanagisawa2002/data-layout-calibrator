#pragma once
#include <vector>
#include "particle.hpp"

enum class DensityMethod
{
    Summation,
    Continuity
};

class Kernel; // forward declaration

class DensityCalculator
{
public:
    DensityCalculator(const Kernel &kernel_, double h_,
                      DensityMethod method_ = DensityMethod::Summation);

    void set_method(DensityMethod m) { method = m; }

    DensityMethod get_method() const { return method; }

    void compute_summation(
        std::vector<Particle> &particles,
        const std::vector<int> &fluid_indices,
        const std::vector<std::vector<int>> &neighbors,
        double Lx,
        double Ly);

    void compute_continuity(std::vector<Particle> &particles,
                            const std::vector<int> &fluid_indices,
                            const std::vector<std::vector<int>> &neighbors,
                            double dt,
                            double Lx,
                            double Ly);

private:
    const Kernel &kernel;
    double h;
    DensityMethod method;
};
