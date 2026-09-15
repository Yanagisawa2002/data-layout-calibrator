// Headless native host for the pinned public Poiseuille application. This is a
// direct C++ translation of its Python setup, calling the actual Solver::run
// and VTKWriter. The storage/solver sources are mechanically generated from the
// pinned upstream tree by prepare_sources.py. See the retained adaptation.patch.
#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>
#include <omp.h>
#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#include <psapi.h>
#pragma comment(lib, "psapi.lib")
#else
#include <sys/resource.h>
#endif
#include "solver.hpp"
#include "verlet_integrator.hpp"
#include "vtk_writer.hpp"
#include "task_trace.hpp"

using Clock = std::chrono::steady_clock;
double elapsed(Clock::time_point t) { return std::chrono::duration<double, std::milli>(Clock::now()-t).count(); }
const char* names[] = {"original-aos", "tuned-aos", "field-soa", "field-aosoa8", "llama-soa", "llama-aosoa8"};

void write_double(std::ostream& output, double value) {
    if (!std::isfinite(value)) throw std::runtime_error("Nonfinite canonical output");
    output.write(reinterpret_cast<const char*>(&value), sizeof(value));
}
template<class P> void canonical(std::ostream& output, const P& p) {
    for (auto v : p.x) write_double(output, v);
    for (auto v : p.v) write_double(output, v);
    write_double(output, p.rho); write_double(output, p.p); write_double(output, p.m);
    const uint32_t type = p.type;
    output.write(reinterpret_cast<const char*>(&type), sizeof(type));
    auto optional = [&](const auto& value) {
        const uint8_t present = value.has_value(); output.write(reinterpret_cast<const char*>(&present), 1);
        if (present) {
            if constexpr (std::is_same_v<std::decay_t<decltype(*value)>, double>) write_double(output, *value);
            else for (double v : *value) write_double(output, v);
        }
    };
    optional(p.drho_dt); optional(p.v_xsph); optional(p.tv); optional(p.bpc); optional(p.vf);
}

