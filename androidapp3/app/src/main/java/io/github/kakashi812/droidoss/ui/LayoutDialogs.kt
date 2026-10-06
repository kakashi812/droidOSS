package io.github.kakashi812.droidoss.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.google.zxing.BarcodeFormat
import com.google.zxing.EncodeHintType
import com.google.zxing.common.BitMatrix
import com.google.zxing.qrcode.QRCodeWriter
import com.google.zxing.qrcode.decoder.ErrorCorrectionLevel
import io.github.kakashi812.droidoss.layout.ControllerLayout
import io.github.kakashi812.droidoss.layout.MAX_LAYOUTS

/**
 * A layout as a QR code, for the phone across the room to scan.
 *
 * Always black on white, whatever the theme: scanners expect dark modules on a
 * light ground, and an inverted code is one many cannot read.
 */
@Composable
fun QrDialog(layout: ControllerLayout, payload: String, onDismiss: () -> Unit) {
    // Medium error correction: survives glare and a slightly smudged screen
    // without making the code so dense it needs a steady hand.
    val matrix = remember(payload) {
        runCatching {
            QRCodeWriter().encode(
                payload,
                BarcodeFormat.QR_CODE,
                0,
                0,
                mapOf(EncodeHintType.ERROR_CORRECTION to ErrorCorrectionLevel.M, EncodeHintType.MARGIN to 0),
            )
        }.getOrNull()
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(layout.name, maxLines = 1, overflow = TextOverflow.Ellipsis) },
        text = {
            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                if (matrix == null) {
                    Text("This layout is too big for a QR code. Share it as a file instead.")
                } else {
                    QrCode(matrix)
                    Spacer(Modifier.height(12.dp))
                    Text(
                        "On the other phone, open droidOSS and tap Scan QR under Layouts.",
                        style = MaterialTheme.typography.bodySmall,
                        textAlign = TextAlign.Center,
                    )
                }
            }
        },
        confirmButton = { TextButton(onClick = onDismiss) { Text("Done") } },
    )
}

@Composable
private fun QrCode(matrix: BitMatrix) {
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .aspectRatio(1f)
            .background(Color.White, RoundedCornerShape(8.dp))
            // The quiet zone: scanners need clear space around the code.
            .padding(16.dp),
    ) {
        Canvas(modifier = Modifier.fillMaxWidth().aspectRatio(1f)) {
            val cell = size.width / matrix.width
            for (y in 0 until matrix.height) {
                for (x in 0 until matrix.width) {
                    if (matrix[x, y]) {
                        drawRect(
                            color = Color.Black,
                            topLeft = Offset(x * cell, y * cell),
                            // A hair over one cell, so no seams show between modules.
                            size = Size(cell + 0.5f, cell + 0.5f),
                        )
                    }
                }
            }
        }
    }
}

/**
 * Confirm a layout someone sent. With room it is simply added; with all
 * [MAX_LAYOUTS] in use, the person picks which custom layout it replaces —
 * nothing is overwritten without them choosing it.
 */
@Composable
fun ImportDialog(
    name: String,
    controls: Int,
    hasRoom: Boolean,
    replaceable: List<ControllerLayout>,
    onAdd: () -> Unit,
    onReplace: (String) -> Unit,
    onDismiss: () -> Unit,
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Import \"$name\"?", maxLines = 2, overflow = TextOverflow.Ellipsis) },
        text = {
            Column(
                modifier = Modifier
                    .heightIn(max = 360.dp)
                    .verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                Text("A layout with $controls controls.")
                if (!hasRoom) {
                    Text(
                        "You already have $MAX_LAYOUTS layouts. Pick one to replace:",
                        color = MaterialTheme.colorScheme.error,
                    )
                    for (layout in replaceable) {
                        OutlinedButton(
                            onClick = { onReplace(layout.id) },
                            modifier = Modifier.fillMaxWidth(),
                        ) { Text("Replace \"${layout.name}\"", maxLines = 1, overflow = TextOverflow.Ellipsis) }
                    }
                }
            }
        },
        confirmButton = {
            if (hasRoom) TextButton(onClick = onAdd) { Text("Import") }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}

/** Shown when an incoming file or code could not be used. */
@Composable
fun ImportErrorDialog(message: String, onDismiss: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Couldn't import") },
        text = { Text(message) },
        confirmButton = { TextButton(onClick = onDismiss) { Text("OK") } },
    )
}

@Composable
fun DeleteDialog(name: String, onConfirm: () -> Unit, onDismiss: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Delete \"$name\"?", maxLines = 2, overflow = TextOverflow.Ellipsis) },
        text = { Text("This can't be undone. Share or save it first if you might want it back.") },
        confirmButton = {
            TextButton(onClick = onConfirm) {
                Text("Delete", color = MaterialTheme.colorScheme.error)
            }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}
