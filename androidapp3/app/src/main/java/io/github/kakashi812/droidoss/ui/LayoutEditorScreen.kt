package io.github.kakashi812.droidoss.ui

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Slider
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.toMutableStateList
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import io.github.kakashi812.droidoss.layout.ControlElement
import io.github.kakashi812.droidoss.layout.ControllerLayout
import io.github.kakashi812.droidoss.layout.defaultLayout
import io.github.kakashi812.droidoss.layout.with
import kotlin.math.max

/**
 * Arrange one layout: move, resize and fade the controls, then Done.
 *
 * The canvas is **full-screen and drawn with the same [drawControl] the live pad
 * uses**, so a control looks in the editor exactly as it will in a game — you are
 * arranging at the true play size, on the real device, with your real thumbs,
 * which is the only thing that makes touch ergonomics honest. Editing chrome
 * floats over the middle-bottom strip, which the default layout deliberately
 * leaves empty (that is where the phone rests in your palms).
 *
 * Overlap is allowed while arranging but blocks saving: any two controls whose
 * circles intersect glow red and **Done is disabled** until they are separated.
 *
 * The caller is responsible for landscape + immersive; this composable assumes it
 * is shown the same way the pad is.
 */
@Composable
fun LayoutEditorScreen(
    initial: ControllerLayout,
    onSave: (ControllerLayout) -> Unit,
    onCancel: () -> Unit,
    modifier: Modifier = Modifier,
) {
    // The working copy. A SnapshotStateList so a move/resize recomposes the canvas
    // without re-running the pointer loop (that loop is keyed on size only, never
    // on the elements — keying it on the elements would restart the gesture the
    // instant a drag moved anything).
    val elements = remember(initial) { initial.elements.toMutableStateList() }
    var selectedId by remember(initial) { mutableStateOf<String?>(null) }

    // Default geometry by id, for the per-control Reset.
    val defaults = remember { defaultLayout().elements.associateBy { it.id } }

    BoxWithConstraints(modifier = modifier.fillMaxSize().background(Color(0xFF101014))) {
        val w = constraints.maxWidth.toFloat()
        val h = constraints.maxHeight.toFloat()

        // Resolved every recomposition from the live elements, so the drawing and
        // overlap check always reflect the latest edit. Cheap: ~15 controls, and
        // only while editing — never on a send path.
        val placed = elements.place(w, h)
        val labels = rememberControlLabels(placed)
        val overlapping = remember(placed) { overlappingIds(placed) }
        val hasOverlap = overlapping.isNotEmpty()

        // ── mutation helpers (close over w, h, elements, selectedId) ──────────
        fun updateElement(id: String, transform: (ControlElement) -> ControlElement) {
            val index = elements.indexOfFirst { it.id == id }
            if (index >= 0) elements[index] = transform(elements[index])
        }

        // Keep a control's whole circle on screen for a given size.
        fun clampCentre(e: ControlElement, cxPx: Float, cyPx: Float, sizeFrac: Float): ControlElement {
            val r = sizeFrac * w / 2f
            val cx = cxPx.coerceIn(r, max(r, w - r))
            val cy = cyPx.coerceIn(r, max(r, h - r))
            return e.with(x = cx / w, y = cy / h, size = sizeFrac)
        }

        fun moveSelectedBy(dx: Float, dy: Float) {
            val id = selectedId ?: return
            updateElement(id) { e -> clampCentre(e, e.x * w + dx, e.y * h + dy, e.size) }
        }

        fun resetSelected() {
            val id = selectedId ?: return
            defaults[id]?.let { def -> updateElement(id) { def } }
        }

        // ── the canvas: draw + gestures ───────────────────────────────────────
        Canvas(
            modifier = Modifier
                .fillMaxSize()
                // Keyed on size only. Never on `elements`, or every move would
                // cancel the coroutine mid-drag.
                .pointerInput(w, h) {
                    awaitPointerEventScope {
                        // Gesture-local state, retained across events in this loop.
                        var lastPos = Offset.Zero
                        var dragging = false
                        var downOnControl = false
                        var movedFar = false
                        var prevCount = 0

                        while (true) {
                            val event = awaitPointerEvent()
                            val down = event.changes.filter { it.pressed }

                            when (down.size) {
                                1 -> {
                                    val c = down[0]
                                    if (prevCount != 1) {
                                        // Single-finger gesture (re)starts.
                                        lastPos = c.position
                                        val hit = elements.place(w, h)
                                            .lastOrNull { it.contains(c.position) }
                                        downOnControl = hit != null
                                        movedFar = false
                                        if (hit != null) {
                                            selectedId = hit.element.id
                                            dragging = true
                                        } else {
                                            dragging = false
                                        }
                                    } else if (dragging) {
                                        val delta = c.position - lastPos
                                        if (delta.getDistance() > TAP_SLOP) movedFar = true
                                        moveSelectedBy(delta.x, delta.y)
                                        lastPos = c.position
                                    }
                                    c.consume()
                                }

                                0 -> {
                                    // All fingers up. A tap on empty space deselects;
                                    // a tap on a control leaves it selected.
                                    if (prevCount == 1 && !movedFar && !downOnControl) {
                                        selectedId = null
                                    }
                                    dragging = false
                                }

                                else -> {
                                    // Resizing is done with the slider, not by
                                    // pinching. A stray second finger just cancels
                                    // the current drag so it can't fling a control.
                                    dragging = false
                                    movedFar = true
                                    down.forEach { it.consume() }
                                }
                            }
                            prevCount = down.size
                        }
                    }
                }
        ) {
            for (p in placed) {
                drawControl(p, NO_PRESSED, NO_KNOBS, labels)
                if (p.element.id in overlapping) {
                    drawCircle(
                        color = Color(0xFFE53935),
                        radius = p.radius + SELECTION_GAP,
                        center = p.centre,
                        style = Stroke(width = SELECTION_STROKE),
                    )
                }
            }
            // Selection ring last, so it reads on top even when it overlaps a red one.
            placed.firstOrNull { it.element.id == selectedId }?.let { sel ->
                drawCircle(
                    color = Color(0xFF6EA8FF),
                    radius = sel.radius + SELECTION_GAP,
                    center = sel.centre,
                    style = Stroke(width = SELECTION_STROKE),
                )
            }
        }

        // ── overlap warning, top-centre ────────────────────────────────────────
        if (hasOverlap) {
            Surface(
                color = Color(0xCCB00020),
                shape = RoundedCornerShape(10.dp),
                modifier = Modifier
                    .align(Alignment.TopCenter)
                    .padding(top = 12.dp),
            ) {
                Text(
                    "Move the red controls apart to finish",
                    color = Color.White,
                    modifier = Modifier.padding(horizontal = 14.dp, vertical = 6.dp),
                )
            }
        }

        // ── toolbar, floating over the empty middle-bottom strip ────────────────
        val selected = elements.firstOrNull { it.id == selectedId }
        Surface(
            color = Color(0xE61A1A22),
            shape = RoundedCornerShape(18.dp),
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .padding(bottom = 14.dp),
        ) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(10.dp),
                modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp),
            ) {
                TextButton(onClick = onCancel) { Text("Cancel", color = Color.White) }

                if (selected != null) {
                    SliderField(
                        label = "Size",
                        value = selected.size,
                        range = MIN_SIZE..MAX_SIZE,
                        onValueChange = { v ->
                            updateElement(selected.id) { clampCentre(it, it.x * w, it.y * h, v) }
                        },
                    )
                    SliderField(
                        label = "Fade",
                        value = selected.opacity,
                        // Never fully transparent — a control you cannot see is a
                        // control you cannot get back.
                        range = 0.2f..1f,
                        onValueChange = { v -> updateElement(selected.id) { it.with(opacity = v) } },
                    )
                    TextButton(onClick = { resetSelected() }) { Text("Reset", color = Color.White) }
                } else {
                    Text(
                        "Tap a control, then drag to move it",
                        color = Color(0xFFB0B0B8),
                    )
                }

                Button(
                    onClick = { onSave(initial.copy(elements = elements.toList())) },
                    enabled = !hasOverlap,
                ) {
                    Text("Done", fontWeight = FontWeight.SemiBold)
                }
            }
        }
    }
}

