using System;

namespace Yanagisawa.DataLayoutCalibrator
{
    /// <summary>
    /// Describes where a counter value came from. SyntheticFixture is reserved for
    /// deterministic tests and must never be promoted to observed evidence.
    /// </summary>
    public enum CounterEvidenceOrigin
    {
        None = 0,
        Observed = 1,
        SyntheticFixture = 2,
    }

    public enum CounterCollectionStatus
    {
        Disabled = 0,
        Unavailable = 1,
        Collected = 2,
        Failed = 3,
    }

    public enum CounterProviderAvailabilityStatus
    {
        Available = 0,
        Unavailable = 1,
        Failed = 2,
    }

    public enum CounterOverheadStatus
    {
        NotMeasured = 0,
        Measured = 1,
        Failed = 2,
    }

    /// <summary>
    /// A single counter capture supports correlation only. Mechanism and causal
    /// conclusions require separate artifact or controlled-experiment evidence.
    /// </summary>
    public enum CounterInterpretationLevel
    {
        None = 0,
        Correlation = 1,
        MechanismEvidence = 2,
        ControlledExperiment = 3,
    }

    [Serializable]
    public struct CounterCaptureContext
    {
        public string RunId;
        public string ScenarioId;
        public int ContractVersion;
        public string CandidateId;
        public string CandidateSchemaSha256;
        public BenchmarkPhase Phase;
        public int RoundIndex;
        public int ElementCount;
        public string ProcessEvidenceId;
        public string DeviceId;
        public string EnvironmentFingerprintSha256;
        public string SettingsFingerprintSha256;
    }

    [Serializable]
    public struct CounterProviderDescriptor
    {
        public string ProviderId;
        public string ProviderVersion;
        public string CollectionMechanism;
        public string ProviderArtifactSha256;
        public string[] SupportedCounterIds;
    }

    [Serializable]
    public struct CounterProviderAvailability
    {
        public CounterProviderAvailabilityStatus Status;
        public string Code;
        public string Reason;

        public static CounterProviderAvailability Available()
        {
            return new CounterProviderAvailability
            {
                Status = CounterProviderAvailabilityStatus.Available,
                Code = "available",
                Reason = "Provider is available.",
            };
        }

        public static CounterProviderAvailability Unavailable(string code, string reason)
        {
            return new CounterProviderAvailability
            {
                Status = CounterProviderAvailabilityStatus.Unavailable,
                Code = code,
                Reason = reason,
            };
        }

        public static CounterProviderAvailability Failed(string code, string reason)
        {
            return new CounterProviderAvailability
            {
                Status = CounterProviderAvailabilityStatus.Failed,
                Code = code,
                Reason = reason,
            };
        }
    }

    [Serializable]
    public struct RawCounterValue
    {
        public string CounterId;
        public double Value;
        public string Unit;
        public bool IsScaled;
        public double ScaleFactor;
    }

    [Serializable]
    public struct DerivedCounterMetric
    {
        public string MetricId;
        public double Value;
        public string Unit;
        public string Formula;
        public string[] SourceCounterIds;
    }

    [Serializable]
    public struct CounterArtifactProvenance
    {
        public string ArtifactKind;
        public string ArtifactPath;
        public string ArtifactSha256;
        public string Producer;
        public string ProducerVersion;
    }

    [Serializable]
    public struct CounterOverheadMetadata
    {
        public CounterOverheadStatus Status;
        public int Repetitions;
        public double DisabledMedianNanoseconds;
        public double EnabledMedianNanoseconds;
        public double EstimatedAddedNanoseconds;
        public double EstimatedOverheadPercent;
        public string Method;
        public string FailureReason;
    }

    [Serializable]
    public sealed class CounterProviderMeasurement
    {
        public CounterEvidenceOrigin Origin;
        public RawCounterValue[] RawCounters;
        public DerivedCounterMetric[] DerivedMetrics;
        public CounterArtifactProvenance[] Artifacts;
        public CounterOverheadMetadata Overhead;
    }

