#pragma once
#include <vector>
#include <array>
#include <cstdio>
#include "particle.hpp"
#include "utils.hpp"

class CellGrid
{
public:
    CellGrid(double rcut_, double Lx_, double Ly_);

    void build(const std::vector<Particle> &particles);

    template <typename Func>
    void find_neighbors(int pid,
                        const std::vector<Particle> &particles,
                        Func callback) const;

    void update_neighbors(const std::vector<Particle> &particles,
                          std::vector<std::vector<int>> &neighbors);

public:
    std::vector<int> sorted_ids;
    std::vector<int> inv_id;

private:
    double rcut, Lx, Ly;
    int nx, ny;
    double hx, hy;

    std::vector<std::vector<int>> cells;            // cells
    std::vector<std::array<int, 9>> cell_neighbors; // predefined neigbor cell
};
