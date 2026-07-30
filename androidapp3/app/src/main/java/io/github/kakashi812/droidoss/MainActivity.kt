package io.github.kakashi812.droidoss

import android.content.pm.ActivityInfo
import android.os.Bundle
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Scaffold
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import androidx.lifecycle.lifecycleScope
import io.github.kakashi812.droidoss.layout.ControllerLayout
import io.github.kakashi812.droidoss.layout.LayoutStore
import io.github.kakashi812.droidoss.layout.defaultLayout
import io.github.kakashi812.droidoss.transport.ConnectionState
import io.github.kakashi812.droidoss.transport.UdpTransport
import io.github.kakashi812.droidoss.ui.ConnectScreen
import io.github.kakashi812.droidoss.ui.LayoutEditorScreen
import io.github.kakashi812.droidoss.ui.PadScreen
import io.github.kakashi812.droidoss.ui.theme.DroidOSSTheme
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class MainActivity : ComponentActivity() {

    // Both are Compose state. `transport` in particular: the screen captures it,
    // so a plain field would leave the UI holding null after a connect, and it
    // would only appear to work because `connectionState` changes a moment later
    // and drags a recomposition along with it.
    private var transport by mutableStateOf<UdpTransport?>(null)
    private var connectionState by mutableStateOf<ConnectionState>(ConnectionState.Idle)

    // Which full-screen layout screen, if any, is open over the gallery. At most
    // one is ever set.
    private var editingId by mutableStateOf<String?>(null)
    private var viewingId by mutableStateOf<String?>(null)

    // The gallery's data, mirrored into Compose state so a Use / rename / save
    // repaints the cards. `store` is the source of truth on disk.
    private val store by lazy { LayoutStore(applicationContext) }
    private var layouts by mutableStateOf<List<ControllerLayout>>(emptyList())
    private var activeId by mutableStateOf("default")

    private val settings by lazy { Settings(applicationContext) }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()

        layouts = store.layouts()
        activeId = store.activeId

        setContent {
            DroidOSSTheme {
                val activeLayout = layouts.firstOrNull { it.id == activeId } ?: defaultLayout()

                when {
                    // Live play takes precedence over everything.
                    connectionState is ConnectionState.Connected -> {
                        KeepAwakeLandscape()
                        // Back leaves the pad rather than the app, so a misfire
                        // during play costs a tap instead of the whole session.
                        BackHandler { disconnect() }
                        PadScreen(
                            transport = transport,
                            layout = activeLayout,
                            modifier = Modifier.fillMaxSize(),
                        )
                    }

                    // Arranging a custom layout. Same landscape framing as the pad,
                    // because you must arrange at the size you will play at.
                    editingId != null -> {
                        val id = editingId!!
                        val layout = layouts.firstOrNull { it.id == id } ?: defaultLayout()
                        KeepAwakeLandscape()
                        BackHandler { editingId = null }
                        LayoutEditorScreen(
                            initial = layout,
                            onSave = { edited ->
                                store.save(edited)
                                layouts = store.layouts()
                                editingId = null
                            },
                            onCancel = { editingId = null },
                            modifier = Modifier.fillMaxSize(),
                        )
                    }

                    // Interactive read-only preview: the real pad, sending nowhere.
                    viewingId != null -> {
                        val id = viewingId!!
                        val layout = layouts.firstOrNull { it.id == id } ?: defaultLayout()
                        KeepAwakeLandscape()
                        BackHandler { viewingId = null }
                        PadScreen(
                            transport = null,
                            layout = layout,
                            modifier = Modifier.fillMaxSize(),
                        )
                    }

                    else -> {
                        Scaffold(modifier = Modifier.fillMaxSize()) { innerPadding ->
                            ConnectScreen(
                                connectionState = connectionState,
                                initialHost = settings.host,
                                layouts = layouts,
                                activeId = activeId,
                                onConnect = ::connect,
                                onDisconnect = ::disconnect,
                                onUse = ::useLayout,
                                onEdit = { editingId = it },
                                onView = { viewingId = it },
                                onRename = ::renameLayout,
                                modifier = Modifier.padding(innerPadding),
                            )
                        }
                    }
                }
            }
        }
    }

    /**
     * Landscape, immersive, and awake, for as long as a full-screen pad (live,
     * editing, or previewing) is on screen.
     *
     * All three are reverted on the way out, so the gallery behaves like a normal
     * app. Keeping the screen on matters more than it sounds: a gamepad receives
     * no touches during a cutscene, and arranging a control takes a moment of
     * looking without touching — the display timing out mid-way would be baffling.
     */
    @Composable
    private fun KeepAwakeLandscape() {
        DisposableEffect(Unit) {
            val previousOrientation = requestedOrientation
            requestedOrientation = ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE
            window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)

            val controller = WindowCompat.getInsetsController(window, window.decorView)
            controller.systemBarsBehavior =
                WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            controller.hide(WindowInsetsCompat.Type.systemBars())

            onDispose {
                requestedOrientation = previousOrientation
                window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                controller.show(WindowInsetsCompat.Type.systemBars())
            }
        }
    }

    /**
     * Neutral state, then BYE, the moment we lose the foreground.
     *
     * Waiting out the server's two-second timeout because someone glanced at a
     * notification is technically correct and practically awful — two seconds of
     * a character running into a wall. This is a non-negotiable of the design,
     * not a nicety. Harmless while editing or previewing (there is no transport).
     */
    override fun onPause() {
        super.onPause()
        disconnect()
    }

    private fun useLayout(id: String) {
        store.setActive(id)
        activeId = store.activeId
    }

    private fun renameLayout(id: String, name: String) {
        store.rename(id, name)
        layouts = store.layouts()
    }

    private fun connect(host: String) {
        // Remembered on attempt rather than on success, so a typo that fails is
        // still there to correct rather than having to be retyped from scratch.
        settings.host = host

        val previous = transport
        transport = null

        lifecycleScope.launch {
            // Off the main thread: constructing the transport opens a socket and
            // resolves the address, and Android kills the app outright for doing
            // either on the UI thread.
            val created = withContext(Dispatchers.IO) {
                // Retire the old session *before* opening a new one. Overlapping
                // them means the server sees two clients from this phone and
                // hands out two pad slots, of which one is a ghost that only
                // disappears on timeout.
                previous?.stop()

                runCatching {
                    UdpTransport(
                        context = applicationContext,
                        host = host,
                        onStateChange = { state ->
                            // Arrives on a socket thread; Compose state must be
                            // written from the main thread.
                            lifecycleScope.launch { connectionState = state }
                        },
                    )
                }
            }

            created
                .onSuccess { transport = it.also { t -> t.start() } }
                .onFailure {
                    connectionState = ConnectionState.NoServer
                    transport = null
                }
        }
    }

    private fun disconnect() {
        val existing = transport ?: return
        transport = null

        // stop() sends the farewell packets and briefly joins the sender thread,
        // so it must not run on the UI thread.
        lifecycleScope.launch(Dispatchers.IO) { existing.stop() }
        connectionState = ConnectionState.Idle
    }
}
