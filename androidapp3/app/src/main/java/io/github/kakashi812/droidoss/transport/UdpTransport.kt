/*
 * Derived from PadConnect's UdpTransport.
 * Copyright (C) 2026 Ishan
 * Copyright (C) 2026 droidOSS contributors
 *
 * This program is free software: you can redistribute it and/or modify it under
 * the terms of the GNU General Public License as published by the Free Software
 * Foundation, version 3 only.
 *
 * This program is distributed without any warranty. See the GNU General Public
 * License for more details.
 *
 * MODIFIED FROM THE ORIGINAL. The two-thread shape and the Wi-Fi-aware retry
 * with exponential backoff come from PadConnect. Everything to do with the wire
 * format is new: PadConnect sends a 21-byte packet with no magic byte, no
 * version, no sequence number and no pad index, and its receiver identifies a
 * client as "whoever spoke last", which supports exactly one controller and
 * leaves a pad held down forever when the phone dies. droidOSS speaks
 * docs/PROTOCOL.md instead, which adds the handshake, four pad slots, sequence
 * ordering and the disconnect timeout. The send loop's timing was also rewritten
 * — see sendLoop().
 */

package io.github.kakashi812.droidoss.transport

import android.content.Context
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.util.Log
import io.github.kakashi812.droidoss.protocol.MessageType
import io.github.kakashi812.droidoss.protocol.PacketReader
import io.github.kakashi812.droidoss.protocol.PacketWriter
import io.github.kakashi812.droidoss.protocol.PadState
import io.github.kakashi812.droidoss.protocol.Protocol
import java.io.IOException
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.SocketTimeoutException
import java.util.concurrent.locks.LockSupport

private const val TAG = "UdpTransport"

/** Where the transport is in its life. Drives what the UI shows. */
sealed interface ConnectionState {
    data object Idle : ConnectionState
    data object Connecting : ConnectionState

    /** Connected and streaming. [slot] is the pad the server assigned us. */
    data class Connected(val slot: Int) : ConnectionState

    /** All four pads are in use. */
    data object ServerFull : ConnectionState

    /** Nothing answered. Wrong address, server not running, or a firewall. */
    data object NoServer : ConnectionState
}

/**
 * Streams controller state to the droidOSS server and listens for what comes
 * back.
 *
 * Owns two threads. The **sender** performs the handshake and then writes one
 * packet every 8 ms for as long as it runs. The **receiver** blocks on the same
 * socket waiting for WELCOME and, later, RUMBLE.
 *
 * Drawing and sending are deliberately decoupled: the send loop runs at a fixed
 * rate whatever the frame rate does. The UI thread writes into [state] under
 * [stateLock] and never touches the socket.
 */
