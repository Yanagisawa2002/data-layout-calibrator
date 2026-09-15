#include <pybind11/pybind11.h>
#include <pybind11/stl.h>
#include "solver.hpp"
#include "particle.hpp"
#include "integrator.hpp"
#include "euler_integrator.hpp"
#include "verlet_integrator.hpp"
#include "tv_verlet_integrator.hpp"
#include "density_calculator.hpp"

#include <omp.h>

namespace py = pybind11;


void set_omp_threads(int n) {
    omp_set_num_threads(n);
}

PYBIND11_MODULE(SimplePH, m)
{
    m.doc() = "SimplePH minimal SPH module";

    m.def("set_omp_threads", &set_omp_threads, "Set number of OpenMP threads");

    py::class_<Particle>(m, "Particle")
        .def(py::init<>())
        .def_readwrite("x", &Particle::x)
        .def_readwrite("v", &Particle::v)
        .def_readwrite("rho", &Particle::rho)
        .def_readwrite("p", &Particle::p)
        .def_readwrite("m", &Particle::m)
        .def_readwrite("type", &Particle::type);

    py::class_<Solver>(m, "Solver")
        .def(py::init<double, double, double, double, double, double, KernelType>(),
             py::arg("h"), py::arg("Lx"), py::arg("Ly"), py::arg("dx0"), py::arg("Lref"), py::arg("vref"), py::arg("kernel_type"))
       .def("set_viscosity", &Solver::set_viscosity,
             py::arg("mu"))
        .def("get_viscosity", &Solver::get_viscosity)
        .def("set_density", &Solver::set_density,
             py::arg("rho0"),
             py::arg("rho_fluct") = 0.01)
        .def("get_density", &Solver::get_density)
        .def("set_acceleration", 
            &Solver::set_acceleration,
            py::arg("b_") = std::array<double,2>{0.0, 0.0},
            py::arg("damp_timesteps_") = 0,
            "Set constant acceleration vector b = [bx, by], with optional damping timesteps")
        .def("get_acceleration", &Solver::get_acceleration)
        .def("set_particles", &Solver::set_particles)
        .def("compute_soundspeed_and_timestep", &Solver::compute_soundspeed_and_timestep)
        .def("step", &Solver::step)
        .def("run", &Solver::run,
             py::arg("steps"),
             py::arg("vtk_freq") = 1,
             py::arg("log_freq") = 0)
        .def("set_output_name", &Solver::set_output_name,
             py::arg("output_name") = "unlabeled_output_external")
        .def("set_eos", &Solver::set_eos,
             py::arg("eos_type"), py::arg("bp_fac") = 0.0, py::arg("tvp_bp_fac") = 0.0)
        .def("set_density_method", &Solver::set_density_method)
        .def("set_particles", &Solver::set_particles)
        .def("set_integrator", &Solver::set_integrator)
        .def("activate_artificial_viscosity", &Solver::activate_artificial_viscosity,
             py::arg("alpha") = 1.0)
        .def("activate_tensile_instability_correction", &Solver::activate_tensile_instability_correction,
             py::arg("epsilon") = 0.2)
        .def("activate_xsph_filter", &Solver::activate_xsph_filter,
             py::arg("eta") = 0.1)
        .def("activate_negative_pressure_truncation", &Solver::activate_negative_pressure_truncation)
        .def("activate_transport_velocity", &Solver::activate_transport_velocity)
        .def("get_particles", &Solver::get_particles,
             py::return_value_policy::reference_internal);

    py::enum_<KernelType>(m, "KernelType")
        .value("CubicSpline", KernelType::CubicSpline)
        .value("QuinticSpline", KernelType::QuinticSpline)
        .value("WendlandC2", KernelType::WendlandC2)
        .value("WendlandC4", KernelType::WendlandC4)
        .export_values();

    py::enum_<EOSType>(m, "EOSType")
        .value("Tait", EOSType::Tait)
        .value("Linear", EOSType::Linear)
        .export_values();

    py::enum_<DensityMethod>(m, "DensityMethod")
        .value("Summation", DensityMethod::Summation)
        .value("Continuity", DensityMethod::Continuity)
        .export_values();

    py::class_<Integrator, std::shared_ptr<Integrator>>(m, "Integrator");

    py::class_<EulerIntegrator, Integrator, std::shared_ptr<EulerIntegrator>>(m, "EulerIntegrator")
        .def(py::init<>());

    py::class_<VerletIntegrator, Integrator, std::shared_ptr<VerletIntegrator>>(m, "VerletIntegrator")
        .def(py::init<>());

    py::class_<TransportVelocityVerletIntegrator, Integrator, std::shared_ptr<TransportVelocityVerletIntegrator>>(m, "TransportVelocityVerletIntegrator")
        .def(py::init<>());

}
