package io.github.kakashi812.droidoss.layout

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

/**
 * Everything persisted about layouts: the custom layouts and which layout is
 * currently selected. The Default layout is *not* stored — it is regenerated from
 * [defaultLayout] every launch, so it is always pristine and is the starting
 * point for every new layout.
 *
 * [version] exists so a format change can be migrated rather than silently
 * misread. Version 1 always held exactly three slots, `custom1`…`custom3`;
 * version 2 holds anywhere from none to [MAX_CUSTOM]. A version 1 file is read
 * as-is — its three slots simply become three of the up-to-nine.
 */
@Serializable
data class LayoutStoreData(
    val version: Int = CURRENT_VERSION,
    val activeId: String = DEFAULT_ID,
    val custom: List<ControllerLayout> = emptyList(),
) {
    companion object {
        const val CURRENT_VERSION = 2
    }
}

const val DEFAULT_ID = "default"

/** Layouts in total, Default included. */
const val MAX_LAYOUTS = 10

/** Custom layouts, beside the always-present Default. */
const val MAX_CUSTOM = MAX_LAYOUTS - 1

/** Longest layout name kept, in characters. Long enough for a game title. */
const val MAX_NAME_LENGTH = 32

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

    private val json = Json {
        // A field added in a later version must not break a file written now.
        ignoreUnknownKeys = true
        // Write defaulted fields explicitly, so an older reader still sees them.
        encodeDefaults = true
        prettyPrint = true
    }

    /** Fresh state: just Default, which is active. Custom layouts are made on demand. */
    fun defaults(): LayoutStoreData = LayoutStoreData()

    fun encode(data: LayoutStoreData): String = json.encodeToString(data)

    /**
     * Decode, tolerating anything. Structurally valid input is [repair]ed so the
     * custom list is always usable and [LayoutStoreData.activeId] always points
     * at a real layout; anything unparseable falls back to [defaults].
     */
    fun decode(text: String): LayoutStoreData =
        runCatching { json.decodeFromString<LayoutStoreData>(text) }
            .getOrNull()
            ?.let(::repair)
            ?: defaults()

    /**
     * The first `customN` id not already taken. Ids are stable — renaming never
     * changes one — and readable in the file, which helps anyone debugging it.
     */
    fun newId(existing: Collection<String>): String =
        generateSequence(1) { it + 1 }
            .map { "custom$it" }
            .first { it !in existing }

    /**
     * [base] if no layout already uses it, otherwise "base (2)", "base (3)"…
     * so an import or a duplicate is never indistinguishable from what was there.
     */
    fun uniqueName(base: String, existing: Collection<String>): String {
        val taken = existing.map { it.lowercase() }.toSet()
        if (base.lowercase() !in taken) return base
        return generateSequence(2) { it + 1 }
            .map { "$base ($it)" }
            .first { it.lowercase() !in taken }
    }

    /**
     * Make whatever was read usable: drop anything claiming Default's id or
     * repeating an earlier id, keep at most [MAX_CUSTOM], give a blank name a
     * real one, and coerce an [LayoutStoreData.activeId] that names no layout
     * back to Default.
     */
    private fun repair(data: LayoutStoreData): LayoutStoreData {
        val seen = mutableSetOf(DEFAULT_ID)
        val custom = data.custom
            .filter { it.id.isNotBlank() && seen.add(it.id) }
            .take(MAX_CUSTOM)
            .mapIndexed { index, layout ->
                val name = layout.name.trim().take(MAX_NAME_LENGTH)
                layout.copy(name = name.ifEmpty { "Custom ${index + 1}" })
            }
        val activeValid = data.activeId == DEFAULT_ID || custom.any { it.id == data.activeId }
        return LayoutStoreData(
            version = LayoutStoreData.CURRENT_VERSION,
            activeId = if (activeValid) data.activeId else DEFAULT_ID,
            custom = custom,
        )
    }
}
