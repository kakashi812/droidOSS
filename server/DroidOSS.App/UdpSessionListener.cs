using System.Net;
using System.Net.Sockets;
using DroidOSS.Core;

namespace DroidOSS.App;

/// <summary>
/// Receives datagrams, hands them to a <see cref="SessionManager"/>, and sends
/// back whatever reply that produces.
/// </summary>
/// <remarks>
/// The only networking code in the project. It exists so that everything
/// interesting stays in <see cref="SessionManager"/>, which can then be tested
/// with no socket in sight.
///
/// One socket serves every phone. UDP has no connections — there is nothing to
/// accept and no per-client socket. Each datagram arrives with its sender's
/// address attached, and that address is the entire basis on which four phones
/// are told apart.
/// </remarks>
public sealed class UdpSessionListener(SessionManager sessions, int port = Protocol.InputPort)
    : IDisposable
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

    private bool _disposed;

    /// <summary>Datagrams received, including ones that turned out to be junk.</summary>
    public long Received { get; private set; }

    /// <summary>Replies sent — WELCOMEs, mostly.</summary>
    public long Sent { get; private set; }

    /// <summary>Binds the port. Throws if something else already holds it.</summary>
    public void Bind()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _socket.Bind(new IPEndPoint(IPAddress.Any, port));
    }

    /// <summary>
    /// Receives until cancelled, feeding every datagram to the session manager.
    /// </summary>
    /// <remarks>
    /// Two buffers, allocated once and reused for the lifetime of the loop. The
    /// obvious <c>UdpClient.ReceiveAsync</c> would allocate a fresh array and a
    /// result object per datagram; at 125 packets a second per phone that is a
    /// steady stream of garbage for the collector, and GC pauses are one of the
    /// few things that cause a genuinely visible latency spike.
    /// </remarks>
    public async Task ListenAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var buffer = new byte[BufferSize];
        var reply = new byte[Protocol.SessionMessageSize];

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

            var sender = ToClientKey(_senderAddress);
            var outcome = sessions.Handle(
                buffer.AsSpan(0, length), sender, reply, out var replyLength);

            _ = outcome;   // counters live on the manager; nothing to do with it here

            if (replyLength > 0) Reply(reply.AsSpan(0, replyLength));
        }
    }

    /// <summary>
    /// Answers whoever just sent us something.
    /// </summary>
    /// <remarks>
    /// Sent to <see cref="_senderAddress"/>, which still holds the address of the
    /// datagram being handled — the reply always happens inside the same loop
    /// iteration, before the next receive overwrites it.
    /// </remarks>
    private void Reply(ReadOnlySpan<byte> data)
    {
        try
        {
            _socket.SendTo(data, SocketFlags.None, _senderAddress);
            Sent++;
        }
        catch (SocketException)
        {
            // The phone may already be gone — it is a WELCOME, not a promise.
            // If it never arrives the phone re-sends HELLO and we try again.
        }
    }

    /// <summary>
    /// Turns a raw socket address into the key the session table uses.
    /// </summary>
    /// <remarks>
    /// Reads the bytes directly rather than materialising an
    /// <see cref="IPEndPoint"/>, which is a class and would therefore allocate
    /// once per datagram — 125 times a second per phone. This is the whole reason
    /// <see cref="ClientKey"/> is two primitives instead of an endpoint.
    ///
    /// The layout is <c>sockaddr_in</c>: two bytes of address family, then the
    /// port and the address, both big-endian because that is network byte order.
    /// </remarks>
    private static ClientKey ToClientKey(SocketAddress address)
    {
        var port = (ushort)((address[2] << 8) | address[3]);

        var ip = ((uint)address[4] << 24)
               | ((uint)address[5] << 16)
               | ((uint)address[6] << 8)
               | address[7];

        return new ClientKey(ip, port);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _socket.Dispose();
    }
}
