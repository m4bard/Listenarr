using System.Net;
using Listenarr.Application.Common.Scheduling;
using Listenarr.Tests.Common;
using Listenarr.Tests.Mocks;

namespace Listenarr.Tests.Features.Api.Features.SystemDiagnostics;

/// <summary>
/// The interval wire format over the registered MVC pipeline rather than over a copy of
/// its serializer options.
/// </summary>
/// <remarks>
/// <c>ScheduledTaskIntervalWireFormatTests</c> builds its own
/// <c>JsonSerializerOptions</c> to match
/// <c>listenarr.api/Startup/ListenarrServiceRegistration.cs:41-45</c>, which is faithful
/// today and cannot notice the day it stops being. The explicit null is the one thing the
/// whole shape rests on, so it is worth one test that reads the bytes the application
/// actually sends.
/// </remarks>
[Trait("Name", "ScheduledTaskIntervalPipelineTests")]
[Trait("Category", "Api")]
public sealed class ScheduledTaskIntervalPipelineTests(ListenarrWebApplicationFactory factory)
    : BaseTests, IClassFixture<ListenarrWebApplicationFactory>
{
    [Fact]
    public async Task GetAll_ARefusedIntervalArrivesAsAnExplicitNull_AndTheOtherRowArrivesWhole()
    {
        // The test host sets Listenarr:DisableHostedServices, so no worker registers and
        // the real registry is empty. The rows have to be substituted rather than started.
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(
            services => services.AddSingleton<IScheduledTaskRegistry>(_ =>
            {
                var registry = new Mock<IScheduledTaskRegistry>();
                registry.Setup(candidate => candidate.GetAll()).Returns(new[]
                {
                    CreateStatus("HealthyWorker", TimeSpan.FromSeconds(10)),
                    CreateStatus("SubSecondWorker", TimeSpan.FromMilliseconds(500))
                });
                return registry.Object;
            })));
        var apiBase = TestUtils.ResolveApiBasePath(host.Services);
        using var client = host.CreateClient();

        using var response = await client.GetAsync($"{apiBase}/system/tasks");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"intervalSeconds\":null", body, StringComparison.Ordinal);
        Assert.Contains("\"intervalSeconds\":10", body, StringComparison.Ordinal);
        Assert.Contains("\"intervalError\":", body, StringComparison.Ordinal);

        // Two controls on the same payload, and both have to come out this way or the
        // assertions above prove nothing. lastStartedAt is null on both rows and carries no
        // attribute, so its absence is WhenWritingNull provably in force in the registered
        // pipeline; intervalError is absent from the healthy row for the same reason, which
        // is what makes its presence on the other row a statement about that row.
        Assert.DoesNotContain("\"lastStartedAt\"", body, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(body, "\"intervalError\":"));
    }

    private static int CountOf(string body, string needle)
    {
        var count = 0;
        var at = body.IndexOf(needle, StringComparison.Ordinal);

        while (at >= 0)
        {
            count++;
            at = body.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static ScheduledTaskStatus CreateStatus(string taskName, TimeSpan interval) => new()
    {
        TaskName = taskName,
        Interval = interval,
        RegisteredAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        IsRegistered = true,
        IsRunning = false,
        ManualTrigger = ScheduledTaskManualTrigger.Allowed,
        LastStartedAt = null,
        LastOutcome = ScheduledTaskOutcome.Unknown
    };
}
