namespace DroidOSS.Core;

/// <summary>
/// Identifies one connected phone: its IPv4 address and the ephemeral port it
/// happens to be sending from.
/// </summary>
/// <remarks>
/// UDP is connectionless. Four phones all send to the same server port, and the
/// only thing distinguishing them is where each datagram came from — so this is
/// what the session table is keyed by.
///
/// It is a <c>record struct</c> of two primitives on purpose. Value equality and
/// hashing come for free, and looking one up allocates nothing. An
/// <c>IPEndPoint</c> would have been the obvious choice and is the wrong one: it
/// is a class, so materialising one per datagram would allocate 125 times a
/// second per phone. That mistake has already been made once in this codebase.
///
/// Keeping it primitive also keeps <c>DroidOSS.Core</c> free of
/// <c>System.Net</c> entirely — the address is translated at the socket edge,
/// where the socket already lives.
///
/// IPv4 only. The phone and PC are on the same LAN by definition here; if that
/// ever changes this becomes a 16-byte value and nothing else moves.
/// </remarks>
public readonly record struct ClientKey(uint Address, ushort Port)
{
    /// <summary>Renders as dotted-quad plus port, for status lines and logs.</summary>
    public override string ToString() =>
        $"{Address >> 24}.{(Address >> 16) & 0xFF}.{(Address >> 8) & 0xFF}.{Address & 0xFF}:{Port}";
}
