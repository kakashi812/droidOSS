using System.Net;
using System.Net.Sockets;
using DroidOSS.Core;

namespace DroidOSS.App;

/// <summary>
/// The running server: the input socket, discovery, and the timeout sweep, around
/// one <see cref="SessionManager"/>.
/// </summary>
/// <remarks>
/// Both front ends drive this — the window and <c>--console</c> — so neither can
/// drift from the other in what the server actually does. It reports, it never
/// prints: what to show, and how, is the front end's business.
///
/// Owns the sockets but not the pad backend, which is created first and outlives
/// a failed start: a "port in use" error can be retried without re-plugging the
/// driver connection.
/// </remarks>
public sealed class ServerHost : IDisposable
{
    private readonly UdpSessionListener _listener;
    private readonly DiscoveryResponder _discovery;
    private bool _disposed;

    public ServerHost(IPadBackend backend, string serverName)
    {
        Sessions = new SessionManager(backend);
        _listener = new UdpSessionListener(Sessions);
        _discovery = new DiscoveryResponder(Sessions, serverName);
    }

    public SessionManager Sessions { get; }

    /// <summary>The name phones see in their server list.</summary>
    public string ServerName => _discovery.ServerName;

    /// <summary>
    /// False when the discovery port could not be bound. The server still works;
    /// phones just have to be given the address by hand.
    /// </summary>
    public bool Discoverable { get; private set; }

    /// <summary>Why discovery is off, when it is.</summary>
    public string? DiscoveryError { get; private set; }

    /// <summary>Datagrams received on the input port, valid or not.</summary>
    public long Received => _listener.Received;

    /// <summary>
    /// Binds the sockets.
    /// </summary>
    /// <exception cref="SocketException">
    /// The input port is taken — almost always another copy of the server.
    /// </exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _listener.Bind();

        try
        {
            _discovery.Bind();
            Discoverable = true;
        }
        catch (SocketException ex)
        {
            Discoverable = false;
            DiscoveryError = ex.Message;
        }
    }

    /// <summary>
    /// Serves phones until cancelled, then zeroes and unplugs every pad.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            _listener.ListenAsync(cancellationToken),
            Discoverable ? _discovery.ListenAsync(cancellationToken) : Task.CompletedTask,
            SweepAsync(cancellationToken));

        // Every remaining session is zeroed and unplugged, in that order — the
        // manager owns that ordering so it cannot be got wrong here.
        Sessions.CloseAll();
    }

    /// <summary>
    /// Drops phones that have stopped sending.
    /// </summary>
    /// <remarks>
    /// Separate from the receive loop on purpose: a phone whose battery died
    /// sends nothing at all, so nothing in the receive path would ever run to
    /// notice. Ten times a second is far finer than the two-second timeout needs
    /// and costs nothing — the sweep walks at most four entries.
    /// </remarks>
    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                Sessions.SweepTimeouts();
        }
        catch (OperationCanceledException)
        {
            // Shutting down, expected.
        }
    }

    /// <summary>
    /// This machine's LAN addresses — what gets typed into the phone.
    /// </summary>
    /// <remarks>
    /// Loopback is excluded because it is useless to a phone. Several may be
    /// listed when there is both Wi-Fi and Ethernet, or a VM adapter; showing all
    /// of them beats guessing wrong.
    /// </remarks>
    public static IReadOnlyList<IPAddress> LocalAddresses()
    {
        IPAddress[] addresses;
        try
        {
            addresses = Dns.GetHostAddresses(Dns.GetHostName());
        }
        catch (SocketException)
        {
            return [];
        }

        return addresses
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
            .ToList();
    }

    /// <summary>Why a session ended, in a few words.</summary>
    public static string Describe(SessionCloseReason? reason) => reason switch
    {
        SessionCloseReason.Bye => "said goodbye",
        SessionCloseReason.Timeout => "timed out",
        SessionCloseReason.ServerShutdown => "server stopping",
        _ => "unknown",
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _listener.Dispose();
        _discovery.Dispose();
    }
}
