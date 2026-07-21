package io.github.kakashi812.droidoss.ui

import androidx.compose.animation.animateColorAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import io.github.kakashi812.droidoss.transport.ConnectionState

/**
 * Everything before the pad: pick a server, connect, understand what went wrong.
 *
 * Kept deliberately plain. This screen is seen for a few seconds at the start of
 * a session and never again, so its job is to be unambiguous rather than
 * decorative — and above all to explain a failure well enough that someone who
 * did not build it can fix it themselves.
 */
@Composable
fun ConnectScreen(
    connectionState: ConnectionState,
    initialHost: String,
    onConnect: (String) -> Unit,
    onDisconnect: () -> Unit,
    modifier: Modifier = Modifier,
) {
    var host by remember { mutableStateOf(initialHost) }

    val busy = connectionState is ConnectionState.Connecting
    val valid = host.isNotBlank()
    val canEdit = connectionState is ConnectionState.Idle ||
        connectionState is ConnectionState.NoServer ||
        connectionState is ConnectionState.ServerFull

    Column(
        modifier = modifier
            .fillMaxSize()
            .padding(horizontal = 28.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
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

        Spacer(Modifier.height(40.dp))

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

        Spacer(Modifier.height(28.dp))

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

        Spacer(Modifier.height(24.dp))

        StatusPanel(connectionState)
    }
}

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