    [Serializable]
    public sealed class CounterCaptureResult
    {
        public int SchemaVersion = 1;
        public CounterCaptureContext Context;
        public CounterProviderDescriptor Provider;
        public CounterCollectionStatus Status;
        public CounterEvidenceOrigin Origin;
        public CounterInterpretationLevel InterpretationLevel;
        public RawCounterValue[] RawCounters;
        public DerivedCounterMetric[] DerivedMetrics;
        public CounterArtifactProvenance[] Artifacts;
        public CounterOverheadMetadata Overhead;
        public string StatusCode;
        public string StatusReason;
    }

    /// <summary>
    /// Optional adapter over an OS, profiler, or vendor counter source. The core does
    /// not ship a sampler and never assumes that a provider exists.
    /// </summary>
    public interface ICounterProvider
    {
        CounterProviderDescriptor Descriptor { get; }

        CounterProviderAvailability Probe();

        ICounterCapture Begin(CounterCaptureContext context);
    }

    public interface ICounterCapture : IDisposable
    {
        CounterProviderMeasurement Complete();
    }

    /// <summary>
    /// An explicit unavailable adapter for hosts that want a stable configured slot
    /// without platform-specific counter support.
    /// </summary>
    public sealed class UnavailableCounterProvider : ICounterProvider
    {
        private readonly CounterProviderDescriptor _descriptor;
        private readonly CounterProviderAvailability _availability;

        public UnavailableCounterProvider(string providerId, string code, string reason)
        {
            _descriptor = new CounterProviderDescriptor
            {
                ProviderId = string.IsNullOrWhiteSpace(providerId) ? "unavailable" : providerId,
                ProviderVersion = "not-installed",
                CollectionMechanism = "none",
                SupportedCounterIds = Array.Empty<string>(),
            };
            _availability = CounterProviderAvailability.Unavailable(code, reason);
        }

        public CounterProviderDescriptor Descriptor => _descriptor;

        public CounterProviderAvailability Probe() => _availability;

        public ICounterCapture Begin(CounterCaptureContext context)
        {
            throw new InvalidOperationException("An unavailable counter provider cannot begin a capture.");
        }
    }

    /// <summary>
    /// Failure-isolating host for optional counters. Provider failures become data;
    /// the measured action still runs once. Exceptions from the measured action are
    /// never swallowed or replaced by provider cleanup failures.
    /// </summary>
    public static class CounterCaptureRunner
    {
        public static CounterCaptureResult Capture(
            ICounterProvider provider,
            bool enabled,
            CounterCaptureContext context,
            Action measuredAction)
        {
            if (measuredAction == null)
                throw new ArgumentNullException(nameof(measuredAction));

            if (!enabled)
            {
                measuredAction();
                return Terminal(
                    context,
                    default,
                    CounterCollectionStatus.Disabled,
                    "disabled",
                    "Counter collection was disabled.");
            }

            if (provider == null)
            {
                measuredAction();
                return Terminal(
                    context,
                    default,
                    CounterCollectionStatus.Unavailable,
                    "provider-not-configured",
                    "No counter provider was configured.");
            }

            CounterProviderDescriptor descriptor;
            try
            {
                descriptor = provider.Descriptor;
            }
            catch (Exception exception)
            {
                measuredAction();
                return ProviderFailure(context, default, "descriptor-failed", exception);
            }

            CounterProviderAvailability availability;
            try
            {
                availability = provider.Probe();
            }
            catch (Exception exception)
            {
                measuredAction();
                return ProviderFailure(context, descriptor, "probe-threw", exception);
            }

            if (availability.Status != CounterProviderAvailabilityStatus.Available)
            {
                measuredAction();
                CounterCollectionStatus status =
                    availability.Status == CounterProviderAvailabilityStatus.Unavailable
                        ? CounterCollectionStatus.Unavailable
                        : CounterCollectionStatus.Failed;
                return Terminal(
                    context,
                    descriptor,
                    status,
                    string.IsNullOrWhiteSpace(availability.Code) ? "probe-not-available" : availability.Code,
                    string.IsNullOrWhiteSpace(availability.Reason)
                        ? "The provider did not report itself available."
                        : availability.Reason);
            }

            ICounterCapture capture;
            try
            {
                capture = provider.Begin(context);
                if (capture == null)
                    throw new InvalidOperationException("The provider returned a null capture session.");
            }
            catch (Exception exception)
            {
                measuredAction();
                return ProviderFailure(context, descriptor, "begin-failed", exception);
            }

            try
            {
                measuredAction();
            }
            catch
            {
                DisposeWithoutMasking(capture);
                throw;
            }

            CounterProviderMeasurement measurement = null;
            Exception providerException = null;
            string failureCode = null;
            try
            {
                measurement = capture.Complete();
                if (measurement == null)
                    throw new InvalidOperationException("The provider returned a null measurement.");
                ValidateMeasurement(measurement);
            }
            catch (Exception exception)
            {
                providerException = exception;
                failureCode = "complete-failed";
            }

            try
            {
                capture.Dispose();
            }
            catch (Exception exception)
            {
                if (providerException == null)
                {
                    providerException = exception;
                    failureCode = "dispose-failed";
                }
            }

            if (providerException != null)
            {
                CounterCaptureResult failed = ProviderFailure(
                    context,
                    descriptor,
                    failureCode,
                    providerException);
                CopyMeasurement(failed, measurement);
                return failed;
            }

            var result = new CounterCaptureResult
            {
                Context = context,
                Provider = descriptor,
                Status = CounterCollectionStatus.Collected,
                Origin = measurement.Origin,
                InterpretationLevel = CounterInterpretationLevel.Correlation,
                StatusCode = "collected",
                StatusReason = "Counter capture completed. Values support correlation only.",
            };
            CopyMeasurement(result, measurement);
            return result;
        }

