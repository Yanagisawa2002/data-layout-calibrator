#!/usr/bin/env python3
"""
Plot velocity profile from Poiseuille flow VTU snapshots.
Compares simulation results with analytical solution.
"""

import vtk
import numpy as np
import argparse
import sys
import pathlib
from matplotlib.ticker import ScalarFormatter
import matplotlib.pyplot as plt

def load_vtu(filename='example.vtu', dx=0.0):
    """Load particle velocity data from VTU file."""
    reader = vtk.vtkXMLUnstructuredGridReader()
    reader.SetFileName(filename)
    reader.Update()
    
    output = reader.GetOutput()
    point_data = output.GetPointData()
    
    p_array = point_data.GetArray("p")
    type_array = point_data.GetArray("type")
    v_array = point_data.GetArray("v")
    
    if p_array is None or type_array is None or v_array is None:
        raise RuntimeError("VTU missing required fields: p, type, v")
    
    p = np.array([p_array.GetValue(i) for i in range(p_array.GetNumberOfTuples())])
    type_id = np.array([type_array.GetValue(i) for i in range(type_array.GetNumberOfTuples())])
    
    n_points = v_array.GetNumberOfTuples()
    u = np.zeros(n_points)
    v = np.zeros(n_points)
    for i in range(n_points):
        val = v_array.GetTuple3(i)
        u[i] = val[0]
        v[i] = val[1]
    
    points = output.GetPoints()
    pos_array = np.array([points.GetPoint(i) for i in range(points.GetNumberOfPoints())])
    x = pos_array[:, 0]
    y = pos_array[:, 1]

    data_points_y = []
    data_vels_x = []
    for i in range(n_points):
        data_points_y.append(y[i])
        data_vels_x.append(u[i])

    return data_points_y, data_vels_x

def get_timesteps(Re):
    if Re == 0.1:
        ts = [5000, 1000, 500, 200]
    elif Re == 10.0:
        ts = [20000, 5000, 2500, 500]
    else:
        raise ValueError(f"Time steps not available for Re = {Re}")

    return ts

def get_dt(Re):
    if Re == 0.1:
        dt = 0.002257813
    elif Re == 10.0:
        dt = 0.000354167
    else:
        raise ValueError(f"dt not available for Re = {Re}")
    return dt

