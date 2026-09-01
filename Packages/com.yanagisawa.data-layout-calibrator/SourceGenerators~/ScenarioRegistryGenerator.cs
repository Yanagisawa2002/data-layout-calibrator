using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Yanagisawa.DataLayoutCalibrator.SourceGenerator
{
    /// <summary>
    /// Emits an AOT-safe, strongly typed scenario factory registry from explicit
    /// assembly attributes.
    /// </summary>
    [Generator]
    public sealed class ScenarioRegistryGenerator : ISourceGenerator
    {
        private const string AttributeMetadataName =
            "Yanagisawa.DataLayoutCalibrator.RegisterCalibrationScenarioFactoryAttribute";

        private const string FactoryInterfaceMetadataName =
            "Yanagisawa.DataLayoutCalibrator.ICalibrationScenarioFactory";

        private static readonly DiagnosticDescriptor InvalidFactory = new DiagnosticDescriptor(
            "DLCGEN001",
            "Invalid calibration scenario factory",
            "The registered type '{0}' {1}",
            "DataLayoutCalibrator.SourceGeneration",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor DuplicateFactory = new DiagnosticDescriptor(
            "DLCGEN002",
            "Duplicate calibration scenario factory",
            "The scenario factory '{0}' is registered more than once",
            "DataLayoutCalibrator.SourceGeneration",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <inheritdoc />
        public void Initialize(GeneratorInitializationContext context)
        {
        }

        /// <inheritdoc />
        public void Execute(GeneratorExecutionContext context)
        {
            INamedTypeSymbol? registrationAttribute =
                context.Compilation.GetTypeByMetadataName(AttributeMetadataName);
            INamedTypeSymbol? factoryInterface =
                context.Compilation.GetTypeByMetadataName(FactoryInterfaceMetadataName);
            if (registrationAttribute == null || factoryInterface == null)
                return;

            var registrations = new List<Registration>();
            var seen = new List<INamedTypeSymbol>();
            foreach (AttributeData attribute in context.Compilation.Assembly.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, registrationAttribute))
                    continue;

                Location location = attribute.ApplicationSyntaxReference?
                    .GetSyntax(context.CancellationToken)
                    .GetLocation() ?? Location.None;
                INamedTypeSymbol? factoryType = GetRegisteredType(attribute);
                if (factoryType == null)
                {
                    ReportInvalid(context, location, "<missing>", "must name a concrete factory type");
                    continue;
                }

                string displayName = factoryType.ToDisplayString();
                string? invalidReason = ValidateFactory(
                    factoryType,
                    factoryInterface,
                    context.Compilation.Assembly);
                if (invalidReason != null)
                {
                    ReportInvalid(context, location, displayName, invalidReason);
                    continue;
                }

                if (seen.Any(candidate =>
                        SymbolEqualityComparer.Default.Equals(candidate, factoryType)))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DuplicateFactory,
                        location,
                        displayName));
                    continue;
                }

                seen.Add(factoryType);
                registrations.Add(new Registration(
                    factoryType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            }

            if (registrations.Count == 0)
                return;

            registrations.Sort((left, right) =>
                StringComparer.Ordinal.Compare(left.FullyQualifiedTypeName, right.FullyQualifiedTypeName));
            context.AddSource(
                "GeneratedCalibrationScenarioRegistry.g.cs",
                SourceText.From(GenerateRegistry(registrations), Encoding.UTF8));
        }

        private static INamedTypeSymbol? GetRegisteredType(AttributeData attribute)
        {
            if (attribute.ConstructorArguments.Length != 1)
                return null;
            return attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
        }

        private static string? ValidateFactory(
            INamedTypeSymbol factoryType,
            INamedTypeSymbol factoryInterface,
            IAssemblySymbol generatedAssembly)
        {
            if (factoryType.TypeKind != TypeKind.Class || factoryType.IsAbstract)
                return "must be a non-abstract class";
            if (factoryType.Arity != 0)
                return "must be a non-generic class";
            if (!factoryType.AllInterfaces.Any(candidate =>
                    SymbolEqualityComparer.Default.Equals(candidate, factoryInterface)))
                return $"must implement {FactoryInterfaceMetadataName}";
            if (!IsTypeAccessible(factoryType, generatedAssembly))
                return "is not accessible from the assembly containing the registration";
            if (!factoryType.InstanceConstructors.Any(constructor =>
                    constructor.Parameters.Length == 0 &&
                    IsConstructorAccessible(constructor, factoryType, generatedAssembly)))
                return "must expose an accessible parameterless constructor";
            return null;
        }

        private static bool IsTypeAccessible(INamedTypeSymbol type, IAssemblySymbol generatedAssembly)
        {
            for (INamedTypeSymbol? current = type; current != null; current = current.ContainingType)
            {
                bool sameAssembly = SymbolEqualityComparer.Default.Equals(
                    current.ContainingAssembly,
                    generatedAssembly);
                if (current.DeclaredAccessibility == Accessibility.Public)
                    continue;
                if (sameAssembly && current.DeclaredAccessibility == Accessibility.Internal)
                    continue;
                return false;
            }

            return true;
        }

        private static bool IsConstructorAccessible(
            IMethodSymbol constructor,
            INamedTypeSymbol factoryType,
            IAssemblySymbol generatedAssembly)
        {
            if (constructor.DeclaredAccessibility == Accessibility.Public)
                return true;

            bool sameAssembly = SymbolEqualityComparer.Default.Equals(
                factoryType.ContainingAssembly,
                generatedAssembly);
            return sameAssembly &&
                   (constructor.DeclaredAccessibility == Accessibility.Internal ||
                    constructor.DeclaredAccessibility == Accessibility.ProtectedOrInternal);
        }

        private static void ReportInvalid(
            GeneratorExecutionContext context,
            Location location,
            string displayName,
            string reason)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidFactory,
                location,
                displayName,
                reason));
        }

        private static string GenerateRegistry(IReadOnlyList<Registration> registrations)
        {
            var source = new StringBuilder(1024);
            source.AppendLine("// <auto-generated />")
                .AppendLine("namespace Yanagisawa.DataLayoutCalibrator.Generated")
                .AppendLine("{")
                .AppendLine("    internal static class GeneratedCalibrationScenarioRegistry")
                .AppendLine("    {")
                .AppendLine("        internal static global::Yanagisawa.DataLayoutCalibrator.ICalibrationScenarioFactory[] CreateFactories()")
                .AppendLine("        {")
                .AppendLine("            return new global::Yanagisawa.DataLayoutCalibrator.ICalibrationScenarioFactory[]")
                .AppendLine("            {");
            for (int index = 0; index < registrations.Count; index++)
            {
                source.Append("                new ")
                    .Append(registrations[index].FullyQualifiedTypeName)
                    .AppendLine("(),");
            }

            source.AppendLine("            };")
                .AppendLine("        }")
                .AppendLine("    }")
                .AppendLine("}");
            return source.ToString();
        }

        private sealed class Registration
        {
            public Registration(string fullyQualifiedTypeName)
            {
                FullyQualifiedTypeName = fullyQualifiedTypeName;
            }

            public string FullyQualifiedTypeName { get; }
        }
    }
}
