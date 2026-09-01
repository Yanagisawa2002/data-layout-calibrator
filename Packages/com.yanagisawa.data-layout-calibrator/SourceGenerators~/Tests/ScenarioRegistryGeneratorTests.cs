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
    public sealed class ScenarioRegistryGeneratorTests
    {
        private const string ProtocolSource = @"
namespace Yanagisawa.DataLayoutCalibrator
{
    public interface ICalibrationScenarioFactory { }

    [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class RegisterCalibrationScenarioFactoryAttribute : System.Attribute
    {
        public RegisterCalibrationScenarioFactoryAttribute(System.Type factoryType) { }
    }
}
";

        [Test]
        public void ValidFactoriesEmitDirectConstructionInDeterministicOrder()
        {
            const string source = @"
using Yanagisawa.DataLayoutCalibrator;
[assembly: RegisterCalibrationScenarioFactory(typeof(Samples.ZebraFactory))]
[assembly: RegisterCalibrationScenarioFactory(typeof(Samples.AlphaFactory))]

namespace Samples
{
    public sealed class ZebraFactory : ICalibrationScenarioFactory { }
    public sealed class AlphaFactory : ICalibrationScenarioFactory { }
}
";

            GeneratorDriverRunResult result = Run(source);

            Assert.That(result.Diagnostics, Is.Empty);
            string generated = result.Results.Single().GeneratedSources.Single().SourceText.ToString();
            int alpha = generated.IndexOf("new global::Samples.AlphaFactory()", StringComparison.Ordinal);
            int zebra = generated.IndexOf("new global::Samples.ZebraFactory()", StringComparison.Ordinal);
            Assert.That(alpha, Is.GreaterThanOrEqualTo(0));
            Assert.That(zebra, Is.GreaterThan(alpha));
            Assert.That(generated, Does.Not.Contain("Reflection"));
            Assert.That(generated, Does.Not.Contain("Activator"));
        }

        [Test]
        public void DuplicateFactoryProducesDlcgen002()
        {
            const string source = @"
using Yanagisawa.DataLayoutCalibrator;
[assembly: RegisterCalibrationScenarioFactory(typeof(Samples.Factory))]
[assembly: RegisterCalibrationScenarioFactory(typeof(Samples.Factory))]

namespace Samples
{
    public sealed class Factory : ICalibrationScenarioFactory { }
}
";

            GeneratorDriverRunResult result = Run(source);

            Assert.That(result.Diagnostics.Select(item => item.Id), Does.Contain("DLCGEN002"));
        }

        [Test]
        public void NonFactoryProducesDlcgen001()
        {
            const string source = @"
using Yanagisawa.DataLayoutCalibrator;
[assembly: RegisterCalibrationScenarioFactory(typeof(Samples.NotAFactory))]

namespace Samples
{
    public sealed class NotAFactory { }
}
";

            GeneratorDriverRunResult result = Run(source);

            Assert.That(result.Diagnostics.Select(item => item.Id), Does.Contain("DLCGEN001"));
        }

        [Test]
        public void AssemblyWithoutRegistrationsProducesNoSource()
        {
            GeneratorDriverRunResult result = Run(string.Empty);

            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Results.Single().GeneratedSources, Is.Empty);
        }

        private static GeneratorDriverRunResult Run(string scenarioSource)
        {
            var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp10);
            SyntaxTree[] syntaxTrees =
            {
                CSharpSyntaxTree.ParseText(ProtocolSource, parseOptions),
                CSharpSyntaxTree.ParseText(scenarioSource, parseOptions),
            };

            var compilation = CSharpCompilation.Create(
                "SourceGeneratorTests",
                syntaxTrees,
                PlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new ISourceGenerator[] { new ScenarioRegistryGenerator() },
                parseOptions: parseOptions);
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out _);
            Diagnostic[] compilerErrors = outputCompilation.GetDiagnostics()
                .Where(item => item.Severity == DiagnosticSeverity.Error)
                .Where(item => !item.Id.StartsWith("DLCGEN", StringComparison.Ordinal))
                .ToArray();
            Assert.That(compilerErrors, Is.Empty, string.Join(Environment.NewLine, compilerErrors.AsEnumerable()));
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
