using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenTK;

namespace SharpOcarina
{
    public partial class MainForm
    {
        private void CreateEmptyDungeonWorkspace()
        {
            CreateEmptyDungeonWorkspace(false);
        }

        private void CreateEmptyDungeonWorkspace(bool suppressUnsavedPrompt)
        {
            if (CurrentScene == null) throw new InvalidOperationException("Open a ROM or scene before creating a dungeon workspace.");
            if (!suppressUnsavedPrompt && !WarningUnsavedChanges(true)) return;

            ZScene source = sandboxRandomDungeonSearchActive && sandboxRandomDungeonSearchTemplate != null
                ? sandboxRandomDungeonSearchTemplate : CurrentScene;
            ZScene empty = source.Clone();
            empty.Name = "Archarina64 Empty Dungeon";
            empty.SandboxGeometryFile = string.Empty;
            empty.CollisionFilename = string.Empty;
            empty.SceneHeaders.Clear();
            empty.Transitions.Clear();
            empty.SpawnPoints.Clear();
            empty.Pathways.Clear();
            empty.Cameras.Clear();
            empty.Cutscene.Clear();
            empty.TileSockets = new List<TileSocket>();
            empty.SandboxGameplayPackages = new List<RoomPackage>();
            empty.ColModel = new ObjFile();
            empty.ColModel.Groups = new List<ObjFile.Group>();
            empty.PolyTypes = source.PolyTypes == null || source.PolyTypes.Count == 0
                ? new List<ZColPolyType> { new ZColPolyType() }
                : source.PolyTypes.Select(p => new ZColPolyType(p.Raw, p.PolyFlagA, p.PolyFlagB)).ToList();
            empty.Rooms = new List<ZScene.ZRoom> { CreateEmptyDungeonRoom() };
            empty.Scale = 1;
            empty.AutoCollision = false;
            empty.PregeneratedMesh = false;
            empty.AutomaticCollisionBounds = true;
            empty.AutomaticSubdivisions = true;
            SetCurrentSceneDocument(empty);
            sandboxAuthoringMarkers = new List<SandboxAuthoringMarker>();
            if (sandboxRandomDungeonSearchActive && sandboxRandomDungeonSearchTemplate == null)
                sandboxRandomDungeonSearchTemplate = empty.Clone();
            authoringSelection = null;
            authoringTreeScene = null;
            authoringTreeDirty = true;
            GroupList.SelectedItem = null;

            manualRoomListItems = true;
            RefreshRoomListItems();
            SelectRoom(0);
            ClearSandboxFaces();
            sandboxUndo.Clear();
            sandboxRedo.Clear();
            ApplySandboxPreview();
            RefreshSandboxRooms();
            FocusSandboxRoom();
            savechanges = true;
            sandboxStatus.Text = "Empty dungeon workspace | drag room tiles into Room 0, then move the room bundle as needed.";
        }

        private static ZScene.ZRoom CreateEmptyDungeonRoom()
        {
            ObjFile model = new ObjFile { Groups = new List<ObjFile.Group>() };
            return new ZScene.ZRoom
            {
                ModelFilename = string.Empty,
                ModelShortFilename = "Dungeon Room 00 (empty)",
                ObjModel = model,
                TrueGroups = model.Groups,
                ZObjects = new List<ZScene.ZUShort>(),
                ZActors = new List<ZActor>(),
                AuthoringPrimitives = new List<ZScene.ZRoom.RoomAuthoringPrimitive>(),
                InjectOffset = 0x02D00000
            };
        }

