using System;
using Yanagisawa.DataLayoutCalibrator;

/// <summary>
/// Compiled integration example; the cost-only console Program never calls it.
/// A real host supplies its workload, settings, full fingerprint and validated
/// allocation provider. Invoking Run performs real workload measurements.
/// The caller owns the provider and must not share its windows across runs.
/// </summary>
public static class CalibrationHostExample
{
    public static ScenarioCalibrationProfile Run(
        ICalibrationScenarioFactory factory,
        CalibrationRunSettings settings,
        CalibrationProfileFingerprint fingerprint,
        IAllocationCounterCapabilities allocationCounter,
        AllocationScope requiredScope)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (allocationCounter == null) throw new ArgumentNullException(nameof(allocationCounter));
        settings.BindSourceContext(fingerprint, requiredScope);
        settings.AllocationCounter = allocationCounter;
        return ScenarioCalibrationEngine.Run(factory, settings);
    }
}
