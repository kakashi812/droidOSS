/*
 * Shared drawing for the gamepad, used by both the live pad ([PadScreen]) and the
 * layout editor ([LayoutEditorScreen]). Keeping it in one place is what guarantees
 * a control looks identical while you are arranging it and while you are playing.
 *
 * Drawing primitives derived from PadConnect's GPEmulationScreen.
 * Copyright (C) 2026 Ishan
 * Copyright (C) 2026 droidOSS contributors
 *
 * This program is free software: you can redistribute it and/or modify it under
 * the terms of the GNU General Public License as published by the Free Software
 * Foundation, version 3 only.
 *
 * This program is distributed without any warranty. See the GNU General Public
 * License for more details.
 */

package io.github.kakashi812.droidoss.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.DrawScope
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.TextLayoutResult
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
import io.github.kakashi812.droidoss.layout.ButtonElement
import io.github.kakashi812.droidoss.layout.ControlElement
import io.github.kakashi812.droidoss.layout.DpadElement
import io.github.kakashi812.droidoss.layout.StickElement
import io.github.kakashi812.droidoss.layout.TriggerElement

/** A control resolved to pixels for the current screen size. */
internal data class Placed(
    val element: ControlElement,
    val centre: Offset,
    val radius: Float,
) {
    fun contains(point: Offset): Boolean =
        (point - centre).getDistanceSquared() <= radius * radius
}

/**
 * Resolve fractional layout elements to pixel positions for a given size.
 *
 * Radius is a fraction of **width** on both axes, so a control stays round rather
 * than stretching with the aspect ratio.
 */
internal fun List<ControlElement>.place(widthPx: Float, heightPx: Float): List<Placed> =
    map { element ->
        Placed(
            element = element,
            centre = Offset(element.x * widthPx, element.y * heightPx),
            radius = element.size * widthPx / 2f,
        )
    }

/**
 * Measure and cache each control's label once per layout/size change.
 *
 * Measuring inside the draw lambda would allocate on every frame — the same
 * discipline the 125 Hz send path follows.
 */
@Composable
internal fun rememberControlLabels(placed: List<Placed>): Map<String, TextLayoutResult> {
    val textMeasurer = rememberTextMeasurer()
    val density = LocalDensity.current
    return remember(placed, textMeasurer, density) {
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
}

internal fun DrawScope.drawControl(
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
internal fun DrawScope.drawDpad(
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

internal const val STROKE_WIDTH = 3f
internal const val CORNER = 8f
internal const val KNOB_FRACTION = 0.42f
internal const val LABEL_FRACTION = 0.62f
