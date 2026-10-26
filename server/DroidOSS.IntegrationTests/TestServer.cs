using System.Net;
using System.Net.Sockets;
using DroidOSS.Core;
using DroidOSS.Net;

namespace DroidOSS.IntegrationTests;

/// <summary>
/// A real <see cref="ServerHost"/> on loopback, on ports the OS picked, running
/// until the test disposes it.
/// </summary>
/// <remarks>
/// Port 0 for both sockets means tests never collide with each other, with a
/// droidOSS server the developer has open, or with another CI job.
/// </remarks>
public sealed class TestServer : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _running;

    public TestServer(string name = "test-pc", TimeSpan? sessionTimeout = null)
    {
        Host = new ServerHost(Backend, name, inputPort: 0, discoveryPort: 0, sessionTimeout);
        Host.Start();
        _running = Task.Run(() => Host.RunAsync(_stop.Token));
    }

    public RecordingPadBackend Backend { get; } = new();

    public ServerHost Host { get; }

    public IPEndPoint InputEndPoint => new(IPAddress.Loopback, Host.InputPort);

    public IPEndPoint DiscoveryEndPoint => new(IPAddress.Loopback, Host.DiscoveryPort);

    /// <summary>A new phone with its own socket, so the server sees a distinct client.</summary>
    public Phone NewPhone() => new(InputEndPoint);

    /// <summary>Stops the server the way closing the window does.</summary>
    public async Task StopAsync()
    {
        if (_stop.IsCancellationRequested) return;
        await _stop.CancelAsync();
        await _running.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        Host.Dispose();
        _stop.Dispose();
    }

    /// <summary>
    /// Polls until <paramref name="condition"/> holds. UDP and the server's own
    /// threads mean results arrive "soon", never "now".
    /// </summary>
    public static async Task Eventually(Func<bool> condition, string because, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        Assert.Fail($"Timed out after {timeoutMs} ms waiting until {because}.");
    }
}

/// <summary>
/// The phone side of the protocol, written against <c>DroidOSS.Core</c>'s
/// encoders: one UDP socket, the same as the Android app.
/// </summary>
public sealed class Phone(IPEndPoint server) : IDisposable
{
    private readonly UdpClient _udp = new(new IPEndPoint(IPAddress.Loopback, 0));
    private uint _sequence;

    /// <summary>Sends HELLO and returns the pad in the WELCOME, or <see cref="Protocol.NoPad"/> when full.</summary>
    public async Task<byte> HelloAsync()
    {
        var hello = new byte[Protocol.SessionMessageSize];
        SessionMessage.Write(hello, MessageType.Hello, Protocol.NoPad);
        await _udp.SendAsync(hello, server);

        var reply = await ReceiveAsync();
        Assert.True(SessionMessage.TryRead(reply, out var type, out var pad), "reply was not a session message");
        Assert.Equal(MessageType.Welcome, type);
        return pad;
    }

    public async Task SendInputAsync(byte pad, PadState state, uint? sequence = null)
    {
        var packet = new byte[Protocol.InputPacketSize];
        InputPacket.Write(packet, pad, sequence ?? ++_sequence, in state);
        await _udp.SendAsync(packet, server);
    }

    public async Task ByeAsync(byte pad)
    {
        var bye = new byte[Protocol.SessionMessageSize];
        SessionMessage.Write(bye, MessageType.Bye, pad);
        await _udp.SendAsync(bye, server);
    }

    public Task SendRawAsync(byte[] data) => _udp.SendAsync(data, server).AsTask();

    public Task SendRawAsync(byte[] data, IPEndPoint to) => _udp.SendAsync(data, to).AsTask();

    public async Task<byte[]> ReceiveAsync(int timeoutMs = 2000)
    {
        using var timeout = new CancellationTokenSource(timeoutMs);
        var result = await _udp.ReceiveAsync(timeout.Token);
        return result.Buffer;
    }

    public void Dispose() => _udp.Dispose();
}
