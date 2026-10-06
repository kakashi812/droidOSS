package io.github.kakashi812.droidoss.layout

import android.content.ClipData
import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.core.content.FileProvider
import java.io.File
import java.io.IOException

/**
 * The Android half of sharing: files in and out. Everything about the content
 * itself — format, limits, validation — is [LayoutShare]'s, and tested there.
 *
 * No storage permission is involved anywhere. Outgoing files are handed over
 * through a [FileProvider] grant that lasts as long as the receiving app needs
 * it; incoming ones arrive as `content://` URIs the picker or the sending app
 * has already granted.
 */
object LayoutTransfer {

    /** Exported layouts are JSON; WhatsApp and the rest keep this type with the file. */
    const val MIME_TYPE = "application/json"

    /** What the import picker offers. Some apps save JSON as plain text or bytes. */
    val PICKER_TYPES = arrayOf(MIME_TYPE, "text/plain", "application/octet-stream")

    private const val SHARE_DIR = "shared"

    /**
     * A share-sheet chooser for [layout] as a file: WhatsApp, Telegram, Gmail,
     * Drive, Quick Share — whatever the phone has.
     *
     * The file lives in the cache, rewritten on each share, so nothing piles up.
     */
    fun shareIntent(context: Context, layout: ControllerLayout): Intent {
        val dir = File(context.cacheDir, SHARE_DIR).apply { mkdirs() }
        // One file at a time is enough; clear out the last one shared.
        dir.listFiles()?.forEach { it.delete() }

        val file = File(dir, LayoutShare.fileName(layout))
        file.writeText(LayoutShare.encodeFile(layout))

        val uri = FileProvider.getUriForFile(context, "${context.packageName}.files", file)
        val send = Intent(Intent.ACTION_SEND).apply {
            type = MIME_TYPE
            putExtra(Intent.EXTRA_STREAM, uri)
            putExtra(Intent.EXTRA_SUBJECT, "droidOSS layout: ${layout.name}")
            // ClipData as well as the extra: some targets only honour a grant
            // attached this way.
            clipData = ClipData.newRawUri(layout.name, uri)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        return Intent.createChooser(send, "Share \"${layout.name}\"")
    }

    /** Write [layout] to a document the user chose with the save dialog. */
    fun writeTo(context: Context, uri: Uri, layout: ControllerLayout): Boolean =
        try {
            context.contentResolver.openOutputStream(uri, "wt")?.use {
                it.write(LayoutShare.encodeFile(layout).toByteArray(Charsets.UTF_8))
            } != null
        } catch (_: IOException) {
            false
        } catch (_: SecurityException) {
            false
        }

    /**
     * Read and decode a shared file. Reads at most one byte past
     * [LayoutShare.MAX_INPUT_BYTES], so a huge file someone picked by mistake
     * is refused without being loaded.
     */
    fun read(context: Context, uri: Uri): ImportResult {
        val text = try {
            context.contentResolver.openInputStream(uri)?.use { input ->
                val bytes = input.readNBytesCompat(LayoutShare.MAX_INPUT_BYTES + 1)
                if (bytes.size > LayoutShare.MAX_INPUT_BYTES) {
                    return ImportResult.Invalid("This file is too big to be a droidOSS layout.")
                }
                String(bytes, Charsets.UTF_8)
            }
        } catch (_: IOException) {
            null
        } catch (_: SecurityException) {
            null
        } ?: return ImportResult.Invalid("Couldn't open that file. Try saving it to the phone first.")

        return LayoutShare.decode(text)
    }

    /**
     * The file a share or "open with" intent carries, if it is one we handle.
     * Anything else — including our own launcher intent — is null.
     */
    fun incomingUri(intent: Intent?): Uri? = when (intent?.action) {
        Intent.ACTION_VIEW -> intent.data
        Intent.ACTION_SEND ->
            @Suppress("DEPRECATION")
            (intent.getParcelableExtra<Uri>(Intent.EXTRA_STREAM) ?: intent.clipData?.getItemAt(0)?.uri)
        else -> null
    }

    // InputStream.readNBytes is API 33; this app goes back to 26.
    private fun java.io.InputStream.readNBytesCompat(limit: Int): ByteArray {
        val out = java.io.ByteArrayOutputStream()
        val buffer = ByteArray(8 * 1024)
        while (out.size() < limit) {
            val n = read(buffer, 0, minOf(buffer.size, limit - out.size()))
            if (n < 0) break
            out.write(buffer, 0, n)
        }
        return out.toByteArray()
    }
}
