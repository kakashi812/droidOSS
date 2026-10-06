package io.github.kakashi812.droidoss.transport

import android.util.Log
import io.github.kakashi812.droidoss.protocol.PacketReader
import io.github.kakashi812.droidoss.protocol.PacketWriter
import io.github.kakashi812.droidoss.protocol.Protocol
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.flowOn
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.NetworkInterface
import java.net.SocketTimeoutException

private const val TAG = "ServerDiscovery"

/** A droidOSS server that answered DISCOVER. */
data class DiscoveredServer(
    /** The PC's name, as the server announced it. */
    val name: String,
    /** Where the announcement came from — the address to connect to. */
    val host: String,
    /** Where to send HELLO. */
    val port: Int,
    /** Pad slots still free when it answered. */
    val freePads: Int,
)

/**
 * Finds servers on the local network so nobody has to type an address.
 *
 * Broadcasts DISCOVER to [Protocol.DISCOVERY_PORT] and collects every
 * announcement that comes back, keyed by where it came from. Each server
 * replies straight to this socket, so several on one network each show up
 * once and the user picks.
 *
 * DISCOVER is repeated a few times through the scan: it is UDP, a broadcast can
 * vanish like any other packet, and a server started a moment after the scan
 * began should still be found.
 */
object ServerDiscovery {

    /** How long one scan listens for answers. */
    private const val SCAN_MS = 1_500L

    /** How often DISCOVER is resent during a scan. */
    private const val RESEND_MS = 300L

    /** Receive timeout, so the loop can resend and notice cancellation. */
    private const val POLL_MS = 100

    /**
     * Scans once. Emits the growing list of servers each time a new one answers,
     * so the UI can show the first server without waiting out the whole scan,
     * then completes.
     */
    fun scan(): Flow<List<DiscoveredServer>> = flow {
        val found = LinkedHashMap<String, DiscoveredServer>()
        emit(emptyList())

        val socket = try {
            DatagramSocket().apply {
                broadcast = true
                soTimeout = POLL_MS
            }
        } catch (e: IOException) {
            Log.w(TAG, "Could not open a discovery socket: ${e.message}")
            return@flow
        }

        try {
            val writer = PacketWriter()
            val discover = writer.bytes.copyOf(writer.writeDiscover())
            val targets = broadcastAddresses()

            val buffer = ByteArray(Protocol.ANNOUNCE_FIXED_SIZE + Protocol.MAX_SERVER_NAME_BYTES)
            val incoming = DatagramPacket(buffer, buffer.size)

            val deadline = System.nanoTime() + SCAN_MS * 1_000_000
            var nextSend = 0L

            while (System.nanoTime() < deadline) {
                currentCoroutineContext().ensureActive()

                if (System.nanoTime() >= nextSend) {
                    for (target in targets) {
                        try {
                            socket.send(
                                DatagramPacket(discover, discover.size, target, Protocol.DISCOVERY_PORT),
                            )
                        } catch (e: IOException) {
                            // One interface refusing a broadcast is no reason to stop
                            // asking on the others.
                            Log.w(TAG, "DISCOVER to $target failed: ${e.message}")
                        }
                    }
                    nextSend = System.nanoTime() + RESEND_MS * 1_000_000
                }

                try {
                    incoming.length = buffer.size
                    socket.receive(incoming)
                } catch (e: SocketTimeoutException) {
                    continue
                }

                val announcement = PacketReader.readAnnounce(incoming.data, incoming.length)
                    ?: continue
                val host = incoming.address.hostAddress ?: continue

                // Keyed by address and port, so a server heard on two broadcast
                // addresses is still one entry. A repeat answer refreshes the
                // free-pad count rather than adding a row.
                val key = "$host:${announcement.inputPort}"
                val server = DiscoveredServer(
                    name = announcement.name.ifBlank { host },
                    host = host,
                    port = announcement.inputPort,
                    freePads = announcement.freePads,
                )
                if (found[key] != server) {
                    found[key] = server
                    emit(found.values.sortedBy { it.name.lowercase() })
                }
            }
        } finally {
            socket.close()
        }
    }.flowOn(Dispatchers.IO)

    /**
     * Every IPv4 broadcast address this phone can reach, plus the limited
     * broadcast address.
     *
     * The subnet's own broadcast (192.168.1.255, say) is what most routers pass
     * reliably; 255.255.255.255 is a fallback for networks where the subnet
     * cannot be read. Walking the interfaces rather than asking for "the Wi-Fi
     * network" also covers the phone being the hotspot the PC is joined to.
     */
    private fun broadcastAddresses(): List<InetAddress> {
        val result = linkedSetOf<InetAddress>()

        try {
            NetworkInterface.getNetworkInterfaces()?.toList().orEmpty()
                .filter { it.isUp && !it.isLoopback }
                .flatMap { it.interfaceAddresses }
                .filter { it.address is Inet4Address }
                .mapNotNullTo(result) { it.broadcast }
        } catch (e: IOException) {
            Log.w(TAG, "Could not list network interfaces: ${e.message}")
        }

        result.add(InetAddress.getByName("255.255.255.255"))
        return result.toList()
    }
}
