#!/usr/bin/env python3
"""A phone that isn't there.

Sends droidOSS INPUT packets over UDP so the Windows server can be built and
debugged with no Android device in existence. This is not scaffolding to delete
later -- it stays as the permanent regression harness, and it is the only way to
test the server in isolation once the real app exists.

It is also a second, independent implementation of the wire format. The server
speaks it in C# and the phone will speak it in Kotlin; having a third
implementation that agrees byte-for-byte is what proves docs/PROTOCOL.md is
unambiguous rather than merely written down.

Usage:
    py tools/fake_phone.py --selftest              # print the golden packets, exit
    py tools/fake_phone.py --host 192.168.1.12     # connect, then stream at 125 Hz
    py tools/fake_phone.py --listen                # decode an incoming stream
    py tools/fake_phone.py --no-handshake          # send input without connecting

Run several at once to test multiple pads -- each gets its own slot.

No dependencies beyond the standard library.
"""

from __future__ import annotations

import argparse
import math
import socket
import struct
import sys
import time

# ---------------------------------------------------------------------------
# Protocol -- transcribed from docs/PROTOCOL.md
#
# Keep in step with server/DroidOSS.Core/Protocol.cs. A disagreement between the
# two produces no error at all, just a controller that behaves strangely.
# ---------------------------------------------------------------------------

MAGIC_BYTE = 0xDA
VERSION = 1

INPUT_PORT = 27500
DISCOVERY_PORT = 27501

INPUT_PACKET_SIZE = 20
SESSION_MESSAGE_SIZE = 4

# The pad byte when it names no particular slot: "any slot, you choose" in a
# HELLO, and "no slot for you" in the WELCOME that answers one.
NO_PAD = 0xFF

# How long to wait for a WELCOME before sending HELLO again. UDP does not
# guarantee delivery, so a HELLO can simply vanish -- the client is the side that
# retries. This also covers the ordinary case of starting this before the server.
HELLO_RETRY_SECONDS = 0.2

# How long the server tolerates silence before it zeroes and unplugs the pad.
SESSION_TIMEOUT_SECONDS = 2.0

MSG_INPUT = 0x01
MSG_HELLO = 0x02
MSG_WELCOME = 0x03
MSG_BYE = 0x04
MSG_RUMBLE = 0x05
MSG_DISCOVER = 0x06

# magic, version, type, pad | sequence | buttons, LT, RT | LX, LY, RX, RY
#
# The leading '<' is load-bearing: it selects little-endian AND standard sizes
# with no padding. Without it Python uses native alignment and the layout shifts.
INPUT_FORMAT = "<BBBBIHBBhhhh"

# HELLO, WELCOME and BYE are the header and nothing else: magic, version, type,
# pad. They carry no sequence number -- ordering only matters for the 125 Hz
# stream, where a late packet would jerk the stick backwards.
SESSION_FORMAT = "<BBBB"

assert struct.calcsize(INPUT_FORMAT) == INPUT_PACKET_SIZE
assert struct.calcsize(SESSION_FORMAT) == SESSION_MESSAGE_SIZE

# Buttons, one bit each.
BTN_DPAD_UP = 0x0001
BTN_DPAD_DOWN = 0x0002
BTN_DPAD_LEFT = 0x0004
BTN_DPAD_RIGHT = 0x0008
BTN_START = 0x0010
BTN_BACK = 0x0020
BTN_LEFT_THUMB = 0x0040
BTN_RIGHT_THUMB = 0x0080
BTN_LEFT_SHOULDER = 0x0100
BTN_RIGHT_SHOULDER = 0x0200
BTN_GUIDE = 0x0400
BTN_A = 0x1000
BTN_B = 0x2000
BTN_X = 0x4000
BTN_Y = 0x8000


def encode_input(
    pad: int,
    sequence: int,
    buttons: int = 0,
    left_trigger: int = 0,
    right_trigger: int = 0,
    thumb_lx: int = 0,
    thumb_ly: int = 0,
    thumb_rx: int = 0,
    thumb_ry: int = 0,
) -> bytes:
    """Build one 20-byte INPUT packet."""
    return struct.pack(
        INPUT_FORMAT,
        MAGIC_BYTE,
        VERSION,
        MSG_INPUT,
        pad,
        sequence & 0xFFFFFFFF,  # wrap rather than overflow, as the server expects
        buttons,
        left_trigger,
        right_trigger,
        thumb_lx,
        thumb_ly,
        thumb_rx,
        thumb_ry,
    )


