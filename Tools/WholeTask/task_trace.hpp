#pragma once
#include <chrono>
#include <map>
#include <string>
#include <vector>
inline std::vector<double> whole_task_ticks;
inline std::map<std::string, double> whole_task_phases;
#if TASK_PROFILE
struct TaskPhase {
    const char* name;
    std::chrono::steady_clock::time_point start = std::chrono::steady_clock::now();
    ~TaskPhase() {
        whole_task_phases[name] += std::chrono::duration<double, std::milli>(
            std::chrono::steady_clock::now() - start).count();
    }
};
#define TASK_PHASE(name, ...) { TaskPhase timer{name}; __VA_ARGS__ }
#else
#define TASK_PHASE(name, ...) { __VA_ARGS__ }
#endif
