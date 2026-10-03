using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace SharpOcarina
{
    public partial class MainForm
    {
        private CheckBox sandboxMagnetic, sandboxDoorAnchors;
        private NumericUpDown sandboxMagnetRadius;
        private TileSnap sandboxSnapPreview;
        private SectionTile sandboxPreviewTile;
        private string sandboxPreviewTilePath;

        private void InitializeSandboxAssembly(FlowLayoutPanel toolbar, Control previousRow)
        {
            toolbar.Height = 106; toolbar.SetFlowBreak(previousRow, true);
            FlowLayoutPanel row = new FlowLayoutPanel { Width = 970, Height = 30, WrapContents = false, Margin = Padding.Empty };
            row.Controls.Add(new Label { Text = "SNAP / CONNECT", AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(0, 5, 8, 0) });
            sandboxMagnetic = new CheckBox { Text = "Magnetic snap", Checked = true, AutoSize = true };
            sandboxDoorAnchors = new CheckBox { Text = "Room door anchors", AutoSize = true };
            sandboxMagnetRadius = new NumericUpDown { Minimum = 1, Maximum = 4096, Value = 150, Width = 65 };
            Button box = new Button { Text = "Select box...", AutoSize = true };
            box.Click += delegate { SandboxRun(ShowSandboxBoxSelection); };
            Button grow = new Button { Text = "Select connected", AutoSize = true };
            grow.Click += delegate { SandboxRun(GrowSandboxSelection); };
            Button sockets = new Button { Text = "Tile sockets...", AutoSize = true };
            sockets.Click += delegate { SandboxRun(EditSandboxTileSockets); };
            row.Controls.Add(sandboxMagnetic); row.Controls.Add(new Label { Text = "Radius", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
            row.Controls.Add(sandboxMagnetRadius); row.Controls.Add(sandboxDoorAnchors); row.Controls.Add(box); row.Controls.Add(grow); row.Controls.Add(sockets);
            toolbar.Controls.Add(row);
            glControl1.DragOver += PreviewSandboxSnap;
            glControl1.DragLeave += delegate { ClearSandboxSnapPreview(); };
        }

        private void ClearSandboxSnapPreview() { sandboxSnapPreview = null; sandboxPreviewTile = null; sandboxPreviewTilePath = null; glControl1.Invalidate(); }

        private List<TileSocket> SandboxSnapTargets()
        {
            var targets = CurrentScene.TileSockets.Select(s => s.Clone()).ToList();
            foreach (SandboxAuthoringMarker marker in sandboxAuthoringMarkers.Where(IsSandboxSnapMarker).Where(m => string.IsNullOrEmpty(m.ConnectedTo)))
                targets.Add(marker.ToSocket());
            if (sandboxDoorAnchors.Checked)
                foreach (ZActor door in CurrentScene.Transitions)
                    foreach (int side in new[] { 0, 1 })
                    {
                        int room = side == 0 ? door.FrontSwitchTo : door.BackSwitchTo;
                        if (room == 255 || room >= CurrentScene.Rooms.Count) continue;
                        string key = "door:" + string.Join(":", new[] { door.Number.ToString(), door.XPos.ToString("R", CultureInfo.InvariantCulture), door.YPos.ToString("R", CultureInfo.InvariantCulture), door.ZPos.ToString("R", CultureInfo.InvariantCulture), door.YRot.ToString("R", CultureInfo.InvariantCulture), room.ToString(), side.ToString() });
                        if (CurrentScene.TileSockets.Any(s => s.ConnectedTo == key)) continue;
                        targets.Add(new TileSocket { Id = key, Name = "Room door", Position = new Vector3d(door.XPos, door.YPos, door.ZPos), Facing = TileAssembly.RoomDoorFacing(door.YRot * 360 / 65536.0, side == 0), Room = room });
                    }
            return targets;
        }

        private static bool IsSandboxSnapMarker(SandboxAuthoringMarker marker)
        {
            if (marker == null) return false;
            return string.Equals(marker.Kind, "Entrance", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(marker.Kind, "Exit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(marker.Kind, "Connector", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(marker.Kind, "Hub", StringComparison.OrdinalIgnoreCase);
        }

        private TileSnap FindSandboxSnap(SectionTile tile, Vector3d position, int turns)
        {
            if (sandboxMagnetic == null || !sandboxMagnetic.Checked || tile.Sockets.Count == 0) return null;
            return TileAssembly.FindSnap(tile.Sockets, SandboxSnapTargets(), position, turns, (double)sandboxMagnetRadius.Value);
        }

        private void PreviewSandboxSnap(object sender, DragEventArgs e)
        {
            var asset = e.Data.GetData(typeof(RomPaletteAsset)) as RomPaletteAsset;
            if (asset == null || asset.TilePath == null || CurrentScene == null || RoomList.SelectedIndex < 0) return;
            try
            {
                if (sandboxPreviewTilePath != asset.TilePath) { sandboxPreviewTile = TileStorage.Load<SectionTile>(asset.TilePath); sandboxPreviewTilePath = asset.TilePath; }
                Vector3d position = SandboxDropPosition(glControl1.PointToClient(new Point(e.X, e.Y)));
                if (sandboxSnap.Checked) position = SnapTileGrid(position, sandboxPreviewTile.Grid);
                sandboxSnapPreview = FindSandboxSnap(sandboxPreviewTile, position, sandboxTileRotation.SelectedIndex);
                sandboxStatus.Text = sandboxSnapPreview == null ? "No compatible free socket in range" : "Socket aligned | " + sandboxSnapPreview.Turns * 90 + " deg";
                glControl1.Invalidate();
            }
            catch (Exception error) { sandboxSnapPreview = null; sandboxStatus.Text = error.Message; }
        }

        private static Vector3d SnapTileGrid(Vector3d position, double grid)
        {
            if (double.IsNaN(grid) || double.IsInfinity(grid) || grid < 1 || grid > 4096) throw new InvalidDataException("Invalid tile grid.");
            return new Vector3d(Math.Round(position.X / grid) * grid, Math.Round(position.Y / grid) * grid, Math.Round(position.Z / grid) * grid);
        }

        private void RenderSandboxSockets()
        {
            if (CurrentScene == null || sandboxMagnetic == null || !sandboxMagnetic.Checked) return;
            GL.PushAttrib(AttribMask.AllAttribBits); GL.MatrixMode(MatrixMode.Modelview); GL.PushMatrix(); GL.LoadIdentity();
            Camera.Position(); GL.Scale(0.005, 0.005, 0.005);
            GL.Disable((EnableCap)All.FragmentProgram); GL.ActiveTexture(TextureUnit.Texture1); GL.Disable(EnableCap.Texture2D);
            GL.ActiveTexture(TextureUnit.Texture0); GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Lighting); GL.Disable(EnableCap.Fog); GL.Disable(EnableCap.AlphaTest);
            GL.Disable(EnableCap.DepthTest); GL.DepthMask(false); GL.LineWidth(2);
            foreach (TileSocket socket in SandboxSnapTargets())
            {
                GL.Color3(string.IsNullOrEmpty(socket.ConnectedTo) ? Color.DeepSkyBlue : Color.LimeGreen); DrawSandboxSocket(socket);
            }
            if (sandboxSnapPreview != null && sandboxPreviewTile != null)
            {
                GL.Color3(Color.Gold);
                DrawSandboxSocket(sandboxPreviewTile.Sockets[sandboxSnapPreview.SourceSocket].Placed(sandboxSnapPreview.Position, sandboxSnapPreview.Turns, RoomList.SelectedIndex, "preview"));
            }
            GL.PopMatrix(); GL.PopAttrib();
        }

        private static void DrawSandboxSocket(TileSocket socket)
        {
            double angle = socket.Facing * Math.PI / 180;
            Vector3d right = new Vector3d(Math.Cos(angle), 0, -Math.Sin(angle)) * socket.Width / 2;
            Vector3d up = new Vector3d(0, socket.Height, 0), center = socket.Position;
            GL.Begin(PrimitiveType.LineLoop); GL.Vertex3(center - right); GL.Vertex3(center + right); GL.Vertex3(center + right + up); GL.Vertex3(center - right + up); GL.End();
            GL.Begin(PrimitiveType.Lines); GL.Vertex3(center + up / 2); GL.Vertex3(center + up / 2 + new Vector3d(Math.Sin(angle), 0, Math.Cos(angle)) * 80); GL.End();
        }

        private List<TileSocket> SourceDoorSocketCandidates()
        {
            int room = RoomList.SelectedIndex;
            return CurrentScene.Transitions.Where(d => d.FrontSwitchTo == room || d.BackSwitchTo == room).Select(d => new TileSocket
            {
                Name = "Door " + d.Number.ToString("X4") + " (" + d.FrontSwitchTo + "/" + d.BackSwitchTo + ")",
                Position = new Vector3d(d.XPos, d.YPos, d.ZPos), Facing = TileAssembly.RoomDoorFacing(d.YRot * 360 / 65536.0, d.FrontSwitchTo == room)
            }).ToList();
        }

        private List<TileSocket> EditSandboxSockets(List<TileSocket> selected, List<TileSocket> candidates, string title)
        {
            using (Form dialog = new Form { Text = title, ClientSize = new Size(790, 380), StartPosition = FormStartPosition.CenterParent })
            {
                DataGridView grid = new DataGridView { Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersVisible = false };
                grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", FillWeight = 50 });
                foreach (string field in new[] { "Name", "X", "Y", "Z", "Facing", "Width", "Height" }) grid.Columns.Add(field, field);
                foreach (TileSocket s in selected) grid.Rows.Add(true, s.Name, s.X, s.Y, s.Z, s.Facing, s.Width, s.Height);
                foreach (TileSocket s in candidates.Where(c => !selected.Any(s => s.Name == c.Name))) grid.Rows.Add(false, s.Name, s.X, s.Y, s.Z, s.Facing, s.Width, s.Height);
                grid.DefaultValuesNeeded += delegate(object sender, DataGridViewRowEventArgs e)
                { object[] defaults = { true, "Door", 0d, 0d, 0d, 0d, 100d, 180d }; for (int i = 0; i < defaults.Length; i++) e.Row.Cells[i].Value = defaults[i]; };
                Button apply = new Button { Text = "Apply sockets", Dock = DockStyle.Bottom, Height = 34 };
                List<TileSocket> result = null;
                apply.Click += delegate
                {
                    try
                    {
                        grid.EndEdit(); var edited = new List<TileSocket>();
                        foreach (DataGridViewRow row in grid.Rows)
                        {
                            if (row.IsNewRow || !Equals(row.Cells[0].Value, true)) continue;
                            var socket = new TileSocket { Id = "socket_" + edited.Count, Name = Convert.ToString(row.Cells[1].Value), X = Convert.ToDouble(row.Cells[2].Value), Y = Convert.ToDouble(row.Cells[3].Value), Z = Convert.ToDouble(row.Cells[4].Value), Facing = Convert.ToDouble(row.Cells[5].Value), Width = Convert.ToDouble(row.Cells[6].Value), Height = Convert.ToDouble(row.Cells[7].Value) };
                            socket.Validate(); edited.Add(socket);
                        }
                        result = edited; dialog.DialogResult = DialogResult.OK;
                    }
                    catch (Exception error) { MessageBox.Show(dialog, error.Message, "Socket values"); }
                };
                dialog.Controls.Add(grid); dialog.Controls.Add(apply);
                return dialog.ShowDialog(this) == DialogResult.OK ? result : null;
            }
        }

        private void EditSandboxTileSockets()
        {
            var asset = sandboxPalette.SelectedItem as RomPaletteAsset;
            if (asset == null || asset.TilePath == null) throw new InvalidOperationException("Select a saved tile in the Tiles palette first.");
            SectionTile tile = TileStorage.Load<SectionTile>(asset.TilePath);
            var sockets = EditSandboxSockets(tile.Sockets, SourceDoorSocketCandidates(), "Tile sockets - pivot-relative coordinates");
            if (sockets == null) return;
            tile.Sockets = sockets; string next = TileStorage.Save(tile, true, new[] { tile.Visual, tile.Collision });
            string history = Path.Combine(TileStorage.Root, "library-history"); Directory.CreateDirectory(history);
            File.Move(asset.TilePath, Path.Combine(history, Path.GetFileName(asset.TilePath)));
            LoadSandboxTiles(); sandboxPalette.SelectedItem = sandboxCatalog.Assets.First(a => a.TilePath == next);
        }

        private void GrowSandboxSelection()
        {
            if (CurrentScene == null || RoomList.SelectedIndex < 0 || sandboxFaceMode == 0) throw new InvalidOperationException("Choose Visual faces or Collision faces first.");
            bool collision = sandboxFaceMode == 2;
            ObjFile model = collision ? CurrentScene.ColModel : CurrentScene.Rooms[RoomList.SelectedIndex].ObjModel;
            var groups = collision ? model.Groups : CurrentScene.Rooms[RoomList.SelectedIndex].TrueGroups;
            HashSet<int> selection = collision ? sandboxCollisionFaces : sandboxVisualFaces;
            selection.UnionWith(TileAssembly.ConnectedFaces(model, groups, selection)); UpdateSandboxFaceCount();
        }

        private void ShowSandboxBoxSelection()
        {
            if (CurrentScene == null || RoomList.SelectedIndex < 0) throw new InvalidOperationException("Open a room first.");
            Vector3d center = GetRoomAuthoringCenter(CurrentScene.Rooms[RoomList.SelectedIndex]);
            using (Form dialog = new Form { Text = "Select whole faces in box", ClientSize = new Size(410, 310), StartPosition = FormStartPosition.CenterParent })
            {
                TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 3, RowCount = 6 };
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                fields.Controls.Add(new Label { Text = "Minimum" }, 1, 0); fields.Controls.Add(new Label { Text = "Maximum" }, 2, 0);
                NumericUpDown[] values = new NumericUpDown[6]; double[] p = { center.X, center.Y, center.Z };
                for (int axis = 0; axis < 3; axis++)
                {
                    fields.Controls.Add(new Label { Text = new[] { "X", "Y", "Z" }[axis] }, 0, axis + 1);
                    for (int side = 0; side < 2; side++)
                    {
                        int i = axis * 2 + side;
                        values[i] = new NumericUpDown { Minimum = short.MinValue, Maximum = short.MaxValue, Value = (decimal)Math.Max(short.MinValue, Math.Min(short.MaxValue, p[axis] + (side == 0 ? -500 : 500))), Increment = sandboxGrid.Value, Dock = DockStyle.Fill };
                        fields.Controls.Add(values[i], side + 1, axis + 1);
                    }
                }
                CheckBox visual = new CheckBox { Text = "Visual", Checked = sandboxFaceMode != 2, AutoSize = true };
                CheckBox collision = new CheckBox { Text = "Collision", Checked = sandboxFaceMode == 2, AutoSize = true };
                fields.Controls.Add(visual, 1, 4); fields.Controls.Add(collision, 2, 4);
                Button select = new Button { Text = "Replace selection", Dock = DockStyle.Fill };
                select.Click += delegate
                {
                    try { SelectSandboxBox(new Vector3d((double)values[0].Value, (double)values[2].Value, (double)values[4].Value), new Vector3d((double)values[1].Value, (double)values[3].Value, (double)values[5].Value), visual.Checked, collision.Checked); dialog.DialogResult = DialogResult.OK; }
                    catch (Exception error) { MessageBox.Show(dialog, error.Message, "Box selection"); }
                };
                fields.Controls.Add(select, 1, 5); fields.SetColumnSpan(select, 2); dialog.Controls.Add(fields); dialog.ShowDialog(this);
            }
        }

        internal void SelectSandboxBox(Vector3d min, Vector3d max, bool visual, bool collision)
        {
            int crossing = 0, count;
            if (!visual && !collision) throw new InvalidOperationException("Select visual, collision, or both.");
            HashSet<int> visualFaces = null, collisionFaces = null;
            var room = CurrentScene.Rooms[RoomList.SelectedIndex];
            if (visual) { visualFaces = TileAssembly.FacesInBox(room.ObjModel, room.TrueGroups, min, max, out count); crossing += count; }
            if (collision) { collisionFaces = TileAssembly.FacesInBox(CurrentScene.ColModel, CurrentScene.ColModel.Groups, min, max, out count); crossing += count; }
            if (visual) { sandboxVisualFaces.Clear(); sandboxVisualFaces.UnionWith(visualFaces); }
            if (collision) { sandboxCollisionFaces.Clear(); sandboxCollisionFaces.UnionWith(collisionFaces); }
            UpdateSandboxFaceCount(); sandboxStatus.Text = crossing + " boundary-overlapping faces excluded (bounds estimate)";
        }

        private TileSocket FindSandboxDoorAlignment(Vector3d position)
        {
            if (!sandboxMagnetic.Checked) return null;
            return CurrentScene.TileSockets.Where(s => s.ConnectedTo == null || !s.ConnectedTo.StartsWith("door:", StringComparison.Ordinal))
                .Where(s => (s.Position - position).Length <= (double)sandboxMagnetRadius.Value)
                .OrderBy(s => (s.Position - position).LengthSquared).ThenBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault();
        }
    }
}
