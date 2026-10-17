using System.Security.Cryptography;
using System.Text;
using FgScanner.Core.Station;
using Xunit;

namespace FgScanner.Core.Tests.Station;

/// <summary>
/// <c>station apply</c> (SPEC-SCANNER-2026-010 AC-5, AC-6): a return reaches the manifest's final
/// state on Jim's laptop, keeps what is his (settings, drafts), never erases a file, and can be
/// undone exactly — or it refuses and changes nothing. The database here is a text stand-in
/// (<see cref="FakeDatabase"/>); the SQLite side is <c>SqliteStationDatabaseTests</c>.
/// </summary>
public sealed class StationApplyTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 16, 14, 0, 0, TimeSpan.FromHours(-4));

    private static readonly System.Text.Json.JsonSerializerOptions WebJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    private readonly string _root = Directory.CreateTempSubdirectory("fg-apply").FullName;

    private readonly FakeDatabase _database = new();

    private bool _appOpen;

    private string Jim => Path.Combine(_root, "jim");

    private string Franz => Path.Combine(_root, "franz");

    /// <summary>Both stations name the same evidence path, as Jim's and Franz's do; the one not in
    /// play is parked beside it.</summary>
    private string Evidence => Path.Combine(_root, "Evidence-Scans");

    private string Transport => Path.Combine(_root, "drive");

    public StationApplyTests()
    {
        Write(Path.Combine(Jim, "fgscanner.db"), "row:group Dental\nrow:page 1\nrow:page 2\nsetting:theme=dark");
        Write(Path.Combine(Jim, "index-drafts", "draft.json"), "Jim's draft");
        Write(Path.Combine(Evidence, "Dental", "scan_00001.jpg"), "jpeg one");
        Write(Path.Combine(Evidence, "Dental", "scan_00002.jpg"), "jpeg two");
        Write(Path.Combine(Evidence, "Dental", "scan_00003.jpg"), "jpeg three");
        Write(Path.Combine(Jim, "trash", "0001", "scan_00009.jpg"), "trashed");
        Directory.CreateDirectory(Transport);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static StationPacker Packer() => new(() => false, File.Copy, _ => long.MaxValue);

    private StationApplier Applier() => new(() => _appOpen, _database);

    private StationPackResult Pack(
        string station, string kind, string? basedOn = null, StationContent? basedOnContent = null, DateTimeOffset? at = null) =>
        Packer().Pack(new StationPackRequest(
            new StationSource(
                kind == "send" ? "jim" : "franz", Path.Combine(station, "fgscanner.db"), Evidence,
                Path.Combine(station, "trash"), "0.8.0", "M2", null, at ?? At),
            station, Transport, basedOn, kind, 0, basedOnContent, _database.Content(Path.Combine(station, "fgscanner.db"))));

    private StationApplyRequest ApplyRequest(
        string pack, string version = "0.8.0", string migration = "M2", string? jimsMigration = null) =>
        new(pack, Jim, Evidence, version, migration, At, jimsMigration ?? migration);

    /// <summary>Jim sends; Franz opens the send, adds a page, changes one, renames one, deletes one,
    /// and packs the return. Jim's evidence is parked while Franz's tree stands at the shared path.</summary>
    private string RoundTrip(Action? franzWorks = null)
    {
        var sent = Pack(Jim, "send");
        Assert.Null(sent.Problem);

        Directory.Move(Evidence, Evidence + ".jim");
        CopyTree(Evidence + ".jim", Evidence);
        Directory.CreateDirectory(Franz);
        File.Copy(Path.Combine(sent.PackFolder!, "fgscanner.db"), Path.Combine(Franz, "fgscanner.db"));
        CopyTree(Path.Combine(Jim, "trash"), Path.Combine(Franz, "trash"));
        File.AppendAllText(Path.Combine(Franz, "fgscanner.db"), "\nrow:page 4\nsetting:theme=light");
        Write(Path.Combine(Evidence, "Dental", "scan_00004.jpg"), "jpeg four");
        File.WriteAllText(Path.Combine(Evidence, "Dental", "scan_00001.jpg"), "jpeg one, turned");
        File.Move(Path.Combine(Evidence, "Dental", "scan_00002.jpg"), Path.Combine(Evidence, "Dental", "scan_00005.jpg"));
        File.Delete(Path.Combine(Evidence, "Dental", "scan_00003.jpg"));
        franzWorks?.Invoke();
        var returned = Pack(Franz, "return", Path.Combine(sent.PackFolder!, StationManifest.FileName),
            _database.Content(Path.Combine(sent.PackFolder!, "fgscanner.db")));
        Assert.Null(returned.Problem);

        Directory.Move(Evidence, Evidence + ".franz");
        Directory.Move(Evidence + ".jim", Evidence);
        return returned.PackFolder!;
    }

    private static void CopyTree(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    /// <summary>Every file of Jim's station and evidence, by path and hash — "changed nothing".</summary>
    private SortedDictionary<string, string> Snapshot()
    {
        var all = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var root in new[] { Jim, Evidence })
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .Where(f => !Path.GetRelativePath(root, f).StartsWith("apply-", StringComparison.Ordinal)))
            {
                all[file] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            }
        }

        return all;
    }

    [Fact]
    public void A_return_reaches_the_final_state_and_keeps_Jims_settings_and_drafts()
    {
        var pack = RoundTrip();

        var result = Applier().Apply(ApplyRequest(pack));

        Assert.Null(result.Problem);
        Assert.Equal(StationManifestVerifier.Verify(pack).Code, result.Code);
        Assert.Equal("jpeg one, turned", File.ReadAllText(Path.Combine(Evidence, "Dental", "scan_00001.jpg")));
        Assert.Equal("jpeg two", File.ReadAllText(Path.Combine(Evidence, "Dental", "scan_00005.jpg")));
        Assert.Equal("jpeg four", File.ReadAllText(Path.Combine(Evidence, "Dental", "scan_00004.jpg")));
        Assert.False(File.Exists(Path.Combine(Evidence, "Dental", "scan_00002.jpg")));
        Assert.False(File.Exists(Path.Combine(Evidence, "Dental", "scan_00003.jpg")));
        var db = File.ReadAllText(Path.Combine(Jim, "fgscanner.db"));
        Assert.Contains("row:page 4", db);
        Assert.Contains("setting:theme=dark", db);
        Assert.DoesNotContain("setting:theme=light", db);
        Assert.Equal("Jim's draft", File.ReadAllText(Path.Combine(Jim, "index-drafts", "draft.json")));
    }

    /// <summary>SPEC-SCANNER-2026-011 AC-2: Jim backs up to his stick while Franz works on the send;
    /// the return still installs.</summary>
    [Fact]
    public void A_backup_between_send_and_return_changes_nothing_the_return_is_checked_against()
    {
        var pack = RoundTrip();
        var stick = Path.Combine(_root, "stick");
        Directory.CreateDirectory(stick);
        var backup = Packer().Pack(new StationPackRequest(
            new StationSource("jim", Path.Combine(Jim, "fgscanner.db"), Evidence, Path.Combine(Jim, "trash"),
                "0.8.0", "M2", null, At.AddMinutes(30)),
            Jim, stick, null, "backup", 0, null, _database.Content(Path.Combine(Jim, "fgscanner.db"))));
        Assert.Null(backup.Problem);

        var result = Applier().Apply(ApplyRequest(pack));

        Assert.Null(result.Problem);
    }

    [Fact]
    public void Nothing_is_erased_replaced_and_deleted_files_are_kept_aside()
    {
        var pack = RoundTrip();

        var result = Applier().Apply(ApplyRequest(pack));

        var backup = result.BackupFolder!;
        Assert.StartsWith(Path.Combine(Jim, "apply-backup-"), backup);
        Assert.Equal("jpeg one", File.ReadAllText(Path.Combine(backup, "evidence", "Dental", "scan_00001.jpg")));
        Assert.Equal("jpeg three", File.ReadAllText(Path.Combine(backup, "evidence", "Dental", "scan_00003.jpg")));
        Assert.Contains("setting:theme=dark", File.ReadAllText(Path.Combine(backup, "fgscanner.db")));
    }

    [Fact]
    public void Undo_restores_the_station_exactly()
    {
        var pack = RoundTrip();
        var before = Snapshot();
        Assert.Null(Applier().Apply(ApplyRequest(pack)).Problem);

        var undone = Applier().Undo(Jim);

        Assert.Null(undone.Problem);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void A_second_undo_has_nothing_to_undo()
    {
        Assert.Null(Applier().Apply(ApplyRequest(RoundTrip())).Problem);
        Assert.Null(Applier().Undo(Jim).Problem);

        Assert.Contains("nothing to undo", Applier().Undo(Jim).Problem);
    }

    [Fact]
    public void Applying_records_the_return_as_the_new_shared_baseline()
    {
        var pack = RoundTrip();

        Applier().Apply(ApplyRequest(pack));

        Assert.Equal(
            File.ReadAllBytes(Path.Combine(pack, StationManifest.FileName)),
            File.ReadAllBytes(Path.Combine(Jim, StationFreezeRecord.SentManifestFileName)));
        Assert.Equal(_database.Content(Path.Combine(Jim, "fgscanner.db")), StationFreezeRecord.Read(Jim)!.Content);
    }

    /// <summary>Revision D, P9-2: a file another program holds (antivirus, a viewer) stops the
    /// commit part-way; the station is put back and Jim reads NOT OK, never a stack trace.</summary>
    [Fact]
    public void A_file_held_open_mid_commit_puts_the_station_back()
    {
        var pack = RoundTrip();
        var before = Snapshot();

        StationApplyResult result;
        using (new FileStream(Path.Combine(Evidence, "Dental", "scan_00003.jpg"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            result = Applier().Apply(ApplyRequest(pack));
        }

        Assert.Contains("put back", result.Problem);
        Assert.Equal(before, Snapshot());
    }

    /// <summary>Revision D, P9-1: the changed file's original was never moved aside, so undo must
    /// not delete it as if it were the update's copy.</summary>
    [Fact]
    public void A_changed_file_whose_original_never_moved_is_kept()
    {
        var pack = RoundTrip();
        var before = Snapshot();

        using (new FileStream(Path.Combine(Evidence, "Dental", "scan_00001.jpg"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Applier().Apply(ApplyRequest(pack));
        }

        Assert.Equal("jpeg one", File.ReadAllText(Path.Combine(Evidence, "Dental", "scan_00001.jpg")));
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Undo_refuses_once_Jim_has_worked_since_the_update()
    {
        Assert.Null(Applier().Apply(ApplyRequest(RoundTrip())).Problem);
        File.AppendAllText(Path.Combine(Jim, "fgscanner.db"), "\nrow:page 77");
        Write(Path.Combine(Evidence, "Dental", "scan_00077.jpg"), "scanned after the update");
        var before = Snapshot();

        var result = Applier().Undo(Jim);

        Assert.Contains("changed since the update", result.Problem);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Undo_refuses_a_station_not_at_rest()
    {
        Assert.Null(Applier().Apply(ApplyRequest(RoundTrip())).Problem);
        Write(Path.Combine(Jim, "fgscanner.db-journal"), "half");

        Assert.Contains("not closed cleanly", Applier().Undo(Jim).Problem);
    }

    [Fact]
    public void Undo_keeps_the_database_it_replaces()
    {
        Assert.Null(Applier().Apply(ApplyRequest(RoundTrip())).Problem);

        Assert.Null(Applier().Undo(Jim).Problem);

        var undone = Directory.GetDirectories(Jim, "apply-undone-*").Single();
        Assert.Contains("row:page 4", File.ReadAllText(Path.Combine(undone, "fgscanner.replaced.db")));
    }

    [Fact]
    public void A_backup_without_a_plan_is_not_undone()
    {
        Assert.Null(Applier().Apply(ApplyRequest(RoundTrip())).Problem);
        Directory.CreateDirectory(Path.Combine(Jim, "apply-backup-29991231-000000"));

        Assert.Null(Applier().Undo(Jim).Problem);
        Assert.Contains("setting:theme=dark", File.ReadAllText(Path.Combine(Jim, "fgscanner.db")));
        Assert.DoesNotContain("row:page 4", File.ReadAllText(Path.Combine(Jim, "fgscanner.db")));
    }

    [Fact]
    public void Apply_refuses_without_room_and_changes_nothing()
    {
        var pack = RoundTrip();
        var before = Snapshot();

        var result = new StationApplier(() => false, _database, _ => 1024).Apply(ApplyRequest(pack));

        Assert.Contains("not enough free space", result.Problem);
        Assert.Equal(before, Snapshot());
    }

    /// <summary>Revision D, P10-3: the pack may not choose which rows the freeze check reads — a
    /// return claiming a subset that Jim's new scans did not touch is refused.</summary>
    [Fact]
    public void A_return_that_chooses_its_own_freeze_tables_is_refused()
    {
        var pack = RoundTrip();
        File.AppendAllText(Path.Combine(Jim, "fgscanner.db"), "\nrow:page 99");
        var manifestPath = Path.Combine(pack, StationManifest.FileName);
        var forged = StationManifest.Parse(File.ReadAllBytes(manifestPath)) with
        {
            BasedOnContent = new StationContent(
                [new StationContentTable("Subset", ["Id"])],
                _database.ContentSha256(Path.Combine(Jim, "fgscanner.db"), [])),
        };
        File.WriteAllBytes(manifestPath, forged.ToJsonBytes());
        var before = Snapshot();

        var result = Applier().Apply(ApplyRequest(pack));

        Assert.Contains("not made from this station's last pack", result.Problem);
        Assert.Equal(before, Snapshot());
    }

    /// <summary>The 0.5.4 sender cannot fingerprint the database, so it keeps a copy of what it
    /// sent; apply works out Jim's own content from that copy.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_send_by_the_054_sender_is_checked_against_its_own_copy(bool jimScannedSince)
    {
        var pack = RoundTrip();
        var freeze = StationFreezeRecord.Read(Jim)! with { Content = null };
        File.WriteAllText(Path.Combine(Jim, StationFreezeRecord.FileName),
            System.Text.Json.JsonSerializer.Serialize(freeze, WebJson));
        File.Copy(Path.Combine(Jim, "fgscanner.db"), Path.Combine(Jim, StationFreezeRecord.SentDatabaseFileName));
        if (jimScannedSince)
        {
            File.AppendAllText(Path.Combine(Jim, "fgscanner.db"), "\nrow:page 99");
        }

        var result = Applier().Apply(ApplyRequest(pack));

        if (jimScannedSince)
        {
            Assert.Contains("changed since it was sent", result.Problem);
        }
        else
        {
            Assert.Null(result.Problem);
        }
    }

    [Fact]
    public void A_new_version_never_started_is_asked_to_start_once()
    {
        var pack = RoundTrip();

        Assert.Contains("start it once", Applier().Apply(ApplyRequest(pack, jimsMigration: "M1")).Problem);
    }

    [Fact]
    public void A_return_naming_a_folder_outside_the_evidence_folder_is_refused()
    {
        var pack = RoundTrip(() => File.AppendAllText(Path.Combine(Franz, "fgscanner.db"), "\npath:\\\\attacker\\share"));
        var before = Snapshot();

        var result = Applier().Apply(ApplyRequest(pack));

        Assert.Contains("outside this station's evidence folder", result.Problem);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void A_return_whose_database_carries_an_extra_table_is_refused()
    {
        var pack = RoundTrip(() => File.AppendAllText(Path.Combine(Franz, "fgscanner.db"), "\nschema:smuggled"));
        var before = Snapshot();

        var result = Applier().Apply(ApplyRequest(pack));

        Assert.Contains("smuggled", result.Problem);
        Assert.Equal(before, Snapshot());
    }

    /// <summary>Re-review R-1: Jim confirms a code, and apply installs exactly that pack — one
    /// swapped in the folder while the two of them compared codes is refused.</summary>
    [Fact]
    public void An_update_other_than_the_one_whose_code_Jim_confirmed_is_refused()
    {
        var pack = RoundTrip();
        var before = Snapshot();

        var result = Applier().Apply(ApplyRequest(pack) with { ExpectedCode = "0000-0000" });

        Assert.Contains("not the one whose code you checked", result.Problem);
        Assert.Equal(before, Snapshot());
    }

    /// <summary>Re-review R-2: a restore that was itself interrupted leaves its mark, and undo
    /// finishes it without asking whether the half-restored station "changed since the update".</summary>
    [Fact]
    public void Undo_finishes_a_restore_that_was_interrupted()
    {
        var pack = RoundTrip();
        var before = Snapshot();
        var applied = Applier().Apply(ApplyRequest(pack));
        File.WriteAllText(Path.Combine(applied.BackupFolder!, "restore-started"), "");
        File.AppendAllText(Path.Combine(Jim, "fgscanner.db"), "\nrow:half put back");

        var undone = Applier().Undo(Jim);

        Assert.Null(undone.Problem);
        Assert.Equal(before, Snapshot());
    }

    /// <summary>Re-review R-4: one undo takes back one update; an older backup still on disk is
    /// not a second thing to undo.</summary>
    [Fact]
    public void Undo_takes_back_only_the_update_the_station_is_on()
    {
        Assert.Null(Applier().Apply(ApplyRequest(RoundTrip())).Problem);
        Assert.Null(Applier().Undo(Jim).Problem);
        var undone = Directory.GetDirectories(Jim, "apply-undone-*").Single();
        Directory.Move(undone, Path.Combine(Jim, "apply-backup-" + Path.GetFileName(undone)["apply-undone-".Length..]));

        Assert.Contains("nothing to undo", Applier().Undo(Jim).Problem);
    }

    [Fact]
    public void Jim_scanning_during_the_freeze_refuses_and_changes_nothing()
    {
        var pack = RoundTrip();
        File.AppendAllText(Path.Combine(Jim, "fgscanner.db"), "\nrow:page 99");
        var before = Snapshot();

        var result = Applier().Apply(ApplyRequest(pack));

        Assert.Contains("changed since it was sent", result.Problem);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Jims_files_changing_during_the_freeze_refuses_and_changes_nothing()
    {
        var pack = RoundTrip();
        Write(Path.Combine(Evidence, "Dental", "scan_00099.jpg"), "a page scanned after sending");
        var before = Snapshot();

        var result = Applier().Apply(ApplyRequest(pack));

        Assert.Contains("evidence/Dental/scan_00099.jpg", result.Problem);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Opening_the_app_or_a_migration_alone_does_not_refuse()
    {
        var pack = RoundTrip();
        File.AppendAllText(Path.Combine(Jim, "fgscanner.db"), "\nschema:OrientationSettled");

        Assert.Null(Applier().Apply(ApplyRequest(pack)).Problem);
    }

    [Theory]
    [InlineData("0.8.1", "M2", "0.8.0")]
    [InlineData("0.8.0", "M3", "M2")]
    public void A_version_or_migration_mismatch_refuses(string version, string migration, string named)
    {
        var pack = RoundTrip();
        var before = Snapshot();

        var result = Applier().Apply(ApplyRequest(pack, version, migration));

        Assert.Contains(named, result.Problem);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void A_pack_not_made_from_Jims_last_send_refuses()
    {
        var pack = RoundTrip();
        Assert.Null(Pack(Jim, "send", at: At.AddMinutes(5)).Problem);

        Assert.Contains("not made from this station's last pack", Applier().Apply(ApplyRequest(pack)).Problem);
    }

    [Fact]
    public void The_app_open_refuses()
    {
        var pack = RoundTrip();
        _appOpen = true;

        Assert.Contains("FG Scanner is open", Applier().Apply(ApplyRequest(pack)).Problem);
    }

    [Fact]
    public void A_leftover_journal_or_an_unsaved_scan_refuses()
    {
        var pack = RoundTrip();
        Write(Path.Combine(Jim, "fgscanner.db-journal"), "half");
        Assert.Contains("not closed cleanly", Applier().Apply(ApplyRequest(pack)).Problem);

        File.Delete(Path.Combine(Jim, "fgscanner.db-journal"));
        Write(Path.Combine(Jim, "recovery", "s1", "page-00001.jpg"), "unsaved");
        Assert.Contains("unsaved scans", Applier().Apply(ApplyRequest(pack)).Problem);
    }

    [Fact]
    public void A_short_file_in_the_pack_refuses_and_changes_nothing()
    {
        var pack = RoundTrip();
        File.WriteAllText(Path.Combine(pack, "evidence", "Dental", "scan_00004.jpg"), "jpeg");
        var before = Snapshot();

        var result = Applier().Apply(ApplyRequest(pack));

        Assert.Contains("changed: evidence/Dental/scan_00004.jpg", result.Problem);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void A_pack_still_downloading_is_not_yet()
    {
        var pack = RoundTrip();
        File.Delete(Path.Combine(pack, StationManifest.FileName));

        var result = Applier().Apply(ApplyRequest(pack));

        Assert.True(result.StillArriving);
    }

    [Fact]
    public void A_database_that_fails_its_integrity_check_changes_nothing()
    {
        var pack = RoundTrip(() => File.AppendAllText(Path.Combine(Franz, "fgscanner.db"), "\ncorrupt"));
        var before = Snapshot();

        var result = Applier().Apply(ApplyRequest(pack));

        Assert.Contains("integrity", result.Problem);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void A_first_pack_opens_into_an_empty_station_whatever_its_version()
    {
        var sent = Pack(Jim, "send");
        var franz = Path.Combine(_root, "franz-empty");
        Directory.Move(Evidence, Evidence + ".jim");

        var result = Applier().Apply(new StationApplyRequest(sent.PackFolder!, franz, Evidence, "0.9.0", "M9", At));

        Assert.Null(result.Problem);
        Assert.Equal("jpeg two", File.ReadAllText(Path.Combine(Evidence, "Dental", "scan_00002.jpg")));
        Assert.Equal("trashed", File.ReadAllText(Path.Combine(franz, "trash", "0001", "scan_00009.jpg")));
        Assert.True(File.Exists(Path.Combine(franz, "fgscanner.db")));
    }

    [Fact]
    public void A_first_pack_refuses_an_evidence_folder_that_already_holds_files()
    {
        var sent = Pack(Jim, "send");

        var result = Applier().Apply(new StationApplyRequest(
            sent.PackFolder!, Path.Combine(_root, "franz-empty"), Evidence, "0.8.0", "M2", At));

        Assert.Contains("already holds files", result.Problem);
    }

    /// <summary>
    /// The database as text: <c>row:</c> lines are evidence content, <c>setting:</c> lines are the
    /// station's own, anything else (a migration's mark) changes the bytes but not the content.
    /// </summary>
    private sealed class FakeDatabase : IStationDatabase
    {
        public StationContent Content(string database) =>
            new([new StationContentTable("Rows", ["line"])], ContentSha256(database, []));

        public string ContentSha256(string database, IReadOnlyList<StationContentTable> tables) =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
                File.ReadAllLines(database).Where(l => l.StartsWith("row:", StringComparison.Ordinal))))));

        public string? SchemaProblem(string incoming, string? receiver, string? lastMigration) =>
            File.ReadAllText(incoming).Contains("schema:smuggled", StringComparison.Ordinal)
                ? "the update's database has a table this version does not: smuggled"
                : null;

        public string? PathOutside(string database, string evidenceRoot) =>
            File.ReadAllLines(database).Where(l => l.StartsWith("path:", StringComparison.Ordinal))
                .Select(l => l["path:".Length..])
                .Where(p => !p.StartsWith(evidenceRoot, StringComparison.OrdinalIgnoreCase))
                .Select(p => $"Groups.DirectoryPath = {p}")
                .FirstOrDefault();

        public void BindToStation(string incoming, string? receiver, string receiverTrash, bool restore = false)
        {
            var settings = receiver is null
                ? []
                : File.ReadAllLines(receiver).Where(l => l.StartsWith("setting:", StringComparison.Ordinal));
            File.WriteAllLines(incoming,
                File.ReadAllLines(incoming).Where(l => !l.StartsWith("setting:", StringComparison.Ordinal)).Concat(settings));
        }

        public bool IntegrityOk(string database) => !File.ReadAllText(database).Contains("corrupt");

        public void RerootEvidence(string database, string fromRoot, string toRoot) =>
            File.WriteAllLines(database, File.ReadAllLines(database).Select(l =>
                l.StartsWith("path:" + fromRoot, StringComparison.OrdinalIgnoreCase) ? "path:" + toRoot + l[("path:" + fromRoot).Length..] : l));
    }
}
