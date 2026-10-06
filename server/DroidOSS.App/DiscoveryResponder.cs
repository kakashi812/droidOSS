using System.Net;
using System.Net.Sockets;
using DroidOSS.Core;

namespace DroidOSS.App;

/// <summary>
/// Answers "any servers out there?" so a phone can list this PC without anyone
/// typing its address.
/// </summary>
/// <remarks>
/// A phone broadcasts DISCOVER to <see cref="Protocol.DiscoveryPort"/>; every
/// server on the network that hears it replies straight back to the sender with
/// its name and how many pads are free. The phone takes the address from the
/// reply's source, which is also the one answer to "which of the addresses the
/// server printed is the right one" that is never wrong.
///
/// Its own socket on its own port, so broadcast traffic never touches the input
/// socket's hot path. Discovery is a handful of packets when someone opens the
/// app, so unlike <see cref="UdpSessionListener"/> this allocates freely.
/// </remarks>
public sealed class DiscoveryResponder(
    SessionManager sessions, string serverName, int port = Protocol.DiscoveryPort) : IDisposable
{
    private const int BufferSize = 64;

    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

    private bool _disposed;

    /// <summary>The name phones are shown.</summary>
    public string ServerName { get; } = serverName;

    /// <summary>Binds the discovery port on every interface.</summary>
    /// <exception cref="SocketException">The port is taken.</exception>
    public void Bind()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
    }

    /// <summary>Answers DISCOVER until cancelled.</summary>
    public async Task ListenAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var buffer = new byte[BufferSize];
        var reply = new byte[DiscoveryMessage.MaxAnnounceSize];
        var any = new IPEndPoint(IPAddress.Any, 0);

        while (!cancellationToken.IsCancellationRequested)
        {
            SocketReceiveFromResult received;
            try
            {
                received = await _socket.ReceiveFromAsync(
                    buffer, SocketFlags.None, any, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;   // Ctrl+C, expected
            }
            catch (SocketException)
            {
                // Windows reports an ICMP "port unreachable" from an earlier
                // reply as an error on the next receive. Not ours to act on.
                continue;
            }

            if (!DiscoveryMessage.IsDiscover(buffer.AsSpan(0, received.ReceivedBytes)))
                continue;

            var length = DiscoveryMessage.WriteAnnounce(
                reply, ServerName, Protocol.InputPort, sessions.FreeSlots);

            try
            {
                _socket.SendTo(reply.AsSpan(0, length), SocketFlags.None, received.RemoteEndPoint);
            }
            catch (SocketException)
            {
                // The phone will ask again; it repeats DISCOVER while it scans.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _socket.Dispose();
    }
}
