using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SharpOcarina
{
    public partial class MainForm
    {
        private sealed class SandboxCommand
        {
            internal string Category, Name, Description;
            internal Action Execute;
            public override string ToString() { return Category + "  |  " + Name; }
        }

        private void ShowSandboxCommandPalette()
        {
            List<SandboxCommand> commands = new List<SandboxCommand>
            {
                new SandboxCommand { Category = "Layout", Name = "Rooms and room geometry", Description = "Open native room groups, room settings, and geometry controls.", Execute = delegate { ShowSandboxNativeTab("tabRooms", "Rooms and room geometry"); } },
                new SandboxCommand { Category = "Layout", Name = "Scene settings and cameras", Description = "Open scene number, cameras, waterboxes, and scene-level settings.", Execute = delegate { ShowSandboxNativeTab("tabGeneral", "Scene settings and cameras"); } },
                new SandboxCommand { Category = "Layout", Name = "Scene environments", Description = "Open scene environment headers, skyboxes, lighting, and alternate headers.", Execute = delegate { ShowSandboxNativeTab("tabSceneEnv", "Scene environments"); } },
                new SandboxCommand { Category = "Layout", Name = "Room environments", Description = "Open room-specific lighting, skybox, and environment controls.", Execute = delegate { ShowSandboxNativeTab("tabRoomEnv", "Room environments"); } },
                new SandboxCommand { Category = "Layout", Name = "Camera effects", Description = "Edit camera effects and room camera behavior.", Execute = delegate { ShowSandboxNativeTab("tabCameraEffect", "Camera effects"); } },
                new SandboxCommand { Category = "Traversal", Name = "Transitions and spawns", Description = "Edit doors, room transitions, spawn points, and actor cutscene data.", Execute = delegate { ShowSandboxNativeTab("tabTransitions", "Transitions and spawns"); } },
                new SandboxCommand { Category = "Traversal", Name = "Collision and exits", Description = "Edit collision bounds, polygon types, exits, and traversal flags.", Execute = delegate { ShowSandboxNativeTab("tabCollision", "Collision and exits"); } },
                new SandboxCommand { Category = "Traversal", Name = "Paths and patrols", Description = "Edit native OoT pathways and patrol points.", Execute = delegate { ShowSandboxNativeTab("tabPathways", "Paths and patrols"); } },
                new SandboxCommand { Category = "Gameplay", Name = "Objects and actors", Description = "Edit room objects, scene objects, actors, NPCs, enemies, items, and props.", Execute = delegate { ShowSandboxNativeTab("tabActors", "Objects and actors"); } },
                new SandboxCommand { Category = "Gameplay", Name = "Cutscenes", Description = "Edit native cutscene commands and actor cutscene data.", Execute = delegate { ShowSandboxNativeTab("tabCutscene", "Cutscenes"); } },
                new SandboxCommand { Category = "Rendering", Name = "Conditional draw", Description = "Edit conditional room draw and visibility settings.", Execute = delegate { ShowSandboxNativeTab("tabConditionalDraw", "Conditional draw"); } },
                new SandboxCommand { Category = "Rendering", Name = "Color blending", Description = "Edit native color blending and scene color behavior.", Execute = delegate { ShowSandboxNativeTab("tabColorBlending", "Color blending"); } },
                new SandboxCommand { Category = "Gameplay", Name = "Link selected actors", Description = "Create a cause-and-effect gameplay link without code.", Execute = delegate { SandboxRun(LinkSandboxActors); } },
                new SandboxCommand { Category = "Geometry", Name = "Extract room tiles", Description = "Extract the current or selected scenes into reusable room tiles.", Execute = delegate { SandboxRun(ExtractSandboxRoomLibrary); } },
                new SandboxCommand { Category = "Geometry", Name = "Auto-chop entrances", Description = "Split the open room into entrance-aware tile regions.", Execute = delegate { SandboxRun(AutoChopSandboxEntrances); } },
                new SandboxCommand { Category = "Geometry", Name = "New empty dungeon workspace", Description = "Start an empty authoring scene for assembling room tiles.", Execute = delegate { SandboxRun(CreateEmptyDungeonWorkspace); } },
                new SandboxCommand { Category = "Geometry", Name = "Add empty dungeon room", Description = "Add another blank native room for multi-room dungeon assembly.", Execute = delegate { SandboxRun(AddEmptyDungeonRoom); } },
                new SandboxCommand { Category = "Geometry", Name = "Move selected room bundle", Description = "Move a room together with its collision, actors, doors, spawns, sockets, and gameplay.", Execute = delegate { SandboxRun(MoveSelectedSandboxRoom); } },
                new SandboxCommand { Category = "Geometry", Name = "Texture animations", Description = "Open native texture animation controls.", Execute = delegate { ShowSandboxNativeTab("tabAnimations", "Texture animations"); } },
                new SandboxCommand { Category = "Rendering", Name = "Texture scrolling", Description = "Edit native texture scrolling and animated tile behavior.", Execute = delegate { ShowSandboxNativeTab("tabTextureScroll", "Texture scrolling"); } },
                new SandboxCommand { Category = "Rendering", Name = "Texture swapping", Description = "Edit native texture swap tables.", Execute = delegate { ShowSandboxNativeTab("tabTextureSwap", "Texture swapping"); } },
                new SandboxCommand { Category = "Rendering", Name = "Texture swap frames", Description = "Edit frame-by-frame texture swap behavior.", Execute = delegate { ShowSandboxNativeTab("tabTextureSwapFrames", "Texture swap frames"); } },
                new SandboxCommand { Category = "Rendering", Name = "Polygon type swapping", Description = "Edit native collision polygon type swaps.", Execute = delegate { ShowSandboxNativeTab("tabSwapPolytype", "Polygon type swapping"); } },
                new SandboxCommand { Category = "Build", Name = "Validate scene", Description = "Check native scene structure, actors, transitions, collision, and links.", Execute = delegate { ShowSandboxValidation(SceneBuilderValidation.Check(CurrentScene), "Scene validation"); } },
                new SandboxCommand { Category = "Build", Name = "Review gameplay and tile assembly", Description = "Review unresolved puzzle links, tile boundaries, and native materialization warnings.", Execute = delegate { ShowSandboxValidation(RoomPackageReview.CheckScene(CurrentScene), "Gameplay and tile assembly review"); } },
                new SandboxCommand { Category = "Playtest", Name = "Playtest preflight", Description = "Combine native validation, gameplay review, ROM status, and Project64 readiness before injection.", Execute = delegate { SandboxRun(ShowSandboxPlaytestPreflight); } },
                new SandboxCommand { Category = "Build", Name = "Save scene project", Description = "Save the editable Archarina64 scene project.", Execute = delegate { SandboxRun(() => { PersistSandboxGeometry(); saveSceneToolStripMenuItem_Click(this, EventArgs.Empty); }); } },
                new SandboxCommand { Category = "Build", Name = "Export native scene package", Description = "Export native scene data without packaging the source ROM.", Execute = delegate { SandboxRun(() => { MaterializeSandboxGameplay(); saveBinaryToolStripMenuItem_Click(this, EventArgs.Empty); }); } },
                new SandboxCommand { Category = "Build", Name = "Inject into ROM copy", Description = "Inject the edited scene into a user-selected ROM copy.", Execute = delegate { SandboxRun(InjectSandboxCopy); } },
                new SandboxCommand { Category = "Playtest", Name = "Configure Project64", Description = "Choose and remember the Project64 executable used for playtesting.", Execute = delegate { SandboxRun(ConfigureSandboxProject64); } },
                new SandboxCommand { Category = "Playtest", Name = "Inject and launch in Project64", Description = "Build a separate ROM copy, inject the scene, and launch it in Project64.", Execute = delegate { SandboxRun(InjectAndLaunchSandboxProject64); } }
            };
            using (Form dialog = new Form { Text = "Command palette", ClientSize = new Size(720, 480), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, KeyPreview = true })
            {
                TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
                layout.Controls.Add(new Label { Text = "Search native controls and authoring actions", Dock = DockStyle.Fill, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Padding = new Padding(0, 5, 0, 0) }, 0, 0);
                TextBox search = new TextBox { Dock = DockStyle.Fill };
                ListBox results = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
                Label hint = new Label { Text = "Enter opens the selected command. Escape closes this palette.", Dock = DockStyle.Fill, ForeColor = Color.Gray, Padding = new Padding(0, 5, 0, 0) };
                layout.Controls.Add(search, 0, 1); layout.Controls.Add(results, 0, 2); layout.Controls.Add(hint, 0, 3); dialog.Controls.Add(layout);
                Action refresh = delegate
                {
                    string filter = search.Text.Trim();
                    results.BeginUpdate(); results.Items.Clear();
                    foreach (SandboxCommand command in commands.Where(c => filter.Length == 0 || (c.Category + " " + c.Name + " " + c.Description).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)) results.Items.Add(command);
                    if (results.Items.Count > 0) results.SelectedIndex = 0;
                    results.EndUpdate();
                };
                search.TextChanged += delegate { refresh(); };
                search.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; dialog.DialogResult = DialogResult.OK; } };
                results.DoubleClick += delegate { dialog.DialogResult = DialogResult.OK; };
                dialog.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { dialog.DialogResult = DialogResult.Cancel; e.SuppressKeyPress = true; } };
                refresh();
                if (dialog.ShowDialog(this) == DialogResult.OK && results.SelectedItem is SandboxCommand selected)
                {
                    try { selected.Execute(); } catch (Exception error) { MessageBox.Show(this, error.Message, selected.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
            }
        }

        private void ShowSandboxNativeTab(string tabName, string title)
        {
            if (tabControl1 == null) throw new InvalidOperationException("Native SharpOcarina controls are not available.");
            TabPage page = tabControl1.TabPages.Cast<TabPage>().FirstOrDefault(tab => tab.Name == tabName);
            if (page == null) throw new InvalidOperationException("The native control group could not be found: " + tabName);
            UpdateForm();
            Control originalParent = tabControl1.Parent;
            int originalIndex = originalParent == null ? 0 : originalParent.Controls.GetChildIndex(tabControl1);
            DockStyle originalDock = tabControl1.Dock;
            if (originalParent != null) originalParent.Controls.Remove(tabControl1);
            try
            {
                using (Form dialog = new Form { Text = "Native controls - " + title, ClientSize = new Size(760, 680), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = true })
                {
                    tabControl1.Dock = DockStyle.Fill; tabControl1.Visible = true; tabControl1.SelectedTab = page; dialog.Controls.Add(tabControl1); dialog.ShowDialog(this);
                }
            }
            finally
            {
                if (tabControl1.Parent != null) tabControl1.Parent.Controls.Remove(tabControl1);
                if (originalParent != null) { originalParent.Controls.Add(tabControl1); originalParent.Controls.SetChildIndex(tabControl1, originalIndex); }
                tabControl1.Dock = originalDock; tabControl1.Visible = false;
            }
        }
    }
}
