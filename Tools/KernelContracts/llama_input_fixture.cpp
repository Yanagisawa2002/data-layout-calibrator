// Copyright 2024 Bernhard Manfred Gruber
// SPDX-License-Identifier: MPL-2.0
// Input-preparation derivative of the pinned LLAMA code_comp main's RNG only.
// No update/move kernel, workload repeat loop, clock or upstream main is linked.
#include <bit>
#include <cstdint>
#include <fstream>
#include <iostream>
#include <random>
#include <string>

int main(int argc, char** argv)
{
    if(argc != 3 || std::string(argv[1]) != "--prepare-17-records")
    {
        std::cerr << "Refused. Only --prepare-17-records <output.json> input preparation is available.\n";
        return 2;
    }
    std::ofstream output(argv[2]);
    if(!output) return 1;
    output << "{\n  \"kind\": \"canonical-input-prefix-not-benchmark\",\n"
           << "  \"upstreamCommit\": \"086e66e7565f677d6b3aff88542e28c7dd6d8228\",\n"
           << "  \"compiler\": \"MSVC " << _MSC_FULL_VER << "\",\n"
           << "  \"standardLibrary\": \"MSVC STL " << _MSVC_STL_VERSION << "." << _MSVC_STL_UPDATE << "\",\n"
           << "  \"encoding\": \"uint32 IEEE754 bits; pos.xyz, vel.xyz, mass\",\n  \"records\": [\n";
    using FP = float;
    std::default_random_engine engine;
    std::normal_distribution<FP> dist(FP{0}, FP{1});
    for(int i = 0; i < 17; ++i)
    {
        // Preserve the exact call order and division from the upstream initializer.
        const FP values[] = {dist(engine), dist(engine), dist(engine), dist(engine) / FP{10},
            dist(engine) / FP{10}, dist(engine) / FP{10}, dist(engine) / FP{100}};
        output << "    [";
        for(int field = 0; field < 7; ++field)
            output << (field ? ", " : "") << std::bit_cast<std::uint32_t>(values[field]);
        output << "]" << (i == 16 ? "\n" : ",\n");
    }
    output << "  ]\n}\n";
    return output ? 0 : 1;
}
