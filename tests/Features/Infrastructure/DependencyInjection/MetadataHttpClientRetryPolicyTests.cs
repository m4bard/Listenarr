/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
using System.Net;
using Listenarr.Tests.Common;
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;
using Polly.Timeout;

namespace Listenarr.Tests.Features.Infrastructure.DependencyInjection;

// AddMetadataHttpClients registers a three-attempt exponential-backoff retry policy on the
// Audible and Audnexus HttpClients. Every Audible call reaches that policy through
// AudibleApiClient, which gives each call its own CancellationTokenSource and passes that
// token into SendAsync (AudibleApiClient.cs:81 and :108).
//
// These tests count how many attempts actually reach the message handler when that per-call
// timeout fires, because the two mechanisms interact in a way that is not visible by reading
// either one alone. The handler counter is the whole point: a policy that looks configured for
// four attempts and delivers one is worth having written down.
//
// The clients here are built with the same AddHttpClient plus AddPolicyHandler shape as the
// real registration, with the primary handler swapped for a counter and the durations scaled
// down so the suite does not spend the real ten second budget proving it.
[Trait("Name", "MetadataHttpClientRetryPolicyTests")]
[Trait("Category", "Infrastructure")]
public sealed class MetadataHttpClientRetryPolicyTests : BaseTests
{
    private static readonly TimeSpan HandlerNeverAnswers = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CallerTimeout = TimeSpan.FromMilliseconds(250);
    private const string ProbeUrl = "http://metadata-retry-policy.invalid/catalog/products";

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;
        private int _attempts;

        public CountingHandler(TimeSpan delay) => _delay = delay;

        public int Attempts => Volatile.Read(ref _attempts);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            await Task.Delay(_delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static HttpClient BuildClient(IAsyncPolicy<HttpResponseMessage> policy, HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("metadata-retry-probe")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddPolicyHandler(policy);
        return services.BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient("metadata-retry-probe");
    }

    private static async Task<int> CountAttemptsAsync(
        IAsyncPolicy<HttpResponseMessage> policy,
        CancellationToken callerToken)
    {
        var handler = new CountingHandler(HandlerNeverAnswers);
        var client = BuildClient(policy, handler);
        try
        {
            await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, ProbeUrl), callerToken);
        }
        catch (Exception)
        {
            // Every case here is expected to end in an exception. The attempt count is the result.
        }

        return handler.Attempts;
    }

    // The registered policy verbatim, from MetadataRegistrationExtensions.cs:25-26.
    private static IAsyncPolicy<HttpResponseMessage> RegisteredRetryPolicy() =>
        HttpPolicyExtensions.HandleTransientHttpError()
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

    [Fact]
    public async Task RegisteredRetryPolicy_MakesOneAttempt_WhenTheCallerTimesOutTheRequest()
    {
        using var caller = new CancellationTokenSource(CallerTimeout);

        var attempts = await CountAttemptsAsync(RegisteredRetryPolicy(), caller.Token);

        Assert.Equal(1, attempts);
    }

    // HandleTransientHttpError matches HttpRequestException and 5xx/408 responses, so the obvious
    // repair is to name the exception a timed-out CancellationTokenSource actually produces. It
    // does not help. The token Polly is executing under is the same one that just fired, and the
    // retry engine checks it before sleeping and again at the top of the loop, so the second
    // attempt is cancelled before it is made. Widening the predicate cannot reach that.
    [Fact]
    public async Task NamingTaskCanceledExceptionInThePredicate_StillMakesOneAttempt()
    {
        var policy = HttpPolicyExtensions.HandleTransientHttpError()
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
        using var caller = new CancellationTokenSource(CallerTimeout);

        var attempts = await CountAttemptsAsync(policy, caller.Token);

        Assert.Equal(1, attempts);
    }

    // A per-attempt timeout inside the retry does work, and is the control for the two tests
    // above. Polly's timeout policy caps each attempt from a linked token of its own and raises
    // TimeoutRejectedException, which leaves the caller's token uncancelled, so the retry above
    // it is free to try again. The caller's own CancellationTokenSource still bounds the call.
    [Fact]
    public async Task APerAttemptTimeoutInsideTheRetry_MakesAllFourAttempts()
    {
        var retry = HttpPolicyExtensions.HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .WaitAndRetryAsync(3, _ => TimeSpan.FromMilliseconds(50));
        var perAttemptTimeout = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromMilliseconds(300));
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));

        var attempts = await CountAttemptsAsync(Policy.WrapAsync(retry, perAttemptTimeout), caller.Token);

        Assert.Equal(4, attempts);
    }

    // A circuit breaker that names TaskCanceledException does count these timeouts, unlike the
    // retry. That matters for how one gets added: a breaker is stateful, so a single instance
    // shared by the Audible and Audnexus registrations would let Audible's timeouts open the
    // circuit for Audnexus. Same defect as #867, same fix as #868, one instance per client.
    [Fact]
    public async Task ACircuitBreakerNamingTaskCanceledException_OpensOnCallerTimeouts()
    {
        var breaker = HttpPolicyExtensions.HandleTransientHttpError()
            .Or<TaskCanceledException>()
            .CircuitBreakerAsync(2, TimeSpan.FromSeconds(30));
        var handler = new CountingHandler(HandlerNeverAnswers);
        var client = BuildClient(breaker, handler);

        for (var i = 0; i < 2; i++)
        {
            using var caller = new CancellationTokenSource(CallerTimeout);
            try
            {
                await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, ProbeUrl), caller.Token);
            }
            catch (Exception)
            {
                // Expected. The breaker's state after the run is the result.
            }
        }

        Assert.Equal(CircuitState.Open, ((ICircuitBreakerPolicy)breaker).CircuitState);
    }
}