def decode_input(data: bytes) -> dict | None:
    """Parse an INPUT packet, or return None if it isn't one.

    Validation happens before any field is trusted. A UDP socket receives port
    scans and other applications' strays; without the magic byte that garbage
    would be read as controller state.
    """
    if len(data) != INPUT_PACKET_SIZE:
        return None

    (magic, version, msg_type, pad, sequence, buttons,
     lt, rt, lx, ly, rx, ry) = struct.unpack(INPUT_FORMAT, data)

    if magic != MAGIC_BYTE or version != VERSION or msg_type != MSG_INPUT:
        return None

    return {
        "pad": pad,
        "sequence": sequence,
        "buttons": buttons,
        "left_trigger": lt,
        "right_trigger": rt,
        "thumb_lx": lx,
        "thumb_ly": ly,
        "thumb_rx": rx,
        "thumb_ry": ry,
    }


def encode_session(msg_type: int, pad: int) -> bytes:
    """Build one 4-byte HELLO, WELCOME or BYE."""
    return struct.pack(SESSION_FORMAT, MAGIC_BYTE, VERSION, msg_type, pad)


def decode_session(data: bytes) -> dict | None:
    """Parse a session message, or return None if it isn't one.

    An INPUT packet is correctly rejected here: the two are told apart by length
    alone, which is why nothing else may ever be exactly four bytes.
    """
    if len(data) != SESSION_MESSAGE_SIZE:
        return None

    magic, version, msg_type, pad = struct.unpack(SESSION_FORMAT, data)

    if magic != MAGIC_BYTE or version != VERSION:
        return None
    if msg_type not in (MSG_HELLO, MSG_WELCOME, MSG_BYE):
        return None

    return {"type": msg_type, "pad": pad}


# ---------------------------------------------------------------------------
# The golden vector
#
# Every field carries a distinctive value, so a transposition or an endianness
# mistake cannot hide. server/DroidOSS.Tests/InputPacketTests.cs asserts this
# exact byte sequence from the C# side.
# ---------------------------------------------------------------------------

GOLDEN_PAD = 2
GOLDEN_SEQUENCE = 0x12345678
GOLDEN_BUTTONS = BTN_A | BTN_DPAD_LEFT  # 0x1004
GOLDEN_LEFT_TRIGGER = 0x20  # 32
GOLDEN_RIGHT_TRIGGER = 0xC8  # 200
GOLDEN_THUMB_LX = 1000
GOLDEN_THUMB_LY = -1000
GOLDEN_THUMB_RX = 32767
GOLDEN_THUMB_RY = -32768

GOLDEN_EXPECTED_HEX = "da010102785634120410 20c8e80318fcff7f0080".replace(" ", "")

# The session messages, in the same spirit. These match the golden arrays in
# server/DroidOSS.Tests/SessionMessageTests.cs byte for byte.
GOLDEN_SESSION = {
    "HELLO":         (MSG_HELLO,   NO_PAD, "da0102ff"),
    "WELCOME pad 1": (MSG_WELCOME, 1,      "da010301"),
    "WELCOME full":  (MSG_WELCOME, NO_PAD, "da0103ff"),
    "BYE pad 1":     (MSG_BYE,     1,      "da010401"),
}


def golden_packet() -> bytes:
    return encode_input(
        pad=GOLDEN_PAD,
        sequence=GOLDEN_SEQUENCE,
        buttons=GOLDEN_BUTTONS,
        left_trigger=GOLDEN_LEFT_TRIGGER,
        right_trigger=GOLDEN_RIGHT_TRIGGER,
        thumb_lx=GOLDEN_THUMB_LX,
        thumb_ly=GOLDEN_THUMB_LY,
        thumb_rx=GOLDEN_THUMB_RX,
        thumb_ry=GOLDEN_THUMB_RY,
    )