def main():
    parser = argparse.ArgumentParser(description='Plot Poiseuille flow velocity profile')
    parser.add_argument('-Re', type=float, default=0.1, help='Reynolds number')
    parser.add_argument('-res', type=int, default=40, help='Resolution')
    parser.add_argument('-vtu_dir', default=None, help='VTU directory (optional override)')
    parser.add_argument('-out', default=None, help='Output filename (optional override)')
    args = parser.parse_args()

    # Enable LaTeX rendering
    plt.rcParams.update({
        "text.usetex": True,
        "font.family": "serif",
        "font.serif": ["Computer Modern"],
    })
    plt.rcParams.update({'font.size': 10})

    # Physical parameters (must match run_poiseuille_flow.py)
    lref = 0.1                  # [m]
    rho0 = 1000.0               # [kg/m**3]
    viscosity = 1.0             # [Pa s]
    res = args.res
    dx = lref / res
    Re = args.Re

    # Calculate body force for given Re
    fx = (24.0 * viscosity**2 * Re) / (rho0**2 * lref**3)
    print('fx: ', fx)
    vmax = (rho0 * fx * (lref/2)**2) / (2 * viscosity)


    # Output figure
    fig_width_cm = 8.0
    fig_height_cm = fig_width_cm / 1.4
    fig_width_inches = fig_width_cm / 2.54
    fig_height_inches = fig_height_cm / 2.54

    fig, ax = plt.subplots(1, 1, figsize=(fig_width_inches, fig_height_inches))

    # Default VTU directory
    if args.vtu_dir is None:
        vtu_dir = pathlib.Path(f"poiseuille_flow_re{Re}_res{res}")
    else:
        vtu_dir = pathlib.Path(args.vtu_dir)

    # Default output name
    if args.out is None:
        out_file = f"poiseuille_flow_re{Re}_res{res}.png"
    else:
        out_file = args.out

    # Get timesteps (matches VTU filenames)
    ts = get_timesteps(Re)

    # Load VTU file at timestep ts
    sel_filenames = []
    for t in ts:
        filename = (
            vtu_dir /
            f"poiseuille_flow_re{Re}_res{res}_{t:05d}.vtu"
        )
        if filename.exists():
            sel_filenames.append(filename)
        else:
            raise FileNotFoundError(f"VTU file not found: {filename}")


    dt = get_dt(Re)

    t_times = [t * dt for t in ts]

    colors = ['red', 'green', 'blue', 'orange']
    labels = [fr'$t = {t:.2f}$s' for t in t_times]

    plt.grid(True, zorder=0)

    # Analytical solution: centered coordinate system (-lref/2 to +lref/2)
    xaxis = np.linspace(-lref/2, lref/2, num=200)
    vel_ana_dict = {}

    for t_idx, t_val in enumerate(t_times):
        vel_ana = []
        for i in range(xaxis.shape[0]):
            # Poiseuille profile: v(x) = (rho*fx)/(2*mu) * x * (x - lref)
            # with x in [-lref/2, lref/2]
            vel_temp = (rho0 * fx) / (2.0 * viscosity) * (xaxis[i] + lref/2) * (xaxis[i] - lref/2)
            
            # Add transient Fourier series
            series = np.linspace(0, 99, num=100)
            vel_temp_sum = 0.0
            for n in range(len(series)):
                vel_temp_sum += (4 * fx * rho0 * lref**2) / (viscosity * np.pi**3 * (2*series[n]+1)**3) \
                    * np.sin(((np.pi * (xaxis[i] + lref/2)) / lref) * (2*series[n]+1)) \
                    * np.exp(-(((2*series[n]+1)**2 * np.pi**2 * viscosity) / (rho0 * lref**2)) * t_val)
            
            vel_temp = vel_temp + vel_temp_sum
            vel_ana.append(vel_temp)
        
        vel_ana = np.asarray(vel_ana)
        vel_ana = -vel_ana  # Negate for flow direction
        vel_ana_dict[t_idx] = vel_ana

        ax.plot(vel_ana, xaxis, color=colors[t_idx], linewidth=0.8, zorder=5)

    # Plot simulation results
    for i, filename in enumerate(sel_filenames):
        if i < len(labels):
            points_y_sim, vel_x_sim = load_vtu(str(filename), dx)
            points_y_sim = np.asarray(points_y_sim)
            points_y_sim = points_y_sim
            ax.scatter(vel_x_sim, points_y_sim, s=7.0, color=colors[i], label=labels[i], zorder=5)

    ax.set_ylim([-lref/2, lref/2])
    ax.set_xlim([0.0, vmax*(4/3)])

    y_ticks = np.linspace(-lref/2, lref/2, 5)
    x_ticks = np.linspace(0.0, vmax*(4/3), 5)
    ax.set_yticks(y_ticks)
    ax.set_xticks(x_ticks)

    ax.set_ylabel(r'$\mathrm{y} \,[\mathrm{m}]$', fontsize=10)
    ax.set_xlabel(r'$\mathrm{v}_{\mathrm{x}}\,[\mathrm{m}/\mathrm{s}]$', fontsize=10)

    formatterx = ScalarFormatter(useMathText=True)
    formatterx.set_powerlimits((2, 3))
    formatterx.set_scientific(True)
    formattery = ScalarFormatter(useMathText=True)
    formattery.set_powerlimits((0, 0))
    formattery.set_scientific(True)
    ax.yaxis.set_major_formatter(formattery)
    ax.xaxis.set_major_formatter(formatterx)

    ax.legend(fontsize=6, fancybox=False)

    # plt.show()
    plt.savefig(f'{out_file}',  dpi=300, bbox_inches='tight', pad_inches=0.02)
    print(f"Saved plot to {out_file}")


if __name__ == '__main__':
    main()
