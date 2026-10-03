using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using SharpOcarina.Shared;

namespace SharpOcarina
{
    public partial class MainForm
    {
        private Panel sandboxRoot;
        private TreeView sandboxCategories;
        private ListBox sandboxPalette, sandboxContents;
        private ComboBox sandboxRooms;
        private TextBox sandboxSearch;
        private ListView sandboxRoomDrawer;
        private ImageList sandboxRoomImages;
        private ListView sandboxAssetDrawer;
        private ImageList sandboxAssetImages;
        private TreeView sandboxLayers;
        private PropertyGrid sandboxInspector;
        private Label sandboxSelectionSummary;
        private NumericUpDown sandboxGrid;
        private CheckBox sandboxSnap;
        private CheckBox sandboxTwoSided, sandboxIsolateRoom, sandboxNativePreview;
        private CheckBox sandboxMoveActor;
        private ComboBox sandboxInteractionMode;
        private readonly List<RadioButton> sandboxToolButtons = new List<RadioButton>();
        private Button sandboxMoveRoomButton;
        private ComboBox sandboxMarkerKind, sandboxMarkerFacing;
        private Button sandboxPlaceMarkerButton;
        private PlacedTile sandboxSelectedTile;
        private SandboxAuthoringMarker sandboxSelectedMarker;
        private bool sandboxMovingTile;
        private Vector3d sandboxTileDragStartWorld, sandboxTileDragOrigin;
        private bool sandboxMovingRoom;
        private bool sandboxRoomMoveArmed;
        private int sandboxMovingRoomIndex = -1;
        private double sandboxRoomDragHeight;
        private Vector3d sandboxRoomDragStartWorld, sandboxRoomDragAppliedDelta;
        private Vector3d sandboxRoomDragStartMin, sandboxRoomDragStartMax;
        private ToolStripStatusLabel sandboxStatus;
        private RomAssetCatalog sandboxCatalog;
        private byte[] sandboxRom;
        private string sandboxRomPath;
        private DungeonRomMechanicsProfile sandboxRomMechanics;
        private string sandboxProject64Path;
        private readonly Project64PlaytestService sandboxPlaytest =
            ArcharinaWorkspace.Project64Playtest;
        private GeneratedDungeonProgressionReport sandboxLastProgression;
        private bool sandboxRefreshing;
        private bool sandboxDarkMode = true;
        private ToolStripMenuItem sandboxDarkModeItem;
        private Point sandboxDragStart;
        private RomPaletteAsset sandboxDragAsset;
        private List<SandboxAuthoringMarker> sandboxAuthoringMarkers = new List<SandboxAuthoringMarker>();

        private sealed class SandboxRoomCategory
        {
            internal string Name;
            internal int SceneId = -1;
            public override string ToString() { return Name; }
        }
        private sealed class SandboxTileCategory
        {
            internal string Name;
            internal int SceneId = -1;
            internal string Family;
            internal bool GeneratedOnly;
            internal bool ImportedOnly;
            internal bool OtherOnly;
            internal bool All;
            public override string ToString() { return Name; }
        }
        private sealed class SandboxRecommendationCategory
        {
            public override string ToString() { return "Recommended rooms"; }
        }
        private readonly Stack<SandboxState> sandboxUndo = new Stack<SandboxState>();
        private readonly Stack<SandboxState> sandboxRedo = new Stack<SandboxState>();

        private sealed class SandboxState
        {
            internal List<List<ZActor>> Actors;
            internal List<List<ZScene.ZUShort>> Objects;
            internal List<ZActor> Spawns, Doors;
            internal List<TileSocket> Sockets;
            internal List<SandboxAuthoringMarker> AuthoringMarkers;
            internal List<RoomPackage> GameplayPackages;
            internal string GeometryFile;
            internal SandboxState(ZScene scene, IEnumerable<SandboxAuthoringMarker> markers)
            {
                GeometryFile = scene.SandboxGeometryFile;
                Actors = scene.Rooms.Select(r => r.ZActors.Select(a => a.Clone()).ToList()).ToList();
                Objects = scene.Rooms.Select(r => r.ZObjects.Select(o => o.Clone()).ToList()).ToList();
                Spawns = scene.SpawnPoints.Select(a => a.Clone()).ToList();
                Doors = scene.Transitions.Select(a => a.Clone()).ToList();
                Sockets = scene.TileSockets.Select(a => a.Clone()).ToList();
                AuthoringMarkers = (markers ?? Enumerable.Empty<SandboxAuthoringMarker>()).Select(a => a.Clone()).ToList();
                GameplayPackages = scene.SandboxGameplayPackages == null ? new List<RoomPackage>() : scene.SandboxGameplayPackages.Select(TileStorage.Clone).ToList();
            }
            internal List<SandboxAuthoringMarker> Apply(ZScene scene)
            {
                for (int i = 0; i < Actors.Count; i++) { scene.Rooms[i].ZActors = Actors[i]; scene.Rooms[i].ZObjects = Objects[i]; }
                scene.SpawnPoints = Spawns; scene.Transitions = Doors;
                scene.TileSockets = Sockets.Select(a => a.Clone()).ToList();
                scene.SandboxGameplayPackages = GameplayPackages == null ? new List<RoomPackage>() : GameplayPackages.Select(TileStorage.Clone).ToList();
                return AuthoringMarkers == null ? new List<SandboxAuthoringMarker>() : AuthoringMarkers.Select(a => a.Clone()).ToList();
            }
        }

        private sealed class SandboxActorItem
        {
            internal ZActor Actor;
            internal string Kind;
            internal string DisplayName;
            public override string ToString() { return DisplayName + " [" + Kind + "]"
                + (Kind == "Door" ? " [" + Actor.FrontSwitchTo + " <-> " + Actor.BackSwitchTo + "]" : ""); }
        }

        private sealed class SandboxLayerNode
        {
            internal int RoomIndex = -1;
            internal SandboxActorItem Actor;
            internal PlacedTile Placement;
            internal SandboxAuthoringMarker Marker;
        }