def run_selftest() -> int:
    """Print the golden packet and check it round-trips."""
    packet = golden_packet()
    actual = packet.hex()

    print("droidOSS protocol self-test")
    print()
    print("  golden packet, 20 bytes:")
    print(f"    {' '.join(f'{b:02X}' for b in packet)}")
    print()
    print("  field by field:")
    print(f"    magic     0x{packet[0]:02X}")
    print(f"    version   {packet[1]}")
    print(f"    type      0x{packet[2]:02X} (INPUT)")
    print(f"    pad       {packet[3]}")
    print(f"    sequence  0x{GOLDEN_SEQUENCE:08X}  -> {packet[4:8].hex().upper()} on the wire")
    print(f"    buttons   0x{GOLDEN_BUTTONS:04X}      -> {packet[8:10].hex().upper()}")
    print(f"    LT / RT   {GOLDEN_LEFT_TRIGGER} / {GOLDEN_RIGHT_TRIGGER}")
    print(f"    LX / LY   {GOLDEN_THUMB_LX} / {GOLDEN_THUMB_LY}")
    print(f"    RX / RY   {GOLDEN_THUMB_RX} / {GOLDEN_THUMB_RY}")
    print()

    ok = True

    if actual != GOLDEN_EXPECTED_HEX:
        print("  FAIL: encoded bytes do not match the expected golden vector")
        print(f"    expected {GOLDEN_EXPECTED_HEX}")
        print(f"    actual   {actual}")
        ok = False

    decoded = decode_input(packet)
    if decoded is None:
        print("  FAIL: the packet we just built did not decode")
        ok = False
    elif (decoded["sequence"] != GOLDEN_SEQUENCE
          or decoded["thumb_ly"] != GOLDEN_THUMB_LY
          or decoded["thumb_ry"] != GOLDEN_THUMB_RY):
        print("  FAIL: round trip lost or altered a field")
        print(f"    {decoded}")
        ok = False

    # Garbage must be rejected rather than interpreted.
    if decode_input(b"\x00" * INPUT_PACKET_SIZE) is not None:
        print("  FAIL: a packet with no magic byte was accepted")
        ok = False
    if decode_input(b"") is not None or decode_input(packet + b"\x00") is not None:
        print("  FAIL: a wrong-length buffer was accepted")
        ok = False

    # ---- session messages -------------------------------------------------

    print("  session messages, 4 bytes each:")
    for name, (msg_type, pad, expected) in GOLDEN_SESSION.items():
        encoded = encode_session(msg_type, pad)
        actual_hex = encoded.hex()
        mark = "  " if actual_hex == expected else "  <- FAIL"
        print(f"    {name:<14} {' '.join(f'{b:02X}' for b in encoded)}{mark}")

        if actual_hex != expected:
            print(f"      expected {expected}, got {actual_hex}")
            ok = False

        decoded = decode_session(encoded)
        if decoded is None or decoded["type"] != msg_type or decoded["pad"] != pad:
            print(f"      FAIL: {name} did not round-trip")
            ok = False
    print()

    # The two decoders must never both accept the same bytes. They are told
    # apart by length alone, so this is what keeps that true.
    if decode_session(packet) is not None:
        print("  FAIL: an INPUT packet was accepted as a session message")
        ok = False
    if decode_input(encode_session(MSG_HELLO, NO_PAD)) is not None:
        print("  FAIL: a HELLO was accepted as an INPUT packet")
        ok = False
    if decode_session(b"\x00" * SESSION_MESSAGE_SIZE) is not None:
        print("  FAIL: a session message with no magic byte was accepted")
        ok = False

    if ok:
        print("  OK - encoding, round trip and rejection all behave.")
        print()
        print("  Cross-check: this hex must match GoldenBytes in")
        print("  server/DroidOSS.Tests/InputPacketTests.cs, and the session")
        print("  vectors in server/DroidOSS.Tests/SessionMessageTests.cs")

    return 0 if ok else 1


# ---------------------------------------------------------------------------
# The handshake
# ---------------------------------------------------------------------------

def perform_handshake(sock: socket.socket, host: str, port: int,
                      attempts: int = 15) -> int | None:
    """Ask for a pad slot, and keep asking until the server answers.

    Returns the assigned slot, NO_PAD if the server is full, or None if nothing
    replied at all.

    The retry loop is the point. A HELLO can be lost, and the server would then
    never know this client exists -- it would drop every INPUT that followed as
    coming from an unknown sender. Retrying is the client's job because the
    client is the side that wants something, and it doubles as the answer to the
    most common real situation: the app being opened before the server is running.
    """
    hello = encode_session(MSG_HELLO, NO_PAD)
    sock.settimeout(HELLO_RETRY_SECONDS)

    try:
        for attempt in range(1, attempts + 1):
            sock.sendto(hello, (host, port))

            try:
                data, _ = sock.recvfrom(64)
            except socket.timeout:
                if attempt == 1 or attempt % 5 == 0:
                    print(f"  no WELCOME yet ({attempt}/{attempts})...")
                continue
            except ConnectionResetError:
                # Windows reports ICMP "port unreachable" this way, which is what
                # you get when nothing is listening. Keep trying -- the server
                # may be about to start.
                if attempt == 1:
                    print("  nothing listening there yet...")
                time.sleep(HELLO_RETRY_SECONDS)
                continue

            message = decode_session(data)
            if message is None or message["type"] != MSG_WELCOME:
                continue

            return message["pad"]

        return None

    finally:
        sock.settimeout(None)


