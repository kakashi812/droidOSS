namespace DroidOSS.ViGEm;

/// <summary>
/// The ViGEmBus driver is not installed, not running, or refused a connection.
/// </summary>
/// <remarks>
/// This is the first-run failure for anyone who installed the app but not the
/// driver, so it is worth a named type rather than letting an interop exception
/// escape. Callers should show the install instructions rather than a stack trace.
/// </remarks>
public sealed class PadDriverUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
