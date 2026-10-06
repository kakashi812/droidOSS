package io.github.kakashi812.droidoss.ui

import androidx.compose.animation.animateColorAsState
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Slider
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalUriHandler
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import io.github.kakashi812.droidoss.BuildConfig
import io.github.kakashi812.droidoss.layout.ControllerLayout
import io.github.kakashi812.droidoss.layout.DEFAULT_ID
import io.github.kakashi812.droidoss.layout.MAX_CUSTOM
import io.github.kakashi812.droidoss.layout.MAX_LAYOUTS
import io.github.kakashi812.droidoss.protocol.Protocol
import io.github.kakashi812.droidoss.transport.ConnectionState
import io.github.kakashi812.droidoss.transport.DiscoveredServer
import kotlin.math.roundToInt

private const val REPO_URL = "https://github.com/kakashi812/droidOSS"

/**
 * Everything before the pad: pick a server, connect, and manage layouts.
 *
 * The layout gallery lets a user preview and arrange controllers *before*
 * connecting — the pad and the editor both work with no server, so there is
 * nothing to wait for. Kept scrollable because this now holds more than one
 * screenful on a short phone in portrait.
 */
@Composable
fun ConnectScreen(
    connectionState: ConnectionState,
    initialHost: String,
    servers: List<DiscoveredServer>,
    scanning: Boolean,
    onScan: () -> Unit,
    layouts: List<ControllerLayout>,
    activeId: String,
    onConnect: (host: String, port: Int) -> Unit,
    onDisconnect: () -> Unit,
    layoutActions: LayoutActions,
    vibration: VibrationControls,
    modifier: Modifier = Modifier,
) {
    var host by remember { mutableStateOf(initialHost) }
    var renamingId by remember { mutableStateOf<String?>(null) }
    var deletingId by remember { mutableStateOf<String?>(null) }

    val busy = connectionState is ConnectionState.Connecting
    val valid = host.isNotBlank()
    val canEdit = connectionState is ConnectionState.Idle ||
        connectionState is ConnectionState.NoServer ||
        connectionState is ConnectionState.ServerFull

    Column(
        modifier = modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Spacer(Modifier.height(32.dp))
        Text(
            text = "droidOSS",
            style = MaterialTheme.typography.displaySmall,
            fontWeight = FontWeight.Bold,
        )
        Spacer(Modifier.height(6.dp))
        Text(
            text = "Use this phone as an Xbox 360 controller",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
        )

        Spacer(Modifier.height(28.dp))

        ServerList(
            servers = servers,
            scanning = scanning,
            lastHost = initialHost,
            enabled = canEdit,
            onScan = onScan,
            onPick = { server ->
                host = server.host
                onConnect(server.host, server.port)
            },
        )

        Spacer(Modifier.height(20.dp))
        Text(
            text = "Or enter the address yourself",
            style = MaterialTheme.typography.labelLarge,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(Modifier.height(8.dp))

        OutlinedTextField(
            value = host,
            onValueChange = { host = it },
            label = { Text("PC address") },
            placeholder = { Text("192.168.1.10") },
            singleLine = true,
            enabled = canEdit,
            textStyle = MaterialTheme.typography.bodyLarge.copy(fontFamily = FontFamily.Monospace),
            keyboardOptions = KeyboardOptions(
                keyboardType = KeyboardType.Decimal,
                imeAction = ImeAction.Done,
            ),
            shape = RoundedCornerShape(14.dp),
            modifier = Modifier.fillMaxWidth(),
        )

        Spacer(Modifier.height(8.dp))
        Text(
            text = "Needed only if the server isn't listed — the server window prints its address when it starts.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.fillMaxWidth(),
        )

        Spacer(Modifier.height(20.dp))

        Button(
            onClick = { if (busy) onDisconnect() else onConnect(host.trim(), Protocol.INPUT_PORT) },
            enabled = busy || (canEdit && valid),
            shape = RoundedCornerShape(16.dp),
            modifier = Modifier
                .fillMaxWidth()
                .height(56.dp),
        ) {
            if (busy) {
                CircularProgressIndicator(
                    modifier = Modifier.size(20.dp),
                    strokeWidth = 2.dp,
                    color = MaterialTheme.colorScheme.onPrimary,
                )
                Spacer(Modifier.size(12.dp))
                Text("Cancel")
            } else {
                Text("Connect", style = MaterialTheme.typography.titleMedium)
            }
        }

        Spacer(Modifier.height(20.dp))

        StatusPanel(connectionState)

        Spacer(Modifier.height(16.dp))

        VibrationPanel(vibration)

        Spacer(Modifier.height(24.dp))

        LayoutGallery(
            layouts = layouts,
            activeId = activeId,
            actions = layoutActions,
            onRenameRequest = { renamingId = it },
            onDeleteRequest = { deletingId = it },
        )

        Spacer(Modifier.height(24.dp))

        Footer()

        Spacer(Modifier.height(24.dp))
    }

    renamingId?.let { id ->
        val current = layouts.firstOrNull { it.id == id }?.name.orEmpty()
        RenameDialog(
            current = current,
            onDismiss = { renamingId = null },
            onConfirm = { name ->
                layoutActions.rename(id, name)
                renamingId = null
            },
        )
    }

    deletingId?.let { id ->
        DeleteDialog(
            name = layouts.firstOrNull { it.id == id }?.name.orEmpty(),
            onConfirm = {
                layoutActions.delete(id)
                deletingId = null
            },
            onDismiss = { deletingId = null },
        )
    }
}

/** The vibration settings, and what changing them does. */
class VibrationControls(
    val available: Boolean,
    val enabled: Boolean,
    val strength: Float,
    val onEnabledChange: (Boolean) -> Unit,
    val onStrengthChange: (Float) -> Unit,
    val onTest: () -> Unit,
)

/**
 * Vibrate when the game rumbles: a switch, how strong, and a button to feel it.
 *
 * The slider matters more than it looks — phone motors range from a polite
 * tick to something that rattles the table, and the same game rumble has to
 * feel right on both.
 */
@Composable
private fun VibrationPanel(controls: VibrationControls) {
    Surface(
        color = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.4f),
        shape = RoundedCornerShape(16.dp),
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(modifier = Modifier.padding(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        "Vibration",
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.SemiBold,
                    )
                    Text(
                        text = if (controls.available) {
                            "The phone vibrates when the game rumbles the controller."
                        } else {
                            "This phone has no vibration motor."
                        },
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                Switch(
                    checked = controls.enabled && controls.available,
                    onCheckedChange = controls.onEnabledChange,
                    enabled = controls.available,
                )
            }

            if (controls.enabled && controls.available) {
                Spacer(Modifier.height(8.dp))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        "Strength ${(controls.strength * 100).roundToInt()}%",
                        style = MaterialTheme.typography.labelLarge,
                        modifier = Modifier.width(112.dp),
                    )
                    Slider(
                        value = controls.strength,
                        onValueChange = controls.onStrengthChange,
                        // Test on release, so dragging does not buzz continuously.
                        onValueChangeFinished = controls.onTest,
                        valueRange = 0.1f..1f,
                        modifier = Modifier.weight(1f),
                    )
                    TextButton(onClick = controls.onTest) { Text("Test") }
                }
            }
        }
    }
}