# ---------------------------------------------------------------------------
# Sending
# ---------------------------------------------------------------------------

def run_sender(host: str, port: int, pad: int, rate: float, duration: float | None,
               no_handshake: bool = False) -> int:
    """Stream INPUT packets at a fixed rate.

    Fixed-rate rather than send-on-change: event-driven sending arrives in
    bursts, which feels like jitter, and it makes silence impossible to
    distinguish from "nothing moved" -- which the server's disconnect timeout
    depends on being able to tell apart.
    """
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    if no_handshake:
        # Deliberately antisocial: fire input at a server that has never heard of
        # us. Everything sent should be counted as coming from an unknown sender
        # and no pad should appear. This is the only way to exercise that path.
        print(f"Skipping the handshake -- sending to {host}:{port} uninvited.")
        print("The server should ignore all of it.")
        print()
        slot = pad
    else:
        print(f"Connecting to {host}:{port}...")
        slot = perform_handshake(sock, host, port)

        if slot is None:
            print()
            print(f"No answer from {host}:{port}.")
            print("Check: is the server running, is the address right, is a firewall in the way?")
            sock.close()
            return 1

        if slot == NO_PAD:
            print()
            print("The server is full -- all four pads are in use.")
            print("Disconnect another phone and try again.")
            sock.close()
            return 1

        print(f"Connected as pad {slot}.")
        print()

    period = 1.0 / rate
    amplitude = 0.85 * 32767
    sweep_period = 2.0

    sequence = 0
    sent = 0
    started = time.perf_counter()
    next_tick = started
    last_report = started

    print(f"Sending to {host}:{port} as pad {slot} at {rate:g} Hz.")
    print("Press Ctrl+C to stop.")
    print()

    try:
        while True:
            now = time.perf_counter()
            elapsed = now - started

            if duration is not None and elapsed >= duration:
                break

            angle = elapsed / sweep_period * 2 * math.pi

            # A button that toggles once a second, so a watcher can see that
            # button bits travel as well as axes.
            buttons = BTN_A if int(elapsed) % 2 == 0 else 0

            packet = encode_input(
                pad=slot,
                sequence=sequence,
                buttons=buttons,
                thumb_lx=int(math.cos(angle) * amplitude),
                thumb_ly=int(math.sin(angle) * amplitude),
            )
            sock.sendto(packet, (host, port))

            sequence += 1
            sent += 1

            if now - last_report >= 1.0:
                measured = sent / elapsed
                print(f"  t={elapsed:5.1f}s  seq={sequence:<8} sent={sent:<8} ~{measured:.0f} Hz")
                last_report = now

            # Absolute deadlines, so small overruns don't accumulate into drift.
            next_tick += period
            delay = next_tick - time.perf_counter()
            if delay > 0:
                time.sleep(delay)
            else:
                # We fell behind. Resync to now rather than firing a burst of
                # catch-up packets, which would arrive as a latency spike.
                next_tick = time.perf_counter()

    except KeyboardInterrupt:
        print()
        print("Stopping.")

    finally:
        # Exactly what the real app must do in onPause: neutral state first, then
        # BYE. Neutral first because a game that latches the last state it saw
        # would otherwise hold the stick wherever the thumb left it; BYE so the
        # server unplugs immediately instead of waiting out its 2 s timeout,
        # which is technically correct and practically awful when someone just
        # glanced at a notification.
        neutral = encode_input(pad=slot, sequence=sequence)
        sock.sendto(neutral, (host, port))

        if not no_handshake:
            sock.sendto(encode_session(MSG_BYE, slot), (host, port))

        sock.close()

    elapsed = time.perf_counter() - started
    print(f"Sent {sent} packets in {elapsed:.1f}s ({sent / elapsed:.0f} Hz average).")
    return 0


