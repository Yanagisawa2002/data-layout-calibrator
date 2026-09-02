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
        Unknown = 0,
        Available = 1,
        Unavailable = 2,
        Failed = 3,
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
        public string DeviceIdentitySha256;
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

            try
            {
                ValidateContext(context);
            }
            catch (Exception exception)
            {
                measuredAction();
                return ProviderFailure(context, default, "context-invalid", exception);
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

            try
            {
                ValidateProviderIdentity(descriptor);
            }
            catch (Exception exception)
            {
                measuredAction();
                return ProviderFailure(context, descriptor, "descriptor-invalid", exception);
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

            try
            {
                ValidateAvailableProviderDescriptor(descriptor);
            }
            catch (Exception exception)
            {
                measuredAction();
                return ProviderFailure(context, descriptor, "descriptor-invalid", exception);
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
            }
            catch (Exception exception)
            {
                providerException = exception;
                failureCode = "complete-failed";
            }

            if (providerException == null)
            {
                try
                {
                    ValidateMeasurement(measurement, descriptor);
                }
                catch (Exception exception)
                {
                    providerException = exception;
                    failureCode = "measurement-invalid";
                }
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
                return ProviderFailure(
                    context,
                    descriptor,
                    failureCode,
                    providerException);
            }

            var result = new CounterCaptureResult
            {
                Context = context,
                Provider = descriptor,
                Status = CounterCollectionStatus.Collected,
                Origin = measurement.Origin,
                InterpretationLevel = measurement.Origin == CounterEvidenceOrigin.Observed
                    ? CounterInterpretationLevel.Correlation
                    : CounterInterpretationLevel.None,
                StatusCode = "collected",
                StatusReason = measurement.Origin == CounterEvidenceOrigin.Observed
                    ? "Observed counter capture completed. Values support correlation only."
                    : "Synthetic fixture capture completed. It has no evidence interpretation level.",
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

        private static void ValidateContext(CounterCaptureContext context)
        {
            RequireStableId(context.RunId, "capture RunId");
            RequireStableId(context.ScenarioId, "capture ScenarioId");
            if (context.ContractVersion <= 0)
                throw new InvalidOperationException("Capture ContractVersion must be positive.");
            RequireStableId(context.CandidateId, "capture CandidateId");
            RequireCanonicalSha256(context.CandidateSchemaSha256, "capture candidate schema");
            if (context.Phase != BenchmarkPhase.Calibration &&
                context.Phase != BenchmarkPhase.Holdout)
            {
                throw new InvalidOperationException("Capture phase is not defined.");
            }
            if (context.RoundIndex < 0)
                throw new InvalidOperationException("Capture RoundIndex must be non-negative.");
            if (context.ElementCount <= 0)
                throw new InvalidOperationException("Capture ElementCount must be positive.");
            RequireStableId(context.ProcessEvidenceId, "capture ProcessEvidenceId");
            RequireStableId(context.DeviceId, "capture DeviceId");
            RequireCanonicalSha256(context.DeviceIdentitySha256, "capture device identity");
            RequireCanonicalSha256(
                context.EnvironmentFingerprintSha256,
                "capture environment fingerprint");
            RequireCanonicalSha256(
                context.SettingsFingerprintSha256,
                "capture settings fingerprint");
        }

        private static void ValidateProviderIdentity(CounterProviderDescriptor descriptor)
        {
            RequireStableId(descriptor.ProviderId, "provider ID");
            RequireStableId(descriptor.ProviderVersion, "provider version");
            RequireStableId(descriptor.CollectionMechanism, "provider collection mechanism");
            if (descriptor.SupportedCounterIds == null)
                throw new InvalidOperationException("Provider supported-counter IDs are required.");
        }

        private static void ValidateAvailableProviderDescriptor(
            CounterProviderDescriptor descriptor)
        {
            RequireCanonicalSha256(descriptor.ProviderArtifactSha256, "provider artifact");
            if (descriptor.SupportedCounterIds.Length == 0)
                throw new InvalidOperationException("An available provider must declare counters.");
            for (int index = 0; index < descriptor.SupportedCounterIds.Length; index++)
            {
                string counterId = descriptor.SupportedCounterIds[index];
                RequireStableId(counterId, $"provider counter ID {index}");
                for (int previous = 0; previous < index; previous++)
                {
                    if (string.Equals(
                            descriptor.SupportedCounterIds[previous],
                            counterId,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Provider counter ID {counterId} is duplicated.");
                    }
                }
            }
        }

        private static void ValidateMeasurement(
            CounterProviderMeasurement measurement,
            CounterProviderDescriptor descriptor)
        {
            if (measurement.Origin != CounterEvidenceOrigin.Observed &&
                measurement.Origin != CounterEvidenceOrigin.SyntheticFixture)
            {
                throw new InvalidOperationException(
                    "The provider did not declare a supported evidence origin.");
            }
            if (measurement.RawCounters == null || measurement.RawCounters.Length == 0)
                throw new InvalidOperationException("A collected measurement must contain raw counters.");
            if (measurement.DerivedMetrics == null)
                throw new InvalidOperationException("Derived metric metadata must be an explicit array.");
            if (measurement.Artifacts == null)
                throw new InvalidOperationException("Artifact provenance must be an explicit array.");

            ValidateRawCounters(measurement.RawCounters, descriptor.SupportedCounterIds);
            ValidateDerivedMetrics(measurement.DerivedMetrics, measurement.RawCounters);
            ValidateArtifacts(measurement.Artifacts);
            ValidateOverhead(measurement.Overhead);
        }

        private static void ValidateRawCounters(
            RawCounterValue[] counters,
            string[] supportedCounterIds)
        {
            for (int index = 0; index < counters.Length; index++)
            {
                RawCounterValue counter = counters[index];
                RequireStableId(counter.CounterId, $"raw counter ID {index}");
                RequireStableId(counter.Unit, $"raw counter {counter.CounterId} unit");
                if (!IsFinite(counter.Value) || counter.Value < 0d)
                {
                    throw new InvalidOperationException(
                        $"Raw counter {counter.CounterId} must be finite and non-negative.");
                }
                if (counter.IsScaled)
                {
                    if (!IsFinite(counter.ScaleFactor) || counter.ScaleFactor <= 0d)
                    {
                        throw new InvalidOperationException(
                            $"Scaled raw counter {counter.CounterId} has an invalid scale factor.");
                    }
                }
                else if (counter.ScaleFactor != 1d)
                {
                    throw new InvalidOperationException(
                        $"Unscaled raw counter {counter.CounterId} must use scale factor 1.");
                }
                if (!ContainsStableId(supportedCounterIds, counter.CounterId))
                {
                    throw new InvalidOperationException(
                        $"Raw counter {counter.CounterId} is not declared by the provider.");
                }
                for (int previous = 0; previous < index; previous++)
                {
                    if (string.Equals(
                            counters[previous].CounterId,
                            counter.CounterId,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Raw counter {counter.CounterId} is duplicated.");
                    }
                }
            }
        }

        private static void ValidateDerivedMetrics(
            DerivedCounterMetric[] metrics,
            RawCounterValue[] rawCounters)
        {
            for (int index = 0; index < metrics.Length; index++)
            {
                DerivedCounterMetric metric = metrics[index];
                RequireStableId(metric.MetricId, $"derived metric ID {index}");
                RequireStableId(metric.Unit, $"derived metric {metric.MetricId} unit");
                RequireText(metric.Formula, $"derived metric {metric.MetricId} formula");
                if (!IsFinite(metric.Value))
                {
                    throw new InvalidOperationException(
                        $"Derived metric {metric.MetricId} is not finite.");
                }
                if (metric.SourceCounterIds == null || metric.SourceCounterIds.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Derived metric {metric.MetricId} must name source counters.");
                }
                for (int source = 0; source < metric.SourceCounterIds.Length; source++)
                {
                    string sourceId = metric.SourceCounterIds[source];
                    RequireStableId(
                        sourceId,
                        $"derived metric {metric.MetricId} source counter {source}");
                    if (!ContainsRawCounter(rawCounters, sourceId))
                    {
                        throw new InvalidOperationException(
                            $"Derived metric {metric.MetricId} references unknown counter {sourceId}.");
                    }
                    for (int previous = 0; previous < source; previous++)
                    {
                        if (string.Equals(
                                metric.SourceCounterIds[previous],
                                sourceId,
                                StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Derived metric {metric.MetricId} repeats source {sourceId}.");
                        }
                    }
                }
                for (int previous = 0; previous < index; previous++)
                {
                    if (string.Equals(
                            metrics[previous].MetricId,
                            metric.MetricId,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Derived metric {metric.MetricId} is duplicated.");
                    }
                }
            }
        }

        private static void ValidateArtifacts(CounterArtifactProvenance[] artifacts)
        {
            for (int index = 0; index < artifacts.Length; index++)
            {
                CounterArtifactProvenance artifact = artifacts[index];
                RequireStableId(artifact.ArtifactKind, $"artifact kind {index}");
                RequireText(artifact.ArtifactPath, $"artifact {artifact.ArtifactKind} path");
                RequireCanonicalSha256(
                    artifact.ArtifactSha256,
                    $"artifact {artifact.ArtifactKind}");
                RequireStableId(
                    artifact.Producer,
                    $"artifact {artifact.ArtifactKind} producer");
                RequireStableId(
                    artifact.ProducerVersion,
                    $"artifact {artifact.ArtifactKind} producer version");
            }
        }

        private static void ValidateOverhead(CounterOverheadMetadata overhead)
        {
            if (!IsFinite(overhead.DisabledMedianNanoseconds) ||
                !IsFinite(overhead.EnabledMedianNanoseconds) ||
                !IsFinite(overhead.EstimatedAddedNanoseconds) ||
                !IsFinite(overhead.EstimatedOverheadPercent) ||
                overhead.DisabledMedianNanoseconds < 0d ||
                overhead.EnabledMedianNanoseconds < 0d)
            {
                throw new InvalidOperationException("Counter overhead values are invalid.");
            }
            RequireText(overhead.Method, "counter overhead method");

            switch (overhead.Status)
            {
                case CounterOverheadStatus.Measured:
                    if (overhead.Repetitions <= 0)
                        throw new InvalidOperationException("Measured overhead requires repetitions.");
                    if (!string.IsNullOrEmpty(overhead.FailureReason))
                        throw new InvalidOperationException("Measured overhead cannot have a failure reason.");
                    double expectedPercent = overhead.DisabledMedianNanoseconds > 0d
                        ? overhead.EstimatedAddedNanoseconds /
                          overhead.DisabledMedianNanoseconds * 100d
                        : 0d;
                    if (!IsFinite(expectedPercent) ||
                        !NearlyEqual(overhead.EstimatedOverheadPercent, expectedPercent))
                    {
                        throw new InvalidOperationException(
                            "Measured overhead percent is inconsistent with its medians and delta.");
                    }
                    break;
                case CounterOverheadStatus.NotMeasured:
                    RequireZeroOverheadPayload(overhead, "NotMeasured");
                    RequireText(overhead.FailureReason, "not-measured overhead reason");
                    break;
                case CounterOverheadStatus.Failed:
                    RequireZeroOverheadPayload(overhead, "Failed");
                    RequireText(overhead.FailureReason, "failed overhead reason");
                    break;
                default:
                    throw new InvalidOperationException("Counter overhead status is not defined.");
            }
        }

        private static void RequireZeroOverheadPayload(
            CounterOverheadMetadata overhead,
            string status)
        {
            if (overhead.Repetitions != 0 ||
                overhead.DisabledMedianNanoseconds != 0d ||
                overhead.EnabledMedianNanoseconds != 0d ||
                overhead.EstimatedAddedNanoseconds != 0d ||
                overhead.EstimatedOverheadPercent != 0d)
            {
                throw new InvalidOperationException(
                    $"{status} overhead cannot contain measured values.");
            }
        }

        private static bool ContainsStableId(string[] values, string target)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (string.Equals(values[index], target, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool ContainsRawCounter(RawCounterValue[] values, string target)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (string.Equals(values[index].CounterId, target, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static void RequireStableId(string value, string label)
        {
            RequireText(value, label);
            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsWhiteSpace(value[index]))
                    throw new InvalidOperationException($"{label} must not contain whitespace.");
            }
        }

        private static void RequireText(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{label} is required and must be canonical.");
            }
            for (int index = 0; index < value.Length; index++)
            {
                if (char.IsControl(value[index]))
                    throw new InvalidOperationException($"{label} must not contain control characters.");
            }
        }

        private static void RequireCanonicalSha256(string value, string label)
        {
            if (value == null || value.Length != 64)
                throw new InvalidOperationException($"{label} SHA-256 must contain 64 uppercase hex characters.");
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool isDigit = character >= '0' && character <= '9';
                bool isUpperHex = character >= 'A' && character <= 'F';
                if (!isDigit && !isUpperHex)
                {
                    throw new InvalidOperationException(
                        $"{label} SHA-256 must contain 64 uppercase hex characters.");
                }
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool NearlyEqual(double left, double right)
        {
            double scale = Math.Max(1d, Math.Max(Math.Abs(left), Math.Abs(right)));
            return Math.Abs(left - right) <= 1e-9d * scale;
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
                    FailureReason = reason,
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
