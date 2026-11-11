using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Xaquiamuni.Tests;

public class HealthEndpointTests
{
    [Fact]
    public async Task GetHealth_ReturnsHealthyStatus()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("healthy", json.RootElement.GetProperty("status").GetString());
    }
}
