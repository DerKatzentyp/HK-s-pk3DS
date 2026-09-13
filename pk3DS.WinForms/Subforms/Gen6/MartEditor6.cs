using pk3DS.Core;
using System;
using System.IO;
using System.Windows.Forms;

namespace pk3DS.WinForms;

public partial class MartEditor6 : Form
{
    public MartEditor6()
    {
        InitializeComponent();
        if (Main.ExeFSPath == null) { WinFormsUtil.Alert("No exeFS code to load."); Close(); }
        string[] files = Directory.GetFiles(Main.ExeFSPath);
        if (!File.Exists(files[0]) || !Path.GetFileNameWithoutExtension(files[0]).Contains("code")) { WinFormsUtil.Alert("No .code.bin detected."); Close(); }
        data = File.ReadAllBytes(files[0]);
        if (data.Length % 0x200 != 0) { WinFormsUtil.Alert(".code.bin not decompressed. Aborting."); Close(); }
        offset = GetDataOffset(data);
        codebin = files[0];
        itemlist[0] = "";
        SetupDGV();

        if (Main.Config.ORAS)
        {
            PointerTableAddr = 0x005F3CD8;
            CountTableAddr = 0x0057A808;
            FreeSpaceFileStart = 0x4EBA14;
            FreeSpaceFileEnd = 0x4F3CB8;
        }
        else
        {
            PointerTableAddr = 0x005ADCE8;
            CountTableAddr = 0x0053C6E4;
            FreeSpaceFileStart = 0x4A5A44;
            FreeSpaceFileEnd = 0x4ADCBC; // verified all-zero up to here in BOTH Pokémon X and Y
        }
        freeSpaceCursor = FreeSpaceFileStart;

        string reason = "";
        usePointerTables = ValidatePointerTables(out reason);
        if (!usePointerTables)
        {
            WinFormsUtil.Alert("This code.bin's shop tables don't match the verified layout." +
                                " Falling back to fixed-size legacy editing — resizing shops is disabled for safety." +
                                Environment.NewLine + Environment.NewLine + "Diagnostic: " + reason);
        }
        else if (Main.Config.ORAS)
        {
            WinFormsUtil.Alert("ORAS shop table support is new and only statically verified (not yet confirmed in-game)." +
                                " Structural checks passed, but please spot-check a shop or two in an emulator before relying on this.");
        }
        if (usePointerTables)
            InitFreeSpaceCursor();

        CB_Location.Items.AddRange(locations);
        CB_Location.SelectedIndex = 0;
    }

    private static int GetDataOffset(byte[] data)
    {
        byte[] vanilla =
        [
            0x00, 0x72, 0x6F, 0x6D, 0x3A, 0x2F, 0x44, 0x6C, 0x6C, 0x53, 0x74, 0x61, 0x72, 0x74, 0x4D, 0x65,
            0x6E, 0x75, 0x2E, 0x63, 0x72, 0x6F, 0x00,
        ];
        int offset = Util.IndexOfBytes(data, vanilla, 0x400000, 0);
        if (offset >= 0)
            return offset + vanilla.Length;

        byte[] patched =
        [
            0x00, 0x72, 0x6F, 0x6D, 0x32, 0x3A, 0x2F, 0x44, 0x6C, 0x6C, 0x53, 0x74, 0x61, 0x72, 0x74, 0x4D,
            0x65, 0x6E, 0x75, 0x2E, 0x63, 0x72, 0x6F, 0x00, 0xFF,
        ];
        offset = Util.IndexOfBytes(data, patched, 0x400000, 0);

        if (offset >= 0)
            return offset + patched.Length;

        return -1;
    }

    private readonly string codebin;
    private readonly string[] itemlist = Main.Config.GetText(TextName.ItemNames);
    private readonly byte[] data;

    private readonly byte[] entries = Main.Config.ORAS
        ?
        [
            3, 10, 14, 17, 18, 19, 19, 19, 19, // General
            1,
            9, 6, 4, 3, 8,
            8, 3, 3, 4,
            3, 6, 8,
            7, 4,
        ]
        :
        [
            2, 11, 14, 17, 18, 19, 19, 19, 19, // General
            1, // Unused
            4, 10, 3, 9, 1, 1, // Misc
            3, 3, // Balls
            5, 5, // TMs
            6, // Vitamins
            7, // Balls
            5, // TMs
            5, // TMs
            8, // Battle
            3, // Balls
        ];

