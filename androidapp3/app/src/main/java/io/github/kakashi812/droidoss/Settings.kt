package io.github.kakashi812.droidoss

import android.content.Context
import io.github.kakashi812.droidoss.transport.Rumbler

/**
 * The handful of things worth remembering between sessions.
 *
 * Plain `SharedPreferences` rather than DataStore: there is one string in here,
 * it is read once at startup and written when it changes, and pulling in a
 * coroutine-based storage library to hold an IP address would be ceremony for
 * its own sake.
 */
class Settings(context: Context) {

    private val prefs = context.getSharedPreferences("droidoss", Context.MODE_PRIVATE)

    /**
     * The server address, remembered so it is typed once rather than every time.
     *
     * Empty by default. It deliberately does **not** ship with a guess baked in:
     * a hardcoded address is right for exactly one machine on exactly one
     * network, and wrong -- confusingly, silently wrong -- for everyone else.
     * Discovery (B6) is what removes the typing properly.
     */
    var host: String
        get() = prefs.getString(KEY_HOST, "").orEmpty()
        set(value) = prefs.edit().putString(KEY_HOST, value.trim()).apply()

    /** Vibrate when the game rumbles. On unless turned off. */
    var vibration: Boolean
        get() = prefs.getBoolean(KEY_VIBRATION, true)
        set(value) = prefs.edit().putBoolean(KEY_VIBRATION, value).apply()

    /** How strong a full rumble is on this phone, 0.1–1. */
    var vibrationStrength: Float
        get() = prefs.getFloat(KEY_VIBRATION_STRENGTH, Rumbler.DEFAULT_STRENGTH)
        set(value) = prefs.edit().putFloat(KEY_VIBRATION_STRENGTH, value.coerceIn(0.1f, 1f)).apply()

    private companion object {
        const val KEY_HOST = "host"
        const val KEY_VIBRATION = "vibration"
        const val KEY_VIBRATION_STRENGTH = "vibration_strength"
    }
}
