package io.github.kakashi812.droidoss.transport

import android.content.Context
import android.media.AudioAttributes
import android.os.Build
import android.os.SystemClock
import android.os.VibrationAttributes
import android.os.VibrationEffect
import android.os.Vibrator
import android.os.VibratorManager
import kotlin.math.max
import kotlin.math.roundToInt
import kotlin.math.roundToLong

/**
 * Turns the game's rumble into this phone's vibration.
 *
 * An Xbox pad has two motors — a heavy, slow one on the left (`large`) and a
 * light, fast one on the right (`small`). A phone almost always has one, so the
 * two are folded into a single strength: the heavy motor counts in full, the
 * light one for three quarters, because it is a buzz more than a shake.
 *
 * **Every vibration is short and self-ending.** Each RUMBLE starts a vibration
 * of [PULSE_MS]; the server repeats RUMBLE every 250 ms while the game keeps
 * asking, so the next one arrives before this one ends and the motor runs
 * continuously. If the messages stop — Wi-Fi gone, server closed, app crashed
 * — the motor stops within half a second on its own. Nothing here can leave a
 * phone buzzing in someone's hands.
 *
 * Safe to call from any thread; the transport calls it from its socket thread.
 */
class Rumbler(context: Context) {

    private val vibrator: Vibrator? =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            context.getSystemService(VibratorManager::class.java)?.defaultVibrator
        } else {
            @Suppress("DEPRECATION")
            context.getSystemService(Vibrator::class.java)
        }

    /** Off entirely when false. Set from the user's setting. */
    @Volatile
    var enabled: Boolean = true
        set(value) {
            field = value
            if (!value) stop()
        }

    /** 0.1–1: how hard a full-strength rumble shakes this phone. */
    @Volatile
    var strength: Float = DEFAULT_STRENGTH

    /** False on the rare phone with no vibration motor at all. */
    val available: Boolean get() = vibrator?.hasVibrator() == true

    private var lastAmplitude = 0
    private var lastStartMs = 0L
    private val lock = Any()

    /** What the game wants now, each motor 0–255. Both zero stops. */
    fun rumble(large: Int, small: Int) {
        val combined = max(large, small * 3 / 4).coerceIn(0, 255)
        play(if (enabled) (combined * strength).roundToInt() else 0)
    }

    /** A short burst at the chosen strength, so the slider can be felt. */
    fun test() {
        if (!enabled) return
        val amplitude = (255 * strength).roundToInt().coerceIn(1, 255)
        vibrate(effect(amplitude, TEST_MS))
        synchronized(lock) { lastAmplitude = 0 }
    }

    fun stop() {
        synchronized(lock) {
            if (lastAmplitude == 0) return
            lastAmplitude = 0
        }
        vibrator?.cancel()
    }

    private fun play(amplitude: Int) {
        if (amplitude < MIN_AMPLITUDE) {
            stop()
            return
        }

        val now = SystemClock.elapsedRealtime()
        synchronized(lock) {
            // Same strength, and the last pulse still has a while to run: leave
            // it be rather than restarting the motor for nothing.
            if (amplitude == lastAmplitude && now - lastStartMs < RENEW_AFTER_MS) return
            lastAmplitude = amplitude
            lastStartMs = now
        }
        vibrate(effect(amplitude, PULSE_MS))
    }

    /**
     * A vibration of [amplitude] (1–255) lasting [durationMs].
     *
     * Many phones — most mid-range ones — cannot set their motor's strength at
     * all; ask for 30% and they give 100%. On those, strength becomes how much
     * of each [DUTY_PERIOD_MS] the motor is on: 30% is 12 ms on, 28 ms off,
     * repeated. Too quick to feel as separate taps, it is felt as weaker.
     */
    private fun effect(amplitude: Int, durationMs: Long): VibrationEffect {
        if (vibrator?.hasAmplitudeControl() == true) {
            return VibrationEffect.createOneShot(durationMs, amplitude.coerceIn(1, 255))
        }

        val duty = amplitude / 255f
        if (duty >= FULL_DUTY) return VibrationEffect.createOneShot(durationMs, VibrationEffect.DEFAULT_AMPLITUDE)

        val on = (DUTY_PERIOD_MS * duty).roundToLong().coerceIn(MIN_ON_MS, DUTY_PERIOD_MS - 1)
        val off = DUTY_PERIOD_MS - on
        val cycles = (durationMs / DUTY_PERIOD_MS).coerceAtLeast(1).toInt()

        // Waveform timings alternate off, on, off, on... starting with a delay.
        val timings = LongArray(cycles * 2 + 1)
        for (i in 0 until cycles) {
            timings[i * 2 + 1] = on
            timings[i * 2 + 2] = off
        }
        return VibrationEffect.createWaveform(timings, -1)
    }

    private fun vibrate(effect: VibrationEffect) {
        val v = vibrator ?: return
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                // Media, not touch: game feedback should follow the media
                // vibration setting, not be cut by "touch vibration off".
                v.vibrate(effect, VibrationAttributes.createForUsage(VibrationAttributes.USAGE_MEDIA))
            } else {
                @Suppress("DEPRECATION")
                v.vibrate(
                    effect,
                    AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_GAME).build(),
                )
            }
        } catch (e: SecurityException) {
            // VIBRATE is a normal permission and always granted; never fatal regardless.
        }
    }

    companion object {
        const val DEFAULT_STRENGTH = 0.8f

        /** Longer than the server's 250 ms repeat, with room for a late packet. */
        private const val PULSE_MS = 450L

        /** Re-start the pulse once it is this old, so it never runs out mid-rumble. */
        private const val RENEW_AFTER_MS = 200L

        private const val TEST_MS = 300L

        /** One on/off cycle when faking strength on a motor that has none. */
        private const val DUTY_PERIOD_MS = 40L

        /** Shorter than this and some motors never spin up. */
        private const val MIN_ON_MS = 6L

        /** Near enough to full that pulsing would only feel rougher. */
        private const val FULL_DUTY = 0.9f

        /** Below this a motor barely turns; treat it as off. */
        private const val MIN_AMPLITUDE = 8
    }
}