# ---------------------------------------------------------------------------
# Listening
# ---------------------------------------------------------------------------

def run_listener(port: int, duration: float | None) -> int:
    """Decode an incoming stream.

    Exists so B1 can be verified before the C# listener exists, and stays useful
    afterwards for answering "is the phone actually sending anything?" without
    involving the server at all.
    """
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind(("", port))
    sock.settimeout(0.5)

    print(f"Listening on UDP {port}. Press Ctrl+C to stop.")
    print()

    received = 0
    rejected = 0
    started = time.perf_counter()
    last_report = started
    last_sequence: int | None = None
    gaps = 0

    # Rate is measured across the packets themselves -- first arrival to last --
    # never from listener startup or to listener shutdown. Idle time at either
    # end would otherwise be averaged in, understating the rate exactly when you
    # are trying to diagnose a rate problem.
    #
    # N packets span N-1 intervals, so that is what the division uses.
    first_packet_at: float | None = None
    last_packet_at: float | None = None

    try:
        while True:
            if duration is not None and time.perf_counter() - started >= duration:
                break

            try:
                data, sender = sock.recvfrom(2048)
            except socket.timeout:
                continue

            packet = decode_input(data)
            if packet is None:
                rejected += 1
                continue

            received += 1

            now = time.perf_counter()
            if first_packet_at is None:
                first_packet_at = now
            last_packet_at = now

            # Sequence gaps are lost packets. Worth counting -- a few are normal
            # and harmless, a flood means something is wrong with the network.
            if last_sequence is not None and packet["sequence"] > last_sequence + 1:
                gaps += packet["sequence"] - last_sequence - 1
            last_sequence = packet["sequence"]

            if now - last_report >= 1.0:
                span = now - first_packet_at
                rate = f"~{(received - 1) / span:.0f} Hz" if received > 1 and span > 0 else "  --  "
                print(
                    f"  from {sender[0]}  pad={packet['pad']}  "
                    f"seq={packet['sequence']:<8} "
                    f"LX={packet['thumb_lx']:>7} LY={packet['thumb_ly']:>7} "
                    f"btn=0x{packet['buttons']:04X}  "
                    f"{rate}  lost={gaps}"
                )
                last_report = now

    except KeyboardInterrupt:
        print()

    finally:
        sock.close()

    if received == 0 or first_packet_at is None or last_packet_at is None:
        waited = time.perf_counter() - started
        print(f"No packets in {waited:.1f}s.")
        if rejected:
            print(f"{rejected} arrived but were not ours -- wrong version, or another app.")
        print("Check: same port, same network, and no firewall in the way.")
        return 1

    span = last_packet_at - first_packet_at
    if received > 1 and span > 0:
        print(f"Received {received} valid packets over {span:.1f}s "
              f"({(received - 1) / span:.0f} Hz), {gaps} lost, "
              f"{rejected} rejected as not ours.")
    else:
        print(f"Received {received} valid packet(s), {rejected} rejected as not ours.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Pretend to be a droidOSS phone.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__.split("Usage:")[1] if "Usage:" in __doc__ else None,
    )
    parser.add_argument("--host", default="127.0.0.1",
                        help="server address (default: 127.0.0.1)")
    parser.add_argument("--port", type=int, default=INPUT_PORT,
                        help=f"UDP port (default: {INPUT_PORT})")
    parser.add_argument("--pad", type=int, default=0, choices=range(4),
                        help="preferred pad slot -- the server decides, and usually ignores this")
    parser.add_argument("--rate", type=float, default=125.0,
                        help="packets per second (default: 125)")
    parser.add_argument("--duration", type=float, default=None,
                        help="stop after this many seconds (default: run until Ctrl+C)")
    parser.add_argument("--selftest", action="store_true",
                        help="print the golden packet and verify encoding, then exit")
    parser.add_argument("--listen", action="store_true",
                        help="receive and decode instead of sending")
    parser.add_argument("--no-handshake", action="store_true",
                        help="send input without connecting first; the server should ignore it")

    args = parser.parse_args()

    if args.selftest:
        return run_selftest()
    if args.listen:
        return run_listener(args.port, args.duration)
    return run_sender(args.host, args.port, args.pad, args.rate, args.duration,
                      args.no_handshake)

    
if __name__ == "__main__":
    sys.exit(main())
