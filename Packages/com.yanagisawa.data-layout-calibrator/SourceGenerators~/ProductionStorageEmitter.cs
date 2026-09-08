using System.Linq;
using System.Text;

namespace Yanagisawa.DataLayoutCalibrator.SourceGenerator
{
    public sealed partial class DataLayoutScaffoldGenerator
    {
        private static string[] Components(RecordField field) =>
            field.DisplayType == "Unity.Mathematics.float3" ? new[] { "x", "y", "z" } : new[] { "" };

        private static string PackedLane(RecordField field, string component, int lane) =>
            "block." + field.Identifier + component.ToUpperInvariant() + (lane / 4) + "." + "xyzw"[lane % 4];

        private static void AppendPackedAccessors(StringBuilder source, RecordSchema schema, RecordField field, string indent)
        {
            source.AppendLine($"{indent}    public static {field.TypeName} Read_{field.Identifier}({schema.AoSoABlockType} block, int lane)")
                .AppendLine($"{indent}    {{").AppendLine($"{indent}        switch (lane)").AppendLine($"{indent}        {{");
            for (int lane = 0; lane < schema.BlockSize; lane++)
            {
                string expression = Components(field).Length == 1 ? PackedLane(field, "", lane) :
                    "new " + field.TypeName + " { " + string.Join(", ", Components(field).Select(c => c + " = " + PackedLane(field, c, lane))) + " }";
                source.AppendLine($"{indent}            case {lane}: return {expression};");
            }
            source.AppendLine($"{indent}            default: throw new global::System.ArgumentOutOfRangeException(nameof(lane));")
                .AppendLine($"{indent}        }}").AppendLine($"{indent}    }}");
            source.AppendLine($"{indent}    public static void Write_{field.Identifier}(ref {schema.AoSoABlockType} block, int lane, {field.TypeName} value)")
                .AppendLine($"{indent}    {{").AppendLine($"{indent}        switch (lane)").AppendLine($"{indent}        {{");
            for (int lane = 0; lane < schema.BlockSize; lane++)
            {
                source.Append($"{indent}            case {lane}: ");
                foreach (string component in Components(field))
                    source.Append(PackedLane(field, component, lane)).Append(" = value").Append(component.Length == 0 ? "" : "." + component).Append("; ");
                source.AppendLine("return;");
            }
            source.AppendLine($"{indent}            default: throw new global::System.ArgumentOutOfRangeException(nameof(lane));")
                .AppendLine($"{indent}        }}").AppendLine($"{indent}    }}");
        }

        private static void AppendBlockIngress(StringBuilder source, RecordSchema schema, string indent)
        {
            source.AppendLine($"{indent}    public void IngressBlock(int blockIndex, global::Unity.Collections.NativeArray<{schema.FullyQualifiedRecordType}> source)")
                .AppendLine($"{indent}    {{")
                .AppendLine($"{indent}        {schema.AoSoABlockType} block = default;")
                .AppendLine($"{indent}        int first = blockIndex * BlockWidth;")
                .AppendLine($"{indent}        int lanes = global::System.Math.Min(BlockWidth, Count - first);")
                .AppendLine($"{indent}        for (int lane = 0; lane < lanes; lane++)")
                .AppendLine($"{indent}        {{")
                .AppendLine($"{indent}            int index = first + lane;")
                .AppendLine($"{indent}            var record = source[index];");
            foreach (RecordField field in schema.Fields)
                source.AppendLine(field.IsHot ?
                    $"{indent}            Write_{field.Identifier}(ref block, lane, record.{field.EscapedName});" :
                    $"{indent}            Cold_{field.Identifier}[index] = record.{field.EscapedName};");
            source.AppendLine($"{indent}        }}")
                .AppendLine($"{indent}        HotBlocks[blockIndex] = block;")
                .AppendLine($"{indent}    }}");
        }

        private static void AppendPaddedStorage(StringBuilder source, RecordSchema schema, string indent)
        {
            string element = schema.RecordName + "GeneratedPadded64Record";
            string type = schema.RecordName + "GeneratedPadded64Storage";
            source.AppendLine($"{indent}[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential, Size = 64)]")
                .AppendLine($"{indent}public struct {element} {{ public {schema.FullyQualifiedRecordType} Value; }}")
                .AppendLine($"{indent}public struct {type} : global::System.IDisposable")
                .AppendLine($"{indent}{{")
                .AppendLine($"{indent}    public const int RecordStrideBytes = 64;")
                .AppendLine($"{indent}    public global::Unity.Collections.NativeArray<{element}> Records;")
                .AppendLine($"{indent}    public int Count;")
                .AppendLine($"{indent}    public bool IsCreated => Records.IsCreated;")
                .AppendLine($"{indent}    public static {type} Allocate(int count, global::Unity.Collections.Allocator allocator)")
                .AppendLine($"{indent}    {{")
                .AppendLine($"{indent}        if (count < 0) throw new global::System.ArgumentOutOfRangeException(nameof(count));")
                .AppendLine($"{indent}        if (global::Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<{element}>() != RecordStrideBytes)")
                .AppendLine($"{indent}            throw new global::System.InvalidOperationException(\"Record exceeds the declared padded stride.\");")
                .AppendLine($"{indent}        return new {type} {{ Count = count, Records = new global::Unity.Collections.NativeArray<{element}>(count, allocator, global::Unity.Collections.NativeArrayOptions.ClearMemory) }};")
                .AppendLine($"{indent}    }}");
            AppendCommonStorageMethods(source, schema, indent, type, "Records[index].Value", $"        Records[index] = new {element} {{ Value = record }};");
            source.AppendLine($"{indent}    public void Dispose() {{ if (Records.IsCreated) Records.Dispose(); Records = default; Count = 0; }}")
                .AppendLine($"{indent}}}");
        }
    }
}
