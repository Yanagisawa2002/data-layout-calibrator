using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Unity.Collections;
using Unity.Jobs;
using Yanagisawa.DataLayoutCalibrator.Samples.ExternalWorkloads;

namespace Yanagisawa.DataLayoutCalibrator.Benchmark
{
    // Explicit functional Player entry. No timing samples from these fixtures
    // enter performance statistics. Decimal accumulates exact dyadic inputs,
    // independently of the production reduction's partition and merge order.
    internal static class BabelDotCorrectness
    {
        [Serializable] public sealed class Case
        {
            public string name, inputSha256;
            public int count, chunkLength;
            public double expected, actual, serial;
            public bool exact, repeatBitsEqual;
        }
        [Serializable] public sealed class Receipt
        {
            public bool passed;
            public string criterion = "Exact binary64 equality to independent exact decimal oracle for dyadic fixtures; repeat bits equal";
            public Case[] cases;
            public int negativeControlsRejected;
        }

        public static Receipt Run()
        {
            var cases = new List<Case>();
            foreach (int n in new[] { 0, 1, 3, 6, 7, 8, 63, 64, 65, 65535, 65536, 65537, 196615 })
                cases.Add(Check("nonuniform", n, n < 100 ? 7 : 65536));
            foreach (int chunk in new[] { 1, 7, 16384, 65536, 262144 })
            {
                cases.Add(Check("tail-and-boundaries", 2 * chunk + 7, chunk));
                cases.Add(Check("within-chunk-cancellation", 3 * chunk + 3, chunk));
                cases.Add(Check("across-chunk-cancellation", 2 * chunk + 1, chunk));
            }
            int rejected = 0;
            if (!BabelStreamContract.Matches(double.PositiveInfinity, 1)) rejected++;
            if (!BabelStreamContract.Matches(double.NaN, 1)) rejected++;
            if (!BabelStreamContract.Matches(1, double.PositiveInfinity)) rejected++;
            if (!BabelStreamContract.Matches(1, double.NaN)) rejected++;
            try { BabelDotReduction.PartialCount(1, 0); } catch (ArgumentOutOfRangeException) { rejected++; }
            try { BabelDotReduction.PartialCount(-1); } catch (ArgumentOutOfRangeException) { rejected++; }
            if (BabelDotReduction.PartialCount(int.MaxValue, 65536) != 32768) throw new Exception("Count overflow");
            // A deliberately omitted nonzero tail/block and serial cancellation
            // must actually fail the SAME exact criterion used for the fixture.
            var cancellation = cases.Find(c => c.name == "within-chunk-cancellation" && c.chunkLength == 65536);
            if (cancellation.serial != cancellation.expected) rejected++;
            var tail = cases.Find(c => c.name == "tail-and-boundaries" && c.chunkLength == 65536);
            if (tail.actual - 17 != tail.expected) rejected++;
            if (tail.actual - 11 != tail.expected) rejected++;
            if (rejected != 9) throw new Exception("Correctness negative control failed: " + rejected);
            return new Receipt { passed = true, cases = cases.ToArray(), negativeControlsRejected = rejected };
        }

        private static Case Check(string name, int n, int chunk)
        {
            using var a = new NativeArray<double>(n, Allocator.Persistent);
            using var b = new NativeArray<double>(n, Allocator.Persistent);
            using var partials = new NativeArray<BabelDotPartial>(BabelDotReduction.PartialCount(n, chunk), Allocator.Persistent);
            using var output = new NativeArray<double>(1, Allocator.Persistent);
            var wa = a; var wb = b; var wp = partials; var wo = output;
            decimal oracle = 0;
            for (int i = 0; i < n; i++)
            {
                long x = (i * 17L % 97) - 48, y = (i * 29L % 89) - 44;
                if (name == "within-chunk-cancellation") { x = i % 3 == 0 ? (1L << 57) : i % 3 == 1 ? 8 : -(1L << 57); y = 16; }
                if (name == "tail-and-boundaries")
                {
                    x = i == n - 1 ? 136 : i == chunk ? 88 : i == chunk - 1 ? 40 : i == 0 ? 24 : 0;
                    y = 16;
                }
                if (name == "across-chunk-cancellation")
                {
                    // Keep correction separate until the negative large partial cancels.
                    x = i == 0 ? (1L << 57) : i == chunk ? -(1L << 57) : i == n - 1 ? 24 : i == chunk - 1 ? 8 : 0;
                    y = 16;
                }
                wa[i] = x / 8.0; wb[i] = y / 16.0;
                oracle += ((decimal)x * y) / 128m;
            }
            var bytes = new byte[n * 16];
            Buffer.BlockCopy(a.ToArray(), 0, bytes, 0, n * 8);
            Buffer.BlockCopy(b.ToArray(), 0, bytes, n * 8, n * 8);
            using var sha = SHA256.Create();
            string hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            double expected = (double)oracle;
            new BabelDotContractJob { A = a, B = b, Sum = output }.Schedule().Complete();
            double serial = output[0];
            long firstBits = 0;
            for (int repeat = 0; repeat < 3; repeat++)
            {
                // Poison every slot, catching unassigned blocks and stale scratch reuse.
                for (int i = 0; i < partials.Length; i++) wp[i] = new BabelDotPartial { Sum = double.NaN, Correction = double.NaN };
                wo[0] = double.NaN;
                BabelDotReduction.Schedule(a, b, partials, output, chunk).Complete();
                if (double.IsNaN(output[0]) || double.IsInfinity(output[0]) || output[0] != expected)
                    throw new Exception($"Dot fixture {name} n={n} chunk={chunk}: {output[0]:R} != {expected:R}");
                long bits = BitConverter.DoubleToInt64Bits(output[0]);
                if (repeat == 0) firstBits = bits;
                else if (bits != firstBits) throw new Exception("Nondeterministic reduction");
            }
            return new Case { name = name, count = n, chunkLength = chunk, inputSha256 = hash,
                expected = expected, actual = output[0], serial = serial, exact = true, repeatBitsEqual = true };
        }
    }
}
