using System;

namespace Yanagisawa.DataLayoutCalibrator
{
    /// <summary>Not serialized in profiles or inferred from historical run settings.
    /// Hosts may construct a permit only following NEW explicit user authorization.</summary>
    public sealed class MeasurementExecutionPermit
    {
        public string AuthorizationReference { get; }
        private MeasurementExecutionPermit(string reference) { AuthorizationReference = reference; }
        public static MeasurementExecutionPermit FromNewExplicitUserAuthorization(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) throw new ArgumentException("A new user authorization reference is required.");
            return new MeasurementExecutionPermit(reference);
        }
    }

    public static class MeasurementExecutionPolicy
    {
        public static void Require(MeasurementExecutionPermit permit)
        {
            if (permit == null || string.Equals(Environment.GetEnvironmentVariable("DLC_FUNCTIONAL_ONLY"), "1", StringComparison.Ordinal))
                throw new InvalidOperationException("Performance execution is disabled. New explicit user authorization is required; functional CI never unlocks it.");
        }
    }
}
