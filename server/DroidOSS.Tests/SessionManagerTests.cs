using DroidOSS.Core;
using Xunit;

namespace DroidOSS.Tests;

public class SessionManagerTests
{
    // 192.168.1.42 and friends, each on its own ephemeral port. Four phones all
    // send to the same server port; the source address is the only thing that
    // tells them apart, which is precisely what the session table keys on.
    private static readonly ClientKey PhoneA = new(0xC0A8012A, 51203);
    private static readonly ClientKey PhoneB = new(0xC0A8012B, 49812);
    private static readonly ClientKey PhoneC = new(0xC0A8012C, 60001);
    private static readonly ClientKey PhoneD = new(0xC0A8012D, 60002);
    private static readonly ClientKey PhoneE = new(0xC0A8012E, 60003);

    private static PadState SampleState => new()
    {
        Buttons = (ushort)(GamepadButtons.A | GamepadButtons.RightShoulder),
        LeftTrigger = 40,
        RightTrigger = 210,
        ThumbLX = -12345,
        ThumbLY = 23456,
        ThumbRX = 999,
        ThumbRY = -999,
    };

    /// <summary>Wraps the manager so a test reads as a conversation, not as buffer plumbing.</summary>
    private sealed class Fixture
    {
        public FakePadBackend Backend { get; } = new();
        public ManualClock Clock { get; } = new();
        public SessionManager Manager { get; }

        private readonly byte[] _reply = new byte[Protocol.SessionMessageSize];

        public Fixture(TimeSpan? timeout = null) =>
            Manager = new SessionManager(Backend, Clock, timeout);

        public MessageOutcome Outcome { get; private set; }

        /// <summary>The pad byte of the last reply, or null if there was no reply.</summary>
        public byte? ReplyPad { get; private set; }

        public MessageType? ReplyType { get; private set; }

        private MessageOutcome Send(ReadOnlySpan<byte> data, ClientKey from)
        {
            Array.Clear(_reply);
            Outcome = Manager.Handle(data, from, _reply, out var replyLength);

            if (replyLength == 0)
            {
                ReplyType = null;
                ReplyPad = null;
            }
            else
            {
                Assert.True(SessionMessage.TryRead(
                    _reply.AsSpan(0, replyLength), out var type, out var pad));
                ReplyType = type;
                ReplyPad = pad;
            }

            return Outcome;
        }

        public MessageOutcome Hello(ClientKey from)
        {
            Span<byte> buffer = stackalloc byte[Protocol.SessionMessageSize];
            SessionMessage.Write(buffer, MessageType.Hello, Protocol.NoPad);
            return Send(buffer, from);
        }

        public MessageOutcome Bye(ClientKey from, byte pad = 0)
        {
            Span<byte> buffer = stackalloc byte[Protocol.SessionMessageSize];
            SessionMessage.Write(buffer, MessageType.Bye, pad);
            return Send(buffer, from);
        }

        public MessageOutcome Input(ClientKey from, uint sequence, PadState state = default)
        {
            Span<byte> buffer = stackalloc byte[Protocol.InputPacketSize];
            InputPacket.Write(buffer, 0, sequence, in state);
            return Send(buffer, from);
        }

        public MessageOutcome Raw(ReadOnlySpan<byte> data, ClientKey from) => Send(data, from);

        /// <summary>Completes a handshake and returns the slot that was granted.</summary>
        public int Connect(ClientKey from)
        {
            Assert.Equal(MessageOutcome.SessionOpened, Hello(from));
            Assert.NotNull(ReplyPad);
            return ReplyPad!.Value;
        }
    }

    // ── the handshake ────────────────────────────────────────────────────────

    [Fact]
    public void Hello_is_answered_with_a_welcome_naming_the_assigned_slot()
    {
        var f = new Fixture();

        Assert.Equal(MessageOutcome.SessionOpened, f.Hello(PhoneA));

        Assert.Equal(MessageType.Welcome, f.ReplyType);
        Assert.Equal((byte)0, f.ReplyPad);
        Assert.Equal([0], f.Backend.Connected);
        Assert.Equal(1, f.Manager.SessionCount);
    }

    /// <summary>
    /// Nothing is plugged in until a phone asks. Before B3 a pad appeared at
    /// startup whether or not anyone was there to drive it.
    /// </summary>
    [Fact]
    public void No_pad_exists_before_anyone_connects()
    {
        var f = new Fixture();

        Assert.Empty(f.Backend.Connected);
        Assert.Equal(0, f.Manager.SessionCount);
    }