        private void AddEmptyDungeonRoom()
        {
            if (CurrentScene == null) throw new InvalidOperationException("Open a ROM or create an empty dungeon workspace first.");
            SandboxRecord();
            ZScene.ZRoom room = CreateEmptyDungeonRoom();
            room.ModelShortFilename = "Dungeon Room " + CurrentScene.Rooms.Count.ToString("D2") + " (empty)";
            CurrentScene.Rooms.Add(room);
            if (!string.IsNullOrEmpty(CurrentScene.SandboxGeometryFile)) PersistSandboxGeometry();
            manualRoomListItems = true;
            RefreshRoomListItems();
            SelectRoom(CurrentScene.Rooms.Count - 1);
            RefreshSandboxRooms();
            FocusSandboxRoom();
            savechanges = true;
            sandboxStatus.Text = "Added empty dungeon room " + (CurrentScene.Rooms.Count - 1) + ". Select it, then place room tiles into it.";
        }

        private void MoveSelectedSandboxRoom()
        {
            if (CurrentScene == null || RoomList.SelectedIndex < 0 || RoomList.SelectedIndex >= CurrentScene.Rooms.Count)
                throw new InvalidOperationException("Select a room in the workspace first.");

            Vector3d delta;
            if (!ShowSandboxMoveRoomDialog(out delta)) return;
            SandboxRecord();
            MoveSandboxRoomBundle(RoomList.SelectedIndex, delta);
            ShiftSandboxRoomTilePlacements(RoomList.SelectedIndex, delta);
            if (!string.IsNullOrEmpty(CurrentScene.SandboxGeometryFile)) PersistSandboxGeometry();
            NativeGameplayMaterializer.Apply(CurrentScene);
            savechanges = true;
            RefreshSandboxContents();
            ApplySandboxPreview();
            FocusSandboxRoom();
            sandboxStatus.Text = "Moved room " + RoomList.SelectedIndex + " and its geometry, collision, actors, doors, spawns, sockets, and gameplay.";
        }

        private bool BeginSandboxRoomMove(Point point)
        {
            if (!sandboxRoomMoveArmed || CurrentScene == null || RoomList.SelectedIndex < 0 || RoomList.SelectedIndex >= CurrentScene.Rooms.Count) return false;
            Vector3d center = GetRoomAuthoringCenter(CurrentScene.Rooms[RoomList.SelectedIndex]);
            GetSandboxRoomBounds(CurrentScene.Rooms[RoomList.SelectedIndex], out sandboxRoomDragStartMin, out sandboxRoomDragStartMax);
            sandboxMovingRoomIndex = RoomList.SelectedIndex;
            sandboxRoomDragHeight = center.Y;
            sandboxRoomDragStartWorld = GetSandboxPlanePosition(point, sandboxRoomDragHeight);
            sandboxRoomDragAppliedDelta = Vector3d.Zero;
            sandboxMovingRoom = true;
            SandboxRecord();
            sandboxStatus.Text = "Moving room " + sandboxMovingRoomIndex + " live; release to commit.";
            return true;
        }

        private void MoveSandboxRoomToSurface(Point point)
        {
            if (!sandboxMovingRoom || CurrentScene == null || sandboxMovingRoomIndex < 0) return;
            Vector3d current = GetSandboxPlanePosition(point, sandboxRoomDragHeight);
            Vector3d next = current - sandboxRoomDragStartWorld;
            if (sandboxSnap != null && sandboxSnap.Checked)
            {
                double grid = sandboxGrid == null ? 100 : (double)sandboxGrid.Value;
                next.X = Math.Round(next.X / grid) * grid;
                next.Z = Math.Round(next.Z / grid) * grid;
                next.Y = 0;
            }
            Vector3d delta = next - sandboxRoomDragAppliedDelta;
            if (delta.LengthSquared < 0.000001) return;
            MoveSandboxRoomVisual(sandboxMovingRoomIndex, delta);
            sandboxRoomDragAppliedDelta = next;
            sandboxStatus.Text = "Moving room " + sandboxMovingRoomIndex + " | X " + next.X.ToString("0.##") + " Z " + next.Z.ToString("0.##");
            glControl1.Invalidate();
        }

