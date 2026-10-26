using DroidOSS.Core;

namespace DroidOSS.IntegrationTests;

// The whole server over real UDP sockets on loopback: HELLO/WELCOME, input, BYE, slots, timeouts, junk, shutdown.
public class ServerOverUdpTests
{
    private static PadState Pressed(GamepadButtons buttons, short lx = 0) =>
        new() { Buttons = (ushort)buttons, ThumbLX = lx };

    [Fact]
    public async Task Hello_is_answered_with_a_welcome_and_plugs_in_a_pad()
    {
        await using var server = new TestServer();
        using var phone = server.NewPhone();

        var pad = await phone.HelloAsync();

        Assert.Equal(0, pad);
        await TestServer.Eventually(() => server.Backend.Count("connect", 0) == 1, "pad 0 is plugged in");
        Assert.Equal(1, server.Host.Sessions.SessionCount);
    }

    [Fact]
    public async Task Input_after_welcome_reaches_the_pad_unchanged()
    {
        await using var server = new TestServer();
        using var phone = server.NewPhone();
        var pad = await phone.HelloAsync();

        var state = new PadState
        {
            Buttons = (ushort)(GamepadButtons.A | GamepadButtons.DPadUp),
            LeftTrigger = 255,
            ThumbLX = -32768,
            ThumbLY = 32767,
            ThumbRX = 1234,
            ThumbRY = -1,
        };
        await phone.SendInputAsync(pad, state);

        await TestServer.Eventually(() => server.Backend.SubmissionsTo(pad).Contains(state), "the state reaches the pad");
    }

    [Fact]
    public async Task A_stream_of_input_arrives_in_order()
    {
        await using var server = new TestServer();
        using var phone = server.NewPhone();
        var pad = await phone.HelloAsync();

        for (short i = 1; i <= 50; i++)
            await phone.SendInputAsync(pad, Pressed(GamepadButtons.None, lx: i));

        await TestServer.Eventually(
            () => server.Backend.SubmissionsTo(pad).Any(s => s.ThumbLX == 50), "the last packet arrives");

        var xs = server.Backend.SubmissionsTo(pad).Select(s => s.ThumbLX).Where(x => x > 0).ToList();
        Assert.Equal(xs.OrderBy(x => x), xs);
    }

    [Fact]
    public async Task Late_packets_with_an_old_sequence_number_are_dropped()
    {
        await using var server = new TestServer();
        using var phone = server.NewPhone();
        var pad = await phone.HelloAsync();

        await phone.SendInputAsync(pad, Pressed(GamepadButtons.A), sequence: 100);
        await phone.SendInputAsync(pad, Pressed(GamepadButtons.B), sequence: 99);
        await phone.SendInputAsync(pad, Pressed(GamepadButtons.X), sequence: 101);

        await TestServer.Eventually(
            () => server.Backend.SubmissionsTo(pad).Any(s => s.IsPressed(GamepadButtons.X)), "packet 101 arrives");
        Assert.DoesNotContain(server.Backend.SubmissionsTo(pad), s => s.IsPressed(GamepadButtons.B));
        Assert.Equal(1, server.Host.Sessions.Stale);
    }

    [Fact]
    public async Task Bye_zeroes_the_pad_before_unplugging_it()
    {
        await using var server = new TestServer();
        using var phone = server.NewPhone();
        var pad = await phone.HelloAsync();
        await phone.SendInputAsync(pad, Pressed(GamepadButtons.A, lx: 30000));
        await TestServer.Eventually(() => server.Backend.SubmissionsTo(pad).Count > 0, "input arrives");

        await phone.ByeAsync(pad);

        await TestServer.Eventually(() => server.Backend.Count("disconnect", pad) == 1, "the pad is unplugged");
        var log = server.Backend.Log().Where(e => e.Slot == pad).ToList();
        Assert.Equal("disconnect", log[^1].Call);
        Assert.Equal(("submit", PadState.Neutral), (log[^2].Call, log[^2].State));
        Assert.Equal(0, server.Host.Sessions.SessionCount);
    }

