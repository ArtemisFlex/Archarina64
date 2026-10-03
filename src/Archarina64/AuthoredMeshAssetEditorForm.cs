using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using SharpOcarina.Shared;

namespace SharpOcarina
{
    internal sealed class AuthoredMeshAssetEditorForm : Form
    {
        private readonly string path;
        private readonly SharedAssetRepository repository;
        private readonly AuthoredMeshAssetDocument document;
        private readonly CheckBox backface = Check("Backface culling");
        private readonly CheckBox collision = Check("Include in collision");
        private readonly CheckBox alphaMask = Check("Alpha mask");
        private readonly CheckBox pixelated = Check("Pixelated filtering");
        private readonly CheckBox renderLast = Check("Render last");
        private readonly CheckBox ignoreFog = Check("Ignore fog");
        private readonly CheckBox metallic = Check("Metallic");
        private readonly CheckBox decal = Check("Decal");
        private readonly CheckBox billboard = Check("Billboard");
        private readonly CheckBox twoAxis = Check("2-axis billboard");
        private readonly NumericUpDown polygonType = Number(0, 255);
        private readonly NumericUpDown tileS = Number(-2, 2);
        private readonly NumericUpDown tileT = Number(-2, 2);
        private readonly TextBox tint = new TextBox { Width = 100 };

        public AuthoredMeshAssetEditorForm(string path)
            : this(path, null)
        {
        }

        public AuthoredMeshAssetEditorForm(string path, SharedAssetRepository repository)
        {
            this.path = path;
            this.repository = repository;
            document = AuthoredMeshAssetIO.LoadDocument(path, out MeshEditorData ignored);
            Text = "Authored N64 Asset Settings";
            StartPosition = FormStartPosition.CenterParent;
            Width = 440;
            Height = 430;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 0, Padding = new Padding(10), AutoScroll = true };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            Add(table, backface); Add(table, collision); Add(table, alphaMask); Add(table, pixelated);
            Add(table, renderLast); Add(table, ignoreFog); Add(table, metallic); Add(table, decal);
            Add(table, billboard); Add(table, twoAxis);
            Add(table, new Label { Text = "Polygon type", AutoSize = true }, polygonType);
            Add(table, new Label { Text = "Texture tile S", AutoSize = true }, tileS);
            Add(table, new Label { Text = "Texture tile T", AutoSize = true }, tileT);
            Add(table, new Label { Text = "Tint/alpha (ARGB hex)", AutoSize = true }, tint);
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft };
            Button save = new Button { Text = "Save", AutoSize = true };
            Button cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            save.Click += delegate { SaveSettings(); };
            buttons.Controls.Add(save); buttons.Controls.Add(cancel);
            Controls.Add(table); Controls.Add(buttons);
            AcceptButton = save; CancelButton = cancel;
            LoadSettings();
        }

        private static CheckBox Check(string text) { return new CheckBox { Text = text, AutoSize = true }; }
        private static NumericUpDown Number(int min, int max) { return new NumericUpDown { Minimum = min, Maximum = max, Width = 100 }; }
        private static void Add(TableLayoutPanel table, Control control) { table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(control, 0, table.RowCount); table.SetColumnSpan(control, 2); table.RowCount++; }
        private static void Add(TableLayoutPanel table, Control label, Control value) { table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(label, 0, table.RowCount); table.Controls.Add(value, 1, table.RowCount); table.RowCount++; }

        private void LoadSettings()
        {
            if (document == null) return;
            backface.Checked = document.BackfaceCulling;
            collision.Checked = document.AddToCollision;
            alphaMask.Checked = document.AlphaMask;
            pixelated.Checked = document.Pixelated;
            renderLast.Checked = document.RenderLast;
            ignoreFog.Checked = document.IgnoreFog;
            metallic.Checked = document.Metallic;
            decal.Checked = document.Decal;
            billboard.Checked = document.Billboard;
            twoAxis.Checked = document.TwoAxisBillboard;
            polygonType.Value = Math.Max(polygonType.Minimum, Math.Min(polygonType.Maximum, document.PolyType));
            tileS.Value = Math.Max(tileS.Minimum, Math.Min(tileS.Maximum, document.TileS));
            tileT.Value = Math.Max(tileT.Minimum, Math.Min(tileT.Maximum, document.TileT));
            tint.Text = document.TintAlpha.ToString("X8", CultureInfo.InvariantCulture);
        }

        private void SaveSettings()
        {
            if (document == null) return;
            document.BackfaceCulling = backface.Checked;
            document.AddToCollision = collision.Checked;
            document.AlphaMask = alphaMask.Checked;
            document.Pixelated = pixelated.Checked;
            document.RenderLast = renderLast.Checked;
            document.IgnoreFog = ignoreFog.Checked;
            document.Metallic = metallic.Checked;
            document.Decal = decal.Checked;
            document.Billboard = billboard.Checked;
            document.TwoAxisBillboard = twoAxis.Checked;
            document.PolyType = (int)polygonType.Value;
            document.TileS = (int)tileS.Value;
            document.TileT = (int)tileT.Value;
            uint parsed;
            if (!uint.TryParse(tint.Text.Trim().Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed))
            {
                MessageBox.Show(this, "Tint/alpha must be an 8-digit ARGB hexadecimal value.", "Invalid tint", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            document.TintAlpha = parsed;
            AuthoredMeshAssetIO.SaveDocument(path, document);
            if (repository != null) repository.NotifyAssetChanged(path);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
