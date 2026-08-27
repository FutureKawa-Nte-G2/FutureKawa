using System.Net;
using System.Net.Http.Json;

namespace FutureKawaSiege.API.Tests.Integration;

public class OrdersControllerIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public OrdersControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PatchStatus_Should_Return404_When_EndpointRemoved()
    {
        var response = await _client.PatchAsJsonAsync(
            $"/api/orders/{Guid.NewGuid()}/status",
            new { status = "Shipped" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
