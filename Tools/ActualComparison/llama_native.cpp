// Copyright 2024 Bernhard Manfred Gruber
// SPDX-License-Identifier: MPL-2.0
// Instrumentation/input-output adapter for the locked LLAMA code_comp examples.
// Included kernel bodies and their compile-time defaults are unmodified.
#include <random>
#include <span>
#include <vector>
#include <new>
#include <array>
#include <chrono>
#include <fstream>
#include <iostream>
#include <iomanip>
#include <string>
#include <cstdint>
#include <stdexcept>
#include <filesystem>
// The retained standalone mains explicitly call ::move. Preserve that dispatch
// when the three otherwise conflicting examples are namespaced in one adapter.
namespace Aos { struct Particle; }
namespace Block { struct ParticleBlock; }
void move(std::span<Aos::Particle> particles);
void move(std::span<Block::ParticleBlock> particles);
#define main upstream_main
namespace Aos {
#include "../../Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/Upstream~/llama/examples/nbody_code_comp/nbody-AoS-baseline.cpp"
}
namespace Soa {
#include "../../Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/Upstream~/llama/examples/nbody_code_comp/nbody-SoA.cpp"
}
namespace Block {
#include "../../Packages/com.yanagisawa.data-layout-calibrator/Samples/ExternalWorkloads/Upstream~/llama/examples/nbody_code_comp/nbody-AoSoA.cpp"
}
#undef main
void move(std::span<Aos::Particle> particles) { Aos::move(particles); }
void move(std::span<Block::ParticleBlock> particles) { Block::move(particles); }

using Clock = std::chrono::steady_clock;
struct Record { float x,y,z,vx,vy,vz,mass; };
static_assert(sizeof(Record) == 28);
constexpr int Count = Aos::problemSize, Steps = Aos::steps;
struct Timing { std::array<double,Steps> update{}, move{}, step{}; double lifecycle{}; };
double elapsed(Clock::time_point start) { return std::chrono::duration<double,std::milli>(Clock::now()-start).count(); }

template<class Update, class Move>
void steps(Timing& timing, Update update, Move move) {
    for(int s=0;s<Steps;s++) {
        auto start=Clock::now();
        update(); timing.update[s]=elapsed(start);
        auto mid=Clock::now(); move(); timing.move[s]=elapsed(mid);
        timing.step[s]=elapsed(start);
    }
}

Timing execute(const std::string& layout, const std::vector<Record>& input, std::vector<Record>& output) {
    Timing timing;
    auto start=Clock::now();
    if(layout=="AoS") {
        std::vector<Aos::Particle> particles(Count);
        for(int i=0;i<Count;i++) { const auto& r=input[i]; particles[i]={{r.x,r.y,r.z},{r.vx,r.vy,r.vz},r.mass}; }
        steps(timing,[&]{Aos::update(particles);},[&]{Aos::move(particles);});
        for(int i=0;i<Count;i++) { const auto& p=particles[i]; output[i]={p.pos.x,p.pos.y,p.pos.z,p.vel.x,p.vel.y,p.vel.z,p.mass}; }
    } else if(layout=="SoA") {
        using Vector=std::vector<float,Soa::AlignedAllocator<float>>;
        Vector x(Count),y(Count),z(Count),vx(Count),vy(Count),vz(Count),mass(Count);
        for(int i=0;i<Count;i++) { const auto& r=input[i]; x[i]=r.x;y[i]=r.y;z[i]=r.z;vx[i]=r.vx;vy[i]=r.vy;vz[i]=r.vz;mass[i]=r.mass; }
        steps(timing,[&]{Soa::update(x.data(),y.data(),z.data(),vx.data(),vy.data(),vz.data(),mass.data());},
                     [&]{Soa::move(x.data(),y.data(),z.data(),vx.data(),vy.data(),vz.data());});
        for(int i=0;i<Count;i++) output[i]={x[i],y[i],z[i],vx[i],vy[i],vz[i],mass[i]};
    } else if(layout=="AoSoA16") {
        std::vector<Block::ParticleBlock> particles(Block::blocks);
        for(int i=0;i<Count;i++) { auto& p=particles[i/Block::lanes]; int l=i%Block::lanes; const auto& r=input[i];
            p.pos.x[l]=r.x;p.pos.y[l]=r.y;p.pos.z[l]=r.z;p.vel.x[l]=r.vx;p.vel.y[l]=r.vy;p.vel.z[l]=r.vz;p.mass[l]=r.mass; }
        steps(timing,[&]{Block::update(particles);},[&]{Block::move(particles);});
        for(int i=0;i<Count;i++) { const auto& p=particles[i/Block::lanes]; int l=i%Block::lanes;
            output[i]={p.pos.x[l],p.pos.y[l],p.pos.z[l],p.vel.x[l],p.vel.y[l],p.vel.z[l],p.mass[l]}; }
    } else throw std::invalid_argument("Unknown frozen layout");
    // All layout-owned storage is destroyed above; canonical caller buffers and
    // file I/O are outside this construction/ingress/steps/export/disposal window.
    timing.lifecycle=elapsed(start);
    return timing;
}