/** Ids of every control whose circle intersects another's. Exact — all controls
 *  are circles of radius [Placed.radius]. */
private fun overlappingIds(placed: List<Placed>): Set<String> {
    val hit = mutableSetOf<String>()
    for (i in placed.indices) {
        for (j in i + 1 until placed.size) {
            val a = placed[i]
            val b = placed[j]
            if ((a.centre - b.centre).getDistance() < a.radius + b.radius) {
                hit += a.element.id
                hit += b.element.id
            }
        }
    }
    return hit
}

/** A compact labelled slider for the editor toolbar. */
@Composable
private fun SliderField(
    label: String,
    value: Float,
    range: ClosedFloatingPointRange<Float>,
    onValueChange: (Float) -> Unit,
) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Text(label, color = Color.White, style = MaterialTheme.typography.labelMedium)
        Spacer(Modifier.width(6.dp))
        Slider(
            value = value,
            onValueChange = onValueChange,
            valueRange = range,
            modifier = Modifier.width(130.dp),
        )
    }
}

private val NO_PRESSED = emptyMap<String, Boolean>()
private val NO_KNOBS = emptyMap<String, Offset>()

private const val TAP_SLOP = 12f
private const val MIN_SIZE = 0.04f
private const val MAX_SIZE = 0.30f
private const val SELECTION_GAP = 6f
private const val SELECTION_STROKE = 4f
