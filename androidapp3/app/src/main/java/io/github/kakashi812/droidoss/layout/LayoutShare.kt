package io.github.kakashi812.droidoss.layout

import io.github.kakashi812.droidoss.protocol.GamepadButton
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import java.io.ByteArrayOutputStream
import java.util.Base64
import java.util.zip.DataFormatException
import java.util.zip.Deflater
import java.util.zip.Inflater
import kotlin.math.roundToInt

/**
 * A layout as it travels between phones: one layout, by name, without the
 * sender's slot id — the receiver files it under an id of its own.
 *
 * [format] marks the JSON as ours, so a random `.json` someone picks by mistake
 * is turned away with a clear message instead of a confusing half-import.
 * [version] is the sharing format's own, separate from the store's: a layout
 * from a newer app can say so rather than being misread.
 */
@Serializable
data class SharedLayout(
    val format: String = "",
    val version: Int = 0,
    val name: String = "",
    val elements: List<ControlElement> = emptyList(),
)

/** What reading a shared layout produced. */
sealed interface ImportResult {
    /** A valid layout, already cleaned up and safe to store. */
    data class Ok(val name: String, val elements: List<ControlElement>) : ImportResult

    /** Not importable; [message] is written for the person holding the phone. */
    data class Invalid(val message: String) : ImportResult
}

/**
 * Turns a layout into something that can leave the phone — a file, or the text
 * inside a QR code — and back.
 *
 * Pure Kotlin with no Android dependency, like [LayoutCodec], so every rule here
 * is unit-tested on the JVM.
 *
 * Everything that comes in is **untrusted**: it arrived over WhatsApp, from a
 * camera, or from whatever file someone picked. [decode] never throws, caps how
 * much it will read or inflate, rejects what it cannot make sense of, and pulls
 * every number back into the range the editor itself allows, so an imported
 * layout can never hold a control the app could not have produced.
 */
object LayoutShare {

    const val FORMAT = "droidoss-layout"
    const val VERSION = 1

    /** Starts the text in a QR code; the digit is the sharing [VERSION]. */
    const val QR_PREFIX = "DROIDOSS1:"

    /** File extension for exported layouts. Ends in .json so any app can open it. */
    const val FILE_EXTENSION = ".droidoss.json"

    /** A real layout is a few KB; anything far beyond that is not one. */
    const val MAX_INPUT_BYTES = 64 * 1024

    /** More controls than any pad needs, few enough that the pad stays responsive. */
    const val MAX_ELEMENTS = 48

    // The same bounds LayoutEditorScreen puts on its sliders.
    private const val MIN_SIZE = 0.04f
    private const val MAX_SIZE = 0.30f
    private const val MIN_OPACITY = 0.2f
    private const val MAX_LABEL_LENGTH = 6
    private const val MAX_ID_LENGTH = 32

    /** Every bit a button may carry: one real Xbox 360 button each. */
    private val BUTTON_MASKS = setOf(
        GamepadButton.DPAD_UP, GamepadButton.DPAD_DOWN, GamepadButton.DPAD_LEFT,
        GamepadButton.DPAD_RIGHT, GamepadButton.START, GamepadButton.BACK,
        GamepadButton.LEFT_THUMB, GamepadButton.RIGHT_THUMB, GamepadButton.LEFT_SHOULDER,
        GamepadButton.RIGHT_SHOULDER, GamepadButton.GUIDE, GamepadButton.A,
        GamepadButton.B, GamepadButton.X, GamepadButton.Y,
    )

