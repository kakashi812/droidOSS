package io.github.kakashi812.droidoss.layout

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

/**
 * Everything persisted about layouts: the three custom slots and which layout is
 * currently selected. The Default layout is *not* stored — it is regenerated from
 * [defaultLayout] every launch, so it is always pristine and is the reset baseline
 * for the custom slots.
 *
 * [version] exists so a future format change (gyro controls, analog trigger
 * travel) can be migrated rather than silently misread.
 */
@Serializable
data class LayoutStoreData(
    val version: Int = CURRENT_VERSION,
    val activeId: String = DEFAULT_ID,
    val custom: List<ControllerLayout> = emptyList(),
) {
    companion object {
        const val CURRENT_VERSION = 1
    }
}

const val DEFAULT_ID = "default"

/**
 * Pure JSON encode/decode for the layout store, with **no Android dependency** so
 * it can be unit-tested without a device — the same Core/App split the server
 * uses. [LayoutStore] wraps this with file IO.
 *
 * The one rule that matters here: **decoding never throws.** A corrupt,
 * truncated, hand-edited, or future-version file returns fresh [defaults] instead
 * of crashing the app at startup, which is the worst possible time to fail.
 */
object LayoutCodec {

    /** The three custom slots, in gallery order: stable id to seed name. */
    val CUSTOM_SLOTS: List<Pair<String, String>> = listOf(
        "custom1" to "Custom 1",
        "custom2" to "Custom 2",
        "custom3" to "Custom 3",
    )

    private val json = Json {
        // A field added in a later version must not break a file written now.
        ignoreUnknownKeys = true
        // Write defaulted fields explicitly, so an older reader still sees them.
        encodeDefaults = true
        prettyPrint = true
    }

    /** Fresh state: each custom slot is a copy of the default; Default is active. */
    fun defaults(): LayoutStoreData = LayoutStoreData(
        version = LayoutStoreData.CURRENT_VERSION,
        activeId = DEFAULT_ID,
        custom = CUSTOM_SLOTS.map { (id, name) -> defaultLayout().copy(id = id, name = name) },
    )

    fun encode(data: LayoutStoreData): String = json.encodeToString(data)

    /**
     * Decode, tolerating anything. Structurally valid input is [repair]ed so the
     * three expected slots always exist and [activeId] always points at a real
     * layout; anything unparseable falls back to [defaults].
     */
    fun decode(text: String): LayoutStoreData =
        runCatching { json.decodeFromString<LayoutStoreData>(text) }
            .getOrNull()
            ?.let(::repair)
            ?: defaults()

    /**
     * Guarantee exactly the three canonical custom slots, preserving any present
     * in the file (so edits and renames survive) and seeding any that are missing.
     * Also coerce an [activeId] that names no existing layout back to Default.
     */
    private fun repair(data: LayoutStoreData): LayoutStoreData {
        val byId = data.custom.associateBy { it.id }
        val custom = CUSTOM_SLOTS.map { (id, name) ->
            byId[id] ?: defaultLayout().copy(id = id, name = name)
        }
        val activeValid = data.activeId == DEFAULT_ID || custom.any { it.id == data.activeId }
        return data.copy(
            custom = custom,
            activeId = if (activeValid) data.activeId else DEFAULT_ID,
        )
    }
}