int main(int argc, char** argv) try {
    if (argc != 7) throw std::runtime_error("Expected: resolution steps export_every Reynolds x_phase_in_dx correctness|performance");
    const std::string mode = argv[6];
    if (mode != "correctness" && mode != "performance") throw std::runtime_error("Invalid task mode");
    const bool checkpoints = mode == "correctness";
    const int res = std::stoi(argv[1]), steps = std::stoi(argv[2]), every = std::stoi(argv[3]);
    const double re = std::stod(argv[4]), phase = std::stod(argv[5]);
    if (res < 12 || res > 256 || steps < 1 || every < 1 || !(re > 0) || !std::isfinite(re) ||
        !std::isfinite(phase) || std::abs(phase) > 0.5) throw std::runtime_error("Task outside supported contract");
    omp_set_dynamic(0); omp_set_num_threads(1);
    whole_task_ticks.reserve(steps);
    const double Lx = 0.1, Ly = 0.1, rho = 1000.0, mu = 1.0;
    const double dx = Ly/res, h = 1.7*dx, rcut = 2*h;
    const int rows = 2*static_cast<int>(std::ceil(rcut/dx)) + res;
    const int count = res*rows;
    const double bcLy = rows*dx, mass = rho*(dx*dx);
    const double body_x = (24.0*(mu*mu)*re)/((rho*rho)*std::pow(Ly, 3.0));
    const double vmax = (rho*body_x*((Ly/2)*(Ly/2)))/(2*mu);
    double input_ms, construct_ms, ingress_ms, run_ms, final_export_ms, disposal_ms, dt;
    const auto complete_start = Clock::now();
    auto start = Clock::now();
    std::vector<Particle> input; input.reserve(count);
    for (int i=0; i<res; ++i) for (int j=0; j<rows; ++j) {
        Particle p{};
        p.x = {-Lx/2+dx/2+i*dx+phase*dx, -bcLy/2+dx/2+j*dx};
        if (p.x[0] >= Lx/2) p.x[0] -= Lx;
        p.v = {0.0, 0.0}; p.m = mass; p.rho = rho; p.p = 0.0;
        p.type = (p.x[1] > Ly/2 || p.x[1] < -Ly/2) ? 1u : 0u;
        input.push_back(p);
    }
    if (checkpoints) {
        std::ofstream input_checkpoint("input.bin", std::ios::binary);
        for (const auto& p : input) canonical(input_checkpoint, p);
        input_checkpoint.close(); if (!input_checkpoint) throw std::runtime_error("Input checkpoint failed");
    }
    input_ms = elapsed(start);
    start = Clock::now();
    auto solver = std::make_unique<Solver>(h, Lx, bcLy, dx, Ly, vmax, KernelType::WendlandC4);
    solver->set_viscosity(mu); solver->set_density(rho, 0.01);
    solver->set_acceleration({body_x, 0.0}, 0); solver->compute_soundspeed_and_timestep();
    solver->set_eos(EOSType::Tait, 0.0); solver->set_integrator(std::make_shared<VerletIntegrator>());
    solver->set_density_method(DensityMethod::Summation); solver->set_output_name("frames");
    dt = solver->task_dt(); construct_ms = elapsed(start);
    start = Clock::now(); solver->set_particles(input); ingress_ms = elapsed(start);
    start = Clock::now(); solver->run(steps, every, 0); run_ms = elapsed(start);
    // Original run exports before each step, starting at zero. Add a terminal
    // snapshot in EVERY arm so a finite invocation delivers its final result.
    start = Clock::now(); VTKWriter::write(solver->get_particles(), steps, "frames");
    // Full-precision verification uses the SAME executable in a separate mode.
    // Its timings are never eligible for selection or performance inference.
    if (checkpoints) {
        std::ofstream checkpoint("state.bin", std::ios::binary);
        for (const auto& p : solver->get_particles()) canonical(checkpoint, p);
        checkpoint.close(); if (!checkpoint) throw std::runtime_error("Checkpoint failed");
    }
    final_export_ms = elapsed(start);
    start = Clock::now(); solver.reset(); input.clear(); input.shrink_to_fit(); disposal_ms = elapsed(start);
    const double complete_ms = elapsed(complete_start);
    uint64_t peak_rss = 0, peak_pagefile = 0;
    bool memory_ok = false, pagefile_ok = false;
#ifdef _WIN32
    PROCESS_MEMORY_COUNTERS memory{};
    memory_ok = GetProcessMemoryInfo(GetCurrentProcess(), &memory, sizeof(memory)) != 0;
    if (memory_ok) { peak_rss = memory.PeakWorkingSetSize; peak_pagefile = memory.PeakPagefileUsage; pagefile_ok = true; }
#else
    struct rusage usage{};
    memory_ok = getrusage(RUSAGE_SELF, &usage) == 0;
    if (memory_ok) peak_rss = static_cast<uint64_t>(usage.ru_maxrss) * 1024;
#endif
    // Result serialization is instrumentation outside the native lifecycle.
    std::ofstream result("native-result.json"); result << std::setprecision(17);
    result << "{\n\"candidate\":\"" << names[LAYOUT_KIND] << "\",\n\"count\":" << count
        << ",\n\"resolution\":" << res << ",\n\"steps\":" << steps << ",\n\"exportEvery\":" << every
        << ",\n\"re\":" << re << ",\n\"phase\":" << phase << ",\n\"dt\":" << dt
        << ",\n\"mode\":\"" << mode << "\""
        << ",\n\"particleSizeBytes\":" << sizeof(Particle) << ",\n\"threads\":1,\n\"diagnostic\":" << (TASK_PROFILE ? "true" : "false")
        << ",\n\"inputMs\":" << input_ms << ",\n\"constructionMs\":" << construct_ms
        << ",\n\"ingressMs\":" << ingress_ms << ",\n\"runMs\":" << run_ms
        << ",\n\"finalExportMs\":" << final_export_ms << ",\n\"disposalMs\":" << disposal_ms
        << ",\n\"nativeLifecycleMs\":" << complete_ms << ",\n\"ticksIncludingScheduledExportMs\":[";
    for (size_t i=0; i<whole_task_ticks.size(); ++i) { if (i) result << ','; result << whole_task_ticks[i]; }
    result << "],\n\"peakWorkingSetBytes\":";
    if (memory_ok) result << peak_rss; else result << "null";
    result << ",\n\"peakPagefileUsageBytes\":";
    if (pagefile_ok) result << peak_pagefile; else result << "null";
    result << ",\n\"diagnosticPhasesMs\":{";
    bool first = true; for (const auto& [name, ms] : whole_task_phases) {
        if (!first) result << ','; first=false; result << '"' << name << "\":" << ms;
    }
    result << "}}\n"; result.close(); if (!result) throw std::runtime_error("Result write failed");
    return 0;
} catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