class UdpTransport(
    private val context: Context,
    private val host: String,
    private val port: Int = Protocol.INPUT_PORT,
    private val onStateChange: (ConnectionState) -> Unit = {},
) {

    private val socket = DatagramSocket()
    private val address: InetAddress = InetAddress.getByName(host)

    /**
     * The one state object. Touch handlers write to it, the sender reads it.
     *
     * The lock lives here rather than inside [PadState] because this is where
     * both threads are visible. Uncontended locks cost tens of nanoseconds,
     * which is nothing against an 8 ms budget, and far cheaper than a torn
     * snapshot that never actually existed.
     */
    private val state = PadState()
    private val stateLock = Any()

    @Volatile
    private var running = false

    @Volatile
    private var slot: Byte = Protocol.NO_PAD

    private var sequence = 0

    /** Sends: handshake, then the fixed-rate stream. */
    private var senderThread: Thread? = null

    /** Receives: WELCOME during the handshake, RUMBLE later (B8). */
    private var receiverThread: Thread? = null

    /**
     * Mutates the shared state under the lock.
     *
     * Everything the UI does goes through here:
     * `transport.update { setButton(GamepadButton.A, true) }`
     *
     * Not inline, deliberately. This runs on touch events — tens per second at
     * most, not the 125 Hz stream — so the lambda is far too cheap to be worth
     * the ceremony an inline public function needs to reach private fields.
     */
    fun update(block: PadState.() -> Unit) {
        synchronized(stateLock) { state.block() }
    }

    fun start() {
        if (running) return
        running = true

        receiverThread = Thread(::receiveLoop, "droidoss-recv").apply {
            isDaemon = true
            start()
        }
        senderThread = Thread(::sendLoop, "droidoss-send").apply {
            isDaemon = true
            priority = Thread.MAX_PRIORITY
            start()
        }
    }

    /**
     * Neutral state, then BYE, then stop.
     *
     * This is exactly what `onPause` must do. Waiting out the server's two-second
     * timeout because someone glanced at a notification is technically correct
     * and practically awful. Neutral **before** BYE because a game that latches
     * the last state it saw would otherwise hold the stick wherever the thumb
     * left it.
     */
    fun stop() {
        if (!running) return
        running = false

        // Wait for the sender to notice before writing the farewell packets
        // ourselves. Without this, stop() (UI thread) and sendLoop (sender
        // thread) would both increment `sequence`, and the server could see the
        // neutral packet arrive with a stale number and discard it as stale —
        // leaving the pad held exactly as if we had never sent it.
        senderThread?.join(JOIN_TIMEOUT_MS)

        try {
            if (slot != Protocol.NO_PAD) {
                val writer = PacketWriter()

                val neutral = synchronized(stateLock) {
                    state.reset()
                    writer.writeInput(slot, ++sequence, state)
                }
                socket.send(DatagramPacket(writer.bytes, neutral, address, port))

                val bye = writer.writeSession(MessageType.BYE, slot)
                socket.send(DatagramPacket(writer.bytes, bye, address, port))
            }
        } catch (e: IOException) {
            // Already gone. The server's timeout is the backstop for exactly this.
            Log.w(TAG, "Could not send BYE: ${e.message}")
        }

        socket.close()
        senderThread = null
        receiverThread = null
        slot = Protocol.NO_PAD
        onStateChange(ConnectionState.Idle)
    }

    /**
     * Handshake, then stream.
     *
     * The retry loop is the point. A HELLO can simply vanish, and the server
     * would then never know this phone exists — it would drop every INPUT that
     * followed as coming from an unknown sender. Retrying is the client's job
     * because the client is the side that wants something, and it doubles as the
     * answer to the most common real situation: the app being opened before the
     * server is running.
     */
    private fun sendLoop() {
        val writer = PacketWriter()

        onStateChange(ConnectionState.Connecting)

        val hello = writer.writeSession(MessageType.HELLO, Protocol.NO_PAD)
        val helloPacket = DatagramPacket(writer.bytes.copyOf(hello), hello, address, port)

        var attempts = 0
        while (running && slot == Protocol.NO_PAD) {
            if (attempts >= HELLO_ATTEMPTS) {
                Log.w(TAG, "No WELCOME from $host:$port after $attempts attempts")
                onStateChange(ConnectionState.NoServer)
                return
            }

            try {
                socket.send(helloPacket)
            } catch (e: IOException) {
                if (!awaitNetwork(e)) return
                continue
            }

            attempts++
            LockSupport.parkNanos(Protocol.HELLO_RETRY_MS * 1_000_000)
        }

        if (!running) return

        val periodNanos = 1_000_000_000L / Protocol.SEND_RATE_HZ

        // One packet object for the life of the loop, pointed at the writer's
        // buffer. Constructing a DatagramPacket per send would allocate 125 times
        // a second forever, and GC pauses are one of the few things that cause a
        // genuinely visible latency spike.
        val outgoing = DatagramPacket(writer.bytes, Protocol.INPUT_PACKET_SIZE, address, port)

        // Absolute deadlines, accumulated. PadConnect computes its next deadline
        // as `now + interval` *after* the send, which makes the real period
        // "work + interval" and lets the rate sag below target. Adding a fixed
        // period to the previous deadline keeps the average exact — the same
        // approach tools/fake_phone.py uses.
        var next = System.nanoTime()

        while (running) {
            val length = synchronized(stateLock) {
                writer.writeInput(slot, ++sequence, state)
            }
            outgoing.setData(writer.bytes, 0, length)

            try {
                socket.send(outgoing)
            } catch (e: IOException) {
                if (!awaitNetwork(e)) return
                next = System.nanoTime()
                continue
            }

            next += periodNanos

            val sleep = next - System.nanoTime()
            if (sleep > 0) {
                LockSupport.parkNanos(sleep)
            } else {
                // We fell behind. Resync rather than firing a burst of catch-up
                // packets, which would arrive as one latency spike.
                next = System.nanoTime()
            }
        }
    }

    /** WELCOME during the handshake; RUMBLE once B8 lands. */
    private fun receiveLoop() {
        val buffer = ByteArray(64)
        val packet = DatagramPacket(buffer, buffer.size)

        while (running && !socket.isClosed) {
            try {
                socket.receive(packet)
            } catch (e: SocketTimeoutException) {
                continue
            } catch (e: IOException) {
                if (!running) return          // stop() closed the socket; expected
                if (!awaitNetwork(e)) return
                continue
            }

            val message = PacketReader.readSession(packet.data, packet.length) ?: continue
            if (message.type != MessageType.WELCOME) continue

            if (message.pad == Protocol.NO_PAD) {
                Log.w(TAG, "Server is full — all four pads in use")
                onStateChange(ConnectionState.ServerFull)
                running = false
                return
            }

            if (slot == Protocol.NO_PAD) {
                slot = message.pad
                Log.i(TAG, "Connected as pad $slot")
                onStateChange(ConnectionState.Connected(slot.toInt()))
            }
        }
    }

    /**
     * Waits for Wi-Fi to come back, with exponential backoff.
     *
     * From PadConnect, and it solves a real problem we would not have
     * anticipated: a phone roaming between access points mid-game throws
     * [IOException] from `send`, and without this the transport would simply die
     * and the pad would go with it.
     *
     * @return false if we were stopped while waiting.
     */
    private fun awaitNetwork(cause: IOException): Boolean {
        var delay = RETRY_MIN_MS
        Log.w(TAG, "Network unavailable (${cause.message}); backing off")

        while (running) {
            if (isWifiAvailable()) {
                Log.i(TAG, "Network back")
                return true
            }
            Thread.sleep(delay)
            delay = (delay * 2).coerceAtMost(RETRY_MAX_MS)
        }
        return false
    }

    private fun isWifiAvailable(): Boolean {
        val cm = context.getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager
            ?: return false
        val network = cm.activeNetwork ?: return false
        val capabilities = cm.getNetworkCapabilities(network) ?: return false
        return capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)
    }

    private companion object {
        /** ~2 s of retrying before we admit nothing is there. */
        const val HELLO_ATTEMPTS = 10

        const val RETRY_MIN_MS = 500L
        const val RETRY_MAX_MS = 5_000L

        /** Long enough for the sender to finish one 8 ms tick; short enough not to stall onPause. */
        const val JOIN_TIMEOUT_MS = 50L
    }
}
