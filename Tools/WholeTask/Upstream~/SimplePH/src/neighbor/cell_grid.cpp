#include "cell_grid.hpp"
#include <cmath>
#include <omp.h>

CellGrid::CellGrid(double rcut_, double Lx_, double Ly_)
    : rcut(rcut_), Lx(Lx_), Ly(Ly_)
{
    // number cells
    nx = static_cast<int>(std::floor(Lx / rcut));
    ny = static_cast<int>(std::floor(Ly / rcut));
    if (nx < 1)
        nx = 1;
    if (ny < 1)
        ny = 1;

    hx = Lx / nx;
    hy = Ly / ny;

    // decrease cell numbers, if hx or hy < rcut
    while (nx > 1 && hx < rcut)
    {
        nx--;
        hx = Lx / nx;
    }
    while (ny > 1 && hy < rcut)
    {
        ny--;
        hy = Ly / ny;
    }

    // printf("CellGrid init: nx=%d ny=%d hx=%.3f hy=%.3f (rcut=%.3f)\n",
    //        nx, ny, hx, hy, rcut);

    // printf("\n--- Cell bounds ---\n");

    // printf("CellGrid init: nx=%d ny=%d hx=%.3f hy=%.3f (rcut=%.3f)\n",
    //        nx, ny, hx, hy, rcut);

    // printf("\n--- Cell bounds ---\n");
    // for (int cy = 0; cy < ny; ++cy)
    // {
    //     for (int cx = 0; cx < nx; ++cx)
    //     {
    //         int idx = cy * nx + cx;

    //         double x0 = cx * hx;
    //         double x1 = (cx + 1) * hx;
    //         double y0 = cy * hy;
    //         double y1 = (cy + 1) * hy;

    //         printf("Cell %2d: cx=%d cy=%d  x:[%.3f, %.3f] y:[%.3f, %.3f]\n",
    //                idx, cx, cy, x0, x1, y0, y1);
    //     }
    // }

    cells.resize(nx * ny);
    cell_neighbors.resize(nx * ny);

    // printf("\n--- Cell neighbors (9 per cell) ---\n");

    for (int cy = 0; cy < ny; ++cy)
        for (int cx = 0; cx < nx; ++cx)
        {
            int idx = cy * nx + cx;
            int k = 0;

            // printf("Cell %2d neighbors:", idx);

            for (int dy = -1; dy <= 1; ++dy)
            {
                for (int dx = -1; dx <= 1; ++dx)
                {

                    int ncx = (cx + dx + nx) % nx;
                    int ncy = (cy + dy + ny) % ny;
                    int nidx = ncy * nx + ncx;

                    cell_neighbors[idx][k++] = nidx;

                    // printf(" %d", nidx);
                }
            }

            // printf("\n");
        }
}

// build function using buffer per thread
void CellGrid::build(const std::vector<Particle> &particles)
{
    const int N = particles.size();

    // clear final cell arrays
    for (auto &c : cells)
        c.clear();

    const int nCells = nx * ny;
    const int nThreads = omp_get_max_threads();

    // each thread builds its own cell buffers
    std::vector<std::vector<std::vector<int>>> local_cells(nThreads, std::vector<std::vector<int>>(nCells));

// parallel fill of buffer per thread
#pragma omp parallel for schedule(static)
    for (int i = 0; i < N; ++i)
    {
        int tid = omp_get_thread_num();

        // periodic wrap to [0, L)
        double xp = fmod(particles[i].x[0], Lx);
        if (xp < 0)
            xp += Lx;
        double yp = fmod(particles[i].x[1], Ly);
        if (yp < 0)
            yp += Ly;

        int cx = int(xp / hx);
        int cy = int(yp / hy);

        local_cells[tid][cy * nx + cx].push_back(i);
    }

    // merge local thread cells to final cells
    for (int cell = 0; cell < nCells; ++cell)
    {
        size_t total = 0;
        for (int t = 0; t < nThreads; ++t)
            total += local_cells[t][cell].size();
        cells[cell].reserve(total);
        for (int t = 0; t < nThreads; ++t)
            for (int pid : local_cells[t][cell])
                cells[cell].push_back(pid);
    }

    // build sorted order for better performance
    sorted_ids.clear();
    sorted_ids.reserve(N);

    for (const auto &c : cells)
        for (int pid : c)
            sorted_ids.push_back(pid);

    // build inverse map
    inv_id.resize(N);
    for (int pos = 0; pos < N; ++pos)
        inv_id[sorted_ids[pos]] = pos;
}

// neighbor search
template <typename Func>
void CellGrid::find_neighbors(int pid,
                          const std::vector<Particle> &particles,
                          Func callback) const
{
    const Particle &p = particles[pid];

    // wrap coordinates into [0,L)
    double xp = fmod(p.x[0], Lx);
    if (xp < 0)
        xp += Lx;
    double yp = fmod(p.x[1], Ly);
    if (yp < 0)
        yp += Ly;
    int cx = int(xp / hx);
    int cy = int(yp / hy);
    int idx = cy * nx + cx;

    // search 3×3 cell neighbors
    for (int nb : cell_neighbors[idx])
    {
        const auto &cell = cells[nb];
        for (int j : cell)
        {
            if (j == pid)
                continue;
            double dx = particles[j].x[0] - p.x[0];
            double dy = particles[j].x[1] - p.x[1];
            double r = min_image_dist(dx, dy, Lx, Ly);
            if (r <= rcut)
                callback(pid, j, dx, dy, r);
        }
    }
}

// update neighbor list
void CellGrid::update_neighbors(const std::vector<Particle> &particles,
                            std::vector<std::vector<int>> &neighbors)
{
    neighbors.clear();
    neighbors.resize(particles.size());
    build(particles);

#pragma omp parallel for
    for (size_t i = 0; i < particles.size(); ++i)
    {
        find_neighbors(static_cast<int>(i), particles,
                       [&](int pid, int jid, double dx, double dy, double r)
                       { neighbors[pid].push_back(jid); });
    }
}

