using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;
using WebClient.Pages;
using Xunit;

namespace WebClient.Tests.Integration;
public class IndexPageIntegrationTests : IClassFixture<WebApplicationFactory<IndexModel>>
{
    private readonly WebApplicationFactory<IndexModel> _factory;

    public IndexPageIntegrationTests(WebApplicationFactory<IndexModel> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient("ManagerClient")
                    .ConfigurePrimaryHttpMessageHandler(() =>
                        new TestHttpMessageHandler());
            });
        });
    }

    [Fact]
    public async Task Get_IndexPage_ReturnsSuccess()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        response.IsSuccessStatusCode.Should().BeTrue();
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Обработка файла");
        content.Should().Contain("processingForm");
    }

    private string ExtractAntiForgeryToken(string html)
    {
        var start = html.IndexOf("__RequestVerificationToken");
        if (start == -1) return string.Empty;
        var valueStart = html.IndexOf("value=\"", start) + 7;
        var valueEnd = html.IndexOf("\"", valueStart);
        return html[valueStart..valueEnd];
    }
}

public class TestHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Возвращаем заглушку для Manager API
        var response = new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("{\"sessionId\":\"test-integration-session\"}", Encoding.UTF8, MediaTypeNames.Application.Json)
        };
        return Task.FromResult(response);
    }
}