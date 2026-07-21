/*
 * Pointer-handling approach derived from PadConnect's GPEmulationScreen.
 * Copyright (C) 2026 Ishan
 * Copyright (C) 2026 droidOSS contributors
 *
 * This program is free software: you can redistribute it and/or modify it under
 * the terms of the GNU General Public License as published by the Free Software
 * Foundation, version 3 only.
 *
 * This program is distributed without any warranty. See the GNU General Public
 * License for more details.
 *
 * MODIFIED FROM THE ORIGINAL. Theirs established the shape: read the raw pointer
 * stream, own controls by PointerId, hit-test yourself, and support slide-off.
 * Changed here: two sticks rather than one hardcoded to the left axis, a real
 * D-pad, radial deadzones, full-range triggers, and pointer release driven by
 * which ids are still present so a cancelled gesture cannot leave a control held.
 */

package io.github.kakashi812.droidoss.ui

import android.view.HapticFeedbackConstants
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.PointerId
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.TextLayoutResult
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.foundation.Canvas
import io.github.kakashi812.droidoss.layout.ButtonElement
import io.github.kakashi812.droidoss.layout.ControlElement
import io.github.kakashi812.droidoss.layout.ControllerLayout
import io.github.kakashi812.droidoss.layout.DpadElement
import io.github.kakashi812.droidoss.layout.Stick
import io.github.kakashi812.droidoss.layout.StickElement
import io.github.kakashi812.droidoss.layout.Trigger
import io.github.kakashi812.droidoss.layout.TriggerElement
import io.github.kakashi812.droidoss.protocol.GamepadButton
import io.github.kakashi812.droidoss.protocol.PadState
import io.github.kakashi812.droidoss.transport.UdpTransport
import kotlin.math.roundToInt

/** A control resolved to pixels for the current screen size. */
private data class Placed(
    val element: ControlElement,
    val centre: Offset,
    val radius: Float,
) {
    fun contains(point: Offset): Boolean =
        (point - centre).getDistanceSquared() <= radius * radius
}

/**
 * The gamepad.
 *
 * **No touch-consuming components anywhere.** A `Button` is built around "one
 * finger taps, then lets go" and cannot express "held while two other things are
 * held", which is the entire job of a gamepad. Everything here reads the raw
 * pointer stream and hit-tests by hand.
 *
 * Drawing and sending are decoupled: this writes into the transport's shared
 * state, and the transport's own thread reads it at a fixed 125 Hz whatever the
 * frame rate happens to be doing.
 */
