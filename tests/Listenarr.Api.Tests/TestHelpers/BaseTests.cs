using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Listenarr.Api.Tests
{
    public class BaseTests : IDisposable
    {
        private string? _tempFolder = null;
        private ServiceCollection _services;

        /// <summary>
        /// SETUP: Runs before EVERY test
        /// </summary>
        public BaseTests()
        {
            _tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempFolder);

            _services = MockUtils.InitServiceCollection();
        }

        public string GetTempPath()
        {
            if (_tempFolder == null)
            {
                _tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(_tempFolder);
            }
            
            return _tempFolder;
        }

        public string GetTempDirectory(string directory)
        {
            var path = Path.Join(GetTempPath(), directory);
            Directory.CreateDirectory(path);

            return path;
        }

        public async Task<string> GetFileAsync(string directory, string filename)
        {
            if (!directory.StartsWith(GetTempPath()))
            {
                directory = Path.Join(GetTempPath(), directory);
            }

            var path = Path.Join(directory, filename);
            await File.WriteAllTextAsync(path, "test");

            return path;
        }

        /// <summary>
        /// CLEANUP: Runs after EVERY test
        /// </summary>
        public void Dispose()
        {
            if (_tempFolder != null && Directory.Exists(_tempFolder))
            {
                Directory.Delete(_tempFolder, true);
            }
        }
    }
}