        private void CommitSandboxRoomMove()
        {
            if (!sandboxMovingRoom || CurrentScene == null) return;
            MoveSandboxRoomAncillary(sandboxMovingRoomIndex, sandboxRoomDragStartMin, sandboxRoomDragStartMax, sandboxRoomDragAppliedDelta);
            if (!string.IsNullOrEmpty(CurrentScene.SandboxGeometryFile))
            {
                PersistSandboxGeometry();
                ShiftSandboxRoomTilePlacements(sandboxMovingRoomIndex, sandboxRoomDragAppliedDelta);
            }
            NativeGameplayMaterializer.Apply(CurrentScene);
            savechanges = true;
            sandboxMovingRoom = false;
            sandboxMovingRoomIndex = -1;
        }

        private void ShiftSandboxRoomTilePlacements(int roomIndex, Vector3d delta)
        {
            if (CurrentScene == null || string.IsNullOrEmpty(CurrentScene.SandboxGeometryFile) || !File.Exists(CurrentScene.SandboxGeometryFile)) return;
            TileSceneGeometry geometry = TileStorage.Load<TileSceneGeometry>(CurrentScene.SandboxGeometryFile);
            foreach (PlacedTile placement in geometry.PlacedTiles.Where(p => p.Room == roomIndex)) placement.Position += delta;
            CurrentScene.SandboxGeometryFile = TileStorage.Save(geometry, false, geometry.Rooms.Concat(new[] { geometry.Collision }));
            TrackSandboxGeometryEdit(geometry, "Room tile placement shift");
        }

        private bool ShowSandboxMoveRoomDialog(out Vector3d delta)
        {
            delta = Vector3d.Zero;
            Vector3d requested = Vector3d.Zero;
            using (Form dialog = new Form { Text = "Move room bundle", ClientSize = new Size(390, 230), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 5 };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                NumericUpDown x = CreateMoveValue(), y = CreateMoveValue(), z = CreateMoveValue();
                layout.Controls.Add(new Label { Text = "Move X", AutoSize = true }, 0, 0); layout.Controls.Add(x, 1, 0);
                layout.Controls.Add(new Label { Text = "Move Y", AutoSize = true }, 0, 1); layout.Controls.Add(y, 1, 1);
                layout.Controls.Add(new Label { Text = "Move Z", AutoSize = true }, 0, 2); layout.Controls.Add(z, 1, 2);
                layout.Controls.Add(new Label { Text = "Applies to", AutoSize = true }, 0, 3);
                layout.Controls.Add(new Label { Text = "Visuals, collision, actors, doors, spawns, sockets, and gameplay", AutoSize = true }, 1, 3);
                FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
                Button move = new Button { Text = "Move room", AutoSize = true };
                move.Click += delegate { requested = new Vector3d((double)x.Value, (double)y.Value, (double)z.Value); dialog.DialogResult = DialogResult.OK; };
                actions.Controls.Add(cancel); actions.Controls.Add(move); layout.Controls.Add(actions, 0, 4); layout.SetColumnSpan(actions, 2);
                dialog.Controls.Add(layout); dialog.AcceptButton = move; dialog.CancelButton = cancel;
                bool accepted = dialog.ShowDialog(this) == DialogResult.OK;
                delta = requested;
                return accepted && delta != Vector3d.Zero;
            }
        }

        private static NumericUpDown CreateMoveValue()
        {
            return new NumericUpDown { Minimum = -32768, Maximum = 32767, DecimalPlaces = 0, Increment = 10, Dock = DockStyle.Fill };
        }

        private void MoveSandboxRoomBundle(int roomIndex, Vector3d delta)
        {
            ZScene.ZRoom room = CurrentScene.Rooms[roomIndex];
            Vector3d min, max;
            GetSandboxRoomBounds(room, out min, out max);
            MoveSandboxRoomVisual(roomIndex, delta);
            MoveSandboxRoomAncillary(roomIndex, min, max, delta);
            glControl1.Invalidate();
        }

