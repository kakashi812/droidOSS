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
 * user action (Use, Save, rename, reset).
 *
 * The **Default** layout is never stored. [layouts] always regenerates it from
 * [defaultLayout], so it is guaranteed pristine and is the baseline [reset]
 * restores a custom slot to.
 */
class LayoutStore(context: Context) {

    private val file = File(context.filesDir, FILE_NAME)

    // Read once. A missing or corrupt file yields defaults rather than throwing.
    private var data: LayoutStoreData =
        runCatching { if (file.exists()) LayoutCodec.decode(file.readText()) else LayoutCodec.defaults() }
            .getOrDefault(LayoutCodec.defaults())

    /** Default first (always fresh), then the three persisted custom slots. */
    fun layouts(): List<ControllerLayout> = buildList {
        add(defaultLayout())
        addAll(data.custom)
    }

    /** The layout the next connection will use. */
    fun activeLayout(): ControllerLayout =
        layouts().firstOrNull { it.id == data.activeId } ?: defaultLayout()

    val activeId: String get() = data.activeId

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

    /** Rename a custom slot (its stable id is unchanged). */
    fun rename(id: String, name: String) {
        val trimmed = name.trim()
        if (id == DEFAULT_ID || trimmed.isEmpty()) return
        val index = data.custom.indexOfFirst { it.id == id }
        if (index < 0) return
        data = data.copy(
            custom = data.custom.toMutableList().apply { this[index] = this[index].copy(name = trimmed) },
        )
        persist()
    }

    /** Restore a custom slot to the default layout, keeping its id and name. */
    fun reset(id: String) {
        if (id == DEFAULT_ID) return
        val index = data.custom.indexOfFirst { it.id == id }
        if (index < 0) return
        val current = data.custom[index]
        data = data.copy(
            custom = data.custom.toMutableList().apply {
                this[index] = defaultLayout().copy(id = current.id, name = current.name)
            },
        )
        persist()
    }

    private fun persist() {
        // A failed write must not crash the app; the in-memory state stays correct
        // for this session and the next successful write catches up.
        runCatching { file.writeText(LayoutCodec.encode(data)) }
    }

    private companion object {
        const val FILE_NAME = "layouts.json"
    }
}
