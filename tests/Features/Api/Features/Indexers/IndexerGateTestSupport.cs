/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Net;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.Features.Indexers;

/// <summary>
/// Gates and servers shared by the tests of the indexer test and debug search workflows.
/// </summary>
internal static class IndexerGateTestSupport
{
    public static readonly RequestDeferral Cooldown = new(
        new DateTime(2026, 6, 19, 12, 30, 0, DateTimeKind.Utc),
        RequestDeferralReason.RateLimited);

    /// <summary>
    /// A server that answers the first path with a 302 to <c>/moved</c> and every other path with
    /// the given status and body.
    /// </summary>
    public static LoopbackHttpServer RedirectingServer(string firstPath, string body = "")
        => new(context =>
        {
            if (context.Request.Url!.AbsolutePath == firstPath)
            {
                context.Response.StatusCode = (int)HttpStatusCode.Redirect;
                context.Response.RedirectLocation = "/moved";
                return;
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        });

    /// <summary>
    /// Records every request context and then dispatches, as the pass-through gate does.
    /// </summary>
    public sealed class RecordingGate : IIndexerRequestGate
    {
        public List<IndexerRequestContext> Contexts { get; } = new();

        public async Task<IndexerRequestResult> SendAsync(
            IndexerRequestContext context,
            Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
            CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            return new IndexerRequestResult.Sent(await dispatch(cancellationToken));
        }

        public Task<RequestDeferral?> GetDeferralAsync(int indexerId, SearchOrigin origin, CancellationToken cancellationToken)
            => Task.FromResult<RequestDeferral?>(null);
    }

    /// <summary>
    /// Defers every request, as the real gate does while an indexer is in cooldown.
    /// </summary>
    public sealed class DeferringGate : IIndexerRequestGate
    {
        public Task<IndexerRequestResult> SendAsync(
            IndexerRequestContext context,
            Func<CancellationToken, Task<HttpResponseMessage>> dispatch,
            CancellationToken cancellationToken)
            => Task.FromResult<IndexerRequestResult>(new IndexerRequestResult.Deferred(Cooldown));

        public Task<RequestDeferral?> GetDeferralAsync(int indexerId, SearchOrigin origin, CancellationToken cancellationToken)
            => Task.FromResult<RequestDeferral?>(Cooldown);
    }
}
