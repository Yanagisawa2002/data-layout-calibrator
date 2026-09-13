// Copyright (c) 2026 Edwin Liu. All Rights Reserved.
// Independent adapter to the public Mono/IL2CPP profiler C ABIs. No runtime patching.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <limits>

namespace {
using ObjectSize = uint32_t(__cdecl*)(void*);
ObjectSize object_size = nullptr;
int initialized_backend = -1;
int profiler_owner = 0;
struct Window { bool active = false; bool overflow = false; uint64_t events = 0; uint64_t bytes = 0; };
thread_local Window window;

void record(void* object) noexcept {
    if (!window.active) return;
    const auto bytes = static_cast<uint64_t>(object_size(object));
    constexpr auto limit = static_cast<uint64_t>((std::numeric_limits<int64_t>::max)());
    if (window.events == limit || window.bytes > limit - bytes) { window.overflow = true; return; }
    ++window.events;
    window.bytes += bytes;
}
void __cdecl mono_allocation(void*, void* object) { record(object); }
void __cdecl il2cpp_allocation(void*, void* object, void*) { record(object); }

template<typename T> T symbol(HMODULE module, const char* name) {
    return reinterpret_cast<T>(GetProcAddress(module, name));
}
}

extern "C" __declspec(dllexport) int __cdecl dlc_allocation_initialize(int backend) {
    // Called once on the main thread during benchmark startup, before scenario creation.
    if (initialized_backend >= 0) return initialized_backend == backend ? 0 : -1;
    HMODULE module = GetModuleHandleW(backend == 0 ? L"mono-2.0-bdwgc.dll" : L"GameAssembly.dll");
    if (!module) return -2;
    if (backend != 0 && backend != 1) return -3;
    if (backend == 0) {
        const auto enable = symbol<int(__cdecl*)()>(module, "mono_profiler_enable_allocations");
        const auto create = symbol<void*(__cdecl*)(void*)>(module, "mono_profiler_create");
        const auto set_callback = symbol<void(__cdecl*)(void*, void(__cdecl*)(void*, void*))>(module, "mono_profiler_set_gc_allocation_callback");
        object_size = symbol<ObjectSize>(module, "mono_object_get_size");
        if (!enable || !create || !set_callback || !object_size) return -4;
        // Unity Boehm has no managed allocator stubs. The runtime also refuses late
        // enabling when managed allocator instrumentation would already be frozen.
        if (!enable()) return -5;
        void* handle = create(&profiler_owner);
        if (!handle) return -6;
        set_callback(handle, mono_allocation);
    } else {
        const auto install = symbol<void(__cdecl*)(void*, void(__cdecl*)(void*))>(module, "il2cpp_profiler_install");
        const auto set_callback = symbol<void(__cdecl*)(void(__cdecl*)(void*, void*, void*))>(module, "il2cpp_profiler_install_allocation");
        const auto set_events = symbol<void(__cdecl*)(int)>(module, "il2cpp_profiler_set_events");
        object_size = symbol<ObjectSize>(module, "il2cpp_object_get_size");
        if (!install || !set_callback || !set_events || !object_size) return -4;
        install(&profiler_owner, nullptr);
        set_callback(il2cpp_allocation);
        set_events(1 << 7); // IL2CPP_PROFILE_ALLOCATIONS, scoped to our newly installed profiler.
    }
    // Runtime profiler callbacks live until runtime shutdown. Pin this adapter for
    // the life of this Player process so a Unity plugin unload cannot dangle them.
    HMODULE self = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,
        reinterpret_cast<LPCWSTR>(&dlc_allocation_initialize), &self)) return -7;
    initialized_backend = backend;
    return 0;
}

extern "C" __declspec(dllexport) int __cdecl dlc_allocation_begin() {
    if (initialized_backend < 0 || window.active) return -1;
    window = Window{};
    window.active = true;
    return 0;
}

extern "C" __declspec(dllexport) int __cdecl dlc_allocation_end(int64_t* events, int64_t* bytes) {
    if (!window.active || !events || !bytes) return -1;
    window.active = false;
    if (window.overflow) return -2;
    *events = static_cast<int64_t>(window.events);
    *bytes = static_cast<int64_t>(window.bytes);
    return 0;
}
