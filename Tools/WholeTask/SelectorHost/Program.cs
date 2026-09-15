using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Yanagisawa.DataLayoutCalibrator;

class Request
{
    public WholeTaskProcessSample[] Samples;
    public string[] CandidateIds;
    public string BaselineId;
    public string PartitionId;
    public WholeTaskTimingDecision FrozenDecision;
    public WholeTaskProcessSample[] ConfirmationBaseline;
    public WholeTaskProcessSample[] ConfirmationExecuted;
}

class Program
{
    static int Main(string[] args)
    {
        var json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
        if (args.Length != 2) throw new ArgumentException("Expected request.json decision.json");
        if (File.Exists(args[1])) throw new IOException("Refusing to overwrite frozen decision.");
        if (args[0] == "--allocation-control")
        {
            var probe = new ThreadManagedAllocationCounter();
            string failure = null;
            try { probe.Validate(); } catch (Exception e) { failure = e.Message; }
            File.WriteAllText(args[1], JsonSerializer.Serialize(new {
                actualDotNetCurrentThreadControl = probe.Capability, controlFailure = failure,
                wholeTaskAllocationEligibility = "Unknown"
            }, json));
            return 0;
        }
        var input = JsonSerializer.Deserialize<Request>(File.ReadAllText(args[0]), json);
        var start = Stopwatch.GetTimestamp();
        object decision = input.FrozenDecision == null
            ? (object)WholeTaskLayoutSelector.Select(input.Samples, input.CandidateIds, input.BaselineId, input.PartitionId)
            : WholeTaskLayoutSelector.Confirm(input.FrozenDecision, input.ConfirmationBaseline, input.ConfirmationExecuted);
        double decisionMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        // Exercise the real 1 MiB control on THIS .NET runtime. It says nothing
        // about C++ allocation, another runtime, Unity, or worker/native scope.
        var counter = new ThreadManagedAllocationCounter();
        string controlFailure = null;
        try { counter.Validate(); } catch (Exception e) { controlFailure = e.Message; }
        File.WriteAllText(args[1], JsonSerializer.Serialize(new {
            decision, decisionMs,
            actualDotNetCurrentThreadControl = counter.Capability,
            controlFailure,
            wholeTaskAllocationEligibility = "Unknown",
            note = "Native C++ allocations are unobserved; this is a timing-only recommendation, not a deployment profile."
        }, json));
        return 0;
    }
}