        private void MoveSandboxRoomVisual(int roomIndex, Vector3d delta)
        {
            ZScene.ZRoom room = CurrentScene.Rooms[roomIndex];
            if (room.ObjModel != null)
                foreach (ObjFile.Vertex vertex in room.ObjModel.Vertices) MoveVertex(vertex, delta);
            if (room.AuthoringPrimitives != null)
                foreach (ZScene.ZRoom.RoomAuthoringPrimitive primitive in room.AuthoringPrimitives)
                {
                    primitive.MinX += delta.X; primitive.MaxX += delta.X;
                    primitive.MinY += delta.Y; primitive.MaxY += delta.Y;
                    primitive.MinZ += delta.Z; primitive.MaxZ += delta.Z;
                }
        }

        private void MoveSandboxRoomAncillary(int roomIndex, Vector3d min, Vector3d max, Vector3d delta)
        {
            MoveSandboxCollisionFaces(min, max, delta);
            HashSet<ZActor> actors = new HashSet<ZActor>(CurrentScene.Rooms[roomIndex].ZActors);
            foreach (ZActor actor in CurrentScene.SpawnPoints.Where(a => a.SpawnRoom == roomIndex)) actors.Add(actor);
            foreach (ZActor actor in CurrentScene.Transitions.Where(a => a.FrontSwitchTo == roomIndex || a.BackSwitchTo == roomIndex)) actors.Add(actor);
            foreach (ZActor actor in actors) MoveSandboxActor(actor, delta);
            foreach (TileSocket socket in CurrentScene.TileSockets.Where(s => s.Room == roomIndex)) socket.Position += delta;
            foreach (SandboxAuthoringMarker marker in sandboxAuthoringMarkers.Where(m => m.Room == roomIndex)) marker.Position += delta;
            if (CurrentScene.SandboxGameplayPackages != null)
                foreach (RoomPackage package in CurrentScene.SandboxGameplayPackages.Where(p => p.Room == roomIndex || p.DestinationRoom == roomIndex))
                    foreach (RoomPackageActor actor in package.Actors.Concat(package.Spawns)) actor.Position += delta;
        }

        private void MoveSandboxCollisionFaces(Vector3d min, Vector3d max, Vector3d delta)
        {
            if (CurrentScene.ColModel == null || CurrentScene.ColModel.Groups == null || CurrentScene.ColModel.Groups.Count == 0) return;
            Vector3d padding = new Vector3d(1, 1, 1);
            int crossing;
            HashSet<int> selected = TileAssembly.FacesInBox(CurrentScene.ColModel, CurrentScene.ColModel.Groups, min - padding, max + padding, out crossing);
            if (selected.Count == 0) return;

            TileMesh moving = TileMesh.Select(CurrentScene.ColModel, CurrentScene.ColModel.Groups, selected, Vector3d.Zero);
            moving.Transform(delta, 0);
            int index = 0;
            List<ObjFile.Group> remainingGroups = CurrentScene.ColModel.Groups.Select(group =>
            {
                ObjFile.Group clone = group.Clone();
                clone.Triangles = group.Triangles.Where(triangle => !selected.Contains(index++)).Select(TileMesh.CopyTriangle).ToList();
                return clone;
            }).Where(group => group.Triangles.Count > 0).ToList();
            TileMesh rebuilt = TileMesh.Copy(CurrentScene.ColModel, remainingGroups);
            rebuilt.Append(moving, "moved_room_" + Guid.NewGuid().ToString("N") + "_");
            CurrentScene.ColModel = rebuilt.ToModel();
            CurrentScene.ColModel.Prepare(false, CurrentScene.ColModel.Groups);
        }

        private static void MoveVertex(ObjFile.Vertex vertex, Vector3d delta)
        {
            vertex.X += delta.X; vertex.Y += delta.Y; vertex.Z += delta.Z;
        }

        private static void MoveSandboxActor(ZActor actor, Vector3d delta)
        {
            actor.XPos += (float)delta.X; actor.YPos += (float)delta.Y; actor.ZPos += (float)delta.Z;
        }