    [Fact]
    public void Four_phones_get_the_four_slots()
    {
        var f = new Fixture();

        Assert.Equal(0, f.Connect(PhoneA));
        Assert.Equal(1, f.Connect(PhoneB));
        Assert.Equal(2, f.Connect(PhoneC));
        Assert.Equal(3, f.Connect(PhoneD));

        Assert.Equal([0, 1, 2, 3], f.Backend.Connected);
        Assert.Equal(4, f.Manager.SessionCount);
    }

    /// <summary>
    /// XInput supports exactly four pads. A fifth phone must be told so rather
    /// than left retrying into silence.
    /// </summary>
    [Fact]
    public void A_fifth_phone_is_refused_and_no_fifth_pad_appears()
    {
        var f = new Fixture();
        foreach (var phone in new[] { PhoneA, PhoneB, PhoneC, PhoneD }) f.Connect(phone);

        Assert.Equal(MessageOutcome.SessionRejected, f.Hello(PhoneE));

        Assert.Equal(MessageType.Welcome, f.ReplyType);
        Assert.Equal(Protocol.NoPad, f.ReplyPad);
        Assert.Equal(4, f.Backend.Connected.Count);
        Assert.Equal(4, f.Manager.SessionCount);
        Assert.Equal(1, f.Manager.Rejected);
    }

    /// <summary>
    /// A repeated HELLO is the normal result of a lost WELCOME. If each retry
    /// consumed a slot, a phone retrying every 200 ms would exhaust the server
    /// in under a second.
    /// </summary>
    [Fact]
    public void A_repeated_hello_returns_the_same_slot_and_does_not_consume_another()
    {
        var f = new Fixture();
        var slot = f.Connect(PhoneA);

        f.Hello(PhoneA);

        Assert.Equal(MessageType.Welcome, f.ReplyType);
        Assert.Equal((byte)slot, f.ReplyPad);
        Assert.Single(f.Backend.Connected);
        Assert.Equal(1, f.Manager.SessionCount);
    }

    [Fact]
    public void A_slot_freed_by_bye_is_handed_to_the_next_phone()
    {
        var f = new Fixture();
        f.Connect(PhoneA);
        var second = f.Connect(PhoneB);
        f.Connect(PhoneC);

        Assert.Equal(MessageOutcome.SessionClosed, f.Bye(PhoneB));
        Assert.Equal(second, f.Connect(PhoneD));
    }

    // ── input routing ────────────────────────────────────────────────────────

    [Fact]
    public void Input_reaches_the_pad_with_every_field_intact()
    {
        var f = new Fixture();
        f.Connect(PhoneA);
        var expected = SampleState;

        Assert.Equal(MessageOutcome.InputApplied, f.Input(PhoneA, 1, expected));

        var actual = f.Backend.LastSubmitted;
        Assert.Equal(expected.Buttons, actual.Buttons);
        Assert.Equal(expected.LeftTrigger, actual.LeftTrigger);
        Assert.Equal(expected.RightTrigger, actual.RightTrigger);
        Assert.Equal(expected.ThumbLX, actual.ThumbLX);
        Assert.Equal(expected.ThumbLY, actual.ThumbLY);
        Assert.Equal(expected.ThumbRX, actual.ThumbRX);
        Assert.Equal(expected.ThumbRY, actual.ThumbRY);
    }

    /// <summary>
    /// The isolation guarantee. Two phones sharing one server port must drive
    /// their own pads and nothing else.
    /// </summary>
    [Fact]
    public void Each_phone_drives_only_its_own_slot()
    {
        var f = new Fixture();
        var a = f.Connect(PhoneA);
        var b = f.Connect(PhoneB);

        f.Input(PhoneA, 1, new PadState { ThumbLX = 111 });
        f.Input(PhoneB, 1, new PadState { ThumbLX = 222 });

        Assert.Equal(111, f.Backend.SubmissionsTo(a)[^1].ThumbLX);
        Assert.Equal(222, f.Backend.SubmissionsTo(b)[^1].ThumbLX);
    }

    /// <summary>
    /// The bug that per-client sequence gating exists to prevent. A phone
    /// joining late starts counting at 1, and a single shared gate — already at
    /// 40,000 from the first phone — would reject every packet it ever sends.
    /// The symptom is a controller that connects successfully and then does
    /// absolutely nothing.
    /// </summary>
    [Fact]
    public void Sequence_numbers_are_tracked_per_phone_not_globally()
    {
        var f = new Fixture();
        f.Connect(PhoneA);

        for (uint seq = 1; seq <= 40_000; seq += 1000) f.Input(PhoneA, seq);

        var b = f.Connect(PhoneB);

        Assert.Equal(MessageOutcome.InputApplied, f.Input(PhoneB, 1, new PadState { ThumbLX = 777 }));
        Assert.Equal(777, f.Backend.SubmissionsTo(b)[^1].ThumbLX);
    }

