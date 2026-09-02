using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Yanagisawa.DataLayoutCalibrator.SourceGenerator.Tests
{
    [TestFixture]
    public sealed class DataLayoutScaffoldGeneratorTests
    {
        private const string ScaffoldProtocolSource = @"
namespace Yanagisawa.DataLayoutCalibrator
{
    public enum DataLayoutFieldTemperature { Hot = 0, Cold = 1 }
    public enum DataLayoutFieldSemantics { Value = 0 }

    [System.AttributeUsage(System.AttributeTargets.Struct)]
    public sealed class GenerateDataLayoutAttribute : System.Attribute
    {
        public GenerateDataLayoutAttribute(string schemaId, int schemaVersion, int aoSoABlockSize = 8) { }
        public int MinimumCompatibleSchemaVersion { get; set; }
        public int DefinitionVersion { get; set; } = 1;
    }

    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class DataLayoutFieldAttribute : System.Attribute
    {
        public DataLayoutFieldAttribute(int order, DataLayoutFieldTemperature temperature) { }
        public DataLayoutFieldSemantics Semantics { get; set; }
    }
}

namespace Unity.Mathematics
{
    public struct float3 { public float x, y, z; }
    public struct float4 { public float x, y, z, w; }
    public struct quaternion { public float4 value; }
    public struct float4x4 { public float4 c0, c1, c2, c3; }
}

namespace Unity.Collections
{
    public enum Allocator { Temp = 0, Persistent = 1 }
    public enum NativeArrayOptions { UninitializedMemory = 0, ClearMemory = 1 }

    public struct NativeArray<T> where T : struct
    {
        public NativeArray(int length, Allocator allocator, NativeArrayOptions options) { }
        public bool IsCreated => true;
        public int Length => 0;
        public T this[int index] { get => default; set { } }
        public void Dispose() { }
    }
}
";

        [Test]
        public void TwoDifferentWorkloadSchemasEmitAllBoundedScaffoldsAndCompile()
        {
            const string source = @"
using Yanagisawa.DataLayoutCalibrator;
using Unity.Mathematics;

namespace Samples.Particles
{
    [GenerateDataLayout(""particle-record"", 2, 8, MinimumCompatibleSchemaVersion = 1)]
    public struct ParticleRecord
    {
        [DataLayoutField(0, DataLayoutFieldTemperature.Hot)] public float3 Position;
        [DataLayoutField(1, DataLayoutFieldTemperature.Hot)] public float3 Velocity;
        [DataLayoutField(2, DataLayoutFieldTemperature.Cold)] public quaternion Rotation;
        [DataLayoutField(3, DataLayoutFieldTemperature.Hot)] public float Lifetime;
        [DataLayoutField(4, DataLayoutFieldTemperature.Cold)] public int Category;
    }
}

namespace Samples.Transforms
{
    [GenerateDataLayout(""transform-export-record"", 1, 4)]
    public struct TransformExportRecord
    {
        [DataLayoutField(0, DataLayoutFieldTemperature.Hot)] public float4x4 LocalToWorld;
        [DataLayoutField(1, DataLayoutFieldTemperature.Hot)] public int EntityId;
        [DataLayoutField(2, DataLayoutFieldTemperature.Hot)] public int Flags;
    }
}
";

            GeneratorDriverRunResult result = Run(source);
            string generated = string.Join(
                Environment.NewLine,
                result.Results.Single().GeneratedSources.Select(item => item.SourceText.ToString()));
            Assert.That(result.Results.Single().GeneratedSources.Count, Is.EqualTo(2));
            Assert.That(generated, Does.Contain("ParticleRecordGeneratedAoSStorage"));
            Assert.That(generated, Does.Contain("ParticleRecordGeneratedSoAStorage"));
            Assert.That(generated, Does.Contain("ParticleRecordGeneratedAoSoA8Storage"));
            Assert.That(generated, Does.Contain("ParticleRecordGeneratedDataLayoutCodec"));
            Assert.That(generated, Does.Contain("ParticleRecordGeneratedParityFieldMap"));
            Assert.That(generated, Does.Contain("Cold_Category"));
            Assert.That(generated, Does.Contain("TransformExportRecordGeneratedAoSoA4Storage"));
            Assert.That(generated, Does.Not.Contain("Activator"));
            Assert.That(generated, Does.Not.Contain("System.Reflection"));
            Assert.That(generated, Does.Not.Contain("BurstCompiler"));
        }

        [Test]
        public void OutputIsDeterministicAcrossDeclarationOrder()
        {
            const string alpha = @"
[Yanagisawa.DataLayoutCalibrator.GenerateDataLayout(""alpha"", 1, 4)]
public struct Alpha
{
    [Yanagisawa.DataLayoutCalibrator.DataLayoutField(0, Yanagisawa.DataLayoutCalibrator.DataLayoutFieldTemperature.Hot)] public float Value;
}
";
            const string zebra = @"
[Yanagisawa.DataLayoutCalibrator.GenerateDataLayout(""zebra"", 1, 4)]
public struct Zebra
{
    [Yanagisawa.DataLayoutCalibrator.DataLayoutField(0, Yanagisawa.DataLayoutCalibrator.DataLayoutFieldTemperature.Hot)] public int Value;
}
";

            GeneratorDriverRunResult first = Run(alpha + zebra);
            GeneratorDriverRunResult second = Run(zebra + alpha);

            string[] firstSources = first.Results.Single().GeneratedSources
                .OrderBy(item => item.HintName, StringComparer.Ordinal)
                .Select(item => item.HintName + "\n" + item.SourceText)
                .ToArray();
            string[] secondSources = second.Results.Single().GeneratedSources
                .OrderBy(item => item.HintName, StringComparer.Ordinal)
                .Select(item => item.HintName + "\n" + item.SourceText)
                .ToArray();
            Assert.That(secondSources, Is.EqualTo(firstSources));
        }

        [Test]
        public void ReferenceAndNestedFieldTypesProduceDlcgen102()
        {
            const string source = @"
using Yanagisawa.DataLayoutCalibrator;
[GenerateDataLayout(""unsupported-record"", 1)]
public struct UnsupportedRecord
{
    [DataLayoutField(0, DataLayoutFieldTemperature.Hot)] public string Name;
}
";

            GeneratorDriverRunResult result = Run(source, assertCompilerSuccess: false);

            Assert.That(result.Diagnostics.Select(item => item.Id), Does.Contain("DLCGEN102"));
            Assert.That(result.Results.Single().GeneratedSources, Is.Empty);
        }

        [Test]
        public void ExplicitLayoutAndFieldOffsetsProduceDlcgen104()
        {
            const string source = @"
using System.Runtime.InteropServices;
using Yanagisawa.DataLayoutCalibrator;
[StructLayout(LayoutKind.Explicit)]
[GenerateDataLayout(""aliased-record"", 1)]
public struct AliasedRecord
{
    [FieldOffset(0)] [DataLayoutField(0, DataLayoutFieldTemperature.Hot)] public int First;
    [FieldOffset(0)] [DataLayoutField(1, DataLayoutFieldTemperature.Hot)] public float Alias;
}
";

            GeneratorDriverRunResult result = Run(source, assertCompilerSuccess: false);

            Assert.That(result.Diagnostics.Select(item => item.Id), Does.Contain("DLCGEN104"));
            Assert.That(result.Results.Single().GeneratedSources, Is.Empty);
        }

        [Test]
        public void DuplicateOrderAndUnknownSemanticsProduceDiagnostics()
        {
            const string source = @"
using Yanagisawa.DataLayoutCalibrator;
[GenerateDataLayout(""bad-fields"", 1)]
public struct BadFields
{
    [DataLayoutField(0, DataLayoutFieldTemperature.Hot)] public int First;
    [DataLayoutField(0, DataLayoutFieldTemperature.Cold, Semantics = (DataLayoutFieldSemantics)99)] public int Second;
}
";

            GeneratorDriverRunResult result = Run(source, assertCompilerSuccess: false);
            string[] ids = result.Diagnostics.Select(item => item.Id).ToArray();

            Assert.That(ids, Does.Contain("DLCGEN103"));
            Assert.That(ids, Does.Contain("DLCGEN105"));
        }

        [Test]
        public void DeclaredSchemaCompatibilityRangeIsEmittedButUnsupportedDefinitionIsRejected()
        {
            const string valid = @"
using Yanagisawa.DataLayoutCalibrator;
[GenerateDataLayout(""migrated-record"", 3, 4, MinimumCompatibleSchemaVersion = 2)]
public struct MigratedRecord
{
    [DataLayoutField(0, DataLayoutFieldTemperature.Hot)] public long Value;
}
";
            GeneratorDriverRunResult validResult = Run(valid);
            string generated = validResult.Results.Single().GeneratedSources.Single().SourceText.ToString();
            Assert.That(generated, Does.Contain("SchemaVersion = 3"));
            Assert.That(generated, Does.Contain("MinimumCompatibleSchemaVersion = 2"));
            Assert.That(generated, Does.Contain("IsDeclaredCompatibleVersion"));
            Assert.That(generated, Does.Match("SchemaHashSha256 = \"[0-9A-F]{64}\""));

            GeneratorDriverRunResult invalidResult = Run(
                valid.Replace(
                    "MinimumCompatibleSchemaVersion = 2",
                    "MinimumCompatibleSchemaVersion = 2, DefinitionVersion = 2"),
                assertCompilerSuccess: false);
            Assert.That(invalidResult.Diagnostics.Select(item => item.Id), Does.Contain("DLCGEN100"));
        }

        [Test]
        public void DuplicateSchemaIdentityProducesDlcgen106()
        {
            const string source = @"
using Yanagisawa.DataLayoutCalibrator;
[GenerateDataLayout(""duplicate"", 1)]
public struct First
{
    [DataLayoutField(0, DataLayoutFieldTemperature.Hot)] public int Value;
}
[GenerateDataLayout(""duplicate"", 1)]
public struct Second
{
    [DataLayoutField(0, DataLayoutFieldTemperature.Hot)] public int Value;
}
";
            GeneratorDriverRunResult result = Run(source, assertCompilerSuccess: false);

            Assert.That(result.Diagnostics.Select(item => item.Id), Does.Contain("DLCGEN106"));
            Assert.That(result.Results.Single().GeneratedSources, Is.Empty);
        }

        private static GeneratorDriverRunResult Run(
            string source,
            bool assertCompilerSuccess = true)
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp10);
            SyntaxTree[] syntaxTrees =
            {
                CSharpSyntaxTree.ParseText(ScaffoldProtocolSource, parseOptions),
                CSharpSyntaxTree.ParseText(source, parseOptions),
            };
            var compilation = CSharpCompilation.Create(
                "DataLayoutScaffoldGeneratorTests",
                syntaxTrees,
                PlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { new DataLayoutScaffoldGenerator() },
                parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out _);
            if (assertCompilerSuccess)
            {
                Diagnostic[] compilerErrors = outputCompilation.GetDiagnostics()
                    .Where(item => item.Severity == DiagnosticSeverity.Error)
                    .Where(item => !item.Id.StartsWith("DLCGEN", StringComparison.Ordinal))
                    .ToArray();
                Assert.That(
                    compilerErrors,
                    Is.Empty,
                    string.Join(Environment.NewLine, compilerErrors.AsEnumerable()));
            }

            return driver.GetRunResult();
        }

        private static IEnumerable<MetadataReference> PlatformReferences()
        {
            string? paths = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (string.IsNullOrEmpty(paths))
                throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
            return paths.Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path));
        }
    }
}
