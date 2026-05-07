using System;
using System.IO;

namespace Core.Config.Providers
{
    /// <summary>
    /// Provider que carrega recursos do sistema de arquivos físico.
    /// </summary>
    public class PhysicalFileResourceProvider : IResourceProvider
    {
        private readonly string _basePath;

        public PhysicalFileResourceProvider(string basePath, string name, int priority)
        {
            _basePath = Path.GetFullPath(basePath);
            Name = name;
            Priority = priority;
        }

        public string Name { get; }
        public int Priority { get; }

        public bool Exists(string relativePath)
        {
            var fullPath = Path.Combine(_basePath, relativePath);
            return File.Exists(fullPath);
        }

        public Stream? OpenRead(string relativePath)
        {
            var fullPath = Path.Combine(_basePath, relativePath);
            return File.Exists(fullPath) ? File.OpenRead(fullPath) : null;
        }

        public string? GetPhysicalPath(string relativePath)
        {
            return Path.Combine(_basePath, relativePath);
        }
    }
}