    /// <summary>
    /// A reused slot must forget the previous occupant's counter, or the new
    /// phone's low sequence numbers look stale.
    /// </summary>
    [Fact]
    public void A_reused_slot_forgets_the_previous_phones_sequence_numbers()
    {
        var f = new Fixture();
        f.Connect(PhoneA);
        f.Input(PhoneA, 50_000);
        f.Bye(PhoneA);

        var slot = f.Connect(PhoneB);

        Assert.Equal(MessageOutcome.InputApplied, f.Input(PhoneB, 1, new PadState { ThumbLX = 555 }));
        Assert.Equal(555, f.Backend.SubmissionsTo(slot)[^1].ThumbLX);
    }

    [Fact]
    public void Out_of_order_input_is_dropped_without_blocking_what_follows()
    {
        var f = new Fixture();
        f.Connect(PhoneA);

        Assert.Equal(MessageOutcome.InputApplied, f.Input(PhoneA, 57));
        Assert.Equal(MessageOutcome.InputApplied, f.Input(PhoneA, 59));
        Assert.Equal(MessageOutcome.Stale, f.Input(PhoneA, 58));
        Assert.Equal(MessageOutcome.InputApplied, f.Input(PhoneA, 60));

        Assert.Equal(3, f.Manager.Applied);
        Assert.Equal(1, f.Manager.Stale);
    }

    // ── the unknown sender rule ──────────────────────────────────────────────

    /// <summary>
    /// What a lost HELLO looks like from the server's side. Dropping rather than
    /// adopting means a stray datagram can never consume one of four slots.
    /// </summary>
    [Fact]
    public void Input_from_a_phone_that_never_said_hello_is_dropped_and_counted()
    {
        var f = new Fixture();

        Assert.Equal(MessageOutcome.UnknownSender, f.Input(PhoneA, 1, SampleState));

        Assert.Empty(f.Backend.Submissions);
        Assert.Empty(f.Backend.Connected);
        Assert.Equal(1, f.Manager.UnknownSender);
        Assert.Equal(0, f.Manager.Applied);
    }

    [Fact]
    public void A_stranger_cannot_close_someone_elses_session()
    {
        var f = new Fixture();
        f.Connect(PhoneA);

        Assert.Equal(MessageOutcome.Ignored, f.Bye(PhoneB));

        Assert.Empty(f.Backend.Disconnected);
        Assert.Equal(1, f.Manager.SessionCount);
    }

    [Fact]
    public void Garbage_never_reaches_a_pad()
    {
        var f = new Fixture();
        f.Connect(PhoneA);
        f.Backend.Submissions.Clear();

        Assert.Equal(MessageOutcome.Malformed, f.Raw(new byte[Protocol.InputPacketSize], PhoneA));
        Assert.Equal(MessageOutcome.Malformed, f.Raw("hello"u8, PhoneA));
        Assert.Equal(MessageOutcome.Malformed, f.Raw([], PhoneA));

        Assert.Empty(f.Backend.Submissions);
        Assert.Equal(3, f.Manager.Malformed);
    }

    [Fact]
    public void Rejects_input_carrying_the_wrong_magic_byte()
    {
        var f = new Fixture();
        f.Connect(PhoneA);

        var bytes = new byte[Protocol.InputPacketSize];
        InputPacket.Write(bytes, 0, 1, SampleState);
        bytes[Protocol.Offset.Magic] = 0x00;

        Assert.Equal(MessageOutcome.Malformed, f.Raw(bytes, PhoneA));
    }

    [Fact]
    public void Rejects_input_from_a_future_protocol_version()
    {
        var f = new Fixture();
        f.Connect(PhoneA);

        var bytes = new byte[Protocol.InputPacketSize];
        InputPacket.Write(bytes, 0, 1, SampleState);
        bytes[Protocol.Offset.Version] = Protocol.Version + 1;

        Assert.Equal(MessageOutcome.Malformed, f.Raw(bytes, PhoneA));
    }

    // ── the timeout ──────────────────────────────────────────────────────────

    [Fact]
    public void A_phone_that_keeps_talking_is_not_timed_out()
    {
        var f = new Fixture();
        f.Connect(PhoneA);

        for (uint seq = 1; seq <= 10; seq++)
        {
            f.Clock.Advance(0.5);
            f.Input(PhoneA, seq);
            Assert.Equal(0, f.Manager.SweepTimeouts());
        }

        Assert.Equal(1, f.Manager.SessionCount);
    }

