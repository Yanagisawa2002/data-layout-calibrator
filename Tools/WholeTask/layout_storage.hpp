#pragma once
// Native adoption bridge for SimplePH. Exact upstream field types (including
// optional engagement) are preserved; x/v stay two-double leaf fields in all
// layouts. Math, neighbor order, and threading live in the common solver source.
#include <array>
#include <optional>
#include <type_traits>
#include <vector>

#if LAYOUT_KIND <= 1
using ParticleStorage = std::vector<Particle>;
#else

template<bool Const> struct ParticleReference {
    template<class T> using Ref = std::conditional_t<Const, const T&, T&>;
    Ref<std::array<double, 2>> x, v;
    Ref<double> rho, p, m;
    Ref<unsigned int> type;
    Ref<std::optional<double>> drho_dt;
    Ref<std::optional<std::array<double, 2>>> v_xsph, tv, bpc, vf;
};

#if LAYOUT_KIND == 2
struct LayoutData {
    std::vector<std::array<double, 2>> x, v;
    std::vector<double> rho, p, m;
    std::vector<unsigned int> type;
    std::vector<std::optional<double>> drho_dt;
    std::vector<std::optional<std::array<double, 2>>> v_xsph, tv, bpc, vf;
    explicit LayoutData(size_t n) : x(n), v(n), rho(n), p(n), m(n), type(n),
        drho_dt(n), v_xsph(n), tv(n), bpc(n), vf(n) {}
    template<bool C, class Self> static ParticleReference<C> at(Self& s, size_t i) {
        return {s.x[i], s.v[i], s.rho[i], s.p[i], s.m[i], s.type[i],
                s.drho_dt[i], s.v_xsph[i], s.tv[i], s.bpc[i], s.vf[i]};
    }
};
#elif LAYOUT_KIND == 3
struct ParticleBlock {
    std::array<double, 2> x[8], v[8];
    double rho[8], p[8], m[8];
    unsigned int type[8];
    std::optional<double> drho_dt[8];
    std::optional<std::array<double, 2>> v_xsph[8], tv[8], bpc[8], vf[8];
};
struct LayoutData {
    std::vector<ParticleBlock> blocks;
    explicit LayoutData(size_t n) : blocks((n + 7) / 8) {}
    template<bool C, class Self> static ParticleReference<C> at(Self& s, size_t i) {
        auto& b = s.blocks[i / 8]; const auto k = i % 8;
        return {b.x[k], b.v[k], b.rho[k], b.p[k], b.m[k], b.type[k],
                b.drho_dt[k], b.v_xsph[k], b.tv[k], b.bpc[k], b.vf[k]};
    }
};
#else
#include <llama/View.hpp>
#include <llama/RecordRef.hpp>
#include <llama/mapping/SoA.hpp>
#include <llama/mapping/AoSoA.hpp>
struct X {}; struct V {}; struct Rho {}; struct P {}; struct M {}; struct Type {};
struct DRho {}; struct Xsph {}; struct TV {}; struct BPC {}; struct VF {};
using Record = llama::Record<llama::Field<X, std::array<double, 2>>,
    llama::Field<V, std::array<double, 2>>, llama::Field<Rho, double>,
    llama::Field<P, double>, llama::Field<M, double>, llama::Field<Type, unsigned int>,
    llama::Field<DRho, std::optional<double>>,
    llama::Field<Xsph, std::optional<std::array<double, 2>>>,
    llama::Field<TV, std::optional<std::array<double, 2>>>,
    llama::Field<BPC, std::optional<std::array<double, 2>>>,
    llama::Field<VF, std::optional<std::array<double, 2>>>>;
using Extents = llama::ArrayExtentsDynamic<size_t, 1>;
#if LAYOUT_KIND == 4
using Mapping = llama::mapping::MultiBlobSoA<Extents, Record>;
#else
using Mapping = llama::mapping::AoSoA<Extents, Record, 8>;
#endif
struct LayoutData {
    decltype(llama::allocView(Mapping{Extents{0}})) view;
    explicit LayoutData(size_t n) : view(llama::allocView(Mapping{Extents{n}})) {}
    template<bool C, class Self> static ParticleReference<C> at(Self& s, size_t i) {
        auto r = s.view(i);
        return {r(X{}), r(V{}), r(Rho{}), r(P{}), r(M{}), r(Type{}),
                r(DRho{}), r(Xsph{}), r(TV{}), r(BPC{}), r(VF{})};
    }
};
#endif

class ParticleStorage {
    size_t count_ = 0;
    LayoutData data_{0};
    template<bool C> struct Iterator {
        using Owner = std::conditional_t<C, const ParticleStorage, ParticleStorage>;
        Owner* owner;
        size_t index;
        auto operator*() const { return (*owner)[index]; }
        Iterator& operator++() { ++index; return *this; }
        bool operator!=(const Iterator& r) const { return index != r.index; }
    };
public:
    size_t size() const { return count_; }
    auto operator[](size_t i) { return LayoutData::at<false>(data_, i); }
    auto operator[](size_t i) const { return LayoutData::at<true>(data_, i); }
    ParticleStorage& operator=(const std::vector<Particle>& input) {
        LayoutData replacement(input.size());
        for (size_t i = 0; i < input.size(); ++i) {
            auto to = LayoutData::at<false>(replacement, i); const auto& from = input[i];
            to.x = from.x; to.v = from.v; to.rho = from.rho; to.p = from.p;
            to.m = from.m; to.type = from.type; to.drho_dt = from.drho_dt;
            to.v_xsph = from.v_xsph; to.tv = from.tv; to.bpc = from.bpc; to.vf = from.vf;
        }
        data_ = std::move(replacement); count_ = input.size(); return *this;
    }
    auto begin() { return Iterator<false>{this, 0}; }
    auto end() { return Iterator<false>{this, count_}; }
    auto begin() const { return Iterator<true>{this, 0}; }
    auto end() const { return Iterator<true>{this, count_}; }
};
#endif