    private readonly int offset;
    private int dataoffset;

    // --- Verified real tables (Pokémon X, this exact dump only — see ValidatePointerTables) ---
    // shop_item_id_lists: array of 4-byte pointers (one per shop), memory addresses.
    // shop_item_counts:   array of 1-byte counts (one per shop).
    // XY addresses verified via live GDB debugging on real hardware + Azahar.
    // ORAS addresses found via static analysis only (perfect 24/24 vanilla count match,
    // consistent ascending pointer table) — NOT yet live-verified. Treat with extra
    // caution until spot-checked in-game (the XY addresses looked just as solid
    // pre-verification, and live testing still caught a real labeling bug).
    private const uint LoadBase = 0x00100000;
    private readonly uint PointerTableAddr;
    private readonly uint CountTableAddr;
    private readonly int FreeSpaceFileStart;
    private readonly int FreeSpaceFileEnd;
    private const int GameHardCap = 60; // per community report (confirmed for XY only); unverified for ORAS

    private readonly bool usePointerTables;
    private int freeSpaceCursor;
    private int loadedCount;

    private bool ValidatePointerTables(out string reason)
    {
        // Fast path: an untouched dump matches the known vanilla counts exactly.
        int countTableOffset = (int)(CountTableAddr - LoadBase);
        int[] expected = Main.Config.ORAS
            ? [3, 10, 14, 17, 18, 19, 19, 19, 19]
            : [2, 11, 14, 17, 18, 19, 19, 19, 19];
        if (countTableOffset >= 0 && countTableOffset + expected.Length <= data.Length)
        {
            bool pristineMatch = true;
            for (int i = 0; i < expected.Length; i++)
            {
                if (data[countTableOffset + i] != expected[i]) { pristineMatch = false; break; }
            }
            if (pristineMatch)
            {
                reason = "";
                return true;
            }
        }

        // Slow path: the file may have already been edited by this tool in a prior
        // session (e.g. a badge shop's count no longer matches vanilla). That's expected
        // and fine — fall back to checking the tables are still structurally sane instead
        // of assuming a mismatched revision.
        int pointerTableOffset = (int)(PointerTableAddr - LoadBase);
        if (pointerTableOffset < 0)
        {
            reason = $"Pointer table address 0x{PointerTableAddr:X} resolves before the start of the file (base 0x{LoadBase:X}).";
            return false;
        }
        for (int i = 0; i < entries.Length; i++)
        {
            int tableEntryOffset = pointerTableOffset + (4 * i);
            if (tableEntryOffset < 0 || tableEntryOffset + 4 > data.Length)
            {
                reason = $"Shop index {i}: pointer table entry at file offset 0x{tableEntryOffset:X} is out of bounds (file length 0x{data.Length:X}).";
                return false;
            }

            int count = GetCount(i);
            if (count is <= 0 or > 100)
            {
                reason = $"Shop index {i}: count byte = {count} (expected 1-100). Raw byte at file offset 0x{(countTableOffset + i):X}.";
                return false;
            }

            int ptrOffset = GetPointerFileOffset(i);
            if (ptrOffset < 0 || ptrOffset + (2 * count) > data.Length)
            {
                reason = $"Shop index {i}: item-list pointer resolves to file offset 0x{ptrOffset:X}, which with count={count} runs out of bounds (file length 0x{data.Length:X}).";
                return false;
            }
        }
        reason = "";
        return true;
    }

    private void InitFreeSpaceCursor()
    {
        // If shops were already relocated into the free-space pocket in a prior session,
        // detect them so we never allocate over already-used space.
        for (int i = 0; i < entries.Length; i++)
        {
            int ptrOffset = GetPointerFileOffset(i);
            if (ptrOffset < FreeSpaceFileStart || ptrOffset >= FreeSpaceFileEnd)
                continue;
            int usedEnd = ptrOffset + (GetCount(i) * 2);
            if (usedEnd > freeSpaceCursor)
                freeSpaceCursor = usedEnd;
        }
        freeSpaceCursor = (freeSpaceCursor + 3) & ~3;
    }

    private int GetCount(int index) => data[(int)(CountTableAddr - LoadBase) + index];
    private void SetCount(int index, int value) => data[(int)(CountTableAddr - LoadBase) + index] = (byte)value;

    private int GetPointerFileOffset(int index)
    {
        int tableOffset = (int)(PointerTableAddr - LoadBase) + (4 * index);
        uint pointer = BitConverter.ToUInt32(data, tableOffset);
        return (int)(pointer - LoadBase);
    }

