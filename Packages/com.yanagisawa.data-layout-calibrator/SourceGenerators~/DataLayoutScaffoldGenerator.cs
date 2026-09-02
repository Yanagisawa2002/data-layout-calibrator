using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Yanagisawa.DataLayoutCalibrator.SourceGenerator
{
    /// <summary>
    /// Emits bounded mechanical storage, boundary codec, and parity field-map
    /// scaffolds for explicit flat record schemas. It never emits workload Jobs,
    /// schedules kernels, or rewrites developer code.
    /// </summary>
    [Generator]
    public sealed class DataLayoutScaffoldGenerator : ISourceGenerator
    {
        private const string RecordAttributeMetadataName =
            "Yanagisawa.DataLayoutCalibrator.GenerateDataLayoutAttribute";
        private const string FieldAttributeMetadataName =
            "Yanagisawa.DataLayoutCalibrator.DataLayoutFieldAttribute";

        private static readonly DiagnosticDescriptor InvalidSchema = new DiagnosticDescriptor(
            "DLCGEN100",
            "Invalid generated data-layout schema",
            "The record '{0}' has an invalid schema declaration: {1}",
            "DataLayoutCalibrator.SourceGeneration",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor InvalidRecord = new DiagnosticDescriptor(
            "DLCGEN101",
            "Unsupported generated data-layout record",
            "The record '{0}' is unsupported: {1}",
            "DataLayoutCalibrator.SourceGeneration",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor UnsupportedField = new DiagnosticDescriptor(
            "DLCGEN102",
            "Unsupported generated data-layout field",
            "The field '{0}' is unsupported: {1}",
            "DataLayoutCalibrator.SourceGeneration",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor InvalidFieldOrder = new DiagnosticDescriptor(
            "DLCGEN103",
            "Invalid generated data-layout field order",
            "The field '{0}' has an invalid schema position: {1}",
            "DataLayoutCalibrator.SourceGeneration",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor UnsupportedLayout = new DiagnosticDescriptor(
            "DLCGEN104",
            "Unsupported generated data-layout alignment or aliasing",
            "The record or field '{0}' uses unsupported layout semantics: {1}",
            "DataLayoutCalibrator.SourceGeneration",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor UnsupportedSemantics = new DiagnosticDescriptor(
            "DLCGEN105",
            "Unsupported generated data-layout field semantics",
            "The field '{0}' uses unsupported semantics value '{1}'",
            "DataLayoutCalibrator.SourceGeneration",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor DuplicateSchema = new DiagnosticDescriptor(
            "DLCGEN106",
            "Duplicate generated data-layout schema identity",
            "The schema ID '{0}' is declared by both '{1}' and '{2}'",
            "DataLayoutCalibrator.SourceGeneration",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private static readonly HashSet<string> SupportedMathTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Unity.Mathematics.float2",
            "Unity.Mathematics.float3",
            "Unity.Mathematics.float4",
            "Unity.Mathematics.double2",
            "Unity.Mathematics.double3",
            "Unity.Mathematics.double4",
            "Unity.Mathematics.int2",
            "Unity.Mathematics.int3",
            "Unity.Mathematics.int4",
            "Unity.Mathematics.uint2",
            "Unity.Mathematics.uint3",
            "Unity.Mathematics.uint4",
            "Unity.Mathematics.quaternion",
            "Unity.Mathematics.float2x2",
            "Unity.Mathematics.float2x3",
            "Unity.Mathematics.float2x4",
            "Unity.Mathematics.float3x2",
            "Unity.Mathematics.float3x3",
            "Unity.Mathematics.float3x4",
            "Unity.Mathematics.float4x2",
            "Unity.Mathematics.float4x3",
            "Unity.Mathematics.float4x4",
            "Unity.Mathematics.double2x2",
            "Unity.Mathematics.double2x3",
            "Unity.Mathematics.double2x4",
            "Unity.Mathematics.double3x2",
            "Unity.Mathematics.double3x3",
            "Unity.Mathematics.double3x4",
            "Unity.Mathematics.double4x2",
            "Unity.Mathematics.double4x3",
            "Unity.Mathematics.double4x4",
        };

        /// <inheritdoc />
        public void Initialize(GeneratorInitializationContext context)
        {
        }

        /// <inheritdoc />
        public void Execute(GeneratorExecutionContext context)
        {
            INamedTypeSymbol? recordAttribute =
                context.Compilation.GetTypeByMetadataName(RecordAttributeMetadataName);
            INamedTypeSymbol? fieldAttribute =
                context.Compilation.GetTypeByMetadataName(FieldAttributeMetadataName);
            if (recordAttribute == null || fieldAttribute == null)
                return;

            var schemas = new List<RecordSchema>();
            foreach (INamedTypeSymbol type in EnumerateTypes(context.Compilation.Assembly.GlobalNamespace))
            {
                AttributeData? attribute = FindAttribute(type.GetAttributes(), recordAttribute);
                if (attribute == null)
                    continue;
                RecordSchema? schema = CreateSchema(context, type, attribute, fieldAttribute);
                if (schema != null)
                    schemas.Add(schema);
            }

            schemas.Sort((left, right) =>
                StringComparer.Ordinal.Compare(left.FullyQualifiedRecordType, right.FullyQualifiedRecordType));
            var schemaOwners = new Dictionary<string, RecordSchema>(StringComparer.Ordinal);
            var duplicates = new HashSet<RecordSchema>();
            foreach (RecordSchema schema in schemas)
            {
                if (schemaOwners.TryGetValue(schema.SchemaId, out RecordSchema? existing))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DuplicateSchema,
                        schema.Location,
                        schema.SchemaId,
                        existing.FullyQualifiedRecordType,
                        schema.FullyQualifiedRecordType));
                    duplicates.Add(existing);
                    duplicates.Add(schema);
                }
                else
                {
                    schemaOwners.Add(schema.SchemaId, schema);
                }
            }

            foreach (RecordSchema schema in schemas)
            {
                if (duplicates.Contains(schema))
                    continue;
                context.AddSource(
                    schema.HintName,
                    SourceText.From(Generate(schema), Encoding.UTF8));
            }
        }

        private static RecordSchema? CreateSchema(
            GeneratorExecutionContext context,
            INamedTypeSymbol type,
            AttributeData attribute,
            INamedTypeSymbol fieldAttribute)
        {
            Location location = AttributeLocation(attribute, type);
            string displayName = type.ToDisplayString();
            if (attribute.ConstructorArguments.Length < 2 ||
                !(attribute.ConstructorArguments[0].Value is string schemaId) ||
                !(attribute.ConstructorArguments[1].Value is int schemaVersion))
            {
                Report(context, InvalidSchema, location, displayName, "schema ID and version are required");
                return null;
            }

            int blockSize = attribute.ConstructorArguments.Length >= 3 &&
                            attribute.ConstructorArguments[2].Value is int value
                ? value
                : 8;
            int minimumCompatibleVersion = NamedInt(attribute, "MinimumCompatibleSchemaVersion", schemaVersion);
            int definitionVersion = NamedInt(attribute, "DefinitionVersion", 1);
            if (!IsSchemaId(schemaId))
            {
                Report(context, InvalidSchema, location, displayName,
                    "schema ID must match [a-z0-9][a-z0-9._-]*");
                return null;
            }

            if (schemaVersion <= 0 ||
                minimumCompatibleVersion <= 0 ||
                minimumCompatibleVersion > schemaVersion)
            {
                Report(context, InvalidSchema, location, displayName,
                    "schema versions must be positive and the minimum compatible version cannot exceed the current version");
                return null;
            }

            if (definitionVersion != 1)
            {
                Report(context, InvalidSchema, location, displayName,
                    $"definition version {definitionVersion} is unsupported; expected 1");
                return null;
            }

            if (blockSize != 4 && blockSize != 8 && blockSize != 16)
            {
                Report(context, InvalidSchema, location, displayName,
                    $"AoSoA block size {blockSize} is unsupported; expected 4, 8, or 16");
                return null;
            }

            if (type.TypeKind != TypeKind.Struct || type.IsRefLikeType || type.Arity != 0)
            {
                Report(context, InvalidRecord, location, displayName,
                    "only non-generic, non-ref structs are supported");
                return null;
            }

            if (type.ContainingType != null)
            {
                Report(context, InvalidRecord, location, displayName,
                    "nested record types are not supported");
                return null;
            }

            if (type.DeclaredAccessibility != Accessibility.Public &&
                type.DeclaredAccessibility != Accessibility.Internal)
            {
                Report(context, InvalidRecord, location, displayName,
                    "the record must be public or internal");
                return null;
            }

            if (HasUnsupportedStructLayout(type, out string layoutReason))
            {
                Report(context, UnsupportedLayout, location, displayName, layoutReason);
                return null;
            }

            IPropertySymbol? instanceProperty = type.GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(property => !property.IsStatic && !property.IsImplicitlyDeclared);
            if (instanceProperty != null)
            {
                Report(context, InvalidRecord, instanceProperty.Locations.FirstOrDefault() ?? location, displayName,
                    "instance properties are not part of the explicit v1 field schema");
                return null;
            }

            IFieldSymbol[] instanceFields = type.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(field => !field.IsStatic && !field.IsImplicitlyDeclared)
                .ToArray();
            if (instanceFields.Length == 0)
            {
                Report(context, InvalidRecord, location, displayName,
                    "at least one explicit instance field is required");
                return null;
            }

            bool invalid = false;
            var fields = new List<RecordField>(instanceFields.Length);
            var orders = new HashSet<int>();
            foreach (IFieldSymbol field in instanceFields)
            {
                Location fieldLocation = field.Locations.FirstOrDefault() ?? location;
                string fieldName = type.ToDisplayString() + "." + field.Name;
                AttributeData? fieldDeclaration = FindAttribute(field.GetAttributes(), fieldAttribute);
                if (fieldDeclaration == null ||
                    fieldDeclaration.ConstructorArguments.Length != 2 ||
                    !(fieldDeclaration.ConstructorArguments[0].Value is int order) ||
                    !(fieldDeclaration.ConstructorArguments[1].Value is int temperature))
                {
                    Report(context, InvalidFieldOrder, fieldLocation, fieldName,
                        "every instance field needs DataLayoutField(order, temperature)");
                    invalid = true;
                    continue;
                }

                if (order < 0 || !orders.Add(order))
                {
                    Report(context, InvalidFieldOrder, fieldLocation, fieldName,
                        order < 0 ? "order must be non-negative" : $"order {order} is duplicated");
                    invalid = true;
                }

                if (temperature != 0 && temperature != 1)
                {
                    Report(context, UnsupportedSemantics, fieldLocation, fieldName,
                        "temperature=" + temperature.ToString(CultureInfo.InvariantCulture));
                    invalid = true;
                }

                int semantics = NamedInt(fieldDeclaration, "Semantics", 0);
                if (semantics != 0)
                {
                    Report(context, UnsupportedSemantics, fieldLocation, fieldName,
                        semantics.ToString(CultureInfo.InvariantCulture));
                    invalid = true;
                }

                if (field.DeclaredAccessibility != Accessibility.Public &&
                    field.DeclaredAccessibility != Accessibility.Internal)
                {
                    Report(context, UnsupportedField, fieldLocation, fieldName,
                        "the generated codec requires public or internal field access");
                    invalid = true;
                }

                if (field.IsReadOnly || field.IsConst || field.IsFixedSizeBuffer)
                {
                    Report(context, UnsupportedField, fieldLocation, fieldName,
                        "readonly, const, fixed-buffer, ref, and aliased fields are outside the v1 codec");
                    invalid = true;
                }

                if (field.GetAttributes().Any(item =>
                        item.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.FieldOffsetAttribute"))
                {
                    Report(context, UnsupportedLayout, fieldLocation, fieldName,
                        "FieldOffset aliases are not supported");
                    invalid = true;
                }

                if (!field.Type.IsUnmanagedType || !IsSupportedFieldType(field.Type))
                {
                    Report(context, UnsupportedField, fieldLocation, fieldName,
                        $"type '{field.Type.ToDisplayString()}' is not in the bounded scalar/Unity.Mathematics allowlist; nested records, references, and ownership-bearing fields require handwritten storage");
                    invalid = true;
                }

                fields.Add(new RecordField(
                    field.Name,
                    EscapeIdentifier(field.Name),
                    field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    field.Type.ToDisplayString(),
                    order,
                    temperature == 0));
            }

            fields.Sort((left, right) => left.Order.CompareTo(right.Order));
            for (int index = 0; index < fields.Count; index++)
            {
                if (fields[index].Order != index)
                {
                    Report(context, InvalidFieldOrder, location, displayName,
                        $"orders must be contiguous from 0; expected {index} but found {fields[index].Order}");
                    invalid = true;
                    break;
                }
            }

            if (!fields.Any(field => field.IsHot))
            {
                Report(context, InvalidRecord, location, displayName,
                    "at least one field must be marked Hot for the AoSoA scaffold");
                invalid = true;
            }

            if (invalid)
                return null;

            string namespaceName = type.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : type.ContainingNamespace.ToDisplayString();
            string recordName = type.Name;
            string fullyQualified = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string canonicalSchema = BuildCanonicalSchema(
                schemaId,
                schemaVersion,
                minimumCompatibleVersion,
                blockSize,
                fields);
            return new RecordSchema(
                namespaceName,
                recordName,
                fullyQualified,
                schemaId,
                schemaVersion,
                minimumCompatibleVersion,
                blockSize,
                Sha256(canonicalSchema),
                fields,
                location);
        }

        private static string Generate(RecordSchema schema)
        {
            var source = new StringBuilder(32768);
            source.AppendLine("// <auto-generated />")
                .AppendLine("// Copyright (c) 2026 Edwin Liu. All Rights Reserved.")
                .AppendLine("#pragma warning disable")
                .AppendLine();
            if (schema.NamespaceName.Length != 0)
            {
                source.Append("namespace ").Append(schema.NamespaceName).AppendLine()
                    .AppendLine("{");
            }

            string indent = schema.NamespaceName.Length == 0 ? string.Empty : "    ";
            AppendSchemaMetadata(source, schema, indent);
            AppendParityFieldMap(source, schema, indent);
            AppendAoSStorage(source, schema, indent);
            AppendSoAStorage(source, schema, indent);
            AppendAoSoAStorage(source, schema, indent);
            AppendCodec(source, schema, indent);

            if (schema.NamespaceName.Length != 0)
                source.AppendLine("}");
            return source.ToString();
        }

        private static void AppendSchemaMetadata(
            StringBuilder source,
            RecordSchema schema,
            string indent)
        {
            source.Append(indent).Append("public static class ").Append(schema.SchemaType).AppendLine()
                .Append(indent).AppendLine("{")
                .Append(indent).AppendLine("    public const int GeneratorDefinitionVersion = 1;")
                .Append(indent).Append("    public const string SchemaId = ").Append(Literal(schema.SchemaId)).AppendLine(";")
                .Append(indent).Append("    public const int SchemaVersion = ").Append(schema.SchemaVersion).AppendLine(";")
                .Append(indent).Append("    public const int MinimumCompatibleSchemaVersion = ").Append(schema.MinimumCompatibleVersion).AppendLine(";")
                .Append(indent).Append("    public const int AoSoABlockSize = ").Append(schema.BlockSize).AppendLine(";")
                .Append(indent).Append("    public const string SchemaHashSha256 = ").Append(Literal(schema.SchemaHash)).AppendLine(";")
                .AppendLine()
                .Append(indent).AppendLine("    public static bool IsDeclaredCompatibleVersion(int version)")
                .Append(indent).AppendLine("    {")
                .Append(indent).AppendLine("        return version >= MinimumCompatibleSchemaVersion && version <= SchemaVersion;")
                .Append(indent).AppendLine("    }")
                .Append(indent).AppendLine("}")
                .AppendLine();
        }

        private static void AppendParityFieldMap(
            StringBuilder source,
            RecordSchema schema,
            string indent)
        {
            source.Append(indent).Append("public static class ").Append(schema.ParityMapType).AppendLine()
                .Append(indent).AppendLine("{")
                .Append(indent).Append("    public const int FieldCount = ").Append(schema.Fields.Count).AppendLine(";")
                .AppendLine();
            AppendFieldMapMethod(source, schema, indent, "GetFieldName", "string", field => Literal(field.Name));
            AppendFieldMapMethod(source, schema, indent, "GetFieldTypeName", "string", field => Literal(field.DisplayType));
            AppendFieldMapMethod(source, schema, indent, "GetTemperature",
                "global::Yanagisawa.DataLayoutCalibrator.DataLayoutFieldTemperature",
                field => field.IsHot
                    ? "global::Yanagisawa.DataLayoutCalibrator.DataLayoutFieldTemperature.Hot"
                    : "global::Yanagisawa.DataLayoutCalibrator.DataLayoutFieldTemperature.Cold");
            source.Append(indent).AppendLine("    public static global::Yanagisawa.DataLayoutCalibrator.DataLayoutFieldSemantics GetSemantics(int fieldIndex)")
                .Append(indent).AppendLine("    {")
                .Append(indent).AppendLine("        if ((uint)fieldIndex >= FieldCount)")
                .Append(indent).AppendLine("            throw new global::System.ArgumentOutOfRangeException(nameof(fieldIndex));")
                .Append(indent).AppendLine("        return global::Yanagisawa.DataLayoutCalibrator.DataLayoutFieldSemantics.Value;")
                .Append(indent).AppendLine("    }")
                .Append(indent).AppendLine("}")
                .AppendLine();
        }

        private static void AppendFieldMapMethod(
            StringBuilder source,
            RecordSchema schema,
            string indent,
            string methodName,
            string returnType,
            Func<RecordField, string> value)
        {
            source.Append(indent).Append("    public static ").Append(returnType).Append(' ').Append(methodName).AppendLine("(int fieldIndex)")
                .Append(indent).AppendLine("    {")
                .Append(indent).AppendLine("        switch (fieldIndex)")
                .Append(indent).AppendLine("        {");
            for (int index = 0; index < schema.Fields.Count; index++)
            {
                source.Append(indent).Append("            case ").Append(index).Append(": return ")
                    .Append(value(schema.Fields[index])).AppendLine(";");
            }
            source.Append(indent).AppendLine("            default: throw new global::System.ArgumentOutOfRangeException(nameof(fieldIndex));")
                .Append(indent).AppendLine("        }")
                .Append(indent).AppendLine("    }")
                .AppendLine();
        }

        private static void AppendAoSStorage(
            StringBuilder source,
            RecordSchema schema,
            string indent)
        {
            string type = schema.AoSStorageType;
            source.Append(indent).Append("public struct ").Append(type).AppendLine(" : global::System.IDisposable")
                .Append(indent).AppendLine("{")
                .Append(indent).Append("    public global::Unity.Collections.NativeArray<").Append(schema.FullyQualifiedRecordType).AppendLine("> Records;")
                .Append(indent).AppendLine("    public int Count;")
                .Append(indent).AppendLine("    public bool IsCreated => Records.IsCreated;")
                .AppendLine()
                .Append(indent).Append("    public static ").Append(type).AppendLine(" Allocate(int count, global::Unity.Collections.Allocator allocator)")
                .Append(indent).AppendLine("    {")
                .Append(indent).AppendLine("        if (count < 0) throw new global::System.ArgumentOutOfRangeException(nameof(count));")
                .Append(indent).Append("        return new ").Append(type).AppendLine()
                .Append(indent).AppendLine("        {")
                .Append(indent).Append("            Records = new global::Unity.Collections.NativeArray<").Append(schema.FullyQualifiedRecordType)
                .AppendLine(">(count, allocator, global::Unity.Collections.NativeArrayOptions.UninitializedMemory),")
                .Append(indent).AppendLine("            Count = count,")
                .Append(indent).AppendLine("        };")
                .Append(indent).AppendLine("    }")
                .AppendLine();
            AppendCommonStorageMethods(source, schema, indent, type, "Records[index]", "Records[index] = record;");
            source.Append(indent).AppendLine("    public void Dispose()")
                .Append(indent).AppendLine("    {")
                .Append(indent).AppendLine("        if (Records.IsCreated) Records.Dispose();")
                .Append(indent).AppendLine("        Count = 0;")
                .Append(indent).AppendLine("    }")
                .Append(indent).AppendLine("}")
                .AppendLine();
        }

        private static void AppendSoAStorage(
            StringBuilder source,
            RecordSchema schema,
            string indent)
        {
            string type = schema.SoAStorageType;
            source.Append(indent).Append("public struct ").Append(type).AppendLine(" : global::System.IDisposable")
                .Append(indent).AppendLine("{");
            foreach (RecordField field in schema.Fields)
            {
                source.Append(indent).Append("    public global::Unity.Collections.NativeArray<").Append(field.TypeName)
                    .Append("> Field_").Append(field.Identifier).AppendLine(";");
            }
            source.Append(indent).AppendLine("    public int Count;")
                .Append(indent).Append("    public bool IsCreated => Field_").Append(schema.Fields[0].Identifier).AppendLine(".IsCreated;")
                .AppendLine()
                .Append(indent).Append("    public static ").Append(type).AppendLine(" Allocate(int count, global::Unity.Collections.Allocator allocator)")
                .Append(indent).AppendLine("    {")
                .Append(indent).AppendLine("        if (count < 0) throw new global::System.ArgumentOutOfRangeException(nameof(count));")
                .Append(indent).Append("        var storage = new ").Append(type).AppendLine("();")
                .Append(indent).AppendLine("        try")
                .Append(indent).AppendLine("        {");
            foreach (RecordField field in schema.Fields)
            {
                source.Append(indent).Append("            storage.Field_").Append(field.Identifier)
                    .Append(" = new global::Unity.Collections.NativeArray<").Append(field.TypeName)
                    .AppendLine(">(count, allocator, global::Unity.Collections.NativeArrayOptions.UninitializedMemory);");
            }
            source.Append(indent).AppendLine("            storage.Count = count;")
                .Append(indent).AppendLine("            return storage;")
                .Append(indent).AppendLine("        }")
                .Append(indent).AppendLine("        catch")
                .Append(indent).AppendLine("        {")
                .Append(indent).AppendLine("            storage.Dispose();")
                .Append(indent).AppendLine("            throw;")
                .Append(indent).AppendLine("        }")
                .Append(indent).AppendLine("    }")
                .AppendLine();
            string readExpression = "new " + schema.FullyQualifiedRecordType + "\n" + indent + "        {\n" +
                string.Join(",\n", schema.Fields.Select(field =>
                    indent + "            " + field.EscapedName + " = Field_" + field.Identifier + "[index]")) +
                ",\n" + indent + "        }";
            string writeStatements = string.Join("\n", schema.Fields.Select(field =>
                indent + "        Field_" + field.Identifier + "[index] = record." + field.EscapedName + ";"));
            AppendCommonStorageMethods(source, schema, indent, type, readExpression, writeStatements);
            source.Append(indent).AppendLine("    public void Dispose()")
                .Append(indent).AppendLine("    {");
            foreach (RecordField field in schema.Fields)
            {
                source.Append(indent).Append("        if (Field_").Append(field.Identifier).Append(".IsCreated) Field_")
                    .Append(field.Identifier).AppendLine(".Dispose();");
            }
            source.Append(indent).AppendLine("        Count = 0;")
                .Append(indent).AppendLine("    }")
                .Append(indent).AppendLine("}")
                .AppendLine();
        }

        private static void AppendAoSoAStorage(
            StringBuilder source,
            RecordSchema schema,
            string indent)
        {
            List<RecordField> hot = schema.Fields.Where(field => field.IsHot).ToList();
            List<RecordField> cold = schema.Fields.Where(field => !field.IsHot).ToList();
            source.Append(indent).Append("public struct ").Append(schema.AoSoABlockType).AppendLine()
                .Append(indent).AppendLine("{");
            foreach (RecordField field in hot)
            for (int lane = 0; lane < schema.BlockSize; lane++)
            {
                source.Append(indent).Append("    public ").Append(field.TypeName).Append(' ')
                    .Append(field.Identifier).Append('_').Append(lane).AppendLine(";");
            }
            source.Append(indent).AppendLine("}")
                .AppendLine();

            string type = schema.AoSoAStorageType;
            source.Append(indent).Append("public struct ").Append(type).AppendLine(" : global::System.IDisposable")
                .Append(indent).AppendLine("{")
                .Append(indent).Append("    public const int BlockWidth = ").Append(schema.BlockSize).AppendLine(";")
                .Append(indent).Append("    public global::Unity.Collections.NativeArray<").Append(schema.AoSoABlockType).AppendLine("> HotBlocks;");
            foreach (RecordField field in cold)
            {
                source.Append(indent).Append("    public global::Unity.Collections.NativeArray<").Append(field.TypeName)
                    .Append("> Cold_").Append(field.Identifier).AppendLine(";");
            }
            source.Append(indent).AppendLine("    public int Count;")
                .Append(indent).AppendLine("    public bool IsCreated => HotBlocks.IsCreated;")
                .Append(indent).AppendLine("    public int BlockCount => HotBlocks.IsCreated ? HotBlocks.Length : 0;")
                .AppendLine()
                .Append(indent).Append("    public static ").Append(type).AppendLine(" Allocate(int count, global::Unity.Collections.Allocator allocator)")
                .Append(indent).AppendLine("    {")
                .Append(indent).AppendLine("        if (count < 0) throw new global::System.ArgumentOutOfRangeException(nameof(count));")
                .Append(indent).Append("        var storage = new ").Append(type).AppendLine("();")
                .Append(indent).AppendLine("        try")
                .Append(indent).AppendLine("        {")
                .Append(indent).AppendLine("            int blockCount = (count + BlockWidth - 1) / BlockWidth;")
                .Append(indent).Append("            storage.HotBlocks = new global::Unity.Collections.NativeArray<").Append(schema.AoSoABlockType)
                .AppendLine(">(blockCount, allocator, global::Unity.Collections.NativeArrayOptions.ClearMemory);");
            foreach (RecordField field in cold)
            {
                source.Append(indent).Append("            storage.Cold_").Append(field.Identifier)
                    .Append(" = new global::Unity.Collections.NativeArray<").Append(field.TypeName)
                    .AppendLine(">(count, allocator, global::Unity.Collections.NativeArrayOptions.UninitializedMemory);");
            }
            source.Append(indent).AppendLine("            storage.Count = count;")
                .Append(indent).AppendLine("            return storage;")
                .Append(indent).AppendLine("        }")
                .Append(indent).AppendLine("        catch")
                .Append(indent).AppendLine("        {")
                .Append(indent).AppendLine("            storage.Dispose();")
                .Append(indent).AppendLine("            throw;")
                .Append(indent).AppendLine("        }")
                .Append(indent).AppendLine("    }")
                .AppendLine();
            AppendFromRecordsAndBoundaryLoops(source, schema, indent, type);
            source.Append(indent).Append("    public ").Append(schema.FullyQualifiedRecordType).AppendLine(" ReadRecord(int index)")
                .Append(indent).AppendLine("    {")
                .Append(indent).AppendLine("        int blockIndex = index / BlockWidth;")
                .Append(indent).AppendLine("        int lane = index % BlockWidth;")
                .Append(indent).Append("        ").Append(schema.AoSoABlockType).AppendLine(" block = HotBlocks[blockIndex];")
                .Append(indent).Append("        return new ").Append(schema.FullyQualifiedRecordType).AppendLine()
                .Append(indent).AppendLine("        {");
            foreach (RecordField field in schema.Fields)
            {
                source.Append(indent).Append("            ").Append(field.EscapedName).Append(" = ");
                if (field.IsHot)
                    source.Append("Read_").Append(field.Identifier).Append("(block, lane)");
                else
                    source.Append("Cold_").Append(field.Identifier).Append("[index]");
                source.AppendLine(",");
            }
            source.Append(indent).AppendLine("        };")
                .Append(indent).AppendLine("    }")
                .AppendLine()
                .Append(indent).Append("    public void WriteRecord(int index, ").Append(schema.FullyQualifiedRecordType).AppendLine(" record)")
                .Append(indent).AppendLine("    {")
                .Append(indent).AppendLine("        int blockIndex = index / BlockWidth;")
                .Append(indent).AppendLine("        int lane = index % BlockWidth;")
                .Append(indent).Append("        ").Append(schema.AoSoABlockType).AppendLine(" block = HotBlocks[blockIndex];");
            foreach (RecordField field in hot)
            {
                source.Append(indent).Append("        Write_").Append(field.Identifier)
                    .Append("(ref block, lane, record.").Append(field.EscapedName).AppendLine(");");
            }
            source.Append(indent).AppendLine("        HotBlocks[blockIndex] = block;");
            foreach (RecordField field in cold)
            {
                source.Append(indent).Append("        Cold_").Append(field.Identifier).Append("[index] = record.")
                    .Append(field.EscapedName).AppendLine(";");
            }
            source.Append(indent).AppendLine("    }")
                .AppendLine();
            foreach (RecordField field in hot)
                AppendAoSoAFieldAccessors(source, schema, field, indent);
            source.Append(indent).AppendLine("    public void Dispose()")
                .Append(indent).AppendLine("    {")
                .Append(indent).AppendLine("        if (HotBlocks.IsCreated) HotBlocks.Dispose();");
            foreach (RecordField field in cold)
            {
                source.Append(indent).Append("        if (Cold_").Append(field.Identifier).Append(".IsCreated) Cold_")
                    .Append(field.Identifier).AppendLine(".Dispose();");
            }
            source.Append(indent).AppendLine("        Count = 0;")
                .Append(indent).AppendLine("    }")
                .Append(indent).AppendLine("}")
                .AppendLine();
        }

        private static void AppendCommonStorageMethods(
            StringBuilder source,
            RecordSchema schema,
            string indent,
            string storageType,
            string readExpression,
            string writeStatements)
        {
            AppendFromRecordsAndBoundaryLoops(source, schema, indent, storageType);
            source.Append(indent).Append("    public ").Append(schema.FullyQualifiedRecordType).AppendLine(" ReadRecord(int index)")
                .Append(indent).AppendLine("    {")
                .Append(indent).Append("        return ").Append(readExpression).AppendLine(";")
                .Append(indent).AppendLine("    }")
                .AppendLine()
                .Append(indent).Append("    public void WriteRecord(int index, ").Append(schema.FullyQualifiedRecordType).AppendLine(" record)")
                .Append(indent).AppendLine("    {")
                .Append(writeStatements).AppendLine()
                .Append(indent).AppendLine("    }")
                .AppendLine();
        }

        private static void AppendFromRecordsAndBoundaryLoops(
            StringBuilder source,
            RecordSchema schema,
            string indent,
            string storageType)
        {
            source.Append(indent).Append("    public static ").Append(storageType).Append(" FromRecords(global::Unity.Collections.NativeArray<")
                .Append(schema.FullyQualifiedRecordType).AppendLine("> source, global::Unity.Collections.Allocator allocator)")
                .Append(indent).AppendLine("    {")
                .Append(indent).Append("        ").Append(storageType).AppendLine(" storage = Allocate(source.Length, allocator);")
                .Append(indent).AppendLine("        storage.Ingress(source);")
                .Append(indent).AppendLine("        return storage;")
                .Append(indent).AppendLine("    }")
                .AppendLine()
                .Append(indent).Append("    public void Ingress(global::Unity.Collections.NativeArray<").Append(schema.FullyQualifiedRecordType).AppendLine("> source)")
                .Append(indent).AppendLine("    {")
                .Append(indent).AppendLine("        ValidateLength(source.Length);")
                .Append(indent).AppendLine("        for (int index = 0; index < Count; index++) WriteRecord(index, source[index]);")
                .Append(indent).AppendLine("    }")
                .AppendLine()
                .Append(indent).Append("    public void Export(global::Unity.Collections.NativeArray<").Append(schema.FullyQualifiedRecordType).AppendLine("> destination)")
                .Append(indent).AppendLine("    {")
                .Append(indent).AppendLine("        ValidateLength(destination.Length);")
                .Append(indent).AppendLine("        for (int index = 0; index < Count; index++) destination[index] = ReadRecord(index);")
                .Append(indent).AppendLine("    }")
                .AppendLine()
                .Append(indent).AppendLine("    private void ValidateLength(int length)")
                .Append(indent).AppendLine("    {")
                .Append(indent).AppendLine("        if (!IsCreated) throw new global::System.InvalidOperationException(\"Storage is not created.\");")
                .Append(indent).AppendLine("        if (length != Count) throw new global::System.ArgumentException(\"Boundary buffer length must equal storage Count.\");")
                .Append(indent).AppendLine("    }")
                .AppendLine();
        }

        private static void AppendAoSoAFieldAccessors(
            StringBuilder source,
            RecordSchema schema,
            RecordField field,
            string indent)
        {
            source.Append(indent).Append("    private static ").Append(field.TypeName).Append(" Read_")
                .Append(field.Identifier).Append('(').Append(schema.AoSoABlockType).AppendLine(" block, int lane)")
                .Append(indent).AppendLine("    {")
                .Append(indent).AppendLine("        switch (lane)")
                .Append(indent).AppendLine("        {");
            for (int lane = 0; lane < schema.BlockSize; lane++)
            {
                source.Append(indent).Append("            case ").Append(lane).Append(": return block.")
                    .Append(field.Identifier).Append('_').Append(lane).AppendLine(";");
            }
            source.Append(indent).AppendLine("            default: throw new global::System.ArgumentOutOfRangeException(nameof(lane));")
                .Append(indent).AppendLine("        }")
                .Append(indent).AppendLine("    }")
                .AppendLine()
                .Append(indent).Append("    private static void Write_").Append(field.Identifier).Append("(ref ")
                .Append(schema.AoSoABlockType).Append(" block, int lane, ").Append(field.TypeName).AppendLine(" value)")
                .Append(indent).AppendLine("    {")
                .Append(indent).AppendLine("        switch (lane)")
                .Append(indent).AppendLine("        {");
            for (int lane = 0; lane < schema.BlockSize; lane++)
            {
                source.Append(indent).Append("            case ").Append(lane).Append(": block.")
                    .Append(field.Identifier).Append('_').Append(lane).AppendLine(" = value; return;");
            }
            source.Append(indent).AppendLine("            default: throw new global::System.ArgumentOutOfRangeException(nameof(lane));")
                .Append(indent).AppendLine("        }")
                .Append(indent).AppendLine("    }")
                .AppendLine();
        }

        private static void AppendCodec(
            StringBuilder source,
            RecordSchema schema,
            string indent)
        {
            source.Append(indent).Append("public static class ").Append(schema.CodecType).AppendLine()
                .Append(indent).AppendLine("{");
            string[] storageTypes =
            {
                schema.AoSStorageType,
                schema.SoAStorageType,
                schema.AoSoAStorageType,
            };
            foreach (string storageType in storageTypes)
            {
                source.Append(indent).Append("    public static void Ingress(global::Unity.Collections.NativeArray<")
                    .Append(schema.FullyQualifiedRecordType).Append("> source, ref ").Append(storageType).AppendLine(" destination)")
                    .Append(indent).AppendLine("    {")
                    .Append(indent).AppendLine("        destination.Ingress(source);")
                    .Append(indent).AppendLine("    }")
                    .AppendLine()
                    .Append(indent).Append("    public static void Export(ref ").Append(storageType)
                    .Append(" source, global::Unity.Collections.NativeArray<").Append(schema.FullyQualifiedRecordType).AppendLine("> destination)")
                    .Append(indent).AppendLine("    {")
                    .Append(indent).AppendLine("        source.Export(destination);")
                    .Append(indent).AppendLine("    }")
                    .AppendLine();
            }
            source.Append(indent).AppendLine("}")
                .AppendLine();
        }

        private static bool HasUnsupportedStructLayout(
            INamedTypeSymbol type,
            out string reason)
        {
            AttributeData? layout = type.GetAttributes().FirstOrDefault(item =>
                item.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.StructLayoutAttribute");
            if (layout == null)
            {
                reason = string.Empty;
                return false;
            }

            int kind = layout.ConstructorArguments.Length == 1 &&
                       layout.ConstructorArguments[0].Value is int value
                ? value
                : 0;
            int pack = NamedInt(layout, "Pack", 0);
            int size = NamedInt(layout, "Size", 0);
            if (kind == 0 && pack == 0 && size == 0)
            {
                reason = string.Empty;
                return false;
            }

            reason = "only default sequential layout is supported; explicit offsets, custom Pack, and custom Size can alias or change alignment";
            return true;
        }

        private static bool IsSupportedFieldType(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                    return true;
            }

            return SupportedMathTypes.Contains(type.ToDisplayString());
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol root)
        {
            foreach (INamedTypeSymbol type in root.GetTypeMembers())
            {
                yield return type;
                foreach (INamedTypeSymbol nested in EnumerateNestedTypes(type))
                    yield return nested;
            }

            foreach (INamespaceSymbol child in root.GetNamespaceMembers())
            foreach (INamedTypeSymbol type in EnumerateTypes(child))
                yield return type;
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol root)
        {
            foreach (INamedTypeSymbol type in root.GetTypeMembers())
            {
                yield return type;
                foreach (INamedTypeSymbol nested in EnumerateNestedTypes(type))
                    yield return nested;
            }
        }

        private static AttributeData? FindAttribute(
            IEnumerable<AttributeData> attributes,
            INamedTypeSymbol attributeType)
        {
            return attributes.FirstOrDefault(attribute =>
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType));
        }

        private static int NamedInt(AttributeData attribute, string name, int fallback)
        {
            foreach (KeyValuePair<string, TypedConstant> pair in attribute.NamedArguments)
            {
                if (pair.Key == name && pair.Value.Value is int value)
                    return value;
            }

            return fallback;
        }

        private static Location AttributeLocation(AttributeData attribute, ISymbol symbol)
        {
            return attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ??
                   symbol.Locations.FirstOrDefault() ??
                   Location.None;
        }

        private static void Report(
            GeneratorExecutionContext context,
            DiagnosticDescriptor descriptor,
            Location location,
            params object[] arguments)
        {
            context.ReportDiagnostic(Diagnostic.Create(descriptor, location, arguments));
        }

        private static bool IsSchemaId(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool alpha = character >= 'a' && character <= 'z';
                bool digit = character >= '0' && character <= '9';
                bool separator = index > 0 && (character == '.' || character == '_' || character == '-');
                if (!alpha && !digit && !separator)
                    return false;
            }

            return true;
        }

        private static string BuildCanonicalSchema(
            string schemaId,
            int schemaVersion,
            int minimumCompatibleVersion,
            int blockSize,
            IReadOnlyList<RecordField> fields)
        {
            var canonical = new StringBuilder();
            canonical.Append("definition=1\n")
                .Append("schema=").Append(schemaId).Append('\n')
                .Append("version=").Append(schemaVersion).Append('\n')
                .Append("minimum=").Append(minimumCompatibleVersion).Append('\n')
                .Append("block=").Append(blockSize).Append('\n');
            foreach (RecordField field in fields)
            {
                canonical.Append(field.Order).Append('|')
                    .Append(field.Name).Append('|')
                    .Append(field.DisplayType).Append('|')
                    .Append(field.IsHot ? "hot" : "cold").Append("|value\n");
            }
            return canonical.ToString();
        }

        private static string Sha256(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            byte[] digest;
            using (SHA256 algorithm = SHA256.Create())
                digest = algorithm.ComputeHash(bytes);
            var builder = new StringBuilder(64);
            foreach (byte item in digest)
                builder.Append(item.ToString("X2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static string Literal(string value)
        {
            return SymbolDisplay.FormatLiteral(value, quote: true);
        }

        private static string EscapeIdentifier(string value)
        {
            return SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None ||
                   SyntaxFacts.GetContextualKeywordKind(value) != SyntaxKind.None
                ? "@" + value
                : value;
        }

        private sealed class RecordSchema
        {
            internal RecordSchema(
                string namespaceName,
                string recordName,
                string fullyQualifiedRecordType,
                string schemaId,
                int schemaVersion,
                int minimumCompatibleVersion,
                int blockSize,
                string schemaHash,
                IReadOnlyList<RecordField> fields,
                Location location)
            {
                NamespaceName = namespaceName;
                RecordName = recordName;
                FullyQualifiedRecordType = fullyQualifiedRecordType;
                SchemaId = schemaId;
                SchemaVersion = schemaVersion;
                MinimumCompatibleVersion = minimumCompatibleVersion;
                BlockSize = blockSize;
                SchemaHash = schemaHash;
                Fields = fields;
                Location = location;
                string hintIdentity = namespaceName.Length == 0
                    ? recordName
                    : namespaceName + "." + recordName;
                HintName = "DataLayoutScaffold." + hintIdentity.Replace('.', '_') + "." +
                           schemaHash.Substring(0, 12) + ".g.cs";
            }

            internal string NamespaceName { get; }
            internal string RecordName { get; }
            internal string FullyQualifiedRecordType { get; }
            internal string SchemaId { get; }
            internal int SchemaVersion { get; }
            internal int MinimumCompatibleVersion { get; }
            internal int BlockSize { get; }
            internal string SchemaHash { get; }
            internal IReadOnlyList<RecordField> Fields { get; }
            internal Location Location { get; }
            internal string HintName { get; }
            internal string SchemaType => RecordName + "GeneratedDataLayoutSchema";
            internal string ParityMapType => RecordName + "GeneratedParityFieldMap";
            internal string AoSStorageType => RecordName + "GeneratedAoSStorage";
            internal string SoAStorageType => RecordName + "GeneratedSoAStorage";
            internal string AoSoABlockType => RecordName + "GeneratedAoSoA" + BlockSize + "Block";
            internal string AoSoAStorageType => RecordName + "GeneratedAoSoA" + BlockSize + "Storage";
            internal string CodecType => RecordName + "GeneratedDataLayoutCodec";
        }

        private sealed class RecordField
        {
            internal RecordField(
                string name,
                string escapedName,
                string typeName,
                string displayType,
                int order,
                bool isHot)
            {
                Name = name;
                EscapedName = escapedName;
                Identifier = escapedName.StartsWith("@", StringComparison.Ordinal)
                    ? "Keyword_" + name
                    : name;
                TypeName = typeName;
                DisplayType = displayType;
                Order = order;
                IsHot = isHot;
            }

            internal string Name { get; }
            internal string EscapedName { get; }
            internal string Identifier { get; }
            internal string TypeName { get; }
            internal string DisplayType { get; }
            internal int Order { get; }
            internal bool IsHot { get; }
        }
    }
}
