// Complete neighbor membership regression for the common periodic-cell fix.
#include <algorithm>
#include <cmath>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <vector>
#include <omp.h>
#include "particle.hpp"
#include "cell_grid.hpp"

int main() try {
    omp_set_dynamic(0); omp_set_num_threads(1);
    const double tiny=std::numeric_limits<double>::epsilon()*.01;
    std::vector<std::array<double,2>> positions={
        {-tiny,-tiny},{0.,0.},{tiny,tiny},{.05,0.},{-.05,0.},
        {0.,.05},{0.,-.05},{.05,.05},{-.05,-.05},
        {std::nextafter(.05,0.),-tiny},{std::nextafter(-.05,0.),tiny},
        {.027,-.032},{-.038,.023}};
    std::vector<Particle> canonical(positions.size());
    for(size_t i=0;i<positions.size();++i) canonical[i].x=positions[i];
    ParticleStorage particles; particles=canonical;
    CellGrid grid(.03,.1,.1);
    std::vector<std::vector<int>> neighbors;
    for(int reuse=0;reuse<2;++reuse) {
        grid.update_neighbors(particles,neighbors);
        if(neighbors.size()!=positions.size()) throw std::runtime_error("Grid count mismatch");
        for(size_t i=0;i<positions.size();++i) {
            std::vector<int> expected;
            for(size_t j=0;j<positions.size();++j) if(i!=j) {
                double dx=positions[j][0]-positions[i][0],dy=positions[j][1]-positions[i][1];
                dx-=.1*std::round(dx/.1); dy-=.1*std::round(dy/.1);
                if(std::sqrt(dx*dx+dy*dy)<=.03) expected.push_back(static_cast<int>(j));
            }
            auto actual=neighbors[i]; std::sort(actual.begin(),actual.end());
            if(actual!=expected) throw std::runtime_error("Periodic insertion/lookup membership or multiplicity mismatch");
        }
    }
    int rejected=0;
    for(double invalid : {std::numeric_limits<double>::quiet_NaN(),std::numeric_limits<double>::infinity()}) {
        canonical[0].x[0]=invalid; particles=canonical;
        try {grid.update_neighbors(particles,neighbors);} catch(const std::invalid_argument&) {++rejected;}
        try {CellGrid invalid_grid(.03,invalid,.1);} catch(const std::invalid_argument&) {++rejected;}
    }
    for(double outside : {std::nextafter(.05,1.),std::nextafter(-.05,-1.),.1,-.1}) {
        canonical[0].x[0]=outside; particles=canonical;
        try {grid.update_neighbors(particles,neighbors);} catch(const std::invalid_argument&) {++rejected;}
    }
    if(rejected!=8) throw std::runtime_error("Invalid coordinates/geometry were masked");
    std::cout<<"{\"passed\":true,\"particles\":13,\"reuseCases\":2,\"invalidCasesRejected\":"<<rejected<<",\"layoutKind\":"<<LAYOUT_KIND<<"}\n";
    return 0;
} catch(const std::exception& error) {std::cerr<<error.what()<<'\n';return 1;}
