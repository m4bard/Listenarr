using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Listenarr.Infrastructure.Models;

namespace Listenarr.Infrastructure
{
    /// <summary>
    /// Design-time factory used by EF tools to create ListenArrDbContext without relying on the application's DI.
    /// Uses a local SQLite file under the repo (listenarr.api/config/database/listenarr.db) by default.
    /// </summary>
    public class ListenArrDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ListenArrDbContext>
    {
        public ListenArrDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ListenArrDbContext>();

            // Prefer explicit environment variable if set (helps CI or custom paths)
            var dbPath = Environment.GetEnvironmentVariable("LISTENARR_SQLITE_PATH");
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                // Try to resolve repo root deterministically by walking up from the
                // assembly base directory (AppContext.BaseDirectory). This is more
                // reliable than Directory.GetCurrentDirectory() which depends on
                // how the process was launched.
                string? repoRoot = FindRepoRoot();
                if (repoRoot != null)
                {
                    var candidate = Path.Combine(repoRoot, "listenarr.api", "config", "database", "listenarr.db");
                    dbPath = Path.GetFullPath(candidate);
                }
                else
                {
                    // Last-resort: fall back to current directory behavior to remain compatible
                    var cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), "..", "listenarr.api", "config", "database", "listenarr.db");
                    dbPath = Path.GetFullPath(cwdCandidate);
                }
            }

            var migrationsAssembly = typeof(Listenarr.Infrastructure.Repositories.QualityProfileRepository).Assembly.GetName().Name;

            optionsBuilder.UseSqlite($"Data Source={dbPath}", sqliteOptions =>
            {
                sqliteOptions.MigrationsAssembly(migrationsAssembly);
            });

            return new ListenArrDbContext(optionsBuilder.Options);
        }

        private static string? FindRepoRoot()
        {
            try
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    // Look for solution file or listenarr.api folder as sentinel
                    var slnx = Path.Combine(dir.FullName, "listenarr.slnx");
                    var sln = Path.Combine(dir.FullName, "listenarr.sln");
                    var apiFolder = Path.Combine(dir.FullName, "listenarr.api");
                    if (File.Exists(slnx) || File.Exists(sln) || Directory.Exists(apiFolder))
                    {
                        return dir.FullName;
                    }

                    dir = dir.Parent;
                }
            }
            catch { /* ignore and return null */ }

            return null;
        }
    }
}
