#!/usr/bin/env python3
"""
Plot velocity profile from Lid Driven Cavity (LDC) VTU snapshots.
Compares simulation results with experimental solution of Ghia et al. 
[U. Ghia, K. Ghia, C. Shin, High-Re solutions for incompressible ﬂow using the Navier–Stokes equations and a multigrid method,
Journal of Computational Physics 48 (3) (1982) 387–411.]
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

    data_points_x = []
    data_points_y = []
    data_vels_x = []
    data_vels_y = []
    pressure = []
    data_type = []
    for i in range(n_points):
        if type_id[i] == 0 and x[i] < dx/2.0 and x[i] > -dx/2.0:
            data_points_y.append(y[i])
            data_vels_x.append(u[i])
        if type_id[i] == 0 and y[i] < dx/2.0 and y[i] > -dx/2.0:
            data_points_x.append(x[i])
            data_vels_y.append(v[i])

    return data_points_x, data_points_y, data_vels_x, data_vels_y, pressure, data_type

import numpy as np

def ghia_ldc_profiles(Re):
    """
    Ghia et al. (1982) lid-driven cavity benchmark data.
    Exact tabulated values.

    Returns:
        x_v, v : v(x) at y = 0.5
        y_u, u : u(y) at x = 0.5
    """
    Re = int(round(Re))

    if Re == 100:
        # u-velocity along vertical centerline, x = 0.5
        y = np.array([1.0000, 0.9766, 0.9688, 0.9609, 0.9531, 0.8516, 0.7344,
            0.6172, 0.5000, 0.4531, 0.2813, 0.1719, 0.1016, 0.0703,
            0.0625, 0.0547, 0.0000])

        v_x = [1.0000, 0.8412, 0.7887, 0.7372, 0.6872, 0.2315, 0.0033,
            -0.1364, -0.2058, -0.2109, -0.1566, -0.1015, -0.06434, -0.04775,
            -0.04192, -0.03717, 0.0000]

        # v-velocity along horizontal centerline, y = 0.5
        x = np.array([1.0000, 0.9688, 0.9609, 0.9531, 0.9453, 0.9063, 0.8594,
            0.8047, 0.5000, 0.2344, 0.2266, 0.1563, 0.0938, 0.0781,
            0.0703, 0.0625, 0.0000])

        v_y = [0.0000, -0.05906, -0.07391, -0.08864, -0.10313, -0.16914,
            -0.22445, -0.24533, 0.05454, 0.17527, 0.17507, 0.16077,
            0.12317, 0.10890, 0.10091, 0.09233, 0.0000]

    elif Re == 1000:
        # u-velocity along vertical centerline, x = 0.5
        y = np.array([1.0000, 0.9766, 0.9688, 0.9609, 0.9531, 0.8516, 0.7344,
            0.6172, 0.5000, 0.4531, 0.2813, 0.1719, 0.1016, 0.0703,
            0.0625, 0.0547, 0.0000])

        v_x = np.array([1.0000, 0.6593, 0.5749, 0.5112, 0.4660, 0.3330, 0.1872,
            0.05702, -0.06080, -0.10648, -0.27805, -0.38289, -0.29730,
            -0.22220, -0.20196, -0.18109, 0.0000])

        # v-velocity along horizontal centerline, y = 0.5
        x = np.array([1.0000, 0.9688, 0.9609, 0.9531, 0.9453, 0.9063, 0.8594,
            0.8047, 0.5000, 0.2344, 0.2266, 0.1563, 0.0938, 0.0781,
            0.0703, 0.0625, 0.0000])

        v_y = np.array([0.0000, -0.21388, -0.27669, -0.33714, -0.39188, -0.51500,
            -0.42665, -0.31966, 0.02526, 0.32235, 0.33075, 0.37095,
            0.32627, 0.30353, 0.29012, 0.27485, 0.0000])

    elif Re == 10000:
        # u-velocity along vertical centerline, x = 0.5
        y = np.array([1.0000, 0.9766, 0.9688, 0.9609, 0.9531, 0.8516, 0.7344,
            0.6172, 0.5000, 0.4531, 0.2813, 0.1719, 0.1016, 0.0703,
            0.0625, 0.0547, 0.0000])

        v_x = [1.0000, 0.47221, 0.47783, 0.48070, 0.47804, 0.34635, 0.20673,
            0.08344, 0.03111, -0.07540, -0.23186, -0.32709, -0.38000,
            -0.41657, -0.42537, -0.42735, 0.0000]

        # v-velocity along horizontal centerline, y = 0.5
        x = np.array([1.0000, 0.9688, 0.9609, 0.9531, 0.9453, 0.9063, 0.8594,
            0.8047, 0.5000, 0.2344, 0.2266, 0.1563, 0.0938, 0.0781,
            0.0703, 0.0625, 0.0000])

        v_y = [0.0000, -0.54302, -0.52987, -0.49099, -0.45863, -0.41496,
            -0.36737, -0.30719, 0.00831, 0.27224, 0.28003, 0.35070,
            0.41487, 0.43124, 0.43733, 0.43983, 0.0000]


    else:
        raise ValueError(f"Ghia data not available for Re = {Re}")

    return y, v_x, x, v_y


def main():
    parser = argparse.ArgumentParser(description='Plot LDC velocity field')
    parser.add_argument('--re', type=float, default=1000.0, help='Reynolds number')
    parser.add_argument('--res', type=int, default=50, help='Resolution')
    parser.add_argument('--ts', type=float, default=20000, help='Simulation timestep')
    parser.add_argument('--vtu_dir', default=None, help='VTU directory (optional override)')
    parser.add_argument('--out', default=None, help='Output filename (optional override)')
    args = parser.parse_args()

    # Enable LaTeX rendering
    plt.rcParams.update({
        "text.usetex": True,
        "font.family": "serif",
        "font.serif": ["Computer Modern"],
    })
    plt.rcParams.update({'font.size': 10})

    # Simulation parameters
    L = 1.0
    vlid = 1.0
    Re = args.re
    res = args.res
    dx = L/res

    # Default VTU directory
    if args.vtu_dir is None:
        vtu_dir = pathlib.Path(f"lid_driven_cavity_tv_re{Re}_res{res}")
    else:
        vtu_dir = pathlib.Path(args.vtu_dir)

    # Default output name
    if args.out is None:
        out_file = f"ldc_velocity_profile_re{Re}_res{res}.png"
    else:
        out_file = args.out

    # Load VTU file at timestep ts
    ts = int(args.ts)
    filename = (
        vtu_dir /
        f"lid_driven_cavity_tv_re{Re}_res{res}_{ts}.vtu"
    )
    if not filename.exists():
        raise FileNotFoundError(f"VTU file not found: {filename}")
    points_x_sim, points_y_sim, vel_x_sim, vel_y_sim, pressure_sim, tid_part = \
        load_vtu(str(filename), dx)

    # Output figure
    fig_width_cm = 8.0
    fig_height_cm = fig_width_cm
    fig_width = fig_width_cm/2.54
    fig_height = fig_height_cm/2.54
    fig_size = [fig_width,fig_height]


    fig, ax = plt.subplots(figsize=(fig_width, fig_height), constrained_layout=True)
    ax.grid(True, zorder=0)

    # Get Ghia et al. data
    y, v_x, x, v_y = ghia_ldc_profiles(Re)

    # shift coordinates: [0,1] -> [-0.5, 0.5]
    x -= 0.5
    y -= 0.5

    ax.set_xlabel(r'$\mathrm{x}\,[\mathrm{m}]$')
    ax.set_ylabel(r'$\mathrm{y}\,[\mathrm{m}]$')

    ax.set_xlim([-0.5, 0.5])
    ax.set_ylim([-0.5, 0.5])
    ax.set_xticks(np.linspace(-0.5, 0.5, 5))
    ax.set_yticks(np.linspace(-0.5, 0.5, 5))

    ax_right = ax.twinx()
    ax_right.scatter(points_x_sim, vel_y_sim, s=7, color='blue',
                label=r'SPH: $\mathrm{v}_\mathrm{y}(\mathrm{x})$', zorder=10)
    ax_right.plot(x, v_y, '--s', color='black', markersize=3,
            label=r'Ghia: $\mathrm{v}_\mathrm{y}(\mathrm{x})$')

    ax_right.set_ylabel(r'$\mathrm{v}_\mathrm{y}\,[\mathrm{m/s}]$')
    ax_right.set_ylim([-0.6, 0.6])

    ax_top = ax.twiny()
    ax_top.scatter(vel_x_sim, points_y_sim, s=7, color='red',
                label=r'SPH: $\mathrm{v}_\mathrm{x}(\mathrm{y})$', zorder=10)
    ax_top.plot(v_x, y, '--o', color='gray', markersize=3,
                label=r'Ghia: $\mathrm{v}_\mathrm{x}(\mathrm{y})$')

    ax_top.set_xlabel(r'$\mathrm{v}_\mathrm{x}\,[\mathrm{m/s}]$')
    ax_top.set_xlim([-0.6, 1.0])

    # get handels of labels
    handles_ax, labels_ax = ax.get_legend_handles_labels()
    handles_top, labels_top = ax_top.get_legend_handles_labels()
    handles_right, labels_right = ax_right.get_legend_handles_labels()

    # merge labels
    handles = handles_ax + handles_top + handles_right
    labels  = labels_ax  + labels_top + labels_right

    # legend
    leg = ax.legend(handles, labels, fontsize=6, loc='upper left', fancybox=False)
    leg.set_zorder(20)

    plt.savefig(f'{out_file}', dpi=300, bbox_inches='tight', pad_inches=0.02)
    print(f"Saved plot to {out_file}")

if __name__ == '__main__':
    main()
