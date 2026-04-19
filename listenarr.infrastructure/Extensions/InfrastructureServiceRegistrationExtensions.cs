using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Listenarr.Api.Services;
using Listenarr.Application.Repositories;
using Listenarr.Infrastructure.Models;
using Listenarr.Infrastructure.Repositories;

namespace Listenarr.Infrastructure.Extensions
{
    /// <summary>
    /// Registers infrastructure implementations (repositories, persistence adapters, etc.).
    /// Keep this in the Infrastructure project so Program.cs can call a single registration surface.
    /// </summary>
    public static class InfrastructureServiceRegistrationExtensions
    {
        /// <summary>
        /// Registers all infrastructure services. When <paramref name="sqliteDbPath"/> is provided
        /// the DbContext, DbContextOptions, and IDbContextFactory are also registered (production
        /// and integration-test paths). Omit the parameter only when the caller has already
        /// registered a DbContext (e.g. unit tests using UseInMemoryDatabase).
        /// </summary>
        public static IServiceCollection AddListenarrInfrastructure(
            this IServiceCollection services,
            string? sqliteDbPath = null)
        {
            if (sqliteDbPath != null)
            {
                // Build DbContextOptions once and register as singleton so factories
                // and background services can create contexts without EF needing to
                // resolve scoped option-configurators from the root provider.
                var dbOptionsBuilder = new DbContextOptionsBuilder<ListenArrDbContext>();
                dbOptionsBuilder.UseSqlite($"Data Source={sqliteDbPath}", sqliteOptions =>
                {
                    sqliteOptions.MigrationsAssembly(typeof(QualityProfileRepository).Assembly.GetName().Name);
                });

                services.AddSingleton<DbContextOptions<ListenArrDbContext>>(sp => dbOptionsBuilder.Options);

                services.AddSingleton<IDbContextFactory<ListenArrDbContext>>(sp =>
                    new SimpleDbContextFactory(sp.GetRequiredService<DbContextOptions<ListenArrDbContext>>()));

                services.AddDbContext<ListenArrDbContext>(options =>
                {
                    options.UseSqlite($"Data Source={sqliteDbPath}", sqliteOptions =>
                    {
                        sqliteOptions.MigrationsAssembly(typeof(QualityProfileRepository).Assembly.GetName().Name);
                    });
                }, ServiceLifetime.Scoped, ServiceLifetime.Singleton);
            }

            services.AddScoped<IAudiobookRepository, AudiobookRepository>();
            services.AddScoped<IQualityProfileRepository, QualityProfileRepository>();

            services.AddScoped<IIndexerRepository, EfIndexerRepository>();
            services.AddScoped<IUserRepository, EfUserRepository>();
            services.AddScoped<IUserSessionRepository, EfUserSessionRepository>();
            services.AddScoped<Listenarr.Application.Repositories.IHistoryRepository, EfHistoryRepository>();
            services.AddScoped<IApplicationSettingsRepository, EfApplicationSettingsRepository>();
            services.AddScoped<IApiConfigurationRepository, EfApiConfigurationRepository>();
            services.AddScoped<IDownloadClientConfigurationRepository, EfDownloadClientConfigurationRepository>();
            services.AddScoped<IRemotePathMappingRepository, EfRemotePathMappingRepository>();
            services.AddScoped<IAudiobookFileRepository, EfAudiobookFileRepository>();
            services.AddScoped<IMoveJobRepository, EfMoveJobRepository>();
            services.AddScoped<IMonitoredAuthorRepository, EfMonitoredAuthorRepository>();
            services.AddScoped<IMonitoredSeriesRepository, EfMonitoredSeriesRepository>();
            services.AddScoped<IProcessExecutionLogRepository, EfProcessExecutionLogRepository>();
            services.AddScoped<IDownloadRepository, EfDownloadRepository>();
            services.AddScoped<IDownloadProcessingJobRepository, EfDownloadProcessingJobRepository>();
            services.AddScoped<IRootFolderRepository, EfRootFolderRepository>();
            services.AddScoped<IDownloadHistoryRepository, DownloadHistoryRepository>();
            services.AddScoped<Listenarr.Application.Services.IDatabaseConnectionProvider, Listenarr.Infrastructure.Services.EfDatabaseConnectionProvider>();

            return services;
        }
    }

    /// <summary>
    /// Lightweight factory that creates ListenArrDbContext instances from a
    /// pre-built DbContextOptions singleton. Avoids EF registering scoped
    /// IDbContextOptionsConfiguration services that would be resolved from
    /// the root provider.
    /// </summary>
    internal sealed class SimpleDbContextFactory : IDbContextFactory<ListenArrDbContext>
    {
        private readonly DbContextOptions<ListenArrDbContext> _options;

        public SimpleDbContextFactory(DbContextOptions<ListenArrDbContext> options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public ListenArrDbContext CreateDbContext() => new ListenArrDbContext(_options);

        public Task<ListenArrDbContext> CreateDbContextAsync() =>
            Task.FromResult(new ListenArrDbContext(_options));
    }
}
