#pragma once
#include <array>
#include <vector>
#include <algorithm>
#include <cmath>

class TimeStepCalculator
{
public:
    TimeStepCalculator(double rho0_, double mu_, double Lref_, double vref_)
        : rho0(rho0_), mu(mu_), Lref(Lref_), vref(vref_), c(0.0), dt(0.0) {}

    void set_params(double rho0_, double mu_, double Lref_, double vref_)
    {
        rho0 = rho0_;
        mu = mu_;
        Lref = Lref_;
        vref = vref_;
    }

    // compute_soundspeed
    double compute_soundspeed(const std::array<double,2>& b, double rho_fluct)
    {
        if (b == std::array<double,2>{0.0,0.0})
        {
            printf("no body force b incorporated for soundspeec calculation\n");
        }
        double c_cfl = (vref * vref) / rho_fluct;
        double b_mag = std::sqrt(b[0]*b[0] + b[1]*b[1]);
        double c_gw = (b_mag * Lref) / rho_fluct;
        double c_fc = (mu * vref) / (rho_fluct * rho0 * Lref);
        c = std::sqrt(std::max({c_cfl, c_gw, c_fc}));
        printf("soundspeed: c=%.3f\n", c);
        return c;
    }

    // compute timestep
    double compute_timestep(const std::array<double,2>& b, double h)
    {
        std::vector<double> candidates;
        candidates.push_back(0.25*h/c); // CFL
        double b_mag = std::sqrt(b[0]*b[0] + b[1]*b[1]);
        if(b_mag > 0.0)
            candidates.push_back(std::sqrt(h / (16.0*b_mag))); // gravity-wave
        candidates.push_back((h*h*rho0)/(8.0*mu)); // Fourier
        dt = *std::min_element(candidates.begin(), candidates.end());
        printf("timestep: dt=%.9f\n", dt);
        return dt;
    }

    // re-compute timestep for artificial viscosity use
    double compute_timestep_AV(const std::array<double,2>& b, double h, double mu_eff)
    {
        std::vector<double> candidates;
        candidates.push_back(0.25 * h / c); // CFL
        double b_mag = std::sqrt(b[0] * b[0] + b[1] * b[1]);
        if (b_mag > 0.0)
            candidates.push_back(std::sqrt(h / (16.0 * b_mag))); // gravity-wave
        candidates.push_back((h * h * rho0) / (8.0 * mu_eff));   // Fourier
        dt = *std::min_element(candidates.begin(), candidates.end());
        printf("new timestep: dt=%.9f\n", dt);
        return dt;
    }


private:
    double rho0, mu, Lref, vref;
    double c;
    double dt;
};
