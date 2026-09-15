#pragma once

#include <optional>
#include <array>   

struct Particle
{
    std::array<double, 2> x; // x[0]=x, x[1]=y
    std::array<double, 2> v; // v[0]=vx, v[1]=vy
    double rho, p;
    double m;
    unsigned int type; // 0 = fluid, 1 = boundary

    std::optional<double> drho_dt; // optional for continuity density approach

    std::optional<std::array<double, 2>> v_xsph; // optional for xsph filter velocity

    std::optional<std::array<double, 2>> tv; // optional transport velocity
    std::optional<std::array<double, 2>> bpc; // optional back pressure correction

    std::optional<std::array<double, 2>> vf; // fictitious velocity for BC
};