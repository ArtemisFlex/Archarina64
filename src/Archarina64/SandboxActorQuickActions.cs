using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenTK;

namespace SharpOcarina
{
    public partial class MainForm
    {
        private enum SandboxActorRole
        {
            Unknown,
            Door,
            Chest,
            Enemy,
            Boss,
            Item,
            Switch,
            PressurePlate,
            Torch,
            Target,
            NPC,
            Platform,
            Path,
            Cutscene,
            Spawn
        }

        private sealed class SandboxQuickAction
        {
            internal string Text;
            internal string Description;
            internal LogicLinkKind Kind;
            internal string Parameter;
            internal SandboxActorRole[] SourceRoles;

            internal SandboxQuickAction(string text, string description, LogicLinkKind kind, string parameter, params SandboxActorRole[] sourceRoles)
            {
                Text = text; Description = description; Kind = kind; Parameter = parameter; SourceRoles = sourceRoles;
            }
        }

        private RomPaletteAsset FindSandboxActorAsset(ZActor actor)
        {
            if (sandboxCatalog == null || actor == null) return null;
            return sandboxCatalog.Assets.FirstOrDefault(asset => asset.Room == null && !asset.Transition && asset.Id == actor.Number && asset.Variable == actor.Variable)
                ?? sandboxCatalog.Assets.FirstOrDefault(asset => asset.Room == null && !asset.Transition && asset.Id == actor.Number);
        }

        private SandboxActorRole GetSandboxActorRole(ZActor actor)
        {
            if (actor == null) return SandboxActorRole.Unknown;
            if (actor.IsTransition) return SandboxActorRole.Door;
            if (actor.SpawnRoom != 0xFF && actor.Number == 0) return SandboxActorRole.Spawn;
            RomPaletteAsset asset = FindSandboxActorAsset(actor);
            string category = asset == null ? "" : asset.Category ?? "";
            string name = asset == null ? "" : asset.Name ?? "";
            string text = (category + " " + name).ToLowerInvariant();
            if (category.Equals("Doors", StringComparison.OrdinalIgnoreCase)) return SandboxActorRole.Door;
            if (category.Equals("Chests", StringComparison.OrdinalIgnoreCase) || text.Contains("chest")) return SandboxActorRole.Chest;
            if (category.Equals("Bosses", StringComparison.OrdinalIgnoreCase)) return SandboxActorRole.Boss;
            if (category.Equals("Enemies", StringComparison.OrdinalIgnoreCase)) return SandboxActorRole.Enemy;
            if (category.Equals("Items", StringComparison.OrdinalIgnoreCase)) return SandboxActorRole.Item;
            if (category.Equals("NPCs", StringComparison.OrdinalIgnoreCase)) return SandboxActorRole.NPC;
            if (category.Equals("Switches", StringComparison.OrdinalIgnoreCase))
                return text.Contains("plate") || text.Contains("pressure") ? SandboxActorRole.PressurePlate : SandboxActorRole.Switch;
            if (text.Contains("torch") || text.Contains("flame") || text.Contains("fire")) return SandboxActorRole.Torch;
            if (text.Contains("target") || text.Contains("eye switch")) return SandboxActorRole.Target;
            if (text.Contains("platform") || text.Contains("elevator") || text.Contains("moving")) return SandboxActorRole.Platform;
            if (text.Contains("cutscene")) return SandboxActorRole.Cutscene;
            if (text.Contains("path") || text.Contains("track")) return SandboxActorRole.Path;
            return SandboxActorRole.Unknown;
        }

