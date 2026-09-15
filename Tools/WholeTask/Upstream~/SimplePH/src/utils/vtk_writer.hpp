#pragma once

#include <vector>
#include <string>
#include <optional>
#include <array>
#include <ostream>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <cmath>

#include "particle.hpp"

class VTKWriter
{
public:
    VTKWriter() = default;
    ~VTKWriter() = default;

    // write to output name and output folder
    static void write(const std::vector<Particle> &particles, int step, const std::string &output_name);
};