    [Fact]
    public void Silence_just_short_of_the_timeout_keeps_the_session()
    {
        var f = new Fixture();
        f.Connect(PhoneA);
        f.Input(PhoneA, 1);

        f.Clock.Advance(1.9);

        Assert.Equal(0, f.Manager.SweepTimeouts());
        Assert.Equal(1, f.Manager.SessionCount);
        Assert.Empty(f.Backend.Disconnected);
    }

    [Fact]
    public void Silence_past_the_timeout_closes_the_session()
    {
        var f = new Fixture();
        f.Connect(PhoneA);
        f.Input(PhoneA, 1);

        f.Clock.Advance(2.1);

        Assert.Equal(1, f.Manager.SweepTimeouts());
        Assert.Equal(0, f.Manager.SessionCount);
        Assert.Equal([0], f.Backend.Disconnected);
    }

    /// <summary>
    /// A duplicate is useless as input but still proves the phone is alive.
    /// Timing out a phone that is demonstrably still sending would be perverse.
    /// </summary>
    [Fact]
    public void A_stale_packet_still_counts_as_a_sign_of_life()
    {
        var f = new Fixture();
        f.Connect(PhoneA);
        f.Input(PhoneA, 10);

        f.Clock.Advance(1.5);
        Assert.Equal(MessageOutcome.Stale, f.Input(PhoneA, 10));

        f.Clock.Advance(1.5);
        Assert.Equal(0, f.Manager.SweepTimeouts());
        Assert.Equal(1, f.Manager.SessionCount);
    }

    [Fact]
    public void One_phone_timing_out_leaves_the_others_alone()
    {
        var f = new Fixture();
        var a = f.Connect(PhoneA);
        var b = f.Connect(PhoneB);
        var c = f.Connect(PhoneC);

        f.Input(PhoneA, 1);
        f.Input(PhoneB, 1);
        f.Input(PhoneC, 1);

        // B goes quiet; A and C keep sending.
        f.Clock.Advance(1.5);
        f.Input(PhoneA, 2);
        f.Input(PhoneC, 2);
        f.Clock.Advance(1.0);

        Assert.Equal(1, f.Manager.SweepTimeouts());
        Assert.Equal([b], f.Backend.Disconnected);
        Assert.Equal(2, f.Manager.SessionCount);

        Assert.Equal(MessageOutcome.InputApplied, f.Input(PhoneA, 3));
        Assert.Equal(MessageOutcome.InputApplied, f.Input(PhoneC, 3));
        Assert.Contains(a, f.Backend.Connected);
        Assert.Contains(c, f.Backend.Connected);
    }

    /// <summary>
    /// After a timeout the phone is a stranger again, so its input is dropped
    /// until it completes a fresh handshake. Verifies the two rules compose.
    /// </summary>
    [Fact]
    public void A_timed_out_phone_must_say_hello_again()
    {
        var f = new Fixture();
        f.Connect(PhoneA);
        f.Input(PhoneA, 1);

        f.Clock.Advance(2.1);
        f.Manager.SweepTimeouts();

        Assert.Equal(MessageOutcome.UnknownSender, f.Input(PhoneA, 2));
        Assert.Equal(MessageOutcome.SessionOpened, f.Hello(PhoneA));
        Assert.Equal(MessageOutcome.InputApplied, f.Input(PhoneA, 3));
    }

    [Fact]
    public void Sweeping_an_empty_server_does_nothing()
    {
        var f = new Fixture();
        f.Clock.Advance(60);

        Assert.Equal(0, f.Manager.SweepTimeouts());
    }

    // ── closing down ─────────────────────────────────────────────────────────

