using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Yanagisawa.DataLayoutCalibrator
{
    /// <summary>
    /// Optional OS-accounted CPU cycles for all threads in this process, user and kernel mode.
    /// This is not a retired-instruction PMU, invariant elapsed-time clock, or per-job attribution.
    /// Construct once outside measured scopes. No privileges, drivers or global sessions are changed.
    /// </summary>
    public sealed class WindowsProcessCycleCounterProvider : ICounterProvider
    {
        public const string CounterId = "windows-process-cpu-cycles";
        private readonly string _artifactDirectory;
        private readonly CounterProviderDescriptor _descriptor;

        public WindowsProcessCycleCounterProvider(string implementationBinaryPath, string artifactDirectory)
        {
            _artifactDirectory = Path.GetFullPath(artifactDirectory);
            Directory.CreateDirectory(_artifactDirectory);
            _descriptor = new CounterProviderDescriptor
            {
                ProviderId = "windows-query-process-cycle-time",
                ProviderVersion = "1",
                CollectionMechanism = "QueryProcessCycleTime-current-process-all-threads-user-and-kernel",
                ProviderArtifactSha256 = HashFile(implementationBinaryPath),
                SupportedCounterIds = new[] { CounterId },
            };
        }

        public CounterProviderDescriptor Descriptor => _descriptor;

        public CounterProviderAvailability Probe()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return CounterProviderAvailability.Unavailable("platform-unsupported", "QueryProcessCycleTime requires Windows.");
            try
            {
                if (QueryProcessCycleTime(GetCurrentProcess(), out _))
                    return CounterProviderAvailability.Available();
                int error = Marshal.GetLastWin32Error();
                return CounterProviderAvailability.Unavailable("win32-" + error, new Win32Exception(error).Message);
            }
            catch (DllNotFoundException exception)
            {
                return CounterProviderAvailability.Unavailable("os-library-unavailable", exception.Message);
            }
            catch (EntryPointNotFoundException exception)
            {
                return CounterProviderAvailability.Unavailable("os-entrypoint-unavailable", exception.Message);
            }
        }

        public ICounterCapture Begin(CounterCaptureContext context) => new Capture(this, context);

        public static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return Hex(sha.ComputeHash(stream));
        }

        public static string HashText(string value)
        {
            using (var sha = SHA256.Create())
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }

        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty);

        private static ulong Read()
        {
            if (!QueryProcessCycleTime(GetCurrentProcess(), out ulong value))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, "QueryProcessCycleTime failed: win32-" + error);
            }
            return value;
        }

        private sealed class Capture : ICounterCapture
        {
            private readonly WindowsProcessCycleCounterProvider _owner;
            private readonly CounterCaptureContext _context;
            private readonly ulong _start;
            private bool _ended;

            public Capture(WindowsProcessCycleCounterProvider owner, CounterCaptureContext context)
            {
                _owner = owner;
                _context = context;
                _start = Read();
            }

            public CounterProviderMeasurement Complete()
            {
                if (_ended) throw new InvalidOperationException("Capture already completed or disposed.");
                ulong end = Read();
                _ended = true;
                if (end < _start) throw new InvalidOperationException("OS process cycle count regressed.");
                ulong delta = end - _start;
                // Preserve integer endpoints exactly. The public double value can lose precision > 2^53.
                string path = Path.Combine(_owner._artifactDirectory, "cycles-" + Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllText(path,
                    "schema=windows-process-cycles-v1\n" +
                    "run=" + _context.RunId + "\ncandidate=" + _context.CandidateId +
                    "\nprocess=" + _context.ProcessEvidenceId + "\nphase=" + _context.Phase +
                    "\nround=" + _context.RoundIndex.ToString(CultureInfo.InvariantCulture) +
                    "\nstart=" + _start.ToString(CultureInfo.InvariantCulture) +
                    "\nend=" + end.ToString(CultureInfo.InvariantCulture) +
                    "\ndelta=" + delta.ToString(CultureInfo.InvariantCulture) + "\n", new UTF8Encoding(false));
                return new CounterProviderMeasurement
                {
                    Origin = CounterEvidenceOrigin.Observed,
                    RawCounters = new[] { new RawCounterValue
                    {
                        CounterId = CounterId, Value = delta, Unit = "os-accounted-cpu-cycles",
                        IsScaled = false, ScaleFactor = 1d,
                    } },
                    DerivedMetrics = Array.Empty<DerivedCounterMetric>(),
                    Artifacts = new[] { new CounterArtifactProvenance
                    {
                        ArtifactKind = "os-counter-endpoints", ArtifactPath = path,
                        ArtifactSha256 = HashFile(path), Producer = _owner._descriptor.ProviderId,
                        ProducerVersion = _owner._descriptor.ProviderVersion,
                    } },
                    Overhead = new CounterOverheadMetadata { Status = CounterOverheadStatus.NotMeasured,
                        Method = "not-measured-per-capture", FailureReason = "A single capture cannot estimate overhead; use the separate paired on/off harness." },
                };
            }

            public void Dispose() { _ended = true; }
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryProcessCycleTime(IntPtr process, out ulong cycles);
    }
}