    private void SetPointerFileOffset(int index, int fileOffset)
    {
        int tableOffset = (int)(PointerTableAddr - LoadBase) + (4 * index);
        uint pointer = (uint)(fileOffset + LoadBase);
        Array.Copy(BitConverter.GetBytes(pointer), 0, data, tableOffset, 4);
    }

    private readonly string[] locations = Main.Config.ORAS
        ?
        [
            "No Gym Badges [After Pokédex]", "1 Gym Badge", "2 Gym Badges", "3 Gym Badges", "4 Gym Badges", "5 Gym Badges", "6 Gym Badges", "7 Gym Badges", "8 Gym Badges",
            "No Gym Badges [Before Pokédex]",
            "Slateport Market [Incenses]", "Slateport Market [Vitamins]", "Slateport Market [TMs]", "Rustboro City [Poké Balls]", "Slateport City [X Items]",
            "Mauville City [TMs]", "Verdanturf Town [Poké Balls]", "Fallarbor Town [Poké Balls]", "Lavaridge Town [Herbs]",
            "Lilycove Dept Store, 2F Left [Run Away Items]", "Lilycove Dept Store, 3F Left [Vitamins]", "Lilycove Dept Store, 3F Right [X Items]",
            "Lilycove Dept Store, 4F Left [Offensive TMs]", "Lilycove Dept Store, 4F Right [Defensive TMs]",
        ]
        :
        [
            "No Gym Badges", "1 Gym Badge", "2 Gym Badges", "3 Gym Badges", "4 Gym Badges", "5 Gym Badges", "6 Gym Badges", "7 Gym Badges", "8 Gym Badges",
            "Unused",
            "Lumiose City [Herboriste]", "Lumiose City [Poké Ball Boutique]", "Lumiose City [Stone Emporium]", "Coumarine City [Incenses]", "Aquacorde Town [Poké Ball]", "Aquacorde Town [Potion]",
            "Lumiose City North Boulevard [Poké Balls]", "Cyllage City [Poké Balls]",
            "Shalour City [TMs]", "Lumiose City South Boulevard [TMs]",
            "Laverre City [Vitamins]",
            "Snowbelle City [Poké Balls]",
            "Kiloude City [TMs]",
            "Anistar City [TMs]",
            "Santalune City [X Items]",
            "Coumarine City [Poké Balls]",
        ];

    private void GetDataOffset(int index)
    {
        dataoffset = offset; // reset
        for (int i = 0; i < index; i++)
            dataoffset += 2 * entries[i];
    }

