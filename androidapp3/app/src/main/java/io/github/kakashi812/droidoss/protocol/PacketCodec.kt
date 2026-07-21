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
}

/** A session message we received and believed. */
data class SessionMessage(val type: MessageType, val pad: Byte)

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
}