        private static void GetSandboxRoomBounds(ZScene.ZRoom room, out Vector3d min, out Vector3d max)
        {
            List<ObjFile.Vertex> vertices = room.ObjModel == null ? new List<ObjFile.Vertex>() : room.ObjModel.Vertices;
            if (vertices.Count == 0) { min = new Vector3d(-1, -1, -1); max = new Vector3d(1, 1, 1); return; }
            min = new Vector3d(vertices.Min(v => v.X), vertices.Min(v => v.Y), vertices.Min(v => v.Z));
            max = new Vector3d(vertices.Max(v => v.X), vertices.Max(v => v.Y), vertices.Max(v => v.Z));
            if (min.X == max.X) max.X += 1; if (min.Y == max.Y) max.Y += 1; if (min.Z == max.Z) max.Z += 1;
        }

        private void SelectSandboxTile(PlacedTile tile)
        {
            if (tile == null) return;
            sandboxSelectedTile = tile;
            sandboxInspector.SelectedObject = tile;
            if (sandboxSelectionSummary != null)
                sandboxSelectionSummary.Text = string.Join(Environment.NewLine, new[]
                {
                    tile.Name + "  [Room tile]",
                    "Room " + tile.Room + "  Position " + tile.X.ToString("0.##") + ", " + tile.Y.ToString("0.##") + ", " + tile.Z.ToString("0.##"),
                    "Rotation: " + (tile.Turns * 90) + " degrees | Scale: " + (tile.Scale <= 0 ? 1.0 : tile.Scale).ToString("0.##"),
                    "Actions: Move drag, Rotate click, Scale mouse wheel, or right-click for tile actions."
                });
            sandboxContents.ClearSelected();
            sandboxStatus.Text = "Selected tile: " + tile.Name;
        }

        private void SelectSandboxAuthoringMarker(SandboxAuthoringMarker marker)
        {
            if (marker == null) return;
            sandboxSelectedMarker = marker;
            sandboxSelectedTile = null;
            sandboxInspector.SelectedObject = marker;
            sandboxSelectionSummary.Text = string.Join(Environment.NewLine, new[]
            {
                (marker.Name ?? marker.Kind) + "  [App-only marker]",
                "Kind: " + marker.Kind + "  Room: " + marker.Room,
                "Position: " + marker.X.ToString("0.##") + ", " + marker.Y.ToString("0.##") + ", " + marker.Z.ToString("0.##"),
                "Facing: " + marker.Facing.ToString("0.##") + " degrees | Size: " + marker.Width.ToString("0.##") + " x " + marker.Height.ToString("0.##"),
                IsSandboxSnapMarker(marker) ? "Magnetic target: enabled" : "Magnetic target: disabled"
            });
            sandboxContents.ClearSelected();
            sandboxStatus.Text = "Selected app-only marker: " + (marker.Name ?? marker.Kind);
            glControl1.Invalidate();
        }

        private void PlaceSandboxAuthoringMarker(Point point)
        {
            if (CurrentScene == null || RoomList.SelectedIndex < 0 || RoomList.SelectedIndex >= CurrentScene.Rooms.Count)
                throw new InvalidOperationException("Select a destination room before placing an authoring marker.");
            PersistSandboxGeometry();
            string kind = Convert.ToString(sandboxMarkerKind.SelectedItem);
            double facing = Convert.ToDouble(Convert.ToString(sandboxMarkerFacing.SelectedItem));
            Vector3d position = SandboxDropPosition(point);
            SandboxAuthoringMarker marker = new SandboxAuthoringMarker
            {
                Id = "marker_" + Guid.NewGuid().ToString("N"),
                Name = kind + " " + (sandboxAuthoringMarkers.Count(m => string.Equals(m.Kind, kind, StringComparison.OrdinalIgnoreCase)) + 1),
                Kind = kind,
                Room = RoomList.SelectedIndex,
                Position = position,
                Facing = facing,
                Width = 100,
                Height = 180
            };
            marker.Validate();
            SandboxRecord();
            sandboxAuthoringMarkers.Add(marker);
            PersistSandboxGeometry();
            savechanges = true;
            RefreshSandboxLayers();
            SelectSandboxAuthoringMarker(marker);
            sandboxStatus.Text = "Placed app-only " + kind + " marker. It will not export to the ROM.";
        }

