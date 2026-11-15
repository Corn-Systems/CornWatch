using System.Net.NetworkInformation;

namespace CornWatch.Core;

/// <summary>
/// Measures throughput on the adapter that actually carries traffic (up, has a
/// default gateway). The adapter is re-picked periodically so switching between
/// Wi-Fi and Ethernet, or connecting a VPN, is followed automatically.
/// </summary>
internal sealed class NetworkMeter
{
    private static readonly TimeSpan _repickEvery = TimeSpan.FromSeconds(10);

    private NetworkInterface? _nic;
    private DateTime _pickedUtc = DateTime.MinValue, _prevUtc;
    private long _prevSent, _prevReceived;

    public string AdapterName => _nic?.Name ?? string.Empty;

    public (float sentMbps, float receivedMbps) Read()
    {
        var now = DateTime.UtcNow;
        if (now - _pickedUtc > _repickEvery)
        {
            _pickedUtc = now;
            var picked = Pick();
            if (picked?.Id != _nic?.Id)
            {
                _nic = picked;
                _prevUtc = DateTime.MinValue;
                SessionLog.Write($"[NET] monitoring adapter: {(_nic is null ? "none" : _nic.Name)}");
            }
        }
        if (_nic is null) return (0f, 0f);

        var stats = _nic.GetIPStatistics();
        var seconds = (now - _prevUtc).TotalSeconds;
        var result = _prevUtc == DateTime.MinValue || seconds <= 0
            ? (0f, 0f)
            : (ToMbps(stats.BytesSent - _prevSent, seconds), ToMbps(stats.BytesReceived - _prevReceived, seconds));

        _prevUtc = now;
        _prevSent = stats.BytesSent;
        _prevReceived = stats.BytesReceived;
        return result;
    }

    // Megabits per second; negative deltas (counter reset) read as zero.
    private static float ToMbps(long bytes, double seconds) => (float)(Math.Max(0, bytes) * 8 / 1_000_000.0 / seconds);

    private static NetworkInterface? Pick() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count > 0)
            .ThenByDescending(n => n.Speed)
            .FirstOrDefault();
}
