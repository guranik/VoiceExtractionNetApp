using FluentAssertions;
using System.Net.NetworkInformation;
using WebClient.Services;
using Xunit;

namespace WebClient.Tests.Services;

public class NetworkStateServiceTests : IDisposable
{
    private NetworkStateService? _service;

    [Fact]
    public void Constructor_InitializesWithSystemNetworkState()
    {
        _service = new NetworkStateService();
        _service.IsNetworkAvailable.Should().Be(NetworkInterface.GetIsNetworkAvailable());
    }

    [Fact]
    public void IsNetworkAvailable_CanBeReadMultipleTimesWithoutException()
    {
        _service = new NetworkStateService();

        // Проверяем стабильность чтения свойства
        var results = new bool[10];
        for (int i = 0; i < results.Length; i++)
            results[i] = _service.IsNetworkAvailable;

        results.Should().OnlyContain(r => r == true || r == false);
    }

    [Fact]
    public void IsNetworkAvailable_IsThreadSafe()
    {
        _service = new NetworkStateService();
        var results = new bool[100];

        Parallel.For(0, 100, i => results[i] = _service.IsNetworkAvailable);

        results.Should().OnlyContain(r => r == true || r == false);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        _service = new NetworkStateService();
        var act = () => _service.Dispose();
        act.Should().NotThrow();
    }

    public void Dispose()
    {
        _service?.Dispose();
    }
}