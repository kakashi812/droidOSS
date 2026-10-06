package io.github.kakashi812.droidoss.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder

/**
 * Builds outgoing packets into one buffer that is allocated once and reused
 * forever.
 *
 * **`ByteOrder.LITTLE_ENDIAN` is the single most important line in this file.**
 * Java and Kotlin default `ByteBuffer` to big-endian, and forgetting to change
 * it corrupts every multi-byte field silently — no exception, no error, just a
 * stick that reads 59395 when it should read 1000. ARM and x86 are both
 * little-endian natively, so this costs nothing on either side of the wire.
 *
 * Not thread-safe. One instance belongs to one sending thread.
 */
class PacketWriter {

    private val buffer: ByteBuffer =
        ByteBuffer.allocate(Protocol.INPUT_PACKET_SIZE).order(ByteOrder.LITTLE_ENDIAN)

    /** The backing array. Valid for [InputPacketSize] or [SessionMessageSize] bytes after a write. */
    val bytes: ByteArray = buffer.array()

    /**
     * Writes a 20-byte INPUT packet.
     *
     * Call this with the state lock held — it reads every field of [state] and a
     * torn read would send a snapshot that never actually existed.
     *
     * @return bytes written, always [Protocol.INPUT_PACKET_SIZE].
     */
    fun writeInput(pad: Byte, sequence: Int, state: PadState): Int {
        buffer.clear()

        buffer.put(Protocol.MAGIC_BYTE)
        buffer.put(Protocol.VERSION)
        buffer.put(MessageType.INPUT.id)
        buffer.put(pad)

        // Signed Int on the wire is the same four bytes as the server's u32. The
        // server compares with subtract-and-cast, so it handles the wrap for us
        // and we can simply keep incrementing.
        buffer.putInt(sequence)

        buffer.putShort(state.buttons.toShort())
        buffer.put(state.leftTrigger.toByte())
        buffer.put(state.rightTrigger.toByte())
        buffer.putShort(state.thumbLX)
        buffer.putShort(state.thumbLY)
        buffer.putShort(state.thumbRX)
        buffer.putShort(state.thumbRY)

        return buffer.position()
    }

    /**
     * Writes a 4-byte HELLO, WELCOME or BYE.
     *
     * @return bytes written, always [Protocol.SESSION_MESSAGE_SIZE].
     */
    fun writeSession(type: MessageType, pad: Byte): Int {
        buffer.clear()

        buffer.put(Protocol.MAGIC_BYTE)
        buffer.put(Protocol.VERSION)
        buffer.put(type.id)
        buffer.put(pad)

        return buffer.position()
    }

    /**
     * Writes a 4-byte DISCOVER, the broadcast that asks every server on the
     * network to announce itself.
     *
     * @return bytes written, always [Protocol.HEADER_SIZE].
     */
    fun writeDiscover(): Int = writeSession(MessageType.DISCOVER, Protocol.NO_PAD)
}

/** A session message we received and believed. */
data class SessionMessage(val type: MessageType, val pad: Byte)

/**
 * What a server says about itself in answer to DISCOVER. Its address is not in
 * here: it is wherever the announcement came from.
 */
data class Announcement(val name: String, val inputPort: Int, val freePads: Int)

/** What the game wants the motors doing: each 0–255, heavy (`large`) and light (`small`). */
data class Rumble(val pad: Byte, val large: Int, val small: Int)

/** Which player the game shows this phone as, and its colour as `0xFFRRGGBB`. */
data class Light(val pad: Byte, val player: Int, val color: Int)

/**
 * Reads the messages that travel PC → phone.
 *
 * Validation happens **before any field is trusted**: length, then magic, then
 * version, then type. A UDP socket receives port scans, other applications'
 * strays, and traffic from someone who mistyped an address; without the magic
 * byte that garbage becomes controller state.
 */
object PacketReader {

