using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using pk3DS.Core.CTR;

namespace pk3DS.Core;

/// <summary>
/// Grows the ROM's parallel data tables so a hack can add new Pokémon forms
/// (Mega Evolutions, regional-style variants) and new items beyond the vanilla counts.
/// </summary>
/// <remarks>
/// <para><b>Form slots.</b> Four archives are indexed by the same form index and are
/// loaded together at boot: <c>levelup</c>, <c>evolution</c>, <c>megaevo</c> and
/// <c>personal</c>. Growing any one of them alone makes the game index past the end of
/// the others — the result is a black screen with no crash handler. They must grow
/// together, which is what <see cref="ExpandForms"/> does.</para>
/// <para>The <c>personal</c> archive is special: the game reads the table from the
/// <b>last file</b> in the GARC (a blob of every record concatenated), not from the
/// individual files. Appending a file to the end therefore replaces the table with a
/// single record. The blob is rebuilt here and kept last.</para>
/// <para><b>Items.</b> The item archive is grown alongside every text sub-file that
/// holds one line per item. Those are detected by line count rather than hard-coded,
/// so this works across XY / ORAS / SM, which use different sub-file numbers and a
/// different number of item text lists.</para>
/// <para>New entries are clones of an existing entry, never zero-filled: a real 3DS is
/// far less tolerant of empty records than an emulator.</para>
/// </remarks>
public static class RomExpander
{
    public sealed class Counts
    {
        public int PersonalRecords, PersonalFiles, Learnsets, Evolutions, MegaEvos;
        public int Items;
        public List<(int Index, int Lines)> ItemTextFiles = [];
        public int PersonalEntrySize;

        public bool FormTablesAligned =>
            Learnsets == Evolutions && Evolutions == MegaEvos && MegaEvos == PersonalRecords;
    }

    /// <summary>Reads the current size of every table this class can grow.</summary>
    public static Counts GetCounts(GameConfig cfg)
    {
        var c = new Counts();
        var personal = cfg.GetGARCData("personal");
        c.PersonalFiles = personal.FileCount;
        c.PersonalEntrySize = personal.GetFile(0).Length;
        c.PersonalRecords = personal.GetFile(personal.FileCount - 1).Length / c.PersonalEntrySize;

        c.Learnsets = cfg.GetGARCData("levelup").FileCount;
        c.Evolutions = cfg.GetGARCData("evolution").FileCount;
        c.MegaEvos = TryCount(cfg, "megaevo");
        c.Items = cfg.GetGARCData("item").FileCount;

        foreach (var (idx, lines) in ItemTextCandidates(cfg, c.Items))
            c.ItemTextFiles.Add((idx, lines));
        return c;
    }

    private static int TryCount(GameConfig cfg, string name)
    {
        try { return cfg.GetGARCData(name).FileCount; }
        catch { return -1; }
    }