        private static void CopyMeasurement(
            CounterCaptureResult destination,
            CounterProviderMeasurement measurement)
        {
            if (measurement == null)
                return;

            destination.Origin = measurement.Origin;
            destination.RawCounters = measurement.RawCounters;
            destination.DerivedMetrics = measurement.DerivedMetrics;
            destination.Artifacts = measurement.Artifacts;
            destination.Overhead = measurement.Overhead;
        }

        private static void ValidateMeasurement(CounterProviderMeasurement measurement)
        {
            if (measurement.Origin == CounterEvidenceOrigin.None)
                throw new InvalidOperationException("The provider did not label the evidence origin.");
            if (measurement.RawCounters == null || measurement.RawCounters.Length == 0)
                throw new InvalidOperationException("A collected measurement must contain raw counters.");

            for (int index = 0; index < measurement.RawCounters.Length; index++)
            {
                RawCounterValue counter = measurement.RawCounters[index];
                if (string.IsNullOrWhiteSpace(counter.CounterId))
                    throw new InvalidOperationException($"Raw counter {index} has no stable counter ID.");
                if (string.IsNullOrWhiteSpace(counter.Unit))
                    throw new InvalidOperationException($"Raw counter {counter.CounterId} has no unit.");
                if (double.IsNaN(counter.Value) || double.IsInfinity(counter.Value))
                    throw new InvalidOperationException($"Raw counter {counter.CounterId} is not finite.");
                if (counter.IsScaled &&
                    (double.IsNaN(counter.ScaleFactor) ||
                     double.IsInfinity(counter.ScaleFactor) ||
                     counter.ScaleFactor <= 0d))
                {
                    throw new InvalidOperationException(
                        $"Scaled raw counter {counter.CounterId} has an invalid scale factor.");
                }
            }
        }

        private static CounterCaptureResult ProviderFailure(
            CounterCaptureContext context,
            CounterProviderDescriptor descriptor,
            string code,
            Exception exception)
        {
            return Terminal(
                context,
                descriptor,
                CounterCollectionStatus.Failed,
                code,
                $"{exception.GetType().FullName}: {exception.Message}");
        }

