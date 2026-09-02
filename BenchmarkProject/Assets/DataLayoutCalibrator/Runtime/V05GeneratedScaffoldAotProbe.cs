using System;
using Unity.Collections;
using UnityEngine;
using Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate;
using Yanagisawa.DataLayoutCalibrator.Samples.TransformExport;

namespace Yanagisawa.DataLayoutCalibrator.Benchmark
{
    /// <summary>
    /// Tiny behavioral probe that keeps generated storage and the strict profile
    /// codec/resolver on Release Player AOT paths. All profile values below are
    /// explicitly synthetic test fixtures and are never written as evidence.
    /// </summary>
    internal static class V05GeneratedScaffoldAotProbe
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RunForBenchmarkPlayer()
        {
            if (!HasCommandLineArgument("-dla-v05-aot-probe"))
                return;

            RunOrThrow();
            Debug.Log(
                "[DataLayoutCalibrator] v0.5 generated-storage/profile AOT probe passed. " +
                "This synthetic probe is not Player performance, device, ISA, hardware-counter, or cross-device evidence.");
        }

        internal static void RunOrThrow()
        {
            ValidateParticleRoundTrip();
            ValidateTransformRoundTrip();
            ValidateSyntheticProfileRoundTrip();
        }

        private static void ValidateParticleRoundTrip()
        {
            NativeArray<ParticleRecord> source = default;
            NativeArray<ParticleRecord> destination = default;
            ParticleRecordGeneratedAoSoA8Storage storage = default;
            try
            {
                source = ParticleDataSet.Create(9, ParticleDataSet.CalibrationSeed, Allocator.Temp);
                destination = new NativeArray<ParticleRecord>(
                    source.Length,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
                storage = ParticleRecordGeneratedAoSoA8Storage.FromRecords(source, Allocator.Temp);
                ParticleRecordGeneratedDataLayoutCodec.Export(ref storage, destination);
                if (ParticleStateValidation.ComputeHash(source) !=
                    ParticleStateValidation.ComputeHash(destination))
                {
                    throw new InvalidOperationException("Generated ParticleRecord AoSoA codec round-trip failed.");
                }
            }
            finally
            {
                storage.Dispose();
                if (destination.IsCreated)
                    destination.Dispose();
                if (source.IsCreated)
                    source.Dispose();
            }
        }

        private static void ValidateTransformRoundTrip()
        {
            NativeArray<TransformRecord> source = default;
            NativeArray<TransformRecord> destination = default;
            TransformRecordGeneratedSoAStorage storage = default;
            try
            {
                source = TransformExportDataSet.Create(
                    7,
                    TransformExportDataSet.CalibrationSeed,
                    Allocator.Temp);
                destination = new NativeArray<TransformRecord>(
                    source.Length,
                    Allocator.Temp,
                    NativeArrayOptions.UninitializedMemory);
                storage = TransformRecordGeneratedSoAStorage.FromRecords(source, Allocator.Temp);
                TransformRecordGeneratedDataLayoutCodec.Export(ref storage, destination);
                if (TransformExportValidation.ComputeInputHash(source) !=
                    TransformExportValidation.ComputeInputHash(destination))
                {
                    throw new InvalidOperationException("Generated TransformRecord SoA codec round-trip failed.");
                }
            }
            finally
            {
                storage.Dispose();
                if (destination.IsCreated)
                    destination.Dispose();
                if (source.IsCreated)
                    source.Dispose();
            }
        }

        private static void ValidateSyntheticProfileRoundTrip()
        {
            const string syntheticHash =
                "0000000000000000000000000000000000000000000000000000000000000000";
            CalibrationProfileFingerprint fingerprint = CalibrationProfileFingerprintBuilder.Create(
                new CalibrationProfileFingerprintInput
                {
                    WorkloadId = "synthetic-aot-probe",
                    WorkloadContractVersion = 1,
                    RecordSchemaId = ParticleRecordGeneratedDataLayoutSchema.SchemaId,
                    RecordSchemaVersion = ParticleRecordGeneratedDataLayoutSchema.SchemaVersion,
                    RecordSchemaHash = ParticleRecordGeneratedDataLayoutSchema.SchemaHashSha256,
                    CandidateDefinitions = new[] { "AoS-synthetic", "SoA-synthetic" },
                    UnityVersion = "synthetic-aot-probe",
                    BurstVersion = "synthetic-aot-probe",
                    CollectionsVersion = "synthetic-aot-probe",
                    MathematicsVersion = "synthetic-aot-probe",
                    ScriptingBackend = "synthetic-aot-probe",
                    BuildTarget = "synthetic-aot-probe",
                    Architecture = "synthetic-aot-probe",
                    BuildFlags = new[] { "synthetic=true" },
                    OperatingSystem = "synthetic-aot-probe",
                    Processor = "synthetic-aot-probe",
                    InstructionSet = "synthetic-aot-probe-not-device-evidence",
                    LogicalProcessorCount = 1,
                    JobWorkerCount = 1,
                    BinaryHash = syntheticHash,
                    CalibrationSettings = new[] { "synthetic=true" },
                });
            FrozenDeploymentProfile profile = FrozenDeploymentProfileFactory.Create(
                fingerprint,
                new FrozenDeploymentDecision
                {
                    BaselineCandidateId = "AoS-synthetic",
                    SelectedCandidateId = "SoA-synthetic",
                    Status = LayoutSelectionStatus.Optimized,
                    ImprovementPercent = 1.0,
                    Reason = "Synthetic AOT codec/resolver fixture; not a performance decision.",
                },
                "{\"syntheticAotProbe\":true,\"evidence\":false}",
                new DeploymentProfileProvenance
                {
                    RunId = "synthetic-aot-probe",
                    CreatedUtcIso8601 = "2000-01-01T00:00:00Z",
                    SourceRepository = "https://github.com/Yanagisawa2002/data-layout-calibrator",
                    SourceCommit = "644893990ed18e56619da8d2737e6b7592eb6080",
                    EvidenceScope = "Synthetic AOT behavior probe only; not Player performance, device, ISA, hardware-counter, or cross-device evidence.",
                });
            ProfileDocumentLoadResult loaded = FrozenDeploymentProfileCodec.Decode(
                FrozenDeploymentProfileCodec.Encode(profile));
            ProfileResolution resolution = DeploymentProfileResolver.Resolve(
                fingerprint,
                loaded,
                "AoS-synthetic",
                new[] { "AoS-synthetic", "SoA-synthetic" });
            if (resolution.Status != ProfileResolutionStatus.ExactMatch ||
                !string.Equals(resolution.CandidateId, "SoA-synthetic", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Synthetic frozen profile codec/resolver round-trip failed.");
            }
        }

        private static bool HasCommandLineArgument(string value)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length; index++)
            {
                if (string.Equals(arguments[index], value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