        private List<SandboxQuickAction> SandboxQuickActionsFor(ZActor target)
        {
            switch (GetSandboxActorRole(target))
            {
                case SandboxActorRole.Door:
                    return new List<SandboxQuickAction>
                    {
                        new SandboxQuickAction("Unlock when enemy is defeated", "Defeating an enemy or boss unlocks this door.", LogicLinkKind.Unlocks, "enemy defeated", SandboxActorRole.Enemy, SandboxActorRole.Boss),
                        new SandboxQuickAction("Unlock with item or key", "Obtaining a key or required item unlocks this door.", LogicLinkKind.Unlocks, "item or key", SandboxActorRole.Item),
                        new SandboxQuickAction("Open from switch", "Activating a switch opens this door.", LogicLinkKind.Opens, "switch activated", SandboxActorRole.Switch),
                        new SandboxQuickAction("Open from pressure plate", "Pressing a plate opens this door.", LogicLinkKind.Opens, "pressure plate pressed", SandboxActorRole.PressurePlate),
                        new SandboxQuickAction("Open when target is shot", "Activating a target opens this door.", LogicLinkKind.Opens, "target activated", SandboxActorRole.Target),
                        new SandboxQuickAction("Open after puzzle completion", "Completing a puzzle opens this door.", LogicLinkKind.CompletesPuzzle, "puzzle complete", SandboxActorRole.Switch, SandboxActorRole.PressurePlate)
                    };
                case SandboxActorRole.Chest:
                    return new List<SandboxQuickAction>
                    {
                        new SandboxQuickAction("Appear when enemy is defeated", "Defeating an enemy makes this chest appear.", LogicLinkKind.Spawns, "enemy defeated", SandboxActorRole.Enemy, SandboxActorRole.Boss),
                        new SandboxQuickAction("Appear from switch", "Activating a switch makes this chest appear.", LogicLinkKind.Spawns, "switch activated", SandboxActorRole.Switch, SandboxActorRole.PressurePlate),
                        new SandboxQuickAction("Grant item when opened", "Opening this chest grants its configured item.", LogicLinkKind.GrantsItem, "chest opened", SandboxActorRole.Item)
                    };
                case SandboxActorRole.Platform:
                    return new List<SandboxQuickAction>
                    {
                        new SandboxQuickAction("Enable from switch", "Activating a switch enables this platform.", LogicLinkKind.Enables, "switch activated", SandboxActorRole.Switch, SandboxActorRole.PressurePlate),
                        new SandboxQuickAction("Start from path", "Starting a path moves this platform.", LogicLinkKind.StartsPath, "path started", SandboxActorRole.Path)
                    };
                case SandboxActorRole.Cutscene:
                    return new List<SandboxQuickAction>
                    {
                        new SandboxQuickAction("Start when enemy is defeated", "Defeating an enemy starts this cutscene.", LogicLinkKind.StartsCutscene, "enemy defeated", SandboxActorRole.Enemy, SandboxActorRole.Boss),
                        new SandboxQuickAction("Start from switch", "Activating a switch starts this cutscene.", LogicLinkKind.StartsCutscene, "switch activated", SandboxActorRole.Switch, SandboxActorRole.PressurePlate),
                        new SandboxQuickAction("Start from NPC dialogue", "Completing NPC dialogue starts this cutscene.", LogicLinkKind.StartsCutscene, "dialogue complete", SandboxActorRole.NPC)
                    };
                case SandboxActorRole.Enemy:
                case SandboxActorRole.Boss:
                    return new List<SandboxQuickAction>
                    {
                        new SandboxQuickAction("Spawn from switch", "Activating a switch spawns or enables this enemy.", LogicLinkKind.Spawns, "switch activated", SandboxActorRole.Switch, SandboxActorRole.PressurePlate),
                        new SandboxQuickAction("Enable from cutscene", "A cutscene enables this enemy or boss.", LogicLinkKind.Enables, "cutscene complete", SandboxActorRole.Cutscene)
                    };
                case SandboxActorRole.Torch:
                    return new List<SandboxQuickAction>
                    {
                        new SandboxQuickAction("Light from switch", "Activating a switch lights this torch.", LogicLinkKind.Activates, "switch activated", SandboxActorRole.Switch, SandboxActorRole.PressurePlate),
                        new SandboxQuickAction("Light from target", "Activating a target lights this torch.", LogicLinkKind.Activates, "target activated", SandboxActorRole.Target)
                    };
                default:
                    return new List<SandboxQuickAction>
                    {
                        new SandboxQuickAction("Activate from switch", "A switch activates the selected actor.", LogicLinkKind.Activates, "switch activated", SandboxActorRole.Switch, SandboxActorRole.PressurePlate),
                        new SandboxQuickAction("Enable after puzzle", "Completing a puzzle enables the selected actor.", LogicLinkKind.Enables, "puzzle complete", SandboxActorRole.Switch, SandboxActorRole.PressurePlate),
                        new SandboxQuickAction("Start from cutscene", "A cutscene starts or enables the selected actor.", LogicLinkKind.StartsCutscene, "cutscene complete", SandboxActorRole.Cutscene)
                    };
            }
        }