    [Fact]
    public async Task Four_phones_get_four_pads_and_a_fifth_is_told_the_server_is_full()
    {
        await using var server = new TestServer();
        var phones = Enumerable.Range(0, 5).Select(_ => server.NewPhone()).ToList();
        try
        {
            var pads = new List<byte>();
            foreach (var phone in phones.Take(4)) pads.Add(await phone.HelloAsync());

            Assert.Equal(new byte[] { 0, 1, 2, 3 }, pads.Order().ToArray());
            Assert.Equal(Protocol.NoPad, await phones[4].HelloAsync());
            Assert.Equal(1, server.Host.Sessions.Rejected);
        }
        finally
        {
            phones.ForEach(p => p.Dispose());
        }
    }

    [Fact]
    public async Task Each_phone_drives_only_its_own_pad()
    {
        await using var server = new TestServer();
        using var first = server.NewPhone();
        using var second = server.NewPhone();
        var a = await first.HelloAsync();
        var b = await second.HelloAsync();

        await first.SendInputAsync(a, Pressed(GamepadButtons.A));
        await second.SendInputAsync(b, Pressed(GamepadButtons.B));

        await TestServer.Eventually(
            () => server.Backend.SubmissionsTo(a).Count > 0 && server.Backend.SubmissionsTo(b).Count > 0, "both pads get input");
        Assert.All(server.Backend.SubmissionsTo(a), s => Assert.False(s.IsPressed(GamepadButtons.B)));
        Assert.All(server.Backend.SubmissionsTo(b), s => Assert.False(s.IsPressed(GamepadButtons.A)));
    }

    [Fact]
    public async Task A_freed_slot_is_given_to_the_next_phone()
    {
        await using var server = new TestServer();
        using var first = server.NewPhone();
        using var second = server.NewPhone();

        var pad = await first.HelloAsync();
        await first.ByeAsync(pad);
        await TestServer.Eventually(() => server.Host.Sessions.SessionCount == 0, "the first phone has left");

        Assert.Equal(pad, await second.HelloAsync());
    }

    [Fact]
    public async Task A_phone_that_goes_silent_is_dropped_after_the_timeout()
    {
        await using var server = new TestServer(sessionTimeout: TimeSpan.FromMilliseconds(300));
        using var phone = server.NewPhone();
        var pad = await phone.HelloAsync();

        // Say nothing at all, as a phone with a dead battery would.
        await TestServer.Eventually(() => server.Backend.Count("disconnect", pad) == 1, "the silent pad is unplugged");
        Assert.Equal(0, server.Host.Sessions.SessionCount);
    }

    [Fact]
    public async Task Input_from_a_phone_that_never_said_hello_is_ignored()
    {
        await using var server = new TestServer();
        using var stranger = server.NewPhone();

        await stranger.SendInputAsync(0, Pressed(GamepadButtons.A));

        await TestServer.Eventually(() => server.Host.Sessions.UnknownSender == 1, "the packet is counted as unknown");
        Assert.Empty(server.Backend.Log());
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0xDA })]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x00 })]                 // wrong magic
    [InlineData(new byte[] { 0xDA, 0x09, 0x02, 0xFF })]                 // unknown version
    [InlineData(new byte[] { 0xDA, 0x01, 0x01, 0x00, 1, 2, 3, 4, 5 })]  // truncated INPUT
    public async Task Junk_datagrams_are_rejected_and_the_server_keeps_working(byte[] junk)
    {
        await using var server = new TestServer();
        using var phone = server.NewPhone();

        await phone.SendRawAsync(junk);
        await phone.SendRawAsync(new byte[64]);

        // Still alive: a proper HELLO afterwards is answered as usual.
        Assert.Equal(0, await phone.HelloAsync());
        Assert.True(server.Host.Sessions.Malformed >= 1);
    }

    [Fact]
    public async Task Stopping_the_server_zeroes_and_unplugs_every_pad()
    {
        await using var server = new TestServer();
        using var first = server.NewPhone();
        using var second = server.NewPhone();
        var a = await first.HelloAsync();
        var b = await second.HelloAsync();
        await first.SendInputAsync(a, Pressed(GamepadButtons.Start));
        await TestServer.Eventually(() => server.Backend.SubmissionsTo(a).Count > 0, "input arrives");

        await server.StopAsync();

        Assert.Equal(1, server.Backend.Count("disconnect", a));
        Assert.Equal(1, server.Backend.Count("disconnect", b));
        Assert.Equal(PadState.Neutral, server.Backend.SubmissionsTo(a)[^1]);
    }
}
