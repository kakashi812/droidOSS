using DroidOSS.Core;

namespace DroidOSS.IntegrationTests;

// Discovery over a real socket: DISCOVER in, announcement with name, input port and free pads out.
public class DiscoveryOverUdpTests
{
    private static async Task<(string Name, ushort Port, int Free)> DiscoverAsync(TestServer server, Phone phone)
    {
        var discover = new byte[DiscoveryMessage.DiscoverSize];
        DiscoveryMessage.WriteDiscover(discover);
        await phone.SendRawAsync(discover, server.DiscoveryEndPoint);

        var reply = await phone.ReceiveAsync();
        Assert.True(DiscoveryMessage.TryReadAnnounce(reply, out var name, out var port, out var free),
            "reply was not an announcement");
        return (name, port, free);
    }

    [Fact]
    public async Task Discover_is_answered_with_the_server_name_and_its_real_input_port()
    {
        await using var server = new TestServer(name: "LIVING-ROOM-PC");
        using var phone = server.NewPhone();

        var (name, port, free) = await DiscoverAsync(server, phone);

        Assert.Equal("LIVING-ROOM-PC", name);
        Assert.Equal(server.Host.InputPort, port);
        Assert.Equal(4, free);
    }

    [Fact]
    public async Task The_announced_free_pads_drop_as_phones_join()
    {
        await using var server = new TestServer();
        using var scanner = server.NewPhone();
        using var player = server.NewPhone();

        await player.HelloAsync();

        var (_, _, free) = await DiscoverAsync(server, scanner);
        Assert.Equal(3, free);
    }

    [Fact]
    public async Task Following_the_announcement_leads_to_a_working_session()
    {
        // What the app does: discover, read the port from the reply, then HELLO there.
        await using var server = new TestServer();
        using var phone = server.NewPhone();
        var (_, port, _) = await DiscoverAsync(server, phone);

        using var connected = new Phone(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port));
        Assert.Equal(0, await connected.HelloAsync());
    }

    [Fact]
    public async Task A_name_longer_than_the_limit_is_cut_at_a_character_boundary()
    {
        var longName = string.Concat(Enumerable.Repeat("पीसी", 40));   // multi-byte UTF-8
        await using var server = new TestServer(name: longName);
        using var phone = server.NewPhone();

        var (name, _, _) = await DiscoverAsync(server, phone);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(name) <= DiscoveryMessage.MaxNameBytes);
        Assert.StartsWith(name, longName);
    }

    [Fact]
    public async Task Anything_other_than_discover_on_the_discovery_port_gets_no_reply()
    {
        await using var server = new TestServer();
        using var phone = server.NewPhone();

        var hello = new byte[Protocol.SessionMessageSize];
        SessionMessage.Write(hello, MessageType.Hello, Protocol.NoPad);
        await phone.SendRawAsync(hello, server.DiscoveryEndPoint);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => phone.ReceiveAsync(timeoutMs: 300));
        Assert.Equal(0, server.Host.Sessions.SessionCount);
    }
}
