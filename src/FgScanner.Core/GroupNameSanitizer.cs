namespace FgScanner.Core;

/// <summary>
/// Makes a user-entered group name safe as a Windows directory name (PLAN §B14):
/// reserved characters, reserved device names, trailing dots/spaces, length.
/// </summary>
public static class GroupNameSanitizer
{
    private static readonly char[] InvalidChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    public const int MaxLength = 100;

    public static string Sanitize(string name)
    {
        var chars = name.Trim().Select(c => InvalidChars.Contains(c) || char.IsControl(c) ? '-' : c).ToArray();
        var result = new string(chars).TrimEnd('.', ' ', '-');

        if (result.Length > MaxLength)
        {
            result = result[..MaxLength].TrimEnd('.', ' ', '-');
        }

        // A reserved device name is reserved even with an extension ("CON.txt"), so check the stem.
        var stem = result.Split('.')[0].TrimEnd(' ');
        if (ReservedNames.Contains(stem))
        {
            result = "_" + result;
        }

        return result.Length == 0 ? "Group" : result;
    }
}
