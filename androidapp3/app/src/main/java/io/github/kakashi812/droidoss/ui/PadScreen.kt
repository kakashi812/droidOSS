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
 *
 * Drawing lives in PadDrawing.kt, shared with the layout editor.
 */

package io.github.kakashi812.droidoss.ui

import android.view.HapticFeedbackConstants
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.PointerId
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalView
import io.github.kakashi812.droidoss.layout.ButtonElement
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
 * frame rate happens to be doing. A **null** transport is legal — the pad then
 * draws and responds to touch but sends nowhere, which is exactly what the "View"
 * preview on the home screen uses.
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
            layout.elements.place(widthPx, heightPx)
        }
        val labels = rememberControlLabels(placed)

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

private const val FULL_TRAVEL = 255
private const val STICK_DEADZONE = 0.12f
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