        private static CounterCaptureResult Terminal(
            CounterCaptureContext context,
            CounterProviderDescriptor descriptor,
            CounterCollectionStatus status,
            string code,
            string reason)
        {
            return new CounterCaptureResult
            {
                Context = context,
                Provider = descriptor,
                Status = status,
                Origin = CounterEvidenceOrigin.None,
                InterpretationLevel = CounterInterpretationLevel.None,
                RawCounters = Array.Empty<RawCounterValue>(),
                DerivedMetrics = Array.Empty<DerivedCounterMetric>(),
                Artifacts = Array.Empty<CounterArtifactProvenance>(),
                Overhead = new CounterOverheadMetadata
                {
                    Status = CounterOverheadStatus.NotMeasured,
                    Method = "not-measured",
                },
                StatusCode = code,
                StatusReason = reason,
            };
        }

        private static void DisposeWithoutMasking(ICounterCapture capture)
        {
            try
            {
                capture.Dispose();
            }
            catch
            {
                // The measured action is authoritative; provider cleanup cannot replace it.
            }
        }
    }

    /// <summary>
    /// Deterministic enabled/disabled overhead calculation. Timing collection is
    /// intentionally delegated to the provider or harness; this type only evaluates
    /// paired duration arrays supplied in nanoseconds.
    /// </summary>
    public static class CounterOverheadEstimator
    {
        public static CounterOverheadMetadata Estimate(
            double[] disabledDurationsNanoseconds,
            double[] enabledDurationsNanoseconds,
            string method)
        {
            if (disabledDurationsNanoseconds == null || enabledDurationsNanoseconds == null)
                return Failure("Enabled and disabled duration arrays are required.", method);
            if (disabledDurationsNanoseconds.Length == 0 ||
                disabledDurationsNanoseconds.Length != enabledDurationsNanoseconds.Length)
            {
                return Failure("Enabled and disabled duration arrays must have the same non-zero length.", method);
            }
            if (!AllFiniteAndNonNegative(disabledDurationsNanoseconds) ||
                !AllFiniteAndNonNegative(enabledDurationsNanoseconds))
            {
                return Failure("Durations must be finite and non-negative.", method);
            }

            double disabledMedian = Median(disabledDurationsNanoseconds);
            double enabledMedian = Median(enabledDurationsNanoseconds);
            var pairedDeltas = new double[disabledDurationsNanoseconds.Length];
            for (int index = 0; index < pairedDeltas.Length; index++)
            {
                pairedDeltas[index] =
                    enabledDurationsNanoseconds[index] - disabledDurationsNanoseconds[index];
            }
            double added = Median(pairedDeltas);
            double percent = disabledMedian > 0d
                ? added / disabledMedian * 100d
                : 0d;
            return new CounterOverheadMetadata
            {
                Status = CounterOverheadStatus.Measured,
                Repetitions = disabledDurationsNanoseconds.Length,
                DisabledMedianNanoseconds = disabledMedian,
                EnabledMedianNanoseconds = enabledMedian,
                EstimatedAddedNanoseconds = added,
                EstimatedOverheadPercent = percent,
                Method = string.IsNullOrWhiteSpace(method)
                    ? "paired-enabled-disabled-median-delta"
                    : method,
                FailureReason = string.Empty,
            };
        }

        private static CounterOverheadMetadata Failure(string reason, string method)
        {
            return new CounterOverheadMetadata
            {
                Status = CounterOverheadStatus.Failed,
                Method = string.IsNullOrWhiteSpace(method)
                    ? "paired-enabled-disabled-median-delta"
                    : method,
                FailureReason = reason,
            };
        }

        private static bool AllFiniteAndNonNegative(double[] values)
        {
            for (int index = 0; index < values.Length; index++)
            {
                double value = values[index];
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
                    return false;
            }

            return true;
        }

        private static double Median(double[] values)
        {
            var sorted = new double[values.Length];
            Array.Copy(values, sorted, values.Length);
            Array.Sort(sorted);
            int midpoint = sorted.Length / 2;
            return sorted.Length % 2 == 0
                ? (sorted[midpoint - 1] + sorted[midpoint]) * 0.5d
                : sorted[midpoint];
        }
    }
}
