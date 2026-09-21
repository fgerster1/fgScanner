namespace FgScanner.Core.Sharing;

/// <summary>Which of the three routes in SPEC-2026-007 §08 actually opened.</summary>
public enum ShareRoute
{
    /// <summary>Nothing on this station could take the files; the operator was told where they are.</summary>
    None,

    /// <summary>Windows' own Share sheet, which new Outlook registers as a target.</summary>
    ShareSheet,

    /// <summary>Simple MAPI, offered only when the registry probe says a client is really installed.</summary>
    Mapi,

    /// <summary>Explorer, with the first file selected, and a sentence asking the operator to attach it.</summary>
    Explorer,
}

/// <summary>
/// Files to put in front of the operator's mail path, and the subject to suggest. The paths come
/// from the database and the session, never from anything typed (§13).
/// </summary>
public sealed record ShareRequest(IReadOnlyList<string> FilePaths, string Subject);

/// <summary>What happened, and the sentence to show. Never an error code (AC-6).</summary>
public sealed record ShareOutcome(ShareRoute Route, string Message);

/// <summary>
/// Hands files to whatever mail path this station has, and returns.
///
/// **There is deliberately no method here that sends** (AC-5). The operator presses Send in
/// their own mail client, under their own identity, having seen the message — or nothing leaves
/// the machine. This is the first thing in the app that moves case material out of the group
/// folder whose checksums and `originals\` archive are its evidentiary integrity (ADR-0003), and
/// an app that can transmit on its own is a different and much larger thing to reason about. A
/// test asserts that no member of this interface is named for sending, so the rule survives a
/// later session that finds it convenient.
///
/// No credentials of any kind: no SMTP, no OAuth, no token, nothing added to the credential
/// store (§13).
/// </summary>
public interface IShareService
{
    /// <summary>Opens a message, or explains why it could not. Returns once the message is up.</summary>
    ShareOutcome Open(ShareRequest request);
}