        private void DeleteSandboxAuthoringMarker(SandboxAuthoringMarker marker)
        {
            if (marker == null) return;
            SandboxRecord();
            sandboxAuthoringMarkers.RemoveAll(item => item.Id == marker.Id);
            PersistSandboxGeometry();
            sandboxSelectedMarker = null;
            savechanges = true;
            RefreshSandboxLayers();
            sandboxInspector.SelectedObject = null;
            sandboxStatus.Text = "Deleted app-only authoring marker.";
            glControl1.Invalidate();
        }

        private PlacedTile PickSandboxTile(Point point)
        {
            if (CurrentScene == null || string.IsNullOrEmpty(CurrentScene.SandboxGeometryFile) || RoomList.SelectedIndex < 0) return null;
            TileSceneGeometry geometry;
            try { geometry = TileStorage.Load<TileSceneGeometry>(CurrentScene.SandboxGeometryFile); }
            catch { return null; }
            Vector3d start, end; GetSandboxRay(point, out start, out end);
            Vector3d ray = end - start;
            double bestT = double.MaxValue; PlacedTile best = null;
            foreach (PlacedTile placement in geometry.PlacedTiles.Where(p => p.Room == RoomList.SelectedIndex))
            {
                if (string.IsNullOrEmpty(placement.TilePath) || !File.Exists(placement.TilePath)) continue;
                SectionTile tile;
                try { tile = TileStorage.Load<SectionTile>(placement.TilePath); } catch { continue; }
                Vector3d min = new Vector3d(double.MaxValue), max = new Vector3d(double.MinValue);
                foreach (ObjFile.Vertex vertex in tile.Visual.Vertices)
                {
                    Vector3d world = TileAssembly.Rotate(vertex.ToVector3d() * (placement.Scale <= 0 ? 1.0 : placement.Scale), placement.Turns) + placement.Position;
                    min.X = Math.Min(min.X, world.X); min.Y = Math.Min(min.Y, world.Y); min.Z = Math.Min(min.Z, world.Z);
                    max.X = Math.Max(max.X, world.X); max.Y = Math.Max(max.Y, world.Y); max.Z = Math.Max(max.Z, world.Z);
                }
                if (tile.Visual.Vertices.Count == 0) continue;
                double hitT;
                if (RayBoxHit(start, ray, min - new Vector3d(8), max + new Vector3d(8), out hitT) && hitT < bestT)
                { bestT = hitT; best = placement; }
            }
            return best;
        }

        private static bool RayBoxHit(Vector3d origin, Vector3d direction, Vector3d min, Vector3d max, out double hit)
        {
            double near = 0, far = 1;
            double[] o = { origin.X, origin.Y, origin.Z }, d = { direction.X, direction.Y, direction.Z };
            double[] lo = { min.X, min.Y, min.Z }, hi = { max.X, max.Y, max.Z };
            for (int i = 0; i < 3; i++)
            {
                if (Math.Abs(d[i]) < 0.000001) { if (o[i] < lo[i] || o[i] > hi[i]) { hit = 0; return false; } continue; }
                double a = (lo[i] - o[i]) / d[i], b = (hi[i] - o[i]) / d[i];
                if (a > b) { double swap = a; a = b; b = swap; }
                near = Math.Max(near, a); far = Math.Min(far, b);
                if (near > far) { hit = 0; return false; }
            }
            hit = near; return true;
        }

