/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 */

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Listenarr.Tests.Common;

/// <summary>
/// A real HTTP server on 127.0.0.1 for tests that must exercise the real client handler, such as
/// its redirect behaviour, which a stub message handler would replace. Records every request path
/// it receives, so wire requests are counted independently of the code under test.
/// </summary>
public sealed class LoopbackHttpServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Action<HttpListenerContext> _respond;
    private readonly ConcurrentQueue<string> _requestPaths = new();
    private readonly Task _serveLoop;

    public LoopbackHttpServer(Action<HttpListenerContext> respond)
    {
        _respond = respond;
        BaseUri = new Uri($"http://127.0.0.1:{FindFreePort()}/");
        _listener.Prefixes.Add(BaseUri.ToString());
        _listener.Start();
        _serveLoop = Task.Run(ServeAsync);
    }

    public Uri BaseUri { get; }

    public IReadOnlyList<string> RequestPaths => _requestPaths.ToArray();

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        _listener.Close();
        await _serveLoop;
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            _requestPaths.Enqueue(context.Request.Url!.AbsolutePath);
            try
            {
                _respond(context);
            }
            finally
            {
                context.Response.Close();
            }
        }
    }

    private static int FindFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
