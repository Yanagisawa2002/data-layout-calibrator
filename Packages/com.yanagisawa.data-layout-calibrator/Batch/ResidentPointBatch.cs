using System;

namespace Yanagisawa.DataLayoutCalibrator.Batch
{
    /// <summary>Single-owner, synchronous point storage with an explicit reusable capacity.
    /// Ingress and transforms stage into a second buffer before committing. No backing
    /// array escapes. Not thread-safe; callers must serialize access, including Dispose.</summary>
    public sealed class ResidentPointBatch : IDisposable
    {
        private BatchPoint3[] front, scratch;
        private double[] x, y, z, sx, sy, sz;
        private int count, capacity;
        private long version;
        private bool disposed;

        public PointStorageLayout Layout { get; }
        public bool IsDisposed => disposed;
        public int Count { get { CheckAlive(); return count; } }
        public int Capacity { get { CheckAlive(); return capacity; } }
        public long Version { get { CheckAlive(); return version; } }
        /// <summary>Two coordinate-buffer payloads only; excludes array/object headers,
        /// caller input/output, and transient old/new buffers during Reserve.</summary>
        public long OwnedCoordinatePayloadBytes { get { CheckAlive(); return 48L * capacity; } }

        public ResidentPointBatch(int capacity, PointStorageLayout layout = PointStorageLayout.AoS)
        {
            ValidateLayout(layout);
            Layout = layout;
            Reserve(capacity);
        }

        internal static void ValidateLayout(PointStorageLayout layout)
        {
            if (layout != PointStorageLayout.AoS && layout != PointStorageLayout.SoA)
                throw new ArgumentOutOfRangeException(nameof(layout));
        }

        /// <summary>Grow to the requested capacity, never shrink. This is the only
        /// post-construction buffer-allocation operation. Existing logical data survives.</summary>
        public void Reserve(int minimumCapacity)
        {
            CheckAlive();
            if (minimumCapacity < 0) throw new ArgumentOutOfRangeException(nameof(minimumCapacity));
            if (minimumCapacity <= capacity) return;
            // Publish only after every allocation and copy succeeds.
            if (Layout == PointStorageLayout.AoS)
            {
                var next = new BatchPoint3[minimumCapacity];
                var nextScratch = new BatchPoint3[minimumCapacity];
                if (count > 0) Array.Copy(front, next, count);
                front = next; scratch = nextScratch;
            }
            else
            {
                var nx = new double[minimumCapacity]; var ny = new double[minimumCapacity]; var nz = new double[minimumCapacity];
                var nsx = new double[minimumCapacity]; var nsy = new double[minimumCapacity]; var nsz = new double[minimumCapacity];
                if (count > 0) { Array.Copy(x, nx, count); Array.Copy(y, ny, count); Array.Copy(z, nz, count); }
                x = nx; y = ny; z = nz; sx = nsx; sy = nsy; sz = nsz;
            }
            capacity = minimumCapacity;
        }

        /// <summary>Copy finite input; never retains the caller's array. Capacity must
        /// already suffice. Failure leaves the previously committed data unchanged.</summary>
        public void Load(BatchPoint3[] source, int sourceOffset, int length)
        {
            CheckAlive();
            CheckRange(source, sourceOffset, length, nameof(source));
            if (length > capacity) throw new ArgumentException("Call Reserve explicitly before loading a larger batch.", nameof(length));
            long nextVersion = checked(version + 1);
            if (Layout == PointStorageLayout.AoS)
            {
                for (int i = 0; i < length; i++)
                {
                    var point = source[sourceOffset + i];
                    if (!point.IsFinite) throw new ArgumentException("Input points must be finite.", nameof(source));
                    scratch[i] = point;
                }
            }
            else
            {
                for (int i = 0; i < length; i++)
                {
                    var point = source[sourceOffset + i];
                    if (!point.IsFinite) throw new ArgumentException("Input points must be finite.", nameof(source));
                    sx[i] = point.X; sy[i] = point.Y; sz[i] = point.Z;
                }
            }
            SwapBuffers(); count = length; version = nextVersion;
        }

        /// <summary>Transform the current resident coordinates, not the original input.
        /// Nonfinite results (including overflow) reject the entire step before commit.</summary>
        public void Transform(AffineTransform3 transform)
        {
            CheckAlive();
            long nextVersion = checked(version + 1);
            // Layout dispatch is outside each element loop. Both arms use the same math.
            if (Layout == PointStorageLayout.AoS)
            {
                for (int i = 0; i < count; i++)
                {
                    var point = transform.Apply(front[i]);
                    if (!point.IsFinite) throw new ArithmeticException("Transform produced a nonfinite point; resident data was not committed.");
                    scratch[i] = point;
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    var point = transform.Apply(new BatchPoint3(x[i], y[i], z[i]));
                    if (!point.IsFinite) throw new ArithmeticException("Transform produced a nonfinite point; resident data was not committed.");
                    sx[i] = point.X; sy[i] = point.Y; sz[i] = point.Z;
                }
            }
            SwapBuffers(); version = nextVersion;
        }

        /// <summary>Reduce directly over resident coordinates. Does not export a full array.</summary>
        public BatchBounds3 ReduceBounds()
        {
            CheckAlive();
            BatchBounds3 bounds = default;
            if (Layout == PointStorageLayout.AoS)
            {
                for (int i = 0; i < count; i++) bounds = bounds.Include(front[i]);
            }
            else
            {
                for (int i = 0; i < count; i++) bounds = bounds.Include(new BatchPoint3(x[i], y[i], z[i]));
            }
            return bounds;
        }

        /// <summary>Full canonical export into caller-owned storage. Only Count entries
        /// starting at destinationOffset are written; no capacity tail is exposed.</summary>
        public int CopyTo(BatchPoint3[] destination, int destinationOffset = 0)
        {
            CheckAlive();
            CheckRange(destination, destinationOffset, count, nameof(destination));
            if (Layout == PointStorageLayout.AoS)
            {
                if (count > 0) Array.Copy(front, 0, destination, destinationOffset, count);
            }
            else
            {
                for (int i = 0; i < count; i++) destination[destinationOffset + i] = new BatchPoint3(x[i], y[i], z[i]);
            }
            return count;
        }

        private static void CheckRange(BatchPoint3[] array, int offset, int length, string name)
        {
            if (array == null) throw new ArgumentNullException(name);
            if (offset < 0 || length < 0 || offset > array.Length - length)
                throw new ArgumentOutOfRangeException(name, "The requested range is outside the array.");
        }

        private void SwapBuffers()
        {
            if (Layout == PointStorageLayout.AoS) { var old = front; front = scratch; scratch = old; }
            else
            {
                var old = x; x = sx; sx = old;
                old = y; y = sy; sy = old;
                old = z; z = sz; sz = old;
            }
        }

        private void CheckAlive()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ResidentPointBatch));
        }

        /// <summary>Release managed references; idempotent. Not a synchronous GC,
        /// native-resource release, or secure memory wipe.</summary>
        public void Dispose()
        {
            if (disposed) return;
            front = scratch = null; x = y = z = sx = sy = sz = null;
            count = capacity = 0; disposed = true;
        }
    }
}