    private void SetupDGV()
    {
        var dgvIndex = new DataGridViewTextBoxColumn();
        {
            dgvIndex.HeaderText = "Index";
            dgvIndex.DisplayIndex = 0;
            dgvIndex.Width = 45;
            dgvIndex.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        var dgvItem = new DataGridViewComboBoxColumn();
        {
            dgvItem.HeaderText = "Item";
            dgvItem.DisplayIndex = 1;
            dgvItem.Items.AddRange(itemlist); // add only the Names

            dgvItem.Width = 135;
            dgvItem.FlatStyle = FlatStyle.Flat;
        }
        dgv.Columns.Add(dgvIndex);
        dgv.Columns.Add(dgvItem);
    }

    private int entry = -1;

    private void ChangeIndex(object sender, EventArgs e)
    {
        if (entry > -1) SetList();
        entry = CB_Location.SelectedIndex;
        GetList();
    }

    private void GetList()
    {
        dgv.Rows.Clear();
        int count = usePointerTables ? GetCount(entry) : entries[entry];
        loadedCount = count;
        dgv.Rows.Add(count);
        if (usePointerTables)
        {
            dgv.AllowUserToAddRows = true;
            dgv.AllowUserToDeleteRows = true;
            dataoffset = GetPointerFileOffset(entry);
        }
        else
        {
            GetDataOffset(entry);
        }
        for (int i = 0; i < count; i++)
        {
            dgv.Rows[i].Cells[0].Value = i.ToString();
            dgv.Rows[i].Cells[1].Value = itemlist[BitConverter.ToUInt16(data, dataoffset + (2 * i))];
        }
    }

    private void SetList()
    {
        int count = 0;
        foreach (DataGridViewRow row in dgv.Rows)
        {
            if (!row.IsNewRow)
                count++;
        }
        if (count == 0)
            return;

        if (!usePointerTables)
        {
            // Legacy fixed-layout write (ORAS, or an unverified code.bin) — same as before, in place only.
            for (int i = 0; i < count && i < dgv.Rows.Count; i++)
                Array.Copy(BitConverter.GetBytes((ushort)Array.IndexOf(itemlist, dgv.Rows[i].Cells[1].Value)), 0, data, dataoffset + (2 * i), 2);
            return;
        }

        // Any row left blank (no item chosen) is treated as "delete this slot" — it's
        // excluded from what gets written, rather than written as item ID 0, which is
        // known to freeze the game on real hardware. This means you don't need to
        // fight the grid's row-deletion UI: just leave a row's item blank and save.
        var realItemIds = new System.Collections.Generic.List<ushort>();
        foreach (DataGridViewRow row in dgv.Rows)
        {
            if (row.IsNewRow)
                continue;
            object val = row.Cells[1].Value;
            if (val is not string s || s.Length == 0)
                continue; // blank row -> treated as removed, not written as item 0
            int idx = Array.IndexOf(itemlist, val);
            if (idx <= 0)
                continue; // unrecognized or blank entry -> also skipped defensively
            realItemIds.Add((ushort)idx);
        }
        count = realItemIds.Count;
        if (count == 0)
        {
            WinFormsUtil.Alert("Every row is blank — refusing to save a shop with zero items. No changes were written for this shop.");
            return;
        }

        if (count > GameHardCap)
        {
            WinFormsUtil.Alert($"{count} items exceeds the game's known hard cap of {GameHardCap} per shop." +
                                " These extra slots likely won't work without additional code patches. Trim the list, or proceed at your own risk.");
        }

        bool needsRelocation = count != loadedCount;

        int writeOffset = dataoffset;
        if (needsRelocation)
        {
            int neededBytes = count * 2;
            if (freeSpaceCursor + neededBytes > FreeSpaceFileEnd)
            {
                WinFormsUtil.Alert("Not enough free space left in the reserved pocket for this change. " +
                                    "No changes were written for this shop.");
                return;
            }
            writeOffset = freeSpaceCursor;
            freeSpaceCursor += neededBytes;
            freeSpaceCursor = (freeSpaceCursor + 3) & ~3; // 4-byte align every allocation --
                                                            // every previously-reported-broken shop
                                                            // was found sitting at a misaligned address
        }

        for (int r = 0; r < realItemIds.Count; r++)
            Array.Copy(BitConverter.GetBytes(realItemIds[r]), 0, data, writeOffset + (2 * r), 2);

        if (needsRelocation)
        {
            SetPointerFileOffset(entry, writeOffset);
            SetCount(entry, count);
            dataoffset = writeOffset;
        }
        loadedCount = count;
    }

    private void B_Save_Click(object sender, EventArgs e)
    {
        if (entry > -1) SetList();
        File.WriteAllBytes(codebin, data);
        Close();
    }

    private void B_Cancel_Click(object sender, EventArgs e)
    {
        Close();
    }

    private void B_Randomize_Click(object sender, EventArgs e)
    {
        if (DialogResult.Yes != WinFormsUtil.Prompt(MessageBoxButtons.YesNoCancel, "Randomize mart inventories?"))
            return;

        int[] validItems = Randomizer.GetRandomItemList();

        int ctr = 0;
        Util.Shuffle(validItems);

        bool specialOnly = DialogResult.Yes == WinFormsUtil.Prompt(MessageBoxButtons.YesNo, "Randomize only special marts?", "Will leave regular necessities intact.");
        int start = specialOnly ? 10 : 0;
        for (int i = start; i < CB_Location.Items.Count; i++)
        {
            CB_Location.SelectedIndex = i;
            foreach (DataGridViewRow row in dgv.Rows)
            {
                if (row.IsNewRow) continue;
                int currentItem = Array.IndexOf(itemlist, row.Cells[1].Value);
                if (CHK_XItems.Checked && MartEditor7.XItems.Contains(currentItem))
                    continue;
                if (MartEditor7.BannedItems.Contains(currentItem))
                    continue;
                row.Cells[1].Value = itemlist[validItems[ctr++]];
                if (ctr <= validItems.Length) continue;
                Util.Shuffle(validItems); ctr = 0;
            }
        }
        WinFormsUtil.Alert("Randomized!");
    }
}
