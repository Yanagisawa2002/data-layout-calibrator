using System;

namespace Yanagisawa.DataLayoutCalibrator.Batch
{
    /// <summary>Three double-precision coordinates. Ingress rejects nonfinite values.</summary>
    public readonly struct BatchPoint3
    {
        public readonly double X, Y, Z;
        public BatchPoint3(double x, double y, double z) { X = x; Y = y; Z = z; }
        internal bool IsFinite => Finite(X) && Finite(Y) && Finite(Z);
        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }

    /// <summary>Affine point transform: dot each row with the point, then add translation.
    /// No perspective divide. The default value maps every finite point to the origin.</summary>
    public readonly struct AffineTransform3
    {
        public readonly BatchPoint3 RowX, RowY, RowZ, Translation;

        public AffineTransform3(BatchPoint3 rowX, BatchPoint3 rowY, BatchPoint3 rowZ, BatchPoint3 translation)
        {
            if (!rowX.IsFinite || !rowY.IsFinite || !rowZ.IsFinite || !translation.IsFinite)
                throw new ArgumentException("Affine coefficients must be finite.");
            RowX = rowX; RowY = rowY; RowZ = rowZ; Translation = translation;
        }

        public static AffineTransform3 Identity => Translate(0, 0, 0);
        public static AffineTransform3 Translate(double x, double y, double z) => new AffineTransform3(
            new BatchPoint3(1, 0, 0), new BatchPoint3(0, 1, 0), new BatchPoint3(0, 0, 1), new BatchPoint3(x, y, z));
        public static AffineTransform3 Scale(double x, double y, double z) => new AffineTransform3(
            new BatchPoint3(x, 0, 0), new BatchPoint3(0, y, 0), new BatchPoint3(0, 0, z), default);

        public BatchPoint3 Apply(BatchPoint3 point) => new BatchPoint3(
            ((RowX.X * point.X + RowX.Y * point.Y) + RowX.Z * point.Z) + Translation.X,
            ((RowY.X * point.X + RowY.Y * point.Y) + RowY.Z * point.Z) + Translation.Y,
            ((RowZ.X * point.X + RowZ.Y * point.Y) + RowZ.Z * point.Z) + Translation.Z);
    }

    /// <summary>Bounds of point coordinates, not transformed mesh extents.
    /// Empty bounds have HasValue=false; their zero Min/Max are not observations.</summary>
    public readonly struct BatchBounds3
    {
        public readonly bool HasValue;
        public readonly BatchPoint3 Min, Max;
        internal BatchBounds3(BatchPoint3 min, BatchPoint3 max) { HasValue = true; Min = min; Max = max; }
        internal BatchBounds3 Include(BatchPoint3 point) => !HasValue ? new BatchBounds3(point, point) : new BatchBounds3(
            new BatchPoint3(Math.Min(Min.X, point.X), Math.Min(Min.Y, point.Y), Math.Min(Min.Z, point.Z)),
            new BatchPoint3(Math.Max(Max.X, point.X), Math.Max(Max.Y, point.Y), Math.Max(Max.Z, point.Z)));
    }

    public enum PointStorageLayout { AoS, SoA }
}
