using pk3DS.Core;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace pk3DS.WinForms;

/// <summary>
/// UI over <see cref="RomExpander"/>: adds Pokemon form slots (for Mega Evolutions and
/// other extra forms) and new item IDs to a loaded ROM, keeping every parallel table in
/// step so the editors can see and edit the new entries.
/// </summary>
public sealed class Expander : Form
{
    private readonly Label L_Status = new();
    private readonly TextBox TB_Log = new();

    private readonly NumericUpDown NUD_Forms = new();
    private readonly NumericUpDown NUD_FormTemplate = new();
    private readonly Button B_Forms = new();
    private readonly CheckBox CHK_Assign = new();

    private readonly NumericUpDown NUD_Items = new();
    private readonly NumericUpDown NUD_ItemTemplate = new();
    private readonly TextBox TB_Name = new();
    private readonly Button B_Items = new();

    public Expander()
    {
        Text = "ROM Expander";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 460);

        L_Status.SetBounds(12, 10, 496, 60);
        L_Status.AutoSize = false;

        var gbForms = new GroupBox { Text = "Pokemon Form Slots" };
        gbForms.SetBounds(12, 76, 496, 114);
        AddLabel(gbForms, "Add slots:", 12, 26);
        NUD_Forms.SetBounds(90, 23, 60, 20);
        NUD_Forms.Minimum = 0; NUD_Forms.Maximum = 200; NUD_Forms.Value = 1;
        AddLabel(gbForms, "Copy stats/moves from species:", 170, 26);
        NUD_FormTemplate.SetBounds(350, 23, 70, 20);
        NUD_FormTemplate.Minimum = 0; NUD_FormTemplate.Maximum = 9999; NUD_FormTemplate.Value = 1;
        CHK_Assign.SetBounds(12, 48, 470, 18);
        CHK_Assign.Checked = true;
        CHK_Assign.Text = "Point that species at the new slots (Form Stats Index + Formes Count)";
        B_Forms.SetBounds(12, 72, 200, 26);
        B_Forms.Text = "Add Form Slots";
        B_Forms.Click += AddForms;
        gbForms.Controls.AddRange([NUD_Forms, NUD_FormTemplate, CHK_Assign, B_Forms]);

        var gbItems = new GroupBox { Text = "Items" };
        gbItems.SetBounds(12, 196, 496, 130);
        AddLabel(gbItems, "Add items:", 12, 26);
        NUD_Items.SetBounds(90, 23, 60, 20);
        NUD_Items.Minimum = 0; NUD_Items.Maximum = 500; NUD_Items.Value = 1;
        AddLabel(gbItems, "Copy from item:", 170, 26);
        NUD_ItemTemplate.SetBounds(280, 23, 70, 20);
        NUD_ItemTemplate.Minimum = 0; NUD_ItemTemplate.Maximum = 9999; NUD_ItemTemplate.Value = 1;
        AddLabel(gbItems, "Name pattern ({id} = item number):", 12, 58);
        TB_Name.SetBounds(220, 55, 260, 20);
        TB_Name.Text = "New Item {id}";
        B_Items.SetBounds(12, 88, 200, 26);
        B_Items.Text = "Add Items";
        B_Items.Click += AddItems;
        gbItems.Controls.AddRange([NUD_Items, NUD_ItemTemplate, TB_Name, B_Items]);

        TB_Log.SetBounds(12, 332, 496, 116);
        TB_Log.Multiline = TB_Log.ReadOnly = true;
        TB_Log.ScrollBars = ScrollBars.Vertical;
        TB_Log.Font = new Font(FontFamily.GenericMonospace, 8.25f);

        Controls.AddRange([L_Status, gbForms, gbItems, TB_Log]);
        Refresh_Counts();
    }

    private static void AddLabel(Control parent, string text, int x, int y)
    {
        parent.Controls.Add(new Label { Text = text, AutoSize = true, Left = x, Top = y });
    }

    private void Refresh_Counts()
    {
        try
        {
            var c = RomExpander.GetCounts(Main.Config);
            // Always show the ID RANGE, never a bare count: the last valid id is
            // count-1, and reading the count as an id is an easy, costly mistake.
            L_Status.Text =
                $"Personal: {c.PersonalRecords} records (entries 0-{c.PersonalRecords - 1})   " +
                $"Level-Up: {c.Learnsets}   Evolution: {c.Evolutions}   Mega: {c.MegaEvos}" +
                Environment.NewLine +
                (c.FormTablesAligned
                    ? "Form tables are in step."
                    : "WARNING: form tables are NOT in step - expansion is blocked.") +
                Environment.NewLine +
                $"Items: {c.Items} (ids 0-{c.Items - 1})   Item text lists found: {c.ItemTextFiles.Count}";
            B_Forms.Enabled = c.FormTablesAligned;
        }
        catch (Exception ex)
        {
            L_Status.Text = "Could not read the ROM tables: " + ex.Message;
            B_Forms.Enabled = B_Items.Enabled = false;
        }
    }

    private void AddForms(object sender, EventArgs e)
    {
        int add = (int)NUD_Forms.Value;
        if (add <= 0)
            return;
        if (Prompt($"Add {add} form slot(s), cloned from species {NUD_FormTemplate.Value}?\n\n" +
                   "This rewrites the personal, level-up, evolution and mega-evolution archives.\n" +
                   "Back up your RomFS first.") != DialogResult.Yes)
            return;

        Run(() => RomExpander.ExpandForms(Main.Config, add, (int)NUD_FormTemplate.Value, CHK_Assign.Checked));
    }

    private void AddItems(object sender, EventArgs e)
    {
        int add = (int)NUD_Items.Value;
        if (add <= 0)
            return;
        if (Prompt($"Add {add} item(s), cloned from item {NUD_ItemTemplate.Value}?\n\n" +
                   "Item names are written when pk3DS closes normally.") != DialogResult.Yes)
            return;

        Run(() => RomExpander.AppendItems(Main.Config, add, (int)NUD_ItemTemplate.Value, TB_Name.Text));
    }

    private static DialogResult Prompt(string message)
    {
        return MessageBox.Show(message, "ROM Expander", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
    }

    private void Run(Func<string> action)
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            TB_Log.Text = action().Replace("\n", Environment.NewLine);
        }
        catch (Exception ex)
        {
            TB_Log.Text = "Failed: " + ex;
        }
        finally
        {
            Cursor = Cursors.Default;
            Refresh_Counts();
        }
    }
}
