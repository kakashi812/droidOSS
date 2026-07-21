package io.github.kakashi812.droidoss.protocol

/**
 * The wire format, transcribed from `docs/PROTOCOL.md`.
 *
 * This is the third hand-written implementation of the same specification —
 * C# in `DroidOSS.Core/Protocol.cs`, Python in `tools/fake_phone.py`, Kotlin
 * here. Nothing generates one from another, so a disagreement produces no
 * error at all: just a controller that behaves strangely.
 *
 * Change anything here and you must change all three, and bump [VERSION].
 */
object Protocol {

    /** First byte of every packet. Anything without it is not ours. */
    const val MAGIC_BYTE: Byte = 0xDA.toByte()

    /** Format version, so an old phone and a new server refuse each other politely. */
    const val VERSION: Byte = 1

    /** UDP port carrying input and session messages. */
    const val INPUT_PORT = 27500

    /** UDP port for discovery broadcasts only. Not used until B6. */
    const val DISCOVERY_PORT = 27501

    /** 4 header + 4 sequence + 12 payload. */
    const val INPUT_PACKET_SIZE = 20

    /** The header every message shares, whatever its type. */
    const val HEADER_SIZE = 4

    /** HELLO, WELCOME and BYE carry no payload — the header says everything. */
    const val SESSION_MESSAGE_SIZE = HEADER_SIZE

    /**
     * The `pad` byte when it names no particular slot.
     *
     * In HELLO it means "any slot, you choose" — a phone never picks its own, or
     * two phones both claim pad 0. In WELCOME it means "no slot for you": all
     * four are in use.
     */
    const val NO_PAD: Byte = 0xFF.toByte()

    /** How long the server tolerates silence before zeroing and unplugging the pad. */
    const val SESSION_TIMEOUT_MS = 2_000L

    /**
     * How long to wait for WELCOME before sending HELLO again.
     *
     * UDP does not guarantee delivery, so a HELLO can simply vanish and the
     * server would never know this phone exists. The client retries because the
     * client is the side that wants something. This also covers the ordinary
     * case of the app being opened before the server is running.
     */
    const val HELLO_RETRY_MS = 200L

    /**
     * Packets per second.
     *
     * Fixed-rate rather than send-on-change: event-driven sending arrives in
     * bursts, which feels like jitter, and it makes silence impossible to tell
     * apart from "nothing moved" — which the disconnect timeout depends on.
     */
    const val SEND_RATE_HZ = 125

    /** Byte positions within a packet. The codec never contains a bare number. */
    object Offset {
        const val MAGIC = 0
        const val VERSION = 1
        const val TYPE = 2
        const val PAD = 3
        const val SEQUENCE = 4

        // The payload below is byte-for-byte XINPUT_GAMEPAD, which is what lets
        // the server read straight off the wire into the driver with no
        // conversion at all.
        const val BUTTONS = 8
        const val LEFT_TRIGGER = 10
        const val RIGHT_TRIGGER = 11
        const val THUMB_LX = 12
        const val THUMB_LY = 14
        const val THUMB_RX = 16
        const val THUMB_RY = 18
    }
}

/**
 * What a packet is for. Byte 2 of every message.
 *
 * There is deliberately no heartbeat type. The input stream *is* the heartbeat —
 * a packet every 8 ms means silence is unmistakable.
 */
enum class MessageType(val id: Byte) {
    /** The 20-byte state snapshot. Phone to PC, 125 times a second. */
    INPUT(0x01),

    /** "I'm here." Phone to PC. The server assigns a pad slot in reply. */
    HELLO(0x02),

    /** "You're pad 2." PC to phone. Also proves a server exists at this address. */
    WELCOME(0x03),

    /** Clean exit. Phone to PC, so the pad unplugs now rather than on timeout. */
    BYE(0x04),

    /** Vibration intensity from the game. PC to phone. Not used until B8. */
    RUMBLE(0x05),

    /** "Any servers out there?" Broadcast, answered with WELCOME. Not used until B6. */
    DISCOVER(0x06);

    companion object {
        fun fromId(id: Byte): MessageType? = entries.firstOrNull { it.id == id }
    }
}

/**
 * Button bits, as they sit in bytes 8–9 of an INPUT packet.
 *
 * Bit 11 is unused. The D-pad occupies bits 0–3 and is **not** optional: it is
 * what platformers, 2D games and emulation are played with, and those are this
 * project's best use case.
 */
object GamepadButton {
    const val DPAD_UP = 0x0001
    const val DPAD_DOWN = 0x0002
    const val DPAD_LEFT = 0x0004
    const val DPAD_RIGHT = 0x0008
    const val START = 0x0010
    const val BACK = 0x0020
    const val LEFT_THUMB = 0x0040
    const val RIGHT_THUMB = 0x0080
    const val LEFT_SHOULDER = 0x0100
    const val RIGHT_SHOULDER = 0x0200
    const val GUIDE = 0x0400
    const val A = 0x1000
    const val B = 0x2000
    const val X = 0x4000
    const val Y = 0x8000
}