        private List<SandboxActorItem> SandboxQuickSources(SandboxQuickAction action, ZActor target)
        {
            HashSet<SandboxActorRole> roles = new HashSet<SandboxActorRole>(action.SourceRoles);
            return sandboxContents.Items.Cast<SandboxActorItem>().Where(item => item.Actor != target && roles.Contains(GetSandboxActorRole(item.Actor))).ToList();
        }

        private List<RomPaletteAsset> SandboxQuickPaletteSources(SandboxQuickAction action)
        {
            HashSet<SandboxActorRole> roles = new HashSet<SandboxActorRole>(action.SourceRoles);
            return (sandboxCatalog == null ? Enumerable.Empty<RomPaletteAsset>() : sandboxCatalog.Assets)
                .Where(asset => asset.Room == null && !asset.Transition && roles.Contains(GetSandboxActorRole(new ZActor { Number = asset.Id, Variable = asset.Variable })))
                .GroupBy(asset => asset.Id.ToString("X4") + ":" + asset.Variable.ToString("X4"))
                .Select(group => group.First()).OrderBy(asset => asset.Name).ToList();
        }

        private void ShowSandboxActorQuickMenu(Point location)
        {
            if (sandboxContents == null) return;
            ZActor target = SelectedSandboxActor();
            if (target == null)
            {
                sandboxStatus.Text = "Select an actor first, then right-click it for compatible gameplay actions.";
                return;
            }
            SandboxActorRole role = GetSandboxActorRole(target);
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem heading = new ToolStripMenuItem("Quick links for " + role + " " + target.Number.ToString("X4")) { Enabled = false };
            menu.Items.Add(heading);
            ToolStripMenuItem links = new ToolStripMenuItem("Link compatible gameplay...");
            foreach (SandboxQuickAction action in SandboxQuickActionsFor(target))
            {
                List<SandboxActorItem> sources = SandboxQuickSources(action, target);
                List<RomPaletteAsset> paletteSources = SandboxQuickPaletteSources(action);
                ToolStripMenuItem item = new ToolStripMenuItem(action.Text) { ToolTipText = action.Description, Enabled = sources.Count > 0 || paletteSources.Count > 0 };
                if (sources.Count == 0 && paletteSources.Count == 0) item.ToolTipText = action.Description + " No compatible source is currently available.";
                item.Click += delegate { ShowSandboxQuickLinkDialog(target, action, sources, paletteSources); };
                links.DropDownItems.Add(item);
            }
            menu.Items.Add(links);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Configure gameplay metadata...", null, delegate { SandboxRun(ConfigureSelectedSandboxGameplay); });
            menu.Items.Add("Replace selected from palette...", null, delegate { SandboxRun(ReplaceSelectedSandboxActor); });
            menu.Items.Add("Open full gameplay link editor...", null, delegate { SandboxRun(LinkSandboxActors); });
            menu.Items.Add("Delete selected actor", null, delegate { DeleteSandboxActor(); });
            menu.Show(location);
        }

