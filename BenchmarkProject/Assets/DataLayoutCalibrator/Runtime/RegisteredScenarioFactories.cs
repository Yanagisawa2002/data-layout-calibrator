using Yanagisawa.DataLayoutCalibrator;
using Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate;
using Yanagisawa.DataLayoutCalibrator.Samples.TransformExport;

[assembly: RegisterCalibrationScenarioFactory(typeof(ParticleIntegrateScenarioFactory))]
[assembly: RegisterCalibrationScenarioFactory(typeof(TransformExportScenarioFactory))]