    /**
     * Parses HELLO, WELCOME or BYE.
     *
     * An INPUT packet is correctly rejected — the two are told apart by length
     * alone, which is why nothing else may ever be exactly four bytes.
     *
     * @return null if this is not a well-formed session message.
     */
    fun readSession(data: ByteArray, length: Int): SessionMessage? {
        if (length != Protocol.SESSION_MESSAGE_SIZE) return null
        if (data[Protocol.Offset.MAGIC] != Protocol.MAGIC_BYTE) return null
        if (data[Protocol.Offset.VERSION] != Protocol.VERSION) return null

        val type = MessageType.fromId(data[Protocol.Offset.TYPE]) ?: return null
        if (type != MessageType.HELLO && type != MessageType.WELCOME && type != MessageType.BYE) {
            return null
        }

        return SessionMessage(type, data[Protocol.Offset.PAD])
    }

    /**
     * Parses a RUMBLE: `DA ver 05 pad large small`, six bytes.
     *
     * State, not an event — the server repeats it while the game keeps asking.
     */
    fun readRumble(data: ByteArray, length: Int): Rumble? {
        if (!hasHeader(data, length, MessageType.RUMBLE, Protocol.RUMBLE_SIZE)) return null
        val at = Protocol.HEADER_SIZE
        return Rumble(data[Protocol.Offset.PAD], data[at].toInt() and 0xFF, data[at + 1].toInt() and 0xFF)
    }

    /**
     * Parses a LIGHT: `DA ver 07 pad player R G B`, eight bytes.
     *
     * The colour is the player's own on an Xbox pad, or whatever the game or
     * Steam set the lightbar to when the server is showing a DualShock 4.
     */
    fun readLight(data: ByteArray, length: Int): Light? {
        if (!hasHeader(data, length, MessageType.LIGHT, Protocol.LIGHT_SIZE)) return null
        val at = Protocol.HEADER_SIZE
        val r = data[at + 1].toInt() and 0xFF
        val g = data[at + 2].toInt() and 0xFF
        val b = data[at + 3].toInt() and 0xFF
        return Light(
            pad = data[Protocol.Offset.PAD],
            player = data[at].toInt() and 0xFF,
            color = (0xFF shl 24) or (r shl 16) or (g shl 8) or b,
        )
    }

    private fun hasHeader(data: ByteArray, length: Int, type: MessageType, size: Int): Boolean =
        length == size &&
            data[Protocol.Offset.MAGIC] == Protocol.MAGIC_BYTE &&
            data[Protocol.Offset.VERSION] == Protocol.VERSION &&
            data[Protocol.Offset.TYPE] == type.id

    /**
     * Parses the announcement a server sends in answer to DISCOVER:
     *
     * ```
     * byte  0     1    2     3      4-5        6        7          8..
     *     0xDA  ver  0x03  0xFF  inputPort  freePads  nameLength  name (UTF-8)
     * ```
     *
     * Only ever read from the discovery socket, and never four bytes long, so it
     * cannot be confused with the session WELCOME that shares its type byte.
     *
     * @return null if this is not a well-formed announcement.
     */
    fun readAnnounce(data: ByteArray, length: Int): Announcement? {
        if (length < Protocol.ANNOUNCE_FIXED_SIZE) return null
        if (data[Protocol.Offset.MAGIC] != Protocol.MAGIC_BYTE) return null
        if (data[Protocol.Offset.VERSION] != Protocol.VERSION) return null
        if (data[Protocol.Offset.TYPE] != MessageType.WELCOME.id) return null

        val buffer = ByteBuffer.wrap(data, 0, length).order(ByteOrder.LITTLE_ENDIAN)
        buffer.position(Protocol.HEADER_SIZE)

        val port = buffer.short.toInt() and 0xFFFF
        val freePads = buffer.get().toInt() and 0xFF
        val nameLength = buffer.get().toInt() and 0xFF

        if (nameLength > Protocol.MAX_SERVER_NAME_BYTES) return null
        if (length != Protocol.ANNOUNCE_FIXED_SIZE + nameLength) return null

        val name = String(data, Protocol.ANNOUNCE_FIXED_SIZE, nameLength, Charsets.UTF_8)
        return Announcement(name, port, freePads)
    }
}
