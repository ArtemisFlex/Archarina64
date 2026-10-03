using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using OpenTK;

namespace SharpOcarina
{
    internal sealed class RoomGeometryEditor : Form
    {
        private readonly MainForm owner;
        private readonly ComboBox type = new ComboBox();
        private readonly TextBox name = new TextBox();
        private readonly TextBox material = new TextBox();
        private readonly TextBox faceMaterials = new TextBox();
        private readonly TextBox texture = new TextBox();
        private readonly TextBox minX = new TextBox();
        private readonly TextBox minY = new TextBox();
        private readonly TextBox minZ = new TextBox();
        private readonly TextBox maxX = new TextBox();
        private readonly TextBox maxY = new TextBox();
        private readonly TextBox maxZ = new TextBox();
        private readonly TextBox scaleX = new TextBox();
        private readonly TextBox scaleY = new TextBox();
        private readonly TextBox scaleZ = new TextBox();
        private readonly TextBox polytype = new TextBox();
        private readonly CheckBox collision = new CheckBox();
        private readonly int editingIndex;

        public RoomGeometryEditor(MainForm owner)
            : this(owner, -1)
        {
        }

        public RoomGeometryEditor(MainForm owner, int editingIndex)
        {
            this.owner = owner;
            this.editingIndex = editingIndex;
            Text = "Room Geometry Authoring";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(430, 450);

            type.DropDownStyle = ComboBoxStyle.DropDownList;
            type.Items.AddRange(new object[] { "Cube", "Plane", "Triangle", "Ramp" });
            type.SelectedIndex = 0;

            name.Text = "Primitive";
            material.Text = "Default";
            faceMaterials.Text = "Optional: floor;wall;roof (one per face)";
            minX.Text = "-50"; minY.Text = "0"; minZ.Text = "-50";
            maxX.Text = "50"; maxY.Text = "100"; maxZ.Text = "50";
            scaleX.Text = "1"; scaleY.Text = "1"; scaleZ.Text = "1";
            polytype.Text = "0";
            collision.Text = "Add matching collision geometry";
            collision.Checked = true;
            texture.Text = "Optional texture path (relative to the room model)";

            ZScene.ZRoom.RoomAuthoringPrimitive saved = editingIndex >= 0
                ? owner.GetSelectedRoomAuthoringPrimitive(editingIndex)
                : null;
            if (saved != null)
            {
                type.SelectedIndex = TypeIndex((RoomPrimitiveType)saved.Type);
                name.Text = saved.Name;
                material.Text = saved.MaterialName;
                faceMaterials.Text = saved.FaceMaterials == null || saved.FaceMaterials.Count == 0
                    ? "Optional: floor;wall;roof (one per face)"
                    : string.Join(";", saved.FaceMaterials);
                texture.Text = string.IsNullOrEmpty(saved.TexturePath) ? "Optional texture path (relative to the room model)" : saved.TexturePath;
                minX.Text = saved.MinX.ToString(CultureInfo.InvariantCulture);
                minY.Text = saved.MinY.ToString(CultureInfo.InvariantCulture);
                minZ.Text = saved.MinZ.ToString(CultureInfo.InvariantCulture);
                maxX.Text = saved.MaxX.ToString(CultureInfo.InvariantCulture);
                maxY.Text = saved.MaxY.ToString(CultureInfo.InvariantCulture);
                maxZ.Text = saved.MaxZ.ToString(CultureInfo.InvariantCulture);
                scaleX.Text = saved.ScaleX.ToString(CultureInfo.InvariantCulture);
                scaleY.Text = saved.ScaleY.ToString(CultureInfo.InvariantCulture);
                scaleZ.Text = saved.ScaleZ.ToString(CultureInfo.InvariantCulture);
                polytype.Text = saved.CollisionPolyType.ToString(CultureInfo.InvariantCulture);
                collision.Checked = saved.AddToCollision;
            }

            TableLayoutPanel table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 14,
                Padding = new Padding(10),
                AutoSize = false
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            AddRow(table, 0, "Primitive", type);
            AddRow(table, 1, "Name", name);
            AddRow(table, 2, "Material", material);
            AddRow(table, 3, "Face materials", faceMaterials);
            AddRow(table, 4, "Texture", texture);
            AddRow(table, 5, "Min X / Y / Z", ThreeInputs(minX, minY, minZ));
            AddRow(table, 6, "Max X / Y / Z", ThreeInputs(maxX, maxY, maxZ));
            AddRow(table, 7, "Scale X / Y / Z", ThreeInputs(scaleX, scaleY, scaleZ));
            AddRow(table, 8, "Collision polytype", polytype);
            table.Controls.Add(collision, 0, 9);
            table.SetColumnSpan(collision, 2);

            Label hint = new Label
            {
                Text = "Plane uses Min/Max as its rectangle. Ramp rises from Min.Z to Max.Z.",
                AutoSize = true,
                ForeColor = Color.DimGray
            };
            table.Controls.Add(hint, 0, 10);
            table.SetColumnSpan(hint, 2);

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            Button add = new Button { Text = editingIndex >= 0 ? "Update Room" : "Add to Room", AutoSize = true };
            Button cancel = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            add.Click += Add_Click;
            buttons.Controls.Add(add);
            buttons.Controls.Add(cancel);
            table.Controls.Add(buttons, 0, 12);
            table.SetColumnSpan(buttons, 2);
            Controls.Add(table);
            AcceptButton = add;
            CancelButton = cancel;
        }

