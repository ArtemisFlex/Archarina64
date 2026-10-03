using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Serialization;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace SharpOcarina
{
    public partial class MainForm
    {
        private bool sandboxRandomDungeonSearchActive;
        private ZScene sandboxRandomDungeonSearchTemplate;
        private string randomDungeonPlacementDiagnostics;

        private const double RandomDungeonDoorGap = 300;
        private const double RandomDungeonDoorGapStep = 250;
        private const double RandomDungeonMaxDoorGap = 5000;
        private readonly HashSet<int> sandboxVisualFaces = new HashSet<int>();
        private readonly HashSet<int> sandboxCollisionFaces = new HashSet<int>();
        private int sandboxFaceMode;
        private ComboBox sandboxTileRotation;
        private ComboBox sandboxTileSurface;
        private Label sandboxFaceCount;
        private TextBox sandboxStartingItems;

        private sealed class RandomDungeonTile
        {
            internal RomPaletteAsset Asset;
            internal SectionTile Tile;
            internal bool HasSpawn, HasBoss, HasReward;
            internal int NpcCount, EnemyCount, PuzzleCount, ObstacleCount, ItemCount, FlagCount, RequiredObjectCount;
            internal List<string> RequiredItems = new List<string>();
            internal List<string> GrantedItems = new List<string>();
            internal int LogicLinkCount;
        }

        private sealed class TextureThemeChoice
        {
            internal RandomDungeonTile Tile;
            public override string ToString() { return Tile == null || Tile.Asset == null ? "Default room materials" : Tile.Asset.Name; }
        }

        private sealed class RandomDungeonPlacement
        {
            internal RandomDungeonTile Tile;
            internal int Room;
            internal Vector3d Position;
            internal int Turns;
            internal Vector3d Min, Max;
            internal HashSet<string> UsedSockets = new HashSet<string>(StringComparer.Ordinal);
            internal RandomDungeonPlacement Parent;
            internal string ParentSocketId;
            internal string IncomingSocketId;
            internal Vector3d SpawnPosition;
            internal Vector3 SpawnRotation;
        }

        private sealed class RandomDungeonConnection
        {
            internal int FromRoom, ToRoom;
            internal string SourceSocketId, TargetSocketId;
            internal Vector3d Start, End;
            internal List<Vector3d> Path;
            internal double Facing;
            internal double Width, Height;
            internal bool NeedsCorridor, NeedsRamp;
        }

        private sealed class RandomDungeonLayout
        {
            internal readonly List<RandomDungeonPlacement> Placements = new List<RandomDungeonPlacement>();
            internal readonly List<RandomDungeonConnection> Connections = new List<RandomDungeonConnection>();
            internal readonly List<int> Route = new List<int>();
            internal string ThemeFloor, ThemeCeiling, ThemeWall;
            internal int FallbackPlacements;
        }

        private sealed class GeneratedDungeonGameplaySummary
        {
            internal int SpawnCount, EnemyCount, BossCount, NpcCount, PuzzleCount, RewardCount, ObstacleCount, LinkCount, ItemArcCount;
            internal string ItemArcItem;
            internal readonly List<string> Issues = new List<string>();

            internal bool IsPlayable
            {
                get { return SpawnCount > 0 && EnemyCount > 0 && BossCount > 0 && RewardCount > 0 && Issues.Count == 0; }
            }
        }

        private sealed class ItemArcNavigation
        {
            internal DungeonItemArcStep Step;
            internal int Room;
            internal bool Acquisition;
        }

        private void InitializeSandboxTiles(FlowLayoutPanel toolbar)
        {
            toolbar.Height = 190; toolbar.MinimumSize = new Size(0, 190); toolbar.WrapContents = true; toolbar.SetFlowBreak(sandboxIsolateRoom, true);
            FlowLayoutPanel tools = new FlowLayoutPanel { Width = 970, Height = 94, MinimumSize = new Size(970, 94), WrapContents = true, AutoScroll = false, Margin = Padding.Empty };
            tools.Controls.Add(new Label { Text = "BUILD / EXTRACT", AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(0, 5, 8, 0) });
            string[] modes = { "Place", "Visual faces", "Collision faces" };
            for (int i = 0; i < modes.Length; i++)
            {
                int mode = i;
                RadioButton button = new RadioButton { Text = modes[i], AutoSize = true, Checked = i == 0 };
                button.CheckedChanged += delegate { if (button.Checked) { sandboxFaceMode = mode; glControl1.Invalidate(); } };
                tools.Controls.Add(button);
            }
            Button clear = new Button { Text = "Clear selection", AutoSize = true };
            clear.Click += delegate { ClearSandboxFaces(); };
            Button save = new Button { Text = "Save tile...", AutoSize = true };
            save.Click += delegate { SandboxRun(ShowSaveSandboxTile); };
            Button chopEntrances = new Button { Text = "Auto-chop entrances", AutoSize = true };
            chopEntrances.Click += delegate { SandboxRun(AutoChopSandboxEntrances); };
            Button extractRooms = new Button { Text = "Extract room tiles...", AutoSize = true };
            extractRooms.Click += delegate { SandboxRun(ExtractSandboxRoomLibrary); };
            Button generateKit = new Button { Text = "Generate custom tile kit...", AutoSize = true };
            generateKit.Click += delegate { SandboxRun(ShowProceduralTileKitDialog); };
            Button texturePack = new Button { Text = "Texture pack...", AutoSize = true };
            texturePack.Click += delegate { SandboxRun(ShowSandboxTexturePackDialog); };
            Button analyzeDungeon = new Button { Text = "Analyze / generate", AutoSize = true };
            analyzeDungeon.Click += delegate { SandboxRun(AnalyzeAndPlanDungeon); };
            Button randomizeDungeon = new Button { Text = "Randomize playable dungeon...", AutoSize = true };
            randomizeDungeon.Click += delegate { SandboxRun(ShowRandomDungeonDialog); };
            Button analyzeRom = new Button { Text = "Analyze ROM", AutoSize = true };
            analyzeRom.Click += delegate { SandboxRun(AnalyzeRomAndPlanDungeon); };
            sandboxTileRotation = new ComboBox { Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };
            sandboxTileRotation.Items.AddRange(new object[] { "0 deg", "90 deg", "180 deg", "270 deg" }); sandboxTileRotation.SelectedIndex = 0;
            sandboxFaceCount = new Label { AutoSize = true, Padding = new Padding(0, 5, 0, 0) };
            sandboxTileSurface = new ComboBox { Width = 135, DropDownStyle = ComboBoxStyle.DropDownList };
            tools.Controls.Add(new Label { Text = "Starting items:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            sandboxStartingItems = new TextBox { Width = 180 };
            tools.Controls.Add(clear); tools.Controls.Add(save); tools.Controls.Add(chopEntrances); tools.Controls.Add(extractRooms); tools.Controls.Add(generateKit); tools.Controls.Add(texturePack); tools.Controls.Add(analyzeDungeon); tools.Controls.Add(randomizeDungeon); tools.Controls.Add(analyzeRom); tools.Controls.Add(sandboxTileRotation); tools.Controls.Add(sandboxTileSurface); tools.Controls.Add(sandboxStartingItems); tools.Controls.Add(sandboxFaceCount);
            toolbar.Controls.Add(tools); InitializeSandboxAssembly(toolbar, tools); ClearSandboxFaces();
        }

        private void ShowRandomDungeonDialog()
        {
            if (sandboxCatalog == null) throw new InvalidOperationException("Import a ROM and create or extract at least two dungeon tiles first.");
            List<RandomDungeonTile> available = LoadRandomDungeonTiles(ReadStartingItems());
            int socketedCount = available.Count(tile => RandomDungeonSockets(tile).Count > 0);
            if (!HasRandomDungeonSocketCapacity(available) && !available.Any(tile => IsGeneratedCustomTile(tile.Asset)))
            {
                GenerateProceduralTileKit(50, DateTime.Now.Millisecond + DateTime.Now.Second * 1000);
                available = LoadRandomDungeonTiles(ReadStartingItems());
                socketedCount = available.Count(tile => RandomDungeonSockets(tile).Count > 0);
            }
            available = available.Where(tile => RandomDungeonSockets(tile).Count > 0).ToList();
            if (available.Count < 2)
                throw new InvalidOperationException("At least two saved tiles with real doorway sockets are required. Extract room entrances or generate the custom tile kit before randomization.");
            using (Form dialog = new Form { Text = "Randomize playable dungeon", ClientSize = new Size(560, 510), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 14 };
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                NumericUpDown count = new NumericUpDown { Minimum = 2, Maximum = Math.Min(64, available.Count), Value = Math.Min(8, available.Count), Dock = DockStyle.Fill };
                NumericUpDown seed = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Value = DateTime.Now.Millisecond + DateTime.Now.Second * 1000, Dock = DockStyle.Fill };
                CheckBox boss = new CheckBox { Text = "Require a boss/finale room", Checked = true, AutoSize = true };
                CheckBox reward = new CheckBox { Text = "Require a reward room", Checked = true, AutoSize = true };
                CheckBox itemArc = new CheckBox { Text = "Require a mid-dungeon item arc", Checked = true, AutoSize = true };
                NumericUpDown minimumItemArcs = new NumericUpDown { Minimum = 1, Maximum = 2, Value = 1, Width = 70 };
                CheckBox partial = new CheckBox { Text = "Allow a partial result if constraints cannot be met", AutoSize = true };
                bool hasCustomKit = available.Any(tile => IsGeneratedCustomTile(tile.Asset));
                CheckBox customOnly = new CheckBox { Text = "Use generated custom tile kit only", Checked = hasCustomKit, Enabled = hasCustomKit, AutoSize = true };
                CheckBox overrideTextures = new CheckBox { Text = "Use one texture set for the generated dungeon", Checked = true, AutoSize = true };
                NumericUpDown maxAttempts = new NumericUpDown { Minimum = 1, Maximum = 2048, Value = 256, Dock = DockStyle.Fill };
                ComboBox textureSet = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
                foreach (RandomDungeonTile tile in available.OrderBy(t => t.Asset == null ? "" : t.Asset.Name, StringComparer.OrdinalIgnoreCase)) textureSet.Items.Add(new TextureThemeChoice { Tile = tile });
                TextureThemeChoice defaultTheme = textureSet.Items.Cast<TextureThemeChoice>().FirstOrDefault(item => item.Tile != null && item.Tile.HasSpawn) ?? textureSet.Items.Cast<TextureThemeChoice>().FirstOrDefault();
                if (defaultTheme != null) textureSet.SelectedItem = defaultTheme;
                overrideTextures.CheckedChanged += delegate { textureSet.Enabled = overrideTextures.Checked; };
                fields.Controls.Add(new Label { Text = "Room count", AutoSize = true }, 0, 0); fields.Controls.Add(count, 1, 0);
                fields.Controls.Add(new Label { Text = "Seed", AutoSize = true }, 0, 1); fields.Controls.Add(seed, 1, 1);
                fields.Controls.Add(boss, 1, 2); fields.Controls.Add(reward, 1, 3); fields.Controls.Add(itemArc, 1, 4); fields.Controls.Add(partial, 1, 5);
                fields.Controls.Add(new Label { Text = "Minimum item arcs", AutoSize = true }, 0, 6); fields.Controls.Add(minimumItemArcs, 1, 6);
                fields.Controls.Add(new Label { Text = "Starting items", AutoSize = true }, 0, 7); fields.Controls.Add(new Label { Text = string.IsNullOrWhiteSpace(sandboxStartingItems.Text) ? "None" : sandboxStartingItems.Text, AutoSize = true }, 1, 7);
                fields.Controls.Add(customOnly, 1, 8);
                fields.Controls.Add(overrideTextures, 1, 9);
                fields.Controls.Add(new Label { Text = "Texture set", AutoSize = true }, 0, 10); fields.Controls.Add(textureSet, 1, 10);
                fields.Controls.Add(new Label { Text = "Automatic search attempts", AutoSize = true }, 0, 11); fields.Controls.Add(maxAttempts, 1, 11);
                FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
                Button generate = new Button { Text = "Generate dungeon", AutoSize = true };
                Label searchStatus = new Label { Text = "The generator will try different seeds until a certified dungeon is found.", AutoSize = true, Dock = DockStyle.Fill };
                fields.Controls.Add(searchStatus, 0, 13); fields.SetColumnSpan(searchStatus, 2);
                generate.Click += delegate
                {
                    TextureThemeChoice choice = textureSet.SelectedItem as TextureThemeChoice;
                    BeginRandomDungeonSearch(dialog, generate, searchStatus, (int)count.Value, (int)seed.Value, (int)maxAttempts.Value,
                        boss.Checked, reward.Checked, itemArc.Checked, (int)minimumItemArcs.Value, partial.Checked, available, customOnly.Checked,
                        overrideTextures.Checked, choice == null ? null : choice.Tile);
                };
                actions.Controls.Add(cancel); actions.Controls.Add(generate); fields.Controls.Add(actions, 0, 12); fields.SetColumnSpan(actions, 2);
                dialog.Controls.Add(fields); dialog.AcceptButton = generate; dialog.CancelButton = cancel; dialog.ShowDialog(this);
            }
        }

        private void BeginRandomDungeonSearch(Form dialog, Button generate, Label status, int roomCount, int seed, int maxAttempts,
            bool requireBoss, bool requireReward, bool requireItemArc, int minimumItemArcs, bool allowPartial, List<RandomDungeonTile> available,
            bool customOnly, bool overrideTextures, RandomDungeonTile textureTheme)
        {
            if (dialog == null || generate == null || status == null) return;
            generate.Enabled = false;
            int attempt = 0;
            int candidateSeed = seed;
            Exception lastError = null;
            sandboxRandomDungeonSearchTemplate = null;
            Timer retryTimer = new Timer { Interval = 25 };
            retryTimer.Tick += delegate
            {
                if (attempt >= maxAttempts)
                {
                    retryTimer.Stop(); retryTimer.Dispose(); generate.Enabled = true;
                    sandboxRandomDungeonSearchTemplate = null;
                    status.Text = "Search exhausted without a certified dungeon.";
                    string detail = lastError == null ? "No candidate was produced." : lastError.Message;
                    MessageBox.Show(dialog, "No certified dungeon was found after " + maxAttempts + " attempts.\n\nLast rejection:\n" + detail,
                        "Dungeon randomizer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                attempt++;
                status.Text = "Trying seed " + candidateSeed + " (attempt " + attempt + " of " + maxAttempts + ")...";
                sandboxStatus.Text = "Searching for a certified dungeon: attempt " + attempt + " of " + maxAttempts + ".";
                try
                {
            sandboxRandomDungeonSearchActive = true;
            try
            {
                GenerateRandomDungeon(roomCount, candidateSeed, requireBoss, requireReward, requireItemArc, minimumItemArcs,
                    allowPartial, available, customOnly, overrideTextures, textureTheme);
            }
            finally
            {
                sandboxRandomDungeonSearchActive = false;
            }
                    retryTimer.Stop(); retryTimer.Dispose();
                    sandboxRandomDungeonSearchTemplate = null;
                    PersistSandboxGeometry();
                    savechanges = true;
                    RefreshSandboxRooms(); ApplySandboxPreview(); FocusSandboxRoom();
                    status.Text = "Certified dungeon found with seed " + candidateSeed + ".";
                    dialog.DialogResult = DialogResult.OK;
                    return;
                }
                catch (Exception error)
                {
                    lastError = error;
                    candidateSeed = candidateSeed == int.MaxValue ? 0 : candidateSeed + 1;
                    string firstLine = (error.Message ?? "Generation rejected").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    status.Text = "Rejected seed " + (candidateSeed == 0 ? int.MaxValue : candidateSeed - 1) + ": " + (string.IsNullOrWhiteSpace(firstLine) ? "generation constraints" : firstLine);
                }
            };
            dialog.FormClosed += delegate { retryTimer.Stop(); retryTimer.Dispose(); sandboxRandomDungeonSearchTemplate = null; };
            retryTimer.Start();
        }

        private List<RandomDungeonTile> LoadRandomDungeonTiles(List<string> startingItems)
        {
            HashSet<string> inventory = new HashSet<string>(startingItems ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            List<RandomDungeonTile> result = new List<RandomDungeonTile>();
            foreach (RomPaletteAsset asset in sandboxCatalog.Assets.Where(a => (a.Room != null || IsGeneratedCustomTile(a)) && !string.IsNullOrEmpty(a.TilePath)).ToList())
            {
                try
                {
                    SectionTile tile = TileStorage.Load<SectionTile>(asset.TilePath);
                    if (tile == null || tile.Version != 1 || tile.Visual == null || tile.Collision == null) continue;
                    List<RoomPackageActor> actors = tile.Gameplay == null ? new List<RoomPackageActor>() : (tile.Gameplay.Actors ?? new List<RoomPackageActor>());
                    int npcCount = actors.Count(a => SandboxActorLooksLikeNpc(a));
                    int enemyCount = actors.Count(a => SandboxActorLooksLikeEnemy(a));
                    int puzzleCount = actors.Count(a => SandboxActorLooksLikePuzzle(a)) + (tile.Gameplay == null || tile.Gameplay.Links == null ? 0 : tile.Gameplay.Links.Count(l => l.Kind == LogicLinkKind.CompletesPuzzle || l.Kind == LogicLinkKind.Activates || l.Kind == LogicLinkKind.Opens || l.Kind == LogicLinkKind.Unlocks));
                    int obstacleCount = actors.Count(a => SandboxActorLooksLikeObstacle(a));
                    int itemCount = actors.Count(a => SandboxActorLooksLikeItem(a));
                    result.Add(new RandomDungeonTile { Asset = asset, Tile = tile,
                        HasSpawn = tile.Gameplay != null && tile.Gameplay.Spawns != null && tile.Gameplay.Spawns.Count > 0,
                        HasBoss = actors.Any(SandboxActorLooksLikeBoss), HasReward = actors.Any(SandboxActorLooksLikeReward),
                        NpcCount = npcCount, EnemyCount = enemyCount, PuzzleCount = puzzleCount, ObstacleCount = obstacleCount, ItemCount = itemCount,
                        FlagCount = actors.Sum(a => a.Flags == null ? 0 : a.Flags.Count),
                        RequiredObjectCount = actors.SelectMany(a => a.RequiredObjects ?? new List<ushort>()).Distinct().Count(),
                        RequiredItems = actors.SelectMany(a => a.RequiredItems ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        GrantedItems = actors.SelectMany(a => a.GrantedItems ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        LogicLinkCount = tile.Gameplay == null || tile.Gameplay.Links == null ? 0 : tile.Gameplay.Links.Count });
                }
                catch (Exception error) { ArcharinaWorkspace.LogError(error); }
            }
            return result;
        }

        private void ShowProceduralTileKitDialog()
        {
            if (sandboxCatalog == null || CurrentScene == null) throw new InvalidOperationException("Open a ROM and destination scene first.");
            using (Form dialog = new Form { Text = "Generate custom dungeon tile kit", ClientSize = new Size(520, 260), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 5 };
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                NumericUpDown count = new NumericUpDown { Minimum = 1, Maximum = 128, Value = 50, Dock = DockStyle.Fill };
                NumericUpDown seed = new NumericUpDown { Minimum = 0, Maximum = int.MaxValue, Value = DateTime.Now.Millisecond + DateTime.Now.Second * 1000, Dock = DockStyle.Fill };
                fields.Controls.Add(new Label { Text = "Tile count", AutoSize = true }, 0, 0); fields.Controls.Add(count, 1, 0);
                fields.Controls.Add(new Label { Text = "Seed", AutoSize = true }, 0, 1); fields.Controls.Add(seed, 1, 1);
                fields.Controls.Add(new Label { Text = "Doorway contract", AutoSize = true }, 0, 2);
                fields.Controls.Add(new Label { Text = "OoT socket width 100 / height 180; grid 100", AutoSize = true }, 1, 2);
                fields.Controls.Add(new Label { Text = "Families", AutoSize = true }, 0, 3);
                fields.Controls.Add(new Label { Text = "1x1, 2x2, 4x4, 1x2, 1x3, 1x4, 2x4; four-way, two-way, dead-end, platform/ramp, stairs, fall shaft", AutoSize = true }, 1, 3);
                FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
                Button generate = new Button { Text = "Generate and save kit", AutoSize = true };
                generate.Click += delegate
                {
                    try
                    {
                        int generated = GenerateProceduralTileKit((int)count.Value, (int)seed.Value);
                        sandboxStatus.Text = "Generated " + generated + " permanent custom dungeon tiles.";
                        dialog.DialogResult = DialogResult.OK;
                    }
                    catch (Exception error) { MessageBox.Show(dialog, error.Message, "Custom tile kit", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                };
                actions.Controls.Add(cancel); actions.Controls.Add(generate); fields.Controls.Add(actions, 0, 4); fields.SetColumnSpan(actions, 2);
                dialog.Controls.Add(fields); dialog.AcceptButton = generate; dialog.CancelButton = cancel; dialog.ShowDialog(this);
            }
        }

        private void ShowSandboxTexturePackDialog()
        {
            if (sandboxCatalog == null || CurrentScene == null) throw new InvalidOperationException("Open a ROM and destination scene first.");
            List<RandomDungeonTile> themes = LoadRandomDungeonTiles(new List<string>())
                .Where(tile => tile.Tile != null && tile.Tile.Visual != null && tile.Tile.Visual.Materials.Count > 0)
                .OrderBy(tile => tile.Asset == null ? "" : tile.Asset.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            using (Form dialog = new Form { Text = "Choose dungeon texture pack", ClientSize = new Size(620, 280), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 5 };
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                ComboBox textureSet = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
                textureSet.Items.Add(new TextureThemeChoice { Tile = null });
                foreach (RandomDungeonTile tile in themes) textureSet.Items.Add(new TextureThemeChoice { Tile = tile });
                string currentPath = ReadSandboxTextureThemePath();
                TextureThemeChoice selected = textureSet.Items.Cast<TextureThemeChoice>().FirstOrDefault(item => item.Tile != null && item.Tile.Asset != null && item.Tile.Asset.TilePath == currentPath);
                textureSet.SelectedItem = selected ?? textureSet.Items[0];
                fields.Controls.Add(new Label { Text = "Texture pack", AutoSize = true }, 0, 0); fields.Controls.Add(textureSet, 1, 0);
                fields.Controls.Add(new Label { Text = "Copies", AutoSize = true }, 0, 1);
                fields.Controls.Add(new Label { Text = "Dominant floor, ceiling, and wall materials from the selected dungeon", AutoSize = true }, 1, 1);
                fields.Controls.Add(new Label { Text = "Preserved", AutoSize = true }, 0, 2);
                fields.Controls.Add(new Label { Text = "Vines, foliage, water, lava, doors, chests, torches, ice, props, actors, and collision", AutoSize = true }, 1, 2);
                fields.Controls.Add(new Label { Text = "Persistence", AutoSize = true }, 0, 3);
                fields.Controls.Add(new Label { Text = "Saved with this dungeon and restored when the project reopens", AutoSize = true }, 1, 3);
                FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
                Button apply = new Button { Text = "Apply texture pack", AutoSize = true };
                apply.Click += delegate
                {
                    try
                    {
                        TextureThemeChoice choice = textureSet.SelectedItem as TextureThemeChoice;
                        ApplySandboxTexturePack(choice == null ? null : choice.Tile);
                        dialog.DialogResult = DialogResult.OK;
                    }
                    catch (Exception error) { MessageBox.Show(dialog, error.Message, "Texture pack", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                };
                actions.Controls.Add(cancel); actions.Controls.Add(apply); fields.Controls.Add(actions, 0, 4); fields.SetColumnSpan(actions, 2);
                dialog.Controls.Add(fields); dialog.AcceptButton = apply; dialog.CancelButton = cancel; dialog.ShowDialog(this);
            }
        }

        private string ReadSandboxTextureThemePath()
        {
            if (CurrentScene == null || string.IsNullOrEmpty(CurrentScene.SandboxGeometryFile) || !File.Exists(CurrentScene.SandboxGeometryFile)) return null;
            try { return TileStorage.Load<TileSceneGeometry>(CurrentScene.SandboxGeometryFile).TextureThemeTilePath; }
            catch (Exception error) { ArcharinaWorkspace.LogError(error); return null; }
        }

        private void ApplySandboxTexturePack(RandomDungeonTile textureTheme)
        {
            if (CurrentScene == null) throw new InvalidOperationException("Open a destination scene first.");
            TileSceneGeometry geometry;
            if (string.IsNullOrEmpty(CurrentScene.SandboxGeometryFile) || !File.Exists(CurrentScene.SandboxGeometryFile))
                geometry = TileSceneGeometry.Capture(CurrentScene);
            else geometry = TileStorage.Load<TileSceneGeometry>(CurrentScene.SandboxGeometryFile);
            geometry.TextureThemeTilePath = textureTheme == null || textureTheme.Asset == null ? null : textureTheme.Asset.TilePath;
            geometry.RuntimeTextureTheme = textureTheme == null ? null : textureTheme.Tile;
            geometry.RebuildPlacements();
            string saved = TileStorage.Save(geometry, false, geometry.Rooms.Concat(new[] { geometry.Collision }));
            RestoreSandboxGeometry(saved);
            CurrentScene.SandboxGeometryFile = saved;
            TrackSandboxGeometryEdit(geometry, "Texture pack edit");
            NativeGameplayMaterializer.Apply(CurrentScene);
            savechanges = true;
            RefreshSandboxRooms(); RefreshSandboxContents(); ApplySandboxPreview();
            sandboxStatus.Text = textureTheme == null || textureTheme.Asset == null
                ? "Texture pack cleared; source materials restored."
                : "Texture pack applied from " + textureTheme.Asset.Name + ". Floors, ceilings, and walls updated; special materials preserved.";
        }

        private int GenerateProceduralTileKit(int count, int seed)
        {
            if (sandboxCatalog == null || CurrentScene == null) throw new InvalidOperationException("Open a ROM and destination scene first.");
            List<SectionTile> tiles = ProceduralTileKitGenerator.Generate(count, seed, CurrentScene.SceneNumber, sandboxCatalog.Rooms.RomFingerprint);
            foreach (SectionTile tile in tiles) TileStorage.Save(tile, true, new[] { tile.Visual, tile.Collision });
            LoadSandboxTiles();
            TreeNode tilesNode = sandboxCategories.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Text == "Tiles");
            if (tilesNode != null) sandboxCategories.SelectedNode = tilesNode;
            RefreshSandboxPalette();
            return tiles.Count;
        }

        private void GenerateRandomDungeon(int roomCount, int seed, bool requireBoss, bool requireReward, bool requireItemArc, int minimumItemArcs, bool allowPartial, List<RandomDungeonTile> available, bool customOnly, bool overrideTextures, RandomDungeonTile textureTheme)
        {
            Random random = new Random(seed);
            List<string> inventory = ReadStartingItems();
            HashSet<string> collected = new HashSet<string>(inventory, StringComparer.OrdinalIgnoreCase);
            List<RandomDungeonTile> source = (customOnly ? available.Where(x => IsGeneratedCustomTile(x.Asset)) : available)
                .Where(x => RandomDungeonSockets(x).Count > 0).ToList();
            if (source.Count < 2) throw new InvalidOperationException("The generated custom tile kit needs at least two saved tiles before it can be used as the only randomizer source.");
            if (roomCount > source.Count) roomCount = source.Count;
            List<RandomDungeonTile> pool = source.OrderBy(x => random.Next()).ToList();
            RandomDungeonTile start = pool.Where(x => x.HasSpawn && xRequiresAvailableItems(x, collected))
                .OrderByDescending(x => RandomDungeonSockets(x).Count).FirstOrDefault() ??
                pool.Where(x => x.HasSpawn).OrderByDescending(x => RandomDungeonSockets(x).Count).FirstOrDefault() ??
                pool.OrderByDescending(x => RandomDungeonSockets(x).Count).FirstOrDefault();
            RandomDungeonTile boss = pool.Where(x => x.HasBoss && x != start)
                .OrderByDescending(x => RandomDungeonSockets(x).Count).FirstOrDefault();
            RandomDungeonTile reward = pool.Where(x => x.HasReward && x != start && x != boss)
                .OrderByDescending(x => RandomDungeonSockets(x).Count).FirstOrDefault();
            bool canAutoGenerateBoss = FindGeneratedActorAsset("Bosses", "Boss", "Phantom", "Iron Knuckle", "Dark Link") != null ||
                FindGeneratedActorAsset("Enemies", "Stalfos", "Moblin", "Lizalfos", "Deku Scrub", "Octorok") != null;
            bool canAutoGenerateReward = FindGeneratedActorAsset("Chests", "Treasure Chest", "Chest") != null;
            if (requireBoss && boss == null && !canAutoGenerateBoss && !allowPartial) throw new InvalidOperationException("No extracted tile or compatible ROM boss/enemy asset satisfies the finale requirement.");
            if (requireReward && reward == null && !canAutoGenerateReward && !allowPartial) throw new InvalidOperationException("No extracted tile or compatible ROM chest asset satisfies the reward requirement.");
            // A one-door reward tile can only be terminal. If a boss is also
            // required, reserve the terminal slot for the boss and let the
            // gameplay materializer add the reward actor to a connected room.
            if (reward != null && boss != null && RandomDungeonSockets(reward).Count < 2)
            {
                if (!canAutoGenerateReward && requireReward && !allowPartial)
                    throw new InvalidOperationException("The selected reward tile has only one real doorway and cannot precede the required finale room.");
                reward = null;
            }
            List<RandomDungeonTile> chosen = new List<RandomDungeonTile> { start };
            AddProgressionRewards(collected, start);
            int reservedSpecialRooms = (boss == null ? 0 : 1) + (reward == null ? 0 : 1);
            int interiorRoomTarget = Math.Max(1, roomCount - reservedSpecialRooms);
            foreach (RandomDungeonTile candidate in pool.Where(x => !chosen.Contains(x) && x != boss && x != reward && RandomDungeonSockets(x).Count >= 2)
                .OrderByDescending(x => DungeonTileScore(x, collected) + random.NextDouble()))
            {
                if (chosen.Count >= interiorRoomTarget) break;
                if (!xRequiresAvailableItems(candidate, collected)) continue;
                chosen.Add(candidate);
                AddProgressionRewards(collected, candidate);
            }
            if (reward != null && chosen.Count < roomCount && !chosen.Contains(reward) && (RandomDungeonSockets(reward).Count >= 2 || chosen.Count == roomCount - 1)) { chosen.Add(reward); AddProgressionRewards(collected, reward); }
            if (boss != null && chosen.Count < roomCount && !chosen.Contains(boss)) chosen.Add(boss);
            foreach (RandomDungeonTile candidate in pool.Where(x => !chosen.Contains(x) && (chosen.Count >= roomCount - 1 || RandomDungeonSockets(x).Count >= 2)).OrderByDescending(x => DungeonTileScore(x, collected)))
            {
                if (chosen.Count >= roomCount) break;
                chosen.Add(candidate);
            }
            if (chosen.Count < roomCount && !allowPartial) throw new InvalidOperationException("Only " + chosen.Count + " compatible room tiles were available for the requested " + roomCount + " rooms.");
            BuildRandomDungeonScene(chosen, seed, requireBoss && boss == null && !canAutoGenerateBoss, requireReward && reward == null && !canAutoGenerateReward, requireItemArc, Math.Max(1, minimumItemArcs), allowPartial, overrideTextures, textureTheme);
        }

        private static bool HasRandomDungeonSocketCapacity(IEnumerable<RandomDungeonTile> tiles)
        {
            List<RandomDungeonTile> source = (tiles ?? Enumerable.Empty<RandomDungeonTile>()).Where(tile => RandomDungeonSockets(tile).Count > 0).ToList();
            // A connected route with a start and finale needs a reusable
            // doorway in every interior room. Reserve enough degree before
            // opening the randomizer instead of discovering the shortage at
            // recursion depth three.
            return source.Count >= 3 && source.Count(tile => RandomDungeonSockets(tile).Count >= 2) >= Math.Min(6, source.Count - 2);
        }

        private static bool xRequiresAvailableItems(RandomDungeonTile tile, ISet<string> inventory)
        {
            return tile == null || (tile.RequiredItems ?? new List<string>()).All(item => inventory.Contains(item));
        }

        private static bool IsGeneratedCustomTile(RomPaletteAsset asset)
        {
            return asset != null && asset.Category == "Tiles" && !string.IsNullOrEmpty(asset.TilePath) &&
                (asset.Name ?? "").StartsWith("Generated Kit ", StringComparison.OrdinalIgnoreCase);
        }

        private static double DungeonTileScore(RandomDungeonTile tile, ISet<string> inventory)
        {
            if (tile == null) return double.MinValue;
            int missing = (tile.RequiredItems ?? new List<string>()).Count(item => !inventory.Contains(item));
            int sockets = tile.Tile == null || tile.Tile.Sockets == null ? 0 : tile.Tile.Sockets.Count;
            return (missing == 0 ? 1000 : -missing * 1000) + sockets * 10 + tile.LogicLinkCount * 35 + tile.GrantedItems.Count * 12 +
                tile.NpcCount * 8 + tile.EnemyCount * 10 + tile.PuzzleCount * 24 + tile.ObstacleCount * 12 + tile.ItemCount * 10 + tile.FlagCount * 4 + tile.RequiredObjectCount;
        }

        private static bool SandboxActorLooksLikeNpc(RoomPackageActor actor)
        {
            return actor != null && (SandboxTextContains(actor.Role, "npc") || SandboxTextContains(actor.Role, "shop") ||
                SandboxTextContains(actor.Role, "person") || SandboxTextContains(actor.Name, "npc") || SandboxTextContains(actor.Name, "shopkeeper"));
        }

        private static bool SandboxActorLooksLikePuzzle(RoomPackageActor actor)
        {
            return actor != null && (SandboxTextContains(actor.Role, "switch") || SandboxTextContains(actor.Role, "puzzle") ||
                SandboxTextContains(actor.Name, "switch") || SandboxTextContains(actor.Name, "target") ||
                SandboxTextContains(actor.Name, "eye") || SandboxTextContains(actor.Name, "block"));
        }

        private static bool SandboxActorLooksLikeObstacle(RoomPackageActor actor)
        {
            return actor != null && (SandboxTextContains(actor.Role, "obstacle") || SandboxTextContains(actor.Role, "prop") ||
                SandboxTextContains(actor.Name, "rock") || SandboxTextContains(actor.Name, "block") || SandboxTextContains(actor.Name, "spike") ||
                SandboxTextContains(actor.Name, "flame") || SandboxTextContains(actor.Name, "fire") || SandboxTextContains(actor.Name, "ice"));
        }

        private static bool SandboxActorLooksLikeItem(RoomPackageActor actor)
        {
            return actor != null && ((actor.GrantedItems != null && actor.GrantedItems.Count > 0) ||
                SandboxTextContains(actor.Role, "item") || SandboxTextContains(actor.Role, "chest") || SandboxTextContains(actor.Name, "chest") ||
                SandboxTextContains(actor.Name, "key") || SandboxTextContains(actor.Name, "heart"));
        }

        private static void AddProgressionRewards(ISet<string> inventory, RandomDungeonTile tile)
        {
            if (inventory == null || tile == null) return;
            foreach (string item in tile.GrantedItems ?? new List<string>()) inventory.Add(item);
        }

        private void BuildRandomDungeonScene(List<RandomDungeonTile> chosen, int seed, bool missingBoss, bool missingReward, bool requireItemArc, int minimumItemArcs, bool allowPartial, bool overrideTextures, RandomDungeonTile textureTheme)
        {
            if (chosen == null || chosen.Count < 2) throw new InvalidOperationException("A playable dungeon needs at least two rooms.");
            CreateEmptyDungeonWorkspace(sandboxRandomDungeonSearchActive);
            while (CurrentScene.Rooms.Count < chosen.Count)
            {
                ZScene.ZRoom room = CreateEmptyDungeonRoom();
                room.ModelShortFilename = "Random Dungeon Room " + CurrentScene.Rooms.Count.ToString("D2");
                CurrentScene.Rooms.Add(room);
            }
            CurrentScene.Transitions.Clear(); CurrentScene.SpawnPoints.Clear(); CurrentScene.SandboxGameplayPackages = new List<RoomPackage>();
            manualRoomListItems = true;
            if (!sandboxRandomDungeonSearchActive)
            {
                RefreshRoomListItems(); SelectRoom(0); RefreshSandboxRooms();
            }
            RandomDungeonLayout layout = BuildRandomDungeonLayout(chosen, seed);
            TileSceneGeometry geometry = TileSceneGeometry.Capture(CurrentScene);
            geometry.TextureThemeTilePath = !overrideTextures || textureTheme == null || textureTheme.Asset == null ? null : textureTheme.Asset.TilePath;
            geometry.RuntimeTextureTheme = !overrideTextures || textureTheme == null ? null : textureTheme.Tile;
            if (geometry.RuntimeTextureTheme != null)
            {
                string[] theme = TileSceneGeometry.TextureThemeMaterialNames(geometry.RuntimeTextureTheme);
                layout.ThemeFloor = theme[0]; layout.ThemeCeiling = theme[1]; layout.ThemeWall = theme[2];
            }
            for (int i = 0; i < layout.Placements.Count; i++)
            {
                RandomDungeonPlacement placement = layout.Placements[i];
                geometry.Place(placement.Tile.Tile, placement.Room, placement.Position, placement.Turns, 0, null, placement.Tile.Asset.TilePath);
            }
            string path = TileStorage.Save(geometry, false, geometry.Rooms.Concat(new[] { geometry.Collision }));
            CurrentScene.SandboxGeometryFile = path;
            RestoreSandboxGeometry(path);
            TrackSandboxGeometryEdit(geometry, "Generated dungeon layout");
            int corridors = AddRandomizedCorridorGeometry(layout);
            if (corridors < layout.Connections.Count)
                throw new InvalidOperationException("The generated dungeon could not build every room connector. No partial disconnected dungeon was accepted.");
            RemoveRandomizedSourceTransitions();
            AddRandomizedRoomConnections(layout);
            ConnectRandomizedDoorSockets(layout);
            GeneratedDungeonGameplaySummary gameplay = PopulateGeneratedDungeonGameplay(layout, seed, missingBoss, missingReward, minimumItemArcs);
            if (requireItemArc && chosen.Count > 2 && gameplay.ItemArcCount < minimumItemArcs && !allowPartial)
                throw new InvalidOperationException("The generated dungeon has only " + gameplay.ItemArcCount + " item acquisition arc(s), but " + minimumItemArcs + " were requested. Choose a ROM with more compatible progression-item actors or allow a partial result.");
            if (requireItemArc && gameplay.ItemArcCount == 0 && !allowPartial)
                throw new InvalidOperationException("The generated dungeon has no mid-dungeon item acquisition arc. Choose a ROM with a compatible item or chest reward actor, or allow a partial result.");
            if (!allowPartial && !gameplay.IsPlayable)
                throw new InvalidOperationException("The generated dungeon could not be made playable from the imported ROM actor library.\n\n" + string.Join("\n", gameplay.Issues.Distinct().ToArray()));
            NormalizeGeneratedSpawns();
            if (CurrentScene.SpawnPoints.Count == 0) CurrentScene.SpawnPoints.Add(new ZActor(0x0000, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0x0FFF) { SpawnRoom = 0 });
            List<string> gameplayIssues = NativeGameplayMaterializer.Apply(CurrentScene);
            gameplayIssues.AddRange(NativeLogicLinkMaterializer.Apply(CurrentScene, sandboxCatalog));
            GeneratedDungeonPlayabilityReport certificate = GeneratedDungeonPlayability.Check(CurrentScene, ReadStartingItems());
            sandboxLastProgression = certificate.Progression;
            if (!allowPartial && !certificate.Solvable)
                throw new InvalidOperationException("The generated dungeon failed native playability certification.\n\n" + string.Join("\n", certificate.Errors.Distinct().ToArray()));
            if (!sandboxRandomDungeonSearchActive)
            {
                PersistSandboxGeometry();
                savechanges = true; RefreshSandboxRooms(); ApplySandboxPreview(); FocusSandboxRoom();
            }
            List<string> issues = SceneBuilderValidation.Check(CurrentScene);
            issues.AddRange(gameplayIssues);
            issues.AddRange(certificate.Errors.Select(error => "PLAYABILITY ERROR: " + error));
            issues.AddRange(certificate.Warnings.Select(warningText => "PLAYABILITY WARNING: " + warningText));
            issues.AddRange(RoomPackageReview.CheckScene(CurrentScene));
            issues = issues.Distinct().ToList();
            sandboxStatus.Text = string.Format("Generated playable dungeon | seed {0} | {1} rooms | {2} actors | {3} logic link{4} | {5} validation item{6} | {7}", seed, chosen.Count,
                gameplay.SpawnCount + gameplay.EnemyCount + gameplay.BossCount + gameplay.NpcCount + gameplay.PuzzleCount + gameplay.RewardCount + gameplay.ObstacleCount,
                gameplay.LinkCount, gameplay.LinkCount == 1 ? "" : "s", issues.Count, issues.Count == 1 ? "" : "s", certificate.Summary + "; " + certificate.Progression.Summary);
            string warning = missingBoss || missingReward ? "\n\nWarnings:\n" + (missingBoss ? "No boss/finale tile was available.\n" : "") + (missingReward ? "No reward tile was available.\n" : "") : "";
            MessageBox.Show(this, "Generated a native multi-room dungeon from the selected tile library.\n\nSeed: " + seed + "\nRooms: " + chosen.Count + "\nNative connections: " + CurrentScene.Transitions.Count + "\nCorridor sections: " + corridors + "\nGameplay actors: " + (gameplay.SpawnCount + gameplay.EnemyCount + gameplay.BossCount + gameplay.NpcCount + gameplay.PuzzleCount + gameplay.RewardCount + gameplay.ObstacleCount) + "\nItem acquisitions: " + gameplay.ItemArcCount + "\nItem arc item: " + (gameplay.ItemArcItem ?? "none detected") + "\nLogic links: " + gameplay.LinkCount + "\nPlayability certificate: " + certificate.Summary + "\nProgression certificate: " + certificate.Progression.Summary + "\nFallback placements: " + layout.FallbackPlacements + "\nValidation items: " + issues.Count + warning + "\n\nThe generator added a player start, combat encounters, puzzle/reward actors, native flags, and supported same-room logic links. Review the mid-dungeon item acquisition, fallback placements, and validation warnings before exporting or play-testing an injected ROM copy.", "Dungeon randomizer", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowGeneratedItemArcDialog()
        {
            if (CurrentScene == null) throw new InvalidOperationException("Open or generate a dungeon workspace first.");
            GeneratedDungeonProgressionReport report = GeneratedDungeonProgression.Check(CurrentScene, ReadStartingItems());
            sandboxLastProgression = report;
            DungeonItemArcReport arc = report.ItemArc ?? new DungeonItemArcReport();
            using (Form dialog = new Form { Text = "Dungeon item acquisition arc", ClientSize = new Size(760, 560), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = true })
            {
                TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 4 };
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                Label summary = new Label { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
                    Text = (report.Solvable ? "Certified" : "Blocked") + " | " + arc.Summary + " | " + report.Summary };
                TreeView tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, FullRowSelect = true, BorderStyle = BorderStyle.FixedSingle };
                TreeNode root = tree.Nodes.Add("Progression items");
                foreach (DungeonItemArcStep step in arc.Steps.Where(item => item.ConsumerRooms.Count > 0 || !item.StartingItem).OrderBy(item => item.AcquisitionRoom < 0 ? int.MaxValue : item.AcquisitionRoom).ThenBy(item => item.Item, StringComparer.OrdinalIgnoreCase))
                {
                    string state = step.StartingItem ? "starting inventory" : step.AcquisitionRoom < 0 ? "missing acquisition" : "acquired in room " + step.AcquisitionRoom;
                    TreeNode itemNode = root.Nodes.Add(step.Item + " - " + state);
                    itemNode.Tag = new ItemArcNavigation { Step = step, Room = step.AcquisitionRoom, Acquisition = true };
                    itemNode.Nodes.Add(step.StartingItem ? "Starts with this item" : step.AcquisitionRoom < 0 ? "No acquisition room was found" : "Acquisition room: " + step.AcquisitionRoom);
                    if (step.FirstConsumerRoom >= 0) itemNode.Nodes.Add("First consumer room: " + step.FirstConsumerRoom);
                    TreeNode consumers = itemNode.Nodes.Add("Consumed by rooms: " + (step.ConsumerRooms.Count == 0 ? "none" : string.Join(", ", step.ConsumerRooms.ToArray())));
                    foreach (int room in step.ConsumerRooms)
                    {
                        TreeNode consumer = consumers.Nodes.Add(DescribeItemArcConsumer(step, room));
                        consumer.Tag = new ItemArcNavigation { Step = step, Room = room, Acquisition = false };
                    }
                    if (step.RequiredBefore.Count > 0) itemNode.Nodes.Add("Prerequisites before acquisition: " + string.Join(", ", step.RequiredBefore.ToArray()));
                    itemNode.Nodes.Add("Evidence: " + (step.Evidence ?? "unknown"));
                }
                if (root.Nodes.Count == 0) root.Nodes.Add("No item consumers were found in the current native scene.");
                root.Expand();
                tree.NodeMouseClick += delegate(object sender, TreeNodeMouseClickEventArgs e)
                {
                    if (e.Button != MouseButtons.Right) return;
                    ItemArcNavigation navigation = e.Node == null ? null : e.Node.Tag as ItemArcNavigation;
                    if (navigation == null) return;
                    e.Node.TreeView.SelectedNode = e.Node;
                    ContextMenuStrip menu = new ContextMenuStrip();
                    if (navigation.Acquisition && navigation.Room >= 0)
                        menu.Items.Add("Focus acquisition room " + navigation.Room, null, delegate { FocusItemArcTarget(navigation); });
                    else if (!navigation.Acquisition)
                        menu.Items.Add("Focus consumer room " + navigation.Room, null, delegate { FocusItemArcTarget(navigation); });
                    if (navigation.Acquisition && navigation.Room >= 0)
                        menu.Items.Add("Select acquisition actor", null, delegate { SelectItemArcActor(navigation); });
                    if (!navigation.Acquisition)
                        menu.Items.Add("Select consumer gate", null, delegate { SelectItemArcActor(navigation); });
                    if (menu.Items.Count > 0) menu.Show(tree, e.Location);
                };
                RichTextBox diagnostics = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, DetectUrls = false,
                    Text = BuildItemArcDiagnostics(report, arc), ScrollBars = RichTextBoxScrollBars.Vertical };
                FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                Button close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
                actions.Controls.Add(close);
                layout.Controls.Add(summary, 0, 0); layout.Controls.Add(tree, 0, 1); layout.Controls.Add(diagnostics, 0, 2); layout.Controls.Add(actions, 0, 3);
                dialog.Controls.Add(layout); dialog.CancelButton = close; dialog.ShowDialog(this);
            }
        }

        private static string BuildItemArcDiagnostics(GeneratedDungeonProgressionReport report, DungeonItemArcReport arc)
        {
            List<string> lines = new List<string> { "ITEM ARC DIAGNOSTICS", "", "The report describes authoring requirements for the native scene currently open.", "Runtime pickup behavior still needs ROM playtesting.", "" };
            foreach (string error in arc.Errors ?? new List<string>()) lines.Add("ERROR: " + error);
            foreach (string warning in arc.Warnings ?? new List<string>()) lines.Add("WARNING: " + warning);
            foreach (string error in report.Errors ?? new List<string>()) if (!lines.Contains("PROGRESSION: " + error)) lines.Add("PROGRESSION: " + error);
            foreach (string warning in report.Warnings ?? new List<string>()) if (!lines.Contains("PROGRESSION: " + warning)) lines.Add("PROGRESSION: " + warning);
            if (lines.Count == 5) lines.Add("No warnings or errors were reported.");
            return string.Join(Environment.NewLine, lines.ToArray());
        }

        private void FocusItemArcTarget(ItemArcNavigation navigation)
        {
            if (navigation == null || navigation.Room < 0 || CurrentScene == null || navigation.Room >= CurrentScene.Rooms.Count) return;
            SelectRoom(navigation.Room);
            FocusSandboxRoom();
            sandboxStatus.Text = (navigation.Acquisition ? "Focused acquisition room " : "Focused consumer room ") + navigation.Room + " for " + navigation.Step.Item + ".";
        }

        private void SelectItemArcActor(ItemArcNavigation navigation)
        {
            if (navigation == null || navigation.Room < 0 || CurrentScene == null || navigation.Room >= CurrentScene.Rooms.Count) return;
            SelectRoom(navigation.Room);
            RoomPackage package = (CurrentScene.SandboxGameplayPackages ?? new List<RoomPackage>()).FirstOrDefault(candidate => candidate != null && !candidate.LogicOnly &&
                (candidate.DestinationRoom == navigation.Room || (candidate.DestinationRoom < 0 && candidate.Room == navigation.Room)));
            if (package == null)
            {
                FocusSandboxRoom();
                sandboxStatus.Text = "Focused room " + navigation.Room + "; no gameplay package actor is available for " + navigation.Step.Item + ".";
                return;
            }
            IEnumerable<RoomPackageActor> actors = (package.Actors ?? new List<RoomPackageActor>()).Concat(package.Spawns ?? new List<RoomPackageActor>());
            RoomPackageActor packageActor = navigation.Acquisition
                ? actors.FirstOrDefault(actor => (actor.GrantedItems ?? new List<string>()).Any(item => string.Equals(NormalizeArcItem(item), navigation.Step.Item, StringComparison.OrdinalIgnoreCase)))
                : actors.FirstOrDefault(actor => (actor.RequiredItems ?? new List<string>()).Any(item => string.Equals(NormalizeArcItem(item), navigation.Step.Item, StringComparison.OrdinalIgnoreCase)));
            ZActor native = packageActor == null ? null : CurrentScene.Rooms[navigation.Room].ZActors.FirstOrDefault(actor => actor.SandboxGameplayId == packageActor.Id);
            if (native == null && packageActor != null) native = CurrentScene.Transitions.FirstOrDefault(actor => actor.SandboxGameplayId == packageActor.Id);
            if (native == null && !navigation.Acquisition)
            {
                RoomPackageActor transitionPackageActor = actors.FirstOrDefault(actor => actor.IsTransition &&
                    (actor.RequiredItems ?? new List<string>()).Any(item => string.Equals(NormalizeArcItem(item), navigation.Step.Item, StringComparison.OrdinalIgnoreCase)));
                if (transitionPackageActor != null)
                {
                    packageActor = transitionPackageActor;
                    native = CurrentScene.Transitions.FirstOrDefault(actor => actor.SandboxGameplayId == transitionPackageActor.Id);
                }
            }
            if (native != null)
            {
                SelectSandboxActor(native);
                sandboxStatus.Text = "Selected " + (navigation.Acquisition ? "acquisition actor" : "consumer gate") + " for " + navigation.Step.Item + ".";
            }
            else
            {
                FocusSandboxRoom();
                sandboxStatus.Text = "Focused room " + navigation.Room + "; actor is not materialized in the native scene yet.";
            }
        }

        private static string NormalizeArcItem(string item)
        {
            string value = (item ?? "").Trim();
            return value.StartsWith("DUNGEON ", StringComparison.OrdinalIgnoreCase) ? value.Substring("DUNGEON ".Length).Trim() : value;
        }

        private string DescribeItemArcConsumer(DungeonItemArcStep step, int room)
        {
            if (CurrentScene == null) return "Room " + room + " requires " + step.Item;
            IEnumerable<RoomPackage> packages = (CurrentScene.SandboxGameplayPackages ?? new List<RoomPackage>()).Where(package => package != null && !package.LogicOnly &&
                (package.DestinationRoom == room || (package.DestinationRoom < 0 && package.Room == room)));
            RoomPackageActor transition = packages.SelectMany(package => (package.Actors ?? new List<RoomPackageActor>()).Concat(package.Spawns ?? new List<RoomPackageActor>()))
                .FirstOrDefault(actor => actor.IsTransition && (actor.RequiredItems ?? new List<string>()).Any(item => string.Equals(NormalizeArcItem(item), step.Item, StringComparison.OrdinalIgnoreCase)));
            if (transition != null)
            {
                ZActor native = (CurrentScene.Transitions ?? new List<ZActor>()).FirstOrDefault(actor => actor.SandboxGameplayId == transition.Id);
                if (native != null) return "Transition: Room " + native.FrontSwitchTo + " -> Room " + native.BackSwitchTo + " requires " + step.Item;
                return "Door transition in Room " + room + " requires " + step.Item;
            }
            return "Room " + room + " actor gate requires " + step.Item;
        }

        private GeneratedDungeonGameplaySummary PopulateGeneratedDungeonGameplay(RandomDungeonLayout layout, int seed, bool missingBoss, bool missingReward, int minimumItemArcs)
        {
            GeneratedDungeonGameplaySummary summary = new GeneratedDungeonGameplaySummary();
            if (layout == null || layout.Placements.Count == 0)
            {
                summary.Issues.Add("No generated room placements are available for gameplay population.");
                return summary;
            }

            RomPaletteAsset spawnAsset = FindGeneratedActorAsset("Spawns", "Player Spawn", "Link", "Spawn point");
            RomPaletteAsset enemyAsset = FindGeneratedActorAsset("Enemies", "Stalfos", "Moblin", "Lizalfos", "Deku Scrub", "Octorok");
            RomPaletteAsset bossAsset = FindGeneratedActorAsset("Bosses", "Boss", "Phantom", "Iron Knuckle", "Dark Link") ?? enemyAsset;
            RomPaletteAsset npcAsset = FindGeneratedActorAsset("NPCs", "Sheik", "King Zora", "Zelda", "Navi");
            RomPaletteAsset switchAsset = FindGeneratedActorAsset("Switches", "Dungeon Switch", "Floor Switch", "Crystal Switch") ??
                FindGeneratedActorAssetWithLogic("Switch");
            RomPaletteAsset chestAsset = FindGeneratedActorAsset("Chests", "Treasure Chest", "Chest");
            RomPaletteAsset itemAsset = FindGeneratedItemAsset("Hookshot", "Longshot", "Iron Boots", "Zora Tunic", "Bow", "Boomerang");
            RomPaletteAsset secondItemAsset = minimumItemArcs > 1 ? FindGeneratedItemAssetExcluding(itemAsset, "Hookshot", "Longshot", "Iron Boots", "Zora Tunic", "Hover Boots", "Bow", "Boomerang", "Bombchu", "Megaton Hammer", "Mirror Shield", "Lens of Truth", "Slingshot") : null;
            RomPaletteAsset itemAcquisitionAsset = itemAsset ?? chestAsset;
            string itemArcName = InferGeneratedProgressionItem(itemAsset) ?? "Hookshot";
            string secondItemName = InferGeneratedProgressionItem(secondItemAsset);
            bool canPlaceSecondItem = secondItemAsset != null && !string.IsNullOrWhiteSpace(secondItemName);
            summary.ItemArcItem = canPlaceSecondItem ? itemArcName + ", " + secondItemName : itemArcName;
            RomPaletteAsset obstacleAsset = FindGeneratedActorAsset("Props", "Bombable Wall", "Push", "Spike", "Stone Block", "Platform") ??
                FindGeneratedActorAsset("Scenery", "Bombable Wall", "Push", "Spike", "Stone Block", "Platform");

            if (spawnAsset == null) summary.Issues.Add("The ROM actor library has no compatible player spawn asset.");
            if (enemyAsset == null) summary.Issues.Add("The ROM actor library has no compatible enemy asset.");
            if (chestAsset == null && itemAsset == null) summary.Issues.Add("The ROM actor library has no compatible treasure chest or item asset.");
            if (missingBoss && bossAsset == null) summary.Issues.Add("A boss/finale was requested, but no compatible boss or enemy asset was found.");
            if (missingReward && chestAsset == null) summary.Issues.Add("A reward was requested, but no compatible chest asset was found.");

            Dictionary<int, RoomPackage> packages = new Dictionary<int, RoomPackage>();
            for (int room = 0; room < layout.Placements.Count; room++)
                packages[room] = EnsureGeneratedGameplayPackage(room, layout.Placements[room]);

            if (spawnAsset != null)
            {
                RoomPackage startPackage = packages[0];
                RandomDungeonPlacement startPlacement = layout.Placements[0];
                RoomPackageActor spawn = AddGeneratedGameplayActor(startPackage, spawnAsset, "Player Spawn", GeneratedDungeonWorldPosition(startPlacement, startPlacement.SpawnPosition), startPlacement.Turns, "spawn_" + seed);
                spawn.Rotation = startPlacement.SpawnRotation;
                startPackage.Actors.Remove(spawn);
                startPackage.Spawns.Add(spawn);
                summary.SpawnCount++;
            }

            if (npcAsset != null && layout.Placements.Count > 1)
            {
                RandomDungeonPlacement placement = layout.Placements[0];
                AddGeneratedGameplayActor(packages[0], npcAsset, "NPC", GeneratedDungeonWorldPosition(placement, new Vector3d(0, 0, 110)), placement.Turns, "npc_" + seed);
                summary.NpcCount++;
            }

            List<int> forwardRoute = (layout.Route ?? new List<int>()).Where(room => room != 0).ToList();
            int puzzleRoom = forwardRoute.Count > 0 ? forwardRoute[0] : 0;
            int secondArcRoom = forwardRoute.Count > 4 ? forwardRoute[ Math.Max(1, forwardRoute.Count / 2) ] : -1;
            bool puzzleAdded = false;
            bool itemArcAdded = false;
            bool secondItemArcAdded = false;
            for (int room = 1; room < layout.Placements.Count; room++)
            {
                RandomDungeonPlacement placement = layout.Placements[room];
                RoomPackage package = packages[room];
                bool finalRoom = room == layout.Placements.Count - 1;
                if (finalRoom)
                {
                    if (bossAsset != null)
                    {
                        AddGeneratedGameplayActor(package, bossAsset, "Boss / Finale", GeneratedDungeonWorldPosition(placement, new Vector3d(0, 0, 0)), placement.Turns, "boss_" + seed);
                        summary.BossCount++; summary.EnemyCount++;
                    }
                    if (chestAsset != null)
                    {
                        RoomPackageActor reward = AddGeneratedGameplayActor(package, chestAsset, "Boss Reward", GeneratedDungeonWorldPosition(placement, new Vector3d(0, 0, 120)), placement.Turns, "boss_reward_" + seed, "DUNGEON Boss Key");
                        summary.RewardCount++;
                    }
                    continue;
                }

                if (enemyAsset != null)
                {
                    int enemyCount = 1 + (room % 2);
                    for (int enemyIndex = 0; enemyIndex < enemyCount; enemyIndex++)
                    {
                        double offset = (enemyIndex - (enemyCount - 1) * 0.5) * 80;
                        AddGeneratedGameplayActor(package, enemyAsset, "Enemy", GeneratedDungeonWorldPosition(placement, new Vector3d(offset, 0, 0)), placement.Turns, "enemy_" + seed + "_" + room + "_" + enemyIndex);
                        summary.EnemyCount++;
                    }
                }

                bool makePuzzle = room == puzzleRoom || room % 3 == 1;
                if (makePuzzle && switchAsset != null && chestAsset != null)
                {
                    RoomPackageActor switchActor = AddGeneratedGameplayActor(package, switchAsset, "Puzzle Switch", GeneratedDungeonWorldPosition(placement, new Vector3d(-90, 0, -80)), placement.Turns, "switch_" + seed + "_" + room);
                    bool itemAcquisition = room == puzzleRoom && layout.Placements.Count > 2 && itemAcquisitionAsset != null;
                    RomPaletteAsset rewardAsset = itemAcquisition && itemAsset != null ? itemAsset : chestAsset;
                    string rewardName = itemAcquisition ? "DUNGEON " + itemArcName : "DUNGEON Small Key";
                    RoomPackageActor reward = AddGeneratedGameplayActor(package, rewardAsset, itemAcquisition ? "Mid-dungeon item" : "Puzzle Reward", GeneratedDungeonWorldPosition(placement, new Vector3d(90, 0, 80)), placement.Turns, "reward_" + seed + "_" + room, rewardName);
                    ConfigureGeneratedSetter(switchActor);
                    ConfigureGeneratedChecker(reward);
                    if (AddGeneratedLogicLink(package, switchActor, reward, LogicLinkKind.Spawns)) summary.LinkCount++;
                    summary.PuzzleCount++; summary.RewardCount++; if (itemAcquisition) { summary.ItemArcCount++; itemArcAdded = true; } puzzleAdded = true;
                }
                else if (chestAsset != null && room % 3 == 0)
                {
                    AddGeneratedGameplayActor(package, chestAsset, "Room Reward", GeneratedDungeonWorldPosition(placement, new Vector3d(0, 0, 90)), placement.Turns, "room_reward_" + seed + "_" + room, "DUNGEON Small Key");
                    summary.RewardCount++;
                }

                if (room == puzzleRoom && !itemArcAdded && itemAcquisitionAsset != null && layout.Placements.Count > 2)
                {
                    AddGeneratedGameplayActor(package, itemAcquisitionAsset, "Mid-dungeon item", GeneratedDungeonWorldPosition(placement, new Vector3d(90, 0, 80)), placement.Turns, "item_arc_" + seed, "DUNGEON " + itemArcName);
                    summary.RewardCount++; summary.ItemArcCount++; itemArcAdded = true;
                }

                if (room == secondArcRoom && canPlaceSecondItem && !secondItemArcAdded)
                {
                    AddGeneratedGameplayActor(package, secondItemAsset, "Second mid-dungeon item", GeneratedDungeonWorldPosition(placement, new Vector3d(-90, 0, 80)), placement.Turns, "item_arc_" + seed + "_second", "DUNGEON " + secondItemName);
                    summary.RewardCount++; summary.ItemArcCount++; secondItemArcAdded = true;
                }

                if (obstacleAsset != null && room % 2 == 0)
                {
                    RoomPackageActor obstacle = AddGeneratedGameplayActor(package, obstacleAsset, "Obstacle", GeneratedDungeonWorldPosition(placement, new Vector3d(0, 0, -110)), placement.Turns, "obstacle_" + seed + "_" + room);
                    if (room > puzzleRoom && obstacle != null)
                    {
                        string requiredItem = secondItemArcAdded && room > secondArcRoom ? secondItemName : itemArcName;
                        obstacle.RequiredItems.Add(requiredItem);
                        obstacle.RequirementText = "Requires " + requiredItem + "; placed after the mid-dungeon item acquisition";
                    }
                    summary.ObstacleCount++;
                }
            }

            if (switchAsset == null) summary.Issues.Add("The ROM actor library has no compatible switch or flag-setting puzzle actor.");
            if (!puzzleAdded && switchAsset != null && chestAsset != null)
                summary.Issues.Add("No puzzle/reward pair could be placed from the compatible switch and chest actors.");
            if (summary.RewardCount == 0) summary.Issues.Add("No playable reward or item acquisition was placed.");
            if (summary.EnemyCount == 0) summary.Issues.Add("No playable enemy encounter was placed.");
            return summary;
        }

        private RoomPackage EnsureGeneratedGameplayPackage(int room, RandomDungeonPlacement placement)
        {
            List<RoomPackage> packages = CurrentScene.SandboxGameplayPackages ?? new List<RoomPackage>();
            CurrentScene.SandboxGameplayPackages = packages;
            RoomPackage package = packages.FirstOrDefault(p => p != null && p.DestinationRoom == room) ??
                packages.FirstOrDefault(p => p != null && p.Room == room);
            if (package == null)
            {
                package = new RoomPackage { Scene = CurrentScene.SceneNumber, Room = room, DestinationRoom = room,
                    RomFingerprint = sandboxCatalog == null || sandboxCatalog.Rooms == null ? null : sandboxCatalog.Rooms.RomFingerprint,
                    Name = "Generated dungeon room " + room.ToString("D2") + " gameplay" };
                packages.Add(package);
            }
            package.Scene = CurrentScene.SceneNumber;
            package.DestinationRoom = room;
            if (string.IsNullOrWhiteSpace(package.Name)) package.Name = "Generated dungeon room " + room.ToString("D2") + " gameplay";
            return package;
        }

        private RomPaletteAsset FindGeneratedActorAsset(string category, params string[] preferredNames)
        {
            if (sandboxCatalog == null) return null;
            IEnumerable<RomPaletteAsset> candidates = sandboxCatalog.Assets.Where(asset => asset != null && !asset.Transition &&
                string.Equals(asset.Category, category, StringComparison.OrdinalIgnoreCase) && IsGeneratedActorCompatible(asset));
            foreach (string preferred in preferredNames ?? new string[0])
            {
                RomPaletteAsset match = candidates.FirstOrDefault(asset => (asset.Name ?? "").IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null) return match;
            }
            return candidates.FirstOrDefault();
        }

        private RomPaletteAsset FindGeneratedItemAsset(params string[] preferredNames)
        {
            if (sandboxCatalog == null) return null;
            return sandboxCatalog.Assets.FirstOrDefault(asset => asset != null && !asset.Transition &&
                string.Equals(asset.Category, "Items", StringComparison.OrdinalIgnoreCase) && IsGeneratedActorCompatible(asset) &&
                (preferredNames ?? new string[0]).Any(preferred => (asset.Name ?? "").IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private RomPaletteAsset FindGeneratedItemAssetExcluding(RomPaletteAsset excluded, params string[] preferredNames)
        {
            if (sandboxCatalog == null) return null;
            return sandboxCatalog.Assets.FirstOrDefault(asset => asset != null && !ReferenceEquals(asset, excluded) && !asset.Transition &&
                string.Equals(asset.Category, "Items", StringComparison.OrdinalIgnoreCase) && IsGeneratedActorCompatible(asset) &&
                (preferredNames ?? new string[0]).Any(preferred => (asset.Name ?? "").IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static string InferGeneratedProgressionItem(RomPaletteAsset asset)
        {
            if (asset == null) return null;
            string[] known = { "Longshot", "Hookshot", "Iron Boots", "Zora Tunic", "Hover Boots", "Bow", "Boomerang", "Bombchu", "Megaton Hammer", "Mirror Shield", "Lens of Truth", "Slingshot" };
            IEnumerable<string> dropdownNames = (asset.LogicProperties ?? new List<RomActorLogicProperty>())
                .Where(property => property != null && ((property.Name ?? "").IndexOf("content", StringComparison.OrdinalIgnoreCase) >= 0 || (property.Name ?? "").IndexOf("reward", StringComparison.OrdinalIgnoreCase) >= 0))
                .SelectMany(property => property.Dropdown ?? new List<RomActorDropdownValue>())
                .Select(value => value == null ? null : value.Name);
            foreach (string knownName in known)
            {
                if (dropdownNames.Any(name => (name ?? "").IndexOf(knownName, StringComparison.OrdinalIgnoreCase) >= 0)) return knownName;
            }
            string assetName = asset.Name ?? "";
            return known.FirstOrDefault(name => assetName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private RomPaletteAsset FindGeneratedActorAssetWithLogic(string kind)
        {
            if (sandboxCatalog == null) return null;
            return sandboxCatalog.Assets.FirstOrDefault(asset => asset != null && !asset.Transition && IsGeneratedActorCompatible(asset) &&
                (asset.LogicProperties ?? new List<RomActorLogicProperty>()).Any(property => string.Equals(property.Kind, kind, StringComparison.OrdinalIgnoreCase)));
        }

        private bool IsGeneratedActorCompatible(RomPaletteAsset asset)
        {
            return asset != null && (asset.Objects ?? new ushort[0]).All(objectId => objectId <= 3 || sandboxCatalog.AvailableObjects.Contains(objectId));
        }

        private RoomPackageActor AddGeneratedGameplayActor(RoomPackage package, RomPaletteAsset asset, string role, Vector3d position, int turns, string id, string rewardName = null)
        {
            if (package == null || asset == null) return null;
            Vector3 rotation = new Vector3(asset.XRotation, asset.YRotation + turns * 16384.0f, asset.ZRotation);
            ZActor native = new ZActor(asset.Id, (float)position.X, (float)position.Y, (float)position.Z, rotation.X, rotation.Y, rotation.Z, asset.Variable);
            if (!string.IsNullOrWhiteSpace(rewardName)) SetGeneratedReward(native, asset, rewardName);
            RoomPackageActor actor = new RoomPackageActor { Id = id, Role = role, Name = asset.Name, SourceIndex = package.Actors.Count + package.Spawns.Count,
                SourceRoom = package.DestinationRoom, Number = asset.Id, Variable = native.Variable, Position = position, Rotation = rotation,
                RequirementText = asset.RequirementText, RequiredObjects = (asset.Objects ?? new ushort[0]).Distinct().ToList() };
            RoomPackageBuilder.PopulateMetadata(actor, asset, native);
            actor.Variable = native.Variable;
            actor.Position = position;
            actor.Rotation = rotation;
            // Generated placements start as freely reachable content. The generator
            // adds explicit item gates below; catalog prose is descriptive metadata,
            // not a complete prerequisite list for every generated actor.
            actor.RequiredItems.Clear();
            actor.RequiredObjects = (asset.Objects ?? new ushort[0]).Distinct().ToList();
            if (!string.IsNullOrWhiteSpace(rewardName) && !actor.GrantedItems.Contains(rewardName, StringComparer.OrdinalIgnoreCase)) actor.GrantedItems.Add(rewardName);
            package.Actors.Add(actor);
            return actor;
        }

        private static void SetGeneratedReward(ZActor native, RomPaletteAsset asset, string rewardName)
        {
            RomActorLogicProperty content = (asset.LogicProperties ?? new List<RomActorLogicProperty>()).FirstOrDefault(property =>
                (property.Name ?? "").IndexOf("content", StringComparison.OrdinalIgnoreCase) >= 0 || (property.Name ?? "").IndexOf("reward", StringComparison.OrdinalIgnoreCase) >= 0);
            if (content == null) return;
            RomActorDropdownValue value = (content.Dropdown ?? new List<RomActorDropdownValue>()).FirstOrDefault(item => (item.Name ?? "").IndexOf(rewardName, StringComparison.OrdinalIgnoreCase) >= 0);
            if (value == null) return;
            ushort encoded = (ushort)((value.Value << content.Position) & content.Mask);
            if (string.Equals(content.Target, "Var", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(content.Target)) native.Variable = (ushort)((native.Variable & ~content.Mask) | encoded);
            else if (string.Equals(content.Target, "XRot", StringComparison.OrdinalIgnoreCase)) native.XRot = value.Value;
            else if (string.Equals(content.Target, "YRot", StringComparison.OrdinalIgnoreCase)) native.YRot = value.Value;
            else if (string.Equals(content.Target, "ZRot", StringComparison.OrdinalIgnoreCase)) native.ZRot = value.Value;
        }

        private static void ConfigureGeneratedSetter(RoomPackageActor actor)
        {
            if (actor == null) return;
            DungeonFlagReference flag = (actor.Flags ?? new List<DungeonFlagReference>()).FirstOrDefault(item => string.Equals(item.Kind, "Switch", StringComparison.OrdinalIgnoreCase));
            if (flag == null)
            {
                flag = new DungeonFlagReference { Kind = "Switch", Property = "Generated puzzle switch", Target = "Var", Mask = 0x003F, Position = 0, Evidence = "Generated dungeon puzzle" };
                actor.Flags.Add(flag);
            }
            flag.CanSet = true; flag.CanCheck = false; flag.Value = 0;
        }

        private static void ConfigureGeneratedChecker(RoomPackageActor actor)
        {
            if (actor == null) return;
            DungeonFlagReference flag = (actor.Flags ?? new List<DungeonFlagReference>()).FirstOrDefault(item => string.Equals(item.Kind, "Switch", StringComparison.OrdinalIgnoreCase));
            if (flag == null)
            {
                flag = new DungeonFlagReference { Kind = "Switch", Property = "Generated reward switch check", Target = "Var", Mask = 0x003F, Position = 0, Evidence = "Generated dungeon reward" };
                actor.Flags.Add(flag);
            }
            flag.CanSet = false; flag.CanCheck = true; flag.Value = 1;
        }

        private static bool AddGeneratedLogicLink(RoomPackage package, RoomPackageActor source, RoomPackageActor target, LogicLinkKind kind)
        {
            if (package == null || source == null || target == null) return false;
            bool compatible = (source.Flags ?? new List<DungeonFlagReference>()).Any(sourceFlag => sourceFlag.CanSet && sourceFlag.Mask != 0 &&
                (target.Flags ?? new List<DungeonFlagReference>()).Any(targetFlag => targetFlag.CanCheck && targetFlag.Mask != 0 && string.Equals(sourceFlag.Kind, targetFlag.Kind, StringComparison.OrdinalIgnoreCase)));
            if (!compatible) return false;
            if (package.Links.Any(link => link.SourceId == source.Id && link.TargetId == target.Id && link.Kind == kind)) return false;
            package.Links.Add(new LogicLink { SourceId = source.Id, TargetId = target.Id, Kind = kind, Parameter = "Generated dungeon progression" });
            return true;
        }

        private static Vector3d GeneratedDungeonWorldPosition(RandomDungeonPlacement placement, Vector3d local)
        {
            return placement == null ? local : TileAssembly.Rotate(local, placement.Turns) + placement.Position;
        }

        private void NormalizeGeneratedSpawns()
        {
            foreach (RoomPackage package in CurrentScene.SandboxGameplayPackages ?? new List<RoomPackage>())
            {
                if (package.LogicOnly) continue;
                if (package.DestinationRoom != 0) package.Spawns.Clear();
                else if (package.Spawns.Count > 1) package.Spawns = package.Spawns.Take(1).ToList();
            }
            CurrentScene.SpawnPoints.Clear();
        }

        private void ConnectRandomizedDoorSockets(RandomDungeonLayout layout)
        {
            if (layout == null || CurrentScene.TileSockets == null) return;
            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
            foreach (RandomDungeonConnection link in layout.Connections)
            {
                TileSocket source = CurrentScene.TileSockets.Where(s => s.Room == link.FromRoom && !used.Contains(s.Id))
                    .OrderBy(s => (s.Position - link.Start).Length).FirstOrDefault();
                TileSocket target = CurrentScene.TileSockets.Where(s => s.Room == link.ToRoom && !used.Contains(s.Id))
                    .OrderBy(s => (s.Position - link.End).Length).FirstOrDefault();
                if (source == null || target == null || (source.Position - link.Start).Length > 2.5 || (target.Position - link.End).Length > 2.5) continue;
                source.ConnectedTo = target.Id; target.ConnectedTo = source.Id; used.Add(source.Id); used.Add(target.Id);
            }
        }

        private void RemoveRandomizedSourceTransitions()
        {
            foreach (RoomPackage package in CurrentScene.SandboxGameplayPackages ?? new List<RoomPackage>())
            {
                HashSet<string> removed = new HashSet<string>(package.Actors.Where(a => a.IsTransition).Select(a => a.Id));
                package.Actors = package.Actors.Where(a => !a.IsTransition).ToList();
                package.Links = package.Links.Where(link => !removed.Contains(link.SourceId) && !removed.Contains(link.TargetId)).ToList();
            }
        }

        private void AddRandomizedRoomConnections(RandomDungeonLayout layout)
        {
            RomPaletteAsset preset = sandboxCatalog.Assets.FirstOrDefault(a => a.Transition && a.Objects.All(id => id <= 3 || sandboxCatalog.AvailableObjects.Contains(id)));
            if (preset == null) throw new InvalidOperationException("The ROM has no compatible native transition actor for the generated dungeon.");
            foreach (RandomDungeonConnection link in layout.Connections)
            {
                ZActor connection = RoomConnections.Prepare(CurrentScene, preset, link.FromRoom, link.ToRoom, link.Start,
                    TileAssembly.NativeDoorYaw(link.Facing), sandboxCatalog.AvailableObjects);
                RoomConnections.Add(CurrentScene, preset, connection);
            }
        }

        private int AddRandomizedCorridorGeometry(RandomDungeonLayout layout)
        {
            int created = 0;
            foreach (RandomDungeonConnection link in layout.Connections)
            {
                if (!link.NeedsCorridor) continue;
                if (link.Path == null || link.Path.Count < 2) continue;
                created += AppendRandomizedDoorCorridor(link.FromRoom, link.Path, link.Width, link.Height, layout);
            }
            for (int i = 0; i < CurrentScene.Rooms.Count; i++)
            {
                CurrentScene.Rooms[i].TrueGroups = CurrentScene.Rooms[i].ObjModel.Groups;
                EnsureGroupSettingsCapacity(CurrentScene.Rooms[i]);
                CurrentScene.Rooms[i].ObjModel.Prepare(true, CurrentScene.Rooms[i].TrueGroups);
            }
            CurrentScene.ColModel.Prepare(false, CurrentScene.ColModel.Groups);
            return created;
        }

        private int AppendRandomizedDoorCorridor(int roomIndex, IList<Vector3d> path, double width, double height, RandomDungeonLayout layout)
        {
            if (roomIndex < 0 || roomIndex >= CurrentScene.Rooms.Count) return 0;
            if (path == null || path.Count < 2) return 0;
            try
            {
                List<string> materials = new List<string>();
                if (layout != null && !string.IsNullOrEmpty(layout.ThemeFloor))
                    materials.AddRange(new[] { layout.ThemeFloor, layout.ThemeCeiling, layout.ThemeWall });
                materials = materials.Select(name => CurrentScene.Rooms[roomIndex].ObjModel.Materials.FirstOrDefault(m => m != null && (m.Name == name || m.Name.EndsWith(name, StringComparison.Ordinal)))?.Name ?? name).ToList();
                while (materials.Count < 3) materials.Add(materials.Count == 0 ? "Archarina64_Corridor" : materials[0]);
                double corridorWidth = width <= 0 ? 100 : width;
                double corridorHeight = height <= 0 ? 180 : height;
                string rampMaterial = FindRampMaterial(roomIndex, materials[0]);
                bool doorwayHeightDiffers = Math.Abs(path[0].Y - path[path.Count - 1].Y) >= 1;
                bool pathHasSlope = false;
                for (int i = 0; i + 1 < path.Count; i++)
                    if (Math.Abs(path[i + 1].Y - path[i].Y) >= 1) { pathHasSlope = true; break; }
                if (!doorwayHeightDiffers && pathHasSlope)
                    throw new InvalidOperationException("Equal-height doorways require a level corridor.");
                if (doorwayHeightDiffers && !pathHasSlope)
                    throw new InvalidOperationException("Different-height doorways require an explicit ramp segment.");
                bool hasRamp = false;
                int runStart = 0;
                for (int i = 0; i + 1 < path.Count; i++)
                {
                    bool slope = Math.Abs(path[i + 1].Y - path[i].Y) >= 1;
                    if (!slope) continue;
                    if (i > runStart)
                        RoomGeometryBuilder.AppendSquareTubePath(CurrentScene.Rooms[roomIndex].ObjModel, CurrentScene.ColModel,
                            path.Skip(runStart).Take(i - runStart + 1).ToList(), corridorWidth, corridorHeight, 100,
                            materials[0], materials[1], materials[2], 0);
                    RoomGeometryBuilder.AppendLowPolyRamp(CurrentScene.Rooms[roomIndex].ObjModel, CurrentScene.ColModel,
                        path[i], path[i + 1], corridorWidth, corridorHeight, rampMaterial, materials[1], materials[2], 0);
                    hasRamp = true;
                    runStart = i + 1;
                }
                if (!hasRamp)
                    RoomGeometryBuilder.AppendSquareTubePath(CurrentScene.Rooms[roomIndex].ObjModel, CurrentScene.ColModel, path,
                        corridorWidth, corridorHeight, 100, materials[0], materials[1], materials[2], 0);
                else if (runStart < path.Count - 1)
                    RoomGeometryBuilder.AppendSquareTubePath(CurrentScene.Rooms[roomIndex].ObjModel, CurrentScene.ColModel,
                        path.Skip(runStart).ToList(), corridorWidth, corridorHeight, 100, materials[0], materials[1], materials[2], 0);
                CurrentScene.Rooms[roomIndex].AuthoringPrimitivesApplied = true;
                return 1;
            }
            catch (Exception error)
            {
                ArcharinaWorkspace.LogError(error);
                return 0;
            }
        }

        private string FindRampMaterial(int roomIndex, string fallback)
        {
            if (CurrentScene == null || roomIndex < 0 || roomIndex >= CurrentScene.Rooms.Count) return fallback;
            ObjFile model = CurrentScene.Rooms[roomIndex].ObjModel;
            if (model == null || model.Materials == null) return fallback;
            ObjFile.Material ramp = model.Materials.FirstOrDefault(material => material != null && !string.IsNullOrWhiteSpace(material.Name) &&
                (material.Name.IndexOf("stair", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 material.Name.IndexOf("step", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 material.Name.IndexOf("ramp", StringComparison.OrdinalIgnoreCase) >= 0));
            return ramp == null || string.IsNullOrWhiteSpace(ramp.Name) ? fallback : ramp.Name;
        }

        private RandomDungeonLayout BuildRandomDungeonLayout(List<RandomDungeonTile> chosen, int seed)
        {
            RandomDungeonLayout layout = new RandomDungeonLayout();
            if (chosen == null || chosen.Count == 0) return layout;
            Random random = new Random(seed);
            Vector3d startSpawn = RandomDungeonSpawnPosition(chosen[0]);
            Vector3 startRotation = RandomDungeonSpawnRotation(chosen[0]);
            RandomDungeonPlacement start = new RandomDungeonPlacement { Tile = chosen[0], Room = 0, Position = -startSpawn, Turns = 0,
                SpawnPosition = startSpawn, SpawnRotation = startRotation };
            SetRandomDungeonBounds(start);
            layout.Placements.Add(start);
            if (!TryBuildRandomDungeonLayout(layout, chosen, 1, random))
            {
                layout.Placements.Clear();
                layout.Connections.Clear();
                throw new InvalidOperationException("No collision-free socket-first layout was found for the selected room set after backtracking. " +
                    (string.IsNullOrEmpty(randomDungeonPlacementDiagnostics) ? "The rooms need compatible doorway sizes, orientations, and free space around their real entry holes." : randomDungeonPlacementDiagnostics));
            }
            layout.Route.AddRange(TraceRandomDungeonRoute(layout, chosen.Count - 1));
            if (layout.Route.Count < chosen.Count || layout.Route[0] != 0 || layout.Route[layout.Route.Count - 1] != chosen.Count - 1)
                throw new InvalidOperationException("The generated dungeon has no complete start-to-finale traversal route.");
            return layout;
        }

        private static List<int> TraceRandomDungeonRoute(RandomDungeonLayout layout, int finaleRoom)
        {
            List<int> route = new List<int> { 0 };
            if (layout == null || layout.Placements.Count == 0) return route;
            Dictionary<int, List<int>> children = new Dictionary<int, List<int>>();
            for (int i = 0; i < layout.Placements.Count; i++) children[i] = new List<int>();
            foreach (RandomDungeonConnection connection in layout.Connections)
            {
                if (connection == null || !children.ContainsKey(connection.FromRoom) || !children.ContainsKey(connection.ToRoom)) continue;
                children[connection.FromRoom].Add(connection.ToRoom);
            }

            HashSet<int> finalBranch = new HashSet<int>();
            int cursor = finaleRoom;
            while (cursor != 0)
            {
                RandomDungeonConnection edge = layout.Connections.LastOrDefault(connection => connection != null && connection.ToRoom == cursor);
                if (edge == null) return new List<int>();
                finalBranch.Add(cursor);
                cursor = edge.FromRoom;
            }

            AppendRandomDungeonRoute(children, finalBranch, 0, route);
            return route;
        }

        private static void AppendRandomDungeonRoute(Dictionary<int, List<int>> children, HashSet<int> finalBranch, int room, List<int> route)
        {
            List<int> orderedChildren = (children.ContainsKey(room) ? children[room] : new List<int>())
                .OrderBy(child => finalBranch.Contains(child) ? 1 : 0).ThenBy(child => child).ToList();
            foreach (int child in orderedChildren)
            {
                route.Add(child);
                AppendRandomDungeonRoute(children, finalBranch, child, route);
                if (!finalBranch.Contains(child)) route.Add(room);
            }
        }

        private bool TryBuildRandomDungeonLayout(RandomDungeonLayout layout, List<RandomDungeonTile> chosen, int room, Random random)
        {
            if (room >= chosen.Count) return true;
            List<RandomDungeonPlacement> candidates = FindRandomDungeonPlacementCandidates(layout, chosen[room], room, random);
            foreach (RandomDungeonPlacement candidate in candidates)
            {
                if (candidate == null || candidate.Parent == null || string.IsNullOrEmpty(candidate.ParentSocketId)) continue;
                candidate.Parent.UsedSockets.Add(candidate.ParentSocketId);
                layout.Placements.Add(candidate);
                RandomDungeonConnection connection = CreateRandomDungeonConnection(layout, candidate.Parent, candidate, candidate.ParentSocketId);
                if (connection != null)
                {
                    layout.Connections.Add(connection);
                    if (TryBuildRandomDungeonLayout(layout, chosen, room + 1, random)) return true;
                    layout.Connections.RemoveAt(layout.Connections.Count - 1);
                }
                layout.Placements.RemoveAt(layout.Placements.Count - 1);
                candidate.Parent.UsedSockets.Remove(candidate.ParentSocketId);
            }
            return false;
        }

        private List<RandomDungeonPlacement> FindRandomDungeonPlacementCandidates(RandomDungeonLayout layout, RandomDungeonTile tile, int room, Random random)
        {
            int parentSocketPairs = 0, facingRejected = 0, overlapRejected = 0, pathRejected = 0, accepted = 0;
            string facingSample = null;
            List<RandomDungeonPlacement> parents = layout.Placements
                .OrderByDescending(p => RandomDungeonSockets(p.Tile).Count(s => !p.UsedSockets.Contains(s.Id)))
                .ThenByDescending(p => p.Room == 0)
                .ThenBy(p => p.Room == room - 1)
                .ThenBy(p => random.Next()).ToList();
            List<TileSocket> childSockets = RandomDungeonSockets(tile);
            List<Tuple<double, RandomDungeonPlacement>> scored = new List<Tuple<double, RandomDungeonPlacement>>();
            foreach (RandomDungeonPlacement parent in parents)
            {
                foreach (TileSocket parentSocket in RandomDungeonSockets(parent.Tile))
                {
                    if (parent.UsedSockets.Contains(parentSocket.Id)) continue;
                    foreach (TileSocket childSocket in childSockets)
                    {
                        for (int turns = 0; turns < 4; turns++)
                        {
                            parentSocketPairs++;
                            double expected = TileAssembly.Angle(RandomDungeonSocketFacing(parent.Tile, parentSocket) - parent.Turns * 90.0 + 180.0);
                            double actual = TileAssembly.Angle(RandomDungeonSocketFacing(tile, childSocket) - turns * 90.0);
                            if (facingSample == null)
                                facingSample = string.Format("parent={0:0.##}deg turn={1} expected={2:0.##}deg child={3:0.##}deg error={4:0.##}deg",
                                    RandomDungeonSocketFacing(parent.Tile, parentSocket), parent.Turns, expected, actual, Math.Abs(TileAssembly.Angle(actual - expected)));
                            double facingError = Math.Abs(TileAssembly.Angle(actual - expected));
                            if (facingError > 10.0) { facingRejected++; continue; }
                            Vector3d parentWorldSocket = TileAssembly.Rotate(parentSocket.Position, parent.Turns) + parent.Position;
                            Vector3d parentFacing = RandomDungeonFacingVector(expected - 180.0);
                            for (double gap = RandomDungeonDoorGap; gap <= RandomDungeonMaxDoorGap; gap += RandomDungeonDoorGapStep)
                            {
                                Vector3d targetWorldSocket = parentWorldSocket + parentFacing * gap;
                                Vector3d position = targetWorldSocket - TileAssembly.Rotate(childSocket.Position, turns);
                                RandomDungeonPlacement candidate = new RandomDungeonPlacement { Tile = tile, Room = room, Position = position, Turns = turns };
                                SetRandomDungeonBounds(candidate);
                                double overlap = RandomDungeonOverlap(candidate, layout.Placements);
                                if (overlap > 0.000001) { overlapRejected++; continue; }
                                List<Vector3d> corridorPath = BuildRandomDungeonCorridorPath(parentWorldSocket, targetWorldSocket, layout.Placements,
                                    Math.Max(80, Math.Min(parentSocket.Width, childSocket.Width)), parent);
                                if (corridorPath == null) { pathRejected++; continue; }
                                double sizeScore = Math.Abs(parentSocket.Width - childSocket.Width) + Math.Abs(parentSocket.Height - childSocket.Height);
                                int freeParentSockets = RandomDungeonSockets(parent.Tile).Count(s => !parent.UsedSockets.Contains(s.Id));
                                int childSocketCount = childSockets.Count;
                                double hubScore = parent.Room == 0 ? -75.0 : 0.0;
                                double branchScore = parent.Room == room - 1 && parent.Room != 0 ? 100.0 : 0.0;
                                double score = sizeScore + gap * 0.05 + facingError * 2 + corridorPath.Count * 25 + hubScore + branchScore - freeParentSockets * 18.0 - childSocketCount * 8.0;
                                candidate.UsedSockets.Add(childSocket.Id);
                                candidate.IncomingSocketId = childSocket.Id;
                                candidate.ParentSocketId = parentSocket.Id;
                                candidate.Parent = parent;
                                scored.Add(Tuple.Create(score + random.NextDouble() * 0.01, candidate));
                                accepted++;
                                break;
                            }
                        }
                    }
                }
            }
            randomDungeonPlacementDiagnostics = string.Format("Placement diagnostics for room {0}: {1} socket-turn pairs, {2} facing rejects, {3} room-overlap rejects, {4} corridor-path rejects, {5} accepted candidates; child sockets={6}; {7}.",
                room, parentSocketPairs, facingRejected, overlapRejected, pathRejected, accepted, childSockets.Count, facingSample ?? "no facing sample");
            return scored.OrderBy(item => item.Item1).Select(item => item.Item2).ToList();
        }

        private RandomDungeonPlacement CreateRandomDungeonFallbackPlacement(RandomDungeonLayout layout, RandomDungeonTile tile, int room)
        {
            // A room without a valid socket placement is not a valid generated
            // room. Returning a bounds-only fallback creates the disconnected,
            // through-the-room corridors this generator is designed to prevent.
            return null;
        }

        private static Vector3d RandomDungeonFacingVector(double facing)
        {
            return TileAssembly.FacingVector(facing);
        }

        private static double RandomDungeonSocketFacing(RandomDungeonTile tile, TileSocket socket)
        {
            if (tile == null || socket == null || tile.Asset == null || tile.Asset.Room == null || tile.Tile == null || tile.Tile.Visual == null ||
                tile.Tile.Visual.Vertices == null || tile.Tile.Visual.Vertices.Count == 0)
                return socket == null ? 0 : socket.Facing;
            Vector3d min = tile.Tile.Visual.Vertices[0].ToVector3d(), max = min;
            foreach (ObjFile.Vertex vertex in tile.Tile.Visual.Vertices)
            {
                Vector3d point = vertex.ToVector3d();
                min = new Vector3d(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
                max = new Vector3d(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
            }
            Vector3d center = new Vector3d((min.X + max.X) * 0.5, socket.Position.Y, (min.Z + max.Z) * 0.5);
            Vector3d outward = socket.Position - center;
            if (outward.X * outward.X + outward.Z * outward.Z < 1.0) return socket.Facing;
            // OoT room rotations are quarter turns. Use the dominant room
            // side rather than an arbitrary diagonal from an irregular room
            // bound, so a doorway can actually match a legal room rotation.
            if (Math.Abs(outward.X) >= Math.Abs(outward.Z)) return outward.X >= 0 ? 0 : 180;
            return outward.Z >= 0 ? 90 : -90;
        }

        private static Vector3d RandomDungeonSpawnPosition(RandomDungeonTile tile)
        {
            if (tile == null || tile.Tile == null || tile.Tile.Gameplay == null || tile.Tile.Gameplay.Spawns == null) return new Vector3d(0, 45, 0);
            RoomPackageActor spawn = tile.Tile.Gameplay.Spawns.FirstOrDefault();
            return spawn == null ? new Vector3d(0, 45, 0) : spawn.Position;
        }

        private static Vector3 RandomDungeonSpawnRotation(RandomDungeonTile tile)
        {
            if (tile == null || tile.Tile == null || tile.Tile.Gameplay == null || tile.Tile.Gameplay.Spawns == null) return Vector3.Zero;
            RoomPackageActor spawn = tile.Tile.Gameplay.Spawns.FirstOrDefault();
            return spawn == null ? Vector3.Zero : spawn.Rotation;
        }

        private RandomDungeonConnection CreateRandomDungeonConnection(RandomDungeonLayout layout, RandomDungeonPlacement from, RandomDungeonPlacement to, string sourceSocketId)
        {
            TileSocket source = RandomDungeonSockets(from.Tile).FirstOrDefault(s => s.Id == sourceSocketId);
            TileSocket target = RandomDungeonSockets(to.Tile).FirstOrDefault(s => s.Id == to.IncomingSocketId);
            Vector3d start = source == null ? new Vector3d(from.Max.X, (from.Min.Y + from.Max.Y) * 0.5, (from.Min.Z + from.Max.Z) * 0.5) : TileAssembly.Rotate(source.Position, from.Turns) + from.Position;
            Vector3d end = target == null ? new Vector3d(to.Min.X, (to.Min.Y + to.Max.Y) * 0.5, (to.Min.Z + to.Max.Z) * 0.5) : TileAssembly.Rotate(target.Position, to.Turns) + to.Position;
            double facing = source == null ? 0 : TileAssembly.Angle(RandomDungeonSocketFacing(from.Tile, source) - from.Turns * 90.0);
            double width = source == null || target == null ? 100 : Math.Max(80, Math.Min(source.Width, target.Width));
            double height = source == null || target == null ? 180 : Math.Max(120, Math.Min(source.Height, target.Height));
            List<Vector3d> path = BuildRandomDungeonCorridorPath(start, end, layout == null ? null : layout.Placements, width, from, to);
            bool needsRamp = Math.Abs(end.Y - start.Y) >= 1;
            if (!needsRamp)
                path = path.Select(point => new Vector3d(point.X, start.Y, point.Z)).ToList();
            return new RandomDungeonConnection { FromRoom = from.Room, ToRoom = to.Room, SourceSocketId = sourceSocketId, TargetSocketId = to.IncomingSocketId, Start = start, End = end, Path = path, Facing = facing,
                Width = width, Height = height, NeedsCorridor = true, NeedsRamp = needsRamp };
        }

        private static IEnumerable<int> RandomDungeonVerticalOffsets()
        {
            yield return 0;
        }

        private static List<Vector3d> BuildRandomDungeonCorridorPath(Vector3d start, Vector3d end, IEnumerable<RandomDungeonPlacement> placements, double width, params RandomDungeonPlacement[] ignored)
        {
            HashSet<RandomDungeonPlacement> ignoredSet = new HashSet<RandomDungeonPlacement>(ignored ?? new RandomDungeonPlacement[0]);
            List<RandomDungeonPlacement> obstacles = (placements ?? Enumerable.Empty<RandomDungeonPlacement>())
                .Where(placement => placement != null && !ignoredSet.Contains(placement) && !PointInsideBounds(start, placement) && !PointInsideBounds(end, placement)).ToList();
            List<List<Vector3d>> candidates = new List<List<Vector3d>>();
            candidates.Add(BuildAxisPath(start, end, false, 0));
            candidates.Add(BuildAxisPath(start, end, true, 0));

            double minX = Math.Min(start.X, end.X), maxX = Math.Max(start.X, end.X);
            double minZ = Math.Min(start.Z, end.Z), maxZ = Math.Max(start.Z, end.Z);
            double margin = Math.Max(100, width * 0.75);
            foreach (RandomDungeonPlacement obstacle in obstacles)
            {
                if (obstacle == null) continue;
                candidates.Add(BuildAxisPath(start, end, false, obstacle.Max.X + margin));
                candidates.Add(BuildAxisPath(start, end, false, obstacle.Min.X - margin));
                candidates.Add(BuildAxisPath(start, end, true, obstacle.Max.Z + margin));
                candidates.Add(BuildAxisPath(start, end, true, obstacle.Min.Z - margin));
            }
            if (obstacles.Count > 0)
            {
                candidates.Add(BuildAxisPath(start, end, false, obstacles.Max(obstacle => obstacle.Max.X) + margin));
                candidates.Add(BuildAxisPath(start, end, false, obstacles.Min(obstacle => obstacle.Min.X) - margin));
                candidates.Add(BuildAxisPath(start, end, true, obstacles.Max(obstacle => obstacle.Max.Z) + margin));
                candidates.Add(BuildAxisPath(start, end, true, obstacles.Min(obstacle => obstacle.Min.Z) - margin));
            }

            List<Vector3d> best = null;
            int bestHits = int.MaxValue;
            double bestLength = double.MaxValue;
            foreach (List<Vector3d> candidate in candidates)
            {
                int hits = 0;
                double length = 0;
                for (int i = 0; i + 1 < candidate.Count; i++)
                {
                    length += (candidate[i + 1] - candidate[i]).Length;
                    foreach (RandomDungeonPlacement obstacle in obstacles)
                    {
                        if (SegmentIntersectsExpandedBounds(candidate[i], candidate[i + 1], obstacle, margin)) hits++;
                    }
                }
                if (hits < bestHits || (hits == bestHits && length < bestLength)) { best = candidate; bestHits = hits; bestLength = length; }
            }
            return bestHits == 0 ? best : null;
        }

        private static List<Vector3d> BuildAxisPath(Vector3d start, Vector3d end, bool zFirst, double detour)
        {
            List<Vector3d> points = new List<Vector3d> { start };
            if (detour != 0)
            {
                points.Add(new Vector3d(zFirst ? start.X : detour, start.Y, zFirst ? detour : start.Z));
                points.Add(new Vector3d(zFirst ? end.X : detour, start.Y, zFirst ? detour : end.Z));
            }
            else if (zFirst)
            {
                points.Add(new Vector3d(start.X, start.Y, end.Z));
            }
            else
            {
                points.Add(new Vector3d(end.X, start.Y, start.Z));
            }
            points.Add(new Vector3d(end.X, start.Y, end.Z));
            if (Math.Abs(end.Y - start.Y) >= 100)
            {
                int rampEnd = -1;
                for (int i = points.Count - 1; i > 0; i--)
                {
                    double horizontalLength = new Vector3d(points[i].X - points[i - 1].X, 0, points[i].Z - points[i - 1].Z).Length;
                    if (horizontalLength >= 1) { rampEnd = i; break; }
                }
                if (rampEnd > 0)
                {
                    for (int i = rampEnd; i < points.Count; i++)
                        points[i] = new Vector3d(points[i].X, end.Y, points[i].Z);
                    points[points.Count - 1] = end;
                }
                else
                {
                    // The caller will reject this as a non-traversable pure
                    // vertical link instead of creating a fake climbing shaft.
                    points[points.Count - 1] = end;
                }
            }
            return SimplifyDungeonPath(points);
        }

        private static List<Vector3d> SimplifyDungeonPath(List<Vector3d> points)
        {
            List<Vector3d> result = new List<Vector3d>();
            foreach (Vector3d point in points)
            {
                if (result.Count == 0 || (point - result[result.Count - 1]).Length >= 1) result.Add(point);
            }
            return result;
        }

        private static bool SegmentIntersectsExpandedBounds(Vector3d start, Vector3d end, RandomDungeonPlacement placement, double margin)
        {
            if (placement == null) return false;
            double minX = placement.Min.X - margin, maxX = placement.Max.X + margin;
            double minY = placement.Min.Y - margin, maxY = placement.Max.Y + margin;
            double minZ = placement.Min.Z - margin, maxZ = placement.Max.Z + margin;
            if (Math.Abs(start.X - end.X) < 1 && Math.Abs(start.Z - end.Z) < 1)
                return start.X > minX && start.X < maxX && start.Y > minY && start.Y < maxY && start.Z > minZ && start.Z < maxZ;
            if (Math.Abs(start.X - end.X) < 1)
                return start.X > minX && start.X < maxX && Math.Max(minZ, Math.Min(start.Z, end.Z)) < Math.Min(maxZ, Math.Max(start.Z, end.Z)) && start.Y > minY && start.Y < maxY;
            if (Math.Abs(start.Z - end.Z) < 1)
                return start.Z > minZ && start.Z < maxZ && Math.Max(minX, Math.Min(start.X, end.X)) < Math.Min(maxX, Math.Max(start.X, end.X)) && start.Y > minY && start.Y < maxY;
            return false;
        }

        private static bool PointInsideBounds(Vector3d point, RandomDungeonPlacement placement)
        {
            return placement != null && point.X >= placement.Min.X && point.X <= placement.Max.X &&
                point.Y >= placement.Min.Y && point.Y <= placement.Max.Y && point.Z >= placement.Min.Z && point.Z <= placement.Max.Z;
        }

        private static List<TileSocket> RandomDungeonSockets(RandomDungeonTile tile)
        {
            if (tile == null || tile.Tile == null) return new List<TileSocket>();
            if (tile.Tile.Sockets != null && tile.Tile.Sockets.Count > 0) return tile.Tile.Sockets;
            List<TileSocket> derived = new List<TileSocket>();
            if (tile.Tile.Gameplay == null) return derived;
            foreach (RoomPackageActor door in tile.Tile.Gameplay.Actors.Where(a => a.IsTransition))
            {
                double facing = door.Rotation.Y * 360.0 / 65536.0;
                int room = tile.Tile.Gameplay.Room;
                bool isFront = door.FrontRoom == room;
                bool isBack = door.BackRoom == room;
                if (!isFront && !isBack) continue;
                facing = TileAssembly.RoomDoorFacing(facing, isFront);
                derived.Add(new TileSocket { Id = door.Id + "_room_" + room, Name = door.Name + " doorway", Position = door.Position, Facing = facing, Width = 100, Height = 180 });
            }
            return derived;
        }

        private void SetRandomDungeonBounds(RandomDungeonPlacement placement)
        {
            if (placement.Tile == null || placement.Tile.Tile == null)
            {
                placement.Min = placement.Position - new Vector3d(500, 500, 500); placement.Max = placement.Position + new Vector3d(500, 500, 500); return;
            }
            List<ObjFile.Vertex> vertices = new List<ObjFile.Vertex>();
            if (placement.Tile.Tile.Visual != null) vertices.AddRange(placement.Tile.Tile.Visual.Vertices);
            if (placement.Tile.Tile.Collision != null) vertices.AddRange(placement.Tile.Tile.Collision.Vertices);
            if (vertices.Count == 0)
            {
                placement.Min = placement.Position - new Vector3d(500, 500, 500); placement.Max = placement.Position + new Vector3d(500, 500, 500); return;
            }
            Vector3d first = TileAssembly.Rotate(vertices[0].ToVector3d(), placement.Turns) + placement.Position;
            placement.Min = first; placement.Max = first;
            foreach (ObjFile.Vertex vertex in vertices)
            {
                Vector3d point = TileAssembly.Rotate(vertex.ToVector3d(), placement.Turns) + placement.Position;
                placement.Min = new Vector3d(Math.Min(placement.Min.X, point.X), Math.Min(placement.Min.Y, point.Y), Math.Min(placement.Min.Z, point.Z));
                placement.Max = new Vector3d(Math.Max(placement.Max.X, point.X), Math.Max(placement.Max.Y, point.Y), Math.Max(placement.Max.Z, point.Z));
            }
        }

        private static double RandomDungeonOverlap(RandomDungeonPlacement candidate, IEnumerable<RandomDungeonPlacement> existing)
        {
            double overlap = 0;
            foreach (RandomDungeonPlacement other in existing)
            {
                double x = Math.Max(0, Math.Min(candidate.Max.X, other.Max.X) - Math.Max(candidate.Min.X, other.Min.X));
                double y = Math.Max(0, Math.Min(candidate.Max.Y, other.Max.Y) - Math.Max(candidate.Min.Y, other.Min.Y));
                double z = Math.Max(0, Math.Min(candidate.Max.Z, other.Max.Z) - Math.Max(candidate.Min.Z, other.Min.Z));
                double volume = x * y * z;
                double candidateVolume = Math.Max(1, (candidate.Max.X - candidate.Min.X) * (candidate.Max.Y - candidate.Min.Y) * (candidate.Max.Z - candidate.Min.Z));
                double otherVolume = Math.Max(1, (other.Max.X - other.Min.X) * (other.Max.Y - other.Min.Y) * (other.Max.Z - other.Min.Z));
                overlap = Math.Max(overlap, volume / Math.Min(candidateVolume, otherVolume));
            }
            return overlap;
        }

        private void AnalyzeAndPlanDungeon()
        {
            if (sandboxCatalog == null) throw new InvalidOperationException("Load the source ROM library first.");
            if (CurrentScene == null) throw new InvalidOperationException("Open a decoded ROM scene first.");
            DungeonAnalysisReport report = ProceduralDungeonAnalyzer.AnalyzeScene(CurrentScene, sandboxCatalog);
            DungeonGeneratorSettings settings = new DungeonGeneratorSettings
            {
                Seed = CurrentScene.SceneNumber * 1009 + report.Rooms.Count,
                RoomCount = Math.Max(1, Math.Min(8, report.Rooms.Count)),
                RequiredFeatures = DungeonRoomFeatures.Floor | DungeonRoomFeatures.Collision,
                GenerateCorridors = true,
                StartingItems = ReadStartingItems(),
                RomMechanics = sandboxRomMechanics
            };
            ProceduralDungeonPlan plan = null;
            string outputDirectory = Path.Combine(sandboxCatalog.DirectoryPath, "procedural");
            Directory.CreateDirectory(outputDirectory);
            string reportPath = Path.Combine(outputDirectory, string.Format("scene_{0:X2}_analysis.xml", CurrentScene.SceneNumber));
            string planPath = Path.Combine(outputDirectory, string.Format("scene_{0:X2}_generated_plan.xml", CurrentScene.SceneNumber));
            using (FileStream stream = File.Create(reportPath)) new XmlSerializer(typeof(DungeonAnalysisReport)).Serialize(stream, report);
            try
            {
                plan = ProceduralDungeonAnalyzer.Generate(report, settings);
                using (FileStream stream = File.Create(planPath)) new XmlSerializer(typeof(ProceduralDungeonPlan)).Serialize(stream, plan);
            }
            catch (InvalidOperationException error)
            {
                report.Diagnostics.Add("Generation was not possible: " + error.Message);
            }
            int features = report.Rooms.Count(r => r.Features != DungeonRoomFeatures.None);
            string summary = string.Format("Analyzed Scene {0:X2}: {1} rooms, {2} native connections, {3} reachable rooms, {4} rooms with detected features.\n\nAnalysis: {5}",
                CurrentScene.SceneNumber, report.Rooms.Count, report.Connections.Count, report.ReachableRooms.Count, features, reportPath);
            if (plan != null) summary += string.Format("\nGenerated plan: {0} rooms, {1} connections.\nPlan: {2}", plan.Rooms.Count, plan.Connections.Count, planPath);
            if (report.Diagnostics.Count > 0) summary += "\n\nDiagnostics:\n" + string.Join("\n", report.Diagnostics.Take(8).ToArray());
            sandboxStatus.Text = plan == null ? "Dungeon analysis saved; generation needs compatible rooms." : "Dungeon analysis and generation plan saved.";
            MessageBox.Show(this, summary, "Dungeon analysis", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private List<string> ReadStartingItems()
        {
            if (sandboxStartingItems == null || string.IsNullOrWhiteSpace(sandboxStartingItems.Text)) return new List<string>();
            return sandboxStartingItems.Text.Split(',').Select(item => item.Trim()).Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void AnalyzeRomAndPlanDungeon()
        {
            if (sandboxCatalog == null || sandboxRom == null) throw new InvalidOperationException("Load the source ROM library first.");
            List<ZScene> scenes = new List<ZScene>();
            List<string> diagnostics = new List<string>();
            int[] sceneIds = sandboxCatalog.Rooms.Rooms.Select(r => r.SceneId).Distinct().OrderBy(x => x).ToArray();
            foreach (int sceneId in sceneIds)
            {
                sandboxStatus.Text = "Analyzing ROM scene " + sceneId.ToString("X2") + "...";
                Application.DoEvents();
                try
                {
                    ZScene scene = LoadZsceneData(new List<byte>(sandboxRom), sceneId);
                    if (scene == null || scene.Rooms == null || scene.Rooms.Count == 0)
                        diagnostics.Add("Scene " + sceneId.ToString("X2") + ": no decoded rooms.");
                    else
                    {
                        scene.SceneNumber = sceneId;
                        scenes.Add(scene);
                    }
                }
                catch (Exception error)
                {
                    diagnostics.Add("Scene " + sceneId.ToString("X2") + ": " + error.Message);
                }
            }

            DungeonAnalysisReport report = ProceduralDungeonAnalyzer.AnalyzeScenes(scenes, sandboxCatalog);
            report.Diagnostics.InsertRange(0, diagnostics);
            report.DungeonPatterns = ProceduralDungeonAnalyzer.AnalyzeDungeonPatterns(scenes, sandboxCatalog);
            report.Diagnostics.AddRange(report.DungeonPatterns.Diagnostics);
            string outputDirectory = Path.Combine(sandboxCatalog.DirectoryPath, "procedural");
            Directory.CreateDirectory(outputDirectory);
            string reportPath = Path.Combine(outputDirectory, "rom_analysis.xml");
            string planPath = Path.Combine(outputDirectory, "rom_generated_plan.xml");
            string patternPath = Path.Combine(outputDirectory, "dungeon_patterns.xml");
            using (FileStream stream = File.Create(reportPath)) new XmlSerializer(typeof(DungeonAnalysisReport)).Serialize(stream, report);
            using (FileStream stream = File.Create(patternPath)) new XmlSerializer(typeof(DungeonPatternReport)).Serialize(stream, report.DungeonPatterns);

            ProceduralDungeonPlan plan = null;
            try
            {
                plan = ProceduralDungeonAnalyzer.Generate(report, new DungeonGeneratorSettings
                {
                    Seed = report.SceneCount * 1009 + report.Rooms.Count,
                    RoomCount = Math.Max(1, Math.Min(32, report.Rooms.Count)),
                    RequiredFeatures = DungeonRoomFeatures.Floor | DungeonRoomFeatures.Collision,
                    GenerateCorridors = true,
                    StartingItems = ReadStartingItems(),
                    RomMechanics = sandboxRomMechanics
                });
                using (FileStream stream = File.Create(planPath)) new XmlSerializer(typeof(ProceduralDungeonPlan)).Serialize(stream, plan);
            }
            catch (InvalidOperationException error)
            {
                report.Diagnostics.Add("Generation was not possible: " + error.Message);
            }
            sandboxStatus.Text = string.Format("Analyzed {0} of {1} ROM scenes; {2} rooms.", scenes.Count, sceneIds.Length, report.Rooms.Count);
            string summary = string.Format("Analyzed {0} of {1} ROM scenes, {2} rooms, and {3} native connections.\n\nAnalysis: {4}",
                scenes.Count, sceneIds.Length, report.Rooms.Count, report.Connections.Count, reportPath);
            summary += string.Format("\nDungeon pattern profiles: {0}.\nPatterns: {1}", report.DungeonPatterns.Dungeons.Count, patternPath);
            if (plan != null) summary += string.Format("\nGenerated plan: {0} rooms, {1} connections.\nPlan: {2}", plan.Rooms.Count, plan.Connections.Count, planPath);
            if (report.Diagnostics.Count > 0) summary += "\n\nDiagnostics:\n" + string.Join("\n", report.Diagnostics.Take(12).ToArray());
            MessageBox.Show(this, summary, "ROM-wide dungeon analysis", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void AutoChopSandboxEntrances()
        {
            if (sandboxCatalog == null) throw new InvalidOperationException("Load the source ROM library first.");
            if (CurrentScene == null || RoomList.SelectedIndex < 0 || RoomList.SelectedIndex >= CurrentScene.Rooms.Count)
                throw new InvalidOperationException("Choose a source room before chopping entrances.");

            List<DungeonRoomTilePlan> plans = DungeonRoomTiling.Analyze(CurrentScene, CurrentScene.SceneNumber, RoomList.SelectedIndex, null);
            List<string> saved = new List<string>();
            List<string> skipped = new List<string>();
            foreach (DungeonRoomTilePlan plan in plans)
            {
                if (plan.VisualFaces.Count == 0 || plan.CollisionFaces.Count == 0)
                {
                    skipped.Add(plan.Name + (string.IsNullOrEmpty(plan.Warning) ? "" : ": " + plan.Warning));
                    continue;
                }

                Vector3d pivot = plan.Socket.Position;
                SectionTile tile = SectionTile.Capture(CurrentScene, RoomList.SelectedIndex,
                    plan.VisualFaces, plan.CollisionFaces, pivot, plan.Name,
                    sandboxCatalog.Rooms.RomFingerprint, sandboxGrid == null ? 100 : (double)sandboxGrid.Value,
                    sandboxCatalog, plan.Min, plan.Max, false);
                tile.Sockets.Add(new TileSocket
                {
                    Id = plan.Socket.Id,
                    Name = plan.Socket.Name,
                    Position = plan.Socket.Position - pivot,
                    Facing = plan.Socket.Facing,
                    Width = plan.SocketWidth,
                    Height = plan.SocketHeight,
                    Room = -1
                });
                TileStorage.Save(tile, true, new[] { tile.Visual, tile.Collision });
                saved.Add(tile.Name);
            }

            LoadSandboxTiles();
            if (saved.Count == 0)
                throw new InvalidOperationException("No entrance regions produced complete visual and collision tiles. " + string.Join(" ", skipped.ToArray()));
            sandboxStatus.Text = "Auto-chopped " + saved.Count + " entrance tile" + (saved.Count == 1 ? "" : "s") + ".";
            string report = "Created " + saved.Count + " entrance tile" + (saved.Count == 1 ? "" : "s") + ":\n\n" + string.Join("\n", saved.ToArray());
            if (skipped.Count > 0) report += "\n\nSkipped:\n" + string.Join("\n", skipped.ToArray());
            MessageBox.Show(this, report, "Entrance subdivision", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ExtractSandboxRoomLibrary()
        {
            if (sandboxCatalog == null || sandboxRom == null) throw new InvalidOperationException("Import a ROM first.");
            List<int> selectedScenes = SelectSandboxExtractionScenes();
            if (selectedScenes == null || selectedScenes.Count == 0) return;
            string thumbnailDirectory = Path.Combine(sandboxCatalog.DirectoryPath, "room-thumbnails");
            Directory.CreateDirectory(thumbnailDirectory);
            HashSet<int> selectedSceneSet = new HashSet<int>(selectedScenes);
            List<SceneTileSource> sources = sandboxCatalog.Rooms.Rooms.Where(r => selectedSceneSet.Contains(r.SceneId)).OrderBy(r => r.SceneId).ThenBy(r => r.RoomId).ToList();
            if (sources.Count == 0) throw new InvalidOperationException("The selected scenes have no indexed rooms.");
            int saved = 0, thumbnails = 0, skipped = 0;
            bool previousTextureWarnings = settings.DisableTextureWarnings;
            settings.DisableTextureWarnings = true;
            try
            {
                using (Form progress = new Form { Text = "Extracting ROM room library", ClientSize = new Size(460, 120), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, ControlBox = false })
                {
                    Label label = new Label { Dock = DockStyle.Top, Height = 42, Text = "Preparing room 0 of " + sources.Count, Padding = new Padding(12) };
                    ProgressBar bar = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = Math.Max(1, sources.Count) };
                    progress.Controls.Add(bar); progress.Controls.Add(label); progress.Show(this);
                    for (int i = 0; i < sources.Count; i++)
                    {
                        SceneTileSource source = sources[i];
                        string thumbnailPath = Path.Combine(thumbnailDirectory, source.SceneId.ToString("X2") + "-" + source.RoomId.ToString("D2") + ".png");
                        label.Text = string.Format("Extracting {0} room {1} of {2}", SandboxRoomLocation(source.SceneId), i + 1, sources.Count);
                        bar.Value = i;
                        try
                        {
                            OpenSandboxRoom(source, false);
                            ZScene.ZRoom room = CurrentScene.Rooms[source.RoomId];
                            int visualCount = room.TrueGroups == null ? 0 : room.TrueGroups.SelectMany(g => g.Triangles).Count();
                            if (visualCount > 0 && CurrentScene.ColModel != null)
                            {
                                Vector3d min = room.ObjModel.Vertices.Select(v => v.ToVector3d()).Aggregate((a, b) => new Vector3d(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)));
                                Vector3d max = room.ObjModel.Vertices.Select(v => v.ToVector3d()).Aggregate((a, b) => new Vector3d(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)));
                                int crossing;
                                HashSet<int> collision = TileAssembly.FacesInBox(CurrentScene.ColModel, CurrentScene.ColModel.Groups, min, max, out crossing);
                                HashSet<int> visual = new HashSet<int>(Enumerable.Range(0, visualCount));
                                if (collision.Count > 0)
                                {
                                    Vector3d pivot = (min + max) * 0.5;
                                    string name = SandboxRoomLocation(source.SceneId) + " - Room " + source.RoomId;
                                    SectionTile tile = SectionTile.Capture(CurrentScene, source.RoomId, visual, collision, pivot, name, sandboxCatalog.Rooms.RomFingerprint, 100, sandboxCatalog, min, max, true);
                                    foreach (RoomPackageActor door in tile.Gameplay.Actors.Where(a => a.IsTransition))
                                    {
                                        bool isFront = door.FrontRoom == source.RoomId;
                                        bool isBack = door.BackRoom == source.RoomId;
                                        if (!isFront && !isBack) continue;
                                        double facing = TileAssembly.RoomDoorFacing(door.Rotation.Y * 360.0 / 65536.0, isFront);
                                        tile.Sockets.Add(new TileSocket
                                        {
                                            Id = door.Id + "_room_" + source.RoomId,
                                            Name = door.Name + " doorway",
                                            Position = door.Position,
                                            Facing = facing,
                                            Width = 100,
                                            Height = 180,
                                            Room = -1
                                        });
                                        RoomPackageActor sourceSpawn = tile.Gameplay.Spawns.FirstOrDefault();
                                        if (sourceSpawn != null)
                                            tile.EntryFrames.Add(new TileEntryFrame
                                            {
                                                SocketId = door.Id + "_room_" + source.RoomId,
                                                SpawnPosition = sourceSpawn.Position,
                                                SpawnRotation = sourceSpawn.Rotation
                                            });
                                    }
                                    string path = TileStorage.Save(tile, true, new[] { tile.Visual, tile.Collision });
                                    RomPaletteAsset asset = sandboxCatalog.Assets.FirstOrDefault(a => a.Room == source);
                                    if (asset != null) asset.TilePath = path;
                                    saved++;
                                }
                                else skipped++;
                            }
                            else skipped++;
                            using (Bitmap thumbnail = CaptureUnlitRoomThumbnail(source)) SaveRoomThumbnail(thumbnail, thumbnailPath);
                            RomPaletteAsset roomAsset = sandboxCatalog.Assets.FirstOrDefault(a => a.Room == source);
                            if (roomAsset != null) roomAsset.ThumbnailPath = thumbnailPath;
                            thumbnails++;
                        }
                        catch (Exception error)
                        {
                            skipped++;
                            ArcharinaWorkspace.LogError(error);
                            // Keep the room drawer complete even when one room cannot be decoded or rendered.
                            try
                            {
                                using (Bitmap thumbnail = MakeRoomFallbackThumbnail(source, "Preview unavailable")) SaveRoomThumbnail(thumbnail, thumbnailPath);
                                RomPaletteAsset roomAsset = sandboxCatalog.Assets.FirstOrDefault(a => a.Room == source);
                                if (roomAsset != null) roomAsset.ThumbnailPath = thumbnailPath;
                                thumbnails++;
                            }
                            catch (Exception thumbnailError)
                            {
                                ArcharinaWorkspace.LogError(thumbnailError);
                            }
                        }
                        Application.DoEvents();
                    }
                    progress.Close();
                }
            }
            finally { settings.DisableTextureWarnings = previousTextureWarnings; }
            LoadSandboxTiles(); RefreshSandboxPalette();
            sandboxStatus.Text = string.Format("Extracted {0} room tiles and {1} room thumbnails; {2} rooms need review.", saved, thumbnails, skipped);
            MessageBox.Show(this, string.Format("Room library ready.\n\n{0} tiles extracted\n{1} visual thumbnails generated\n{2} rooms need review\n\nChoose a location in the Room drawer to browse them.", saved, thumbnails, skipped), "Room library", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private List<int> SelectSandboxExtractionScenes()
        {
            List<int> sceneIds = sandboxCatalog.Rooms.Rooms.Select(r => r.SceneId).Distinct().OrderBy(x => x).ToList();
            using (Form dialog = new Form { Text = "Choose scenes to extract", ClientSize = new Size(620, 520), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                Label instructions = new Label { Dock = DockStyle.Fill, Text = "Extract only the scenes you need. Use Current loaded scene for the room currently open in the viewport, or check any combination below.", AutoSize = false };
                CheckedListBox scenes = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
                foreach (int sceneId in sceneIds)
                {
                    int roomCount = sandboxCatalog.Rooms.Rooms.Count(r => r.SceneId == sceneId);
                    scenes.Items.Add(string.Format("Scene {0:X2} - {1} ({2} rooms)", sceneId, SandboxRoomLocation(sceneId), roomCount));
                }
                int loadedIndex = sceneIds.IndexOf(CurrentScene == null ? -1 : CurrentScene.SceneNumber);
                if (loadedIndex >= 0) scenes.SetItemChecked(loadedIndex, true);
                FlowLayoutPanel quick = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
                Button current = new Button { Text = "Current loaded scene", AutoSize = true };
                current.Click += delegate { for (int i = 0; i < scenes.Items.Count; i++) scenes.SetItemChecked(i, i == loadedIndex); };
                Button all = new Button { Text = "Select all scenes", AutoSize = true };
                all.Click += delegate { for (int i = 0; i < scenes.Items.Count; i++) scenes.SetItemChecked(i, true); };
                Button none = new Button { Text = "Clear selection", AutoSize = true };
                none.Click += delegate { for (int i = 0; i < scenes.Items.Count; i++) scenes.SetItemChecked(i, false); };
                quick.Controls.Add(current); quick.Controls.Add(all); quick.Controls.Add(none);
                FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
                Button extract = new Button { Text = "Extract selected scenes", AutoSize = true };
                List<int> result = null;
                extract.Click += delegate
                {
                    result = sceneIds.Where((sceneId, index) => scenes.GetItemChecked(index)).ToList();
                    if (result.Count == 0)
                    {
                        MessageBox.Show(dialog, "Check at least one scene.", "Scene extraction", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    dialog.DialogResult = DialogResult.OK;
                };
                actions.Controls.Add(cancel); actions.Controls.Add(extract);
                layout.Controls.Add(instructions, 0, 0); layout.Controls.Add(scenes, 0, 1); layout.Controls.Add(quick, 0, 2); layout.Controls.Add(actions, 0, 3);
                dialog.Controls.Add(layout); dialog.AcceptButton = extract; dialog.CancelButton = cancel;
                return dialog.ShowDialog(this) == DialogResult.OK ? result : null;
            }
        }

        private Bitmap CaptureUnlitRoomThumbnail(SceneTileSource source)
        {
            const int width = 220;
            const int height = 150;
            ZScene.ZRoom room = CurrentScene == null || source.RoomId < 0 || source.RoomId >= CurrentScene.Rooms.Count
                ? null : CurrentScene.Rooms[source.RoomId];
            ObjFile model = room == null ? null : room.ObjModel;
            IList<ObjFile.Group> groups = room == null ? null : room.TrueGroups;
            if (model == null || groups == null || model.Vertices == null || model.Vertices.Count == 0 || !groups.SelectMany(g => g.Triangles).Any())
                return MakeRoomFallbackThumbnail(source, "No visual geometry");

            Vector3d min = model.Vertices[0].ToVector3d();
            Vector3d max = min;
            foreach (ObjFile.Vertex vertex in model.Vertices)
            {
                Vector3d point = vertex.ToVector3d();
                min = new Vector3d(Math.Min(min.X, point.X), Math.Min(min.Y, point.Y), Math.Min(min.Z, point.Z));
                max = new Vector3d(Math.Max(max.X, point.X), Math.Max(max.Y, point.Y), Math.Max(max.Z, point.Z));
            }
            Vector3d center = (min + max) * 0.5;
            double extent = Math.Max(Math.Max(max.X - min.X, max.Y - min.Y), max.Z - min.Z);
            if (extent <= 0.001) return MakeRoomFallbackThumbnail(source, "Degenerate geometry");

            glControl1.MakeCurrent();
            GL.PushAttrib(AttribMask.AllAttribBits);
            GL.MatrixMode(MatrixMode.Projection); GL.PushMatrix(); GL.LoadIdentity();
            GL.Viewport(0, 0, width, height);
            double aspect = (double)width / height;
            GL.Ortho(-aspect, aspect, -1.0, 1.0, -10.0, 10.0);
            GL.MatrixMode(MatrixMode.Modelview); GL.PushMatrix(); GL.LoadIdentity();
            GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Lighting); GL.Disable(EnableCap.Fog);
            GL.Disable(EnableCap.CullFace); GL.Disable(EnableCap.Blend); GL.Enable(EnableCap.DepthTest);
            GL.ClearColor(Color.FromArgb(18, 22, 28));
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.Translate(0.0, -0.03, 0.0);
            GL.Rotate(26.0, 1.0, 0.0, 0.0);
            GL.Rotate(-35.0, 0.0, 1.0, 0.0);
            GL.Scale(1.65 / extent, 1.65 / extent, 1.65 / extent);
            GL.Translate(-center.X, -center.Y, -center.Z);
            GL.Begin(BeginMode.Triangles);
            int groupIndex = 0;
            foreach (ObjFile.Group group in groups)
            {
                float shade = 0.54f + (groupIndex++ % 4) * 0.045f;
                GL.Color3(shade, shade * 0.98f, shade * 0.88f);
                foreach (ObjFile.Triangle triangle in group.Triangles)
                {
                    if (triangle.VertIndex == null || triangle.VertIndex.Length < 3) continue;
                    bool valid = true;
                    for (int corner = 0; corner < 3; corner++)
                        if (triangle.VertIndex[corner] < 0 || triangle.VertIndex[corner] >= model.Vertices.Count) valid = false;
                    if (!valid) continue;
                    for (int corner = 0; corner < 3; corner++)
                    {
                        ObjFile.Vertex vertex = model.Vertices[triangle.VertIndex[corner]];
                        GL.Vertex3(vertex.X, vertex.Y, vertex.Z);
                    }
                }
            }
            GL.End();
            Bitmap result = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            System.Drawing.Imaging.BitmapData data = result.LockBits(new Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            // The thumbnail is captured before SwapBuffers, so the freshly rendered image is in the back buffer.
            GL.ReadBuffer(ReadBufferMode.Back);
            GL.ReadPixels(0, 0, width, height, PixelFormat.Bgr, PixelType.UnsignedByte, data.Scan0);
            result.UnlockBits(data);
            result.RotateFlip(RotateFlipType.RotateNoneFlipY);
            GL.MatrixMode(MatrixMode.Modelview); GL.PopMatrix();
            GL.MatrixMode(MatrixMode.Projection); GL.PopMatrix();
            GL.PopAttrib();
            return result;
        }

        private static void SaveRoomThumbnail(Bitmap thumbnail, string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                thumbnail.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        }

        private static Bitmap MakeRoomFallbackThumbnail(SceneTileSource source, string message)
        {
            Bitmap result = new Bitmap(220, 150);
            using (Graphics graphics = Graphics.FromImage(result))
            using (Font font = new Font("Segoe UI", 9.0f, FontStyle.Regular))
            using (Brush brush = new SolidBrush(Color.FromArgb(210, 220, 228)))
            {
                graphics.Clear(Color.FromArgb(18, 22, 28));
                graphics.DrawRectangle(Pens.DimGray, 8, 8, 204, 134);
                graphics.DrawString(string.Format("Scene {0:X2} / Room {1:D2}", source.SceneId, source.RoomId), font, brush, 16, 50);
                graphics.DrawString(message, font, Brushes.Gray, 16, 76);
            }
            return result;
        }

        private void RefreshSandboxSurfaces()
        {
            if (sandboxTileSurface == null) return;
            int selected = sandboxTileSurface.SelectedIndex;
            sandboxTileSurface.Items.Clear(); sandboxTileSurface.Items.Add("Source surfaces");
            if (CurrentScene != null) for (int i = 0; i < CurrentScene.PolyTypes.Count; i++) sandboxTileSurface.Items.Add("Surface " + i);
            sandboxTileSurface.SelectedIndex = Math.Max(0, Math.Min(selected, sandboxTileSurface.Items.Count - 1));
        }

        private void ClearSandboxFaces()
        {
            sandboxVisualFaces.Clear(); sandboxCollisionFaces.Clear(); UpdateSandboxFaceCount();
        }
        private void UpdateSandboxFaceCount()
        {
            if (sandboxFaceCount != null) sandboxFaceCount.Text = sandboxVisualFaces.Count + " visual / " + sandboxCollisionFaces.Count + " collision";
            if (glControl1 != null) glControl1.Invalidate();
        }
        private bool TrySelectSandboxTileFace(MouseEventArgs e)
        {
            if (sandboxRoot == null || sandboxFaceMode == 0 || e.Button != MouseButtons.Left) return false;
            SandboxRun(() =>
            {
                if (CurrentScene == null || RoomList.SelectedIndex < 0) return;
                bool collision = sandboxFaceMode == 2;
                ObjFile model = collision ? CurrentScene.ColModel : CurrentScene.Rooms[RoomList.SelectedIndex].ObjModel;
                if (model == null) return;
                var groups = collision ? model.Groups : CurrentScene.Rooms[RoomList.SelectedIndex].TrueGroups;
                Vector3d start, end; GetSandboxRay(e.Location, out start, out end);
                int hit = PickSandboxFace(model, groups, start, end, (float)CurrentScene.Scale);
                if (hit < 0) return;
                HashSet<int> selection = collision ? sandboxCollisionFaces : sandboxVisualFaces;
                if (!selection.Add(hit)) selection.Remove(hit);
                UpdateSandboxFaceCount();
            });
            return true;
        }

        internal static int PickSandboxFace(ObjFile model, IList<ObjFile.Group> groups, Vector3d start, Vector3d end, float scale)
        {
            int index = 0, best = -1; double distance = double.MaxValue;
            foreach (ObjFile.Triangle t in groups.SelectMany(g => g.Triangles))
            {
                Vector3 hit = ObjFile.RayCollision(model.Vertices[t.VertIndex[0]], model.Vertices[t.VertIndex[1]], model.Vertices[t.VertIndex[2]], (Vector3)start, (Vector3)end, scale);
                if (hit != new Vector3(-1))
                {
                    double d = ((Vector3d)hit - start).LengthSquared;
                    if (d < distance) { distance = d; best = index; }
                }
                index++;
            }
            return best;
        }

        private void RenderSandboxSelection()
        {
            if (sandboxRoot == null || CurrentScene == null || RoomList.SelectedIndex < 0) return;
            GL.PushAttrib(AttribMask.AllAttribBits);
            GL.MatrixMode(MatrixMode.Modelview); GL.PushMatrix(); GL.LoadIdentity(); Camera.Position();
            GL.Scale(0.005 * CurrentScene.Scale, 0.005 * CurrentScene.Scale, 0.005 * CurrentScene.Scale);
            GL.Disable((EnableCap)All.FragmentProgram); GL.ActiveTexture(TextureUnit.Texture1); GL.Disable(EnableCap.Texture2D);
            GL.ActiveTexture(TextureUnit.Texture0); GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Lighting); GL.Disable(EnableCap.Fog);
            GL.Disable(EnableCap.CullFace); GL.Disable(EnableCap.AlphaTest); GL.DepthMask(false);
            GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.PolygonOffsetFill); GL.PolygonOffset(-1, -1);
            ZScene.ZRoom room = CurrentScene.Rooms[RoomList.SelectedIndex];
            if (sandboxFaceMode == 2 && CurrentScene.ColModel != null)
            {
                GL.Disable(EnableCap.DepthTest); GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line); GL.Color4(0.2f, 0.8f, 1f, 0.3f);
                DrawSandboxFaces(CurrentScene.ColModel, CurrentScene.ColModel.Groups, null);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill); GL.Color4(0.2f, 0.9f, 1f, 0.6f);
                DrawSandboxFaces(CurrentScene.ColModel, CurrentScene.ColModel.Groups, sandboxCollisionFaces);
            }
            else if (room.ObjModel != null)
            {
                GL.Enable(EnableCap.DepthTest); GL.Color4(1f, 0.75f, 0.1f, 0.6f);
                DrawSandboxFaces(room.ObjModel, room.TrueGroups, sandboxVisualFaces);
            }
            GL.PopMatrix(); GL.PopAttrib();
        }

        private void RenderSandboxActorGizmo()
        {
            if (sandboxMoveActor == null || !sandboxMoveActor.Checked || sandboxMovingActor == null && SelectedSandboxActor() == null) return;
            ZActor actor = sandboxMovingActor ?? SelectedSandboxActor();
            if (actor == null) return;
            GL.PushAttrib(AttribMask.AllAttribBits); GL.MatrixMode(MatrixMode.Modelview); GL.PushMatrix(); GL.LoadIdentity(); Camera.Position();
            GL.Scale(0.005 * (CurrentScene == null ? 1 : CurrentScene.Scale), 0.005 * (CurrentScene == null ? 1 : CurrentScene.Scale), 0.005 * (CurrentScene == null ? 1 : CurrentScene.Scale));
            GL.Disable((EnableCap)All.FragmentProgram); GL.Disable(EnableCap.Lighting); GL.Disable(EnableCap.Fog); GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false); GL.LineWidth(4); Vector3d center = new Vector3d(actor.XPos, actor.YPos, actor.ZPos); double length = 180;
            GL.Begin(PrimitiveType.Lines); GL.Color3(Color.Red); GL.Vertex3(center); GL.Vertex3(center + new Vector3d(length, 0, 0));
            GL.Color3(Color.LimeGreen); GL.Vertex3(center); GL.Vertex3(center + new Vector3d(0, length, 0));
            GL.Color3(Color.DeepSkyBlue); GL.Vertex3(center); GL.Vertex3(center + new Vector3d(0, 0, length)); GL.End();
            GL.PointSize(9); GL.Begin(PrimitiveType.Points); GL.Color3(Color.White); GL.Vertex3(center); GL.End();
            GL.PopMatrix(); GL.PopAttrib();
        }
        private static void DrawSandboxFaces(ObjFile model, IList<ObjFile.Group> groups, ISet<int> selection)
        {
            int index = 0; GL.Begin(PrimitiveType.Triangles);
            foreach (ObjFile.Triangle t in groups.SelectMany(g => g.Triangles))
            {
                if (selection == null || selection.Contains(index))
                    foreach (int i in t.VertIndex) GL.Vertex3(model.Vertices[i].ToVector3d());
                index++;
            }
            GL.End();
        }

        private void ShowSaveSandboxTile()
        {
            if (sandboxVisualFaces.Count == 0 || sandboxCollisionFaces.Count == 0) throw new InvalidOperationException("Select visual faces and collision faces before saving a tile.");
            ZScene.ZRoom room = CurrentScene.Rooms[RoomList.SelectedIndex];
            var selected = room.TrueGroups.SelectMany(g => g.Triangles).Where((t, i) => sandboxVisualFaces.Contains(i))
                .SelectMany(t => t.VertIndex).Distinct().Select(i => room.ObjModel.Vertices[i]).ToList();
            using (Form dialog = new Form { Text = "Save section tile", ClientSize = new Size(380, 280), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false })
            {
                TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 6 };
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                TextBox name = new TextBox { Text = "Room section", Dock = DockStyle.Fill };
                fields.Controls.Add(new Label { Text = "Name" }, 0, 0); fields.Controls.Add(name, 1, 0);
                NumericUpDown[] values = new NumericUpDown[4];
                double[] defaults = { (selected.Min(v => v.X) + selected.Max(v => v.X)) / 2, selected.Min(v => v.Y), (selected.Min(v => v.Z) + selected.Max(v => v.Z)) / 2, (double)sandboxGrid.Value };
                string[] labels = { "Pivot X", "Pivot Y", "Pivot Z", "Grid" };
                for (int i = 0; i < 4; i++)
                {
                    values[i] = new NumericUpDown { Minimum = i == 3 ? 1 : short.MinValue, Maximum = i == 3 ? 4096 : short.MaxValue, Value = (decimal)defaults[i], DecimalPlaces = i == 3 ? 0 : 1, Dock = DockStyle.Fill };
                    fields.Controls.Add(new Label { Text = labels[i] }, 0, i + 1); fields.Controls.Add(values[i], 1, i + 1);
                }
                Button save = new Button { Text = "Save tile", Dock = DockStyle.Fill };
                save.Click += delegate { try { SaveSandboxTile(name.Text, new Vector3d((double)values[0].Value, (double)values[1].Value, (double)values[2].Value), (double)values[3].Value); dialog.DialogResult = DialogResult.OK; } catch (Exception error) { MessageBox.Show(dialog, error.Message, "Tile capture"); } };
                fields.Controls.Add(save, 1, 5); dialog.Controls.Add(fields); dialog.AcceptButton = save; dialog.ShowDialog(this);
            }
        }

        internal string SaveSandboxTile(string name, Vector3d pivot, double grid)
        {
            if (sandboxCatalog == null) throw new InvalidOperationException("Load the source ROM library first.");
            SectionTile tile = SectionTile.Capture(CurrentScene, RoomList.SelectedIndex, sandboxVisualFaces, sandboxCollisionFaces, pivot, name, sandboxCatalog.Rooms.RomFingerprint, grid,
                sandboxCatalog, null, null, true);
            string path = TileStorage.Save(tile, true, new[] { tile.Visual, tile.Collision });
            LoadSandboxTiles(); sandboxCategories.SelectedNode = sandboxCategories.Nodes.Cast<TreeNode>().First(n => n.Text == "Tiles");
            List<string> review = tile.Gameplay == null ? new List<string>() : tile.Gameplay.Review();
            sandboxStatus.Text = "Saved tile: " + tile.Name + (review.Count == 0 ? " (gameplay manifest complete)" : " (" + review.Count + " gameplay review item" + (review.Count == 1 ? "" : "s") + ")"); return path;
        }

        private void ImportSandboxModelTile()
        {
            if (sandboxCatalog == null) throw new InvalidOperationException("Load the source ROM library first.");
            using (OpenFileDialog picker = new OpenFileDialog
            {
                Filter = "3D model (*.obj;*.dae)|*.obj;*.dae|Wavefront OBJ (*.obj)|*.obj|Collada DAE (*.dae)|*.dae",
                Title = "Import model as reusable room tile"
            })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                ObjFile model = new ObjFile(picker.FileName, false);
                if (model.Vertices.Count == 0 || model.Groups == null || model.Groups.Count == 0)
                    throw new InvalidDataException("The imported model has no usable geometry.");

                List<ObjFile.Group> collisionGroups = model.Groups
                    .Where(group => group.Name == null || group.Name.IndexOf("#NoCollision", StringComparison.OrdinalIgnoreCase) < 0)
                    .ToList();
                if (collisionGroups.Count == 0) collisionGroups = model.Groups.ToList();
                TileMesh visual = TileMesh.Copy(model, model.Groups);
                TileMesh collision = TileMesh.Copy(model, collisionGroups);
                for (int i = 0; i < collision.Groups.Count; i++)
                {
                    collision.Groups[i].Name = "Collision" + i;
                    collision.Groups[i].PolyType = 0;
                }
                SectionTile tile = new SectionTile
                {
                    Name = "Imported model - " + Path.GetFileNameWithoutExtension(picker.FileName),
                    LibraryKind = "ImportedModel",
                    RomFingerprint = sandboxCatalog.Rooms.RomFingerprint,
                    SourceScene = CurrentScene == null ? 0 : CurrentScene.SceneNumber,
                    Grid = sandboxGrid == null ? 100 : (double)sandboxGrid.Value,
                    Visual = visual,
                    Collision = collision,
                    Surfaces = new List<ZColPolyType> { new ZColPolyType() }
                };
                string path = TileStorage.Save(tile, true, new[] { tile.Visual, tile.Collision });
                LoadSandboxTiles();
                TreeNode imported = sandboxCategories.Nodes.Cast<TreeNode>().SelectMany(SandboxDescendants).Concat(sandboxCategories.Nodes.Cast<TreeNode>())
                    .FirstOrDefault(node => (node.Tag as SandboxTileCategory)?.ImportedOnly == true);
                if (imported != null) sandboxCategories.SelectedNode = imported;
                sandboxStatus.Text = "Imported model tile: " + tile.Name + ". Author doorway sockets with Tile sockets... if needed.";
            }
        }

        private void LoadSandboxTiles()
        {
            if (sandboxCatalog == null) return;
            // Refresh only generated tile entries. Source-room entries retain their
            // TilePath association so room cards and recommendations stay connected.
            sandboxCatalog.Assets.RemoveAll(a => a.Category == "Tiles" && a.TilePath != null);
            string directory = Path.Combine(TileStorage.Root, "library");
            int loaded = 0;
            int skippedFingerprint = 0;
            if (Directory.Exists(directory)) foreach (string path in Directory.GetFiles(directory, "*.xml"))
            {
                try
                {
                    SectionTile tile = TileStorage.Load<SectionTile>(path);
                    if (tile.Version != 1 || !string.Equals(tile.RomFingerprint, sandboxCatalog.Rooms.RomFingerprint, StringComparison.OrdinalIgnoreCase))
                    {
                        skippedFingerprint++;
                        continue;
                    }

                    SceneTileSource source = FindSandboxTileSource(tile);
                    RomPaletteAsset asset = new RomPaletteAsset
                    {
                        Category = "Tiles",
                        Name = string.IsNullOrWhiteSpace(tile.Name) ? Path.GetFileNameWithoutExtension(path) : tile.Name,
                        TilePath = path,
                        TileSourceScene = tile.SourceScene,
                        GeneratedTile = (tile.Name ?? "").StartsWith("Generated Kit ", StringComparison.OrdinalIgnoreCase),
                        ImportedTile = string.Equals(tile.LibraryKind, "ImportedModel", StringComparison.OrdinalIgnoreCase)
                    };
                    if (source != null)
                    {
                        asset.Room = source;
                        asset.TileSourceScene = source.SceneId;
                        RomPaletteAsset roomAsset = sandboxCatalog.Assets.FirstOrDefault(a => a.Room == source);
                        if (roomAsset != null)
                        {
                            roomAsset.TilePath = path;
                            asset.ThumbnailPath = SandboxRoomThumbnailPath(source, roomAsset);
                        }
                    }
                    sandboxCatalog.Assets.Add(asset);
                    loaded++;
                }
                catch (Exception error) { ArcharinaWorkspace.LogError(error); }
            }
            RebuildSandboxCategories();
            RefreshSandboxPalette();
            if (sandboxCategories.SelectedNode != null && sandboxCategories.SelectedNode.Text == "Tiles")
                sandboxStatus.Text = loaded == 0
                    ? "No saved tiles match the loaded ROM. Extract or save a tile first."
                    : string.Format("Loaded {0} room tile{1} for this ROM{2}.", loaded, loaded == 1 ? "" : "s", skippedFingerprint == 0 ? "" : " (other ROM tiles hidden)");
        }

        private SceneTileSource FindSandboxTileSource(SectionTile tile)
        {
            if (tile == null || sandboxCatalog == null || sandboxCatalog.Rooms == null) return null;
            string name = tile.Name ?? "";
            const string marker = " - Room ";
            int markerIndex = name.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            int roomId;
            if (markerIndex < 0 || !int.TryParse(name.Substring(markerIndex + marker.Length).Trim(), out roomId)) return null;
            return sandboxCatalog.Rooms.Rooms.FirstOrDefault(r => r.SceneId == tile.SourceScene && r.RoomId == roomId);
        }

        internal void PlaceSandboxTile(string path, Vector3d position, int turns)
        {
            if (CurrentScene == null || CurrentScene.Scale != 1 || CurrentScene.SceneHeaders.Count > 0 || RoomList.SelectedIndex < 0)
                throw new InvalidOperationException("Tile placement currently requires a scale-1 scene without alternate setups.");
            SectionTile tile = TileStorage.Load<SectionTile>(path);
            if (tile.Version != 1 || sandboxCatalog == null || tile.RomFingerprint != sandboxCatalog.Rooms.RomFingerprint)
                throw new InvalidOperationException("This tile needs its matching source ROM.");
            if (!EnsureSandboxTileSurface(tile)) return;
            TileSnap snap = sandboxMagnetic != null && sandboxMagnetic.Checked ? FindSandboxSnap(tile, position, turns) : null;
            if (snap != null) { position = snap.Position; turns = snap.Turns; }
            else if (sandboxSnap.Checked) { position.X = Math.Round(position.X / tile.Grid) * tile.Grid; position.Y = Math.Round(position.Y / tile.Grid) * tile.Grid; position.Z = Math.Round(position.Z / tile.Grid) * tile.Grid; }
            TileSceneGeometry geometry;
            if (string.IsNullOrEmpty(CurrentScene.SandboxGeometryFile))
            {
                geometry = TileSceneGeometry.Capture(CurrentScene);
                CurrentScene.SandboxGeometryFile = TileStorage.Save(geometry, false, geometry.Rooms.Concat(new[] { geometry.Collision }));
                TrackSandboxGeometryEdit(geometry, "Create sandbox geometry sidecar");
            }
            else geometry = TileStorage.Load<TileSceneGeometry>(CurrentScene.SandboxGeometryFile);
            SandboxRecord();
            geometry.Place(tile, RoomList.SelectedIndex, position, turns, sandboxTileSurface.SelectedIndex - 1, snap, path);
            string next = TileStorage.Save(geometry, false, geometry.Rooms.Concat(new[] { geometry.Collision }));
            TrackSandboxGeometryEdit(geometry, "Place sandbox tile");
            RestoreSandboxGeometry(next);
            List<string> nativeReview = NativeGameplayMaterializer.Apply(CurrentScene);
            savechanges = true; ClearSandboxFaces(); RefreshSandboxContents();
            List<string> review = geometry.GameplayWarnings.Concat(nativeReview).Distinct().ToList();
            sandboxStatus.Text = "Placed tile: " + tile.Name + (review.Count == 0 ? "" : " | gameplay review: " + review.Count);
            if (review.Count > 0)
                MessageBox.Show(this, string.Join("\n", review.Take(12).ToArray()), "Gameplay package review", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private bool EnsureSandboxTileSurface(SectionTile tile)
        {
            if (tile == null || sandboxTileSurface == null || tile.SourceScene == CurrentScene.SceneNumber || sandboxTileSurface.SelectedIndex > 0)
                return true;
            if (CurrentScene.PolyTypes == null || CurrentScene.PolyTypes.Count == 0)
                throw new InvalidOperationException("The destination scene has no collision surfaces to map this tile onto.");
            using (Form dialog = new Form { Text = "Map tile collision surface", ClientSize = new Size(520, 190), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 4 };
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                fields.Controls.Add(new Label { Text = "Tile source", AutoSize = true }, 0, 0);
                fields.Controls.Add(new Label { Text = "Scene " + tile.SourceScene.ToString("X2") + " - " + tile.Name, AutoSize = true }, 1, 0);
                fields.Controls.Add(new Label { Text = "Destination surface", AutoSize = true }, 0, 1);
                ComboBox surface = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
                for (int i = 0; i < CurrentScene.PolyTypes.Count; i++) surface.Items.Add("Surface " + i + " - native collision type " + i);
                surface.SelectedIndex = 0; fields.Controls.Add(surface, 1, 1);
                fields.Controls.Add(new Label { Text = "Why", AutoSize = true }, 0, 2);
                fields.Controls.Add(new Label { Text = "Cross-scene collision must use an explicit destination mapping.", AutoSize = true }, 1, 2);
                FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
                Button apply = new Button { Text = "Map and place", AutoSize = true };
                apply.Click += delegate { sandboxTileSurface.SelectedIndex = surface.SelectedIndex + 1; dialog.DialogResult = DialogResult.OK; };
                actions.Controls.Add(cancel); actions.Controls.Add(apply); fields.Controls.Add(actions, 0, 3); fields.SetColumnSpan(actions, 2);
                dialog.Controls.Add(fields); dialog.AcceptButton = apply; dialog.CancelButton = cancel;
                return dialog.ShowDialog(this) == DialogResult.OK;
            }
        }

        private void PersistSandboxGeometry()
        {
            if (CurrentScene == null) return;
            TileSceneGeometry geometry = string.IsNullOrEmpty(CurrentScene.SandboxGeometryFile)
                ? TileSceneGeometry.Capture(CurrentScene)
                : TileStorage.Load<TileSceneGeometry>(CurrentScene.SandboxGeometryFile);
            if (geometry.BaseRooms == null) geometry.BaseRooms = new List<TileMesh>();
            while (geometry.BaseRooms.Count < CurrentScene.Rooms.Count)
            {
                int index = geometry.BaseRooms.Count;
                geometry.BaseRooms.Add(TileMesh.Copy(CurrentScene.Rooms[index].ObjModel, CurrentScene.Rooms[index].TrueGroups));
            }
            if (geometry.BaseCollision == null) geometry.BaseCollision = TileMesh.Copy(CurrentScene.ColModel, CurrentScene.ColModel.Groups);
            geometry.Rooms = CurrentScene.Rooms.Select(r => TileMesh.Copy(r.ObjModel, r.TrueGroups)).ToList();
            geometry.Collision = TileMesh.Copy(CurrentScene.ColModel, CurrentScene.ColModel.Groups);
            geometry.Sockets = CurrentScene.TileSockets.Select(s => s.Clone()).ToList();
            geometry.Surfaces = CurrentScene.PolyTypes.Select(p => new ZColPolyType(p.Raw, p.PolyFlagA, p.PolyFlagB)).ToList();
            geometry.GameplayPackages = CurrentScene.SandboxGameplayPackages == null ? new List<RoomPackage>() : CurrentScene.SandboxGameplayPackages.Select(TileStorage.Clone).ToList();
            geometry.AuthoringMarkers = sandboxAuthoringMarkers.Select(marker => marker.Clone()).ToList();
            geometry.BaseAuthoringMarkers = sandboxAuthoringMarkers.Select(marker => marker.Clone()).ToList();
            CurrentScene.SandboxGeometryFile = TileStorage.Save(geometry, false, geometry.Rooms.Concat(new[] { geometry.Collision }));
            TrackSandboxGeometryEdit(geometry, "Sandbox geometry edit");
        }

        private void TrackSandboxGeometryEdit(TileSceneGeometry geometry, string reason)
        {
            if (CurrentDocument == null || !object.ReferenceEquals(CurrentDocument.NativeScene, CurrentScene)) return;
            CurrentDocument.ReplaceSidecarState(geometry, reason);
        }

        internal void OpenSandboxSavedScene(string path)
        {
            OpenScene(path); RestoreSandboxGeometry(CurrentScene.SandboxGeometryFile);
            ClearSandboxFaces(); sandboxUndo.Clear(); sandboxRedo.Clear(); ApplySandboxPreview(); RefreshSandboxRooms(); FocusSandboxRoom();
        }

        private void RestoreSandboxGeometry(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            TileSceneGeometry geometry = TileStorage.Load<TileSceneGeometry>(path);
            if (geometry.Rooms.Count != CurrentScene.Rooms.Count) throw new InvalidDataException("Tile geometry room count does not match this scene.");
            sandboxAuthoringMarkers = (geometry.AuthoringMarkers ?? geometry.BaseAuthoringMarkers ?? new List<SandboxAuthoringMarker>())
                .Select(marker => marker.Clone()).ToList();
            glControl1.MakeCurrent();
            CurrentScene.TileSockets = geometry.Sockets.Select(s => s.Clone()).ToList();
            var previous = CurrentScene.Rooms.Select(r => r.ObjModel).Concat(new[] { CurrentScene.ColModel }).Where(m => m != null).ToList();
            var models = geometry.Rooms.Select(m => m.ToModel()).ToList();
            foreach (ObjFile model in models) { model.PreviewTwoSided = sandboxTwoSided.Checked; model.Prepare(true, model.Groups); }
            ObjFile collision = geometry.Collision.ToModel(); collision.Prepare(false, collision.Groups);
            for (int i = 0; i < models.Count; i++)
            {
                ZScene.ZRoom room = CurrentScene.Rooms[i]; room.ObjModel = models[i]; room.TrueGroups = models[i].Groups;
                EnsureGroupSettingsCapacity(room);
                foreach (var field in typeof(ZScene.ZRoom.ZGroupSettings).GetFields().Where(f => f.FieldType.IsArray && f.FieldType.GetArrayRank() == 1))
                {
                    var source = typeof(ObjFile.Group).GetField(field.Name);
                    if (source == null) continue;
                    Array array = (Array)field.GetValue(room.GroupSettings);
                    for (int g = 0; g < room.TrueGroups.Count; g++) array.SetValue(source.GetValue(room.TrueGroups[g]), g);
                }
            }
            CurrentScene.ColModel = collision; CurrentScene.PolyTypes = geometry.Surfaces;
            CurrentScene.SandboxGameplayPackages = geometry.GameplayPackages == null ? new List<RoomPackage>() : geometry.GameplayPackages.Select(TileStorage.Clone).ToList();
            CurrentScene.AutoCollision = false; CurrentScene.PregeneratedMesh = false; CurrentScene.SandboxGeometryFile = path;
            if (CurrentDocument != null) CurrentDocument.SynchronizeSidecar(geometry);
            RefreshSandboxSurfaces();
            foreach (int id in previous.SelectMany(m => m.Groups).Select(g => g.GLID).Where(id => id != 0).Distinct())
                if (GL.IsList(id)) GL.DeleteLists(id, 1);
            foreach (int id in previous.SelectMany(m => m.Materials).Select(m => m.GLID).Where(id => id != 0).Distinct())
                if (GL.IsTexture(id)) GL.DeleteTexture(id);
            foreach (Bitmap bitmap in previous.SelectMany(m => m.Materials).Select(m => m.TexImage).Where(b => b != null).Distinct()) bitmap.Dispose();
            SelectRoom(RoomList.SelectedIndex); glControl1.Invalidate();
        }
    }
}
