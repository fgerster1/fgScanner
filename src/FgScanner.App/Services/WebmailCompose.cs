using FgScanner.Core.Sharing;

namespace FgScanner.App.Services;

/// <summary>
/// The compose page for each webmail service. A browser page cannot be handed a file by any
/// Windows mechanism — it is neither a MAPI client nor a share target, and mailto: cannot carry
/// an attachment (RFC 6068) — so the most a desktop app can do is open a new message with the
/// subject filled in and put the file beside it to drag in.
///
/// Neither link is an official API. Gmail's is long-standing and widely used. Yahoo documents
/// none; this is the form in common use, and docs/manual-tests.md has it checked on Jim's station.
/// If a service stops honouring the subject, the operator still gets a blank message.
///
/// The subject lands in the browser's address bar and history — the operator's own browser, but
/// a place it did not reach before (ADR-0012).
/// </summary>
public static class WebmailCompose
{
    public static string Url(MailPath via, string subject)
    {
        // Escaped, because the subject is free text: an unescaped "&" ends the parameter.
        var escaped = Uri.EscapeDataString(subject);
        return via switch
        {
            MailPath.Gmail => $"https://mail.google.com/mail/?view=cm&fs=1&su={escaped}",
            MailPath.Yahoo => $"https://compose.mail.yahoo.com/?subject={escaped}",
            _ => throw new ArgumentOutOfRangeException(nameof(via), via, "A mail program on this PC has no compose page."),
        };
    }

    public static string Name(MailPath via) => via switch
    {
        MailPath.Gmail => "Gmail",
        MailPath.Yahoo => "Yahoo Mail",
        _ => "your mail app",
    };
}
