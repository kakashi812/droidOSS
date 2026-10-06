package io.github.kakashi812.droidoss.layout

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * The layout store's persistence contract, verified without a device.
 *
 * The load-bearing guarantee is that [LayoutCodec.decode] *never* throws: a
 * corrupt or unexpected file must degrade to fresh defaults rather than crash the
 * app on launch. The round-trip, migration and repair cases pin the rest down.
 *
 * Plain JVM tests — no Android APIs. This is exactly why the codec is split from
 * the file-IO store.
 */
class LayoutCodecTest {

    private fun custom(id: String, name: String = id) =
        defaultLayout().copy(id = id, name = name)

    private fun dataWith(vararg layouts: ControllerLayout, activeId: String = DEFAULT_ID) =
        LayoutStoreData(activeId = activeId, custom = layouts.toList())

    @Test
    fun `defaults hold no custom layouts and Default is active`() {
        val d = LayoutCodec.defaults()

        assertEquals(DEFAULT_ID, d.activeId)
        assertTrue(d.custom.isEmpty())
        assertEquals(LayoutStoreData.CURRENT_VERSION, d.version)
    }

    @Test
    fun `encode then decode is identity`() {
        val original = dataWith(custom("custom1", "Racing"), custom("custom2", "Platformer"), activeId = "custom2")
        val restored = LayoutCodec.decode(LayoutCodec.encode(original))
        assertEquals(original, restored)
    }

    @Test
    fun `an edited custom layout survives a round-trip`() {
        val slot = custom("custom1")
        val movedFirst = slot.elements[0].with(x = 0.123f, size = 0.20f)
        val edited = dataWith(
            slot.copy(name = "Racing", elements = listOf(movedFirst) + slot.elements.drop(1)),
            activeId = "custom1",
        )

        val restored = LayoutCodec.decode(LayoutCodec.encode(edited))

        assertEquals("Racing", restored.custom[0].name)
        assertEquals("custom1", restored.activeId)
        assertEquals(0.123f, restored.custom[0].elements[0].x)
        assertEquals(0.20f, restored.custom[0].elements[0].size)
        // And the polymorphic type is preserved, not flattened.
        assertEquals(edited, restored)
    }

    @Test
    fun `a version 1 file keeps its three slots, names and selection`() {
        // Written by v0.1.x: always exactly custom1..custom3.
        val v1 = LayoutCodec.encode(
            dataWith(
                custom("custom1", "Custom 1"),
                custom("custom2", "Racing"),
                custom("custom3", "Custom 3"),
                activeId = "custom2",
            ),
        ).replace("\"version\": 2", "\"version\": 1")

        val restored = LayoutCodec.decode(v1)

        assertEquals(listOf("custom1", "custom2", "custom3"), restored.custom.map { it.id })
        assertEquals("Racing", restored.custom[1].name)
        assertEquals("custom2", restored.activeId)
        assertEquals(LayoutStoreData.CURRENT_VERSION, restored.version)
    }

    @Test
    fun `unknown keys are ignored so a newer file still loads`() {
        val withExtra = LayoutCodec.encode(dataWith(custom("custom1")))
            .replaceFirst("{", "{\n  \"futureFlag\": true,")

        val restored = LayoutCodec.decode(withExtra)
        assertEquals(1, restored.custom.size)
    }

    @Test
    fun `garbage decodes to defaults instead of throwing`() {
        assertEquals(LayoutCodec.defaults(), LayoutCodec.decode("this is not json {{{"))
        assertEquals(LayoutCodec.defaults(), LayoutCodec.decode(""))
        assertEquals(LayoutCodec.defaults(), LayoutCodec.decode("null"))
    }

    @Test
    fun `no more than nine custom layouts are kept`() {
        val many = (1..12).map { custom("custom$it") }.toTypedArray()
        val restored = LayoutCodec.decode(LayoutCodec.encode(dataWith(*many)))

        assertEquals(MAX_CUSTOM, restored.custom.size)
        assertEquals("custom1", restored.custom.first().id)
    }

    @Test
    fun `duplicate ids and a stored Default are dropped`() {
        val restored = LayoutCodec.decode(
            LayoutCodec.encode(
                dataWith(custom("custom1", "First"), custom("custom1", "Second"), custom(DEFAULT_ID)),
            ),
        )

        assertEquals(listOf("custom1"), restored.custom.map { it.id })
        assertEquals("First", restored.custom[0].name)
    }

    @Test
    fun `a blank name is replaced`() {
        val restored = LayoutCodec.decode(LayoutCodec.encode(dataWith(custom("custom1", "   "))))
        assertEquals("Custom 1", restored.custom[0].name)
    }

    @Test
    fun `an activeId pointing at nothing is coerced back to default`() {
        val bogus = dataWith(custom("custom1"), activeId = "does-not-exist")
        val restored = LayoutCodec.decode(LayoutCodec.encode(bogus))
        assertEquals(DEFAULT_ID, restored.activeId)
    }

    @Test
    fun `newId takes the first free customN`() {
        assertEquals("custom1", LayoutCodec.newId(listOf(DEFAULT_ID)))
        assertEquals("custom2", LayoutCodec.newId(listOf(DEFAULT_ID, "custom1", "custom3")))
    }

    @Test
    fun `uniqueName numbers a clash, ignoring case`() {
        assertEquals("Racing", LayoutCodec.uniqueName("Racing", listOf("Default")))
        assertEquals("Racing (2)", LayoutCodec.uniqueName("Racing", listOf("racing")))
        assertEquals("Racing (3)", LayoutCodec.uniqueName("Racing", listOf("Racing", "Racing (2)")))
    }
}