void writeRecords(const std::filesystem::path& path, const std::vector<Record>& data) {
    if(std::filesystem::exists(path)) throw std::runtime_error("Output already exists");
    std::ofstream f(path,std::ios::binary); f.write(reinterpret_cast<const char*>(data.data()),data.size()*sizeof(Record));
    if(!f) throw std::runtime_error("Cannot persist complete canonical records");
}
void arrayJson(std::ostream& stream,const std::array<double,Steps>& values) {
    stream<<'['; for(int i=0;i<Steps;i++) stream<<(i?",":"")<<values[i]; stream<<']';
}

int main(int argc,char** argv) try {
    if(argc==4 && std::string(argv[1])=="--prepare") {
        unsigned seed=std::stoul(argv[3]);
        std::default_random_engine engine(seed);
        std::normal_distribution<float> dist(0.0f,1.0f);
        std::vector<Record> records(Count);
        for(auto& p:records) p={dist(engine),dist(engine),dist(engine),dist(engine)/10.0f,dist(engine)/10.0f,dist(engine)/10.0f,dist(engine)/100.0f};
        writeRecords(argv[2],records);
        std::cout<<"Prepared "<<Count<<" upstream-initialized records; seed="<<seed<<"; MSVC="<<_MSC_FULL_VER<<"; STL="<<_MSVC_STL_VERSION<<'.'<<_MSVC_STL_UPDATE<<'\n';
        return 0;
    }
    if(argc!=5 || std::string(argv[1])!="--run") {
        std::cerr<<"Explicit --prepare <file> <seed> or --run <input> <output-prefix> <AoS|SoA|AoSoA16> required\n";return 2;
    }
    std::ifstream file(argv[2],std::ios::binary);
    if(std::filesystem::file_size(argv[2])!=Count*sizeof(Record)) throw std::runtime_error("Requires exact upstream default record count");
    std::vector<Record> input(Count),output(Count);
    file.read(reinterpret_cast<char*>(input.data()),input.size()*sizeof(Record));
    if(!file) throw std::runtime_error("Incomplete canonical input");
    std::string prefix=argv[3],layout=argv[4];
    if(std::filesystem::exists(prefix+".json") || std::filesystem::exists(prefix+".bin")) throw std::runtime_error("Immutable output path reused");
    auto timing=execute(layout,input,output);
    writeRecords(prefix+".bin",output);
    std::ofstream result(prefix+".json");
    result<<std::setprecision(17)<<"{\n\"schema\":1,\"workload\":\"LLAMA-code_comp-instrumented-native\",\"layout\":\""<<layout
          <<"\",\"count\":"<<Count<<",\"steps\":"<<Steps<<",\"threads\":1,\"warmups\":0,\"compiler\":\"MSVC "<<_MSC_FULL_VER
          <<"\",\"stl\":\""<<_MSVC_STL_VERSION<<'.'<<_MSVC_STL_UPDATE<<"\",\"clock\":\"steady_clock\",\"unit\":\"ms\",\n\"updateMs\":";
    arrayJson(result,timing.update);result<<",\n\"moveMs\":";arrayJson(result,timing.move);
    result<<",\n\"wholeStepMs\":";arrayJson(result,timing.step);
    result<<",\n\"storageLifecycleMs\":"<<timing.lifecycle
          <<",\n\"lifecycleBoundary\":\"layout allocation+canonical ingress+5 original update/move steps+full export+layout disposal; excludes caller-owned canonical buffers, input generation, file I/O, checks and process startup\"\n}\n";
    if(!result) throw std::runtime_error("Cannot persist timing record");
    std::cout<<"Complete: "<<layout<<"; full output persisted; no numerical acceptance criterion claimed by upstream.\n";
    return 0;
} catch(const std::exception& e) { std::cerr<<e.what()<<'\n';return 1; }
