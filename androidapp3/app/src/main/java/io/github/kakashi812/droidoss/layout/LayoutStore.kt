package io.github.kakashi812.droidoss.layout

import android.content.Context
import java.io.File

/**
 * Persists the custom layouts and the active selection to a single JSON file.
 *
 * All the fragile parsing lives in [LayoutCodec], which is pure and unit-tested;
 * this class only reads and writes the file and exposes the operations the UI
 * needs. State is held in memory after the first read, so the gallery and the
 * editor never touch the disk on a hot path — writes happen only on an explicit
 * user action (Use, Save, rename, new, delete, import).
 *
 * One instance per process, from [get]. Opening a shared layout from another
 * app can start a second MainActivity in that app's task; two stores would each
 * hold their own copy of the file and the last to write would silently discard
 * the other's change.
 *
 * The **Default** layout is never stored. [layouts] always regenerates it from
 * [defaultLayout], so it is guaranteed pristine. There are between 0 and
 * [MAX_CUSTOM] custom layouts beside it.
 */
class LayoutStore private constructor(context: Context) {

    private val file = File(context.filesDir, FILE_NAME)

    // Read once. A missing or corrupt file yields defaults rather than throwing.
    private var data: LayoutStoreData =
        runCatching { if (file.exists()) LayoutCodec.decode(file.readText()) else LayoutCodec.defaults() }
            .getOrDefault(LayoutCodec.defaults())

    /** Default first (always fresh), then the custom layouts in creation order. */
    fun layouts(): List<ControllerLayout> = buildList {
        add(defaultLayout())
        addAll(data.custom)
    }

    /** The layout the next connection will use. */
    fun activeLayout(): ControllerLayout =
        layouts().firstOrNull { it.id == data.activeId } ?: defaultLayout()

    val activeId: String get() = data.activeId

    /** False once there are [MAX_LAYOUTS], Default included. */
    val hasRoom: Boolean get() = data.custom.size < MAX_CUSTOM

    /** Look one up by id, Default included. */
    fun layout(id: String): ControllerLayout =
        layouts().firstOrNull { it.id == id } ?: defaultLayout()

    /** Mark a layout active. Default is a valid choice. */
    fun setActive(id: String) {
        if (id == data.activeId) return
        if (layouts().none { it.id == id }) return
        data = data.copy(activeId = id)
        persist()
    }

    /**
     * Save an edited custom layout back into its slot. The Default id is ignored —
     * it is not editable and must stay pristine.
     */
    fun save(layout: ControllerLayout) {
        if (layout.id == DEFAULT_ID) return
        val index = data.custom.indexOfFirst { it.id == layout.id }
        if (index < 0) return
        data = data.copy(custom = data.custom.toMutableList().apply { this[index] = layout })
        persist()
    }

    /** Rename a custom layout (its stable id is unchanged). */
    fun rename(id: String, name: String) {
        val trimmed = name.trim().take(MAX_NAME_LENGTH)
        if (id == DEFAULT_ID || trimmed.isEmpty()) return
        val index = data.custom.indexOfFirst { it.id == id }
        if (index < 0) return
        data = data.copy(
            custom = data.custom.toMutableList().apply { this[index] = this[index].copy(name = trimmed) },
        )
        persist()
    }

    /**
     * Copy [sourceId] — Default included, which is how a new layout is made —
     * into a new custom layout.
     *
     * @return the new layout's id, or null when there is no room.
     */
    fun duplicate(sourceId: String): String? {
        val source = layout(sourceId)
        val base = if (source.id == DEFAULT_ID) "Custom" else source.name
        val name = LayoutCodec.uniqueName(base, layouts().map { it.name })
        return add(name, source.elements)
    }

    /**
     * Add a layout received from elsewhere, or made by [duplicate].
     *
     * @return the new layout's id, or null when there is no room.
     */
    fun add(name: String, elements: List<ControlElement>): String? {
        if (!hasRoom) return null
        val id = LayoutCodec.newId(layouts().map { it.id })
        val unique = LayoutCodec.uniqueName(name.trim().take(MAX_NAME_LENGTH), layouts().map { it.name })
        data = data.copy(custom = data.custom + ControllerLayout(id, unique, elements))
        persist()
        return id
    }

    /** Overwrite a custom layout's name and controls in place, keeping its id. */
    fun replace(id: String, name: String, elements: List<ControlElement>) {
        if (id == DEFAULT_ID) return
        val index = data.custom.indexOfFirst { it.id == id }
        if (index < 0) return
        val others = layouts().filter { it.id != id }.map { it.name }
        val unique = LayoutCodec.uniqueName(name.trim().take(MAX_NAME_LENGTH), others)
        data = data.copy(
            custom = data.custom.toMutableList().apply {
                this[index] = ControllerLayout(id, unique, elements)
            },
        )
        persist()
    }

    /** Remove a custom layout. If it was in use, Default takes over. */
    fun delete(id: String) {
        if (id == DEFAULT_ID) return
        if (data.custom.none { it.id == id }) return
        data = data.copy(
            custom = data.custom.filter { it.id != id },
            activeId = if (data.activeId == id) DEFAULT_ID else data.activeId,
        )
        persist()
    }

    private fun persist() {
        // A failed write must not crash the app; the in-memory state stays correct
        // for this session and the next successful write catches up.
        runCatching { file.writeText(LayoutCodec.encode(data)) }
    }

    companion object {
        private const val FILE_NAME = "layouts.json"

        @Volatile
        private var instance: LayoutStore? = null

        /** The process-wide store. */
        fun get(context: Context): LayoutStore =
            instance ?: synchronized(this) {
                instance ?: LayoutStore(context.applicationContext).also { instance = it }
            }
    }
}