        private bool BeginSandboxTileMove(Point point)
        {
            PlacedTile tile = PickSandboxTile(point);
            if (tile == null) return false;
            SelectSandboxTile(tile);
            sandboxTileDragOrigin = tile.Position;
            sandboxTileDragStartWorld = GetSandboxPlanePosition(point, tile.Y);
            sandboxMovingTile = true;
            SandboxRecord();
            sandboxStatus.Text = "Moving tile: " + tile.Name + " | release to place";
            return true;
        }

        private void RotateSandboxTileAt(Point point)
        {
            PlacedTile selected = PickSandboxTile(point);
            if (selected == null) return;
            SelectSandboxTile(selected);
            if (selected.SourceSocket >= 0)
            {
                sandboxStatus.Text = "Disconnect this snapped tile before rotating it.";
                return;
            }
            if (string.IsNullOrEmpty(CurrentScene.SandboxGeometryFile)) return;
            SandboxRecord();
            TileSceneGeometry geometry = TileStorage.Load<TileSceneGeometry>(CurrentScene.SandboxGeometryFile);
            PlacedTile placement = geometry.PlacedTiles.FirstOrDefault(p => p.Id == selected.Id);
            if (placement == null) return;
            placement.Turns = (placement.Turns + 1) % 4;
            string saved = TileStorage.Save(RebuildSandboxGeometry(geometry), false, geometry.Rooms.Concat(new[] { geometry.Collision }));
            CurrentScene.SandboxGeometryFile = saved;
            TrackSandboxGeometryEdit(geometry, "Rotate sandbox tile");
            RestoreSandboxGeometry(saved);
            NativeGameplayMaterializer.Apply(CurrentScene);
            sandboxSelectedTile = TileStorage.Load<TileSceneGeometry>(saved).PlacedTiles.FirstOrDefault(p => p.Id == selected.Id);
            if (sandboxSelectedTile != null) SelectSandboxTile(sandboxSelectedTile);
            savechanges = true;
            RefreshSandboxContents();
            sandboxStatus.Text = "Rotated tile 90 degrees: " + selected.Name;
        }

        private bool SelectSandboxTileForScaling(Point point)
        {
            PlacedTile selected = PickSandboxTile(point);
            if (selected == null) return false;
            SelectSandboxTile(selected);
            sandboxStatus.Text = "Scale tool active: use the mouse wheel over the tile.";
            return true;
        }

        private void ScaleSelectedSandboxTile(double factor)
        {
            if (sandboxSelectedTile == null || string.IsNullOrEmpty(CurrentScene.SandboxGeometryFile)) return;
            if (sandboxSelectedTile.SourceSocket >= 0)
            {
                sandboxStatus.Text = "Disconnect this snapped tile before scaling it.";
                return;
            }
            SandboxRecord();
            TileSceneGeometry geometry = TileStorage.Load<TileSceneGeometry>(CurrentScene.SandboxGeometryFile);
            PlacedTile placement = geometry.PlacedTiles.FirstOrDefault(p => p.Id == sandboxSelectedTile.Id);
            if (placement == null) return;
            double current = placement.Scale <= 0 ? 1.0 : placement.Scale;
            placement.Scale = Math.Max(0.1, Math.Min(4.0, current * factor));
            string saved = TileStorage.Save(RebuildSandboxGeometry(geometry), false, geometry.Rooms.Concat(new[] { geometry.Collision }));
            CurrentScene.SandboxGeometryFile = saved;
            TrackSandboxGeometryEdit(geometry, "Scale sandbox tile");
            RestoreSandboxGeometry(saved);
            NativeGameplayMaterializer.Apply(CurrentScene);
            sandboxSelectedTile = TileStorage.Load<TileSceneGeometry>(saved).PlacedTiles.FirstOrDefault(p => p.Id == sandboxSelectedTile.Id);
            if (sandboxSelectedTile != null) SelectSandboxTile(sandboxSelectedTile);
            savechanges = true;
            RefreshSandboxContents();
            sandboxStatus.Text = "Scaled tile to " + (sandboxSelectedTile == null ? placement.Scale : sandboxSelectedTile.Scale).ToString("0.##") + ".";
        }

