using System;

namespace Yanagisawa.DataLayoutCalibrator
{
    /// <summary>
    /// Declares a flat, versioned record schema for the bounded storage scaffold
    /// generator. The generator emits storage and boundary codecs only; workload
    /// kernels and scheduling remain developer-authored.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class GenerateDataLayoutAttribute : Attribute
    {
        public GenerateDataLayoutAttribute(
            string schemaId,
            int schemaVersion,
            int aoSoABlockSize = 8)
        {
            SchemaId = schemaId;
            SchemaVersion = schemaVersion;
            AoSoABlockSize = aoSoABlockSize;
            MinimumCompatibleSchemaVersion = schemaVersion;
        }

        public string SchemaId { get; }

        public int SchemaVersion { get; }

        public int AoSoABlockSize { get; }

        /// <summary>Emit additional component-packed float4 blocks at widths 4, 8 and 16.
        /// Hot fields must be float or float3. Does not imply cache-line aligned allocation.</summary>
        public bool GeneratePackedFloat4 { get; set; }

        /// <summary>Optional physical AoS stride (0 or 64). Allocation rejects records larger than the stride.
        /// Padding does not increase the guaranteed base alignment.</summary>
        public int PaddedRecordSize { get; set; }

        /// <summary>
        /// Lowest record schema version that the author explicitly declares to
        /// have the same generated field map. This is metadata, not an automatic
        /// semantic migration.
        /// </summary>
        public int MinimumCompatibleSchemaVersion { get; set; }

        /// <summary>
        /// Version of the generator attribute contract. Version 1 is the only
        /// currently supported definition format.
        /// </summary>
        public int DefinitionVersion { get; set; } = 1;
    }

    public enum DataLayoutFieldTemperature
    {
        Hot = 0,
        Cold = 1,
    }

    public enum DataLayoutFieldSemantics
    {
        /// <summary>
        /// The field is copied exactly at ingress and export. Aliased, derived,
        /// or ownership-bearing semantics are intentionally outside v1.
        /// </summary>
        Value = 0,
    }

    /// <summary>
    /// Places a field at an explicit position in the generated schema and marks
    /// whether it belongs in AoSoA hot blocks or a cold side array.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class DataLayoutFieldAttribute : Attribute
    {
        public DataLayoutFieldAttribute(
            int order,
            DataLayoutFieldTemperature temperature)
        {
            Order = order;
            Temperature = temperature;
        }

        public int Order { get; }

        public DataLayoutFieldTemperature Temperature { get; }

        public DataLayoutFieldSemantics Semantics { get; set; } =
            DataLayoutFieldSemantics.Value;
    }
}
