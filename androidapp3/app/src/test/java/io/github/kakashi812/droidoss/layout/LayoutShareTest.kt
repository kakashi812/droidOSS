package io.github.kakashi812.droidoss.layout

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.ByteArrayOutputStream
import java.util.Base64
import java.util.zip.Deflater

/**
 * Sharing a layout between phones: what goes out round-trips exactly, and what
 * comes in — from WhatsApp, a camera, any file at all — is never trusted.
 */
class LayoutShareTest {

    private val racing = defaultLayout().copy(id = "custom4", name = "Racing")

    private fun ok(result: ImportResult): ImportResult.Ok {
        assertTrue("expected Ok, got $result", result is ImportResult.Ok)
        return result as ImportResult.Ok
    }

    private fun invalid(result: ImportResult): String {
        assertTrue("expected Invalid, got $result", result is ImportResult.Invalid)
        return (result as ImportResult.Invalid).message
    }

    @Test
    fun `a file round-trips name and controls`() {
        val result = ok(LayoutShare.decode(LayoutShare.encodeFile(racing)))

        assertEquals("Racing", result.name)
        assertEquals(racing.elements, result.elements)
    }

    @Test
    fun `a QR code round-trips name and controls`() {
        val result = ok(LayoutShare.decode(LayoutShare.encodeQr(racing)))

        assertEquals("Racing", result.name)
        assertEquals(racing.elements, result.elements)
    }

    @Test
    fun `the file carries the format marker and version but not the sender's id`() {
        val file = LayoutShare.encodeFile(racing)

        assertTrue(file.contains("\"format\": \"droidoss-layout\""))
        assertTrue(file.contains("\"version\": 1"))
        assertTrue(!file.contains("custom4"))
    }

    @Test
    fun `the default layout's QR code stays small enough to scan off a screen`() {
        val qr = LayoutShare.encodeQr(defaultLayout())

        assertTrue(qr.startsWith(LayoutShare.QR_PREFIX))
        // ~1 KB is a QR version in the low 20s at medium error correction:
        // comfortably readable from one phone's screen by another's camera.
        assertTrue("QR text is ${qr.length} chars", qr.length < 1_000)
    }

    @Test
    fun `positions are rounded so float noise does not bloat the code`() {
        val noisy = racing.copy(
            elements = listOf(racing.elements[0].with(x = 0.432178940f)) + racing.elements.drop(1),
        )
        val result = ok(LayoutShare.decode(LayoutShare.encodeQr(noisy)))
        assertEquals(0.4322f, result.elements[0].x)
    }

    @Test
    fun `surrounding whitespace is ignored`() {
        ok(LayoutShare.decode("\n  " + LayoutShare.encodeQr(racing) + "  \n"))
    }

    @Test
    fun `garbage is refused, never thrown`() {
        invalid(LayoutShare.decode(""))
        invalid(LayoutShare.decode("hello"))
        invalid(LayoutShare.decode("{}"))
        invalid(LayoutShare.decode("[1,2,3]"))
        invalid(LayoutShare.decode("{\"format\": \"droidoss-layout\""))
    }

    @Test
    fun `some other app's JSON is refused`() {
        val other = LayoutShare.encodeFile(racing).replace("droidoss-layout", "something-else")
        invalid(LayoutShare.decode(other))
    }

    @Test
    fun `a layout from a newer app says so`() {
        val newer = LayoutShare.encodeFile(racing).replace("\"version\": 1", "\"version\": 2")
        assertTrue(invalid(LayoutShare.decode(newer)).contains("newer"))
        assertTrue(invalid(LayoutShare.decode("DROIDOSS2:abc")).contains("newer"))
    }

    @Test
    fun `a damaged QR code is refused`() {
        val qr = LayoutShare.encodeQr(racing)
        invalid(LayoutShare.decode(qr.dropLast(20)))
        invalid(LayoutShare.decode(LayoutShare.QR_PREFIX + "!!!not base64!!!"))
    }

    @Test
    fun `a compression bomb is refused`() {
        // A megabyte of spaces deflates to about a kilobyte.
        val deflater = Deflater(Deflater.BEST_COMPRESSION, true)
        deflater.setInput(ByteArray(1_000_000) { ' '.code.toByte() })
        deflater.finish()
        val out = ByteArrayOutputStream()
        val chunk = ByteArray(1024)
        while (!deflater.finished()) out.write(chunk, 0, deflater.deflate(chunk))
        deflater.end()

        val bomb = LayoutShare.QR_PREFIX + Base64.getUrlEncoder().withoutPadding().encodeToString(out.toByteArray())
        invalid(LayoutShare.decode(bomb))
    }

    @Test
    fun `oversized input is refused before parsing`() {
        invalid(LayoutShare.decode(" ".repeat(10) + "x".repeat(LayoutShare.MAX_INPUT_BYTES + 1)))
    }

    @Test
    fun `out-of-range numbers are pulled back to what the editor allows`() {
        val wild = racing.copy(
            elements = listOf(
                racing.elements[0].with(x = 5f, y = -3f, size = 9f, opacity = 0f),
            ) + racing.elements.drop(1),
        )
        val first = ok(LayoutShare.decode(LayoutShare.encodeFile(wild))).elements[0]

        assertEquals(1f, first.x)
        assertEquals(0f, first.y)
        assertEquals(0.30f, first.size)
        assertEquals(0.2f, first.opacity)
    }

    @Test
    fun `a button with a made-up mask is refused`() {
        val bad = LayoutShare.encodeFile(racing).replace("\"mask\": 4096", "\"mask\": 3")
        invalid(LayoutShare.decode(bad))
    }

    @Test
    fun `repeated control ids are refused`() {
        val doubled = racing.copy(elements = racing.elements + racing.elements[0])
        invalid(LayoutShare.decode(LayoutShare.encodeFile(doubled)))
    }

    @Test
    fun `an empty layout is refused`() {
        invalid(LayoutShare.decode(LayoutShare.encodeFile(racing.copy(elements = emptyList()))))
    }

    @Test
    fun `an unknown control type is refused`() {
        val alien = LayoutShare.encodeFile(racing).replaceFirst("\"type\": \"button\"", "\"type\": \"laser\"")
        invalid(LayoutShare.decode(alien))
    }

    @Test
    fun `a blank or overlong name is fixed up`() {
        assertEquals("Imported layout", ok(LayoutShare.decode(LayoutShare.encodeFile(racing.copy(name = "  ")))).name)
        val long = ok(LayoutShare.decode(LayoutShare.encodeFile(racing.copy(name = "x".repeat(100))))).name
        assertEquals(MAX_NAME_LENGTH, long.length)
    }

    @Test
    fun `file names are safe for any file system or chat app`() {
        assertEquals("Racing.droidoss.json", LayoutShare.fileName(racing))
        assertEquals("FIFA_24 _ R_L.droidoss.json", LayoutShare.fileName(racing.copy(name = "FIFA:24 / R*L")))
        assertEquals("layout.droidoss.json", LayoutShare.fileName(racing.copy(name = "   ")))
        // A duplicate's "(2)" survives: brackets are safe everywhere.
        assertEquals("Racing (2).droidoss.json", LayoutShare.fileName(racing.copy(name = "Racing (2)")))
    }
}