/** Everything the layout gallery can ask for, by layout id where one applies. */
class LayoutActions(
    val use: (String) -> Unit,
    val edit: (String) -> Unit,
    val view: (String) -> Unit,
    val rename: (id: String, name: String) -> Unit,
    val duplicate: (String) -> Unit,
    val delete: (String) -> Unit,
    val share: (String) -> Unit,
    val saveToFile: (String) -> Unit,
    val showQr: (String) -> Unit,
    val importFile: () -> Unit,
    val scanQr: () -> Unit,
)

/**
 * The servers that answered discovery, one tappable row each.
 *
 * Tapping a row connects — with several PCs on one network, the row *is* the
 * choice. A full server stays listed but cannot be tapped, so it is clear it
 * was found rather than mysteriously missing.
 */
@Composable
private fun ServerList(
    servers: List<DiscoveredServer>,
    scanning: Boolean,
    lastHost: String,
    enabled: Boolean,
    onScan: () -> Unit,
    onPick: (DiscoveredServer) -> Unit,
) {
    Surface(
        color = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.4f),
        shape = RoundedCornerShape(16.dp),
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(modifier = Modifier.padding(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    "Servers on this network",
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.SemiBold,
                    modifier = Modifier.weight(1f),
                )
                if (scanning) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(18.dp),
                        strokeWidth = 2.dp,
                    )
                } else {
                    TextButton(onClick = onScan, enabled = enabled) { Text("Scan again") }
                }
            }

            HorizontalDivider(Modifier.padding(vertical = 12.dp))

            if (servers.isEmpty()) {
                Text(
                    text = if (scanning) {
                        "Looking for droidOSS servers…"
                    } else {
                        "None found yet — still looking. Check the " +
                            "server is running on your PC and both devices are on the " +
                            "same Wi-Fi, or enter its address below."
                    },
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            } else {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    for (server in servers) {
                        ServerRow(
                            server = server,
                            lastUsed = server.host == lastHost,
                            enabled = enabled && server.freePads > 0,
                            onClick = { onPick(server) },
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun ServerRow(
    server: DiscoveredServer,
    lastUsed: Boolean,
    enabled: Boolean,
    onClick: () -> Unit,
) {
    Card(
        shape = RoundedCornerShape(12.dp),
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .clickable(enabled = enabled, onClick = onClick),
    ) {
        Row(
            verticalAlignment = Alignment.CenterVertically,
            modifier = Modifier.padding(horizontal = 14.dp, vertical = 12.dp),
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = server.name,
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.SemiBold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = if (lastUsed) "${server.host}  ·  last used" else server.host,
                    style = MaterialTheme.typography.bodySmall.copy(fontFamily = FontFamily.Monospace),
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            Spacer(Modifier.size(12.dp))
            Text(
                text = when (server.freePads) {
                    0 -> "Full"
                    1 -> "1 pad free"
                    else -> "${server.freePads} pads free"
                },
                style = MaterialTheme.typography.labelMedium,
                color = if (server.freePads > 0) {
                    MaterialTheme.colorScheme.primary
                } else {
                    MaterialTheme.colorScheme.error
                },
            )
        }
    }
}

/**
 * The Layouts panel: Default plus up to [MAX_CUSTOM] custom layouts, two to a
 * row, with the ways to make, receive and pass layouts on above them.
 */
@Composable
private fun LayoutGallery(
    layouts: List<ControllerLayout>,
    activeId: String,
    actions: LayoutActions,
    onRenameRequest: (String) -> Unit,
    onDeleteRequest: (String) -> Unit,
) {
    val full = layouts.size >= MAX_LAYOUTS

    Surface(
        color = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.4f),
        shape = RoundedCornerShape(16.dp),
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(modifier = Modifier.padding(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    "Layouts",
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.SemiBold,
                    modifier = Modifier.weight(1f),
                )
                Text(
                    "${layouts.size} of $MAX_LAYOUTS",
                    style = MaterialTheme.typography.labelMedium,
                    color = if (full) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            Spacer(Modifier.height(4.dp))
            Text(
                "Pick which layout to use or edit. Tap ⋮ on a layout to rename, share, show its QR code or delete it.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Spacer(Modifier.height(12.dp))

            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedButton(
                    onClick = { actions.duplicate(DEFAULT_ID) },
                    enabled = !full,
                    contentPadding = SmallPadding,
                    modifier = Modifier.weight(1f),
                ) { CardButtonLabel("+ New") }
                OutlinedButton(
                    onClick = actions.importFile,
                    contentPadding = SmallPadding,
                    modifier = Modifier.weight(1f),
                ) { CardButtonLabel("Import file") }
                OutlinedButton(
                    onClick = actions.scanQr,
                    contentPadding = SmallPadding,
                    modifier = Modifier.weight(1f),
                ) { CardButtonLabel("Scan QR") }
            }

            HorizontalDivider(Modifier.padding(vertical = 12.dp))

            layouts.chunked(2).forEach { pair ->
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    for (layout in pair) {
                        LayoutCard(
                            layout = layout,
                            isActive = layout.id == activeId,
                            canDuplicate = !full,
                            actions = actions,
                            onRenameRequest = { onRenameRequest(layout.id) },
                            onDeleteRequest = { onDeleteRequest(layout.id) },
                            modifier = Modifier.weight(1f),
                        )
                    }
                    // Keep a lone card at half width rather than stretching it.
                    if (pair.size == 1) Spacer(Modifier.weight(1f))
                }
                Spacer(Modifier.height(12.dp))
            }

            if (layouts.size == 1) {
                Text(
                    "Tap + New to make a layout of your own, or import one someone shared.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun LayoutCard(
    layout: ControllerLayout,
    isActive: Boolean,
    canDuplicate: Boolean,
    actions: LayoutActions,
    onRenameRequest: () -> Unit,
    onDeleteRequest: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val isDefault = layout.id == DEFAULT_ID
    var menuOpen by remember { mutableStateOf(false) }
    val border = if (isActive) {
        Modifier.border(2.dp, MaterialTheme.colorScheme.primary, RoundedCornerShape(12.dp))
    } else {
        Modifier
    }

    Card(
        shape = RoundedCornerShape(12.dp),
        modifier = modifier
            .then(border)
            // Long-press renames a custom layout. Default is not renamable, so its
            // long-press does nothing rather than offering a rename it can't honour.
            .combinedClickable(
                onClick = {},
                onLongClick = { if (!isDefault) onRenameRequest() },
            ),
    ) {
        Column(modifier = Modifier.padding(start = 12.dp, end = 4.dp, top = 4.dp, bottom = 12.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = layout.name,
                        style = MaterialTheme.typography.titleSmall,
                        fontWeight = FontWeight.SemiBold,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    if (isActive) {
                        Text(
                            "In use",
                            style = MaterialTheme.typography.labelSmall,
                            color = MaterialTheme.colorScheme.primary,
                            fontWeight = FontWeight.SemiBold,
                        )
                    }
                }
                Box {
                    TextButton(
                        onClick = { menuOpen = true },
                        contentPadding = PaddingValues(0.dp),
                        modifier = Modifier.size(40.dp),
                    ) {
                        Text("⋮", style = MaterialTheme.typography.titleLarge)
                    }
                    DropdownMenu(expanded = menuOpen, onDismissRequest = { menuOpen = false }) {
                        // Default is the pristine baseline everyone already has:
                        // it can be copied, but not renamed, shared or deleted.
                        if (!isDefault) {
                            MenuItem("Rename") {
                                menuOpen = false
                                onRenameRequest()
                            }
                        }
                        MenuItem(if (isDefault) "Make a copy" else "Duplicate", enabled = canDuplicate) {
                            menuOpen = false
                            actions.duplicate(layout.id)
                        }
                        if (!isDefault) {
                            MenuItem("Share…") {
                                menuOpen = false
                                actions.share(layout.id)
                            }
                            MenuItem("Save to file") {
                                menuOpen = false
                                actions.saveToFile(layout.id)
                            }
                            MenuItem("Show QR code") {
                                menuOpen = false
                                actions.showQr(layout.id)
                            }
                            MenuItem("Delete", destructive = true) {
                                menuOpen = false
                                onDeleteRequest()
                            }
                        }
                    }
                }
            }

            Spacer(Modifier.height(6.dp))

            Row(
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                modifier = Modifier.padding(end = 8.dp),
            ) {
                if (isActive) {
                    FilledTonalButton(
                        onClick = {},
                        enabled = false,
                        contentPadding = SmallPadding,
                        modifier = Modifier.weight(1f),
                    ) { CardButtonLabel("Using") }
                } else {
                    Button(
                        onClick = { actions.use(layout.id) },
                        contentPadding = SmallPadding,
                        modifier = Modifier.weight(1f),
                    ) { CardButtonLabel("Use") }
                }

                OutlinedButton(
                    onClick = { if (isDefault) actions.view(layout.id) else actions.edit(layout.id) },
                    contentPadding = SmallPadding,
                    modifier = Modifier.weight(1f),
                ) { CardButtonLabel(if (isDefault) "View" else "Edit") }
            }
        }
    }
}

@Composable
private fun MenuItem(
    label: String,
    enabled: Boolean = true,
    destructive: Boolean = false,
    onClick: () -> Unit,
) {
    DropdownMenuItem(
        text = {
            Text(
                label,
                color = if (destructive && enabled) MaterialTheme.colorScheme.error else Color.Unspecified,
            )
        },
        enabled = enabled,
        onClick = onClick,
    )
}

@Composable
private fun RenameDialog(
    current: String,
    onDismiss: () -> Unit,
    onConfirm: (String) -> Unit,
) {
    var name by remember { mutableStateOf(current) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Rename layout") },
        text = {
            OutlinedTextField(
                value = name,
                onValueChange = { name = it },
                singleLine = true,
                label = { Text("Name") },
            )
        },
        confirmButton = {
            TextButton(
                onClick = { onConfirm(name) },
                enabled = name.isNotBlank(),
            ) { Text("Save") }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text("Cancel") } },
    )
}

@Composable
private fun Footer() {
    val uriHandler = LocalUriHandler.current
    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Text(
            "version ${BuildConfig.VERSION_NAME}",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Spacer(Modifier.height(2.dp))
        Text(
            "github.com/kakashi812/droidOSS",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.primary,
            modifier = Modifier
                .clip(RoundedCornerShape(4.dp))
                .clickable { uriHandler.openUri(REPO_URL) }
                .padding(4.dp),
        )
    }
}

/** Card buttons are half-card wide, so their labels must stay on one small line
 *  rather than wrapping or clipping ("Using" was the tight one). */
@Composable
private fun CardButtonLabel(text: String) {
    Text(
        text,
        style = MaterialTheme.typography.labelMedium,
        maxLines = 1,
        softWrap = false,
    )
}

private val SmallPadding = PaddingValues(horizontal = 4.dp, vertical = 6.dp)

/**
 * One line of state and, when something is wrong, what to do about it.
 *
 * The advice matters more than the status. "No answer" on its own sends someone
 * to the wrong place; the three things worth checking are always the same three,
 * so the app says them rather than making anyone guess.
 */
@Composable
private fun StatusPanel(state: ConnectionState) {
    val colour by animateColorAsState(
        targetValue = when (state) {
            is ConnectionState.Connected -> Color(0xFF4CAF50)
            is ConnectionState.Connecting -> Color(0xFFFFB300)
            is ConnectionState.NoServer, is ConnectionState.ServerFull -> Color(0xFFE53935)
            is ConnectionState.Idle -> Color(0xFF9E9E9E)
        },
        label = "status",
    )

    Surface(
        color = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.4f),
        shape = RoundedCornerShape(14.dp),
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(modifier = Modifier.padding(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(
                    Modifier
                        .size(10.dp)
                        .clip(CircleShape)
                        .background(colour),
                )
                Spacer(Modifier.size(10.dp))
                Text(
                    text = headline(state),
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.SemiBold,
                )
            }

            detail(state)?.let { text ->
                Spacer(Modifier.height(8.dp))
                Text(
                    text = text,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

private fun headline(state: ConnectionState): String = when (state) {
    is ConnectionState.Idle -> "Not connected"
    is ConnectionState.Connecting -> "Looking for the server…"
    is ConnectionState.Connected -> "Connected as player ${state.slot + 1}"
    is ConnectionState.ServerFull -> "Server is full"
    is ConnectionState.NoServer -> "No answer from that server"
}

private fun detail(state: ConnectionState): String? = when (state) {
    is ConnectionState.Idle ->
        "Start the droidOSS server on your PC, then pick it above."

    is ConnectionState.Connecting -> null

    is ConnectionState.Connected -> null

    is ConnectionState.ServerFull ->
        "All four controller slots are in use. Disconnect another phone and try again."

    is ConnectionState.NoServer ->
        "Check that:\n" +
            "  •  the server is running on your PC\n" +
            "  •  the address matches one it printed\n" +
            "  •  both devices are on the same Wi-Fi network"
}
