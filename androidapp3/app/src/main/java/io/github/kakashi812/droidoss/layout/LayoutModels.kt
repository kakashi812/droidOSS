/*
 * Derived from PadConnect's LayoutModels.
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
 * MODIFIED FROM THE ORIGINAL. The shape is theirs — controls as a list of data
 * with fractional coordinates, which is what makes a layout editor cheap later.
 * Two things are new, and both are the point:
 *
 *   1. [StickElement] carries a [Stick] field. PadConnect's stick element does
 *      not, so its draw code hardcodes the left axis and a right stick is not
 *      expressible without a refactor.
 *   2. [DpadElement] exists at all. PadConnect has no D-pad — its "dpad" is an
 *      analog stick that replaced one — which quietly abandons platformers, 2D
 *      and emulation.
 */

package io.github.kakashi812.droidoss.layout

import io.github.kakashi812.droidoss.protocol.GamepadButton

/** Which physical stick a [StickElement] drives. */
enum class Stick { LEFT, RIGHT }

/** Which trigger a [TriggerElement] drives. */
enum class Trigger { LEFT, RIGHT }

/**
 * One control on the pad.
 *
 * **Controls are data, not code.** Everything needed to draw and hit-test a
 * control lives in these fields, which is what makes the layout editor nearly
 * free later: dragging a control is editing [x] and [y].
 *
 * Coordinates are **fractions of the container**, 0..1, and refer to the
 * *centre* of the control. Never pixels — the same layout has to land correctly
 * on a 5" 720p phone and a 12" tablet.
 */
sealed interface ControlElement {
    val id: String
    val x: Float
    val y: Float

    /** Diameter or width, as a fraction of container **width** on both axes so
     *  controls stay round rather than stretching with the aspect ratio. */
    val size: Float

    val opacity: Float

    /** A disabled control is drawn faintly and ignores touches. */
    val enabled: Boolean
}

/** An analog stick. [stick] is the field PadConnect is missing. */
data class StickElement(
    override val id: String,
    override val x: Float,
    override val y: Float,
    override val size: Float,
    override val opacity: Float = 0.8f,
    override val enabled: Boolean = true,
    val stick: Stick,
) : ControlElement

/**
 * A real four-way D-pad emitting protocol bits 0–3.
 *
 * Not an analog stick wearing a hat. Platformers, 2D games and emulation are
 * this project's best use case and they are played with a D-pad; a stick feels
 * mushy for them and misses inputs a D-pad lands cleanly.
 */
data class DpadElement(
    override val id: String,
    override val x: Float,
    override val y: Float,
    override val size: Float,
    override val opacity: Float = 0.8f,
    override val enabled: Boolean = true,

    /** Fraction of the way from centre to edge before a direction registers. */
    val deadzone: Float = 0.25f,
) : ControlElement

/** Any button that maps to a single bit of the button mask. */
data class ButtonElement(
    override val id: String,
    override val x: Float,
    override val y: Float,
    override val size: Float,
    override val opacity: Float = 0.85f,
    override val enabled: Boolean = true,
    val mask: Int,
    val label: String,
) : ControlElement

/**
 * A trigger, which is an axis rather than a bit.
 *
 * B4 emits full travel (255) on touch. **Not 100** — PadConnect uses 100 out of
 * 255, which is both digital *and* only 39% of the range, so a racing game reads
 * a permanently part-pressed throttle. Real analog travel from slide distance is
 * a B5 job; until then, full-on beats wrong-on.
 */
data class TriggerElement(
    override val id: String,
    override val x: Float,
    override val y: Float,
    override val size: Float,
    override val opacity: Float = 0.85f,
    override val enabled: Boolean = true,
    val trigger: Trigger,
    val label: String,
) : ControlElement

/** A complete pad. */
data class ControllerLayout(
    val name: String,
    val elements: List<ControlElement>,
)

/**
 * The layout you get before touching anything.
 *
 * Ergonomics, from the design notes: thumbs pivot in arcs from the bottom
 * corners, so movement controls sit low and outboard. Nothing lives in the
 * middle-bottom — that is where the phone rests in your palms. Face buttons are
 * a diamond bottom-right; shoulders and triggers run along the top edge where
 * index fingers already are.
 *
 * Both sticks *and* a D-pad are present from the first commit. That is the whole
 * reason this model exists in this shape.
 */
fun defaultLayout(): ControllerLayout = ControllerLayout(
    name = "Default",
    elements = listOf(
        // ── top edge: index fingers ──────────────────────────────────────
        TriggerElement(id = "lt", x = 0.07f, y = 0.13f, size = 0.10f,
            trigger = Trigger.LEFT, label = "LT"),
        ButtonElement(id = "lb", x = 0.19f, y = 0.11f, size = 0.09f,
            mask = GamepadButton.LEFT_SHOULDER, label = "LB"),
        ButtonElement(id = "rb", x = 0.81f, y = 0.11f, size = 0.09f,
            mask = GamepadButton.RIGHT_SHOULDER, label = "RB"),
        TriggerElement(id = "rt", x = 0.93f, y = 0.13f, size = 0.10f,
            trigger = Trigger.RIGHT, label = "RT"),

        // ── centre top: rarely pressed, deliberately out of the way ──────
        ButtonElement(id = "back", x = 0.44f, y = 0.11f, size = 0.06f,
            mask = GamepadButton.BACK, label = "⧉"),
        ButtonElement(id = "start", x = 0.56f, y = 0.11f, size = 0.06f,
            mask = GamepadButton.START, label = "≡"),

        // ── left thumb ───────────────────────────────────────────────────
        StickElement(id = "stick_left", x = 0.14f, y = 0.55f, size = 0.17f,
            stick = Stick.LEFT),
        DpadElement(id = "dpad", x = 0.32f, y = 0.82f, size = 0.17f),

        // Stick clicks. These are buttons 9 and 10 in joy.cpl, and without them
        // a great many games lose sprint, crouch or melee entirely. Small and
        // set inboard of the sticks so a thumb cannot catch one mid-sweep.
        ButtonElement(id = "l3", x = 0.05f, y = 0.86f, size = 0.055f,
            mask = GamepadButton.LEFT_THUMB, label = "L3"),
        ButtonElement(id = "r3", x = 0.95f, y = 0.86f, size = 0.055f,
            mask = GamepadButton.RIGHT_THUMB, label = "R3"),

        // ── right thumb ──────────────────────────────────────────────────
        // Face buttons in the Xbox arrangement: Y top, A bottom, X left, B right.
        ButtonElement(id = "btn_y", x = 0.88f, y = 0.44f, size = 0.085f,
            mask = GamepadButton.Y, label = "Y"),
        ButtonElement(id = "btn_x", x = 0.81f, y = 0.56f, size = 0.085f,
            mask = GamepadButton.X, label = "X"),
        ButtonElement(id = "btn_b", x = 0.95f, y = 0.56f, size = 0.085f,
            mask = GamepadButton.B, label = "B"),
        ButtonElement(id = "btn_a", x = 0.88f, y = 0.68f, size = 0.085f,
            mask = GamepadButton.A, label = "A"),

        StickElement(id = "stick_right", x = 0.68f, y = 0.82f, size = 0.17f,
            stick = Stick.RIGHT),
    ),
)
