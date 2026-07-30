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
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
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
import io.github.kakashi812.droidoss.transport.ConnectionState

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
    layouts: List<ControllerLayout>,
    activeId: String,
    onConnect: (String) -> Unit,
    onDisconnect: () -> Unit,
    onUse: (String) -> Unit,
    onEdit: (String) -> Unit,
    onView: (String) -> Unit,
    onRename: (id: String, name: String) -> Unit,
    modifier: Modifier = Modifier,
) {
    var host by remember { mutableStateOf(initialHost) }
    var renamingId by remember { mutableStateOf<String?>(null) }

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
            text = "The server window prints this address when it starts.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            modifier = Modifier.fillMaxWidth(),
        )

        Spacer(Modifier.height(20.dp))

        Button(
            onClick = { if (busy) onDisconnect() else onConnect(host.trim()) },
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

        Spacer(Modifier.height(24.dp))

        LayoutGallery(
            layouts = layouts,
            activeId = activeId,
            onUse = onUse,
            onEdit = onEdit,
            onView = onView,
            onRenameRequest = { renamingId = it },
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
                onRename(id, name)
                renamingId = null
            },
        )
    }
}

/** The Layouts panel: Default + the custom slots, two to a row. */
@Composable
private fun LayoutGallery(
    layouts: List<ControllerLayout>,
    activeId: String,
    onUse: (String) -> Unit,
    onEdit: (String) -> Unit,
    onView: (String) -> Unit,
    onRenameRequest: (String) -> Unit,
) {
    Surface(
        color = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.4f),
        shape = RoundedCornerShape(16.dp),
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(modifier = Modifier.padding(16.dp)) {
            Text(
                "Layouts",
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold,
            )
            Spacer(Modifier.height(4.dp))
            Text(
                "Pick which layout to use/edit (long press on layout to rename it)",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            HorizontalDivider(Modifier.padding(vertical = 12.dp))

            layouts.chunked(2).forEach { pair ->
                Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    for (layout in pair) {
                        LayoutCard(
                            layout = layout,
                            isActive = layout.id == activeId,
                            onUse = { onUse(layout.id) },
                            onEdit = { onEdit(layout.id) },
                            onView = { onView(layout.id) },
                            onRenameRequest = { onRenameRequest(layout.id) },
                            modifier = Modifier.weight(1f),
                        )
                    }
                    // Keep a lone card at half width rather than stretching it.
                    if (pair.size == 1) Spacer(Modifier.weight(1f))
                }
                Spacer(Modifier.height(12.dp))
            }
        }
    }
}

@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun LayoutCard(
    layout: ControllerLayout,
    isActive: Boolean,
    onUse: () -> Unit,
    onEdit: () -> Unit,
    onView: () -> Unit,
    onRenameRequest: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val isDefault = layout.id == DEFAULT_ID
    val border = if (isActive) {
        Modifier.border(2.dp, MaterialTheme.colorScheme.primary, RoundedCornerShape(12.dp))
    } else {
        Modifier
    }

    Card(
        shape = RoundedCornerShape(12.dp),
        modifier = modifier
            .then(border)
            // Long-press renames a custom slot. Default is not renamable, so its
            // long-press does nothing rather than offering a rename it can't honour.
            .combinedClickable(
                onClick = {},
                onLongClick = { if (!isDefault) onRenameRequest() },
            ),
    ) {
        Column(modifier = Modifier.padding(12.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = layout.name,
                    style = MaterialTheme.typography.titleSmall,
                    fontWeight = FontWeight.SemiBold,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.weight(1f),
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

            Spacer(Modifier.height(10.dp))

            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                if (isActive) {
                    FilledTonalButton(
                        onClick = {},
                        enabled = false,
                        contentPadding = SmallPadding,
                        modifier = Modifier.weight(1f),
                    ) { CardButtonLabel("Using") }
                } else {
                    Button(
                        onClick = onUse,
                        contentPadding = SmallPadding,
                        modifier = Modifier.weight(1f),
                    ) { CardButtonLabel("Use") }
                }

                OutlinedButton(
                    onClick = if (isDefault) onView else onEdit,
                    contentPadding = SmallPadding,
                    modifier = Modifier.weight(1f),
                ) { CardButtonLabel(if (isDefault) "View" else "Edit") }
            }
        }
    }
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
    is ConnectionState.NoServer -> "No answer from that address"
}

private fun detail(state: ConnectionState): String? = when (state) {
    is ConnectionState.Idle ->
        "Start the droidOSS server on your PC, then connect."

    is ConnectionState.Connecting -> null

    is ConnectionState.Connected -> null

    is ConnectionState.ServerFull ->
        "All four controller slots are in use. Disconnect another phone and try again."

    is ConnectionState.NoServer ->
        "Check that:\n" +
            "  •  the server is running on your PC\n" +
            "  •  the address above matches the one it printed\n" +
            "  •  both devices are on the same Wi-Fi network"
}
