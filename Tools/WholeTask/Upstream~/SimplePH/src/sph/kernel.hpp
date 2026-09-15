#pragma once
#include <cmath>

using KernelRCut = double (*)(double h);
using KernelW = double (*)(double r, double h);
using KernelDW = double (*)(double r, double h);

// smoothing kernel types
enum class KernelType
{
    CubicSpline,
    QuinticSpline,
    WendlandC2,
    WendlandC4
};

// cubic spline
inline double W_cubic_rcut(double h)
{
    return 2.0 * h;
}

inline double W_cubic(double r, double h)
{
    double q = r / h;
    double W = 0.0;
    // alpha/h^2
    const double norm = 10.0 / (7.0 * M_PI * h * h);

    if (q >= 0.0 && q <= 1.0)
    {
        W = norm * (1.0 - 1.5 * q * q + 0.75 * q * q * q);
    }
    else if (q > 1.0 && q <= 2.0)
    {
        W = norm * 0.25 * (2.0 - q) * (2.0 - q) * (2.0 - q);
    }

    return W;
}

inline double dW_cubic(double r, double h)
{
    double q = r / h;
    double dW = 0.0;
    // alpha/h^3
    const double norm = 10.0 / (7.0 * M_PI * h * h * h);

    if (q >= 0.0 && q <= 1.0)
    {
        dW = -norm * (3.0 * q - 2.25 * q * q);
    }
    else if (q > 1.0 && q <= 2.0)
    {
        dW = -norm * (0.75 * (2.0 - q) * (2.0 - q));
    }

    return dW;
}

// quintic spline
inline double W_quintic_rcut(double h)
{
    return 3.0 * h;
}

inline double W_quintic(double r, double h)
{
    double q = r / h;
    double W = 0.0;
    // alpha/h^2
    const double norm = 7.0 / (478.0 * M_PI * h * h);

    if( q >= 0.0 && q <= 1.0 )
    {
        double temp0 = (3.0-q) * (3.0-q) * (3.0-q) * (3.0-q) * (3.0-q);
        double temp1 = (2.0-q) * (2.0-q) * (2.0-q) * (2.0-q) * (2.0-q);
        double temp2 = (1.0-q) * (1.0-q) * (1.0-q) * (1.0-q) * (1.0-q);
        W = norm * ( (temp0)- (6.0*temp1) + (15.0*temp2) );
    }
    else if ( q > 1.0 && q <= 2.0 )
    {
        double temp0 = (3.0-q) * (3.0-q) * (3.0-q) * (3.0-q) * (3.0-q);
        double temp1 = (2.0-q) * (2.0-q) * (2.0-q) * (2.0-q) * (2.0-q);
        W = norm * ( (temp0)- (6.0*temp1) );
    }
    else if ( q > 2.0 && q <= 3.0 )
    {
        double temp0 = (3.0-q) * (3.0-q) * (3.0-q) * (3.0-q) * (3.0-q);
        W = norm * ( (temp0) );
    }

    return W;
}

inline double dW_quintic(double r, double h)
{
    double q = r / h;
    double dW = 0.0;
    // alpha/h^3
    const double norm = 7.0 / (478.0 * M_PI * h * h * h);

    if( q >= 0.0 && q <= 1.0 )
    {
        double temp0 = (3.0-q) * (3.0-q) * (3.0-q) * (3.0-q);
        double temp1 = (2.0-q) * (2.0-q) * (2.0-q) * (2.0-q);
        double temp2 = (1.0-q) * (1.0-q) * (1.0-q) * (1.0-q);
        dW = -norm*( (5.0*temp0) - (30.0*temp1) + (75.0*temp2) );
    }
    else if ( q > 1.0 && q <= 2.0 )
    {
        double temp0 = (3.0-q) * (3.0-q) * (3.0-q) * (3.0-q);
        double temp1 = (2.0-q) * (2.0-q) * (2.0-q) * (2.0-q);
        dW = -norm*( (5.0*temp0) - (30.0*temp1) );

    }
    else if ( q > 2.0 && q <= 3.0 )
    {
        double temp0 = (3.0-q) * (3.0-q) * (3.0-q) * (3.0-q);
        dW = -norm*(5.0*temp0);
    }

    return dW;
}


// Wendland C2
inline double W_WendlandC2_rcut(double h)
{
    return 2.0 * h;
}

inline double W_WendlandC2(double r, double h)
{
    double q = r / h;
    double W = 0.0;
    // alpha/h^2
    const double norm = 7.0 / (4.0 * M_PI * h * h);

    if (q >= 0.0 && q <= 2.0)
    {
        W = norm * (1.0 - 0.5 * q) * (1.0 - 0.5 * q) * (1.0 - 0.5 * q) * (1.0 - 0.5 * q) * (1.0 + 2.0 * q);
    }

    return W;
}

inline double dW_WendlandC2(double r, double h)
{
    double q = r / h;
    double dW = 0.0;
    // alpha/h^3
    const double norm = 7.0 / (4.0 * M_PI * h * h * h);
    if (q >= 0.0 && q <= 2.0)
    {
        dW = -norm * (1.0 - 0.5 * q) * (1.0 - 0.5 * q) * (1.0 - 0.5 * q) * 5.0 * q;
    }

    return dW;
}

// Wendland C4
inline double W_WendlandC4_rcut(double h)
{
    return 2.0 * h;
}

inline double W_WendlandC4(double r, double h)
{
    double q = r / h;
    double W = 0.0;
    // alpha/h^2
    const double norm = 9.0 / (4.0 * M_PI * h * h);

    if (q >= 0.0 && q <= 2.0)
    {
        W = norm * (1.0 - 0.5 * q) * (1.0 - 0.5 * q) * (1.0 - 0.5 * q) * (1.0 - 0.5 * q) * (1.0 - 0.5 * q) * (1.0 - 0.5 * q) * ((35.0 / 12.0) * q * q + 3.0 * q + 1.0);
    }

    return W;
}

inline double dW_WendlandC4(double r, double h)
{
    double q = r / h;
    double dW = 0.0;
    // alpha/h^3
    const double norm = 9.0 / (4.0 * M_PI * h * h * h);
    if (q >= 0.0 && q <= 2.0)
    {
        dW = -norm * (1.0 - 0.5 * q) * (1.0 - 0.5 * q) * (1.0 - 0.5 * q) * (1.0 - 0.5 * q) * (1.0 - 0.5 * q) * ((35.0 / 3.0) * q * q + (14.0 / 3.0) * q);
    }

    return dW;
}

struct Kernel
{
    KernelRCut rcut;
    KernelW W;
    KernelDW dW;

    Kernel() = default; // empty

    Kernel(KernelType type, double h)
    {
        set(type, h);
    }

    void set(KernelType type, double h)
    {
        switch (type)
        {
        case KernelType::CubicSpline:
            rcut = &W_cubic_rcut;
            W = &W_cubic;
            dW = &dW_cubic;
            break;

        case KernelType::QuinticSpline:
            rcut = &W_quintic_rcut;
            W = &W_quintic;
            dW = &dW_quintic    ;
            break;

        case KernelType::WendlandC2:
            rcut = &W_WendlandC2_rcut;
            W = &W_WendlandC2;
            dW = &dW_WendlandC2;
            break;

        case KernelType::WendlandC4:
            rcut = &W_WendlandC4_rcut;
            W = &W_WendlandC4;
            dW = &dW_WendlandC4;
            break;
        }
    }

    // wrapper
    inline double get_rcut(double h) const { return rcut(h); }
    inline double getW(double r, double h) const { return W(r, h); }
    inline double getdW(double r, double h) const { return dW(r, h); }
};