        private void InitializeSandboxWorkspace()
        {
            if (sandboxRoot != null) return;
            SuspendLayout();
            Panel legacyBackend = new Panel { Visible = false, Name = "LegacyBackendControls" };
            foreach (Control control in Controls.Cast<Control>().ToArray()) legacyBackend.Controls.Add(control);
            Controls.Add(legacyBackend);
            sandboxRoot = new Panel { Dock = DockStyle.Fill, Name = "SandboxWorkspace" };
            Controls.Add(sandboxRoot);
            sandboxRoot.BringToFront();
            MinimumSize = new Size(1050, 650);
            Size = new Size(1380, 900);

            MenuStrip menu = new MenuStrip { Dock = DockStyle.Top };
            ToolStripMenuItem file = new ToolStripMenuItem("Project");
            file.DropDownItems.Add("Open ROM / import library...", null, async delegate
            {
                using (OpenFileDialog picker = new OpenFileDialog { Filter = "OoT ROM (*.z64)|*.z64" })
                    if (picker.ShowDialog(this) == DialogResult.OK)
                        try { await OpenSandboxRomAsync(picker.FileName); }
                        catch (Exception error) { MessageBox.Show(this, error.Message, "ROM library", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            });
            file.DropDownItems.Add("Save scene...", null, delegate { SandboxRun(() => { PersistSandboxGeometry(); saveSceneToolStripMenuItem_Click(this, EventArgs.Empty); }); });
            file.DropDownItems.Add("Browse ROM scenes...", null, delegate { SandboxRun(BrowseSandboxScenes); });
            file.DropDownItems.Add("New empty dungeon workspace...", null, delegate { SandboxRun(CreateEmptyDungeonWorkspace); });
            file.DropDownItems.Add("Add empty dungeon room", null, delegate { SandboxRun(AddEmptyDungeonRoom); });
            file.DropDownItems.Add("Apply latest Scene/Modeler handoff", null, delegate { SandboxRun(ApplyLatestSharedSceneHandoff); });
            file.DropDownItems.Add("Import model as room tile...", null, delegate { SandboxRun(ImportSandboxModelTile); });
            file.DropDownItems.Add("Open saved scene...", null, delegate
            {
                using (OpenFileDialog picker = new OpenFileDialog { Filter = "Scene project (*.xml)|*.xml" })
                    if (picker.ShowDialog(this) == DialogResult.OK && WarningUnsavedChanges(true))
                        SandboxRun(() => OpenSandboxSavedScene(picker.FileName));
            });
            file.DropDownItems.Add("Export native scene package...", null, delegate { SandboxRun(() => { MaterializeSandboxGameplay(); saveBinaryToolStripMenuItem_Click(this, EventArgs.Empty); }); });
            file.DropDownItems.Add("Inject into ROM copy...", null, delegate { SandboxRun(InjectSandboxCopy); });
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add("Configure Project64...", null, delegate { SandboxRun(ConfigureSandboxProject64); });
            file.DropDownItems.Add("Inject and launch in Project64...", null, delegate { SandboxRun(InjectAndLaunchSandboxProject64); });
            menu.Items.Add(file);
            ToolStripMenuItem edit = new ToolStripMenuItem("Edit");
            edit.DropDownItems.Add("Undo placement/edit", null, delegate { SandboxHistory(sandboxUndo, sandboxRedo); });
            edit.DropDownItems.Add("Redo placement/edit", null, delegate { SandboxHistory(sandboxRedo, sandboxUndo); });
            edit.DropDownItems.Add("Replace selected from palette", null, delegate { SandboxRun(ReplaceSelectedSandboxActor); });
            edit.DropDownItems.Add("Configure selected gameplay...", null, delegate { SandboxRun(ConfigureSelectedSandboxGameplay); });
            edit.DropDownItems.Add("Choose dungeon texture pack...", null, delegate { SandboxRun(ShowSandboxTexturePackDialog); });
            edit.DropDownItems.Add("Link selected actors...", null, delegate { SandboxRun(LinkSandboxActors); });
            edit.DropDownItems.Add("Command palette...", null, delegate { ShowSandboxCommandPalette(); });
            edit.DropDownItems.Add("Delete selected", null, delegate { DeleteSandboxActor(); });
            menu.Items.Add(edit);
            ToolStripMenuItem sceneMenu = new ToolStripMenuItem("Scene");
            sceneMenu.DropDownItems.Add("Validate...", null, delegate { ShowSandboxValidation(SceneBuilderValidation.Check(CurrentScene), "Scene validation"); });
            sceneMenu.DropDownItems.Add("Review gameplay and tile assembly...", null, delegate { ShowSandboxValidation(RoomPackageReview.CheckScene(CurrentScene), "Gameplay and tile assembly review"); });
            sceneMenu.DropDownItems.Add("Review item acquisition arc...", null, delegate { SandboxRun(ShowGeneratedItemArcDialog); });
            sceneMenu.DropDownItems.Add("Playtest preflight...", null, delegate { SandboxRun(ShowSandboxPlaytestPreflight); });
            sceneMenu.DropDownItems.Add("Randomize playable dungeon...", null, delegate { SandboxRun(ShowRandomDungeonDialog); });
            sceneMenu.DropDownItems.Add("Focus room", null, delegate { FocusSandboxRoom(); });
            sceneMenu.DropDownItems.Add("Move selected room bundle...", null, delegate { SandboxRun(MoveSelectedSandboxRoom); });
            menu.Items.Add(sceneMenu);
            ToolStripMenuItem viewMenu = new ToolStripMenuItem("View");
            sandboxDarkModeItem = new ToolStripMenuItem("Dark mode") { Checked = true, CheckOnClick = true };
            sandboxDarkModeItem.Click += delegate { sandboxDarkMode = sandboxDarkModeItem.Checked; ApplySandboxTheme(); };
            viewMenu.DropDownItems.Add(sandboxDarkModeItem);
            viewMenu.DropDownItems.Add("Light mode", null, delegate { sandboxDarkMode = false; sandboxDarkModeItem.Checked = false; ApplySandboxTheme(); });
            menu.Items.Add(viewMenu);
            ToolStripMenuItem helpMenu = new ToolStripMenuItem("Help");
            helpMenu.DropDownItems.Add("How to use Archarina64...", null, delegate { ShowSandboxHelp(); });
            menu.Items.Add(helpMenu);

            FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 190, MinimumSize = new Size(0, 190), Padding = new Padding(5), WrapContents = true };
            toolbar.Controls.Add(new Label { Text = "WORKSPACE", AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(0, 5, 8, 0) });
            toolbar.Controls.Add(new Label { Text = "Source room", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            sandboxRooms = new ComboBox { Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
            toolbar.Controls.Add(sandboxRooms);
            sandboxSnap = new CheckBox { Text = "Grid snap", Checked = true, AutoSize = true, Padding = new Padding(10, 3, 0, 0) };
            sandboxGrid = new NumericUpDown { Minimum = 1, Maximum = 4096, Value = 100, Increment = 10, Width = 75 };
            toolbar.Controls.Add(sandboxSnap); toolbar.Controls.Add(sandboxGrid);
            sandboxTwoSided = new CheckBox { Text = "Two-sided preview", Checked = true, AutoSize = true, Padding = new Padding(8, 3, 0, 0) };
            sandboxIsolateRoom = new CheckBox { Text = "Selected room only", Checked = true, AutoSize = true, Padding = new Padding(8, 3, 0, 0) };
            sandboxNativePreview = new CheckBox { Text = "Native display-list preview", Checked = true, AutoSize = true, Padding = new Padding(8, 3, 0, 0) };
            sandboxMoveActor = new CheckBox { Text = "Move actor", AutoSize = true, Padding = new Padding(8, 3, 0, 0) };
            sandboxInteractionMode = new ComboBox { Width = 105, DropDownStyle = ComboBoxStyle.DropDownList };
            sandboxInteractionMode.Items.AddRange(new object[] { "Select", "Place objects / tiles", "Place authoring marker", "Move", "Rotate", "Scale", "Edit" });
            sandboxInteractionMode.SelectedIndex = 0;
            toolbar.Controls.Add(sandboxTwoSided); toolbar.Controls.Add(sandboxIsolateRoom); toolbar.Controls.Add(sandboxNativePreview);
            toolbar.Controls.Add(new Label { Text = "Tools", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
            foreach (string tool in new[] { "Select", "Place", "Marker", "Move", "Rotate", "Scale", "Edit" })
            {
                string mode = tool == "Place" ? "Place objects / tiles" : tool == "Marker" ? "Place authoring marker" : tool;
                toolbar.Controls.Add(CreateSandboxToolButton(tool, mode));
            }
            sandboxMoveRoomButton = new Button { Text = "Move room live", AutoSize = true, Margin = new Padding(8, 3, 0, 0) };
            sandboxMoveRoomButton.Click += delegate
            {
                sandboxRoomMoveArmed = !sandboxRoomMoveArmed;
                sandboxMoveRoomButton.Text = sandboxRoomMoveArmed ? "Stop room move" : "Move room live";
                sandboxStatus.Text = sandboxRoomMoveArmed ? "Select a room, then drag in the viewport to move its full bundle." : "Room move cancelled.";
            };
            toolbar.Controls.Add(sandboxMoveRoomButton);
            Button importModelButton = new Button { Text = "Import model...", AutoSize = true, Margin = new Padding(4, 3, 0, 0) };
            importModelButton.Click += delegate { SandboxRun(ImportSandboxModelTile); };
            toolbar.Controls.Add(importModelButton);
            toolbar.Controls.Add(new Label { Text = "Marker", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
            sandboxMarkerKind = new ComboBox { Width = 125, DropDownStyle = ComboBoxStyle.DropDownList };
            sandboxMarkerKind.Items.AddRange(new object[] { "Entrance", "Exit", "Connector", "Hub", "Puzzle surface", "Player start", "Drop / climb", "No connection" });
            sandboxMarkerKind.SelectedIndex = 0;
            sandboxMarkerFacing = new ComboBox { Width = 58, DropDownStyle = ComboBoxStyle.DropDownList };
            sandboxMarkerFacing.Items.AddRange(new object[] { "0", "90", "180", "270" }); sandboxMarkerFacing.SelectedIndex = 0;
            sandboxPlaceMarkerButton = new Button { Text = "Place marker", AutoSize = true, Margin = new Padding(4, 3, 0, 0) };
            sandboxPlaceMarkerButton.Click += delegate
            {
                SetSandboxInteractionMode("Place authoring marker");
                sandboxStatus.Text = "Place " + sandboxMarkerKind.SelectedItem + " marker in the viewport.";
            };
            toolbar.Controls.Add(sandboxMarkerKind); toolbar.Controls.Add(new Label { Text = "Facing", AutoSize = true, Padding = new Padding(4, 5, 0, 0) });
            toolbar.Controls.Add(sandboxMarkerFacing); toolbar.Controls.Add(sandboxPlaceMarkerButton);
            Button helpButton = new Button { Text = "How to use", AutoSize = true, Margin = new Padding(12, 3, 0, 0) };
            helpButton.Click += delegate { ShowSandboxHelp(); };
            toolbar.Controls.Add(helpButton);
            InitializeSandboxTiles(toolbar);
            settings.OnlyRenderSelectedRoom = true;
            sandboxTwoSided.CheckedChanged += delegate { SandboxRun(ApplySandboxPreview); };
            sandboxIsolateRoom.CheckedChanged += delegate { settings.OnlyRenderSelectedRoom = sandboxIsolateRoom.Checked; glControl1.Invalidate(); };
            sandboxNativePreview.CheckedChanged += delegate { SandboxRun(ApplySandboxNativePreview); };
            sandboxRooms.SelectedIndexChanged += delegate
            {
                if (sandboxRefreshing || sandboxRooms.SelectedIndex < 0) return;
                ClearSandboxFaces(); SelectRoom(sandboxRooms.SelectedIndex); RefreshSandboxContents(); FocusSandboxRoom();
            };
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 460));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 270));
            Panel palette = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
            sandboxSearch = new TextBox { Dock = DockStyle.Top };
            sandboxCategories = new TreeView { Dock = DockStyle.Top, Height = 180, HideSelection = false };
            sandboxPalette = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
            sandboxRoomImages = new ImageList { ImageSize = new Size(150, 96), ColorDepth = ColorDepth.Depth32Bit };
            sandboxRoomDrawer = new ListView { Dock = DockStyle.Fill, View = View.LargeIcon, LargeImageList = sandboxRoomImages, MultiSelect = false, HideSelection = false, FullRowSelect = true, AutoArrange = true, LabelWrap = true };
            sandboxRoomDrawer.Visible = false;
            sandboxRoomDrawer.DoubleClick += delegate
            {
                RomPaletteAsset asset = sandboxRoomDrawer.SelectedItems.Count == 0 ? null : sandboxRoomDrawer.SelectedItems[0].Tag as RomPaletteAsset;
                if (asset != null && asset.Room != null) SandboxRun(() => OpenSandboxRoom(asset.Room));
            };
            sandboxRoomDrawer.ItemDrag += delegate(object sender, ItemDragEventArgs e)
            {
                RomPaletteAsset asset = ((ListViewItem)e.Item).Tag as RomPaletteAsset;
                if (asset != null) sandboxRoomDrawer.DoDragDrop(asset, DragDropEffects.Copy);
            };
            Button openRoom = new Button { Name = "OpenSandboxSourceRoom", Text = "Open source room", Dock = DockStyle.Bottom, Height = 30, Enabled = false };
            sandboxAssetImages = new ImageList { ImageSize = new Size(120, 90), ColorDepth = ColorDepth.Depth32Bit };
            sandboxAssetDrawer = new ListView { Dock = DockStyle.Fill, View = View.LargeIcon, LargeImageList = sandboxAssetImages, MultiSelect = false, HideSelection = false, FullRowSelect = true, AutoArrange = true, LabelWrap = true, Visible = false };
            sandboxAssetDrawer.ItemDrag += delegate(object sender, ItemDragEventArgs e)
            {
                RomPaletteAsset asset = ((ListViewItem)e.Item).Tag as RomPaletteAsset;
                if (asset != null) sandboxAssetDrawer.DoDragDrop(asset, DragDropEffects.Copy);
            };
            sandboxAssetDrawer.SelectedIndexChanged += delegate
            {
                RomPaletteAsset asset = sandboxAssetDrawer.SelectedItems.Count == 0 ? null : sandboxAssetDrawer.SelectedItems[0].Tag as RomPaletteAsset;
                if (asset == null) return;
                int index = sandboxPalette.Items.IndexOf(asset);
                if (index >= 0) sandboxPalette.SelectedIndex = index;
                openRoom.Enabled = asset.Room != null;
            };
            sandboxPalette.SelectedIndexChanged += delegate { openRoom.Enabled = (sandboxPalette.SelectedItem as RomPaletteAsset)?.Room != null; };
            openRoom.Click += delegate
            {
                RomPaletteAsset asset = sandboxPalette.SelectedItem as RomPaletteAsset;
                if (asset != null && asset.Room != null) SandboxRun(() => OpenSandboxRoom(asset.Room));
            };
            Label paletteTitle = new Label { Text = "ROOMS, TILES & ASSETS", Dock = DockStyle.Top, Height = 24, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(0, 3, 0, 0) };
            Label searchTitle = new Label { Text = "Search source assets", Dock = DockStyle.Top, Height = 20, Padding = new Padding(0, 2, 0, 0) };
            Label categoryTitle = new Label { Text = "Browse by location or asset type", Dock = DockStyle.Top, Height = 20, Padding = new Padding(0, 2, 0, 0) };
            palette.Controls.Add(sandboxPalette); palette.Controls.Add(sandboxAssetDrawer); palette.Controls.Add(sandboxRoomDrawer); palette.Controls.Add(sandboxCategories);
            palette.Controls.Add(categoryTitle); palette.Controls.Add(searchTitle); palette.Controls.Add(sandboxSearch); palette.Controls.Add(paletteTitle);
            palette.Controls.Add(openRoom);
            layout.Controls.Add(palette, 0, 0);
            Panel viewport = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            glControl1.Parent = viewport;
            glControl1.Dock = DockStyle.Fill;
            glControl1.Visible = true;
            glControl1.AllowDrop = true;
            layout.Controls.Add(viewport, 1, 0);
            Panel details = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
            sandboxLayers = new TreeView { Dock = DockStyle.Top, Height = 190, HideSelection = false, FullRowSelect = true, BorderStyle = BorderStyle.FixedSingle };
            sandboxContents = new ListBox { Dock = DockStyle.Top, Height = 200, IntegralHeight = false };
            sandboxInspector = new PropertyGrid { Dock = DockStyle.Fill, HelpVisible = true, ToolbarVisible = false };
            Label layersTitle = new Label { Text = "LAYERS: ROOM HIERARCHY", Dock = DockStyle.Top, Height = 24, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(0, 3, 0, 0) };
            Label contentsTitle = new Label { Text = "SCENE CONTENT: ACTORS, SPAWNS & DOORS", Dock = DockStyle.Top, Height = 24, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(0, 3, 0, 0) };
            Label summaryTitle = new Label { Text = "Selected item overview", Dock = DockStyle.Top, Height = 20, Padding = new Padding(0, 2, 0, 0) };
            sandboxSelectionSummary = new Label { Text = "Select an actor in the list or click a marker in the viewport.", Dock = DockStyle.Top, Height = 92, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(5), AutoEllipsis = false };
            Label inspectorTitle = new Label { Text = "Selected item properties", Dock = DockStyle.Top, Height = 20, Padding = new Padding(0, 2, 0, 0) };
            details.Controls.Add(sandboxInspector); details.Controls.Add(inspectorTitle); details.Controls.Add(sandboxSelectionSummary); details.Controls.Add(summaryTitle); details.Controls.Add(sandboxContents); details.Controls.Add(contentsTitle); details.Controls.Add(sandboxLayers); details.Controls.Add(layersTitle);
            layout.Controls.Add(details, 2, 0);
            const float paletteWidth = 460;
            const float detailsWidth = 270;
            Button paletteToggle = new Button { Text = "Assets", AutoSize = true, Margin = new Padding(12, 3, 0, 0) };
            Button detailsToggle = new Button { Text = "Inspector", AutoSize = true, Margin = new Padding(4, 3, 0, 0) };
            Button focusViewport = new Button { Text = "Focus viewport", AutoSize = true, Margin = new Padding(4, 3, 0, 0) };
            paletteToggle.Click += delegate
            {
                palette.Visible = !palette.Visible; layout.ColumnStyles[0].Width = palette.Visible ? paletteWidth : 0;
                paletteToggle.Text = palette.Visible ? "Assets" : "Show assets";
            };
            detailsToggle.Click += delegate
            {
                details.Visible = !details.Visible; layout.ColumnStyles[2].Width = details.Visible ? detailsWidth : 0;
                detailsToggle.Text = details.Visible ? "Inspector" : "Show inspector";
            };
            focusViewport.Click += delegate
            {
                bool focused = palette.Visible || details.Visible;
                palette.Visible = !focused; details.Visible = !focused;
                layout.ColumnStyles[0].Width = focused ? 0 : paletteWidth;
                layout.ColumnStyles[2].Width = focused ? 0 : detailsWidth;
                paletteToggle.Text = focused ? "Show assets" : "Assets";
                detailsToggle.Text = focused ? "Show inspector" : "Inspector";
                focusViewport.Text = focused ? "Restore drawers" : "Focus viewport";
            };
            toolbar.Controls.Add(paletteToggle); toolbar.Controls.Add(detailsToggle); toolbar.Controls.Add(focusViewport);
            StatusStrip status = new StatusStrip();
            sandboxStatus = new ToolStripStatusLabel("No ROM loaded"); status.Items.Add(sandboxStatus);
            sandboxRoot.Controls.Add(layout); sandboxRoot.Controls.Add(toolbar); sandboxRoot.Controls.Add(menu); sandboxRoot.Controls.Add(status);
            MainMenuStrip = menu;
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.K && sandboxRoot != null && sandboxRoot.Visible)
                {
                    e.SuppressKeyPress = true;
                    ShowSandboxCommandPalette();
                }
                if (SandboxTextInputFocused() || e.Control || e.Alt || e.KeyCode == Keys.K) return;
                if (e.KeyCode == Keys.V) SetSandboxInteractionMode("Select");
                else if (e.KeyCode == Keys.P) SetSandboxInteractionMode("Place objects / tiles");
                else if (e.KeyCode == Keys.M) SetSandboxInteractionMode("Place authoring marker");
                else if (e.KeyCode == Keys.G) SetSandboxInteractionMode("Move");
                else if (e.KeyCode == Keys.R) SetSandboxInteractionMode("Rotate");
                else if (e.KeyCode == Keys.S) SetSandboxInteractionMode("Scale");
                else return;
                e.SuppressKeyPress = true;
            };
            ApplySandboxTheme();
            sandboxSearch.TextChanged += delegate { RefreshSandboxPalette(); };
            sandboxCategories.AfterSelect += delegate { RefreshSandboxPalette(); };
            sandboxPalette.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                int index = sandboxPalette.IndexFromPoint(e.Location);
                if (e.Button != MouseButtons.Left || index < 0) return;
                sandboxPalette.SelectedIndex = index;
                sandboxDragStart = e.Location;
                sandboxDragAsset = sandboxPalette.SelectedItem as RomPaletteAsset;
            };
            sandboxPalette.MouseUp += delegate { sandboxDragAsset = null; };
            sandboxPalette.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || sandboxDragAsset == null || sandboxDragAsset.Room != null) return;
                Size threshold = SystemInformation.DragSize;
                Rectangle area = new Rectangle(sandboxDragStart.X - threshold.Width / 2, sandboxDragStart.Y - threshold.Height / 2, threshold.Width, threshold.Height);
                if (area.Contains(e.Location)) return;
                RomPaletteAsset asset = sandboxDragAsset;
                sandboxDragAsset = null;
                sandboxPalette.DoDragDrop(asset, DragDropEffects.Copy);
            };
            sandboxPalette.DoubleClick += delegate
            {
                RomPaletteAsset asset = sandboxPalette.SelectedItem as RomPaletteAsset;
                if (asset != null && asset.Room != null) SandboxRun(() => OpenSandboxRoom(asset.Room));
            };
            glControl1.DragEnter += delegate(object sender, DragEventArgs e)
            { e.Effect = e.Data.GetDataPresent(typeof(RomPaletteAsset)) && CurrentScene != null && CurrentScene.Rooms.Count > 0 ? DragDropEffects.Copy : DragDropEffects.None; };
            glControl1.DragDrop += delegate(object sender, DragEventArgs e)
            {
                RomPaletteAsset asset = e.Data.GetData(typeof(RomPaletteAsset)) as RomPaletteAsset;
                SandboxRun(() => PlaceSandboxAsset(asset, SandboxDropPosition(glControl1.PointToClient(new Point(e.X, e.Y)))));
            };
            glControl1.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right)
                {
                    PlacedTile pickedTile = PickSandboxTile(e.Location);
                    ZActor picked = PickSandboxActor(e.Location);
                    if (picked != null) { SelectSandboxActor(picked); ShowSandboxActorQuickMenu(glControl1.PointToScreen(e.Location)); }
                    else if (pickedTile != null) { SelectSandboxTile(pickedTile); ShowSandboxTileQuickMenu(glControl1.PointToScreen(e.Location)); }
                    return;
                }
                if (e.Button != MouseButtons.Left) return;
                if (sandboxRoomMoveArmed && BeginSandboxRoomMove(e.Location)) return;
                if (SandboxModeIs("Place authoring marker")) { SandboxRun(() => PlaceSandboxAuthoringMarker(e.Location)); return; }
                if (SandboxModeIs("Move") && BeginSandboxTileMove(e.Location)) return;
                if (SandboxModeIs("Rotate")) { SandboxRun(() => RotateSandboxTileAt(e.Location)); return; }
                if (SandboxModeIs("Scale") && SelectSandboxTileForScaling(e.Location)) return;
                ZActor actor = SelectedSandboxActor();
                ZActor pickedActor = PickSandboxActor(e.Location);
                if (pickedActor != null)
                {
                    SelectSandboxActor(pickedActor);
                    actor = pickedActor;
                }
                else if (SandboxModeIs("Select"))
                {
                    PlacedTile pickedTile = PickSandboxTile(e.Location);
                    if (pickedTile != null) { SelectSandboxTile(pickedTile); return; }
                }
                if (!SandboxModeIs("Move") && (sandboxMoveActor == null || !sandboxMoveActor.Checked)) return;
                if (actor == null) return;
                SandboxRecord(); sandboxMovingActor = actor; sandboxStatus.Text = "Dragging " + actor.Number.ToString("X4") + " | release to place";
            };
            glControl1.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (sandboxMovingRoom && e.Button == MouseButtons.Left) { MoveSandboxRoomToSurface(e.Location); return; }
                if (sandboxMovingTile && e.Button == MouseButtons.Left) { SandboxRun(() => MoveSandboxTileToSurface(e.Location)); return; }
                if (sandboxMovingActor == null || e.Button != MouseButtons.Left) return;
                SandboxRun(() => MoveSandboxActorToSurface(e.Location));
            };
            glControl1.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (sandboxMovingRoom && e.Button == MouseButtons.Left)
                {
                    SandboxRun(CommitSandboxRoomMove);
                    sandboxRoomMoveArmed = false;
                    if (sandboxMoveRoomButton != null) sandboxMoveRoomButton.Text = "Move room live";
                    RefreshSandboxContents(); RefreshSandboxLayers(); FocusSandboxRoom();
                    sandboxStatus.Text = "Committed room move."; glControl1.Invalidate(); return;
                }
                if (sandboxMovingTile && e.Button == MouseButtons.Left)
                {
                    sandboxMovingTile = false; savechanges = true; sandboxStatus.Text = "Moved tile: " + (sandboxSelectedTile == null ? "" : sandboxSelectedTile.Name); glControl1.Invalidate(); return;
                }
                if (sandboxMovingActor == null || e.Button != MouseButtons.Left) return;
                ZActor actor = sandboxMovingActor; sandboxMovingActor = null; SyncSandboxGameplayActor(actor);
                savechanges = true; RefreshSandboxContents();
                sandboxContents.SelectedItem = sandboxContents.Items.Cast<SandboxActorItem>().FirstOrDefault(item => ReferenceEquals(item.Actor, actor));
                sandboxStatus.Text = "Moved actor " + actor.Number.ToString("X4"); glControl1.Invalidate();
            };
            glControl1.MouseWheel += delegate(object sender, MouseEventArgs e)
            {
                if (!SandboxModeIs("Scale") || sandboxSelectedTile == null) return;
                SandboxRun(() => ScaleSelectedSandboxTile(e.Delta > 0 ? 1.1 : 1.0 / 1.1));
            };
            sandboxContents.SelectedIndexChanged += delegate
            {
                SandboxActorItem item = sandboxContents.SelectedItem as SandboxActorItem;
                sandboxInspector.SelectedObject = item == null ? null : item.Actor;
                UpdateSandboxSelectionSummary(item == null ? null : item.Actor, item == null ? null : item.Kind);
            };
            sandboxLayers.AfterSelect += delegate(object sender, TreeViewEventArgs e)
            {
                SandboxLayerNode layer = e.Node == null ? null : e.Node.Tag as SandboxLayerNode;
                if (layer == null) return;
                if (layer.Actor != null) SelectSandboxActor(layer.Actor.Actor);
                else if (layer.Placement != null) SelectSandboxTile(layer.Placement);
                else if (layer.Marker != null) SelectSandboxAuthoringMarker(layer.Marker);
                else if (layer.RoomIndex >= 0 && layer.RoomIndex < CurrentScene.Rooms.Count && RoomList.SelectedIndex != layer.RoomIndex) SelectRoom(layer.RoomIndex);
            };
            sandboxLayers.NodeMouseClick += delegate(object sender, TreeNodeMouseClickEventArgs e)
            {
                if (e.Button != MouseButtons.Right) return;
                SandboxLayerNode layer = e.Node == null ? null : e.Node.Tag as SandboxLayerNode;
                if (layer == null || layer.RoomIndex < 0) return;
                e.Node.TreeView.SelectedNode = e.Node;
                SelectRoom(layer.RoomIndex);
                ContextMenuStrip layerMenu = new ContextMenuStrip();
                if (layer.Actor != null)
                {
                    AddSandboxActorRoomLinkMenu(layerMenu, layer.Actor.Actor);
                    layerMenu.Items.Add(new ToolStripSeparator());
                    layerMenu.Items.Add("Focus room", null, delegate { FocusSandboxRoom(); });
                }
                else if (layer.Marker != null)
                {
                    sandboxSelectedMarker = layer.Marker;
                    layerMenu.Items.Add("Delete authoring marker", null, delegate { SandboxRun(() => DeleteSandboxAuthoringMarker(layer.Marker)); });
                    layerMenu.Items.Add("Focus room", null, delegate { FocusSandboxRoom(); });
                }
                else if (layer.Placement != null)
                {
                    sandboxSelectedTile = layer.Placement;
                    layerMenu.Items.Add("Move room tile", null, delegate { SetSandboxInteractionMode("Move"); });
                    layerMenu.Items.Add("Delete room tile", null, delegate { SandboxRun(DeleteSelectedSandboxTile); });
                }
                else
                {
                    ZActor selectedActor = SelectedSandboxActor();
                    if (selectedActor != null)
                    {
                        layerMenu.Items.Add("Link selected actor here", null, delegate { SandboxRun(() => AttachSandboxActorToRoom(selectedActor, layer.RoomIndex)); });
                        layerMenu.Items.Add(new ToolStripSeparator());
                    }
                    layerMenu.Items.Add("Move room bundle...", null, delegate { SandboxRun(MoveSelectedSandboxRoom); });
                    layerMenu.Items.Add("Move room live", null, delegate
                    {
                        RoomList.SelectedIndex = layer.RoomIndex;
                        if (sandboxMoveRoomButton != null) sandboxMoveRoomButton.PerformClick();
                    });
                    layerMenu.Items.Add("Focus room", null, delegate { FocusSandboxRoom(); });
                }
                layerMenu.Show(sandboxLayers, e.Location);
            };
            sandboxInteractionMode.SelectedIndexChanged += delegate
            {
                SyncSandboxToolButtons();
                if (sandboxStatus != null) sandboxStatus.Text = "Mode: " + sandboxInteractionMode.SelectedItem + ". Left-click selects; right-click opens actions.";
            };
            sandboxContents.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Right) return;
                int index = sandboxContents.IndexFromPoint(e.Location);
                if (index >= 0 && index < sandboxContents.Items.Count)
                {
                    sandboxContents.SelectedIndex = index;
                    ShowSandboxActorQuickMenu(sandboxContents.PointToScreen(e.Location));
                }
            };
            sandboxInspector.PropertyValueChanged += delegate(object sender, PropertyValueChangedEventArgs e)
            {
                object actor = sandboxInspector.SelectedObject;
                var descriptor = e.ChangedItem.PropertyDescriptor;
                object next = descriptor.GetValue(actor);
                descriptor.SetValue(actor, e.OldValue);
                SandboxRecord();
                descriptor.SetValue(actor, next);
                SyncSandboxGameplayActor(actor as ZActor);
                if (actor is SandboxAuthoringMarker)
                {
                    SandboxAuthoringMarker marker = (SandboxAuthoringMarker)actor;
                    marker.Validate();
                    PersistSandboxGeometry();
                    RefreshSandboxLayers();
                    SelectSandboxAuthoringMarker(marker);
                }
                savechanges = true;
                int selected = sandboxContents.SelectedIndex;
                RefreshSandboxContents();
                if (selected >= 0 && selected < sandboxContents.Items.Count) sandboxContents.SelectedIndex = selected;
                glControl1.Invalidate();
            };
            ResumeLayout(true);
            RefreshSandboxRooms();
        }

        private ZActor sandboxMovingActor;

        private void ApplySandboxTheme()
        {
            if (sandboxRoot == null) return;
            Color background = sandboxDarkMode ? Color.FromArgb(24, 27, 31) : SystemColors.Control;
            Color surface = sandboxDarkMode ? Color.FromArgb(31, 35, 40) : SystemColors.Window;
            Color input = sandboxDarkMode ? Color.FromArgb(42, 47, 53) : SystemColors.Window;
            Color foreground = sandboxDarkMode ? Color.FromArgb(232, 236, 240) : SystemColors.ControlText;
            ApplySandboxTheme(sandboxRoot, background, surface, input, foreground);
            if (glControl1 != null) glControl1.BackColor = Color.Black;
        }

        private static void ApplySandboxTheme(Control control, Color background, Color surface, Color input, Color foreground)
        {
            if (control is OpenTK.GLControl) return;
            bool editorInput = control is TextBox || control is ComboBox || control is ListBox || control is TreeView || control is PropertyGrid;
            control.BackColor = editorInput ? input : background;
            control.ForeColor = foreground;
            if (control is Button)
            {
                control.BackColor = surface;
                ((Button)control).FlatStyle = FlatStyle.System;
            }
            foreach (Control child in control.Controls)
                ApplySandboxTheme(child, background, surface, input, foreground);
        }

        private void ShowSandboxHelp()
        {
            using (Form dialog = new Form { Text = "Archarina64 Help and Contents", ClientSize = new Size(860, 650), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false })
            {
                TabControl tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 4) };
                tabs.TabPages.Add(CreateSandboxHelpPage("Contents", "Every screen and control", 
                    "MAIN WORKSPACE\n" +
                    "The main editor screen is divided into four areas:\n" +
                    "- Top menus and toolbar: project commands, room selection, preview options, editing modes, extraction, snapping, and help.\n" +
                    "- Left drawer: a widened visual browser for source-room locations, extracted room tiles, actors, objects, enemies, items, NPCs, props, spawns, doors, connections, and object banks. Only the selected drawer is shown at a time.\n" +
                     "- Center viewport: the 3D scene. Drag assets here to place them; select and move actors here when Move actor is enabled.\n" +
                    "- Right panel: the current room's actors, spawns, doors, and selected-item properties.\n\n" +
                    "SCREENS AND DIALOGS\n" +
                    "- ROM import: choose the external decompressed big-endian OoT ROM and build its local asset catalog.\n" +
                    "- ROM scenes and rooms: browse original scenes by named location, expand a scene, and open a room.\n" +
                    "- Room extraction progress: watch the selected-scene extraction pass and thumbnail generation.\n" +
                    "- Room connection: choose front room, back room, and facing for a placed transition.\n" +
                    "- Configure gameplay actor: author the user-facing role, requirements, rewards/items, and native flags for a selected ROM-compatible actor.\n" +
                    "- Link gameplay actors: connect a cause actor to an effect actor without code.\n" +
                    "- Tile sockets: edit doorway and connection sockets on a saved tile.\n" +
                    "- Box selection: select visual and/or collision faces inside coordinate bounds.\n" +
                    "- Validation and gameplay review: inspect missing collision, objects, transitions, logic links, and export warnings.\n" +
                    "- Save/export/inject: save a project, export native scene data, or inject into a ROM copy.\n\n" +
                    "MENUS\n" +
                    "Project opens/imports ROMs, saves projects, browses ROM scenes, opens saved scenes, exports native packages, and injects into a ROM copy.\n" +
                    "Edit undoes and redoes changes, replaces or deletes the selected actor, and opens the gameplay-link editor.\n" +
                    "Scene validates the current scene, reviews gameplay/tile assembly, and focuses the active room.\n" +
                    "View switches between dark and light mode.\n" +
                    "Help opens this reference."));
                tabs.TabPages.Add(CreateSandboxHelpPage("Workflow", "The shortest complete path", 
                    "1. IMPORT\nChoose Project > Open ROM / import library. The ROM stays external. Archarina64 stores extracted metadata, tiles, thumbnails, and scene projects locally.\n\n" +
                    "2. FIND ROOMS\nChoose Project > Browse ROM scenes to inspect the original scene and room list. After extraction, choose a location in the left drawer to browse a compact two-column room thumbnail grid. Double-click a room to open it, or drag a room tile into the viewport.\n\n" +
                    "3. BUILD THE LAYOUT\nUse Grid snap for repeatable placement. Turn on Magnetic snap and Room door anchors to line up compatible sockets and existing doors. Use Auto-chop entrances to split an existing room into doorway-aware tiles.\n\n" +
                    "4. PLACE GAMEPLAY\nChoose Items, Enemies, NPCs, Props, Spawns, Doors, or Connections in the left drawer. Their visual asset grid shows a compact thumbnail for each entry. Drag an entry into the viewport. Select it on the right to edit its transform and properties.\n\n" +
                    "5. AUTHOR GAMEPLAY\nSelect an actor, open Edit > Configure selected gameplay, and enter its plain-language role, requirement, required items, rewards/items, and flag rows. Use existing extracted ROM actors or palette assets so object banks and variable semantics stay valid.\n\n" +
                    "6. LINK CAUSE AND EFFECT\nChoose Edit > Link selected actors. Pick the source cause, target result, and action such as Opens, Unlocks, Spawns, CompletesPuzzle, SetsFlag, or StartsCutscene.\n\n" +
                    "7. CHECK AND EXPORT\nRun Scene > Validate and Scene > Review gameplay and tile assembly. Save the project, export the native scene package, and inject only into a ROM copy when ready."));
                tabs.TabPages.Add(CreateSandboxHelpPage("Controls", "Toolbar, drawers, viewport, and inspector", 
                    "TOP TOOLBAR\n" +
                    "- WORKSPACE / Source room: choose the currently open room.\n" +
                    "- Grid snap: enable or disable placement quantization.\n" +
                    "- Grid value: set the snap spacing in N64 world units.\n" +
                    "- Two-sided preview: show both sides of preview geometry.\n" +
                    "- Selected room only: hide other rooms in the viewport.\n" +
                    "- Native display-list preview: toggle native scene rendering.\n" +
                     "- Move actor: drag the selected actor across the room surface.\n" +
                     "- Move room live: select a room from Source room or Layers, press this button, and drag in the viewport. Visuals, collision, actors, spawns, sockets, and gameplay move together; release commits the tile placement offsets.\n" +
                     "- Import model: choose an OBJ or DAE and save it as a reusable room tile. Use Tile sockets to author doorway sockets when the model needs snap connections.\n" +
                    "- How to use: open this Help and Contents window.\n\n" +
                    "BUILD / EXTRACT\n" +
                    "- Place: normal drag-and-drop placement mode.\n" +
                    "- Visual faces: select faces from the visible room mesh.\n" +
                    "- Collision faces: select collision faces for a tile.\n" +
                    "- Clear selection: remove current face selections.\n" +
                    "- Save tile: save the selected visual/collision region as a reusable tile.\n" +
                    "- Auto-chop entrances: analyze a room and create doorway-aware tiles.\n" +
                    "- Extract room tiles: choose the current loaded scene, any checked scenes, or all scenes, then create the organized room library and thumbnails.\n" +
                    "- Generate custom tile kit: create permanent grid-aligned square, circular, tube, elbow, and hub tiles with OoT-sized doorway sockets.\n" +
                    "- Texture pack: choose a saved dungeon tile as the source for floor, ceiling, and wall materials at any time. Special materials such as vines remain unchanged.\n" +
                    "- Analyze / generate: analyze the current scene and create a procedural plan.\n" +
                    "- Randomize playable dungeon: generate an editable multi-room dungeon from extracted rooms or the generated custom kit. This is also available under Scene > Randomize playable dungeon.\n" +
                    "- Require a mid-dungeon item arc: keep this enabled for a complete dungeon. The generator must place a progression item after the start and mark later gates as consuming it. Disable it only for a deliberately partial or combat-only test layout.\n" +
                    "- Minimum item arcs: request one or two distinct progression-item acquisitions. Request two when the ROM palette exposes multiple compatible dungeon items; complete generation stops if the requested count cannot be satisfied.\n" +
                    "- Analyze ROM: analyze decoded ROM scenes, dungeon topology, progression roles, transitions, object dependencies, flags, requirements, and rewards.\n" +
                    "- Starting items: comma-separated items available before procedural generation.\n" +
                    "- Rotation: rotate a placed tile by 0, 90, 180, or 270 degrees.\n" +
                    "- Surface: choose the destination collision surface mapping.\n\n" +
                    "SNAP / CONNECT\n" +
                    "- Magnetic snap: align compatible sockets while dragging a tile.\n" +
                    "- Radius: maximum distance for magnetic matching.\n" +
                    "- Room door anchors: expose original door transitions as snap targets.\n" +
                    "- Select box: select faces within coordinate bounds.\n" +
                    "- Select connected: grow the current face selection through connected geometry.\n" +
                    "- Tile sockets: edit a saved tile's doorway/socket metadata.\n\n" +
                    "LEFT DRAWER\n" +
                    "Search filters the selected location or asset category. A named ROM location opens the room drawer: rooms appear as a compact two-column grid with cached unlit thumbnails. Tiles opens saved tile cards with mesh previews. Actor categories open the visual actor drawer with compact thumbnails. Drag any card into the viewport. Double-clicking a source-room card opens that room; Open source room does the same for the selected room.\n\n" +
                    "RIGHT PANEL\n" +
                    "Scene content lists actors, spawns, and doors. Select one to show editable properties below. Edit properties directly; changes are recorded for undo and synchronized with gameplay metadata. Right-click an actor for compatible links, gameplay metadata, palette replacement, and deletion.\n\n" +
                    "GAMEPLAY AUTHORING\n" +
                    "Configure selected gameplay creates or updates the selected actor's native gameplay package. Name is the plain-language label shown to users. Role is used by the dungeon generator to recognize NPCs, enemies, rewards, puzzles, obstacles, doors, and items. Requirements describe the intended condition. Required items are comma-separated prerequisites. Granted rewards/items are comma-separated inventory results.\n\n" +
                    "FLAGS AND SWITCHES\n" +
                    "Each flag row contains a native Kind, source Property label, storage Target such as Var or a rotation field, Value, bit Mask, and Bit position. Sets marks a property as a flag writer; Checks marks it as a reader. The logic materializer uses these rows to allocate or synchronize compatible flags when rooms are rearranged."));
                tabs.TabPages.Add(CreateSandboxHelpPage("Reference", "Links, files, and troubleshooting", 
                    "GAMEPLAY LINKS\n" +
                    "Use the link editor for cause/effect behavior: a switch or pressure plate can open a door; defeating enemies can unlock a chest; an item can enable a path; a trigger can start a cutscene; a puzzle can spawn an actor. Source is the cause, target is the result, Action describes the relationship, and Parameter stores an optional detail.\n\n" +
                    "LOCAL OUTPUT\n" +
                    "ROM data is never packaged into the repository or executable. Extracted thumbnails, tile payloads, analysis reports, and scene projects are stored in the local Archarina64 workspace.\n\n" +
                    "WHEN SOMETHING IS MISSING\n" +
                    "If an actor is invisible, check Native display-list preview and Selected room only. If room cards have no thumbnails, make sure extraction has finished, select a named location category, and confirm the local room-thumbnail cache exists for the imported ROM. If a tile card has only a fallback preview, its mesh could not be loaded; check the tile source path and ROM fingerprint. If a tile will not place, check that the destination has collision and that the source object banks exist in the imported ROM. If a door will not align, enable Magnetic snap and Room door anchors, then inspect Tile sockets. If a puzzle is incomplete, run the gameplay review and add the suggested actor or logic link.\n\n" +
                    "BEFORE INJECTION\n" +
                    "Run validation, review warnings, save the scene project, export the native package, and inject into a copy of the ROM. Keep the original ROM unchanged."));
                tabs.TabPages.Add(CreateSandboxHelpPage("First session", "A complete first-time walkthrough",
                    "FIRST LAUNCH\n\n" +
                    "1. Start Archarina64 from the normal Debug build or your installed shortcut. The editor opens to the sandbox workspace. The original Sharp Ocarina controls remain available through the command palette, but the sandbox is the recommended starting point.\n\n" +
                    "2. Choose Project > Open ROM / import library. Select a decompressed, big-endian OoT ROM. Archarina64 reads the ROM at runtime; it does not package ROM data into the repository or executable.\n\n" +
                    "3. Wait for the catalog to finish. The catalog contains source scenes, rooms, actors, object banks, transitions, thumbnails, collision information, and metadata-backed logic hints. Extraction output is stored in the private local Archarina64 workspace.\n\n" +
                    "4. Choose Project > Browse ROM scenes. Expand a named location and open a source room. This is for inspection and extraction; it is not the final dungeon workspace.\n\n" +
                    "5. Choose Extract room tiles. Start with Current loaded scene or a small checked scene list. Extracting every ROM room is possible, but it is slow and unnecessary for a first project.\n\n" +
                    "6. Choose Project > New empty dungeon workspace. This creates a blank authoring scene with no source-room geometry. Use Project > Add empty dungeon room for each additional native room you need.\n\n" +
                    "7. Select a destination room in the Source room dropdown. Browse a location in the left drawer, select a room card marked Tile ready when available, and drag it into the viewport. For saved custom tiles, select Tiles; for actors, select an actor category and drag an actor thumbnail into the viewport.\n\n" +
                    "8. Use Select to inspect content, Move to drag tiles or actors, Rotate to turn a selected tile 90 degrees, and Scale to select a tile then use the mouse wheel. Place adds assets, Marker adds app-only snap guides, and Edit opens property editing. Shortcuts: V Select, G Move, R Rotate, S Scale, P Place, M Marker.\n\n" +
                    "9. Add a player spawn, doors, enemies, rewards, and puzzle actors. Use right-click quick actions or Edit > Link selected actors to create cause-and-effect logic without code.\n\n" +
                    "10. For a one-click automatic dungeon, choose Generate custom tile kit... and save about 50 permanent tiles. Then choose Randomize playable dungeon... and enable Use generated custom tile kit only. The generator uses grid-aligned square, circular, tube, elbow, and hub pieces with OoT-sized doorway sockets, then automatically adds a player spawn, combat encounters, a finale enemy, reward chests, a puzzle switch, native flags, object banks, room transitions, and supported same-room logic links.\n\n" +
                    "11. Alternatively, leave custom-kit-only disabled to randomize from extracted room tiles. Extracted rooms retain their native collision and gameplay manifests where available. Use Analyze ROM first when you want to study how original dungeons divide hubs, combat spaces, reward gates, keys, switches, transitions, and finale rooms.\n\n" +
                    "12. At any time, choose Edit > Choose dungeon texture pack. Select a saved room or tile from another dungeon. Archarina64 applies its dominant floor, ceiling, and wall materials to the current dungeon and keeps special materials such as vines, foliage, water, lava, doors, chests, torches, ice, and props unchanged. Choose Keep source materials to clear the override.\n\n" +
                    "13. Run Scene > Validate and Scene > Review gameplay and tile assembly. Save the editable project before exporting.\n\n" +
                    "14. Choose Project > Export native scene package for a project package, or Project > Inject into ROM copy for a playable test ROM. Always inject into a copy.\n\n" +
                    "15. For the shortest playtest loop, choose Project > Configure Project64 once, then Project > Inject and launch in Project64. Archarina64 creates a separate injected .z64 copy and passes that copy to Project64.\n\n" +
                    "16. Archarina64 previews native geometry and scene data, but it does not emulate the game runtime itself. Project64 supplies the external gameplay runtime."));
                tabs.TabPages.Add(CreateSandboxHelpPage("Rooms and tiles", "Extracting, preparing, placing, and moving geometry",
                    "SOURCE ROOMS\n\n" +
                    "A source room is decoded from the imported ROM. Open it from Project > Browse ROM scenes or by double-clicking a room thumbnail. Source rooms are useful for understanding the original layout, locating entrances, identifying actors, and creating tiles. The room drawer is widened and uses a compact two-column card grid so more rooms remain visible while browsing.\n\n" +
                    "RECOMMENDED ROOMS\n\n" +
                    "The Recommended rooms category scans extracted room tiles from every scene, not only the currently selected dungeon. It ranks candidates using preserved gameplay manifests: door sockets, enemies, rewards, spawns, and boss-like actors. It tries to fill missing dungeon roles such as a hub, combat room, reward room, connector, or finale. Recommendations are suggestions, not restrictions; browse the original location categories when you want a specific scene or theme.\n\n" +
                    "EXTRACT ROOM TILES\n\n" +
                    "Extract room tiles creates reusable room assets with thumbnails. Use the current scene for a quick test, selected scenes for a dungeon-sized library, or all scenes only when you need a complete catalog. Each room is sorted by the original scene location. Thumbnails are cached locally and reloaded by ROM fingerprint when the app starts again.\n\n" +
                    "CUSTOM TILE KIT AND RANDOMIZATION\n\n" +
                    "Generate custom tile kit creates permanent SectionTile assets in the local Archarina64 tile library. The kit is organized around snap-sized footprints instead of arbitrary room bounds: 1x1, 2x2, 4x4, 1x2, 1x3, 1x4, 2x4, and related variants. It includes four-way hubs, two-way rooms, dead ends, circular rooms, straight and elbow tube corridors, platform/ramp rooms, spiral stair rooms, fall-shaft rooms intended for vine-climb setup, push-block maze rooms, and Hookshot-gap rooms. Push-block rooms carry a deterministic state model that is checked for a valid solution before the tile is offered to the generator. Every doorway is placed on a tile perimeter endpoint and uses the OoT doorway socket contract: width 100, height 180, outward-facing sockets, and a 100-unit grid. Corridors are slightly wider and taller than doorways for camera clearance, but they remain traversable horizontal geometry.\n\n" +
                    "Randomize playable dungeon can use the generated custom tile kit only, extracted rooms only, or the combined library. In custom-kit mode it creates a complete native gameplay starting point in one click: player spawn, room-to-room transitions, enemy encounters, a finale enemy, reward chests, a puzzle switch/reward pair, native object banks, flag values, and compatible logic links are generated from the imported ROM actor library. Extracted rooms can contribute preserved native gameplay manifests where available. During ROM import, Archarina64 also scans for the native Hookshot movement signature and records derived Hookshot/Longshot limits in the generated plan. Patched or alternate builds fall back to conservative limits and can be refined with runtime traces. Use the no-code palette and link editor afterward to customize or expand the generated design.\n\n" +
                    "ADVANCED DUNGEON ANALYSIS\n\n" +
                    "Analyze ROM examines original scenes and room packages for topology, doorway transitions, room roles, spawns, enemies, NPCs, rewards, requirements, granted items, flags, object dependencies, and progression links. The progression solver uses those observations to score starting rooms, hubs, combat spaces, puzzle rooms, reward gates, connectors, and finale rooms. This analysis informs recommendations and randomization; it does not rewrite the original ROM scenes.\n\n" +
                    "TRAVERSAL AND PUZZLE INTENT\n\n" +
                    "The analyzer now records movement affordances separately from ordinary room geometry. It recognizes native Hookshotable collision surfaces, climbing/crawling flags, steep surfaces, Epona-blocking surfaces, and authored evidence for jump gaps, platform spacing, vines, climb walls, Hookshot targets, stairs, ramps, fall shafts, and movable blocks. A room can therefore be a puzzle room because of how the player crosses it, not only because it contains a switch.\n\n" +
                    "Generated tiles carry traversal contracts such as JumpGap, VineClimb, Hookshot, PushBlock, Stairs, FallDrop, and Walk. Each contract records its intended endpoints, conservative maximum travel distance, required item when known, evidence, and confidence. On ROM import, the scanner can also pair jump velocity, gravity, and run-speed constants to calculate a collision-authoring envelope; if that pairing is not confident, the generator keeps its conservative fallback. The validator classifies imported collision groups, uses AABBs as a broad phase, checks the actual source triangles, and samples player-clearance distance along the route. This means a distance-valid jump can still fail because its swept path intersects a real wall or passes too close to one, while launch and landing surfaces remain valid endpoints. These are authoring constraints for the PCG planner, not claims that one number describes every OoT movement situation. The generator warns when a route needs an item that is not in the starting inventory or earned earlier in the generated route.\n\n" +
                    "When studying an original dungeon, compare the traversal evidence with the actual playthrough route: a gap should be reachable by the current jump movement; a vine should be placed where the player can see and reach it; a Hookshot target should be within the intended tool range and should lead to a useful landing; a fall should have a recovery route or be an intentional one-way progression; and a push-block arrangement should have a solvable state sequence rather than decorative blocks. Native collision flags remain attached to collision surfaces during remapping, while vines, doors, torches, chests, water, lava, foliage, and other special materials are not overwritten by a wall/floor/ceiling texture theme.\n\n" +
                    "WATER AND EQUIPMENT MECHANICS\n\n" +
                    "Waterboxes are analyzed as gameplay volumes, not decoration. Archarina64 records each water surface, estimated depth, room ownership, camera, environment, and distinct surface level. Deep-water rooms receive explicit underwater and sinking-route contracts. The progression planner can require Zora Tunic for sustained underwater breathing and Iron Boots for sinking and walking along the floor, while ordinary swimming remains a separate capability. Multiple surface levels are preserved so Water Temple-style layouts can be recognized as stateful vertical dungeons: the player may raise or lower water, change boots repeatedly, return through mid-height passages, and revisit rooms under a different water state. Tunics and boots are therefore progression inventory, not cosmetic labels.\n\n" +
                    "ITEM ACQUISITION ARCS\n\n" +
                    "A complete dungeon is not just a sequence of rooms and rewards. The generator looks for a mid-dungeon acquisition arc: the player reaches a puzzle or reward room, earns a defining item such as Hookshot, Iron Boots, Zora Tunic, Bow, or another catalogued progression item, then uses that item in later rooms. The item-arc certificate lists the acquisition room, every later consumer room, whether the item came from starting inventory, and any other requirements that must already be available. Small Keys, Boss Keys, maps, compasses, Heart Pieces, and Heart Containers remain separate economy or completion rewards and are not mistaken for the dungeon's central item gimmick.\n\n" +
                    "Multiple items are supported. A later room may require Hookshot plus an earlier shield, or Zora Tunic plus Iron Boots for a deep-water route. Requirements can come from a room, a transition, a traversal contract, a door, or a generated obstacle. If a non-starting item is required but no reachable acquisition exists, the report marks the arc unsolvable. If the only acquisition appears after an earlier consumer, it warns about the intended alternate route. In the randomizer baseline, the first puzzle branch can receive a generated Hookshot-style acquisition and later obstacles are tagged with that requirement; replace or refine the item actor from the imported palette as needed.\n\n" +
                    "This is an authoring certificate, not a claim that an arbitrary actor will automatically behave like the item in the ROM. The selected reward actor must have a compatible native content dropdown, object bank, and gameplay flag. After changing an item, run Scene > Validate, review the item arc and progression reports, then inject a ROM copy and play the route to confirm the actual pickup and gate behavior.\n\n" +
                    "REVIEWING THE ITEM ARC\n\n" +
                    "Choose Scene > Review item acquisition arc to open the expandable item-arc inspector. Expand an item to see its acquisition room, first consumer, every room that uses it, prerequisites, and evidence. The diagnostics area separates item-arc errors from broader progression warnings. This view reads the current native scene, so use it after moving rooms, changing rewards, or adding gates.\n\n" +
                    "Right-click an acquisition or consumer entry in that inspector to focus its room or select the corresponding materialized actor. If an actor has not yet been materialized into the native scene, the command focuses the room and explains why selection is unavailable.\n\n" +
                    "Consumer selection also checks native room transitions. This is important because an OoT doorway or entrance gate may carry the requirement even when the visible room mesh has no separate gate actor.\n\n" +
                    "TRAVERSAL PROGRESSION CHECK\n\n" +
                    "Generated room plans are checked in route order. Rewards earned in earlier rooms can unlock later Hookshot or strength-gated traversal contracts. If a room appears before the item that its route requires, generation records a traversal softlock warning so you can move the reward earlier, add a starting item, or choose another room. This complements the item/key progression solver: one checks inventory gates, the other checks movement-mechanic gates.\n\n" +
                    "CALIBRATION PROFILES\n\n" +
                    "Traversal distances use a named authoring profile. The default OoT conservative profile is intentionally cautious: it is a design-time limit, not a measured claim about every version of the game. A playtest trace records the ROM fingerprint, game version, scene/room, mechanic, distance, success/failure, and notes. Trace sets can be saved and reloaded as XML, then fitted into a version-specific profile from the largest successful sample and nearest failed sample. When emulator playtests produce verified traces, update the profile values rather than changing individual room logic. The report identifies the profile name and the exact route span that exceeded it.\n\n" +
                    "PLAYABLE ASSEMBLY\n\n" +
                    "The generator creates separate native rooms, places tiles by doorway sockets, keeps room-local actors grouped with their room package, adds native room transitions, preserves collision mappings, and validates unresolved gameplay requirements. In one click it also populates the generated rooms with a playable baseline: a starting spawn, enemies, finale encounter, reward progression, puzzle logic, and native flags. Rooms can snap directly at their natural doorway endpoints; corridors are optional pieces for extending or bending the path between rooms. Review every warning, then use the actor palette and no-code link editor to turn the baseline into your own full dungeon.\n\n" +
                    "NATIVE PLAYABILITY CERTIFICATE\n\n" +
                    "After generation, the status line and confirmation dialog report a native playability certificate. It checks the actual scene that will be exported: a valid player start, native room connections, one gameplay package per room, unique actor IDs, resolvable logic-link endpoints, reachability from the start room, combat, a finale, a reward, and a linked puzzle. A non-partial generation is rejected when any of those requirements fail. A partial result is still editable, but its certificate errors remain visible in validation until you repair the missing content.\n\n" +
                    "The certificate also runs the existing inventory-aware progression solver against the generated native packages. It checks required items, small-key costs, boss-key ownership, and item-gated transitions in the order a player can reach them. This is stronger than a room-count check, but it is still a pre-runtime simulation; exact actor behavior, collision, camera behavior, and save-state behavior must be confirmed in the injected ROM.\n\n" +
                    "TEXTURE PACKS\n\n" +
                    "Choose Edit > Choose dungeon texture pack whenever you want to restyle the current dungeon. The list comes from saved room and tile assets in the local library, so a dungeon theme can be copied from another extracted scene or from a generated tile. The analyzer chooses the dominant horizontal-up material as floor, horizontal-down material as ceiling, and side-facing material as wall. The selected source is saved with the dungeon workspace and reapplied when the project is reopened. Collision is never changed by a texture pack. Special materials and authored actors are preserved.\n\n" +
                    "AUTO-CHOP ENTRANCES\n\n" +
                    "Auto-chop entrances reads the native transitions in the active source room and creates doorway-aware regions. It preserves whole visual and collision triangles where possible and reports regions that cannot be captured cleanly.\n\n" +
                    "MANUAL TILE CAPTURE\n\n" +
                    "Open a source room, choose Visual faces, select geometry in the viewport, choose Collision faces, select matching collision, then choose Save tile. Select box captures whole triangles inside a coordinate box. Select connected expands the selection across connected edges. Tiles cannot currently clip partial triangles.\n\n" +
                    "PLACING A TILE\n\n" +
                    "Select the destination room first. Choose Place objects / tiles mode. Drag a tile into the viewport. Grid snap rounds the requested position. Rotation chooses a quarter-turn before placement. Magnetic snap searches compatible doorway sockets. Room door anchors expose native transitions as alignment targets.\n\n" +
                    "CROSS-SCENE COLLISION\n\n" +
                    "A tile from another source scene cannot blindly reuse its collision surface IDs. Archarina64 asks you to choose a destination collision surface. Select the surface that matches the floor, wall, water, ice, conveyor, or other destination behavior.\n\n" +
                    "MOVING A TILE\n\n" +
                    "Placed tiles are retained as local tile instances. Choose Select and left-click the tile. The right panel identifies its name, room, position, rotation, scale, and source. Choose Move and drag it; Rotate and click it for a 90-degree turn; Scale and use the mouse wheel over it. Right-click a tile for tile actions. Transforming rebuilds the room geometry, collision, sockets, and tile gameplay package from the saved tile instances, then rematerializes the associated native actors so they follow the tile.\n\n" +
                    "ROOM HIERARCHY\n\n" +
                    "The right panel contains a Layers: Room hierarchy drawer. Each Room layer owns its geometry, actors, doors, and spawns. Expand a room to select an individual actor, or right-click the room layer for Move room bundle and Focus room. Room-level movement applies the same translation to the room geometry, collision, sockets, native actors, transitions, spawns, and gameplay packages.\n\n" +
                    "ACTOR AND TILE CARDS\n\n" +
                    "The Tiles category shows a compact unlit mesh preview for saved custom tiles. Room and Recommended rooms use smaller two-column thumbnail cards. Actor, enemy, NPC, item, prop, door, spawn, boss, and object-bank categories show compact actor or fallback previews. These previews are selection aids; they do not change native asset data. Dragging a card uses the same placement path as the previous text list.\n\n" +
                    "IMPORTANT\n\n" +
                    "Projects created before tile-instance tracking was added may contain baked geometry without individual tile records. Re-place those tiles once in the updated build if you need to move them individually."));
                tabs.TabPages.Add(CreateSandboxHelpPage("Modes and selection", "How the viewport mouse model works",
                    "SELECT MODE\n\n" +
                    "Select is the normal inspection mode. Left-click an actor marker or tile to select it. The right panel updates immediately. Left-click does not move anything in this mode.\n\n" +
                    "PLACE OBJECTS / TILES MODE\n\n" +
                    "Use this mode when dragging content from the left visual asset drawer. It is appropriate for room tiles, saved tiles, actors, object banks, spawns, doors, enemies, NPCs, items, props, and connections. The thumbnail card is only the visual front-end; placement still uses the same asset definition and object-bank checks.\n\n" +
                    "MOVE MODE\n\n" +
                    "Move mode makes left-click dragging explicit. Click-drag a placed tile to move its complete tile instance. Click-drag an actor, spawn, or transition to move that gameplay object. Rotate mode turns a free tile one quarter-turn per click. Scale mode selects a free tile and uses the mouse wheel from 0.1x to 4x. Connected snap tiles must be disconnected before rotation or scaling. Grid snap affects tile movement and placement.\n\n" +
                    "EDIT MODE\n\n" +
                    "Edit mode is for selecting content and changing its properties in the right inspector or through the native Sharp Ocarina panels. It is intentionally separate from movement so accidental clicks do not reposition actors.\n\n" +
                    "LEFT CLICK\n\n" +
                    "Left-click selects the nearest visible actor marker or tile. In Move mode, left-click and drag begins a move.\n\n" +
                    "RIGHT CLICK\n\n" +
                    "Right-click selects the nearest actor or tile first, then opens its quick menu. Actor menus show valid logical links based on the actor role. Tile menus provide movement and deletion actions.\n\n" +
                    "RIGHT PANEL\n\n" +
                    "The Layers: Room hierarchy groups content by destination room. The scene-content list contains actors, spawns, and doors for the selected room. Actor labels use plain catalog names such as Stalfos, Moblin, Deku Scrub, Chest, Player Spawn, and Door when the imported ROM provides them; the native hexadecimal ID remains available in the selected-item overview and property grid. The selected-item overview explains the identity, category, native ID, variable, position, requirements, logic fields, and available actions. The property grid below exposes editable Sharp Ocarina fields."));
                tabs.TabPages.Add(CreateSandboxHelpPage("Actors and logic", "Building gameplay without writing code",
                    "ADDING ACTORS\n\n" +
                    "Choose a category in the left drawer, search by name, and drag an asset into the viewport. Archarina64 adds the actor to the current room, adds known object-bank dependencies, and selects the new actor in the right panel.\n\n" +
                    "OBJECT BANKS\n\n" +
                    "An actor's model and behavior may require one or more object banks. If the imported ROM catalog contains the required bank, placement adds it to the room. If it is unavailable, placement is rejected with an explanation.\n\n" +
                    "ACTOR PROPERTIES\n\n" +
                    "The native actor ID and variable are the values written to the OoT actor record. Rotation fields, position fields, switch flags, chest flags, collectible flags, path IDs, and actor-specific parameters can be edited through the inspector or native actors panel.\n\n" +
                    "QUICK LINKING\n\n" +
                    "Select a door, chest, platform, enemy, boss, switch, pressure plate, torch, target, NPC, path, or cutscene marker. Right-click it. The menu filters possible causes and effects by role. Choose a proposed action, then choose the compatible source actor. Archarina64 creates a typed logic link and records a reviewable suggestion.\n\n" +
                    "MANUAL LINKING\n\n" +
                    "Choose Edit > Link selected actors. Select the source, target, action, and optional parameter. Use this when the automatic role filter is too conservative or when a custom relationship is intended.\n\n" +
                    "COMMON PATTERNS\n\n" +
                    "Enemy group defeated -> unlock door. Switch activated -> open door. Pressure plate pressed -> enable platform. Target activated -> start path. Chest opened -> grant item. Puzzle completed -> spawn reward. Trigger -> start cutscene.\n\n" +
                    "VALIDATION\n\n" +
                    "Review gameplay and tile assembly reports links whose endpoints are missing, actors whose required objects are absent, unresolved requirements, unassigned flags, and suggestions for incomplete dungeon logic. These reports are guidance; runtime behavior must still be tested in an emulator."));
                tabs.TabPages.Add(CreateSandboxHelpPage("Native SO controls", "Using the full Sharp Ocarina feature set",
                    "OPENING NATIVE CONTROLS\n\n" +
                    "Press Ctrl+K and search for a feature. Archarina64 opens the matching native Sharp Ocarina control group in a focused popup instead of permanently displaying every control. The popup refreshes from the active Archarina64 scene before it opens.\n\n" +
                    "LAYOUT AND SCENE\n\n" +
                    "Rooms and room geometry edits room models, groups, room settings, and mesh behavior. Scene settings and cameras edits scene identity, cameras, waterboxes, exits, and scene-level values. Scene and room environments edit skyboxes, lighting, fog, time, and alternate environment data.\n\n" +
                    "TRAVERSAL\n\n" +
                    "Transitions and spawns edits native room connections, entrance records, spawn points, front/back rooms, and camera references. Collision and exits edits collision bounds, polygon types, exits, and traversal flags. Paths and patrols edits native pathway points and actor path references.\n\n" +
                    "GAMEPLAY\n\n" +
                    "Objects and actors edits room object banks and native actor records. Cutscenes edits native cutscene commands, camera points, text boxes, and actor cutscene data.\n\n" +
                    "RENDERING\n\n" +
                    "Conditional draw, color blending, texture animations, texture scrolling, texture swapping, texture swap frames, and polygon-type swapping expose the native rendering behavior available in the Sharp Ocarina baseline.\n\n" +
                    "WHEN TO USE NATIVE CONTROLS\n\n" +
                    "Use the sandbox for tile placement, selection, movement, asset browsing, and no-code logic. Use native controls when you need exact OoT header fields, camera data, room flags, object lists, path data, cutscene commands, environment settings, or renderer-specific values."));
                tabs.TabPages.Add(CreateSandboxHelpPage("Export and playtest", "From editable scene to a playable ROM copy",
                    "VALIDATE FIRST\n\n" +
                    "Run Scene > Validate. Fix every error before exporting. Then run Scene > Review gameplay and tile assembly and inspect warnings about unresolved links, collision, objects, transitions, rooms, flags, and progression.\n\n" +
                    "SAVE THE PROJECT\n\n" +
                    "Choose Project > Save scene. This stores the editable scene XML and references the private local geometry snapshot. Keep the LocalAppData Archarina64 workspace with the project.\n\n" +
                    "EXPORT A NATIVE PACKAGE\n\n" +
                    "Choose Project > Export native scene package. The existing Sharp Ocarina conversion path writes native scene and room output for a compatible ROM-hack project format. This does not create an emulator-ready ROM by itself.\n\n" +
                    "INJECT A TEST ROM\n\n" +
                    "Choose Project > Inject into ROM copy. Select a new output filename. Archarina64 copies the imported ROM and injects into the copy. Never select the original source ROM as the output.\n\n" +
                    "NATIVE PREVIEW VERSUS GAMEPLAY\n\n" +
                    "Native display-list preview shows how decoded OoT display lists render. Collision preview shows collision geometry. Neither mode runs Link, enemy AI, save logic, actor update code, or room streaming.\n\n" +
                    "EMULATOR PLAYTEST\n\n" +
                    "Run Scene > Playtest preflight first. It materializes tile actors and logic, combines native validation with gameplay-review diagnostics, and reports whether the source ROM and Project64 are configured. Resolve every ERROR before continuing. Then choose Project > Configure Project64 and select Project64.exe once, followed by Project > Inject and launch in Project64. Archarina64 writes a separate injected .z64 copy and starts Project64 with that file; the original imported ROM remains unchanged. Test the initial spawn, collision, every transition, doors, keys, chests, enemies, switches, puzzle completion, boss access, cutscenes, save/reload, and any alternate setup. Archarina64 is an editor, not an emulator; Project64 supplies the gameplay runtime."));
                tabs.TabPages.Add(CreateSandboxHelpPage("Troubleshooting", "Common problems and fixes",
                    "ROM WILL NOT IMPORT\n\n" +
                    "Use a decompressed big-endian .z64 ROM. The ROM must match a supported OoT build, including the custom NTSC 1.2 loader path. Compressed, little-endian, byte-swapped, or unsupported revisions will fail before catalog creation.\n\n" +
                    "EMPTY WORKSPACE SHOWS A SOURCE DUNGEON\n\n" +
                    "Make sure the current build is running and that the title says Archarina64 Empty Dungeon. Native display-list preview should use the active CurrentScene. If stale source geometry appears, restart the current build and reopen the empty workspace.\n\n" +
                    "TILE WILL NOT PLACE\n\n" +
                    "Confirm that a destination room is selected, the tile matches the imported ROM fingerprint, the tile has visual and collision data, and the required object banks exist. Cross-scene tiles require destination collision-surface mapping.\n\n" +
                    "TILE WILL NOT ALIGN\n\n" +
                    "Enable Magnetic snap, increase Radius, enable Room door anchors, and verify the tile sockets have matching width, height, and opposite facing. Use Tile sockets to inspect or repair socket metadata.\n\n" +
                    "ACTOR IS INVISIBLE\n\n" +
                    "Enable Native display-list preview when the actor requires native display-list decoding. Verify the actor's object bank is present. Check Selected room only and inspect the actor's source category and object dependencies.\n\n" +
                    "RIGHT PANEL IS BLANK\n\n" +
                    "Use Select mode and left-click the actor or tile. Right-clicking also selects first. The scene-content list only contains actors, spawns, and transitions for the selected room.\n\n" +
                    "LOGIC REVIEW HAS WARNINGS\n\n" +
                    "Warnings are not automatically proof that the actor is broken. Inspect the referenced native flag, variable, path, object dependency, and target room. Then verify the behavior in an injected ROM under an emulator.\n\n" +
                    "INJECTION FAILS\n\n" +
                    "Choose a separate output ROM, close any emulator using it, make sure it is decompressed and writable, and run validation first. Keep the original ROM unchanged."));
                Button close = new Button { Text = "Close", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right, Width = 90, Height = 28 };
                FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 5, 0, 0) };
                actions.Controls.Add(close);
                dialog.Controls.Add(tabs); dialog.Controls.Add(actions); dialog.AcceptButton = close; ApplySandboxTheme(dialog, sandboxDarkMode ? Color.FromArgb(24, 27, 31) : SystemColors.Control, sandboxDarkMode ? Color.FromArgb(31, 35, 40) : SystemColors.Window, sandboxDarkMode ? Color.FromArgb(42, 47, 53) : SystemColors.Window, sandboxDarkMode ? Color.FromArgb(232, 236, 240) : SystemColors.ControlText);
                dialog.ShowDialog(this);
            }
        }

        private static TabPage CreateSandboxHelpPage(string title, string subtitle, string content)
        {
            TabPage page = new TabPage(title);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 2 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { Text = subtitle, Dock = DockStyle.Fill, Font = new Font(SystemFonts.DefaultFont.FontFamily, 12, FontStyle.Bold), Padding = new Padding(0, 3, 0, 0) }, 0, 0);
            layout.Controls.Add(new RichTextBox { Text = content, Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, DetectUrls = false, Font = new Font("Segoe UI", 10), ScrollBars = RichTextBoxScrollBars.Vertical }, 0, 1);
            page.Controls.Add(layout);
            return page;
        }

        private ZActor SelectedSandboxActor()
        {
            SandboxActorItem item = sandboxContents == null ? null : sandboxContents.SelectedItem as SandboxActorItem;
            return item == null ? null : item.Actor;
        }

        private bool SandboxModeIs(string mode)
        {
            return sandboxInteractionMode != null && string.Equals(sandboxInteractionMode.SelectedItem as string, mode, StringComparison.OrdinalIgnoreCase);
        }

        private RadioButton CreateSandboxToolButton(string text, string mode)
        {
            RadioButton button = new RadioButton
            {
                Text = text,
                Tag = mode,
                Appearance = Appearance.Button,
                AutoSize = true,
                FlatStyle = FlatStyle.Standard,
                Padding = new Padding(6, 2, 6, 2),
                Margin = new Padding(2, 3, 0, 0),
                UseVisualStyleBackColor = true
            };
            button.CheckedChanged += delegate
            {
                if (button.Checked) SetSandboxInteractionMode(mode);
            };
            sandboxToolButtons.Add(button);
            return button;
        }

        private void SetSandboxInteractionMode(string mode)
        {
            if (sandboxInteractionMode == null) return;
            if (!sandboxInteractionMode.Items.Contains(mode)) return;
            if (!string.Equals(sandboxInteractionMode.SelectedItem as string, mode, StringComparison.OrdinalIgnoreCase))
                sandboxInteractionMode.SelectedItem = mode;
            SyncSandboxToolButtons();
        }

        private void SyncSandboxToolButtons()
        {
            string mode = sandboxInteractionMode == null ? null : sandboxInteractionMode.SelectedItem as string;
            foreach (RadioButton button in sandboxToolButtons)
                button.Checked = string.Equals(button.Tag as string, mode, StringComparison.OrdinalIgnoreCase);
        }

        private bool SandboxTextInputFocused()
        {
            Control focused = ActiveControl;
            while (focused is ContainerControl && ((ContainerControl)focused).ActiveControl != null)
                focused = ((ContainerControl)focused).ActiveControl;
            return focused is TextBoxBase || focused is NumericUpDown || focused is ComboBox ||
                focused is ListBox || focused is TreeView || focused is PropertyGrid;
        }

        private void SelectSandboxActor(ZActor actor)
        {
            if (actor == null || sandboxContents == null) return;
            SandboxActorItem item = sandboxContents.Items.Cast<SandboxActorItem>().FirstOrDefault(candidate => ReferenceEquals(candidate.Actor, actor));
            if (item == null)
            {
                item = sandboxContents.Items.Cast<SandboxActorItem>().FirstOrDefault(candidate =>
                    candidate.Actor.Number == actor.Number && candidate.Actor.Variable == actor.Variable &&
                    Math.Abs(candidate.Actor.XPos - actor.XPos) < 0.01f &&
                    Math.Abs(candidate.Actor.YPos - actor.YPos) < 0.01f &&
                    Math.Abs(candidate.Actor.ZPos - actor.ZPos) < 0.01f);
            }
            if (item != null)
            {
                sandboxContents.SelectedItem = item;
                actor = item.Actor;
            }

            // Keep the inspector authoritative even when the list item was rebuilt
            // during a native-room refresh and reference identity changed.
            sandboxInspector.SelectedObject = actor;
            UpdateSandboxSelectionSummary(actor, item == null ? "Actor" : item.Kind);
            glControl1.Invalidate();
        }

        private void AddSandboxActorRoomLinkMenu(ContextMenuStrip menu, ZActor actor)
        {
            if (menu == null || actor == null || CurrentScene == null) return;
            ToolStripMenuItem link = new ToolStripMenuItem("Link actor to room");
            for (int i = 0; i < CurrentScene.Rooms.Count; i++)
            {
                int roomIndex = i;
                link.DropDownItems.Add("Room " + roomIndex, null, delegate { SandboxRun(() => AttachSandboxActorToRoom(actor, roomIndex)); });
            }
            menu.Items.Add(link);
        }

        private void AttachSandboxActorToRoom(ZActor actor, int targetRoom)
        {
            if (CurrentScene == null || actor == null || targetRoom < 0 || targetRoom >= CurrentScene.Rooms.Count) return;
            if (actor.IsTransition)
                throw new InvalidOperationException("Door connections belong to two rooms. Edit their front/back endpoints in the connection editor.");
            SandboxRecord();
            if (CurrentScene.SpawnPoints.Contains(actor))
            {
                actor.SpawnRoom = (byte)targetRoom;
            }
            else
            {
                foreach (ZScene.ZRoom room in CurrentScene.Rooms) room.ZActors.Remove(actor);
                CurrentScene.Rooms[targetRoom].ZActors.Add(actor);
            }
            if (!string.IsNullOrEmpty(actor.SandboxGameplayId) && CurrentScene.SandboxGameplayPackages != null)
                foreach (RoomPackage package in CurrentScene.SandboxGameplayPackages)
                {
                    RoomPackageActor entry = (package.Actors ?? new List<RoomPackageActor>()).Concat(package.Spawns ?? new List<RoomPackageActor>())
                        .FirstOrDefault(item => item.Id == actor.SandboxGameplayId);
                    if (entry == null) continue;
                    entry.SourceRoom = targetRoom;
                    if (!package.LogicOnly) { package.Room = targetRoom; package.DestinationRoom = targetRoom; }
                    break;
                }
            savechanges = true;
            RefreshSandboxRooms();
            RefreshSandboxContents();
            RefreshSandboxLayers();
            SelectRoom(targetRoom);
            SelectSandboxActor(actor);
            sandboxStatus.Text = "Linked actor to room " + targetRoom + ".";
        }

        private bool TrySelectSandboxActorNative(Point point)
        {
            if (sandboxRoot == null || !sandboxRoot.Visible || CurrentScene == null ||
                RoomList.SelectedIndex < 0 || RoomList.SelectedIndex >= CurrentScene.Rooms.Count ||
                glControl1 == null || !glControl1.IsHandleCreated)
                return false;

            glControl1.MakeCurrent();
            byte[] pixel = new byte[4];
            int[] viewport = new int[4];
            bool selected = false;
            try
            {
                GL.Disable(EnableCap.Texture2D);

                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                foreach (ZActor actor in CurrentScene.Rooms[RoomList.SelectedIndex].ZActors)
                {
                    DrawActorModel(actor, Color.FromArgb(192, 255, 192), ActorCubeGLID, false, false);
                    GL.GetInteger(GetPName.Viewport, viewport);
                    GL.ReadPixels(point.X, viewport[3] - point.Y - 6, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
                    if (pixel[0] == 192)
                    {
                        SelectSandboxActor(actor);
                        selected = true;
                        break;
                    }
                }

                if (!selected)
                {
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    foreach (ZActor actor in CurrentScene.Transitions.Where(a => a.FrontSwitchTo == RoomList.SelectedIndex || a.BackSwitchTo == RoomList.SelectedIndex))
                    {
                        DrawActorModel(actor, Color.FromArgb(255, 192, 192), ActorPyramidGLID, false, false);
                        GL.GetInteger(GetPName.Viewport, viewport);
                        GL.ReadPixels(point.X, viewport[3] - point.Y - 6, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
                        if (pixel[0] == 255)
                        {
                            SelectSandboxActor(actor);
                            selected = true;
                            break;
                        }
                    }
                }

                if (!selected)
                {
                    GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    foreach (ZActor actor in CurrentScene.SpawnPoints.Where(a => a.SpawnRoom == RoomList.SelectedIndex))
                    {
                        DrawActorModel(actor, Color.FromArgb(192, 192, 255), ActorPyramidGLID, false, false);
                        GL.GetInteger(GetPName.Viewport, viewport);
                        GL.ReadPixels(point.X, viewport[3] - point.Y - 6, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
                        if (pixel[0] == 192)
                        {
                            SelectSandboxActor(actor);
                            selected = true;
                            break;
                        }
                    }
                }
            }
            finally
            {
                GL.Enable(EnableCap.Texture2D);
                glControl1.Invalidate();
            }
            return selected;
        }

        private ZActor PickSandboxActor(Point point)
        {
            if (CurrentScene == null || RoomList.SelectedIndex < 0 || RoomList.SelectedIndex >= CurrentScene.Rooms.Count) return null;
            Vector3d start, end;
            GetSandboxRay(point, out start, out end);
            Vector3d ray = end - start;
            double lengthSquared = ray.LengthSquared;
            if (lengthSquared < 0.000001) return null;
            IEnumerable<ZActor> actors = CurrentScene.Rooms[RoomList.SelectedIndex].ZActors
                .Concat(CurrentScene.SpawnPoints.Where(a => a.SpawnRoom == RoomList.SelectedIndex))
                .Concat(CurrentScene.Transitions.Where(a => a.FrontSwitchTo == RoomList.SelectedIndex || a.BackSwitchTo == RoomList.SelectedIndex));
            ZActor nearest = null;
            double nearestDistance = 140.0;
            foreach (ZActor actor in actors.Distinct())
            {
                Vector3d position = new Vector3d(actor.XPos, actor.YPos, actor.ZPos);
                double t = Vector3d.Dot(position - start, ray) / lengthSquared;
                t = Math.Max(0, Math.Min(1, t));
                double distance = (position - (start + ray * t)).Length;
                if (distance < nearestDistance) { nearest = actor; nearestDistance = distance; }
            }
            return nearest;
        }

        private void UpdateSandboxSelectionSummary(ZActor actor, string kind)
        {
            if (sandboxSelectionSummary == null) return;
            if (actor == null)
            {
                sandboxSelectionSummary.Text = "Select an actor in the list or click a marker in the viewport.";
                return;
            }
            RomPaletteAsset asset = sandboxCatalog == null ? null : sandboxCatalog.Assets.FirstOrDefault(a => a.Id == actor.Number && !a.Transition);
            string name = asset == null ? "Unknown actor" : asset.Name;
            string category = asset == null ? (kind ?? "Actor") : asset.Category;
            List<string> lines = new List<string>
            {
                name + "  [" + category + "]",
                "Actor 0x" + actor.Number.ToString("X4") + "  Variable 0x" + actor.Variable.ToString("X4"),
                "Position " + actor.XPos.ToString("0.##") + ", " + actor.YPos.ToString("0.##") + ", " + actor.ZPos.ToString("0.##")
            };
            if (asset != null && !string.IsNullOrWhiteSpace(asset.RequirementText)) lines.Add("Requirement: " + asset.RequirementText);
            if (asset != null && asset.LogicProperties != null && asset.LogicProperties.Count > 0)
                lines.Add("Logic fields: " + string.Join(", ", asset.LogicProperties.Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Take(3).ToArray()));
            lines.Add("Actions: edit properties, move, replace, delete, or right-click for compatible links.");
            sandboxSelectionSummary.Text = string.Join(Environment.NewLine, lines.ToArray());
        }

        private void MoveSandboxActorToSurface(Point point)
        {
            if (sandboxMovingActor == null || CurrentScene == null) return;
            Vector3d position = SandboxDropPosition(point);
            sandboxMovingActor.XPos = (float)position.X; sandboxMovingActor.YPos = (float)position.Y; sandboxMovingActor.ZPos = (float)position.Z;
            SyncSandboxGameplayActor(sandboxMovingActor);
            sandboxInspector.SelectedObject = sandboxMovingActor; glControl1.Invalidate();
        }

        internal async Task OpenSandboxRomAsync(string path)
        {
            if (InvokeRequired) throw new InvalidOperationException("Open the ROM from the editor UI thread.");
            // The inherited application uses a manual message loop, not Application.Run.
            if (!(System.Threading.SynchronizationContext.Current is WindowsFormsSynchronizationContext))
                System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            if (!WarningUnsavedChanges(true)) return;
            sandboxStatus.Text = "Importing ROM library...";
            sandboxRoot.Enabled = false;
            try
            {
                byte[] bytes = await Task.Run(() => File.ReadAllBytes(path));
                if (bytes.Length < 33554432 + 50000 || bytes[0] != 0x80 || bytes[1] != 0x37)
                    throw new InvalidDataException("Choose a decompressed, big-endian OoT ROM.");
                ROM version = CheckVersion(new List<byte>(bytes));
                if (version == null || !string.Equals(version.Game, "OOT", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Archarina64 could not identify this as a supported OoT ROM build. Use a decompressed NTSC 1.0/1.2 or OoT MQ debug ROM.");
                string xml = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XML", "OOT");
                RomAssetCatalog catalog = await Task.Run(() => RomAssetCatalog.Import(bytes, version, xml, ArcharinaWorkspace.LibraryPath));
                sandboxCatalog = catalog; sandboxRom = bytes; sandboxRomPath = Path.GetFullPath(path);
                sandboxRomMechanics = DungeonRomMechanicsScanner.Scan(bytes, catalog.Rooms.RomFingerprint, version.Game);
                ArcharinaWorkspace.ActiveObjectDirectory = Path.Combine(catalog.DirectoryPath, "objects");
                settings.MajorasMask = false;
                OpenGlobalFile(sandboxRomPath);
                LoadSandboxTiles();
                TreeNode firstLocation = sandboxCategories.Nodes.Cast<TreeNode>().SelectMany(n => n.Nodes.Cast<TreeNode>().Concat(new[] { n }))
                    .FirstOrDefault(n => n.Tag is SandboxRoomCategory && ((SandboxRoomCategory)n.Tag).SceneId >= 0);
                if (firstLocation != null) sandboxCategories.SelectedNode = firstLocation;
                else if (sandboxCategories.Nodes.Count > 0) sandboxCategories.SelectedNode = sandboxCategories.Nodes[0];
                if (catalog.Rooms.Rooms.Count > 0) OpenSandboxRoom(catalog.Rooms.Rooms[0], false);
                sandboxStatus.Text = string.Format("{0} | {1:N0} assets | {2} diagnostics | {3}", Path.GetFileName(path), catalog.Assets.Count, catalog.Diagnostics.Count, sandboxRomMechanics.Detection);
            }
            catch (Exception error)
            {
                sandboxStatus.Text = "Import failed: " + error.Message;
                throw;
            }
            finally { sandboxRoot.Enabled = true; }
        }

        private void OpenSandboxRoom(SceneTileSource source, bool warn = true)
        {
            if (source == null || sandboxRom == null) throw new InvalidOperationException("Open a ROM library first.");
            if (warn && !WarningUnsavedChanges(true)) return;
            sandboxStatus.Text = "Loading " + source + "...";
            sandboxRoot.Enabled = false;
            try
            {
                LoadZscene(new List<byte>(sandboxRom), source.SceneId);
                if (CurrentScene == null || CurrentScene.Rooms.Count <= source.RoomId) throw new InvalidDataException("The source room could not be loaded.");
                CurrentScene.SceneNumber = source.SceneId;
                SetCurrentSceneDocument(CurrentScene, sandboxRomPath);
                sandboxAuthoringMarkers = new List<SandboxAuthoringMarker>();
                // The hidden legacy ListBox cannot rely on its old CurrencyManager after a scene replacement.
                manualRoomListItems = true;
                RefreshRoomListItems();
                SelectRoom(source.RoomId);
                ApplySandboxPreview();
                ClearSandboxFaces();
                sandboxUndo.Clear(); sandboxRedo.Clear();
                RefreshSandboxRooms(); FocusSandboxRoom();
                string sidecarWarning = CurrentDocument == null ? null : CurrentDocument.SidecarLoadError;
                sandboxStatus.Text = string.Format("{0} | {1} rooms | {2} preview diagnostics", source, CurrentScene.Rooms.Count, CurrentScene.PreviewDiagnostics.Count) +
                    (string.IsNullOrEmpty(sidecarWarning) ? string.Empty : " | Sidecar warning: " + sidecarWarning);
            }
            finally { NowLoading = false; sandboxRoot.Enabled = true; }
        }

        private void ApplySandboxPreview()
        {
            if (CurrentScene == null || sandboxTwoSided == null) return;
            glControl1.MakeCurrent();
            foreach (ZScene.ZRoom room in CurrentScene.Rooms)
                if (room.ObjModel != null && room.ObjModel.PreviewTwoSided != sandboxTwoSided.Checked)
                {
                    room.ObjModel.PreviewTwoSided = sandboxTwoSided.Checked;
                    room.ObjModel.Prepare(false, room.TrueGroups);
                }
            settings.OnlyRenderSelectedRoom = sandboxIsolateRoom.Checked;
            glControl1.Invalidate();
        }

        private void ApplySandboxNativePreview()
        {
            SimulateN64Gfx = sandboxNativePreview != null && sandboxNativePreview.Checked;
            if (SimulateN64CheckBox != null) SimulateN64CheckBox.Checked = SimulateN64Gfx;
            glControl1.Invalidate();
        }

        private void BrowseSandboxScenes()
        {
            if (sandboxCatalog == null) throw new InvalidOperationException("Open a ROM library first.");
            using (Form browser = new Form { Text = "ROM scenes and rooms", ClientSize = new Size(560, 580), StartPosition = FormStartPosition.CenterParent })
            {
                TreeView tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false };
                foreach (var scene in sandboxCatalog.Rooms.Rooms.GroupBy(r => r.SceneId))
                {
                    TreeNode node = tree.Nodes.Add("Scene " + scene.Key.ToString("X2") + " - " + OoTSceneNames.Get(scene.Key) + " - " + scene.Count() + " rooms");
                    foreach (SceneTileSource room in scene) node.Nodes.Add(new TreeNode("Room " + room.RoomId.ToString("D2") + " - " + OoTSceneNames.Get(scene.Key)) { Tag = room });
                }
                Button open = new Button { Text = "Open", Dock = DockStyle.Bottom, Height = 36, Enabled = false };
                tree.AfterSelect += delegate { open.Enabled = tree.SelectedNode?.Tag is SceneTileSource; };
                SceneTileSource selected = null;
                open.Click += delegate { selected = tree.SelectedNode.Tag as SceneTileSource; browser.DialogResult = DialogResult.OK; };
                tree.NodeMouseDoubleClick += delegate(object sender, TreeNodeMouseClickEventArgs e)
                { if (e.Node.Tag is SceneTileSource) { selected = (SceneTileSource)e.Node.Tag; browser.DialogResult = DialogResult.OK; } };
                browser.Controls.Add(tree); browser.Controls.Add(open); browser.AcceptButton = open;
                if (browser.ShowDialog(this) == DialogResult.OK && selected != null) OpenSandboxRoom(selected);
            }
        }

        private void RefreshSandboxPalette()
        {
            if (sandboxCatalog == null) return;
            if (sandboxCategories.SelectedNode != null && sandboxCategories.SelectedNode.Tag is SandboxRecommendationCategory)
            {
                RefreshSandboxRecommendedRooms();
                return;
            }
            SandboxRoomCategory roomCategory = sandboxCategories.SelectedNode == null ? null : sandboxCategories.SelectedNode.Tag as SandboxRoomCategory;
            if (roomCategory != null)
            {
                sandboxPalette.Visible = false; sandboxAssetDrawer.Visible = false; sandboxRoomDrawer.Visible = true; sandboxRoomImages.Images.Clear(); sandboxRoomDrawer.Items.Clear();
                string search = sandboxSearch.Text ?? "";
                IEnumerable<SceneTileSource> roomSources = sandboxCatalog.Rooms.Rooms
                    .Where(r => roomCategory.SceneId < 0 || r.SceneId == roomCategory.SceneId)
                    .Where(r => string.IsNullOrEmpty(search) || ("Room " + r.RoomId).IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0);
                foreach (SceneTileSource source in roomSources)
                {
                    RomPaletteAsset asset = sandboxCatalog.Assets.FirstOrDefault(a => a.Room == source);
                    if (asset == null) asset = new RomPaletteAsset { Category = roomCategory.Name, Name = source.ToString(), Room = source };
                    string imageKey = source.SceneId.ToString("X2") + "_" + source.RoomId;
                    int imageIndex = -1;
                    string thumbnailPath = SandboxRoomThumbnailPath(source, asset);
                    if (!string.IsNullOrEmpty(thumbnailPath))
                    {
                        // Detach the image from the cache file so the drawer can be refreshed
                        // and the extractor can overwrite thumbnails without a file lock.
                        using (Image fileImage = Image.FromFile(thumbnailPath))
                        using (Bitmap imageCopy = new Bitmap(fileImage))
                            sandboxRoomImages.Images.Add(imageKey, imageCopy);
                        imageIndex = sandboxRoomImages.Images.IndexOfKey(imageKey);
                    }
                    ListViewItem item = new ListViewItem("Room " + source.RoomId + "\n" + (asset.TilePath == null ? "Source room" : "Tile ready"), imageIndex) { Tag = asset };
                    sandboxRoomDrawer.Items.Add(item);
                }
                return;
            }
            SandboxTileCategory tileCategory = sandboxCategories.SelectedNode == null ? null : sandboxCategories.SelectedNode.Tag as SandboxTileCategory;
            if (tileCategory != null)
            {
                List<RomPaletteAsset> tileAssets = sandboxCatalog.Assets
                    .Where(a => a.Category == "Tiles" && !string.IsNullOrEmpty(a.TilePath))
                    .Where(a => tileCategory.All ||
                        (tileCategory.ImportedOnly ? a.ImportedTile : tileCategory.OtherOnly ? !a.GeneratedTile && !a.ImportedTile && a.TileSourceScene < 0 : tileCategory.GeneratedOnly ? a.GeneratedTile : !a.GeneratedTile && !a.ImportedTile) &&
                        (tileCategory.SceneId < 0 || a.TileSourceScene == tileCategory.SceneId) &&
                        (string.IsNullOrEmpty(tileCategory.Family) || SandboxGeneratedTileFamily(a) == tileCategory.Family))
                    .Where(a => string.IsNullOrEmpty(sandboxSearch.Text) || SandboxAssetLabel(a).IndexOf(sandboxSearch.Text, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                RefreshSandboxAssetPalette(tileAssets);
                return;
            }
            sandboxRoomDrawer.Visible = false;
            string category = sandboxCategories.SelectedNode == null ? "" : sandboxCategories.SelectedNode.Text;
            List<RomPaletteAsset> assets = sandboxCatalog.Assets
                .Where(a => a.Category == category && a.Name.IndexOf(sandboxSearch.Text, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            RefreshSandboxAssetPalette(assets);
        }

        private void RefreshSandboxAssetPalette(List<RomPaletteAsset> assets)
        {
            sandboxRoomDrawer.Visible = false;
            sandboxPalette.DataSource = null;
            sandboxPalette.DataSource = assets ?? new List<RomPaletteAsset>();
            sandboxAssetImages.Images.Clear();
            sandboxAssetDrawer.Items.Clear();
            foreach (RomPaletteAsset asset in assets ?? new List<RomPaletteAsset>())
            {
                string key = "asset_" + sandboxAssetDrawer.Items.Count;
                Bitmap image = null;
                try { image = CreateSandboxAssetThumbnail(asset); }
                catch (Exception error) { ArcharinaWorkspace.LogError(error); }
                if (image == null)
                {
                    image = new Bitmap(128, 96);
                    using (Graphics graphics = Graphics.FromImage(image))
                    {
                        graphics.Clear(Color.FromArgb(42, 42, 42));
                        using (Brush brush = new SolidBrush(Color.FromArgb(180, 180, 180)))
                            graphics.DrawString("Preview unavailable", SystemFonts.DefaultFont, brush, new PointF(6, 40));
                    }
                }
                using (image) sandboxAssetImages.Images.Add(key, image);
                sandboxAssetDrawer.Items.Add(new ListViewItem(SandboxAssetLabel(asset), sandboxAssetImages.Images.IndexOfKey(key)) { Tag = asset });
            }
            sandboxPalette.Visible = false;
            sandboxAssetDrawer.Visible = true;
        }

        private void RebuildSandboxCategories()
        {
            if (sandboxCatalog == null || sandboxCategories == null) return;
            string selectedKey = SandboxCategoryKey(sandboxCategories.SelectedNode);
            HashSet<string> expanded = new HashSet<string>(sandboxCategories.Nodes.Cast<TreeNode>().SelectMany(SandboxDescendants)
                .Where(n => n.IsExpanded).Select(SandboxCategoryKey));
            sandboxCategories.BeginUpdate();
            try
            {
                sandboxCategories.Nodes.Clear();
                sandboxCategories.Nodes.Add(new TreeNode("Recommended rooms") { Tag = new SandboxRecommendationCategory() });

                List<SceneTileSource> sourceRooms = sandboxCatalog.Rooms == null ? new List<SceneTileSource>() : sandboxCatalog.Rooms.Rooms;
                TreeNode rooms = sandboxCategories.Nodes.Add("Rooms (" + sourceRooms.Count + ")");
                rooms.Tag = new SandboxRoomCategory { Name = "Rooms", SceneId = -1 };
                foreach (var scene in sourceRooms.GroupBy(r => r.SceneId).OrderBy(g => g.Key))
                {
                    TreeNode sceneNode = rooms.Nodes.Add(SandboxSceneCategoryLabel(scene.Key, scene.Count()));
                    sceneNode.Tag = new SandboxRoomCategory { Name = SandboxRoomLocation(scene.Key), SceneId = scene.Key };
                }

                List<RomPaletteAsset> savedTiles = sandboxCatalog.Assets.Where(a => a.Category == "Tiles" && !string.IsNullOrEmpty(a.TilePath)).ToList();
                TreeNode tiles = sandboxCategories.Nodes.Add("Tiles (" + savedTiles.Count + ")");
                tiles.Tag = new SandboxTileCategory { Name = "Tiles", All = true };
                foreach (var scene in savedTiles.Where(a => !a.GeneratedTile && !a.ImportedTile).GroupBy(a => a.TileSourceScene).OrderBy(g => g.Key))
                {
                    TreeNode sceneNode = tiles.Nodes.Add(SandboxSceneCategoryLabel(scene.Key, scene.Count()) + " tiles");
                    sceneNode.Tag = new SandboxTileCategory { Name = sceneNode.Text, SceneId = scene.Key };
                }
                List<RomPaletteAsset> imported = savedTiles.Where(a => a.ImportedTile).ToList();
                if (imported.Count > 0)
                {
                    TreeNode importedNode = tiles.Nodes.Add("Imported models (" + imported.Count + ")");
                    importedNode.Tag = new SandboxTileCategory { Name = importedNode.Text, ImportedOnly = true };
                }
                List<RomPaletteAsset> generated = savedTiles.Where(a => a.GeneratedTile).ToList();
                if (generated.Count > 0)
                {
                    TreeNode pcg = tiles.Nodes.Add("Generated PCG tiles (" + generated.Count + ")");
                    pcg.Tag = new SandboxTileCategory { Name = pcg.Text, GeneratedOnly = true };
                    foreach (var family in generated.GroupBy(SandboxGeneratedTileFamily).OrderBy(g => g.Key))
                    {
                        TreeNode familyNode = pcg.Nodes.Add(family.Key + " (" + family.Count() + ")");
                        familyNode.Tag = new SandboxTileCategory { Name = familyNode.Text, GeneratedOnly = true, Family = family.Key };
                    }
                }
                List<RomPaletteAsset> otherTiles = savedTiles.Where(a => !a.GeneratedTile && !a.ImportedTile && a.TileSourceScene < 0).ToList();
                if (otherTiles.Count > 0)
                {
                    TreeNode other = tiles.Nodes.Add("Other saved tiles (" + otherTiles.Count + ")");
                    other.Tag = new SandboxTileCategory { Name = other.Text, SceneId = -1, OtherOnly = true };
                }

                foreach (string category in sandboxCatalog.Assets.Select(a => a.Category).Where(c => c != "Rooms" && c != "Tiles")
                    .Distinct().OrderBy(x => x)) sandboxCategories.Nodes.Add(category);
                rooms.Expand();
                tiles.Expand();
                TreeNode restored = sandboxCategories.Nodes.Cast<TreeNode>().SelectMany(n => SandboxDescendants(n).Concat(new[] { n }))
                    .FirstOrDefault(n => SandboxCategoryKey(n) == selectedKey);
                if (restored != null) sandboxCategories.SelectedNode = restored;
                foreach (TreeNode node in sandboxCategories.Nodes.Cast<TreeNode>().SelectMany(SandboxDescendants).Concat(sandboxCategories.Nodes.Cast<TreeNode>()))
                    if (expanded.Contains(SandboxCategoryKey(node))) node.Expand();
            }
            finally { sandboxCategories.EndUpdate(); }
        }

        private static IEnumerable<TreeNode> SandboxDescendants(TreeNode node)
        {
            foreach (TreeNode child in node.Nodes)
            {
                yield return child;
                foreach (TreeNode descendant in SandboxDescendants(child)) yield return descendant;
            }
        }

        private static string SandboxCategoryKey(TreeNode node)
        {
            if (node == null) return "";
            SandboxRoomCategory room = node.Tag as SandboxRoomCategory;
            if (room != null) return "room:" + room.SceneId;
            SandboxTileCategory tile = node.Tag as SandboxTileCategory;
            if (tile != null) return "tile:" + tile.SceneId + ":" + tile.GeneratedOnly + ":" + tile.ImportedOnly + ":" + tile.OtherOnly + ":" + tile.Family + ":" + tile.All;
            return "asset:" + node.Text;
        }

        private static string SandboxSceneCategoryLabel(int sceneId, int count)
        {
            return string.Format("Scene {0:X2} - {1} ({2})", sceneId, OoTSceneNames.Get(sceneId), count);
        }

        private static string SandboxGeneratedTileFamily(RomPaletteAsset asset)
        {
            string name = asset == null ? "" : asset.Name ?? "";
            if (name.IndexOf("Circular", StringComparison.OrdinalIgnoreCase) >= 0) return "Circular rooms";
            if (name.IndexOf("Tube Corridor", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Corridor", StringComparison.OrdinalIgnoreCase) >= 0) return "Corridors and elbows";
            if (name.IndexOf("Hub", StringComparison.OrdinalIgnoreCase) >= 0) return "Hubs and arenas";
            if (name.IndexOf("spiral", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("fall shaft", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("push block", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Hookshot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("ramp", StringComparison.OrdinalIgnoreCase) >= 0) return "Traversal and puzzle rooms";
            if (name.IndexOf("Room", StringComparison.OrdinalIgnoreCase) >= 0) return "Square rooms";
            return "Other generated tiles";
        }

        private void RefreshSandboxRecommendedRooms()
        {
            sandboxPalette.Visible = false;
            sandboxAssetDrawer.Visible = false;
            sandboxRoomDrawer.Visible = true;
            sandboxRoomImages.Images.Clear();
            sandboxRoomDrawer.Items.Clear();
            string search = sandboxSearch.Text ?? "";
            foreach (RomPaletteAsset asset in BuildSandboxRoomRecommendations()
                .Where(a => string.IsNullOrEmpty(search) || a.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(80))
            {
                SceneTileSource source = asset.Room;
                if (source == null) continue;
                string imageKey = "recommended_" + source.SceneId.ToString("X2") + "_" + source.RoomId;
                int imageIndex = -1;
                string thumbnailPath = SandboxRoomThumbnailPath(source, asset);
                if (!string.IsNullOrEmpty(thumbnailPath))
                    try
                    {
                        using (Image fileImage = Image.FromFile(thumbnailPath))
                        using (Bitmap imageCopy = new Bitmap(fileImage))
                            sandboxRoomImages.Images.Add(imageKey, imageCopy);
                        imageIndex = sandboxRoomImages.Images.IndexOfKey(imageKey);
                    }
                    catch (Exception error) { ArcharinaWorkspace.LogError(error); }
                string role = string.IsNullOrEmpty(asset.RecommendationReason) ? "Candidate room" : asset.RecommendationReason;
                string label = string.Format("{0:X2} {1}\nRoom {2} | {3}", source.SceneId, OoTSceneNames.Get(source.SceneId), source.RoomId, role);
                sandboxRoomDrawer.Items.Add(new ListViewItem(label, imageIndex) { Tag = asset });
            }
            sandboxStatus.Text = "Recommended rooms combine compatible roles from every extracted scene; the full library remains available below.";
        }

        private List<RomPaletteAsset> BuildSandboxRoomRecommendations()
        {
            List<RomPaletteAsset> candidates = sandboxCatalog.Assets
                .Where(a => a.Room != null && !string.IsNullOrEmpty(a.TilePath))
                .ToList();
            List<RoomPackage> placed = CurrentScene == null || CurrentScene.SandboxGameplayPackages == null
                ? new List<RoomPackage>() : CurrentScene.SandboxGameplayPackages;
            bool hasBoss = placed.SelectMany(p => p.Actors ?? new List<RoomPackageActor>()).Any(SandboxActorLooksLikeBoss);
            bool hasReward = placed.SelectMany(p => p.Actors ?? new List<RoomPackageActor>()).Any(SandboxActorLooksLikeReward);
            bool hasHub = placed.Any(p => p.Sockets != null && p.Sockets.Count >= 3);
            bool hasCombat = placed.SelectMany(p => p.Actors ?? new List<RoomPackageActor>()).Any(SandboxActorLooksLikeEnemy);

            foreach (RomPaletteAsset asset in candidates)
            {
                asset.RecommendationScore = 0;
                asset.RecommendationReason = "";
                try
                {
                    SectionTile tile = TileStorage.Load<SectionTile>(asset.TilePath);
                    RoomPackage package = tile.Gameplay;
                    List<RoomPackageActor> actors = package == null ? new List<RoomPackageActor>() : package.Actors ?? new List<RoomPackageActor>();
                    int sockets = tile.Sockets == null ? 0 : tile.Sockets.Count;
                    bool boss = SandboxActorLooksLikeBoss(actors);
                    bool reward = SandboxActorLooksLikeReward(actors);
                    bool enemy = SandboxActorLooksLikeEnemy(actors);
                    if (!hasBoss && boss) { asset.RecommendationScore += 45; asset.RecommendationReason = "Boss / finale"; }
                    if (hasBoss && boss) asset.RecommendationScore -= 35;
                    if (!hasReward && reward) { asset.RecommendationScore += 28; if (asset.RecommendationReason == "") asset.RecommendationReason = "Reward room"; }
                    if (!hasHub && sockets >= 3) { asset.RecommendationScore += 24; if (asset.RecommendationReason == "") asset.RecommendationReason = "Hub / connector"; }
                    if (sockets >= 2) asset.RecommendationScore += 8;
                    if (!hasCombat && enemy) { asset.RecommendationScore += 14; if (asset.RecommendationReason == "") asset.RecommendationReason = "Combat room"; }
                    if (hasCombat && enemy) asset.RecommendationScore += 4;
                    if (package != null && package.Spawns != null && package.Spawns.Count > 0) asset.RecommendationScore += 5;
                    if (placed.Count == 0 && boss) asset.RecommendationScore -= 25;
                    if (placed.Count == 0 && sockets > 0) asset.RecommendationScore += 12;
                    if (asset.RecommendationReason == "") asset.RecommendationReason = sockets >= 2 ? "Flexible connector" : "Supporting room";
                }
                catch (Exception error)
                {
                    ArcharinaWorkspace.LogError(error);
                    asset.RecommendationReason = "Needs review";
                }
            }
            return candidates.OrderByDescending(a => a.RecommendationScore)
                .ThenBy(a => a.Room.SceneId).ThenBy(a => a.Room.RoomId).ToList();
        }

        private static bool SandboxActorLooksLikeBoss(RoomPackageActor actor)
        {
            return actor != null && (SandboxTextContains(actor.Name, "boss") || SandboxTextContains(actor.Role, "boss"));
        }

        private static bool SandboxActorLooksLikeBoss(IEnumerable<RoomPackageActor> actors)
        {
            return (actors ?? Enumerable.Empty<RoomPackageActor>()).Any(SandboxActorLooksLikeBoss);
        }

        private static bool SandboxActorLooksLikeReward(RoomPackageActor actor)
        {
            return actor != null && (SandboxTextContains(actor.Role, "chest") || SandboxTextContains(actor.Name, "chest") || (actor.GrantedItems != null && actor.GrantedItems.Count > 0));
        }

        private static bool SandboxActorLooksLikeReward(IEnumerable<RoomPackageActor> actors)
        {
            return (actors ?? Enumerable.Empty<RoomPackageActor>()).Any(SandboxActorLooksLikeReward);
        }

        private static bool SandboxActorLooksLikeEnemy(RoomPackageActor actor)
        {
            return actor != null && (SandboxTextContains(actor.Role, "enemy") || SandboxTextContains(actor.Name, "enemy"));
        }

        private static bool SandboxActorLooksLikeEnemy(IEnumerable<RoomPackageActor> actors)
        {
            return (actors ?? Enumerable.Empty<RoomPackageActor>()).Any(SandboxActorLooksLikeEnemy);
        }

        private static bool SandboxTextContains(string value, string part)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string SandboxAssetLabel(RomPaletteAsset asset)
        {
            if (asset == null) return "Asset";
            string name = asset.Name ?? asset.Category ?? "Asset";
            return name.Length > 28 ? name.Substring(0, 25) + "..." : name;
        }

        private Bitmap CreateSandboxAssetThumbnail(RomPaletteAsset asset)
        {
            if (asset != null && !string.IsNullOrEmpty(asset.ThumbnailPath))
                try
                {
                    using (Image source = Image.FromFile(asset.ThumbnailPath)) return new Bitmap(source, new Size(128, 96));
                }
                catch { }
            if (asset != null && asset.Room != null)
            {
                string path = SandboxRoomThumbnailPath(asset.Room, asset);
                if (!string.IsNullOrEmpty(path))
                    try
                    {
                        using (Image source = Image.FromFile(path)) return new Bitmap(source, new Size(128, 96));
                    }
                    catch { }
            }
            if (asset != null && asset.TilePath != null)
                try { return RenderSandboxTileThumbnail(TileStorage.Load<SectionTile>(asset.TilePath).Visual); }
                catch { }
            return RoomAssetBrowser.RenderActorThumbnail(new ZActor(asset == null ? (ushort)0 : asset.Id, 0, 0, 0, 0, 0, 0, asset == null ? (ushort)0 : asset.Variable));
        }

        private static Bitmap RenderSandboxTileThumbnail(TileMesh mesh)
        {
            const int width = 128, height = 96;
            Bitmap bitmap = new Bitmap(width, height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen outline = new Pen(Color.FromArgb(170, 220, 235), 1))
            {
                graphics.Clear(Color.FromArgb(42, 42, 42));
                if (mesh == null || mesh.Vertices == null || mesh.Vertices.Count == 0 || mesh.Groups == null) return bitmap;
                double minX = mesh.Vertices.Min(v => v.X), maxX = mesh.Vertices.Max(v => v.X);
                double minY = mesh.Vertices.Min(v => v.Y), maxY = mesh.Vertices.Max(v => v.Y);
                double minZ = mesh.Vertices.Min(v => v.Z), maxZ = mesh.Vertices.Max(v => v.Z);
                double extent = Math.Max(Math.Max(maxX - minX, maxY - minY), maxZ - minZ);
                if (extent < 0.001) return bitmap;
                double centerX = (minX + maxX) * 0.5, centerY = (minY + maxY) * 0.5, centerZ = (minZ + maxZ) * 0.5;
                double scale = 72.0 / extent;
                int groupIndex = 0;
                foreach (ObjFile.Group group in mesh.Groups)
                {
                    using (SolidBrush fill = new SolidBrush(Color.FromArgb(100 + (groupIndex % 4) * 25, 115 + (groupIndex % 3) * 20, 145 + (groupIndex % 2) * 20)))
                    {
                        foreach (ObjFile.Triangle triangle in group.Triangles)
                        {
                            if (triangle.VertIndex == null || triangle.VertIndex.Length < 3 || triangle.VertIndex.Any(i => i < 0 || i >= mesh.Vertices.Count)) continue;
                            PointF[] points = triangle.VertIndex.Take(3).Select(i =>
                            {
                                ObjFile.Vertex v = mesh.Vertices[i];
                                double sx = (v.X - centerX) - (v.Z - centerZ) * 0.75;
                                double sy = (v.X - centerX) * 0.35 + (v.Z - centerZ) * 0.35 - (v.Y - centerY) * 0.85;
                                return new PointF((float)(width * 0.5 + sx * scale), (float)(height * 0.52 + sy * scale));
                            }).ToArray();
                            graphics.FillPolygon(fill, points); graphics.DrawPolygon(outline, points);
                        }
                    }
                    groupIndex++;
                }
            }
            return bitmap;
        }

        private string SandboxRoomThumbnailPath(SceneTileSource source, RomPaletteAsset asset)
        {
            if (asset != null && !string.IsNullOrEmpty(asset.ThumbnailPath) && File.Exists(asset.ThumbnailPath))
                return asset.ThumbnailPath;
            if (sandboxCatalog == null || source == null || string.IsNullOrEmpty(sandboxCatalog.DirectoryPath)) return null;
            string path = Path.Combine(sandboxCatalog.DirectoryPath, "room-thumbnails",
                source.SceneId.ToString("X2") + "-" + source.RoomId.ToString("D2") + ".png");
            return File.Exists(path) ? path : null;
        }

        private static string SandboxRoomLocation(int scene)
        {
            return OoTSceneNames.Get(scene);
        }

        private void RefreshSandboxRooms()
        {
            RefreshSandboxSurfaces();
            sandboxRefreshing = true;
            sandboxRooms.Items.Clear();
            if (CurrentScene != null)
                for (int i = 0; i < CurrentScene.Rooms.Count; i++) sandboxRooms.Items.Add(i + " - " + CurrentScene.Rooms[i].ModelShortFilename);
            sandboxRooms.SelectedIndex = Math.Min(RoomList.SelectedIndex, sandboxRooms.Items.Count - 1);
            sandboxRefreshing = false;
            RefreshSandboxContents();
        }

        private void RefreshSandboxContents()
        {
            ZActor preserveActor = SelectedSandboxActor();
            sandboxInspector.SelectedObject = null;
            sandboxContents.Items.Clear();
            int index = RoomList.SelectedIndex;
            if (CurrentScene == null || index < 0 || index >= CurrentScene.Rooms.Count) { RefreshSandboxLayers(); return; }
            foreach (ZActor actor in CurrentScene.Rooms[index].ZActors) sandboxContents.Items.Add(CreateSandboxActorItem(actor, "Actor"));
            foreach (ZActor actor in CurrentScene.SpawnPoints.Where(a => a.SpawnRoom == index)) sandboxContents.Items.Add(CreateSandboxActorItem(actor, "Spawn"));
            foreach (ZActor actor in CurrentScene.Transitions.Where(a => a.FrontSwitchTo == index || a.BackSwitchTo == index)) sandboxContents.Items.Add(CreateSandboxActorItem(actor, "Door"));
            actorEditControl.SetActors(ref CurrentScene.Rooms[index].ZActors);
            transitionEditControl.SetActors(ref CurrentScene.Transitions);
            spawnerEditControl.SetActors(ref CurrentScene.SpawnPoints);
            authoringTreeDirty = true;
            RefreshSandboxLayers();
            if (preserveActor != null)
                SelectSandboxActor(preserveActor);
            glControl1.Invalidate();
        }

        private SandboxActorItem CreateSandboxActorItem(ZActor actor, string kind)
        {
            return new SandboxActorItem { Actor = actor, Kind = kind, DisplayName = GetSandboxActorDisplayName(actor, kind) };
        }

        private string GetSandboxActorDisplayName(ZActor actor, string kind)
        {
            if (actor == null) return kind;
            if (kind == "Spawn") return "Player Spawn";
            RoomPackageActor packageActor = (CurrentScene == null || CurrentScene.SandboxGameplayPackages == null)
                ? null : CurrentScene.SandboxGameplayPackages.SelectMany(p => (p.Actors ?? new List<RoomPackageActor>()).Concat(p.Spawns ?? new List<RoomPackageActor>())).FirstOrDefault(a => a.Id == actor.SandboxGameplayId);
            if (packageActor != null && !string.IsNullOrWhiteSpace(packageActor.Name) && !packageActor.Name.StartsWith("Actor ", StringComparison.OrdinalIgnoreCase)) return packageActor.Name;
            RomPaletteAsset asset = sandboxCatalog == null ? null : sandboxCatalog.Assets.FirstOrDefault(a => a.Id == actor.Number && a.Transition == actor.IsTransition);
            if (asset != null && !string.IsNullOrWhiteSpace(asset.Name)) return asset.Name;
            if (kind == "Door") return "Door";
            return kind + " " + actor.Number.ToString("X4");
        }

        private void RefreshSandboxLayers()
        {
            if (sandboxLayers == null) return;
            sandboxLayers.BeginUpdate(); sandboxLayers.Nodes.Clear();
            if (CurrentScene == null) { sandboxLayers.EndUpdate(); return; }
            for (int index = 0; index < CurrentScene.Rooms.Count; index++)
            {
                ZScene.ZRoom room = CurrentScene.Rooms[index];
                TreeNode root = new TreeNode("Room " + index + " - " + (string.IsNullOrEmpty(room.ModelShortFilename) ? "Untitled" : room.ModelShortFilename))
                { Tag = new SandboxLayerNode { RoomIndex = index } };
                TreeNode geometry = new TreeNode("Geometry / room tiles");
                try
                {
                    if (!string.IsNullOrEmpty(CurrentScene.SandboxGeometryFile) && File.Exists(CurrentScene.SandboxGeometryFile))
                    {
                        TileSceneGeometry tileGeometry = TileStorage.Load<TileSceneGeometry>(CurrentScene.SandboxGeometryFile);
                        foreach (PlacedTile placement in tileGeometry.PlacedTiles.Where(p => p.Room == index))
                        {
                            TreeNode tileNode = new TreeNode("Tile: " + placement.Name + " [" + placement.Turns * 90 + " deg, x" + (placement.Scale <= 0 ? 1.0 : placement.Scale).ToString("0.##") + "]")
                            { Tag = new SandboxLayerNode { RoomIndex = index, Placement = placement } };
                            List<RoomPackage> packages = (CurrentScene.SandboxGameplayPackages ?? new List<RoomPackage>()).Where(p => p.PlacementId == placement.Id).ToList();
                            int gameplayCount = packages.Sum(p => (p.Actors == null ? 0 : p.Actors.Count) + (p.Spawns == null ? 0 : p.Spawns.Count));
                            tileNode.Nodes.Add(new TreeNode("Tile gameplay / actors (" + gameplayCount + ")"));
                            geometry.Nodes.Add(tileNode);
                        }
                    }
                }
                catch { }
                if (geometry.Nodes.Count == 0) geometry.Nodes.Add(new TreeNode("No placed room tiles"));
                root.Nodes.Add(geometry);
                List<SandboxAuthoringMarker> markers = sandboxAuthoringMarkers.Where(m => m.Room == index).ToList();
                TreeNode markerLayer = new TreeNode("Authoring markers (" + markers.Count + ")");
                foreach (SandboxAuthoringMarker marker in markers)
                    markerLayer.Nodes.Add(new TreeNode((marker.Name ?? marker.Kind) + " [" + marker.Kind + "]")
                    { Tag = new SandboxLayerNode { RoomIndex = index, Marker = marker } });
                root.Nodes.Add(markerLayer);
                TreeNode actors = new TreeNode("Actors (" + room.ZActors.Count + ")");
                foreach (ZActor actor in room.ZActors)
                {
                    SandboxActorItem item = CreateSandboxActorItem(actor, "Actor");
                    actors.Nodes.Add(new TreeNode(item.ToString()) { Tag = new SandboxLayerNode { RoomIndex = index, Actor = item } });
                }
                root.Nodes.Add(actors);
                List<ZActor> doors = CurrentScene.Transitions.Where(a => a.FrontSwitchTo == index || a.BackSwitchTo == index).ToList();
                TreeNode doorLayer = new TreeNode("Doors (" + doors.Count + ")");
                foreach (ZActor actor in doors)
                {
                    SandboxActorItem item = CreateSandboxActorItem(actor, "Door");
                    doorLayer.Nodes.Add(new TreeNode(item.ToString()) { Tag = new SandboxLayerNode { RoomIndex = index, Actor = item } });
                }
                root.Nodes.Add(doorLayer);
                List<ZActor> spawns = CurrentScene.SpawnPoints.Where(a => a.SpawnRoom == index).ToList();
                TreeNode spawnLayer = new TreeNode("Spawns (" + spawns.Count + ")");
                foreach (ZActor actor in spawns)
                {
                    SandboxActorItem item = CreateSandboxActorItem(actor, "Spawn");
                    spawnLayer.Nodes.Add(new TreeNode(item.ToString()) { Tag = new SandboxLayerNode { RoomIndex = index, Actor = item } });
                }
                root.Nodes.Add(spawnLayer);
                root.Expand(); markerLayer.Expand(); actors.Expand(); doorLayer.Expand(); spawnLayer.Expand(); sandboxLayers.Nodes.Add(root);
            }
            sandboxLayers.EndUpdate();
        }

        private void ClearSandboxMaterializedGameplay()
        {
            if (CurrentScene == null || CurrentScene.SandboxGameplayPackages == null) return;
            HashSet<string> ids = new HashSet<string>(CurrentScene.SandboxGameplayPackages
                .SelectMany(p => (p.Actors ?? new List<RoomPackageActor>()).Concat(p.Spawns ?? new List<RoomPackageActor>()))
                .Select(a => a.Id).Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);
            if (ids.Count == 0) return;
            foreach (ZScene.ZRoom room in CurrentScene.Rooms)
                room.ZActors.RemoveAll(actor => ids.Contains(actor.SandboxGameplayId));
            CurrentScene.SpawnPoints.RemoveAll(actor => ids.Contains(actor.SandboxGameplayId));
            CurrentScene.Transitions.RemoveAll(actor => ids.Contains(actor.SandboxGameplayId));
        }

        private void FocusSandboxRoom()
        {
            int index = RoomList.SelectedIndex;
            if (CurrentScene == null || index < 0 || index >= CurrentScene.Rooms.Count) return;
            Vector3d center = GetRoomAuthoringCenter(CurrentScene.Rooms[index]);
            Camera.Pos = ConvertToCameraPosition(center + new Vector3d(0, 650, 1400));
            Camera.Rot = new Vector3d(25, 0, 0);
            glControl1.Invalidate();
        }

        private Vector3d SandboxDropPosition(Point point)
        {
            Vector3d start, end; GetSandboxRay(point, out start, out end);
            Vector3 hit = CurrentScene.ColModel == null ? new Vector3(-1) : MoveToCollision((Vector3)start, (Vector3)end);
            Vector3d result;
            if (hit != new Vector3(-1)) result = (Vector3d)hit;
            else
            {
                double floor = GetRoomAuthoringCenter(CurrentScene.Rooms[RoomList.SelectedIndex]).Y;
                double dy = end.Y - start.Y;
                double t = Math.Abs(dy) < 0.000001 ? -1 : (floor - start.Y) / dy;
                if (t < 0 || t > 1) throw new InvalidOperationException("Drop onto a visible surface or the room floor plane.");
                result = start + (end - start) * t;
            }
            if (sandboxSnap.Checked)
            {
                double grid = (double)sandboxGrid.Value;
                result.X = Math.Round(result.X / grid) * grid; result.Z = Math.Round(result.Z / grid) * grid;
            }
            return result;
        }

        private void GetSandboxRay(Point point, out Vector3d start, out Vector3d end)
        {
            glControl1.MakeCurrent();
            SetViewport(glControl1.Width, glControl1.Height);
            Camera.Position(); GL.Scale(0.005, 0.005, 0.005);
            Matrix4d view, projection;
            GL.GetDouble(GetPName.ModelviewMatrix, out view); GL.GetDouble(GetPName.ProjectionMatrix, out projection);
            Matrix4d inverse = Matrix4d.Invert(view * projection);
            double x = 2.0 * point.X / glControl1.Width - 1, y = 1 - 2.0 * point.Y / glControl1.Height;
            Vector4d near = Vector4d.Transform(new Vector4d(x, y, -1, 1), inverse);
            Vector4d far = Vector4d.Transform(new Vector4d(x, y, 1, 1), inverse);
            start = near.Xyz / near.W; end = far.Xyz / far.W;
        }

        internal void PlaceSandboxAsset(RomPaletteAsset asset, Vector3d position)
        {
            if (asset == null || asset.Room != null) return;
            if (asset.TilePath != null) { PlaceSandboxTile(asset.TilePath, position, sandboxTileRotation.SelectedIndex); return; }
            int index = RoomList.SelectedIndex;
            if (CurrentScene == null || index < 0 || index >= CurrentScene.Rooms.Count) throw new InvalidOperationException("Open a destination scene first.");
            if (new[] { position.X, position.Y, position.Z }.Any(x => double.IsNaN(x) || double.IsInfinity(x) || x < short.MinValue || x > short.MaxValue))
                throw new InvalidOperationException("Placement is outside the N64 coordinate range.");
            if (asset.Transition) { ShowSandboxConnectionDialog(asset, position); return; }
            if (asset.Category == "Doors") throw new InvalidOperationException("Use a scene transition from the Connections palette for room-to-room placement.");
            ZScene.ZRoom room = CurrentScene.Rooms[index];
            ushort[] required = asset.Category == "Object banks" ? new[] { asset.Id } : asset.Objects;
            if (sandboxCatalog != null && required.Any(id => id > 3 && !sandboxCatalog.AvailableObjects.Contains(id)))
                throw new InvalidOperationException("This preset requires an object bank unavailable in the source ROM.");
            SandboxRecord();
            ZActor inserted = null;
            foreach (ushort id in required.Where(id => id > 3))
                if (!room.ZObjects.Any(o => o.Value == id)) room.ZObjects.Add(new ZScene.ZUShort(id));
            if (asset.Category != "Object banks")
            {
                ZActor actor = new ZActor(asset.Id, (float)position.X, (float)position.Y, (float)position.Z,
                    asset.XRotation, asset.YRotation, asset.ZRotation, asset.Variable);
                inserted = actor;
                if (asset.Category == "Spawns") { actor.SpawnRoom = (byte)index; CurrentScene.SpawnPoints.Add(actor); }
                else room.ZActors.Add(actor);
            }
            savechanges = true;
            RefreshSandboxContents();
            if (inserted != null)
                sandboxContents.SelectedItem = sandboxContents.Items.Cast<SandboxActorItem>().FirstOrDefault(item => ReferenceEquals(item.Actor, inserted));
            sandboxStatus.Text = "Placed " + asset.Name;
        }

        private void ShowSandboxConnectionDialog(RomPaletteAsset asset, Vector3d position)
        {
            if (CurrentScene.Rooms.Count < 2) throw new InvalidOperationException("A connection needs two rooms in the destination scene.");
            using (Form dialog = new Form { Text = "Room connection", ClientSize = new Size(420, 240),
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent })
            {
                TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 5 };
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                ComboBox front = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
                ComboBox back = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
                for (int i = 0; i < Math.Min(128, CurrentScene.Rooms.Count); i++)
                {
                    string label = i + " - " + CurrentScene.Rooms[i].ModelShortFilename;
                    front.Items.Add(label); back.Items.Add(label);
                }
                front.SelectedIndex = Math.Max(0, Math.Min(RoomList.SelectedIndex, front.Items.Count - 1));
                back.SelectedIndex = front.SelectedIndex == 0 ? 1 : 0;
                NumericUpDown yaw = new NumericUpDown { Minimum = -180, Maximum = 180, DecimalPlaces = 2, Increment = 90,
                    Value = (decimal)(asset.YRotation * 360.0 / 65536), Dock = DockStyle.Fill };
                fields.Controls.Add(new Label { Text = asset.Name, AutoSize = true, MaximumSize = new Size(390, 50) }, 0, 0);
                fields.SetColumnSpan(fields.GetControlFromPosition(0, 0), 2);
                fields.Controls.Add(new Label { Text = "Front room", AutoSize = true }, 0, 1); fields.Controls.Add(front, 1, 1);
                fields.Controls.Add(new Label { Text = "Back room", AutoSize = true }, 0, 2); fields.Controls.Add(back, 1, 2);
                fields.Controls.Add(new Label { Text = "Facing (degrees)", AutoSize = true }, 0, 3); fields.Controls.Add(yaw, 1, 3);
                FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
                Button place = new Button { Text = "Place" };
                place.Click += delegate
                {
                    try { PlaceSandboxConnection(asset, position, front.SelectedIndex, back.SelectedIndex, (double)yaw.Value); dialog.DialogResult = DialogResult.OK; }
                    catch (Exception error) { MessageBox.Show(dialog, error.Message, "Room connection", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                };
                actions.Controls.Add(cancel); actions.Controls.Add(place);
                fields.Controls.Add(actions, 0, 4); fields.SetColumnSpan(actions, 2);
                dialog.Controls.Add(fields); dialog.AcceptButton = place; dialog.CancelButton = cancel;
                dialog.ShowDialog(this);
            }
        }

        internal void PlaceSandboxConnection(RomPaletteAsset asset, Vector3d position, int front, int back, double yawDegrees)
        {
            ZActor connection = RoomConnections.Prepare(CurrentScene, asset, front, back, position, yawDegrees,
                sandboxCatalog == null ? null : sandboxCatalog.AvailableObjects);
            SandboxRecord();
            RoomConnections.Add(CurrentScene, asset, connection);
            savechanges = true;
            RefreshSandboxContents();
            sandboxContents.SelectedItem = sandboxContents.Items.Cast<SandboxActorItem>().FirstOrDefault(item => ReferenceEquals(item.Actor, connection));
            sandboxStatus.Text = "Placed connection " + front + " <-> " + back + " | " + asset.Name;
        }

        private void SandboxRecord() { sandboxUndo.Push(new SandboxState(CurrentScene, sandboxAuthoringMarkers)); sandboxRedo.Clear(); }
        private void SandboxHistory(Stack<SandboxState> source, Stack<SandboxState> destination)
        {
            if (source.Count == 0 || CurrentScene == null) return;
            SandboxState state = source.Peek();
            SandboxState previous = new SandboxState(CurrentScene, sandboxAuthoringMarkers);
            if (state.GeometryFile != CurrentScene.SandboxGeometryFile) RestoreSandboxGeometry(state.GeometryFile);
            destination.Push(previous); sandboxAuthoringMarkers = source.Pop().Apply(CurrentScene); ClearSandboxFaces();
            savechanges = true; RefreshSandboxContents();
        }
        private void DeleteSandboxActor()
        {
            SandboxActorItem item = sandboxContents.SelectedItem as SandboxActorItem;
            if (item == null) return;
            SandboxRecord();
            CurrentScene.Rooms[RoomList.SelectedIndex].ZActors.Remove(item.Actor);
            CurrentScene.SpawnPoints.Remove(item.Actor); CurrentScene.Transitions.Remove(item.Actor);
            RemoveSandboxGameplayActor(item.Actor);
            savechanges = true; RefreshSandboxContents();
        }

        private void ReplaceSelectedSandboxActor()
        {
            SandboxActorItem item = sandboxContents.SelectedItem as SandboxActorItem;
            RomPaletteAsset asset = sandboxPalette.SelectedItem as RomPaletteAsset;
            if (item == null) throw new InvalidOperationException("Select an actor to replace.");
            if (asset == null || asset.Room != null || asset.TilePath != null || asset.Transition || asset.Category == "Object banks")
                throw new InvalidOperationException("Choose a palette actor, enemy, item, prop, NPC, or spawn as the replacement.");
            if (item.Actor.IsTransition) throw new InvalidOperationException("Replace room connections through the connection editor.");
            if (sandboxCatalog != null && asset.Objects.Any(id => id > 3 && !sandboxCatalog.AvailableObjects.Contains(id)))
                throw new InvalidOperationException("This replacement requires an object bank unavailable in the source ROM.");
            SandboxRecord();
            item.Actor.Number = asset.Id; item.Actor.Variable = asset.Variable;
            item.Actor.XRot = asset.XRotation; item.Actor.YRot = asset.YRotation; item.Actor.ZRot = asset.ZRotation;
            foreach (ushort id in asset.Objects.Where(id => id > 3))
                if (!CurrentScene.Rooms[RoomList.SelectedIndex].ZObjects.Any(o => o.Value == id)) CurrentScene.Rooms[RoomList.SelectedIndex].ZObjects.Add(new ZScene.ZUShort(id));
            SyncSandboxGameplayActor(item.Actor, asset);
            savechanges = true; RefreshSandboxContents();
            sandboxContents.SelectedItem = sandboxContents.Items.Cast<SandboxActorItem>().FirstOrDefault(existing => ReferenceEquals(existing.Actor, item.Actor));
            sandboxStatus.Text = "Replaced selected actor with " + asset.Name;
        }

        private void SyncSandboxGameplayActor(ZActor actor, RomPaletteAsset replacement = null)
        {
            if (actor == null || string.IsNullOrEmpty(actor.SandboxGameplayId) || CurrentScene.SandboxGameplayPackages == null) return;
            foreach (RoomPackage package in CurrentScene.SandboxGameplayPackages)
            {
                RoomPackageActor entry = package.Actors.Concat(package.Spawns).FirstOrDefault(a => a.Id == actor.SandboxGameplayId);
                if (entry == null) continue;
                entry.Number = actor.Number; entry.Variable = actor.Variable;
                entry.Position = new Vector3d(actor.XPos, actor.YPos, actor.ZPos);
                entry.Rotation = new Vector3(actor.XRot, actor.YRot, actor.ZRot);
                if (replacement != null)
                {
                    entry.Name = replacement.Name; entry.Role = replacement.Category; entry.RequirementText = replacement.RequirementText;
                    entry.RequiredObjects = replacement.Objects == null ? new List<ushort>() : replacement.Objects.Distinct().ToList();
                    RoomPackageBuilder.PopulateMetadata(entry, replacement, actor);
                }
                return;
            }
        }

        private void RemoveSandboxGameplayActor(ZActor actor)
        {
            if (actor == null || string.IsNullOrEmpty(actor.SandboxGameplayId) || CurrentScene.SandboxGameplayPackages == null) return;
            foreach (RoomPackage package in CurrentScene.SandboxGameplayPackages)
            {
                string id = actor.SandboxGameplayId;
                package.Actors.RemoveAll(a => a.Id == id); package.Spawns.RemoveAll(a => a.Id == id);
                package.Links.RemoveAll(link => link.SourceId == id || link.TargetId == id);
            }
        }

        private void LinkSandboxActors()
        {
            if (CurrentScene == null || RoomList.SelectedIndex < 0) throw new InvalidOperationException("Open a destination room first.");
            List<SandboxActorItem> actors = sandboxContents.Items.Cast<SandboxActorItem>().ToList();
            if (actors.Count < 2) throw new InvalidOperationException("Place or extract at least two actors before creating a link.");
            using (Form dialog = new Form { Text = "Link gameplay actors", ClientSize = new Size(520, 300), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false })
            {
                TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 2, RowCount = 6 };
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                ComboBox source = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
                ComboBox target = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
                ComboBox kind = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
                TextBox parameter = new TextBox { Dock = DockStyle.Fill };
                source.Items.AddRange(actors.ToArray()); target.Items.AddRange(actors.ToArray()); kind.Items.AddRange(Enum.GetNames(typeof(LogicLinkKind)));
                source.SelectedIndex = Math.Max(0, actors.FindIndex(a => a.Actor == SelectedSandboxActor())); target.SelectedIndex = target.Items.Count > 1 ? 1 : 0; kind.SelectedItem = LogicLinkKind.Opens.ToString();
                fields.Controls.Add(new Label { Text = "When / source", AutoSize = true }, 0, 0); fields.Controls.Add(source, 1, 0);
                fields.Controls.Add(new Label { Text = "Then / target", AutoSize = true }, 0, 1); fields.Controls.Add(target, 1, 1);
                fields.Controls.Add(new Label { Text = "Action", AutoSize = true }, 0, 2); fields.Controls.Add(kind, 1, 2);
                fields.Controls.Add(new Label { Text = "Parameter", AutoSize = true }, 0, 3); fields.Controls.Add(parameter, 1, 3);
                Label hint = new Label { Text = "Example: enemy clear -> door, switch -> chest, plate -> platform", AutoSize = true };
                fields.Controls.Add(hint, 0, 4); fields.SetColumnSpan(hint, 2);
                FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
                Button link = new Button { Text = "Link", DialogResult = DialogResult.OK };
                actions.Controls.Add(cancel); actions.Controls.Add(link); fields.Controls.Add(actions, 0, 5); fields.SetColumnSpan(actions, 2); dialog.Controls.Add(fields);
                link.Click += delegate
                {
                    if (source.SelectedItem == null || target.SelectedItem == null || source.SelectedItem == target.SelectedItem) { MessageBox.Show(dialog, "Choose two different actors.", "Gameplay link"); dialog.DialogResult = DialogResult.None; return; }
                    SandboxActorItem sourceItem = (SandboxActorItem)source.SelectedItem, targetItem = (SandboxActorItem)target.SelectedItem;
                    LogicLinkKind linkKind = (LogicLinkKind)Enum.Parse(typeof(LogicLinkKind), (string)kind.SelectedItem);
                    SandboxRecord();
                    RoomPackage package = GetSandboxLogicPackage();
                    EnsureSandboxLogicActor(package, sourceItem.Actor); EnsureSandboxLogicActor(package, targetItem.Actor);
                    LogicLinkGraph.Add(package, sourceItem.Actor.SandboxGameplayId, targetItem.Actor.SandboxGameplayId, linkKind, parameter.Text);
                    savechanges = true; sandboxStatus.Text = "Linked " + sourceItem.Actor.Number.ToString("X4") + " -> " + targetItem.Actor.Number.ToString("X4") + " (" + linkKind + ")";
                    RefreshSandboxContents();
                };
                dialog.ShowDialog(this);
            }
        }

        private void ConfigureSelectedSandboxGameplay()
        {
            ZActor actor = SelectedSandboxActor();
            if (actor == null) throw new InvalidOperationException("Select an actor, spawn, door, reward, puzzle, or obstacle first.");
            if (CurrentScene == null || RoomList.SelectedIndex < 0) throw new InvalidOperationException("Open a destination room first.");
            RomPaletteAsset asset = sandboxCatalog == null ? null : sandboxCatalog.Assets.FirstOrDefault(a => a.Id == actor.Number && (a.Transition == actor.IsTransition || !actor.IsTransition));
            RoomPackageActor entry = EnsureSandboxGameplayActorPackage(actor, asset);
            using (Form dialog = new Form { Text = "Configure gameplay actor", ClientSize = new Size(820, 620), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.Sizable, MinimizeBox = false, MaximizeBox = false })
            {
                TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 2, RowCount = 7 };
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                TextBox name = new TextBox { Text = entry.Name ?? "", Dock = DockStyle.Fill };
                TextBox role = new TextBox { Text = entry.Role ?? "Actor", Dock = DockStyle.Fill };
                TextBox requirement = new TextBox { Text = entry.RequirementText ?? "", Dock = DockStyle.Fill };
                TextBox requiredItems = new TextBox { Text = string.Join(", ", entry.RequiredItems ?? new List<string>()), Dock = DockStyle.Fill };
                TextBox grantedItems = new TextBox { Text = string.Join(", ", entry.GrantedItems ?? new List<string>()), Dock = DockStyle.Fill };
                DataGridView flags = CreateGameplayFlagGrid(entry.Flags);
                fields.Controls.Add(new Label { Text = "User-facing name", AutoSize = true }, 0, 0); fields.Controls.Add(name, 1, 0);
                fields.Controls.Add(new Label { Text = "Gameplay role", AutoSize = true }, 0, 1); fields.Controls.Add(role, 1, 1);
                fields.Controls.Add(new Label { Text = "Requirement", AutoSize = true }, 0, 2); fields.Controls.Add(requirement, 1, 2);
                fields.Controls.Add(new Label { Text = "Required items", AutoSize = true }, 0, 3); fields.Controls.Add(requiredItems, 1, 3);
                fields.Controls.Add(new Label { Text = "Granted rewards/items", AutoSize = true }, 0, 4); fields.Controls.Add(grantedItems, 1, 4);
                fields.Controls.Add(new Label { Text = "Flags and switches", AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, 0, 5); fields.Controls.Add(flags, 1, 5);
                Label hint = new Label { Text = "Use one row per flag. Kind must match the compatible setter/checker kind. Values accept decimal or 0x-prefixed hexadecimal.", AutoSize = true, MaximumSize = new Size(630, 45) };
                fields.Controls.Add(hint, 0, 6); fields.SetColumnSpan(hint, 2);
                FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.RightToLeft };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
                Button apply = new Button { Text = "Apply gameplay", AutoSize = true };
                apply.Click += delegate
                {
                    try
                    {
                        List<DungeonFlagReference> updatedFlags = ReadGameplayFlagGrid(flags);
                        SandboxRecord();
                        entry.Name = name.Text.Trim(); entry.Role = role.Text.Trim(); entry.RequirementText = requirement.Text.Trim();
                        entry.RequiredItems = ParseGameplayList(requiredItems.Text); entry.GrantedItems = ParseGameplayList(grantedItems.Text); entry.Flags = updatedFlags;
                        SyncSandboxGameplayActor(actor);
                        NativeLogicLinkMaterializer.Apply(CurrentScene, sandboxCatalog);
                        savechanges = true; RefreshSandboxContents(); SelectSandboxActor(actor); sandboxStatus.Text = "Configured gameplay: " + entry.Name;
                        dialog.DialogResult = DialogResult.OK;
                    }
                    catch (Exception error) { MessageBox.Show(dialog, error.Message, "Gameplay actor", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                };
                actions.Controls.Add(cancel); actions.Controls.Add(apply); dialog.Controls.Add(fields); dialog.Controls.Add(actions);
                dialog.AcceptButton = apply; dialog.CancelButton = cancel; dialog.ShowDialog(this);
            }
        }

        private RoomPackageActor EnsureSandboxGameplayActorPackage(ZActor actor, RomPaletteAsset asset)
        {
            if (string.IsNullOrEmpty(actor.SandboxGameplayId)) actor.SandboxGameplayId = "manual_room_" + RoomList.SelectedIndex + "_actor_" + Guid.NewGuid().ToString("N");
            foreach (RoomPackage package in CurrentScene.SandboxGameplayPackages ?? new List<RoomPackage>())
            {
                RoomPackageActor existing = package.Actors.Concat(package.Spawns).FirstOrDefault(a => a.Id == actor.SandboxGameplayId);
                if (existing != null) return existing;
            }
            if (CurrentScene.SandboxGameplayPackages == null) CurrentScene.SandboxGameplayPackages = new List<RoomPackage>();
            RoomPackage packageForRoom = CurrentScene.SandboxGameplayPackages.FirstOrDefault(p => !p.LogicOnly && p.DestinationRoom == RoomList.SelectedIndex);
            if (packageForRoom == null)
            {
                packageForRoom = new RoomPackage { Scene = CurrentScene.SceneNumber, Room = RoomList.SelectedIndex, DestinationRoom = RoomList.SelectedIndex,
                    Name = "Room " + RoomList.SelectedIndex + " gameplay authoring" };
                CurrentScene.SandboxGameplayPackages.Add(packageForRoom);
            }
            RoomPackageActor result = new RoomPackageActor
            {
                Id = actor.SandboxGameplayId, Role = actor.IsTransition ? "Doors" : (asset == null ? "Actor" : asset.Category),
                Name = asset == null ? "Actor " + actor.Number.ToString("X4") : asset.Name,
                SourceRoom = RoomList.SelectedIndex, Number = actor.Number, Variable = actor.Variable,
                Position = new Vector3d(actor.XPos, actor.YPos, actor.ZPos), Rotation = new Vector3(actor.XRot, actor.YRot, actor.ZRot),
                IsTransition = actor.IsTransition, FrontRoom = actor.FrontSwitchTo, BackRoom = actor.BackSwitchTo,
                FrontCamera = actor.FrontCamera, BackCamera = actor.BackCamera,
                RequirementText = asset == null ? null : asset.RequirementText,
                RequiredObjects = asset == null || asset.Objects == null ? new List<ushort>() : asset.Objects.Distinct().ToList()
            };
            RoomPackageBuilder.PopulateMetadata(result, asset, actor);
            if (CurrentScene.SpawnPoints.Contains(actor)) packageForRoom.Spawns.Add(result); else packageForRoom.Actors.Add(result);
            return result;
        }

        private static List<string> ParseGameplayList(string text)
        {
            return (text ?? "").Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static DataGridView CreateGameplayFlagGrid(IEnumerable<DungeonFlagReference> values)
        {
            DataGridView grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
            grid.Columns.Add("Kind", "Kind"); grid.Columns.Add("Property", "Property"); grid.Columns.Add("Target", "Target"); grid.Columns.Add("Value", "Value");
            grid.Columns.Add("Mask", "Mask"); grid.Columns.Add("Position", "Bit");
            DataGridViewCheckBoxColumn set = new DataGridViewCheckBoxColumn { Name = "CanSet", HeaderText = "Sets" }; grid.Columns.Add(set);
            DataGridViewCheckBoxColumn check = new DataGridViewCheckBoxColumn { Name = "CanCheck", HeaderText = "Checks" }; grid.Columns.Add(check);
            foreach (DungeonFlagReference flag in values ?? Enumerable.Empty<DungeonFlagReference>())
                grid.Rows.Add(flag.Kind, flag.Property, flag.Target, FormatGameplayInteger(flag.Value), FormatGameplayInteger(flag.Mask), flag.Position, flag.CanSet, flag.CanCheck);
            return grid;
        }

        private static List<DungeonFlagReference> ReadGameplayFlagGrid(DataGridView grid)
        {
            List<DungeonFlagReference> result = new List<DungeonFlagReference>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                string kind = Convert.ToString(row.Cells["Kind"].Value).Trim();
                if (kind.Length == 0) continue;
                int value = ParseGameplayInteger(row.Cells["Value"].Value, "Value");
                int mask = ParseGameplayInteger(row.Cells["Mask"].Value, "Mask");
                int position = ParseGameplayInteger(row.Cells["Position"].Value, "Bit");
                result.Add(new DungeonFlagReference { Kind = kind, Property = Convert.ToString(row.Cells["Property"].Value), Target = Convert.ToString(row.Cells["Target"].Value), Value = value, Mask = (ushort)mask, Position = position,
                    CanSet = Convert.ToBoolean(row.Cells["CanSet"].Value ?? false), CanCheck = Convert.ToBoolean(row.Cells["CanCheck"].Value ?? false), Evidence = "Manual gameplay authoring" });
            }
            return result;
        }

        private static int ParseGameplayInteger(object value, string label)
        {
            string text = Convert.ToString(value).Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                int parsedHex; if (int.TryParse(text.Substring(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out parsedHex)) return parsedHex;
            }
            int parsed; if (int.TryParse(text, out parsed)) return parsed;
            throw new InvalidOperationException(label + " must be a decimal number or 0x-prefixed hexadecimal value.");
        }

        private static string FormatGameplayInteger(int value)
        {
            return "0x" + value.ToString("X");
        }

        private RoomPackage GetSandboxLogicPackage()
        {
            RoomPackage package = (CurrentScene.SandboxGameplayPackages ?? new List<RoomPackage>()).FirstOrDefault(p => p.LogicOnly && p.Scene == CurrentScene.SceneNumber);
            if (package != null) return package;
            if (CurrentScene.SandboxGameplayPackages == null) CurrentScene.SandboxGameplayPackages = new List<RoomPackage>();
            package = new RoomPackage { Scene = CurrentScene.SceneNumber, Room = RoomList.SelectedIndex, DestinationRoom = RoomList.SelectedIndex, LogicOnly = true, Name = "Manual gameplay links" };
            CurrentScene.SandboxGameplayPackages.Add(package); return package;
        }

        private void EnsureSandboxLogicActor(RoomPackage package, ZActor actor)
        {
            if (string.IsNullOrEmpty(actor.SandboxGameplayId)) actor.SandboxGameplayId = "manual_room_" + RoomList.SelectedIndex + "_actor_" + Guid.NewGuid().ToString("N");
            if (package.Actors.Concat(package.Spawns).Any(a => a.Id == actor.SandboxGameplayId)) return;
            package.Actors.Add(new RoomPackageActor
            {
                Id = actor.SandboxGameplayId, Role = actor.IsTransition ? "Doors" : "Actor", Name = "Actor " + actor.Number.ToString("X4"),
                SourceRoom = RoomList.SelectedIndex, Number = actor.Number, Variable = actor.Variable,
                Position = new Vector3d(actor.XPos, actor.YPos, actor.ZPos), Rotation = new Vector3(actor.XRot, actor.YRot, actor.ZRot), IsTransition = actor.IsTransition,
                FrontRoom = actor.FrontSwitchTo, BackRoom = actor.BackSwitchTo, FrontCamera = actor.FrontCamera, BackCamera = actor.BackCamera
            });
            RomPaletteAsset asset = sandboxCatalog == null ? null : sandboxCatalog.Assets.FirstOrDefault(a => a.Id == actor.Number && !a.Transition);
            RoomPackageBuilder.PopulateMetadata(package.Actors[package.Actors.Count - 1], asset, actor);
        }
        private string GetSandboxProject64Path()
        {
            string configuredPath = sandboxPlaytest.GetConfiguredExecutable();
            sandboxProject64Path = configuredPath;
            return configuredPath;
        }

        private void ConfigureSandboxProject64()
        {
            using (OpenFileDialog picker = new OpenFileDialog
            {
                Filter = "Project64 executable (Project64.exe)|Project64.exe|Executable (*.exe)|*.exe",
                Title = "Choose Project64"
            })
            {
                string configuredPath = GetSandboxProject64Path();
                if (!string.IsNullOrEmpty(configuredPath)) picker.FileName = configuredPath;
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                sandboxPlaytest.SetExecutable(picker.FileName);
                sandboxProject64Path = sandboxPlaytest.GetConfiguredExecutable();
                sandboxStatus.Text = "Project64 configured: " + Path.GetFileName(sandboxProject64Path);
            }
        }

        private string CreateInjectedSandboxRomCopy()
        {
            if (string.IsNullOrEmpty(sandboxRomPath)) throw new InvalidOperationException("Open the source ROM first.");
            MaterializeSandboxGameplay();
            List<string> errors = SceneBuilderValidation.Check(CurrentScene).Where(x => x.StartsWith("ERROR:")).ToList();
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "OoT ROM (*.z64)|*.z64", FileName = "Archarina64-build.z64" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return null;
                if (string.Equals(Path.GetFullPath(dialog.FileName), sandboxRomPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Choose a separate output ROM.");
                File.Copy(sandboxRomPath, dialog.FileName, true);
                InjectToRom(dialog.FileName);
                return dialog.FileName;
            }
        }

        private void InjectSandboxCopy()
        {
            CreateInjectedSandboxRomCopy();
        }

        private void InjectAndLaunchSandboxProject64()
        {
            string emulatorPath = GetSandboxProject64Path();
            if (string.IsNullOrEmpty(emulatorPath))
            {
                ConfigureSandboxProject64();
                emulatorPath = GetSandboxProject64Path();
            }
            if (string.IsNullOrEmpty(emulatorPath)) return;
            string romPath = CreateInjectedSandboxRomCopy();
            if (string.IsNullOrEmpty(romPath)) return;
            sandboxPlaytest.Launch(romPath);
            sandboxStatus.Text = "Launched Project64 with " + Path.GetFileName(romPath);
        }
        private void MaterializeSandboxGameplay()
        {
            List<string> issues = NativeGameplayMaterializer.Apply(CurrentScene);
            issues.AddRange(NativeLogicLinkMaterializer.Apply(CurrentScene, sandboxCatalog));
            issues.AddRange(SceneBuilderValidation.Check(CurrentScene).Where(x => x.StartsWith("ERROR:")));
            List<string> errors = issues.Where(x => x.StartsWith("ERROR:")).Distinct().ToList();
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
            if (issues.Count > 0) sandboxStatus.Text = "Native gameplay export prepared with " + issues.Count + " review item" + (issues.Count == 1 ? "" : "s");
            savechanges = true; RefreshSandboxContents();
        }
        private void ShowSandboxValidation(List<string> issues, string title)
        {
            using (Form report = new Form { Text = "Archarina64 - " + title, Width = 820, Height = 520, StartPosition = FormStartPosition.CenterParent })
            {
                report.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                    ScrollBars = ScrollBars.Both, WordWrap = false, Text = issues.Count == 0 ? "No issues found." : string.Join(Environment.NewLine, issues.ToArray()) });
                report.ShowDialog(this);
            }
        }

        private void ShowSandboxPlaytestPreflight()
        {
            MaterializeSandboxGameplay();
            List<string> issues = SceneBuilderValidation.Check(CurrentScene);
            string emulator = GetSandboxProject64Path();
            List<string> report = new List<string>
            {
                "Archarina64 PLAYTEST PREFLIGHT",
                "",
                "Source ROM: " + (string.IsNullOrEmpty(sandboxRomPath) ? "Not loaded" : Path.GetFileName(sandboxRomPath)),
                "Project64: " + (string.IsNullOrEmpty(emulator) ? "Not configured" : emulator),
                "Native rooms: " + (CurrentScene == null ? "0" : CurrentScene.Rooms.Count.ToString()),
                "Native transitions: " + (CurrentScene == null ? "0" : CurrentScene.Transitions.Count.ToString()),
                "",
                "Resolve every ERROR before injection. REVIEW items are warnings or runtime checks that still need confirmation in Project64.",
                ""
            };
            if (issues.Count == 0) report.Add("PASS: No validation or gameplay-review issues were reported.");
            else report.AddRange(issues);
            if (string.IsNullOrEmpty(sandboxRomPath)) report.Add("ERROR: Open the source ROM before injecting a playtest copy.");
            if (string.IsNullOrEmpty(emulator)) report.Add("REVIEW: Configure Project64 before using Inject and launch in Project64.");
            ShowSandboxValidation(report, "Playtest preflight");
        }
        private void SandboxRun(Action action)
        {
            try { action(); }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Archarina64", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void RenderSandboxGrid()
        {
            if (sandboxRoot == null || sandboxSnap == null || !sandboxSnap.Checked || CurrentScene == null || RoomList.SelectedIndex < 0) return;
            Vector3d center = GetRoomAuthoringCenter(CurrentScene.Rooms[RoomList.SelectedIndex]);
            double step = (double)sandboxGrid.Value;
            double cx = Math.Round(center.X / step) * step, cz = Math.Round(center.Z / step) * step;
            GL.PushAttrib(AttribMask.EnableBit | AttribMask.CurrentBit | AttribMask.LineBit);
            GL.MatrixMode(MatrixMode.Modelview); GL.PushMatrix(); GL.LoadIdentity();
            Camera.Position(); GL.Scale(0.005, 0.005, 0.005);
            GL.Disable(EnableCap.Texture2D); GL.Disable(EnableCap.Lighting);
            GL.Color3(0.35, 0.48, 0.46); GL.LineWidth(1);
            GL.Begin(PrimitiveType.Lines);
            for (int i = -20; i <= 20; i++)
            {
                GL.Vertex3(cx + i * step, center.Y + 0.5, cz - 20 * step); GL.Vertex3(cx + i * step, center.Y + 0.5, cz + 20 * step);
                GL.Vertex3(cx - 20 * step, center.Y + 0.5, cz + i * step); GL.Vertex3(cx + 20 * step, center.Y + 0.5, cz + i * step);
            }
            GL.End(); GL.PopMatrix(); GL.PopAttrib();
        }

        internal void CaptureSandboxPreview(string path)
        {
            glControl1.Refresh();
            glControl1.MakeCurrent();
            int width = glControl1.Width, height = glControl1.Height;
            using (Bitmap viewport = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            {
                var data = viewport.LockBits(new Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.WriteOnly, viewport.PixelFormat);
                GL.ReadBuffer(ReadBufferMode.Front);
                GL.ReadPixels(0, 0, width, height, OpenTK.Graphics.OpenGL.PixelFormat.Bgr, PixelType.UnsignedByte, data.Scan0);
                viewport.UnlockBits(data);
                viewport.RotateFlip(RotateFlipType.RotateNoneFlipY);
                using (Bitmap frame = new Bitmap(sandboxRoot.Width, sandboxRoot.Height))
                {
                    sandboxRoot.DrawToBitmap(frame, sandboxRoot.ClientRectangle);
                    using (Graphics graphics = Graphics.FromImage(frame))
                        graphics.DrawImageUnscaled(viewport, sandboxRoot.PointToClient(glControl1.PointToScreen(Point.Empty)));
                    frame.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
        }
    }
}