    private val fileJson = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
        prettyPrint = true
    }

    // For the QR code every byte makes the code denser and harder to scan, so
    // leave out anything the reader would fill in with the same default anyway.
    private val compactJson = Json {
        ignoreUnknownKeys = true
        encodeDefaults = false
    }

    /** A readable JSON file holding [layout]. */
    fun encodeFile(layout: ControllerLayout): String =
        fileJson.encodeToString(envelope(layout))

    /**
     * The text for a QR code: [QR_PREFIX], then the compact JSON deflated and
     * Base64url-encoded. A typical layout comes to well under 1 KB, which keeps
     * the code coarse enough to scan off another phone's screen.
     */
    fun encodeQr(layout: ControllerLayout): String {
        val raw = compactJson.encodeToString(envelope(layout)).toByteArray(Charsets.UTF_8)
        val deflater = Deflater(Deflater.BEST_COMPRESSION, true)
        val packed = try {
            deflater.setInput(raw)
            deflater.finish()
            val out = ByteArrayOutputStream()
            val chunk = ByteArray(1024)
            while (!deflater.finished()) out.write(chunk, 0, deflater.deflate(chunk))
            out.toByteArray()
        } finally {
            deflater.end()
        }
        return QR_PREFIX + Base64.getUrlEncoder().withoutPadding().encodeToString(packed)
    }

    /**
     * A safe file name for [layout]: its name with anything a file system or
     * chat app might object to replaced, plus [FILE_EXTENSION].
     */
    fun fileName(layout: ControllerLayout): String {
        val safe = layout.name
            .replace(Regex("[^A-Za-z0-9 ()_-]"), "_")
            .trim()
            .take(MAX_NAME_LENGTH)
            .ifEmpty { "layout" }
        return safe + FILE_EXTENSION
    }

    /**
     * Read a shared layout from a file's contents or a scanned QR code. Never
     * throws.
     */
    fun decode(text: String): ImportResult {
        val trimmed = text.trim()
        if (trimmed.isEmpty()) return notALayout()
        if (trimmed.length > MAX_INPUT_BYTES) return Invalid("This file is too big to be a droidOSS layout.")

        val jsonText = when {
            trimmed.startsWith(QR_PREFIX) -> unpackQr(trimmed.removePrefix(QR_PREFIX))
                ?: return Invalid("This QR code is damaged or incomplete. Try scanning it again.")
            // A QR from a newer app: same prefix, higher digit.
            trimmed.startsWith("DROIDOSS") -> return tooNew()
            else -> trimmed
        }

        val shared = runCatching { compactJson.decodeFromString<SharedLayout>(jsonText) }
            .getOrNull()
            ?: return notALayout()

        if (shared.format != FORMAT) return notALayout()
        if (shared.version > VERSION) return tooNew()
        return validate(shared)
    }

    private fun envelope(layout: ControllerLayout) = SharedLayout(
        format = FORMAT,
        version = VERSION,
        name = layout.name,
        elements = layout.elements.map(::rounded),
    )

    /**
     * Positions to 1/10 000 of the screen — far finer than a finger, and it
     * keeps float noise like 0.43217894 from bloating the QR code.
     */
    private fun rounded(element: ControlElement): ControlElement {
        val r = { v: Float -> (v * 10_000).roundToInt() / 10_000f }
        return element.with(x = r(element.x), y = r(element.y), size = r(element.size), opacity = r(element.opacity))
    }

    /** Base64url → inflate, refusing to produce more than [MAX_INPUT_BYTES]. */
    private fun unpackQr(payload: String): String? {
        val packed = runCatching { Base64.getUrlDecoder().decode(payload) }.getOrNull() ?: return null
        val inflater = Inflater(true)
        return try {
            inflater.setInput(packed)
            val out = ByteArrayOutputStream()
            val chunk = ByteArray(1024)
            while (!inflater.finished()) {
                val n = inflater.inflate(chunk)
                if (n == 0 && (inflater.needsInput() || inflater.needsDictionary())) return null
                out.write(chunk, 0, n)
                // A few hundred bytes that inflate to megabytes is an attack, not a layout.
                if (out.size() > MAX_INPUT_BYTES) return null
            }
            out.toString(Charsets.UTF_8.name())
        } catch (_: DataFormatException) {
            null
        } finally {
            inflater.end()
        }
    }

    private fun validate(shared: SharedLayout): ImportResult {
        val elements = shared.elements
        if (elements.isEmpty()) return Invalid("This layout has no controls in it.")
        if (elements.size > MAX_ELEMENTS) return Invalid("This layout has too many controls to be a droidOSS layout.")

        val ids = mutableSetOf<String>()
        val cleaned = elements.map { element ->
            if (element.id.isBlank() || element.id.length > MAX_ID_LENGTH || !ids.add(element.id)) {
                return notALayout()
            }
            val numbers = listOf(element.x, element.y, element.size, element.opacity)
            if (numbers.any { !it.isFinite() }) return notALayout()

            val placed = element.with(
                x = element.x.coerceIn(0f, 1f),
                y = element.y.coerceIn(0f, 1f),
                size = element.size.coerceIn(MIN_SIZE, MAX_SIZE),
                opacity = element.opacity.coerceIn(MIN_OPACITY, 1f),
            )
            when (placed) {
                is ButtonElement -> {
                    if (placed.mask !in BUTTON_MASKS) return notALayout()
                    placed.copy(label = placed.label.take(MAX_LABEL_LENGTH))
                }
                is TriggerElement -> placed.copy(label = placed.label.take(MAX_LABEL_LENGTH))
                is DpadElement -> {
                    if (!placed.deadzone.isFinite()) return notALayout()
                    placed.copy(deadzone = placed.deadzone.coerceIn(0f, 0.9f))
                }
                is StickElement -> placed
            }
        }

        val name = shared.name.trim().take(MAX_NAME_LENGTH).ifEmpty { "Imported layout" }
        return ImportResult.Ok(name, cleaned)
    }

    private fun Invalid(message: String) = ImportResult.Invalid(message)

    private fun notALayout() = Invalid("This isn't a droidOSS layout, or it is damaged.")

    private fun tooNew() =
        Invalid("This layout was made by a newer version of droidOSS. Update the app to import it.")
}
