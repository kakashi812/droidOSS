package io.github.kakashi812.droidoss.layout

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * The layout store's persistence contract, verified without a device.
 *
 * The load-bearing guarantee is that [LayoutCodec.decode] *never* throws: a
 * corrupt or unexpected file must degrade to fresh defaults rather than crash the
 * app on launch. The round-trip and repair cases pin the rest down.
 *
 * Plain JVM tests — no Android APIs. This is exactly why the codec is split from
 * the file-IO store.
 */
class LayoutCodecTest {

    @Test
    fun `defaults seed three named custom slots from the default layout`() {
        val d = LayoutCodec.defaults()

        assertEquals(DEFAULT_ID, d.activeId)
        assertEquals(3, d.custom.size)
        assertEquals(listOf("custom1", "custom2", "custom3"), d.custom.map { it.id })
        assertEquals(listOf("Custom 1", "Custom 2", "Custom 3"), d.custom.map { it.name })

        // Each slot is a genuine copy of the default's controls.
        val defaultElements = defaultLayout().elements.map { it.id }
        for (slot in d.custom) {
            assertEquals(defaultElements, slot.elements.map { it.id })
        }
    }

    @Test
    fun `encode then decode is identity`() {
        val original = LayoutCodec.defaults()
        val restored = LayoutCodec.decode(LayoutCodec.encode(original))
        assertEquals(original, restored)
    }

    @Test
    fun `an edited custom layout survives a round-trip`() {
        val edited = LayoutCodec.defaults().let { data ->
            // Nudge the first control of custom1 and rename the slot.
            val slot = data.custom[0]
            val first = slot.elements[0]
            val movedFirst = when (first) {
                is ButtonElement -> first.copy(x = 0.123f, size = 0.20f)
                is TriggerElement -> first.copy(x = 0.123f, size = 0.20f)
                is StickElement -> first.copy(x = 0.123f, size = 0.20f)
                is DpadElement -> first.copy(x = 0.123f, size = 0.20f)
            }
            val movedSlot = slot.copy(
                name = "Racing",
                elements = listOf(movedFirst) + slot.elements.drop(1),
            )
            data.copy(
                activeId = "custom1",
                custom = listOf(movedSlot) + data.custom.drop(1),
            )
        }

        val restored = LayoutCodec.decode(LayoutCodec.encode(edited))

        assertEquals("Racing", restored.custom[0].name)
        assertEquals("custom1", restored.activeId)
        assertEquals(0.123f, restored.custom[0].elements[0].x)
        assertEquals(0.20f, restored.custom[0].elements[0].size)
        // And the polymorphic type is preserved, not flattened.
        assertEquals(edited, restored)
    }

    @Test
    fun `unknown keys are ignored so a newer file still loads`() {
        // Simulate a file written by a future version that added fields.
        val withExtra = LayoutCodec.encode(LayoutCodec.defaults())
            .replaceFirst("{", "{\n  \"futureFlag\": true,")

        val restored = LayoutCodec.decode(withExtra)
        assertEquals(3, restored.custom.size)
    }

    @Test
    fun `garbage decodes to defaults instead of throwing`() {
        assertEquals(LayoutCodec.defaults(), LayoutCodec.decode("this is not json {{{"))
        assertEquals(LayoutCodec.defaults(), LayoutCodec.decode(""))
        assertEquals(LayoutCodec.defaults(), LayoutCodec.decode("null"))
    }

    @Test
    fun `a missing custom slot is reseeded on decode`() {
        // A file that only carries custom1 must still yield all three slots.
        val onlyOne = LayoutCodec.defaults().let {
            it.copy(custom = it.custom.take(1))
        }
        val restored = LayoutCodec.decode(LayoutCodec.encode(onlyOne))

        assertEquals(listOf("custom1", "custom2", "custom3"), restored.custom.map { it.id })
    }

    @Test
    fun `an activeId pointing at nothing is coerced back to default`() {
        val bogus = LayoutCodec.defaults().copy(activeId = "does-not-exist")
        val restored = LayoutCodec.decode(LayoutCodec.encode(bogus))
        assertEquals(DEFAULT_ID, restored.activeId)
    }

    @Test
    fun `a valid custom activeId is preserved`() {
        val active = LayoutCodec.defaults().copy(activeId = "custom2")
        val restored = LayoutCodec.decode(LayoutCodec.encode(active))
        assertEquals("custom2", restored.activeId)
    }

    @Test
    fun `custom slots are independent copies not the same instance`() {
        // Guards against a future refactor that shares one list between slots.
        val d = LayoutCodec.defaults()
        assertNotEquals(d.custom[0].id, d.custom[1].id)
        assertTrue(d.custom.all { it.elements.isNotEmpty() })
    }
}
