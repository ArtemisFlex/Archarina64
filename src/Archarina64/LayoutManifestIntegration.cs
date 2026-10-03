using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SharpOcarina
{
    public partial class MainForm
    {
        private bool layoutToolMode;

        private void InitializeLayoutManifestIntegration()
        {
            ToolStripMenuItem layoutMode = new ToolStripMenuItem("Native Layout Editing Mode");
            layoutMode.CheckOnClick = true;
            layoutMode.ToolTipText = "Use SharpOcarina's OpenTK viewport for actor placement and editor-only scaling";
            layoutMode.CheckedChanged += delegate { layoutToolMode = layoutMode.Checked; };
            fileToolStripMenuItem.DropDownItems.Insert(2, layoutMode);

            ToolStripMenuItem importLayout = new ToolStripMenuItem("Import OoT Layout JSON...");
            importLayout.ToolTipText = "Import actor placement exported by the OoT layout viewer";
            importLayout.Click += ImportLayoutManifest_Click;
            fileToolStripMenuItem.DropDownItems.Insert(2, importLayout);
        }

        private void ImportLayoutManifest_Click(object sender, EventArgs e)
        {
            if (CurrentScene == null)
            {
                MessageBox.Show("Open or create a SharpOcarina scene before importing a layout.", "Layout import", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "OoT layout JSON (*.json)|*.json|All files (*.*)|*.*";
                dialog.Title = "Import OoT Layout JSON";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    int imported = LayoutManifestImporter.Apply(dialog.FileName, CurrentScene);
                    if (CurrentScene.Rooms.Count > 0 && RoomList.Items.Count > 0)
                    {
                        if (RoomList.SelectedIndex < 0 || RoomList.SelectedIndex >= RoomList.Items.Count ||
                            RoomList.SelectedIndex >= CurrentScene.Rooms.Count)
                            RoomList.SelectedIndex = 0;
                        actorEditControl.SetActors(ref CurrentScene.Rooms[RoomList.SelectedIndex].ZActors);
                    }
                    glControl1.Invalidate();
                    MessageBox.Show("Imported " + imported + " actor placement(s). Review the scene and save it when ready.", "Layout import", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Layout import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }

    internal static class LayoutManifestImporter
    {
        public static int Apply(string filename, ZScene scene)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> document;
            using (StreamReader reader = File.OpenText(filename))
            {
                document = serializer.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
            }

            object rawActors;
            if (!document.TryGetValue("actors", out rawActors) || !(rawActors is object[]))
                throw new InvalidDataException("The layout JSON does not contain an actors array.");

            int imported = 0;
            foreach (object rawActor in (object[])rawActors)
            {
                Dictionary<string, object> actor = rawActor as Dictionary<string, object>;
                if (actor == null) continue;

                int roomIndex = ReadInt(actor, "room", 0);
                if (roomIndex < 0 || roomIndex >= scene.Rooms.Count)
                    throw new InvalidDataException("Layout actor references room " + roomIndex + ", but the scene has " + scene.Rooms.Count + " room(s).");

                float[] position = ReadVector(actor, "location", 3);
                float[] rotation = ReadVector(actor, "rotation_euler", 3);
                ushort actorId = (ushort)ReadInt(actor, "actor_id", 0);
                ushort parameters = (ushort)ReadInt(actor, "params", 0);
                ZActor importedActor = new ZActor(actorId, position[0], position[1], position[2],
                    ToN64Rotation(rotation[0]), ToN64Rotation(rotation[1]), ToN64Rotation(rotation[2]), parameters);
                float[] scale = ReadVector(actor, "scale", 3);
                importedActor.EditorScale = new OpenTK.Vector3(scale[0], scale[1], scale[2]);

                scene.Rooms[roomIndex].ZActors.Add(importedActor);
                imported++;
            }
            return imported;
        }

        private static float[] ReadVector(Dictionary<string, object> actor, string key, int length)
        {
            object value;
            if (!actor.TryGetValue(key, out value) || !(value is object[]))
                throw new InvalidDataException("Layout actor is missing " + key + ".");

            object[] values = (object[])value;
            if (values.Length != length) throw new InvalidDataException("Layout actor has an invalid " + key + " vector.");
            float[] result = new float[length];
            for (int i = 0; i < length; i++) result[i] = Convert.ToSingle(values[i]);
            return result;
        }

        private static int ReadInt(Dictionary<string, object> actor, string key, int fallback)
        {
            object value;
            if (!actor.TryGetValue(key, out value) || value == null) return fallback;
            string text = value.ToString().Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return Convert.ToInt32(text.Substring(2), 16);
            return Convert.ToInt32(value);
        }

        private static short ToN64Rotation(float radians)
        {
            int units = (int)Math.Round(radians * 32768.0 / Math.PI);
            return unchecked((short)units);
        }
    }
}