        private static Control ThreeInputs(Control a, Control b, Control c)
        {
            FlowLayoutPanel panel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true };
            a.Width = b.Width = c.Width = 72;
            panel.Controls.Add(a); panel.Controls.Add(b); panel.Controls.Add(c);
            return panel;
        }

        private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            control.Dock = DockStyle.Fill;
            table.Controls.Add(control, 1, row);
        }

        private void Add_Click(object sender, EventArgs e)
        {
            try
            {
                Vector3d scale = new Vector3d(Read(scaleX), Read(scaleY), Read(scaleZ));
                if (scale.X <= 0 || scale.Y <= 0 || scale.Z <= 0)
                    throw new InvalidOperationException("Scale values must be greater than zero.");

                RoomPrimitiveSpec spec = new RoomPrimitiveSpec
                {
                    Type = SelectedType(),
                    Name = name.Text,
                    MaterialName = material.Text,
                    FaceMaterials = ParseFaceMaterials(),
                    TexturePath = string.IsNullOrWhiteSpace(texture.Text) || texture.Text.StartsWith("Optional ") ? null : texture.Text,
                    Min = new Vector3d(Read(minX), Read(minY), Read(minZ)),
                    Max = new Vector3d(Read(maxX), Read(maxY), Read(maxZ)),
                    Scale = scale,
                    CollisionPolyType = ParseInt(polytype.Text),
                    AddToCollision = collision.Checked
                };

                if (editingIndex >= 0)
                    owner.UpdateRoomPrimitive(editingIndex, spec);
                else
                    owner.AppendRoomPrimitive(spec);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Room Geometry", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private RoomPrimitiveType SelectedType()
        {
            switch (type.SelectedIndex)
            {
                case 1: return RoomPrimitiveType.Plane;
                case 2: return RoomPrimitiveType.Triangle;
                case 3: return RoomPrimitiveType.Ramp;
                default: return RoomPrimitiveType.Cube;
            }
        }

        private static int TypeIndex(RoomPrimitiveType primitiveType)
        {
            switch (primitiveType)
            {
                case RoomPrimitiveType.Plane: return 1;
                case RoomPrimitiveType.Triangle: return 2;
                case RoomPrimitiveType.Ramp: return 3;
                default: return 0;
            }
        }

        private List<string> ParseFaceMaterials()
        {
            if (string.IsNullOrWhiteSpace(faceMaterials.Text) || faceMaterials.Text.StartsWith("Optional:"))
                return new List<string>();
            return new List<string>(faceMaterials.Text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static double Read(TextBox box)
        {
            double value;
            if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new InvalidOperationException("Enter valid numeric coordinates.");
            return value;
        }

        private static int ParseInt(string text)
        {
            int value;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new InvalidOperationException("Collision polytype must be an integer.");
            return value;
        }
    }
}
