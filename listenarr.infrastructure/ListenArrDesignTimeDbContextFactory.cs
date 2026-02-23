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
                // Default to repo-relative path where the API stores the DB during development
                var repoRoot = Directory.GetCurrentDirectory();
                // Handle running from different working directories: attempt common relative location
                var candidate = Path.Combine(repoRoot, "..", "listenarr.api", "config", "database", "listenarr.db");
                candidate = Path.GetFullPath(candidate);
                dbPath = candidate;
            }

            var migrationsAssembly = typeof(Listenarr.Infrastructure.Repositories.QualityProfileRepository).Assembly.GetName().Name;

            optionsBuilder.UseSqlite($"Data Source={dbPath}", sqliteOptions =>
            {
                sqliteOptions.MigrationsAssembly(migrationsAssembly);
            });

            return new ListenArrDbContext(optionsBuilder.Options);
        }
    }
}
