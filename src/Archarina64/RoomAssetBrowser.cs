using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SharpOcarina
{
    internal sealed class RoomAssetBrowser : Form
    {
        private readonly MainForm owner;
        private readonly ZScene.ZRoom room;
        private readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };

        public RoomAssetBrowser(MainForm owner, ZScene.ZRoom room)
        {
            this.owner = owner;
            this.room = room;
            Text = "Room Asset Browser";
            StartPosition = FormStartPosition.CenterParent;
            Width = 760;
            Height = 560;
            tabs.TabPages.Add(BuildMaterialsPage());
            tabs.TabPages.Add(BuildModelsPage());
            tabs.TabPages.Add(BuildActorsPage());
            tabs.TabPages.Add(BuildObjectsPage());
            Controls.Add(tabs);
        }

        private TabPage BuildMaterialsPage()
        {
            TabPage page = new TabPage("Textures / Materials");
            FlowLayoutPanel flow = CreateFlow();
            if (room != null && room.ObjModel != null)
            {
                foreach (ObjFile.Material material in room.ObjModel.Materials)
                    flow.Controls.Add(CreateMaterialTile(material));
            }
            if (flow.Controls.Count == 0)
                flow.Controls.Add(new Label { Text = "No materials are loaded for this room.", AutoSize = true, Padding = new Padding(12) });
            page.Controls.Add(flow);
            return page;
        }

        private TabPage BuildModelsPage()
        {
            TabPage page = new TabPage("Models / Groups");
            FlowLayoutPanel flow = CreateFlow();
            if (room != null && room.ObjModel != null)
            {
                foreach (ObjFile.Group group in room.ObjModel.Groups)
                    flow.Controls.Add(CreateModelTile(group));
            }
            if (flow.Controls.Count == 0)
                flow.Controls.Add(new Label { Text = "No room model groups are loaded.", AutoSize = true, Padding = new Padding(12) });
            page.Controls.Add(flow);
            return page;
        }

        private TabPage BuildActorsPage()
        {
            TabPage page = new TabPage("Actors");
            FlowLayoutPanel flow = CreateFlow();
            if (room != null)
            {
                foreach (ZActor actor in room.ZActors)
                    flow.Controls.Add(CreateActorTile(actor));
            }
            if (flow.Controls.Count == 0)
                flow.Controls.Add(new Label { Text = "No actors are placed in this room.", AutoSize = true, Padding = new Padding(12) });
            page.Controls.Add(flow);
            return page;
        }

        private TabPage BuildObjectsPage()
        {
            TabPage page = new TabPage("Objects");
            FlowLayoutPanel flow = CreateFlow();
            if (room != null)
            {
                foreach (ZScene.ZUShort objectId in room.ZObjects)
                    flow.Controls.Add(CreateObjectTile(objectId));
            }
            if (flow.Controls.Count == 0)
                flow.Controls.Add(new Label { Text = "No room objects are loaded.", AutoSize = true, Padding = new Padding(12) });
            page.Controls.Add(flow);
            return page;
        }

        private static FlowLayoutPanel CreateFlow()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(10),
                WrapContents = true
            };
        }

        private Control CreateMaterialTile(ObjFile.Material material)
        {
            Panel tile = CreateTile(material == null ? "Material" : material.Name);
            PictureBox preview = new PictureBox
            {
                Width = 128,
                Height = 96,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(42, 42, 42)
            };
            preview.Image = LoadMaterialImage(material);
            preview.Location = new Point(10, 10);
            tile.Controls.Add(preview);
            Label path = new Label
            {
                Text = material == null ? "" : (material.map_Kd ?? "No texture"),
                AutoEllipsis = true,
                Width = 128,
                Height = 34,
                Location = new Point(10, 112)
            };
            tile.Controls.Add(path);
            return tile;
        }

        private Control CreateModelTile(ObjFile.Group group)
        {
            Panel tile = CreateTile(group == null ? "Model" : group.Name);
            PictureBox preview = new PictureBox
            {
                Width = 128,
                Height = 96,
                SizeMode = PictureBoxSizeMode.CenterImage,
                BackColor = Color.FromArgb(42, 42, 42)
            };
            preview.Image = RenderGroupThumbnail(group);
            preview.Location = new Point(10, 10);
            tile.Controls.Add(preview);
            Label stats = new Label
            {
                Text = group == null ? "" : group.Triangles.Count + " triangles",
                AutoSize = true,
                Location = new Point(10, 112)
            };
            tile.Controls.Add(stats);
            return tile;
        }

        private Control CreateActorTile(ZActor actor)
        {
            Panel tile = CreateTile("Actor " + actor.Number.ToString("X4"));
            PictureBox preview = new PictureBox { Width = 128, Height = 96, SizeMode = PictureBoxSizeMode.CenterImage, BackColor = Color.FromArgb(42, 42, 42) };
            preview.Image = RenderActorThumbnail(actor);
            preview.Location = new Point(10, 10);
            tile.Controls.Add(preview);
            tile.Controls.Add(new Label { Text = "Var " + actor.Variable.ToString("X4"), AutoSize = true, Location = new Point(10, 112) });
            AddPlacementButton(tile, "Place actor", delegate { owner.PlaceActorFromAsset(actor); });
            return tile;
        }

        private Control CreateObjectTile(ZScene.ZUShort objectId)
        {
            Panel tile = CreateTile("Object " + objectId.Value.ToString("X4"));
            PictureBox preview = new PictureBox { Width = 128, Height = 96, SizeMode = PictureBoxSizeMode.CenterImage, BackColor = Color.FromArgb(42, 42, 42) };
            Bitmap image = new Bitmap(128, 96);
            using (Graphics graphics = Graphics.FromImage(image))
            using (Pen pen = new Pen(Color.FromArgb(180, 210, 220), 2))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(85, 110, 135)))
            {
                graphics.Clear(Color.FromArgb(42, 42, 42));
                graphics.FillRectangle(brush, 34, 25, 60, 48);
                graphics.DrawRectangle(pen, 34, 25, 60, 48);
                graphics.DrawLine(pen, 34, 25, 52, 12);
                graphics.DrawLine(pen, 94, 25, 112, 12);
                graphics.DrawLine(pen, 52, 12, 112, 12);
            }
            preview.Image = image;
            preview.Location = new Point(10, 10);
            tile.Controls.Add(preview);
            AddPlacementButton(tile, "Place object", delegate { owner.PlaceObjectFromAsset(objectId.Value); });
            return tile;
        }

        private static void AddPlacementButton(Panel tile, string text, EventHandler handler)
        {
            Button button = new Button { Text = text, Width = 128, Height = 24, Location = new Point(10, 132) };
            button.Click += handler;
            tile.Controls.Add(button);
        }

        internal static Bitmap RenderActorThumbnail(ZActor actor)
        {
            Bitmap image = new Bitmap(128, 96);
            using (Graphics graphics = Graphics.FromImage(image))
            using (Pen bone = new Pen(Color.FromArgb(210, 225, 235), 3))
            using (SolidBrush joint = new SolidBrush(Color.FromArgb(100, 170, 205)))
            {
                graphics.Clear(Color.FromArgb(42, 42, 42));
                ZScene.ZObjRender render = MainForm.zobj_cache.Find(x => x.actor == actor.Number);
                if (render == null || render.Limbs == null || render.Limbs.Count == 0)
                {
                    graphics.DrawEllipse(bone, 54, 12, 20, 20);
                    graphics.DrawLine(bone, 64, 32, 64, 70);
                    graphics.DrawLine(bone, 64, 42, 42, 58);
                    graphics.DrawLine(bone, 64, 42, 86, 58);
                    graphics.DrawLine(bone, 64, 70, 48, 86);
                    graphics.DrawLine(bone, 64, 70, 80, 86);
                    return image;
                }

                for (int i = 0; i < render.Limbs.Count; i++)
                {
                    ZScene.Limb limb = render.Limbs[i];
                    PointF end = new PointF(64 + limb.x / 4.0f, 48 - limb.y / 4.0f);
                    if (limb.parent >= 0 && limb.parent < render.Limbs.Count)
                    {
                        ZScene.Limb parent = render.Limbs[limb.parent];
                        PointF start = new PointF(64 + parent.x / 4.0f, 48 - parent.y / 4.0f);
                        graphics.DrawLine(bone, start, end);
                    }
                    graphics.FillEllipse(joint, end.X - 3, end.Y - 3, 6, 6);
                }
            }
            return image;
        }

        private static Panel CreateTile(string title)
        {
            Panel tile = new Panel { Width = 150, Height = 190, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(6) };
            Label label = new Label { Text = title ?? "", AutoEllipsis = true, Width = 128, Height = 22, Location = new Point(10, 158) };
            tile.Controls.Add(label);
            return tile;
        }

        private static Image LoadMaterialImage(ObjFile.Material material)
        {
            if (material == null) return null;
            if (material.TexImage != null) return new Bitmap(material.TexImage);
            if (string.IsNullOrEmpty(material.map_Kd)) return null;
            try
            {
                string path = material.map_Kd;
                if (!Path.IsPathRooted(path) && MainForm.CurrentScene != null)
                    path = Path.Combine(MainForm.CurrentScene.BasePath ?? "", path);
                if (File.Exists(path))
                    return new Bitmap(Bitmap.FromFile(path));
            }
            catch { }
            return null;
        }

        private Bitmap RenderGroupThumbnail(ObjFile.Group group)
        {
            Bitmap bitmap = new Bitmap(128, 96);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen outline = new Pen(Color.FromArgb(170, 220, 235), 1))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(90, 120, 150)))
            {
                graphics.Clear(Color.FromArgb(42, 42, 42));
                if (group == null || room == null || room.ObjModel == null || group.Triangles.Count == 0)
                    return bitmap;

                List<ObjFile.Vertex> vertices = group.Triangles
                    .SelectMany(t => t.VertIndex)
                    .Where(i => i >= 0 && i < room.ObjModel.Vertices.Count)
                    .Select(i => room.ObjModel.Vertices[i])
                    .ToList();
                if (vertices.Count == 0) return bitmap;

                double minX = vertices.Min(v => v.X), maxX = vertices.Max(v => v.X);
                double minZ = vertices.Min(v => v.Z), maxZ = vertices.Max(v => v.Z);
                if (Math.Abs(maxX - minX) < 0.001) { minX = vertices.Min(v => v.Y); maxX = vertices.Max(v => v.Y); }
                if (Math.Abs(maxZ - minZ) < 0.001) { minZ = vertices.Min(v => v.Y); maxZ = vertices.Max(v => v.Y); }
                double scale = Math.Min(108.0 / Math.Max(1, maxX - minX), 76.0 / Math.Max(1, maxZ - minZ));

                foreach (ObjFile.Triangle triangle in group.Triangles)
                {
                    PointF[] points = new PointF[3];
                    for (int i = 0; i < 3; i++)
                    {
                        ObjFile.Vertex v = room.ObjModel.Vertices[triangle.VertIndex[i]];
                        double x = (Math.Abs(maxX - minX) < 0.001 ? v.Y : v.X);
                        double z = (Math.Abs(maxZ - minZ) < 0.001 ? v.Y : v.Z);
                        points[i] = new PointF((float)(10 + (x - minX) * scale), (float)(86 - (z - minZ) * scale));
                    }
                    graphics.FillPolygon(fill, points);
                    graphics.DrawPolygon(outline, points);
                }
            }
            return bitmap;
        }
    }
}
