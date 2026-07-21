package io.github.kakashi812.droidoss.protocol

/**
 * One complete controller snapshot — everything a game needs to know at one
 * instant.
 *
 * **Mutable and reused, never reallocated.** One instance lives for the life of
 * the transport; touch handlers write into it and the send thread reads it 125
 * times a second. Allocating a fresh state per packet would be a steady stream
 * of garbage for the collector, and GC pauses are one of the few things that
 * cause a genuinely visible latency spike.
 *
 * **Not thread-safe by itself.** The transport owns a lock around it. That is
 * deliberate: the lock belongs where both threads are visible, not scattered
 * through the accessors.
 *
 * Field order and widths mirror `XINPUT_GAMEPAD` exactly, which is why the
 * server can read the payload straight into the driver.
 */
class PadState {

    /** Button bitmask — see [GamepadButton]. Held as Int; only the low 16 bits travel. */
    var buttons: Int = 0

    /** Trigger travel, 0–255. Analog, **not** a boolean: racing needs the range. */
    var leftTrigger: Int = 0
    var rightTrigger: Int = 0

    /** Stick axes, −32768..+32767. Y is positive-**up**, opposite to the screen. */
    var thumbLX: Short = 0
    var thumbLY: Short = 0
    var thumbRX: Short = 0
    var thumbRY: Short = 0

    /** Presses a button without disturbing the others. */
    fun setButton(mask: Int, down: Boolean) {
        buttons = if (down) buttons or mask else buttons and mask.inv()
    }

    fun isPressed(mask: Int): Boolean = (buttons and mask) != 0

    /**
     * Everything centred and released.
     *
     * Sent immediately before BYE, and on `onPause`. Without it the server's last
     * known state stays "hard left, A held" and the character runs into a wall.
     */
    fun reset() {
        buttons = 0
        leftTrigger = 0
        rightTrigger = 0
        thumbLX = 0
        thumbLY = 0
        thumbRX = 0
        thumbRY = 0
    }
}