    /// <summary>
    /// <b>The most important assertion in this block.</b> Some games latch the
    /// last state they saw when a controller disappears. Unplugging a pad while
    /// a stick is held therefore leaves the input stuck exactly as if nothing
    /// had been cleaned up — the character keeps running into a wall. Zeroing
    /// must come first, on every path out of a session.
    /// </summary>
    [Theory]
    [InlineData(SessionCloseReason.Bye)]
    [InlineData(SessionCloseReason.Timeout)]
    [InlineData(SessionCloseReason.ServerShutdown)]
    public void A_pad_is_always_zeroed_before_it_is_unplugged(SessionCloseReason reason)
    {
        var f = new Fixture();
        var slot = f.Connect(PhoneA);

        // Hard left with a button held — the state that must not survive.
        f.Input(PhoneA, 1, new PadState
        {
            ThumbLX = short.MinValue,
            Buttons = (ushort)GamepadButtons.A,
        });

        switch (reason)
        {
            case SessionCloseReason.Bye:
                f.Bye(PhoneA);
                break;
            case SessionCloseReason.Timeout:
                f.Clock.Advance(2.1);
                f.Manager.SweepTimeouts();
                break;
            case SessionCloseReason.ServerShutdown:
                f.Manager.CloseAll();
                break;
        }

        var disconnectAt = f.Backend.Log.FindIndex(
            e => e.Call == PadCall.Disconnect && e.Slot == slot);
        Assert.True(disconnectAt > 0, "the pad was never unplugged");

        var previous = f.Backend.Log[disconnectAt - 1];
        Assert.Equal(PadCall.Submit, previous.Call);
        Assert.Equal(slot, previous.Slot);
        Assert.Equal(0, previous.State.ThumbLX);
        Assert.Equal(0, previous.State.Buttons);
    }

    [Fact]
    public void Close_all_clears_every_session()
    {
        var f = new Fixture();
        f.Connect(PhoneA);
        f.Connect(PhoneB);
        f.Connect(PhoneC);

        f.Manager.CloseAll();

        Assert.Equal(0, f.Manager.SessionCount);
        Assert.Equal(3, f.Backend.Disconnected.Count);
    }

    [Fact]
    public void Bye_zeroes_and_unplugs_just_that_phone()
    {
        var f = new Fixture();
        var a = f.Connect(PhoneA);
        f.Connect(PhoneB);

        Assert.Equal(MessageOutcome.SessionClosed, f.Bye(PhoneA, (byte)a));

        Assert.Equal([a], f.Backend.Disconnected);
        Assert.Equal(1, f.Manager.SessionCount);
    }

    // ── events and reporting ─────────────────────────────────────────────────

    [Fact]
    public void Opening_and_closing_raise_events_carrying_the_reason()
    {
        var f = new Fixture();
        var opened = new List<int>();
        var closed = new List<(int Slot, SessionCloseReason? Reason)>();

        f.Manager.SessionOpened += (_, e) => opened.Add(e.Slot);
        f.Manager.SessionClosed += (_, e) => closed.Add((e.Slot, e.Reason));

        var slot = f.Connect(PhoneA);
        f.Input(PhoneA, 1);
        f.Clock.Advance(2.1);
        f.Manager.SweepTimeouts();

        Assert.Equal([slot], opened);
        Assert.Equal([(slot, SessionCloseReason.Timeout)], closed);
    }

    [Fact]
    public void A_snapshot_reports_each_phone_with_its_slot_and_last_state()
    {
        var f = new Fixture();
        var a = f.Connect(PhoneA);
        var b = f.Connect(PhoneB);

        f.Input(PhoneA, 1, new PadState { ThumbLX = 111 });
        f.Input(PhoneB, 1, new PadState { ThumbLX = 222 });
        f.Input(PhoneB, 2, new PadState { ThumbLX = 333 });

        var snapshot = f.Manager.Snapshot();

        Assert.Equal(2, snapshot.Count);

        var first = snapshot.Single(s => s.Slot == a);
        Assert.Equal(PhoneA, first.Client);
        Assert.Equal(111, first.LastState.ThumbLX);
        Assert.Equal(1, first.Applied);

        var second = snapshot.Single(s => s.Slot == b);
        Assert.Equal(333, second.LastState.ThumbLX);
        Assert.Equal(2, second.Applied);
    }

    [Fact]
    public void Counters_add_up_across_a_mixed_stream()
    {
        var f = new Fixture();
        f.Connect(PhoneA);

        f.Input(PhoneA, 1);                                  // applied
        f.Input(PhoneA, 2);                                  // applied
        f.Input(PhoneA, 1);                                  // stale
        f.Raw(new byte[Protocol.InputPacketSize], PhoneA);   // malformed
        f.Input(PhoneA, 3);                                  // applied
        f.Raw("junk"u8, PhoneA);                             // malformed
        f.Input(PhoneB, 1);                                  // unknown sender

        Assert.Equal(3, f.Manager.Applied);
        Assert.Equal(1, f.Manager.Stale);
        Assert.Equal(2, f.Manager.Malformed);
        Assert.Equal(1, f.Manager.UnknownSender);
    }

    /// <summary>A dotted-quad in a status line beats a bare integer.</summary>
    [Fact]
    public void A_client_key_prints_as_an_address_and_port()
    {
        Assert.Equal("192.168.1.42:51203", PhoneA.ToString());
    }
}