@Composable
fun PadScreen(
    transport: UdpTransport?,
    layout: ControllerLayout,
    modifier: Modifier = Modifier,
) {
    val view = LocalView.current

    // Which control each finger owns. Decided at touch-down and kept until that
    // finger lifts -- never re-decided mid-gesture for sticks, or a thumb that
    // strays outside the stick would be stolen by whatever is underneath.
    val owners = remember { mutableStateMapOf<PointerId, Placed>() }

    // Visual state. Separate from the wire state so drawing never reaches into
    // the transport's lock.
    val pressed = remember { mutableStateMapOf<String, Boolean>() }
    val knobs = remember { mutableStateMapOf<String, Offset>() }

    BoxWithConstraints(modifier = modifier.fillMaxSize().background(Color(0xFF101014))) {
        val widthPx = constraints.maxWidth.toFloat()
        val heightPx = constraints.maxHeight.toFloat()

        // Resolved once per size change, not per frame or per touch event.
        val placed = remember(layout, widthPx, heightPx) {
            layout.elements.map { element ->
                Placed(
                    element = element,
                    centre = Offset(element.x * widthPx, element.y * heightPx),
                    radius = element.size * widthPx / 2f,
                )
            }
        }

        // Text is measured once per layout change and cached. Measuring inside
        // the draw lambda would allocate on every frame, which is the same
        // discipline the 125 Hz send path follows.
        val textMeasurer = rememberTextMeasurer()
        val density = LocalDensity.current
        val labels = remember(placed, textMeasurer, density) {
            buildMap {
                for (p in placed) {
                    val text = when (val e = p.element) {
                        is ButtonElement -> e.label
                        is TriggerElement -> e.label
                        else -> null
                    } ?: continue

                    put(
                        p.element.id,
                        textMeasurer.measure(
                            text = AnnotatedString(text),
                            style = TextStyle(
                                fontSize = with(density) { (p.radius * LABEL_FRACTION).toSp() },
                                fontWeight = FontWeight.Medium,
                            ),
                        ),
                    )
                }
            }
        }

        Canvas(
            modifier = Modifier
                .fillMaxSize()
                .pointerInput(placed, transport) {
                    awaitPointerEventScope {
                        while (true) {
                            val event = awaitPointerEvent()

                            for (change in event.changes) {
                                if (change.pressed && !owners.containsKey(change.id)) {
                                    // Touch-down: claim whatever is under it.
                                    val hit = placed.firstOrNull {
                                        it.element.enabled && it.contains(change.position)
                                    } ?: continue

                                    owners[change.id] = hit
                                    onDown(transport, hit, change.position, pressed, knobs)
                                    view.performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY)
                                } else if (change.pressed) {
                                    val owned = owners[change.id] ?: continue
                                    onMove(
                                        transport, owned, change.position, placed,
                                        pressed, knobs, owners, change.id, view,
                                    )
                                }
                            }

                            // Release anything whose finger is no longer down.
                            // Driving this from "which ids are still pressed"
                            // rather than from up-events means a cancelled
                            // gesture -- the notification shade opening
                            // mid-press -- cannot leave a control stuck on.
                            val stillDown = event.changes
                                .filter { it.pressed }
                                .map { it.id }
                                .toSet()

                            val lifted = owners.keys.filter { it !in stillDown }
                            for (id in lifted) {
                                owners.remove(id)?.let { onUp(transport, it, pressed, knobs) }
                            }
                        }
                    }
                }
        ) {
            for (p in placed) drawControl(p, pressed, knobs, labels)
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Touch
// ─────────────────────────────────────────────────────────────────────────────

private fun onDown(
    transport: UdpTransport?,
    placed: Placed,
    position: Offset,
    pressed: MutableMap<String, Boolean>,
    knobs: MutableMap<String, Offset>,
) {
    when (val element = placed.element) {
        is ButtonElement -> {
            pressed[element.id] = true
            transport?.update { setButton(element.mask, true) }
        }

        is TriggerElement -> {
            pressed[element.id] = true
            transport?.update { setTrigger(element.trigger, FULL_TRAVEL) }
        }

        is StickElement -> applyStick(transport, placed, element, position, knobs)
        is DpadElement -> applyDpad(transport, placed, element, position, pressed, knobs)
    }
}

private fun onMove(
    transport: UdpTransport?,
    owned: Placed,
    position: Offset,
    placed: List<Placed>,
    pressed: MutableMap<String, Boolean>,
    knobs: MutableMap<String, Offset>,
    owners: MutableMap<PointerId, Placed>,
    id: PointerId,
    view: android.view.View,
) {
    when (val element = owned.element) {
        // Sticks and the D-pad keep their finger no matter how far it strays.
        // Letting go at the edge would make a hard-left input drop out exactly
        // when you are pushing hardest.
        is StickElement -> applyStick(transport, owned, element, position, knobs)
        is DpadElement -> applyDpad(transport, owned, element, position, pressed, knobs)

        // Buttons slide off. A thumb rolling from A onto B should release A and
        // press B -- PadConnect gets this right and it matters for feel.
        is ButtonElement, is TriggerElement -> {
            if (owned.contains(position)) return

            val next = placed.firstOrNull {
                it.element.enabled && it.contains(position) &&
                    (it.element is ButtonElement || it.element is TriggerElement)
            }

            onUp(transport, owned, pressed, knobs)

            if (next == null) {
                owners.remove(id)
            } else {
                owners[id] = next
                onDown(transport, next, position, pressed, knobs)
                view.performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY)
            }
        }
    }
}

private fun onUp(
    transport: UdpTransport?,
    placed: Placed,
    pressed: MutableMap<String, Boolean>,
    knobs: MutableMap<String, Offset>,
) {
    when (val element = placed.element) {
        is ButtonElement -> {
            pressed[element.id] = false
            transport?.update { setButton(element.mask, false) }
        }

        is TriggerElement -> {
            pressed[element.id] = false
            transport?.update { setTrigger(element.trigger, 0) }
        }

        is StickElement -> {
            knobs[element.id] = Offset.Zero
            transport?.update { setStick(element.stick, 0, 0) }
        }

        is DpadElement -> {
            knobs[element.id] = Offset.Zero
            pressed[element.id] = false
            transport?.update {
                setButton(GamepadButton.DPAD_UP, false)
                setButton(GamepadButton.DPAD_DOWN, false)
                setButton(GamepadButton.DPAD_LEFT, false)
                setButton(GamepadButton.DPAD_RIGHT, false)
            }
        }
    }
}

/**
 * Maps a thumb position to stick axes.
 *
 * The deadzone is **radial**, not per-axis. Deadzoning each axis independently
 * carves a square hole out of the centre, so a gentle diagonal has one axis
 * suppressed and the other not, and every diagonal snaps toward the compass
 * points.
 *
 * Y is negated: screens count downward, XInput counts upward.
 */
private fun applyStick(
    transport: UdpTransport?,
    placed: Placed,
    element: StickElement,
    position: Offset,
    knobs: MutableMap<String, Offset>,
) {
    val delta = position - placed.centre
    val distance = delta.getDistance()

    val clamped = if (distance > placed.radius) delta * (placed.radius / distance) else delta
    knobs[element.id] = clamped

    val magnitude = (clamped.getDistance() / placed.radius).coerceIn(0f, 1f)

    val scaled = if (magnitude <= STICK_DEADZONE) {
        0f
    } else {
        // Rescale so the axis still reaches a full 1.0 at the rim. Without this
        // the stick would top out at (1 - deadzone) and never quite run.
        (magnitude - STICK_DEADZONE) / (1f - STICK_DEADZONE)
    }

    val unit = if (distance > 0f) clamped / clamped.getDistance() else Offset.Zero
    val x = (unit.x * scaled * Short.MAX_VALUE).roundToInt().coerceIn(MIN_AXIS, MAX_AXIS)
    val y = (-unit.y * scaled * Short.MAX_VALUE).roundToInt().coerceIn(MIN_AXIS, MAX_AXIS)

    transport?.update { setStick(element.stick, x.toShort(), y.toShort()) }
}

/** Four bits, from which way the thumb is pushed. Diagonals press two. */
private fun applyDpad(
    transport: UdpTransport?,
    placed: Placed,
    element: DpadElement,
    position: Offset,
    pressed: MutableMap<String, Boolean>,
    knobs: MutableMap<String, Offset>,
) {
    val delta = position - placed.centre
    val distance = delta.getDistance()

    val clamped = if (distance > placed.radius) delta * (placed.radius / distance) else delta
    knobs[element.id] = clamped
    pressed[element.id] = true

    val threshold = placed.radius * element.deadzone

    // Compared independently so a diagonal genuinely presses two directions,
    // which is what platformers and fighting games expect.
    val up = delta.y < -threshold
    val down = delta.y > threshold
    val left = delta.x < -threshold
    val right = delta.x > threshold

    transport?.update {
        setButton(GamepadButton.DPAD_UP, up)
        setButton(GamepadButton.DPAD_DOWN, down)
        setButton(GamepadButton.DPAD_LEFT, left)
        setButton(GamepadButton.DPAD_RIGHT, right)
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Drawing
// ─────────────────────────────────────────────────────────────────────────────

private fun DrawScope.drawControl(
    placed: Placed,
    pressed: Map<String, Boolean>,
    knobs: Map<String, Offset>,
    labels: Map<String, TextLayoutResult>,
) {
    val element = placed.element
    val isDown = pressed[element.id] == true
    val alpha = element.opacity * (if (element.enabled) 1f else 0.3f)

    when (element) {
        is StickElement -> {
            drawCircle(
                color = Color.White.copy(alpha = alpha * 0.10f),
                radius = placed.radius,
                center = placed.centre,
            )
            drawCircle(
                color = Color.White.copy(alpha = alpha * 0.35f),
                radius = placed.radius,
                center = placed.centre,
                style = Stroke(width = STROKE_WIDTH),
            )

            // The knob tracking the thumb is most of what replaces the missing
            // tactile feel -- you can see exactly what the stick is reporting.
            val knob = knobs[element.id] ?: Offset.Zero
            drawCircle(
                color = Color.White.copy(alpha = alpha * 0.70f),
                radius = placed.radius * KNOB_FRACTION,
                center = placed.centre + knob,
            )
        }

        // A cross, not a circle with a knob. Drawing it like a stick made it
        // read as a third stick, which is exactly what it must never look like:
        // a D-pad promises four discrete directions and a stick promises smooth
        // travel, and the shape is the only thing telling you which you have.
        is DpadElement -> drawDpad(placed, element, knobs[element.id] ?: Offset.Zero, alpha)

        is ButtonElement, is TriggerElement -> {
            drawCircle(
                color = Color.White.copy(alpha = if (isDown) alpha * 0.80f else alpha * 0.15f),
                radius = placed.radius,
                center = placed.centre,
            )
            drawCircle(
                color = Color.White.copy(alpha = alpha * 0.5f),
                radius = placed.radius,
                center = placed.centre,
                style = Stroke(width = STROKE_WIDTH),
            )
        }
    }

    // Labels last, so they sit on top of the pressed fill.
    labels[element.id]?.let { measured ->
        drawText(
            textLayoutResult = measured,
            color = Color.White.copy(alpha = alpha * if (isDown) 0.95f else 0.7f),
            topLeft = placed.centre - Offset(
                measured.size.width / 2f,
                measured.size.height / 2f,
            ),
        )
    }
}

/**
 * A four-armed cross whose arms light up individually.
 *
 * Each arm is highlighted from the knob offset using the same threshold the
 * touch handler uses, so what you see is exactly what is being sent — including
 * a diagonal lighting two arms at once.
 */
private fun DrawScope.drawDpad(
    placed: Placed,
    element: DpadElement,
    knob: Offset,
    alpha: Float,
) {
    val r = placed.radius
    val arm = r * 0.62f          // length of each arm from centre
    val thickness = r * 0.52f
    val threshold = r * element.deadzone

    val up = knob.y < -threshold
    val down = knob.y > threshold
    val left = knob.x < -threshold
    val right = knob.x > threshold

    fun armColour(active: Boolean) =
        Color.White.copy(alpha = alpha * if (active) 0.80f else 0.15f)

    // Vertical and horizontal bars, drawn as two rounded rectangles crossing at
    // the centre. Each half is filled separately so one direction can light
    // without the other.
    drawRoundRect(                                  // up
        color = armColour(up),
        topLeft = placed.centre + Offset(-thickness / 2f, -arm),
        size = Size(thickness, arm),
        cornerRadius = CornerRadius(CORNER, CORNER),
    )
    drawRoundRect(                                  // down
        color = armColour(down),
        topLeft = placed.centre + Offset(-thickness / 2f, 0f),
        size = Size(thickness, arm),
        cornerRadius = CornerRadius(CORNER, CORNER),
    )
    drawRoundRect(                                  // left
        color = armColour(left),
        topLeft = placed.centre + Offset(-arm, -thickness / 2f),
        size = Size(arm, thickness),
        cornerRadius = CornerRadius(CORNER, CORNER),
    )
    drawRoundRect(                                  // right
        color = armColour(right),
        topLeft = placed.centre + Offset(0f, -thickness / 2f),
        size = Size(arm, thickness),
        cornerRadius = CornerRadius(CORNER, CORNER),
    )

    // Outline of the whole cross, so it reads as one control at rest.
    drawRoundRect(
        color = Color.White.copy(alpha = alpha * 0.30f),
        topLeft = placed.centre + Offset(-thickness / 2f, -arm),
        size = Size(thickness, arm * 2f),
        cornerRadius = CornerRadius(CORNER, CORNER),
        style = Stroke(width = STROKE_WIDTH),
    )
    drawRoundRect(
        color = Color.White.copy(alpha = alpha * 0.30f),
        topLeft = placed.centre + Offset(-arm, -thickness / 2f),
        size = Size(arm * 2f, thickness),
        cornerRadius = CornerRadius(CORNER, CORNER),
        style = Stroke(width = STROKE_WIDTH),
    )
}

private const val FULL_TRAVEL = 255
private const val STICK_DEADZONE = 0.12f
private const val KNOB_FRACTION = 0.42f
private const val STROKE_WIDTH = 3f
private const val CORNER = 8f
private const val LABEL_FRACTION = 0.62f
private const val MIN_AXIS = -32768
private const val MAX_AXIS = 32767

// ─────────────────────────────────────────────────────────────────────────────
// Layout enums -> wire fields
//
// These live here, not on PadState, so that `protocol` stays independent of
// `layout`. The protocol package should be usable by anything that speaks the
// wire format, including code that has never heard of an on-screen control.
// ─────────────────────────────────────────────────────────────────────────────

private fun PadState.setStick(stick: Stick, x: Short, y: Short) {
    when (stick) {
        Stick.LEFT -> { thumbLX = x; thumbLY = y }
        Stick.RIGHT -> { thumbRX = x; thumbRY = y }
    }
}

private fun PadState.setTrigger(trigger: Trigger, value: Int) {
    when (trigger) {
        Trigger.LEFT -> leftTrigger = value
        Trigger.RIGHT -> rightTrigger = value
    }
}
