using System;
using System.IO;
using System.Text;

namespace Yanagisawa.DataLayoutCalibrator
{
    public sealed class ProfileStoreWriteResult
    {
        public bool Succeeded;
        public string Path;
        public string Diagnostic;
    }

    public interface IFrozenDeploymentProfileStore
    {
        ProfileDocumentLoadResult Load(string profileKey);

        ProfileStoreWriteResult Save(
            string profileKey,
            FrozenDeploymentProfile profile);
    }

    /// <summary>
    /// File-backed cache with hashed keys and same-directory atomic replacement.
    /// Stored bytes are the strict FrozenDeploymentProfileCodec format.
    /// </summary>
    public sealed class FileFrozenDeploymentProfileStore : IFrozenDeploymentProfileStore
    {
        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false, true);
        private readonly string _rootDirectory;

        public FileFrozenDeploymentProfileStore(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("A cache root directory is required.", nameof(rootDirectory));
            _rootDirectory = Path.GetFullPath(rootDirectory);
        }

        public ProfileDocumentLoadResult Load(string profileKey)
        {
            string path = GetProfilePath(profileKey);
            if (!File.Exists(path))
            {
                return new ProfileDocumentLoadResult
                {
                    Status = ProfileDocumentLoadStatus.Missing,
                    Diagnostic = "No frozen deployment profile exists for the requested key.",
                };
            }

            try
            {
                return FrozenDeploymentProfileCodec.Decode(
                    File.ReadAllText(path, Utf8WithoutBom));
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is NotSupportedException)
            {
                return new ProfileDocumentLoadResult
                {
                    Status = ProfileDocumentLoadStatus.StorageError,
                    Diagnostic = "The frozen profile could not be read: " + exception.Message,
                };
            }
        }

        public ProfileStoreWriteResult Save(
            string profileKey,
            FrozenDeploymentProfile profile)
        {
            string path = GetProfilePath(profileKey);
            string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                string document = FrozenDeploymentProfileCodec.Encode(profile);
                Directory.CreateDirectory(_rootDirectory);
                File.WriteAllText(temporaryPath, document, Utf8WithoutBom);
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, null);
                else
                    File.Move(temporaryPath, path);

                return new ProfileStoreWriteResult
                {
                    Succeeded = true,
                    Path = path,
                    Diagnostic = "Saved the raw suite, frozen decision, and provenance atomically.",
                };
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is NotSupportedException ||
                exception is ArgumentException)
            {
                return new ProfileStoreWriteResult
                {
                    Succeeded = false,
                    Path = path,
                    Diagnostic = "The frozen profile could not be saved: " + exception.Message,
                };
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // The destination was never replaced if the temporary file
                    // is still present. Cleanup can be retried by the host.
                }
                catch (UnauthorizedAccessException)
                {
                    // Same failure isolation as IOException above.
                }
            }
        }

        public string GetProfilePath(string profileKey)
        {
            string key = ProfileCanonicalization.Required(profileKey, nameof(profileKey));
            return Path.Combine(
                _rootDirectory,
                ProfileCanonicalization.Sha256(key) + ".dlcprofile");
        }
    }
}
