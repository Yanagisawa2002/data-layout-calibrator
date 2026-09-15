// Full-field/tail contract independent of the SPH math or VTU's decimal precision.
#include <array>
#include <cmath>
#include <iostream>
#include <optional>
#include <stdexcept>
#include <vector>
#include "particle.hpp"

template<class P> void equal(const Particle& a, const P& b) {
    if (a.x != b.x || a.v != b.v || a.rho != b.rho || a.p != b.p || a.m != b.m || a.type != b.type ||
        a.drho_dt != b.drho_dt || a.v_xsph != b.v_xsph || a.tv != b.tv || a.bpc != b.bpc || a.vf != b.vf)
        throw std::runtime_error("Complete field parity failed");
}
int main() try {
    int cases = 0;
    for (size_t n : {0u, 1u, 7u, 8u, 9u, 15u, 16u, 17u, 273u, 425u}) {
        std::vector<Particle> source(n);
        for (size_t i=0; i<n; ++i) {
            auto& p=source[i]; double d=static_cast<double>(i);
            p.x={d+.25, -d}; p.v={d*.125, d*.375}; p.rho=1000+d; p.p=-d*4; p.m=d+1; p.type=i%3;
            if (i%2) p.drho_dt=d+.5;
            if (i%3) p.v_xsph={d+.625, -d-.375};
            if (i%4) p.tv={d+.75, d};
            if (i%5) p.bpc={-d, d+.125};
            if (i%6) p.vf={d+.25, -d-.125};
        }
        ParticleStorage storage;
        storage=source;
        if(storage.size()!=n) throw std::runtime_error("Count mismatch");
        const auto& read=storage;
        for (size_t i=0; i<n; ++i) equal(source[i], read[i]);
        for (size_t i=0; i<n; ++i) {
            auto&& p=storage[i]; p.x[0]+=.125; p.rho+=.25; p.drho_dt=3.25;
            p.v_xsph=std::nullopt; p.tv={.5, -.5}; p.bpc=std::nullopt; p.vf={1., 2.};
            source[i].x[0]+=.125; source[i].rho+=.25; source[i].drho_dt=3.25;
            source[i].v_xsph=std::nullopt; source[i].tv={.5,-.5}; source[i].bpc=std::nullopt; source[i].vf={1.,2.};
        }
        size_t i=0; for (const auto& p : read) equal(source[i++],p);
        if(i!=n) throw std::runtime_error("Logical tail leaked");
        storage=std::vector<Particle>{};
        if(storage.size()!=0) throw std::runtime_error("Reset failed");
        storage=source;
        for(size_t k=0;k<n;++k) equal(source[k],read[k]);
        ++cases;
    }
    std::cout << "{\"passed\":true,\"countCases\":" << cases << ",\"layoutKind\":" << LAYOUT_KIND << "}\n";
    return 0;
} catch (const std::exception& e) { std::cerr << e.what() << '\n'; return 1; }
