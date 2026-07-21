package io.github.kakashi812.droidoss

import android.content.Context

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

    private companion object {
        const val KEY_HOST = "host"
    }
}