        private void ShowSandboxQuickLinkDialog(ZActor target, SandboxQuickAction action, List<SandboxActorItem> sources, List<RomPaletteAsset> paletteSources)
        {
            if ((sources == null || sources.Count == 0) && (paletteSources == null || paletteSources.Count == 0)) return;
            using (Form dialog = new Form { Text = action.Text, ClientSize = new Size(560, sources.Count > 0 ? 230 : 270), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = sources.Count > 0 ? 5 : 6 };
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                fields.Controls.Add(new Label { Text = "Selected target", AutoSize = true }, 0, 0); fields.Controls.Add(new Label { Text = target.Number.ToString("X4") + " - " + GetSandboxActorRole(target), AutoSize = true }, 1, 0);
                fields.Controls.Add(new Label { Text = sources.Count > 0 ? "When this source" : "Place compatible source", AutoSize = true }, 0, 1);
                ComboBox source = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
                ComboBox palette = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
                if (sources.Count > 0) { source.Items.AddRange(sources.ToArray()); source.SelectedIndex = 0; fields.Controls.Add(source, 1, 1); }
                else { palette.Items.AddRange(paletteSources.ToArray()); palette.SelectedIndex = 0; fields.Controls.Add(palette, 1, 1); }
                fields.Controls.Add(new Label { Text = "Action", AutoSize = true }, 0, 2); fields.Controls.Add(new Label { Text = action.Kind + " - " + action.Parameter, AutoSize = true }, 1, 2);
                TextBox parameter = new TextBox { Dock = DockStyle.Fill, Text = action.Parameter }; fields.Controls.Add(new Label { Text = "Parameter", AutoSize = true }, 0, 3); fields.Controls.Add(parameter, 1, 3);
                FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true }; Button link = new Button { Text = sources.Count > 0 ? "Create link" : "Place source and link", AutoSize = true };
                link.Click += delegate
                {
                    try
                    {
                        ZActor sourceActor;
                        if (sources.Count > 0)
                        {
                            SandboxActorItem sourceItem = source.SelectedItem as SandboxActorItem;
                            if (sourceItem == null) return;
                            sourceActor = sourceItem.Actor;
                        }
                        else
                        {
                            RomPaletteAsset asset = palette.SelectedItem as RomPaletteAsset;
                            if (asset == null) return;
                            Vector3d position = new Vector3d(target.XPos + 150, target.YPos, target.ZPos);
                            PlaceSandboxAsset(asset, position);
                            sourceActor = CurrentScene.Rooms[RoomList.SelectedIndex].ZActors.LastOrDefault(actor => actor.Number == asset.Id && actor.Variable == asset.Variable && Math.Abs(actor.XPos - position.X) < 0.1 && Math.Abs(actor.YPos - position.Y) < 0.1 && Math.Abs(actor.ZPos - position.Z) < 0.1);
                            if (sourceActor == null) throw new InvalidOperationException("The compatible source actor could not be placed in the current room.");
                        }
                        SandboxRecord(); RoomPackage package = GetSandboxLogicPackage();
                        EnsureSandboxLogicActor(package, sourceActor); EnsureSandboxLogicActor(package, target);
                        LogicLinkGraph.Add(package, sourceActor.SandboxGameplayId, target.SandboxGameplayId, action.Kind, parameter.Text);
                        savechanges = true; RefreshSandboxContents(); sandboxStatus.Text = "Linked " + GetSandboxActorRole(sourceActor) + " -> " + GetSandboxActorRole(target) + " (" + action.Kind + ")"; dialog.DialogResult = DialogResult.OK;
                    }
                    catch (Exception error) { MessageBox.Show(dialog, error.Message, "Gameplay link", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                };
                actions.Controls.Add(cancel); actions.Controls.Add(link); fields.Controls.Add(actions, 0, 4); fields.SetColumnSpan(actions, 2);
                dialog.Controls.Add(fields); dialog.AcceptButton = link; dialog.CancelButton = cancel; dialog.ShowDialog(this);
            }
        }
    }
}
