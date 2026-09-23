using FgScanner.App.Services;
using Xunit;

namespace FgScanner.App.Tests;

/// <summary>
/// Simple MAPI is offered only when the registry says a client is really there — probed, never
/// invoked to find out. Calling `MAPISendMailW` on a station with no client is how you get a
/// modal Windows error, or a COM server spun up in the operator's face, instead of a fallback.
///
/// All three conditions are false on this station (research `2026-08-30-quick-scan-spikes.md:75,
/// :117-119`), so the three negative cases below would also pass against a probe that simply
/// always answered no. The positive case is what stops that.
/// </summary>
public sealed class MapiProbeTests
{
    private const string MailClients = @"HKEY_LOCAL_MACHINE\SOFTWARE\Clients\Mail";
    private const string Subsystem = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Messaging Subsystem";

    /// <summary>Reads from a dictionary, so no test touches the real registry of this machine.</summary>
    private sealed class FakeRegistry : IRegistryReader
    {
        public Dictionary<string, string?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? ReadString(string keyPath, string? valueName) =>
            Values.TryGetValue($"{keyPath}\\\\{valueName ?? ""}", out var v) ? v : null;
    }

    /// <summary>A station with a working client: all three conditions hold.</summary>
    private static (FakeRegistry Registry, HashSet<string> Files) Working()
    {
        var registry = new FakeRegistry();
        registry.Values[$@"{MailClients}\\"] = "Contoso Mail";
        registry.Values[$@"{MailClients}\Contoso Mail\\DLLPathEx"] = @"C:\Program Files\Contoso\mapi.dll";
        registry.Values[$@"{Subsystem}\\MAPI"] = "1";
        return (registry, new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Program Files\Contoso\mapi.dll",
        });
    }

    private static bool Probe(FakeRegistry registry, HashSet<string> files) =>
        new MapiProbe(registry, files.Contains).IsAvailable();

    [Fact]
    public void A_station_with_a_registered_client_offers_mapi()
    {
        var (registry, files) = Working();

        Assert.True(Probe(registry, files));
    }

    [Fact]
    public void No_default_mail_client_means_no_mapi()
    {
        var (registry, files) = Working();
        registry.Values[$@"{MailClients}\\"] = "";

        Assert.False(Probe(registry, files));
    }

    [Fact]
    public void A_client_whose_library_is_missing_means_no_mapi()
    {
        var (registry, files) = Working();
        files.Clear();

        Assert.False(Probe(registry, files));
    }

    [Fact]
    public void No_messaging_subsystem_value_means_no_mapi()
    {
        var (registry, files) = Working();
        registry.Values.Remove($@"{Subsystem}\\MAPI");

        Assert.False(Probe(registry, files));
    }

    /// <summary>The older value name is still accepted; some clients only write that one.</summary>
    [Fact]
    public void The_older_DLLPath_value_counts_too()
    {
        var (registry, files) = Working();
        registry.Values.Remove($@"{MailClients}\Contoso Mail\\DLLPathEx");
        registry.Values[$@"{MailClients}\Contoso Mail\\DLLPath"] = @"C:\Program Files\Contoso\mapi.dll";

        Assert.True(Probe(registry, files));
    }

    /// <summary>
    /// The point of the whole probe: it answers from the registry, and never loads or calls MAPI
    /// to find out. A probe that invokes is a probe that can hang or raise a modal error.
    /// </summary>
    [Fact]
    public void The_probe_reads_only_the_registry_and_the_file_system()
    {
        var (registry, files) = Working();
        var asked = new List<string>();
        var watched = new FakeRegistry();
        foreach (var pair in registry.Values)
        {
            watched.Values[pair.Key] = pair.Value;
        }

        new MapiProbe(watched, path => { asked.Add(path); return files.Contains(path); }).IsAvailable();

        Assert.Equal([@"C:\Program Files\Contoso\mapi.dll"], asked);
    }
}