        private static TileSceneGeometry RebuildSandboxGeometry(TileSceneGeometry geometry)
        {
            geometry.RebuildPlacements();
            return geometry;
        }

        private Vector3d GetSandboxPlanePosition(Point point, double height)
        {
            Vector3d start, end; GetSandboxRay(point, out start, out end);
            double dy = end.Y - start.Y;
            if (Math.Abs(dy) < 0.000001) return start;
            double t = (height - start.Y) / dy;
            return start + (end - start) * Math.Max(0, Math.Min(1, t));
        }

        private void MoveSandboxTileToSurface(Point point)
        {
            if (!sandboxMovingTile || sandboxSelectedTile == null || string.IsNullOrEmpty(CurrentScene.SandboxGeometryFile)) return;
            Vector3d current = GetSandboxPlanePosition(point, sandboxTileDragOrigin.Y);
            Vector3d next = sandboxTileDragOrigin + (current - sandboxTileDragStartWorld);
            if (sandboxSnap != null && sandboxSnap.Checked)
            {
                double grid = sandboxGrid == null ? 100 : (double)sandboxGrid.Value;
                next.X = Math.Round(next.X / grid) * grid; next.Z = Math.Round(next.Z / grid) * grid;
            }
            string id = sandboxSelectedTile.Id;
            TileSceneGeometry geometry = TileStorage.Load<TileSceneGeometry>(CurrentScene.SandboxGeometryFile);
            PlacedTile placement = geometry.PlacedTiles.FirstOrDefault(p => p.Id == id);
            if (placement == null) return;
            placement.Position = next;
            geometry.RebuildPlacements();
            string saved = TileStorage.Save(geometry, false, geometry.Rooms.Concat(new[] { geometry.Collision }));
            CurrentScene.SandboxGeometryFile = saved;
            TrackSandboxGeometryEdit(geometry, "Move sandbox tile");
            ClearSandboxMaterializedGameplay();
            RestoreSandboxGeometry(saved);
            NativeGameplayMaterializer.Apply(CurrentScene);
            sandboxSelectedTile = TileStorage.Load<TileSceneGeometry>(saved).PlacedTiles.FirstOrDefault(p => p.Id == id);
            if (sandboxSelectedTile != null) SelectSandboxTile(sandboxSelectedTile);
            RefreshSandboxContents();
        }

        private void ShowSandboxTileQuickMenu(Point screenPoint)
        {
            if (sandboxSelectedTile == null) return;
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Move tile", null, delegate { if (sandboxInteractionMode != null) sandboxInteractionMode.SelectedItem = "Move"; });
            menu.Items.Add("Delete tile", null, delegate { SandboxRun(DeleteSelectedSandboxTile); });
            menu.Show(screenPoint);
        }

        private void DeleteSelectedSandboxTile()
        {
            if (sandboxSelectedTile == null || string.IsNullOrEmpty(CurrentScene.SandboxGeometryFile)) return;
            SandboxRecord();
            TileSceneGeometry geometry = TileStorage.Load<TileSceneGeometry>(CurrentScene.SandboxGeometryFile);
            geometry.PlacedTiles.RemoveAll(p => p.Id == sandboxSelectedTile.Id);
            geometry.RebuildPlacements();
            string saved = TileStorage.Save(geometry, false, geometry.Rooms.Concat(new[] { geometry.Collision }));
            CurrentScene.SandboxGeometryFile = saved;
            TrackSandboxGeometryEdit(geometry, "Delete sandbox tile");
            RestoreSandboxGeometry(saved);
            sandboxSelectedTile = null;
            savechanges = true;
            sandboxStatus.Text = "Deleted selected tile.";
        }
    }
}
