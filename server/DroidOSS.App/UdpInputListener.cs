using System.Net;
using System.Net.Sockets;
using DroidOSS.Core;

namespace DroidOSS.App;

/// <summary>
/// Receives datagrams and hands them to a <see cref="PadServer"/>.
/// </summary>
/// <remarks>
/// The only networking code in the project. It exists so that everything
/// interesting stays in <see cref="PadServer"/>, which can then be tested with
/// no socket in sight.
///
/// One socket serves every client. UDP has no connections — there is nothing to
/// accept and no per-client socket. Each datagram arrives with its sender's
/// address attached, which is how clients are told apart at B3.
/// </remarks>
public sealed class UdpInputListener(PadServer server, int port = Protocol.InputPort) : IDisposable
{
    /// <summary>
    /// Comfortably larger than a 20-byte INPUT packet.
    /// </summary>
    /// <remarks>
    /// Oversized datagrams are received in full and then rejected on length by
    /// the parser. A buffer of exactly 20 would silently truncate them into
    /// something that looks valid, which is a far nastier failure.
    /// </remarks>
    private const int BufferSize = 64;

    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

    /// <summary>Rewritten in place by each receive — never copied per packet.</summary>
    private readonly SocketAddress _senderAddress = new(AddressFamily.InterNetwork);

    /// <summary>Only used to turn a <see cref="SocketAddress"/> into an endpoint on demand.</summary>
    private static readonly IPEndPoint EndPointTemplate = new(IPAddress.Any, 0);

    private bool _disposed;

    /// <summary>Datagrams received, including ones that turned out to be junk.</summary>
    public long Received { get; private set; }

    /// <summary>
    /// Where the most recent packet came from. Null until one arrives.
    /// </summary>
    /// <remarks>
    /// Built on access rather than per packet. Materialising an
    /// <see cref="IPEndPoint"/> allocates, and the status line asks for this once
    /// a second while packets arrive 125 times a second.
    /// </remarks>
    public IPEndPoint? LastSender =>
        Received == 0 ? null : (IPEndPoint)EndPointTemplate.Create(_senderAddress);

    /// <summary>Binds the port. Throws if something else already holds it.</summary>
    public void Bind()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
    }

    /// <summary>
    /// Receives until cancelled, feeding every datagram to the server.
    /// </summary>
    /// <remarks>
    /// One buffer, allocated once and reused for the lifetime of the loop. The
    /// obvious <c>UdpClient.ReceiveAsync</c> would allocate a fresh array and a
    /// result object per datagram; at 125 packets a second that is a steady
    /// stream of garbage for the collector, and GC pauses are one of the few
    /// things that cause a genuinely visible latency spike.
    /// </remarks>
    public async Task ListenAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var buffer = new byte[BufferSize];

        while (!cancellationToken.IsCancellationRequested)
        {
            int length;
            try
            {
                length = await _socket.ReceiveFromAsync(
                    buffer, SocketFlags.None, _senderAddress, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;   // Ctrl+C, expected
            }
            catch (SocketException)
            {
                // A datagram that could not be delivered can surface here on
                // Windows (ICMP port unreachable from a previous send). It says
                // nothing about our socket's health, so keep listening.
                continue;
            }

            Received++;
            server.Handle(buffer.AsSpan(0, length));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _socket.Dispose();
    }
}
