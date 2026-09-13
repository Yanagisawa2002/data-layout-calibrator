// Instrumented Windows/OpenMP variant of BabelStream, NOT an original submission.
// Copyright 2015-16 Tom Deakin, Simon McIntosh-Smith, University of Bristol HPC.
// Upstream custom license/run rules: ../../Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/Upstream~/BabelStream/LICENSE
// Original kernels, driver loop, defaults and full checker are included unchanged.
#include <memory>
#include <cstdlib>
#include <malloc.h>
#include <fstream>
#include <filesystem>
#include <chrono>
#include <vector>
#include <stdexcept>
#define OMP
#include "Stream.h"
#include "OMPStream.h"
// Real Windows allocation/free pair; identical requested size and 2 MiB alignment.
static void* windows_aligned_alloc(size_t alignment, size_t size) {
    if (size % alignment) throw std::runtime_error("Upstream aligned allocation size constraint");
    void* p = _aligned_malloc(size, alignment);
    if (!p) throw std::bad_alloc();
    return p;
}
#define aligned_alloc windows_aligned_alloc
#define free _aligned_free
#include "omp/OMPStream.cpp"
#undef free
#undef aligned_alloc
#define main retained_upstream_main
#include "main.cpp"
#undef main

int main(int argc, char** argv) try {
    if (argc != 3 || std::string(argv[1]) != "--run") {
        std::cerr << "Explicit --run <new-output-prefix> required\n"; return 2;
    }
    std::string prefix = argv[2];
    if (std::filesystem::exists(prefix+".json") || std::filesystem::exists(prefix+".bin"))
        throw std::runtime_error("Output already exists");
    // Caller-owned export buffers are outside the layout-owned lifetime on BOTH sides.
    std::vector<double> outA(array_size), outB(array_size), outC(array_size);
    double sum = 0;
    std::vector<std::vector<double>> timings;
    double construct, init, exportMs, disposeMs;
    std::unique_ptr<Stream<double>> stream;
    auto lifeStart = std::chrono::steady_clock::now();
    construct = time([&]{stream = make_stream<double>(selection,array_size,deviceIndex,startA,startB,startC);})*1000;
    init = time([&]{stream->init_arrays(startA,startB,startC);})*1000;
    timings = run_all<double>(stream,sum);
    exportMs = time([&]{
        const double *a, *b, *c; stream->get_arrays(a,b,c);
        std::copy_n(a,array_size,outA.data()); std::copy_n(b,array_size,outB.data()); std::copy_n(c,array_size,outC.data());
    })*1000;
    disposeMs = time([&]{stream.reset();})*1000;
    double lifeMs = std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-lifeStart).count();
    // Raw complete output survives even a subsequent numerical failure.
    std::ofstream binary(prefix+".bin",std::ios::binary);
    for(auto* v : {&outA,&outB,&outC}) binary.write(reinterpret_cast<const char*>(v->data()),v->size()*sizeof(double));
    binary.close(); if (!binary) throw std::runtime_error("Complete output write failed");
    std::ofstream json(prefix+".json");
    json << std::setprecision(17) << "{\"workload\":\"BabelStream-Windows-OpenMP-variant\",\"count\":" << array_size
         << ",\"iterations\":" << num_times << ",\"threads\":" << omp_get_max_threads()
         << ",\"compiler\":\"MSVC " << _MSC_FULL_VER << "\",\"unit\":\"ms\",\"constructMs\":" << construct
         << ",\"initMs\":" << init << ",\"exportMs\":" << exportMs << ",\"disposeMs\":" << disposeMs
         << ",\"storageLifecycleMs\":" << lifeMs << ",\"sum\":" << sum << ",\"operationMs\":[";
    for (int i=0;i<5;i++) { json << (i?",":"") << '[';
        for (size_t k=0;k<num_times;k++) json << (k?",":"") << timings[i][k]*1000;
        json << ']'; }
    json << "]}\n"; json.close(); if(!json) throw std::runtime_error("Timing write failed");
    check_solution<double>(num_times,outA.data(),outB.data(),outC.data(),sum);
    std::cout << "Full upstream check passed: " << 3*array_size << " array elements and Dot. Output: " << prefix << "\n";
    return 0;
} catch(const std::exception& e) { std::cerr << e.what() << '\n'; return 1; }
