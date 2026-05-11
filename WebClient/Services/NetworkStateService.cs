using System.Net.NetworkInformation;

namespace WebClient.Services;

public sealed class NetworkStateService : IDisposable
{
    private volatile bool _networkAvailable = true;

    public bool IsNetworkAvailable => _networkAvailable;

    public event Action? NetworkRestored;

    public NetworkStateService()
    {
        _networkAvailable = NetworkInterface.GetIsNetworkAvailable();

        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
    }

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        _networkAvailable = e.IsAvailable;

        if (e.IsAvailable)
            NetworkRestored?.Invoke();
    }

    private void OnAddressChanged(object? sender, EventArgs e)
    {
        _networkAvailable = NetworkInterface.GetIsNetworkAvailable();

        if (_networkAvailable)
            NetworkRestored?.Invoke();
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
    }
}