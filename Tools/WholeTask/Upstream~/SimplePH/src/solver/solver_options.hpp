#pragma once

// solver options
struct SolverOptions {
    // articifial viscosity flag and alpha
    bool use_artificial_viscosity = false;
    double alpha = 1.0;

    // tensile instability correction flag and epsilon
    bool use_tensile_instability_correction = false;
    double epsilon = 0.0;

        // xsph filter flag and pre-factor
    bool use_xsph_filter = false;
    double eta = 0.0;

    // negative pressure truncation flag
    bool use_negative_pressure_truncation = false;

        // transport velocity approach flag
    bool use_transport_velocity = false;
};