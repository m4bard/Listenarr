/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

namespace Listenarr.Tests.Common;

/// <summary>
/// An <see cref="IHttpClientFactory"/> whose clients all send through one handler. Each call
/// returns a new client that leaves the handler alone when disposed, as the real factory's
/// clients do.
/// </summary>
public sealed class StubHttpClientFactory(HttpMessageHandler handler, Uri? baseAddress = null) : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
        => new(handler, disposeHandler: false) { BaseAddress = baseAddress };
}
