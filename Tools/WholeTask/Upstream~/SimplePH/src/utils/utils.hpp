#pragma once
#include <cmath>
#include <iostream>
#include <string>
#include <sstream>
#include <iomanip>

inline double min_image_dist(double &dx, double &dy, double Lx, double Ly)
{
    if (dx > 0.5 * Lx)
        dx -= Lx;
    if (dx < -0.5 * Lx)
        dx += Lx;
    if (dy > 0.5 * Ly)
        dy -= Ly;
    if (dy < -0.5 * Ly)
        dy += Ly;
    return std::sqrt(dx * dx + dy * dy);
}

inline void abort_if_not_finite(double x, double y, int pid)
{
    if (!std::isfinite(x) || !std::isfinite(y))
    {
        #pragma omp critical
        {
            std::cerr << "[FATAL] Particle " << pid
                      << " has invalid position: ("
                      << x << ", " << y << ")\n";
        }
        std::exit(EXIT_FAILURE);
    }
}

inline std::string format_time(double seconds)
{
    int h = static_cast<int>(seconds) / 3600;
    int m = (static_cast<int>(seconds) % 3600) / 60;
    int s = static_cast<int>(seconds) % 60;

    std::ostringstream oss;
    oss << std::setfill('0')
        << std::setw(2) << h << ":"
        << std::setw(2) << m << ":"
        << std::setw(2) << s;
    return oss.str();
}