    /// <summary>Text sub-files holding exactly one line per item.</summary>
    private static IEnumerable<(int Index, int Lines)> ItemTextCandidates(GameConfig cfg, int itemCount)
    {
        var all = cfg.GameTextStrings;
        if (all == null)
            yield break;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].Length == itemCount)
                yield return (i, all[i].Length);
        }
    }

    /// <summary>
    /// Adds <paramref name="add"/> form slots to all four form-indexed archives at once.
    /// New records clone <paramref name="template"/> (a species index).
    /// </summary>
    /// <summary>Offsets shared by XY/ORAS/SM personal records.</summary>
    private const int OFS_FormStatsIndex = 0x1C;   // u16 - first extra form's record index
    private const int OFS_FormeCount     = 0x20;   // u8  - forms INCLUDING the base

    public static string ExpandForms(GameConfig cfg, int add, int template, bool assignToTemplate = true)
    {
        if (add <= 0)
            return "Nothing to do.";

        var before = GetCounts(cfg);
        if (!before.FormTablesAligned)
        {
            return "Refusing to expand: the form tables are already out of step " +
                   $"(personal {before.PersonalRecords}, level-up {before.Learnsets}, " +
                   $"evolution {before.Evolutions}, mega {before.MegaEvos}). " +
                   "Restore a matching set first.";
        }

        var log = new StringBuilder();
        int size = before.PersonalEntrySize;

        // ---- personal: records live in the trailing blob, which must stay last ----
        var gp = cfg.GetGARCData("personal");
        byte[] blob = gp.GetFile(gp.FileCount - 1);
        var records = new List<byte[]>();
        for (int i = 0; i < blob.Length / size; i++)
            records.Add(blob.Skip(i * size).Take(size).ToArray());

        if (template < 0 || template >= records.Count)
            return $"Template species {template} is out of range (0-{records.Count - 1}).";

        for (int i = 0; i < add; i++)
            records.Add((byte[])records[template].Clone());

        // Point the template species at the new slots. Without this the records exist
        // but nothing references them: the game never loads them and pk3DS cannot wire
        // them up either, because it never exposes FormStatsIndex.
        string assignNote;
        if (!assignToTemplate)
        {
            assignNote = $"Template species {template} was NOT modified - the new slots are " +
                         "unreferenced until something points at them.";
        }
        else if (size <= OFS_FormeCount)
        {
            assignNote = "Personal records are too small for this game's form fields; " +
                         "the template species was not modified.";
        }
        else if (records[template][OFS_FormeCount] > 1)
        {
            int have = records[template][OFS_FormeCount];
            int at = BitConverter.ToUInt16(records[template], OFS_FormStatsIndex);
            assignNote = $"Template species {template} already has {have} forms at index {at}. " +
                         "Its forms must stay contiguous, so it was left alone - assign the new " +
                         "slots to a species that has none.";
        }
        else
        {
            BitConverter.GetBytes((ushort)before.PersonalRecords)
                        .CopyTo(records[template], OFS_FormStatsIndex);
            records[template][OFS_FormeCount] = (byte)(1 + add);
            assignNote = $"Species {template}: Form Stats Index = {before.PersonalRecords}, " +
                         $"Formes Count = {1 + add}.";
        }

        var personalFiles = new byte[records.Count + 1][];
        for (int i = 0; i < records.Count; i++)
            personalFiles[i] = records[i];
        personalFiles[records.Count] = records.SelectMany(r => r).ToArray();
        gp.SetFilesResize(personalFiles);
        gp.Save();
        log.AppendLine($"personal   {before.PersonalRecords} -> {records.Count} records ({personalFiles.Length} files, blob rebuilt)");

        // ---- level-up learnsets: clone the template's, never leave one empty ----
        var gl = cfg.GetGARCData("levelup");
        var learn = gl.Files.ToList();
        for (int i = 0; i < add; i++)
            learn.Add((byte[])learn[template].Clone());
        gl.SetFilesResize([.. learn]);
        gl.Save();
        log.AppendLine($"level-up   {before.Learnsets} -> {learn.Count}");

        // ---- evolutions / mega evolutions: vanilla form slots hold zeroed entries ----
        log.AppendLine(AppendBlank(cfg, "evolution", add, before.Evolutions));
        if (before.MegaEvos > 0)
            log.AppendLine(AppendBlank(cfg, "megaevo", add, before.MegaEvos));

        cfg.InitializePersonal();
        cfg.InitializeLearnset();
        cfg.InitializeEvos();

        log.AppendLine();
        log.AppendLine($"Added {add} form slots: {before.PersonalRecords} .. {records.Count - 1}.");
        log.AppendLine(assignNote);
        log.AppendLine();
        log.AppendLine("Edit the new entries in the Personal editor, and add a Mega Evolution");
        log.AppendLine("entry if that is what they are for. The model/sprite is separate: a new");
        log.AppendLine("form shows no sprite in pk3DS and uses the base form's model in game.");
        return log.ToString();
    }

    private static string AppendBlank(GameConfig cfg, string garc, int add, int before)
    {
        var g = cfg.GetGARCData(garc);
        var files = g.Files.ToList();
        int len = files[0].Length;
        for (int i = 0; i < add; i++)
            files.Add(new byte[len]);
        g.SetFilesResize([.. files]);
        g.Save();
        return $"{garc,-10} {before} -> {files.Count}";
    }

    /// <summary>
    /// Appends <paramref name="add"/> items, cloning <paramref name="template"/>, and
    /// extends every item text list so the new IDs are named and usable in the editors.
    /// </summary>
    public static string AppendItems(GameConfig cfg, int add, int template, string namePattern)
    {
        if (add <= 0)
            return "Nothing to do.";

        if (cfg.GameTextStrings == null)
            return "Game text is not loaded, so the new items could not be named. Open the ROM fully first.";

        var before = GetCounts(cfg);
        if (before.ItemTextFiles.Count == 0)
            return "No text list matches the current item count, so new items would be nameless. Aborting.";

        var gi = cfg.GetGARCData("item");
        var files = gi.Files.ToList();
        if (template < 0 || template >= files.Count)
            return $"Template item {template} is out of range (0-{files.Count - 1}).";

        for (int i = 0; i < add; i++)
            files.Add((byte[])files[template].Clone());
        gi.SetFilesResize([.. files]);
        gi.Save();

        var log = new StringBuilder();
        log.AppendLine($"item       {before.Items} -> {files.Count}");

        int flavour = -1;
        try { flavour = cfg.GetGameTextIndex(TextName.ItemFlavor); } catch { /* not all games */ }

        foreach (var (idx, _) in before.ItemTextFiles)
        {
            var lines = cfg.GameTextStrings[idx].ToList();
            string templateLine = lines[template];
            for (int i = 0; i < add; i++)
            {
                int id = before.Items + i;
                lines.Add(idx == flavour
                    ? "A new item awaiting a description."
                    : RenameKeepingVariables(templateLine, namePattern.Replace("{id}", id.ToString())));
            }
            cfg.GameTextStrings[idx] = [.. lines];
            log.AppendLine($"text {idx,-5} {before.Items} -> {lines.Count} lines");
        }

        log.AppendLine(ExtendOtherLanguages(cfg, add, before, namePattern));
        log.AppendLine();
        log.AppendLine($"Added items {before.Items} .. {files.Count - 1}  (the last id is {files.Count - 1}, not {files.Count}).");
        log.AppendLine("Text for the loaded language is written when pk3DS closes.");
        return log.ToString();
    }


    /// <summary>
    /// Appends the same item lines to every OTHER language slot of the game-text archive.
    /// The loaded language is skipped: pk3DS writes that one itself from
    /// <c>GameTextStrings</c> when it closes, so touching it here would double up.
    /// Without this, a game running in any other language shows a blank item name -
    /// which is exactly what an Italian save did.
    /// </summary>
    private static string ExtendOtherLanguages(GameConfig cfg, int add, Counts before, string namePattern)
    {
        var baseRef = cfg.GetGARCReference("gametext");
        if (!baseRef.LanguageVariant)
            return "Game text is not language-split for this game; nothing else to do.";

        // A sibling language slot has the SAME sub-file count as the loaded one.
        // Without this guard the scan walks off the end of the language range into
        // unrelated archives (a/0/8/x in XY) and would edit them if a line count
        // happened to match.
        int expectFiles;
        try { expectFiles = cfg.GetGARCData("gametext").FileCount; }
        catch { return "Could not read the game-text archive."; }

        var log = new StringBuilder();
        int done = 0, skipped = 0, notSibling = 0;
        for (int lang = 0; lang < 16; lang++)
        {
            if (lang == cfg.Language)
                continue;
            // NOTE: cfg.GetGARCByReference() cannot be used here. It takes a reference
            // but resolves the path BY NAME, re-applying cfg.Language - so every slot
            // would come back as the currently loaded language. Load by explicit path.
            var gr = baseRef.GetRelativeGARC(lang, baseRef.Name);
            string path = Path.Combine(cfg.RomFS, gr.Reference);
            if (!File.Exists(path))
                continue;               // that language slot does not exist

            GARCFile g;
            try { g = new GARCFile(new GARC.MemGARC(File.ReadAllBytes(path)), gr, path); }
            catch { continue; }
            if (g.FileCount != expectFiles)
            { notSibling++; continue; }  // different archive, not a language sibling

            var files = g.Files;
            bool touched = false;
            foreach (var (idx, _) in before.ItemTextFiles)
            {
                if (idx < 0 || idx >= files.Length)
                    continue;
                string[] lines;
                try { lines = new TextFile(cfg, files[idx], cfg.RemapCharacters).Lines; }
                catch { continue; }
                if (lines.Length != before.Items)
                { skipped++; continue; }   // not the same list in this slot - leave it alone

                string templateLine = lines[^1];
                var list = lines.ToList();
                for (int i = 0; i < add; i++)
                {
                    int id = before.Items + i;
                    list.Add(RenameKeepingVariables(templateLine, namePattern.Replace("{id}", id.ToString())));
                }
                files[idx] = TextFile.GetBytes(cfg, [.. list], cfg.RemapCharacters);
                touched = true;
            }
            if (!touched)
                continue;
            g.Files = files;              // same file count - the plain setter is correct here
            g.Save();
            done++;
            log.Append($"lang {lang} ");
        }
        string tail = (skipped > 0 ? $"  [{skipped} list(s) skipped: line count did not match]" : "")
                    + (notSibling > 0 ? $"  [{notSibling} non-language archive(s) ignored]" : "");
        string hint = skipped > 0
            ? Environment.NewLine +
              "  A skipped list is already out of step with the item count - that slot was" + Environment.NewLine +
              "  left untouched on purpose. Start from a clean RomFS so every language" + Environment.NewLine +
              "  slot is extended together."
            : "";
        return done == 0
            ? "No other language slots were updated." + tail + hint
            : $"Also extended {done} other language slot(s): {log.ToString().Trim()}" + tail + hint;
    }

    /// <summary>
    /// Swaps the leading name of a text line while preserving everything after it.
    /// Some item name lists append a pluralisation variable to the name; dropping it
    /// leaves the game parsing a malformed variable, which crashes when the line is drawn.
    /// </summary>
    private static string RenameKeepingVariables(string templateLine, string newName)
    {
        for (int i = 0; i < templateLine.Length; i++)
        {
            if (templateLine[i] != '[')
                continue;
            if (i > 0 && templateLine[i - 1] == '\\') // an escaped literal bracket, not a variable
                continue;
            return newName + templateLine[i..];
        }
        return newName;
    }
}
