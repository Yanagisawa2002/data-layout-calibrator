#!/usr/bin/env python3
"""
Plot velocity field from Lid Driven Cavity (LDC) VTU snapshots.
"""

import vtk
import numpy as np
from scipy.interpolate import griddata
import argparse
import sys
import pathlib
from matplotlib import rc
import matplotlib.colors as mcolors
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
        data_points_x.append(x[i])
        data_points_y.append(y[i])
        data_vels_x.append(u[i])
        data_vels_y.append(v[i])
        pressure.append(p[i])
        data_type.append(type_id[i])

    return data_points_x, data_points_y, data_vels_x, data_vels_y, pressure, data_type

def resulting_velocity(vel_x, vel_y):
	vel_res = np.zeros(len(vel_x))
	for i in range(len(vel_x)):
		vel_res[i] = np.sqrt(vel_x[i]**2+vel_y[i]**2)
	return vel_res

def main():
    parser = argparse.ArgumentParser(description='Plot LDC velocity field')
    parser.add_argument('--re', type=float, default=1000.0, help='Reynolds number')
    parser.add_argument('--res', type=int, default=50, help='Resolution')
    parser.add_argument('--ts', type=float, default=10000, help='Simulation timestep')
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
        out_file = f"ldc_velocity_field_re{Re}_res{res}.png"
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
    fig_size      = [fig_width,fig_height]

    # Plot parameters
    interpolation_method = 'linear'
    interpolation_res = 100
    fig, ax = plt.subplots(1,1,figsize=(fig_width, fig_height), constrained_layout=False)

    # Create modified RdYlBu_r colormap
    cmap = plt.cm.RdYlBu_r
    gamma = 3.0  # Adjust this parameter to control the divergence of colors
    new_cmap = mcolors.PowerNorm(gamma)(cmap(np.linspace(0, 1, 256)))


    # Adjusted standardization for lower and upper limits
    vel_min, vel_max = 0, 1.0  # Upper and lower boundaries
    norm = mcolors.Normalize(vmin=vel_min, vmax=vel_max)  # Consistent color mapping

    # Plot size
    x_min, x_max = -0.5*L, 0.5*L
    y_min, y_max = -0.5*L, 0.5*L

    points_x_sim = np.asarray(points_x_sim)
    points_y_sim = np.asarray(points_y_sim)

    # Calculate resulting velocity 
    vel_res = resulting_velocity(vel_x_sim, vel_y_sim)

    # Convert to numpy arrays
    vel_res = np.asarray(vel_res)
    vel_x_sim = np.asarray(vel_x_sim)
    vel_y_sim = np.asarray(vel_y_sim)
    tid_part = np.asarray(tid_part)

    # Separate fluid and solid particles
    indices_fluid = np.where(tid_part == 0)
    indices_solid = np.where(tid_part == 1)

    vel_scatter = ax.scatter(points_x_sim, points_y_sim, s=5.0, c=vel_res, cmap=mcolors.ListedColormap(new_cmap), norm=norm,  edgecolors='black', linewidths=0.1)

    # Interpolate onto grid for streamline interpolation
    vel_x_grid = vel_x_sim
    vel_y_grid = vel_y_sim
    vel_res_grid = vel_res

    # Set solid particle velocities to NaN for streamline plotting
    vel_x_grid[indices_solid] = np.nan
    vel_y_grid[indices_solid] = np.nan
    vel_res_grid[indices_solid] = np.nan

    # Create simulation grid
    x_simgrid, y_simgrid = np.meshgrid(np.linspace(x_min, x_max, num=interpolation_res), np.linspace(y_min, y_max, num=interpolation_res))

    # Interpolate velocity components onto the grid
    u_grid = griddata((points_x_sim, points_y_sim), vel_x_sim, (x_simgrid, y_simgrid), method=interpolation_method)
    v_grid = griddata((points_x_sim, points_y_sim), vel_y_sim, (x_simgrid, y_simgrid), method=interpolation_method)
    vel_res_grid = griddata((points_x_sim, points_y_sim), vel_res, (x_simgrid, y_simgrid), method=interpolation_method)

    # Plot streamlines
    sim_streamlines = ax.streamplot(x_simgrid, y_simgrid, u_grid, v_grid, density=0.5, linewidth=0.7, color='black')

    # Ensure equal aspect ratio and display the plot
    ax.set_aspect('equal', adjustable='box')

    # Add Reynolds number text box
    ax.text(0.22, 0.432, f'$Re = {Re}$', 
            bbox={'facecolor': 'white', 'alpha': 0.8, 'pad': 5}, fontsize = 6, zorder = 7)

    # Set ticks
    x_ticks = np.linspace(x_min, x_max, 5)
    plt.xticks(x_ticks)
    y_ticks = np.linspace(y_min, y_max, 5)
    plt.yticks(y_ticks)

    # Colorbar below the plot
    cbar_ax = fig.add_axes([0.26, -0.1, 0.5, 0.04])#   left  bottom width height
    cbar = fig.colorbar(vel_scatter,cax=cbar_ax,orientation='horizontal')
    cbar.set_label(r'$|\mathbf{v}|\,[\mathrm{m/s}]$', fontsize=10)
    cbar.ax.tick_params(labelsize=10)

    # Set axis labels
    ax.set_xlabel(r'$\mathrm{x}\,[\mathrm{m}]$')
    ax.set_ylabel(r'$\mathrm{y}\,[\mathrm{m}]$')

    # Set axis limits
    ax.set_xlim(x_min,x_max)
    ax.set_ylim(y_min,y_max)

    plt.savefig(f'{out_file}', dpi=300, bbox_inches='tight', pad_inches=0.02)

if __name__ == '__main__':
    main()
