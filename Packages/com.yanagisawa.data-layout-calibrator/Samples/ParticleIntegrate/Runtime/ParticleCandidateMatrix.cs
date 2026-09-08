using System;
using System.Collections.Generic;

namespace Yanagisawa.DataLayoutCalibrator.Samples.ParticleIntegrate
{
    [Serializable]
    public sealed class ParticleMatrixCell
    {
        public CandidateDescriptor Candidate;
        public bool Supported;
        public string Reason;
        public string ScheduleUnit;
        public int RecordsPerScheduleUnit;
        public int PhysicalBatchSize;
        public string TailPolicy;
    }

    /// <summary>
    /// Explicit opt-in experimental matrix. Source control flow is not an ISA claim.
    /// Factor definitions are already bound by CandidateDefinitionProtocol v1.
    /// </summary>
    public static class ParticleCandidateMatrix
    {
        public const string MatrixId = "particle-crossed-v1";
        public static CandidateDescriptor[] CreateCandidates(int[] batches = null)
        {
            var result = new List<CandidateDescriptor>();
            foreach (var cell in CreateCells(batches))
                if (cell.Supported) result.Add(cell.Candidate);
            return result.ToArray();
        }

        public static ParticleMatrixCell[] CreateCells(int[] batches = null)
        {
            batches = batches ?? new[] { 32, 64, 128, 256 };
            if (batches.Length == 0) throw new ArgumentException("At least one batch is required.");
            var seen = new HashSet<int>();
            foreach (int batch in batches)
                if (batch <= 0 || !seen.Add(batch))
                    throw new ArgumentException("Batches must be positive and unique.");
            var layouts = new[]
            {
                new LayoutPolicy("AoS"), new LayoutPolicy("SoA"),
                new LayoutPolicy("AoSoA4", 4), new LayoutPolicy("AoSoA8", 8),
                new LayoutPolicy("AoSoA16", 16),
                new LayoutPolicy("AoSPadded64", paddingBytes: 16),
            };
            var kernels = new[]
            {
                new KernelPolicy("ScalarBranched", KernelControlFlow.Branched),
                new KernelPolicy("ScalarBranchless", KernelControlFlow.Branchless),
                new KernelPolicy("PackedBranchless4", KernelControlFlow.Branchless, 4),
                new KernelPolicy("PackedBranchless8", KernelControlFlow.Branchless, 8),
                new KernelPolicy("PackedBranchless16", KernelControlFlow.Branchless, 16),
            };
            var executions = new[] { ExecutionPolicy.FrameFaithful, ExecutionPolicy.DependencyChain,
                ExecutionPolicy.TemporalBlock(4, true) };
            var cells = new List<ParticleMatrixCell>();
            foreach (var layout in layouts)
            foreach (var kernel in kernels)
            for (int execution = 0; execution < executions.Length; execution++)
            for (int batch = 0; batch < batches.Length; batch++)
            {
                // Retain every pre-existing definition, including its hashed sort order.
                int family = layout.PolicyId == "AoS" ? (kernel.VectorWidth == 1 && kernel.ControlFlow == KernelControlFlow.Branched ? 0 : 1)
                    : layout.PolicyId == "SoA" && kernel.ControlFlow == KernelControlFlow.Branched ? 2
                    : layout.PolicyId == "AoSoA8" && kernel.VectorWidth == 8 ? 3
                    : 4 + Array.IndexOf(layouts, layout) * 5 + Array.IndexOf(kernels, kernel);
                int canonicalBatch = Array.IndexOf(new[] { 32, 64, 128, 256 }, batches[batch]);
                var candidate = new CandidateDescriptor(layout, kernel, BatchPolicy.JobBatch(batches[batch]),
                    executions[execution], layout.PolicyId == "AoS",
                    family * 100 + execution * 10 + (canonicalBatch >= 0 ? canonicalBatch : batches[batch]));
                string reason = UnsupportedReason(candidate);
                int width = layout.BlockWidth;
                cells.Add(new ParticleMatrixCell
                {
                    Candidate = candidate, Supported = reason.Length == 0, Reason = reason,
                    ScheduleUnit = width > 1 ? "hot-block" : "record",
                    RecordsPerScheduleUnit = width,
                    PhysicalBatchSize = Math.Max(1, batches[batch] / width),
                    TailPolicy = width > 1 ? "Zero-initialized padding lanes execute; only logical lanes export." : "No padding lanes.",
                });
            }
            return cells.ToArray();
        }

        public static string UnsupportedReason(CandidateDescriptor candidate)
        {
            candidate.ValidateFactorConsistency();
            var layout = candidate.EffectiveLayout;
            var kernel = candidate.EffectiveKernel;
            var execution = candidate.EffectiveExecution;
            if (layout.AlignmentBytes != 0)
                return "Unavailable: no allocator contract guarantees requested base alignment; stride padding is a separate control.";
            int width = layout.PolicyId == "AoSoA4" ? 4 : layout.PolicyId == "AoSoA8" ? 8 : layout.PolicyId == "AoSoA16" ? 16 : 1;
            if (layout.PolicyId != "AoS" && layout.PolicyId != "SoA" && layout.PolicyId != "AoSPadded64" && width == 1)
                return "Unavailable: no storage implementation for layout.";
            if (layout.BlockWidth != width || layout.PaddingBytes != (layout.PolicyId == "AoSPadded64" ? 16 : 0))
                return "Unavailable: metadata does not match physical block width or padding.";
            if (!execution.Equals(ExecutionPolicy.FrameFaithful) && !execution.Equals(ExecutionPolicy.DependencyChain))
                return "Unavailable: temporal reordering is not implemented by the frame-observable workload contract.";
            if (kernel.Equals(new KernelPolicy("ScalarBranched", KernelControlFlow.Branched)) ||
                kernel.Equals(new KernelPolicy("ScalarBranchless", KernelControlFlow.Branchless))) return "";
            if (width > 1 && kernel.Equals(new KernelPolicy("PackedBranchless" + width, KernelControlFlow.Branchless, width))) return "";
            return "Unavailable: packed kernel requires its exact component-packed block width; gather/repacking would introduce another factor.";
        }
    }
}
