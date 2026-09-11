/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */
using Listenarr.Infrastructure.DependencyInjection.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace Listenarr.Infrastructure.DependencyInjection.Metadata;

internal static class MetadataRegistrationExtensions
{
    public static IServiceCollection AddMetadataHttpClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var retryPolicy = HttpPolicyExtensions.HandleTransientHttpError()
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
        services.AddHttpClient<AudibleService>()
            .ConfigurePrimaryHttpMessageHandler(PlatformRegistrationExtensions.CreateExternalHandler)
            .AddPolicyHandler(retryPolicy);
        services.AddHttpClient<IAudnexusService, AudnexusService>()
            .ConfigurePrimaryHttpMessageHandler(PlatformRegistrationExtensions.CreateExternalHandler)
            .AddPolicyHandler(retryPolicy);
        return services;
    }

    public static IServiceCollection AddMetadataServices(this IServiceCollection services)
    {
        services.AddScoped<IMetadataService, MetadataService>();
        services.AddScoped<IAsinLookupService, AsinLookupService>();
        services.AddScoped<IAudiobookMetadataService, AudiobookMetadataService>();
        services.AddScoped<IMetadataRefreshService, MetadataRefreshService>();
        services.AddScoped<IOpenLibraryService, OpenLibraryService>();
        // The coordinator gates every refresh entry point, including the foreground API trigger
        // in LibraryController, so it is registered unconditionally here rather than inside
        // AddFeatureWorkers: that registration is skipped whenever background hosted services
        // are disabled (test hosts, and any future operator override), which would otherwise
        // leave the library API unable to construct at all.
        services.AddSingleton<MetadataRefreshOptionsHolder>();
        services.AddSingleton<IMetadataRefreshCoordinator, MetadataRefreshCoordinator>();
        services.AddSingleton<MetadataExtractionLimiter>();
        services.AddHttpClient("Ffmpeg");
        services.AddSingleton<IFfmpegService>(provider =>
            new FfmpegService(
                provider.GetRequiredService<ILogger<FfmpegService>>(),
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("Ffmpeg"),
                provider.GetRequiredService<IStartupConfigService>(),
                provider.GetRequiredService<IProcessRunner>(),
                provider.GetRequiredService<IApplicationPathService>()));
        return services;
    }

    public static IServiceCollection AddMetadataInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IHtmlTextExtractor, HtmlAgilityPackTextExtractor>();
        services.AddSingleton<IAudibleAuthorPageParser, HtmlAgilityPackAudibleAuthorPageParser>();
        services.AddScoped<IAudioTagWriter, TagLibAudioTagWriter>();
        services.AddHttpClient<ICoverImageProbe, ImageSharpCoverImageProbe>();
        services.AddHttpClient<ImageCacheService>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.All
            });
        services.AddSingleton<IImageCacheService, ImageCacheService>();
        return services;
    }
}
