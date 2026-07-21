package io.github.kakashi812.droidoss.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * Proves this Kotlin encoder agrees byte-for-byte with the other two
 * implementations of the same specification.
 *
 * The hex strings below are copied from `tools/fake_phone.py`, which asserts
 * them from Python, and `server/DroidOSS.Tests/InputPacketTests.cs`, which
 * asserts them from C#. Three independent implementations producing identical
 * bytes is what makes `docs/PROTOCOL.md` unambiguous rather than merely written
 * down.
 *
 * **If one of these fails, do not adjust the expected value.** It is shared with
 * two other languages; changing it here just moves the disagreement somewhere
 * harder to find.
 *
 * Plain JVM tests — no Android APIs, no device, no emulator.
 */
class PacketCodecTest {

    private fun hex(bytes: ByteArray, length: Int): String =
        bytes.take(length).joinToString("") { "%02x".format(it) }

    /** The golden vector. Every field carries a distinctive value, so a
     *  transposition or an endianness mistake cannot hide. */
    @Test
    fun `input packet matches the golden vector`() {
        val state = PadState().apply {
            buttons = GamepadButton.A or GamepadButton.DPAD_LEFT   // 0x1004
            leftTrigger = 0x20                                     // 32
            rightTrigger = 0xC8                                    // 200
            thumbLX = 1000
            thumbLY = -1000
            thumbRX = 32767
            thumbRY = -32768
        }

        val writer = PacketWriter()
        val length = writer.writeInput(pad = 2, sequence = 0x12345678, state = state)

        assertEquals(Protocol.INPUT_PACKET_SIZE, length)
        assertEquals("da010102785634120410 20c8e80318fcff7f0080".replace(" ", ""),
            hex(writer.bytes, length))
    }

    /**
     * The endianness trap, isolated.
     *
     * If `ByteOrder.LITTLE_ENDIAN` is ever dropped from [PacketWriter], this is
     * the test that says so in one line instead of leaving you to wonder why the
     * stick jumps to a corner.
     */
    @Test
    fun `multi-byte fields are little-endian`() {
        val state = PadState().apply { thumbLX = 1000 }   // 0x03E8
        val writer = PacketWriter()
        writer.writeInput(pad = 0, sequence = 1, state = state)

        // Low byte first. Big-endian would give e8 at THUMB_LX + 1.
        assertEquals(0xE8.toByte(), writer.bytes[Protocol.Offset.THUMB_LX])
        assertEquals(0x03.toByte(), writer.bytes[Protocol.Offset.THUMB_LX + 1])

        // And the sequence, the widest field.
        writer.writeInput(pad = 0, sequence = 0x12345678, state = state)
        assertArrayEquals(
            byteArrayOf(0x78, 0x56, 0x34, 0x12),
            writer.bytes.copyOfRange(Protocol.Offset.SEQUENCE, Protocol.Offset.SEQUENCE + 4),
        )
    }

    @Test
    fun `session messages match the golden vectors`() {
        val writer = PacketWriter()

        val cases = listOf(
            Triple(MessageType.HELLO, Protocol.NO_PAD, "da0102ff"),
            Triple(MessageType.WELCOME, 1.toByte(), "da010301"),
            Triple(MessageType.WELCOME, Protocol.NO_PAD, "da0103ff"),
            Triple(MessageType.BYE, 1.toByte(), "da010401"),
        )

        for ((type, pad, expected) in cases) {
            val length = writer.writeSession(type, pad)
            assertEquals(Protocol.SESSION_MESSAGE_SIZE, length)
            assertEquals("$type pad $pad", expected, hex(writer.bytes, length))
        }
    }

    @Test
    fun `welcome round-trips`() {
        val writer = PacketWriter()
        val length = writer.writeSession(MessageType.WELCOME, 2)

        val message = PacketReader.readSession(writer.bytes, length)
        assertEquals(SessionMessage(MessageType.WELCOME, 2), message)
    }

    /** Garbage must be rejected rather than interpreted. */
    @Test
    fun `reader rejects what is not a session message`() {
        val writer = PacketWriter()
        val inputLength = writer.writeInput(0, 1, PadState())

        // An INPUT packet is well-formed but belongs to the other reader.
        assertNull("INPUT accepted as a session message",
            PacketReader.readSession(writer.bytes, inputLength))

        assertNull("empty buffer accepted", PacketReader.readSession(ByteArray(0), 0))

        val noMagic = byteArrayOf(0x00, Protocol.VERSION, MessageType.WELCOME.id, 1)
        assertNull("missing magic byte accepted", PacketReader.readSession(noMagic, 4))

        val wrongVersion = byteArrayOf(Protocol.MAGIC_BYTE, 99, MessageType.WELCOME.id, 1)
        assertNull("wrong version accepted", PacketReader.readSession(wrongVersion, 4))

        val unknownType = byteArrayOf(Protocol.MAGIC_BYTE, Protocol.VERSION, 0x7F, 1)
        assertNull("unknown type accepted", PacketReader.readSession(unknownType, 4))
    }

    @Test
    fun `setButton leaves other buttons alone`() {
        val state = PadState()

        state.setButton(GamepadButton.A, true)
        state.setButton(GamepadButton.DPAD_LEFT, true)
        assertEquals(0x1004, state.buttons)

        state.setButton(GamepadButton.A, false)
        assertEquals(GamepadButton.DPAD_LEFT, state.buttons)
        assertEquals(true, state.isPressed(GamepadButton.DPAD_LEFT))
        assertEquals(false, state.isPressed(GamepadButton.A))
    }

    @Test
    fun `reset centres everything`() {
        val state = PadState().apply {
            buttons = 0xFFFF
            leftTrigger = 255
            rightTrigger = 255
            thumbLX = 1000; thumbLY = -1000; thumbRX = 32767; thumbRY = -32768
        }

        state.reset()

        val writer = PacketWriter()
        val length = writer.writeInput(pad = 0, sequence = 7, state = state)

        // Header (4) + sequence (4) + twelve zero bytes of payload = 40 hex chars.
        val expected = "da010100" + "07000000" + "0".repeat(24)
        assertEquals(expected, hex(writer.bytes, length))
    }
}
