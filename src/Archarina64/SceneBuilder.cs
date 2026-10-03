using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SharpOcarina
{
    public partial class MainForm
    {
        private void InitializeSceneBuilder()
        {
            ToolStripMenuItem builder = new ToolStripMenuItem("Scene Builder");
            builder.DropDownItems.Add("Browse ROM rooms...", null, delegate { BrowseRomTiles(); });
            builder.DropDownItems.Add("Make selected room editable...", null, delegate { SplitRoomGeometryIntoEditableGroups(); });
            builder.DropDownItems.Add("Browse room assets...", null, delegate { OpenAuthoringAssetBrowser(); });
            builder.DropDownItems.Add("Check scene readiness...", null, delegate
            {
                List<string> issues = SceneBuilderValidation.Check(CurrentScene);
                using (Form report = new Form { Text = "Archarina64 - Scene readiness", Width = 720, Height = 440, StartPosition = FormStartPosition.CenterParent })
                {
                    report.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                        ScrollBars = ScrollBars.Both, WordWrap = false,
                        Text = string.Join(Environment.NewLine, issues.ToArray()) });
                    report.ShowDialog(this);
                }
            });
            extraToolStripMenuItem.Owner.Items.Insert(0, builder);
            Shown += delegate { Text = Program.ApplicationTitle; InitializeSandboxWorkspace(); };
        }

        private void BrowseRomTiles()
        {
            using (OpenFileDialog picker = new OpenFileDialog { Filter = "Decompressed OoT ROM (*.z64)|*.z64", CheckFileExists = true })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    byte[] bytes = File.ReadAllBytes(picker.FileName);
                    if (bytes.Length < 0x50010) throw new InvalidDataException("This file is too small to be an OoT ROM.");
                    ROM version = CheckVersion(new List<byte>(bytes));
                    if (version == null || version.Game != "OOT") throw new InvalidDataException("This catalog supports OoT ROMs only.");
                    SceneTileLibrary library = SceneTileLibrary.Read(bytes, version.SceneTable, version.SceneTableEnd);
                    library.Save(ArcharinaWorkspace.LibraryPath);
                    using (Form palette = new Form { Text = "Archarina64 - ROM rooms", Width = 600, Height = 650, StartPosition = FormStartPosition.CenterParent })
                    {
                        TextBox search = new TextBox { Dock = DockStyle.Top };
                        ListBox rooms = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
                        Button open = new Button { Text = "Open source scene", Dock = DockStyle.Bottom, Height = 36, Enabled = false };
                        Label status = new Label { Dock = DockStyle.Bottom, Height = 38,
                            Text = library.Rooms.Count + " rooms | Main setup | " + library.Diagnostics.Count + " catalog warnings" };
                        palette.Controls.Add(rooms);
                        palette.Controls.Add(search);
                        palette.Controls.Add(status);
                        palette.Controls.Add(open);
                        Action refresh = delegate
                        {
                            rooms.DataSource = library.Rooms.Where(room => room.ToString().IndexOf(search.Text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                        };
                        search.TextChanged += delegate { refresh(); };
                        rooms.SelectedIndexChanged += delegate { open.Enabled = rooms.SelectedItem != null; };
                        SceneTileSource selected = null;
                        open.Click += delegate { selected = rooms.SelectedItem as SceneTileSource; palette.DialogResult = DialogResult.OK; };
                        refresh();
                        if (palette.ShowDialog(this) != DialogResult.OK || selected == null) return;
                        if (!WarningUnsavedChanges(true)) return;
                        LoadZscene(new List<byte>(bytes), selected.SceneId);
                        if (CurrentScene != null && selected.RoomId < CurrentScene.Rooms.Count) SelectRoom(selected.RoomId);
                    }
                }
                catch (Exception error)
                {
                    MessageBox.Show(this, error.Message, "Archarina64 ROM catalog", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }

    public static class SceneBuilderValidation
    {
        public static List<string> Check(ZScene scene)
        {
            List<string> issues = new List<string>();
            if (scene == null) { issues.Add("ERROR: No scene is loaded."); return issues; }
            if (scene.Rooms.Count == 0) issues.Add("ERROR: No rooms.");
            if (scene.Rooms.Count > 255) issues.Add("ERROR: Room count exceeds the byte-sized room list.");
            if (scene.SpawnPoints.Count == 0) issues.Add("ERROR: No player spawn.");
            if (scene.ColModel == null || scene.ColModel.Vertices.Count == 0) issues.Add("ERROR: No collision geometry.");
            foreach (ZActor spawn in scene.SpawnPoints)
            {
                if (spawn.SpawnRoom >= scene.Rooms.Count) issues.Add("ERROR: Spawn references missing room " + spawn.SpawnRoom + ".");
                if (!Fits(spawn.XPos) || !Fits(spawn.YPos) || !Fits(spawn.ZPos)) issues.Add("ERROR: Spawn has an out-of-range position.");
            }
            if (scene.Transitions.Count > RoomConnections.MaximumConnections) issues.Add("ERROR: More than 64 indexed transition actors.");
            foreach (ZActor door in scene.Transitions)
            {
                if (door.FrontSwitchTo != 255 && (door.FrontSwitchTo > 127 || door.FrontSwitchTo >= scene.Rooms.Count))
                    issues.Add("ERROR: Door front references missing room " + door.FrontSwitchTo + ".");
                if (door.BackSwitchTo != 255 && (door.BackSwitchTo > 127 || door.BackSwitchTo >= scene.Rooms.Count))
                    issues.Add("ERROR: Door back references missing room " + door.BackSwitchTo + ".");
                if (door.Variable > 0x03FF) issues.Add("ERROR: Door parameters overlap reserved transition-index bits.");
                if (!Fits(door.XPos) || !Fits(door.YPos) || !Fits(door.ZPos)) issues.Add("ERROR: Door has an out-of-range position.");
            }
            for (int i = 0; i < scene.Rooms.Count; i++)
            {
                ZScene.ZRoom room = scene.Rooms[i];
                if (room.ZActors.Count > 255) issues.Add("ERROR: Room " + i + " exceeds the actor count field.");
                if (room.ZObjects.Count > 255) issues.Add("ERROR: Room " + i + " exceeds the object count field.");
                foreach (ZActor actor in room.ZActors)
                    if (!Fits(actor.XPos) || !Fits(actor.YPos) || !Fits(actor.ZPos))
                        issues.Add("ERROR: Room " + i + " actor " + actor.Number.ToString("X4") + " has an out-of-range position.");
            }
            if (scene.PregeneratedMesh) issues.Add("REVIEW: Imported display lists still use the original mesh path.");
            if (scene.SceneHeaders.Count != 0) issues.Add("REVIEW: Alternate setups require independent validation.");
            issues.AddRange(RoomPackageReview.CheckScene(scene));
            issues.Add("REVIEW: Object dependencies, gameplay flags, memory budgets and in-game traversal are not certified by this check.");
            return issues.Distinct().ToList();
        }

        private static bool Fits(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= short.MinValue && value <= short.MaxValue;
        }
    }
}
