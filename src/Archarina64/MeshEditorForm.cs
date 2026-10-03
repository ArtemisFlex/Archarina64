using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using SharpOcarina.Shared;

namespace SharpOcarina
{
    internal sealed class MeshEditorForm : Form
    {
        private enum EditMode { Vertex, Edge, Face }
        private enum WorkspaceMode { Object, Edit, VertexPaint, UV }
        private enum TransformGizmo { Move, Rotate, Scale }
        private enum ViewPlane { XY, XZ, YZ }

        private readonly MainForm owner;
        private readonly ZScene targetScene;
        private readonly ArcharinaSceneDocument targetDocument;
        private readonly int roomIndex;
        private readonly int groupIndex;
        private readonly ObjFile model;
        private readonly ObjFile.Group sourceGroup;
        private readonly MeshEditorData mesh;
        private readonly GLControl viewport;
        private readonly Label status;
        private readonly ToolTip toolTips = new ToolTip { AutoPopDelay = 9000, InitialDelay = 500, ReshowDelay = 200, ShowAlways = true };
        private EditMode mode = EditMode.Vertex;
        private WorkspaceMode workspaceMode = WorkspaceMode.Object;
        private TransformGizmo activeGizmo = TransformGizmo.Move;
        private ViewPlane viewPlane = ViewPlane.XY;
        private bool perspectiveView = true;
        private bool wireframeView;
        private bool unlitView;
        private bool surfaceCulling;
        private double orbitYaw = 45.0;
        private double orbitPitch = 25.0;
        private double orbitDistance = 500.0;
        private bool orbiting;
        private bool dragging;
        private bool panning;
        private bool dragUndoSaved;
        private char dragConstraint;
        private Point lastMouse;
        private double minX, maxX, minY, maxY;
        private readonly Stack<MeshEditorData> undoStack = new Stack<MeshEditorData>();
        private readonly Stack<MeshEditorData> redoStack = new Stack<MeshEditorData>();

        public MeshEditorForm(MainForm owner)
            : this(owner, -1, -1, null, null)
        {
        }

        public MeshEditorForm(MainForm owner, int roomIndex, int groupIndex, ObjFile model, ObjFile.Group group)
        {
            this.owner = owner;
            this.targetScene = MainForm.CurrentScene;
            this.targetDocument = MainForm.CurrentDocument;
            this.roomIndex = roomIndex;
            this.groupIndex = groupIndex;
            this.model = model;
            this.sourceGroup = group;
            surfaceCulling = group != null && group.BackfaceCulling;
            mesh = model == null || group == null
                ? MeshEditorData.CreateDefaultCube(100.0)
                : MeshEditorData.FromGroup(model, group);
            Text = model == null || group == null ? "Archarina64 Modeling Editor" : "Archarina64 Mesh Editor";
            if (owner != null && owner.Icon != null)
                Icon = new Icon(owner.Icon, owner.Icon.Size);
            else
            {
                string iconPath = Path.Combine(Application.StartupPath, "Archarina64.ico");
                if (File.Exists(iconPath)) Icon = new Icon(iconPath);
            }
            StartPosition = FormStartPosition.CenterParent;
            Width = 980;
            Height = 720;
            KeyPreview = true;

            FlowLayoutPanel toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Width = 980,
                Height = 72,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(4),
                BackColor = Color.FromArgb(235, 235, 235),
                BorderStyle = BorderStyle.FixedSingle
            };
            Label toolHeader = new Label
            {
                Text = "Mesh Tools  |  drag",
                Width = 165,
                Height = 24,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                BackColor = Color.FromArgb(205, 210, 216),
                Cursor = Cursors.SizeAll
            };
            Point toolDragOffset = Point.Empty;
            toolHeader.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) toolDragOffset = e.Location;
            };
            toolHeader.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                Point location = toolbar.Location;
                location.Offset(e.X - toolDragOffset.X, e.Y - toolDragOffset.Y);
                location.X = Math.Max(0, Math.Min(ClientSize.Width - toolbar.Width, location.X));
                location.Y = Math.Max(0, Math.Min(ClientSize.Height - 30, location.Y));
                toolbar.Location = location;
            };
            toolbar.Controls.Add(toolHeader);
            Button vertex = ToolButton("Vertex", "vertex", delegate { mode = EditMode.Vertex; UpdateStatus(); });
            Button edge = ToolButton("Edge", "edge", delegate { mode = EditMode.Edge; UpdateStatus(); });
            Button face = ToolButton("Face", "face", delegate { mode = EditMode.Face; UpdateStatus(); });
            toolTips.SetToolTip(vertex, "Vertex selection (1)");
            toolTips.SetToolTip(edge, "Edge selection (2)");
            toolTips.SetToolTip(face, "Face selection (3)");
            Button objectMode = ToolButton("Object Mode", "object-mode", delegate { SetWorkspaceMode(WorkspaceMode.Object); });
            Button editMode = ToolButton("Edit Mode", "edit-mode", delegate { SetWorkspaceMode(WorkspaceMode.Edit); });
            Button vertexPaintMode = ToolButton("Vertex Paint", "vertex-paint", delegate { SetWorkspaceMode(WorkspaceMode.VertexPaint); });
            Button uvMode = ToolButton("UV Editing", "uv-editing", delegate { SetWorkspaceMode(WorkspaceMode.UV); });
            Button gizmoMove = ToolButton("Move gizmo", "move", delegate { SetActiveGizmo(TransformGizmo.Move, "Move gizmo active"); });
            Button gizmoRotate = ToolButton("Rotate gizmo", "rotate", delegate { SetActiveGizmo(TransformGizmo.Rotate, "Rotate gizmo active"); });
            Button gizmoScale = ToolButton("Scale gizmo", "scale", delegate { SetActiveGizmo(TransformGizmo.Scale, "Scale gizmo active"); });
            Button perspective = null;
            perspective = ToolButton("Perspective camera", "perspective", delegate
            {
                perspectiveView = !perspectiveView;
                if (perspectiveView) FitPerspectiveCamera();
                perspective.Image = ArcharinaModelerIcons.Load(perspectiveView ? "perspective" : "orthographic");
                toolTips.SetToolTip(perspective, perspectiveView ? "Perspective camera" : "Orthographic camera");
                viewport.Invalidate();
                UpdateStatus();
            });
            ComboBox orthographicView = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 84, Height = 28 };
            orthographicView.Items.AddRange(new object[] { "XY", "XZ", "YZ" });
            orthographicView.SelectedIndex = 0;
            orthographicView.SelectedIndexChanged += delegate
            {
                perspectiveView = false;
                viewPlane = (ViewPlane)orthographicView.SelectedIndex;
                perspective.Image = ArcharinaModelerIcons.Load("orthographic");
                toolTips.SetToolTip(perspective, "Orthographic camera");
                FitMesh(); viewport.Invalidate(); UpdateStatus(orthographicView.SelectedItem + " orthographic");
            };
            Button wireframe = null;
            wireframe = ToolButton("Wireframe: Off", "edit-mode", delegate
            {
                wireframeView = !wireframeView;
                wireframe.Text = "Wireframe: " + (wireframeView ? "On" : "Off");
                wireframe.Tag = wireframeView;
                viewport.Invalidate();
            });
            Button unlit = null;
            unlit = ToolButton("Unlit: Off", "object-mode", delegate
            {
                unlitView = !unlitView;
                unlit.Text = "Unlit: " + (unlitView ? "On" : "Off");
                unlit.Tag = unlitView;
                viewport.Invalidate();
            });
            Button culling = null;
            culling = ToolButton("Surface culling: Off", "face", delegate
            {
                surfaceCulling = !surfaceCulling;
                culling.Text = "Surface culling: " + (surfaceCulling ? "On" : "Off");
                culling.Tag = surfaceCulling;
                viewport.Invalidate();
            });
            wireframe.Tag = wireframeView;
            unlit.Tag = unlitView;
            culling.Tag = surfaceCulling;
            Button extrude = ToolButton("Extrude selected", "extrude", delegate
            {
                if (PromoteCurrentSelectionToFaces())
                {
                    SaveUndoState();
                    mesh.ExtrudeSelectedFaces(25.0);
                    FitMesh();
                    viewport.Invalidate();
                    UpdateStatus();
                }
            });
            Button inset = ToolButton("Inset selected", "inset", delegate
            {
                if (PromoteCurrentSelectionToFaces())
                {
                    SaveUndoState();
                    mesh.InsetSelectedFaces(0.35);
                    FitMesh();
                    viewport.Invalidate();
                    UpdateStatus();
                }
            });
            Button subdivide = ToolButton("Subdivide faces", "face", delegate
            {
                if (!PromoteCurrentSelectionToFaces())
                {
                    UpdateStatus("Select geometry to subdivide");
                    return;
                }
                SaveUndoState();
                mesh.SubdivideSelectedFaces();
                FitMesh();
                viewport.Invalidate();
                UpdateStatus();
            });
            Button delete = ToolButton("Delete", "delete", delegate
            {
                if (workspaceMode != WorkspaceMode.Edit) { UpdateStatus("Delete is available in Edit mode"); return; }
                SaveUndoState();
                mesh.DeleteSelection();
                FitMesh();
                viewport.Invalidate();
                UpdateStatus();
            });
            Button duplicate = ToolButton("Duplicate", "duplicate", delegate
            {
                if (!PromoteCurrentSelectionToFaces())
                {
                    UpdateStatus("Select geometry to duplicate");
                    return;
                }
                SaveUndoState();
                mesh.DuplicateSelection();
                viewport.Invalidate();
                UpdateStatus();
            });
            Button flip = ToolButton("Flip winding", "flip", delegate
            {
                if (!PromoteCurrentSelectionToFaces())
                {
                    UpdateStatus("Select geometry to flip");
                    return;
                }
                SaveUndoState();
                mesh.FlipSelectedFaces();
                viewport.Invalidate();
                UpdateStatus();
            });
            Button merge = ToolButton("Merge nearby", "align", delegate
            {
                SaveUndoState();
                mesh.MergeNearbyVertices(0.5);
                FitMesh();
                viewport.Invalidate();
                UpdateStatus();
            });
            Button mergeSelected = ToolButton("Merge selected", "align", delegate
            {
                if (workspaceMode != WorkspaceMode.Edit || mode != EditMode.Vertex || mesh.SelectedVertices.Count < 2)
                {
                    UpdateStatus("Select 2+ vertices in Vertex mode");
                    return;
                }
                SaveUndoState();
                mesh.MergeSelectedVertices();
                FitMesh();
                viewport.Invalidate();
                UpdateStatus("Merged selected vertices");
            });
            ComboBox material = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
            material.Items.Add("Default");
            if (model != null && model.Materials != null)
                foreach (ObjFile.Material entry in model.Materials)
                    if (entry != null && !string.IsNullOrEmpty(entry.Name) && !material.Items.Contains(entry.Name)) material.Items.Add(entry.Name);
            material.SelectedIndex = 0;
            Button makeFace = ToolButton("Make face", "face", delegate
            {
                if (workspaceMode != WorkspaceMode.Edit || mode != EditMode.Vertex || mesh.SelectedVertices.Count < 3)
                {
                    UpdateStatus("Select 3+ vertices in Vertex mode");
                    return;
                }
                SaveUndoState();
                if (mesh.CreateFaceFromSelectedVertices(material.SelectedItem as string))
                {
                    viewport.Invalidate();
                    UpdateStatus("Created face fan");
                }
            });
            Button makeQuad = ToolButton("Make quad", "face", delegate
            {
                if (workspaceMode != WorkspaceMode.Edit || mode != EditMode.Vertex || mesh.SelectedVertices.Count != 4)
                {
                    UpdateStatus("Select exactly 4 vertices in Vertex mode");
                    return;
                }
                SaveUndoState();
                if (mesh.CreateQuadFromSelectedVertices(material.SelectedItem as string))
                {
                    viewport.Invalidate();
                    UpdateStatus("Created quad");
                }
            });
            NumericUpDown moveX = DeltaField();
            NumericUpDown moveY = DeltaField();
            NumericUpDown moveZ = DeltaField();
            Button move = ToolButton("Move XYZ", "move", delegate
            {
                if (mesh.SelectedVertices.Count == 0 && mesh.SelectedEdges.Count == 0 && mesh.SelectedFaces.Count == 0) return;
                SaveUndoState();
                mesh.TranslateSelection(new Vector3d((double)moveX.Value, (double)moveY.Value, (double)moveZ.Value));
                moveX.Value = 0; moveY.Value = 0; moveZ.Value = 0;
                FitMesh();
                viewport.Invalidate();
                UpdateStatus();
            });
            NumericUpDown scaleX = ScaleField();
            NumericUpDown scaleY = ScaleField();
            NumericUpDown scaleZ = ScaleField();
            Button scale = ToolButton("Scale XYZ", "scale", delegate
            {
                if (mesh.SelectedVertices.Count == 0 && mesh.SelectedEdges.Count == 0 && mesh.SelectedFaces.Count == 0) return;
                SaveUndoState();
                mesh.ScaleSelection(new Vector3d((double)scaleX.Value, (double)scaleY.Value, (double)scaleZ.Value));
                scaleX.Value = 1; scaleY.Value = 1; scaleZ.Value = 1;
                FitMesh();
                viewport.Invalidate();
                UpdateStatus();
            });
            NumericUpDown rotateX = RotateField();
            NumericUpDown rotateY = RotateField();
            NumericUpDown rotateZ = RotateField();
            Button rotate = ToolButton("Rotate XYZ", "rotate", delegate
            {
                if (mesh.SelectedVertices.Count == 0 && mesh.SelectedEdges.Count == 0 && mesh.SelectedFaces.Count == 0) return;
                SaveUndoState();
                mesh.RotateSelection(new Vector3d((double)rotateX.Value, (double)rotateY.Value, (double)rotateZ.Value));
                rotateX.Value = 0; rotateY.Value = 0; rotateZ.Value = 0;
                FitMesh();
                viewport.Invalidate();
                UpdateStatus();
            });
            Button assignMaterial = ToolButton("Assign material", "assign-material", delegate
            {
                if (workspaceMode != WorkspaceMode.Edit) { UpdateStatus("Material editing is available in Edit mode"); return; }
                if (mesh.SelectedFaces.Count == 0) return;
                SaveUndoState();
                mesh.AssignMaterial(material.SelectedItem as string);
                viewport.Invalidate();
                UpdateStatus();
            });
            Button selectAll = ToolButton("Select all", "select-all", delegate { SelectAllCurrentMode(); viewport.Invalidate(); UpdateStatus(); });
            Button clearSelection = ToolButton("Clear selection", "clear-selection", delegate { mesh.ClearSelection(); viewport.Invalidate(); UpdateStatus(); });
            Button linked = ToolButton("Select linked", "select-linked", delegate
            {
                if (workspaceMode != WorkspaceMode.Edit) { UpdateStatus("Selection tools are available in Edit mode"); return; }
                if (mode == EditMode.Vertex) mesh.SelectLinked(MeshEditorData.EditSelectionMode.Vertex);
                else if (mode == EditMode.Edge) mesh.SelectLinked(MeshEditorData.EditSelectionMode.Edge);
                else mesh.SelectLinked(MeshEditorData.EditSelectionMode.Face);
                viewport.Invalidate();
                UpdateStatus();
            });
            Button grow = ToolButton("Grow selection", "grow-selection", delegate
            {
                if (workspaceMode != WorkspaceMode.Edit) { UpdateStatus("Selection tools are available in Edit mode"); return; }
                if (mode == EditMode.Vertex) mesh.GrowSelection(MeshEditorData.EditSelectionMode.Vertex);
                else if (mode == EditMode.Edge) mesh.GrowSelection(MeshEditorData.EditSelectionMode.Edge);
                else mesh.GrowSelection(MeshEditorData.EditSelectionMode.Face);
                viewport.Invalidate();
                UpdateStatus();
            });
            Button shrink = ToolButton("Shrink selection", "shrink-selection", delegate
            {
                if (workspaceMode != WorkspaceMode.Edit) { UpdateStatus("Selection tools are available in Edit mode"); return; }
                if (mode == EditMode.Vertex) mesh.ShrinkSelection(MeshEditorData.EditSelectionMode.Vertex);
                else if (mode == EditMode.Edge) mesh.ShrinkSelection(MeshEditorData.EditSelectionMode.Edge);
                else mesh.ShrinkSelection(MeshEditorData.EditSelectionMode.Face);
                viewport.Invalidate();
                UpdateStatus();
            });
            Button edgeLoop = ToolButton("Select edge loop", "edge", delegate
            {
                if (workspaceMode != WorkspaceMode.Edit) { UpdateStatus("Loop tools are available in Edit mode"); return; }
                if (mode != EditMode.Edge) { UpdateStatus("Switch to Edge mode first"); return; }
                mesh.SelectEdgeLoop();
                viewport.Invalidate();
                UpdateStatus("Selected edge loop");
            });
            Button faceLoop = ToolButton("Select face loop", "face", delegate
            {
                if (workspaceMode != WorkspaceMode.Edit) { UpdateStatus("Loop tools are available in Edit mode"); return; }
                if (mode != EditMode.Face) { UpdateStatus("Switch to Face mode first"); return; }
                mesh.SelectFaceLoop();
                viewport.Invalidate();
                UpdateStatus("Selected face loop");
            });
            Button closeLoop = ToolButton("Close selected loop", "close-editor", delegate
            {
                if (!PromoteCurrentSelectionToFaces())
                {
                    UpdateStatus("Select an open face boundary");
                    return;
                }
                SaveUndoState();
                if (!mesh.CloseSelectedBoundaryLoop())
                {
                    Undo();
                    UpdateStatus("Select an open face boundary");
                    return;
                }
                FitMesh();
                viewport.Invalidate();
                UpdateStatus("Closed selected loop");
            });
            Button frameSelected = ToolButton("Frame selected", "frame-selected", delegate { FitSelection(); viewport.Invalidate(); UpdateStatus(); });
            Button undo = ToolButton("Undo", "undo", delegate { Undo(); });
            Button redo = ToolButton("Redo", "redo", delegate { Redo(); });
            Button saveAsset = ToolButton("Save asset", "save-asset", delegate
            {
                using (SaveFileDialog dialog = new SaveFileDialog
                {
                    Filter = "Wavefront OBJ (*.obj)|*.obj|All files (*.*)|*.*",
                    DefaultExt = "obj",
                    AddExtension = true,
                    InitialDirectory = owner.GetGlobalAuthoredGeometryDirectory(),
                    FileName = "AuthoredMesh.obj"
                })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        mesh.SaveObj(dialog.FileName, System.IO.Path.GetFileNameWithoutExtension(dialog.FileName));
                        string assetPath = System.IO.Path.ChangeExtension(dialog.FileName, ".sog.xml");
                        AuthoredMeshAssetIO.Save(assetPath,
                            System.IO.Path.GetFileNameWithoutExtension(dialog.FileName), mesh, model, sourceGroup);
                        if (owner != null) owner.GetAuthoredGeometryRepository().NotifyAssetChanged(assetPath);
                        UpdateStatus("Saved asset");
                    }
                }
            });
            Button commit = ToolButton("Commit to room", "commit-room", delegate
            {
                if (roomIndex < 0 || MainForm.CurrentScene == null)
                {
                    UpdateStatus("Standalone asset: save it to the authored model library");
                    return;
                }
                if (!ReferenceEquals(MainForm.CurrentScene, targetScene) ||
                    (targetDocument != null && !ReferenceEquals(MainForm.CurrentDocument, targetDocument)) ||
                    !owner.IsMeshEditorTargetCurrent(roomIndex, groupIndex, model, sourceGroup))
                {
                    UpdateStatus("The source room changed; reopen the mesh editor before committing");
                    return;
                }
                owner.CommitMeshEditorData(roomIndex, groupIndex, mesh);
                UpdateStatus("Committed to room");
            });
            Button close = ToolButton("Close", "close-editor", delegate { Close(); });
            toolTips.SetToolTip(gizmoMove, "Move gizmo (G)");
            toolTips.SetToolTip(gizmoRotate, "Rotate gizmo (R)");
            toolTips.SetToolTip(gizmoScale, "Scale gizmo (S)");
            toolTips.SetToolTip(extrude, "Extrude selected geometry (E)");
            toolTips.SetToolTip(inset, "Inset selected faces (I)");
            toolTips.SetToolTip(duplicate, "Duplicate selected geometry (D)");
            toolTips.SetToolTip(frameSelected, "Frame selected geometry (F)");
            toolTips.SetToolTip(wireframe, "Toggle wireframe display (W)");
            toolTips.SetToolTip(unlit, "Toggle unlit display (U)");
            toolTips.SetToolTip(selectAll, "Select all geometry (Ctrl+A)");
            ContextMenuStrip quickMenu = new ContextMenuStrip();
            ToolStripMenuItem quickExtrude = new ToolStripMenuItem("Extrude selected", null, delegate { extrude.PerformClick(); });
            ToolStripMenuItem quickInset = new ToolStripMenuItem("Inset selected", null, delegate { inset.PerformClick(); });
            ToolStripMenuItem quickMakeFace = new ToolStripMenuItem("Make face", null, delegate { makeFace.PerformClick(); });
            ToolStripMenuItem quickMakeQuad = new ToolStripMenuItem("Make quad", null, delegate { makeQuad.PerformClick(); });
            ToolStripMenuItem quickSubdivide = new ToolStripMenuItem("Subdivide faces", null, delegate { subdivide.PerformClick(); });
            ToolStripMenuItem quickDuplicate = new ToolStripMenuItem("Duplicate", null, delegate { duplicate.PerformClick(); });
            ToolStripMenuItem quickFlip = new ToolStripMenuItem("Flip winding", null, delegate { flip.PerformClick(); });
            ToolStripMenuItem quickDelete = new ToolStripMenuItem("Delete", null, delegate { delete.PerformClick(); });
            ToolStripMenuItem quickMerge = new ToolStripMenuItem("Merge selected vertices", null, delegate { mergeSelected.PerformClick(); });
            ToolStripMenuItem quickSelectAll = new ToolStripMenuItem("Select all", null, delegate { selectAll.PerformClick(); });
            ToolStripMenuItem quickClear = new ToolStripMenuItem("Clear selection", null, delegate { clearSelection.PerformClick(); });
            ToolStripMenuItem quickLinked = new ToolStripMenuItem("Select linked", null, delegate { linked.PerformClick(); });
            ToolStripMenuItem quickGrow = new ToolStripMenuItem("Grow selection", null, delegate { grow.PerformClick(); });
            ToolStripMenuItem quickShrink = new ToolStripMenuItem("Shrink selection", null, delegate { shrink.PerformClick(); });
            ToolStripMenuItem quickEdgeLoop = new ToolStripMenuItem("Select edge loop", null, delegate { edgeLoop.PerformClick(); });
            ToolStripMenuItem quickFaceLoop = new ToolStripMenuItem("Select face loop", null, delegate { faceLoop.PerformClick(); });
            ToolStripMenuItem quickCloseLoop = new ToolStripMenuItem("Close selected loop", null, delegate { closeLoop.PerformClick(); });
            ToolStripMenuItem quickFrame = new ToolStripMenuItem("Frame selected", null, delegate { frameSelected.PerformClick(); });
            ToolStripMenuItem quickFit = new ToolStripMenuItem("Frame all", null, delegate { FitMesh(); viewport.Invalidate(); });
            ToolStripMenuItem quickPerspective = new ToolStripMenuItem("Toggle perspective", null, delegate { perspective.PerformClick(); });
            ToolStripMenuItem quickUndo = new ToolStripMenuItem("Undo", null, delegate { undo.PerformClick(); });
            ToolStripMenuItem quickRedo = new ToolStripMenuItem("Redo", null, delegate { redo.PerformClick(); });
            ToolStripMenuItem quickCommit = new ToolStripMenuItem("Commit to room", null, delegate { commit.PerformClick(); });
            quickMenu.Items.Add(new ToolStripLabel("Selection"));
            quickMenu.Items.AddRange(new ToolStripItem[] { quickSelectAll, quickClear, quickLinked, quickGrow, quickShrink, quickEdgeLoop, quickFaceLoop, quickCloseLoop, quickFrame, quickFit });
            quickMenu.Items.Add(new ToolStripSeparator());
            quickMenu.Items.Add(new ToolStripLabel("Topology"));
            quickMenu.Items.AddRange(new ToolStripItem[] { quickExtrude, quickInset, quickMakeFace, quickMakeQuad, quickSubdivide, quickDuplicate, quickFlip, quickMerge, quickDelete });
            quickMenu.Items.Add(new ToolStripSeparator());
            quickMenu.Items.AddRange(new ToolStripItem[] { quickPerspective, quickUndo, quickRedo, quickCommit });
            quickMenu.Opening += delegate
            {
                bool hasSelection = mesh.SelectedVertices.Count > 0 || mesh.SelectedEdges.Count > 0 || mesh.SelectedFaces.Count > 0;
                quickExtrude.Enabled = hasSelection;
                quickInset.Enabled = hasSelection;
                quickSubdivide.Enabled = hasSelection;
                quickDuplicate.Enabled = hasSelection;
                quickFlip.Enabled = hasSelection;
                quickDelete.Enabled = hasSelection;
                quickFrame.Enabled = hasSelection;
                quickLinked.Enabled = hasSelection;
                quickGrow.Enabled = hasSelection;
                quickShrink.Enabled = hasSelection;
                quickEdgeLoop.Enabled = hasSelection && mode == EditMode.Edge;
                quickFaceLoop.Enabled = hasSelection && mode == EditMode.Face;
                quickCloseLoop.Enabled = hasSelection;
                quickMakeFace.Visible = mode == EditMode.Vertex;
                quickMakeQuad.Visible = mode == EditMode.Vertex;
                quickMerge.Visible = mode == EditMode.Vertex;
            };
            toolbar.Controls.Add(vertex);
            toolbar.Controls.Add(edge);
            toolbar.Controls.Add(face);
            toolbar.Controls.Add(perspective);
            toolbar.Controls.Add(orthographicView);
            toolbar.Controls.Add(wireframe);
            toolbar.Controls.Add(unlit);
            toolbar.Controls.Add(culling);
            toolbar.Controls.Add(extrude);
            toolbar.Controls.Add(inset);
            toolbar.Controls.Add(makeFace);
            toolbar.Controls.Add(subdivide);
            toolbar.Controls.Add(delete);
            toolbar.Controls.Add(duplicate);
            toolbar.Controls.Add(flip);
            toolbar.Controls.Add(merge);
            toolbar.Controls.Add(mergeSelected);
            toolbar.Controls.Add(material);
            toolbar.Controls.Add(makeQuad);
            toolbar.Controls.Add(new Label { Text = "Move X", AutoSize = true, Padding = new Padding(4, 6, 0, 0) });
            toolbar.Controls.Add(moveX);
            toolbar.Controls.Add(new Label { Text = "Y", AutoSize = true, Padding = new Padding(4, 6, 0, 0) });
            toolbar.Controls.Add(moveY);
            toolbar.Controls.Add(new Label { Text = "Z", AutoSize = true, Padding = new Padding(4, 6, 0, 0) });
            toolbar.Controls.Add(moveZ);
            toolbar.Controls.Add(move);
            toolbar.Controls.Add(new Label { Text = "Scale X", AutoSize = true, Padding = new Padding(4, 6, 0, 0) });
            toolbar.Controls.Add(scaleX);
            toolbar.Controls.Add(new Label { Text = "Y", AutoSize = true, Padding = new Padding(4, 6, 0, 0) });
            toolbar.Controls.Add(scaleY);
            toolbar.Controls.Add(new Label { Text = "Z", AutoSize = true, Padding = new Padding(4, 6, 0, 0) });
            toolbar.Controls.Add(scaleZ);
            toolbar.Controls.Add(scale);
            toolbar.Controls.Add(new Label { Text = "Rotate X", AutoSize = true, Padding = new Padding(4, 6, 0, 0) });
            toolbar.Controls.Add(rotateX);
            toolbar.Controls.Add(new Label { Text = "Y", AutoSize = true, Padding = new Padding(4, 6, 0, 0) });
            toolbar.Controls.Add(rotateY);
            toolbar.Controls.Add(new Label { Text = "Z", AutoSize = true, Padding = new Padding(4, 6, 0, 0) });
            toolbar.Controls.Add(rotateZ);
            toolbar.Controls.Add(rotate);
            toolbar.Controls.Add(assignMaterial);
            toolbar.Controls.Add(selectAll);
            toolbar.Controls.Add(clearSelection);
            toolbar.Controls.Add(linked);
            toolbar.Controls.Add(grow);
            toolbar.Controls.Add(shrink);
            toolbar.Controls.Add(frameSelected);
            toolbar.Controls.Add(undo);
            toolbar.Controls.Add(redo);
            toolbar.Controls.Add(saveAsset);
            toolbar.Controls.Add(commit);
            toolbar.Controls.Add(close);
            status = new Label { AutoSize = true, Padding = new Padding(12, 6, 0, 0), Text = "" };
            toolbar.Controls.Add(status);

            toolbar.Controls.Remove(toolHeader);
            FlowLayoutPanel sideBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                Width = 158,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(5),
                BackColor = Color.FromArgb(225, 228, 232),
                BorderStyle = BorderStyle.FixedSingle
            };
            Label sideHeader = new Label
            {
                Text = "Model Tools",
                Width = 136,
                Height = 24,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                BackColor = Color.FromArgb(205, 210, 216)
            };
            sideBar.Controls.Add(sideHeader);
            Control[] modelingControls =
            {
                objectMode, editMode, vertexPaintMode, uvMode,
                gizmoMove, gizmoRotate, gizmoScale,
                vertex, edge, face,
                selectAll, clearSelection, linked, grow, shrink, frameSelected,
                edgeLoop, faceLoop, closeLoop,
                extrude, inset, makeFace, makeQuad, subdivide,
                delete, duplicate, flip, merge, mergeSelected, assignMaterial
            };
            foreach (Control control in modelingControls)
            {
                toolbar.Controls.Remove(control);
                control.Width = 136;
                sideBar.Controls.Add(control);
            }

            viewport = new GLControl(new GraphicsMode(32, 24, 0, 0)) { Dock = DockStyle.Fill, BackColor = Color.FromArgb(35, 38, 42) };
            viewport.ContextMenuStrip = quickMenu;
            viewport.Paint += Viewport_Paint;
            viewport.Resize += delegate
            {
                UpdateOpenGlViewport();
                FitMesh();
                viewport.Invalidate();
            };
            viewport.MouseDown += Viewport_MouseDown;
            viewport.MouseMove += Viewport_MouseMove;
            viewport.MouseUp += Viewport_MouseUp;
            viewport.MouseWheel += Viewport_MouseWheel;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                bool editingText = ActiveControl is TextBoxBase || ActiveControl is NumericUpDown;
                if (!editingText && !e.Control && !e.Alt)
                {
                    if (e.KeyCode == Keys.G && workspaceMode == WorkspaceMode.Object) { SetActiveGizmo(TransformGizmo.Move, "Move gizmo active"); e.Handled = true; return; }
                    if (e.KeyCode == Keys.R && workspaceMode == WorkspaceMode.Object) { SetActiveGizmo(TransformGizmo.Rotate, "Rotate gizmo active"); e.Handled = true; return; }
                    if (e.KeyCode == Keys.S && workspaceMode == WorkspaceMode.Object) { SetActiveGizmo(TransformGizmo.Scale, "Scale gizmo active"); e.Handled = true; return; }
                    if (e.KeyCode == Keys.E && workspaceMode == WorkspaceMode.Edit) { extrude.PerformClick(); e.Handled = true; return; }
                    if (e.KeyCode == Keys.I && workspaceMode == WorkspaceMode.Edit) { inset.PerformClick(); e.Handled = true; return; }
                    if (e.KeyCode == Keys.D && workspaceMode == WorkspaceMode.Edit) { duplicate.PerformClick(); e.Handled = true; return; }
                    if (e.KeyCode == Keys.F) { frameSelected.PerformClick(); e.Handled = true; return; }
                    if (e.KeyCode == Keys.W) { wireframe.PerformClick(); e.Handled = true; return; }
                    if (e.KeyCode == Keys.U) { unlit.PerformClick(); e.Handled = true; return; }
                }
                if (e.KeyCode == Keys.D1 || e.KeyCode == Keys.NumPad1)
                {
                    mode = EditMode.Vertex; viewport.Invalidate(); UpdateStatus(); e.Handled = true; return;
                }
                if (e.KeyCode == Keys.D2 || e.KeyCode == Keys.NumPad2)
                {
                    mode = EditMode.Edge; viewport.Invalidate(); UpdateStatus(); e.Handled = true; return;
                }
                if (e.KeyCode == Keys.D3 || e.KeyCode == Keys.NumPad3)
                {
                    mode = EditMode.Face; viewport.Invalidate(); UpdateStatus(); e.Handled = true; return;
                }
                if (dragging && (e.KeyCode == Keys.X || e.KeyCode == Keys.Y || e.KeyCode == Keys.Z))
                {
                    dragConstraint = e.KeyCode == Keys.X ? 'X' : e.KeyCode == Keys.Y ? 'Y' : 'Z';
                    UpdateStatus();
                    e.Handled = true;
                    return;
                }
                if (e.Control && e.KeyCode == Keys.A)
                {
                    SelectAllCurrentMode();
                    viewport.Invalidate();
                    UpdateStatus();
                    e.Handled = true;
                    return;
                }
                if (e.KeyCode == Keys.Home)
                {
                    FitSelection();
                    viewport.Invalidate();
                    UpdateStatus();
                    e.Handled = true;
                    return;
                }
                if (e.KeyCode == Keys.Delete)
                {
                    SaveUndoState();
                    mesh.DeleteSelection();
                    FitMesh();
                    viewport.Invalidate();
                    UpdateStatus();
                    e.Handled = true;
                }
            };
            Controls.Add(viewport);
            Controls.Add(toolbar);
            Controls.Add(sideBar);
            toolbar.BringToFront();
            sideBar.BringToFront();
            ArcharinaUiTheme.Apply(this);
            Shown += delegate
            {
                viewport.MakeCurrent();
                FitMesh();
                FitPerspectiveCamera();
                SetWorkspaceMode(WorkspaceMode.Object);
            };
        }

        private Button ToolButton(string text, EventHandler handler)
        {
            Button button = new Button { Text = text, AutoSize = true, Height = 24 };
            button.Click += handler;
            if (toolTips != null) toolTips.SetToolTip(button, text);
            return button;
        }

        private Button ToolButton(string text, string iconKey, EventHandler handler)
        {
            Button button = ToolButton(text, handler);
            button.Text = string.Empty;
            button.AutoSize = false;
            button.Size = new Size(36, 30);
            button.Image = ArcharinaModelerIcons.Load(iconKey);
            button.ImageAlign = ContentAlignment.MiddleCenter;
            button.AccessibleName = text;
            button.TextChanged += delegate
            {
                if (!String.IsNullOrEmpty(button.Text)) button.Text = String.Empty;
            };
            if (toolTips != null) toolTips.SetToolTip(button, text);
            return button;
        }

        private void SetWorkspaceMode(WorkspaceMode next)
        {
            workspaceMode = next;
            SetButtonVisualState("Object Mode", next == WorkspaceMode.Object);
            SetButtonVisualState("Edit Mode", next == WorkspaceMode.Edit);
            SetButtonVisualState("Vertex Paint", next == WorkspaceMode.VertexPaint);
            SetButtonVisualState("UV Editing", next == WorkspaceMode.UV);
            mesh.ClearSelection();
            mode = EditMode.Vertex;
            if (next == WorkspaceMode.Object)
            {
                for (int index = 0; index < mesh.Vertices.Count; index++) mesh.SelectedVertices.Add(index);
                UpdateStatus("Object mode: G move, R rotate, S scale");
            }
            else if (next == WorkspaceMode.Edit)
            {
                UpdateStatus("Edit mode: select vertices, edges, or faces");
            }
            else if (next == WorkspaceMode.VertexPaint)
            {
                UpdateStatus("Vertex Paint mode: vertex color workflow ready");
            }
            else
            {
                UpdateStatus("UV Editing mode: UV workflow ready");
            }
            viewport.Invalidate();
        }

        private void SetButtonVisualState(string accessibleName, bool active)
        {
            SetButtonVisualStateRecursive(this, accessibleName, active);
        }

        private static void SetButtonVisualStateRecursive(Control parent, string accessibleName, bool active)
        {
            foreach (Control child in parent.Controls)
            {
                Button button = child as Button;
                if (button != null && String.Equals(button.AccessibleName, accessibleName, StringComparison.OrdinalIgnoreCase))
                {
                    button.Tag = active;
                    button.Invalidate();
                }
                if (child.Controls.Count > 0) SetButtonVisualStateRecursive(child, accessibleName, active);
            }
        }

        private static NumericUpDown DeltaField()
        {
            return new NumericUpDown
            {
                Width = 62,
                DecimalPlaces = 2,
                Increment = 1,
                Minimum = -100000,
                Maximum = 100000,
                Value = 0
            };
        }

        private static NumericUpDown ScaleField()
        {
            return new NumericUpDown
            {
                Width = 62,
                DecimalPlaces = 2,
                Increment = 0.1M,
                Minimum = 0.01M,
                Maximum = 1000,
                Value = 1
            };
        }

        private static NumericUpDown RotateField()
        {
            return new NumericUpDown
            {
                Width = 62,
                DecimalPlaces = 1,
                Increment = 5,
                Minimum = -3600,
                Maximum = 3600,
                Value = 0
            };
        }

        private void SetActiveGizmo(TransformGizmo next, string message)
        {
            activeGizmo = next;
            SetButtonVisualState("Move gizmo", next == TransformGizmo.Move);
            SetButtonVisualState("Rotate gizmo", next == TransformGizmo.Rotate);
            SetButtonVisualState("Scale gizmo", next == TransformGizmo.Scale);
            UpdateStatus(message);
            if (viewport != null && !viewport.IsDisposed)
            {
                viewport.MakeCurrent();
                viewport.Invalidate();
                viewport.Update();
            }
        }

        private void UpdateStatus(string message = null)
        {
            if (status == null) return;
            SetButtonVisualState("Vertex", mode == EditMode.Vertex);
            SetButtonVisualState("Edge", mode == EditMode.Edge);
            SetButtonVisualState("Face", mode == EditMode.Face);
            status.Text = message ?? string.Format("{0} | {1} mode | {2} view | {3} verts | {4} tris | selected V:{5} E:{6} F:{7}",
                workspaceMode, mode, perspectiveView ? "Perspective" : viewPlane.ToString(), mesh.Vertices.Count, mesh.Triangles.Count,
                mesh.SelectedVertices.Count, mesh.SelectedEdges.Count, mesh.SelectedFaces.Count) +
                (dragging && dragConstraint != '\0' ? " | constrain " + dragConstraint : "");
        }

        private void SaveUndoState()
        {
            undoStack.Push(mesh.Clone());
            redoStack.Clear();
        }

        private void Undo()
        {
            if (undoStack.Count == 0) return;
            redoStack.Push(mesh.Clone());
            mesh.Restore(undoStack.Pop());
            FitMesh();
            viewport.Invalidate();
            UpdateStatus("Undo");
        }

        private void Redo()
        {
            if (redoStack.Count == 0) return;
            undoStack.Push(mesh.Clone());
            mesh.Restore(redoStack.Pop());
            FitMesh();
            viewport.Invalidate();
            UpdateStatus("Redo");
        }

        private void FitMesh()
        {
            if (mesh.Vertices.Count == 0) { minX = -100; maxX = 100; minY = -100; maxY = 100; return; }
            minX = maxX = PlaneA(mesh.Vertices[0]);
            minY = maxY = PlaneB(mesh.Vertices[0]);
            foreach (ObjFile.Vertex vertex in mesh.Vertices)
            {
                minX = Math.Min(minX, PlaneA(vertex)); maxX = Math.Max(maxX, PlaneA(vertex));
                minY = Math.Min(minY, PlaneB(vertex)); maxY = Math.Max(maxY, PlaneB(vertex));
            }
            double padX = Math.Max(25, (maxX - minX) * 0.15);
            double padY = Math.Max(25, (maxY - minY) * 0.15);
            minX -= padX; maxX += padX; minY -= padY; maxY += padY;
            NormalizeOrthoAspect();
        }

        private void FitSelection()
        {
            HashSet<int> indices = new HashSet<int>(mesh.SelectedVertices);
            foreach (int edge in mesh.SelectedEdges)
            {
                int start = MeshEditorData.EdgeStart(edge), end = MeshEditorData.EdgeEnd(edge);
                if (start >= 0 && start < mesh.Vertices.Count) indices.Add(start);
                if (end >= 0 && end < mesh.Vertices.Count) indices.Add(end);
            }
            foreach (int face in mesh.SelectedFaces)
            {
                if (face < 0 || face >= mesh.Triangles.Count) continue;
                indices.UnionWith(mesh.Triangles[face].VertIndex);
            }
            if (indices.Count == 0) { FitMesh(); return; }
            bool first = true;
            foreach (int index in indices)
            {
                if (index < 0 || index >= mesh.Vertices.Count) continue;
                ObjFile.Vertex vertex = mesh.Vertices[index];
                double a = PlaneA(vertex), b = PlaneB(vertex);
                if (first) { minX = maxX = a; minY = maxY = b; first = false; }
                else { minX = Math.Min(minX, a); maxX = Math.Max(maxX, a); minY = Math.Min(minY, b); maxY = Math.Max(maxY, b); }
            }
            double padX = Math.Max(25, (maxX - minX) * 0.5);
            double padY = Math.Max(25, (maxY - minY) * 0.5);
            minX -= padX; maxX += padX; minY -= padY; maxY += padY;
            NormalizeOrthoAspect();
        }

        private void NormalizeOrthoAspect()
        {
            if (viewport == null || viewport.ClientSize.Width <= 0 || viewport.ClientSize.Height <= 0) return;
            double width = Math.Max(0.001, maxX - minX);
            double height = Math.Max(0.001, maxY - minY);
            double target = viewport.ClientSize.Width / (double)viewport.ClientSize.Height;
            double current = width / height;
            if (current < target)
            {
                double expanded = height * target;
                double extra = (expanded - width) * 0.5;
                minX -= extra; maxX += extra;
            }
            else if (current > target)
            {
                double expanded = width / target;
                double extra = (expanded - height) * 0.5;
                minY -= extra; maxY += extra;
            }
        }

        private double PlaneA(ObjFile.Vertex vertex)
        {
            return viewPlane == ViewPlane.YZ ? vertex.Y : vertex.X;
        }

        private double PlaneB(ObjFile.Vertex vertex)
        {
            return viewPlane == ViewPlane.XY ? vertex.Y : vertex.Z;
        }

        private void Viewport_Paint(object sender, PaintEventArgs e)
        {
            if (!viewport.IsHandleCreated) return;
            viewport.MakeCurrent();
            UpdateOpenGlViewport();
            if (perspectiveView)
            {
                PaintPerspective();
                return;
            }
            GL.ClearColor(Color.FromArgb(35, 38, 42));
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.Disable(EnableCap.DepthTest);
            if (surfaceCulling) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();
            GL.Ortho(minX, maxX, minY, maxY, -10000, 10000);
            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadIdentity();
            DrawOrthographicGrid();

            if (!wireframeView)
            {
                GL.Begin(BeginMode.Triangles);
                foreach (ObjFile.Triangle triangle in mesh.Triangles)
                {
                    bool selected = mesh.SelectedFaces.Contains(mesh.Triangles.IndexOf(triangle));
                    GL.Color3(mode == EditMode.Face && selected ? Color.FromArgb(220, 180, 60) : MaterialPreviewColor(triangle.MaterialName));
                    foreach (int index in triangle.VertIndex)
                    {
                        if (index >= 0 && index < mesh.Vertices.Count)
                            GL.Vertex2(PlaneA(mesh.Vertices[index]), PlaneB(mesh.Vertices[index]));
                    }
                }
                GL.End();
            }

            GL.LineWidth(1.5f);
            GL.Begin(BeginMode.Lines);
            for (int triangleIndex = 0; triangleIndex < mesh.Triangles.Count; triangleIndex++)
            {
                ObjFile.Triangle triangle = mesh.Triangles[triangleIndex];
                for (int corner = 0; corner < 3; corner++)
                {
                    int aIndex = triangle.VertIndex[corner];
                    int bIndex = triangle.VertIndex[(corner + 1) % 3];
                    ObjFile.Vertex a = mesh.Vertices[aIndex];
                    ObjFile.Vertex b = mesh.Vertices[bIndex];
                    GL.Color3(mode == EditMode.Edge && mesh.SelectedEdges.Contains(MeshEditorData.EdgeKey(aIndex, bIndex))
                        ? Color.FromArgb(245, 190, 60) : Color.FromArgb(210, 215, 220));
                    GL.Vertex2(PlaneA(a), PlaneB(a)); GL.Vertex2(PlaneA(b), PlaneB(b));
                }
            }
            GL.End();

            GL.PointSize(7.0f);
            GL.Begin(BeginMode.Points);
            for (int vertexIndex = 0; vertexIndex < mesh.Vertices.Count; vertexIndex++)
            {
                ObjFile.Vertex vertex = mesh.Vertices[vertexIndex];
                GL.Color3(mode == EditMode.Vertex && mesh.SelectedVertices.Contains(vertexIndex)
                    ? Color.FromArgb(255, 210, 70) : Color.FromArgb(235, 235, 235));
                GL.Vertex2(PlaneA(vertex), PlaneB(vertex));
            }
            GL.End();
            DrawTransformGizmo();
            viewport.SwapBuffers();
        }

        private void UpdateOpenGlViewport()
        {
            if (viewport == null || !viewport.IsHandleCreated || viewport.ClientSize.Width <= 0 || viewport.ClientSize.Height <= 0)
                return;
            viewport.MakeCurrent();
            GL.Viewport(0, 0, viewport.ClientSize.Width, viewport.ClientSize.Height);
        }

        private void PaintPerspective()
        {
            GL.ClearColor(Color.FromArgb(35, 38, 42));
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.Enable(EnableCap.DepthTest);
            if (surfaceCulling) GL.Enable(EnableCap.CullFace); else GL.Disable(EnableCap.CullFace);
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();
            Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(55.0f),
                Math.Max(0.1f, (float)viewport.ClientSize.Width / Math.Max(1, viewport.ClientSize.Height)), 0.1f, 100000.0f);
            GL.MultMatrix(ref projection);
            GL.MatrixMode(MatrixMode.Modelview);
            GL.LoadIdentity();
            Vector3 target = MeshCenter();
            double yaw = orbitYaw * Math.PI / 180.0;
            double pitch = orbitPitch * Math.PI / 180.0;
            Vector3 camera = new Vector3(
                target.X + (float)(Math.Cos(pitch) * Math.Cos(yaw) * orbitDistance),
                target.Y + (float)(Math.Sin(pitch) * orbitDistance),
                target.Z + (float)(Math.Cos(pitch) * Math.Sin(yaw) * orbitDistance));
            Matrix4 view = Matrix4.LookAt(camera, target, Vector3.UnitY);
            GL.MultMatrix(ref view);
            DrawPerspectiveGrid(target);
            if (!wireframeView)
            {
                GL.Begin(BeginMode.Triangles);
                for (int triangleIndex = 0; triangleIndex < mesh.Triangles.Count; triangleIndex++)
                {
                    ObjFile.Triangle triangle = mesh.Triangles[triangleIndex];
                    bool selected = mesh.SelectedFaces.Contains(triangleIndex);
                    GL.Color3(mode == EditMode.Face && selected ? Color.FromArgb(220, 180, 60) : MaterialPreviewColor(triangle.MaterialName));
                    foreach (int index in triangle.VertIndex)
                        if (index >= 0 && index < mesh.Vertices.Count) GL.Vertex3(mesh.Vertices[index].X, mesh.Vertices[index].Y, mesh.Vertices[index].Z);
                }
                GL.End();
            }
            GL.LineWidth(1.5f);
            GL.Begin(BeginMode.Lines);
            for (int triangleIndex = 0; triangleIndex < mesh.Triangles.Count; triangleIndex++)
            {
                ObjFile.Triangle triangle = mesh.Triangles[triangleIndex];
                for (int corner = 0; corner < 3; corner++)
                {
                    int aIndex = triangle.VertIndex[corner];
                    int bIndex = triangle.VertIndex[(corner + 1) % 3];
                    ObjFile.Vertex a = mesh.Vertices[aIndex];
                    ObjFile.Vertex b = mesh.Vertices[bIndex];
                    GL.Color3(mode == EditMode.Edge && mesh.SelectedEdges.Contains(MeshEditorData.EdgeKey(aIndex, bIndex))
                        ? Color.Gold : Color.FromArgb(210, 215, 220));
                    GL.Vertex3(a.X, a.Y, a.Z); GL.Vertex3(b.X, b.Y, b.Z);
                }
            }
            GL.End();
            GL.PointSize(7.0f);
            GL.Begin(BeginMode.Points);
            for (int vertexIndex = 0; vertexIndex < mesh.Vertices.Count; vertexIndex++)
            {
                ObjFile.Vertex vertex = mesh.Vertices[vertexIndex];
                GL.Color3(mode == EditMode.Vertex && mesh.SelectedVertices.Contains(vertexIndex) ? Color.Gold : Color.White);
                GL.Vertex3(vertex.X, vertex.Y, vertex.Z);
            }
            GL.End();
            DrawPerspectiveGizmo(target);
            viewport.SwapBuffers();
        }

        private Vector3 MeshCenter()
        {
            if (mesh.Vertices.Count == 0) return Vector3.Zero;
            Vector3 center = Vector3.Zero;
            foreach (ObjFile.Vertex vertex in mesh.Vertices) center += new Vector3((float)vertex.X, (float)vertex.Y, (float)vertex.Z);
            center /= mesh.Vertices.Count;
            return center;
        }

        private void PerspectiveCamera(out Vector3 camera, out Vector3 target, out Vector3 right, out Vector3 up, out Vector3 forward)
        {
            target = MeshCenter();
            double yaw = orbitYaw * Math.PI / 180.0;
            double pitch = orbitPitch * Math.PI / 180.0;
            camera = new Vector3(
                target.X + (float)(Math.Cos(pitch) * Math.Cos(yaw) * orbitDistance),
                target.Y + (float)(Math.Sin(pitch) * orbitDistance),
                target.Z + (float)(Math.Cos(pitch) * Math.Sin(yaw) * orbitDistance));
            forward = Vector3.Normalize(target - camera);
            right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
            if (right.LengthSquared < 0.0001f) right = Vector3.UnitX;
            up = Vector3.Normalize(Vector3.Cross(right, forward));
        }

        private void DrawPerspectiveGizmo(Vector3 target)
        {
            HashSet<int> indices = SelectedVertexIndices();
            if (indices.Count == 0) return;
            Vector3 center = Vector3.Zero;
            int count = 0;
            foreach (int index in indices)
            {
                if (index < 0 || index >= mesh.Vertices.Count) continue;
                ObjFile.Vertex vertex = mesh.Vertices[index];
                center += new Vector3((float)vertex.X, (float)vertex.Y, (float)vertex.Z);
                count++;
            }
            if (count == 0) return;
            center /= count;
            float length = (float)Math.Max(20, orbitDistance * 0.16);
            GL.LineWidth(4.0f);
            GL.Begin(BeginMode.Lines);
            GL.Color3(Color.FromArgb(25, 25, 25));
            GL.Vertex3(center - Vector3.UnitX * length); GL.Vertex3(center + Vector3.UnitX * length);
            GL.Vertex3(center - Vector3.UnitY * length); GL.Vertex3(center + Vector3.UnitY * length);
            GL.Vertex3(center - Vector3.UnitZ * length); GL.Vertex3(center + Vector3.UnitZ * length);
            GL.End();
            GL.LineWidth(2.5f);
            GL.Begin(BeginMode.Lines);
            GL.Color3(Color.FromArgb(225, 70, 70)); GL.Vertex3(center); GL.Vertex3(center + Vector3.UnitX * length);
            GL.Color3(Color.FromArgb(70, 210, 100)); GL.Vertex3(center); GL.Vertex3(center + Vector3.UnitY * length);
            GL.Color3(Color.FromArgb(70, 125, 235)); GL.Vertex3(center); GL.Vertex3(center + Vector3.UnitZ * length);
            GL.End();
            if (activeGizmo == TransformGizmo.Move)
            {
                DrawPerspectiveArrowhead(center + Vector3.UnitX * length, Vector3.UnitX, Color.FromArgb(225, 70, 70), length * 0.16f);
                DrawPerspectiveArrowhead(center + Vector3.UnitY * length, Vector3.UnitY, Color.FromArgb(70, 210, 100), length * 0.16f);
                DrawPerspectiveArrowhead(center + Vector3.UnitZ * length, Vector3.UnitZ, Color.FromArgb(70, 125, 235), length * 0.16f);
            }
        }

        private static void DrawPerspectiveArrowhead(Vector3 tip, Vector3 direction, Color color, float size)
        {
            Vector3 axis = Vector3.Normalize(direction);
            Vector3 side = Vector3.Cross(axis, Vector3.UnitY);
            if (side.LengthSquared < 0.001f) side = Vector3.Cross(axis, Vector3.UnitX);
            side = Vector3.Normalize(side) * (size * 0.58f);
            Vector3 basePoint = tip - axis * size;
            GL.LineWidth(2.5f);
            GL.Color3(color);
            GL.Begin(BeginMode.Lines);
            GL.Vertex3(tip); GL.Vertex3(basePoint + side);
            GL.Vertex3(tip); GL.Vertex3(basePoint - side);
            GL.End();
        }

        private void FitPerspectiveCamera()
        {
            if (mesh.Vertices.Count == 0) { orbitDistance = 500; return; }
            double min = 0, max = 0;
            bool first = true;
            foreach (ObjFile.Vertex vertex in mesh.Vertices)
            {
                Vector3 point = new Vector3((float)vertex.X, (float)vertex.Y, (float)vertex.Z);
                if (first) { min = max = point.Length; first = false; }
                else { min = Math.Min(min, point.Length); max = Math.Max(max, point.Length); }
            }
            orbitDistance = Math.Max(100, max * 2.5);
        }

        private void DrawOrthographicGrid()
        {
            double step = GridStep(Math.Max(maxX - minX, maxY - minY));
            GL.LineWidth(1.0f);
            GL.Begin(BeginMode.Lines);
            GL.Color3(Color.FromArgb(60, 66, 74));
            for (double value = Math.Floor(minX / step) * step; value <= maxX; value += step)
            {
                GL.Vertex2(value, minY); GL.Vertex2(value, maxY);
            }
            for (double value = Math.Floor(minY / step) * step; value <= maxY; value += step)
            {
                GL.Vertex2(minX, value); GL.Vertex2(maxX, value);
            }
            GL.Color3(Color.FromArgb(190, 75, 75));
            GL.Vertex2(0, minY); GL.Vertex2(0, maxY);
            GL.Color3(Color.FromArgb(75, 190, 100));
            GL.Vertex2(minX, 0); GL.Vertex2(maxX, 0);
            GL.End();
        }

        private void DrawPerspectiveGrid(Vector3 target)
        {
            double half = Math.Max(100, orbitDistance * 0.7);
            double step = GridStep(half * 2.0);
            double y = target.Y - half * 0.05;
            GL.LineWidth(1.0f);
            GL.Begin(BeginMode.Lines);
            GL.Color3(Color.FromArgb(60, 66, 74));
            for (double value = Math.Floor((target.X - half) / step) * step; value <= target.X + half; value += step)
            {
                GL.Vertex3(value, y, target.Z - half); GL.Vertex3(value, y, target.Z + half);
            }
            for (double value = Math.Floor((target.Z - half) / step) * step; value <= target.Z + half; value += step)
            {
                GL.Vertex3(target.X - half, y, value); GL.Vertex3(target.X + half, y, value);
            }
            GL.Color3(Color.FromArgb(190, 75, 75));
            GL.Vertex3(target.X - half, y, target.Z); GL.Vertex3(target.X + half, y, target.Z);
            GL.Color3(Color.FromArgb(75, 190, 100));
            GL.Vertex3(target.X, y, target.Z - half); GL.Vertex3(target.X, y, target.Z + half);
            GL.End();
        }

        private static double GridStep(double span)
        {
            if (span <= 0) return 10;
            double raw = span / 12.0;
            double power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double normalized = raw / power;
            double step = normalized < 2 ? 1 : normalized < 5 ? 2 : 5;
            return step * power;
        }

        private void DrawTransformGizmo()
        {
            HashSet<int> indices = SelectedVertexIndices();
            if (indices.Count == 0) return;
            double centerA = 0, centerB = 0; int count = 0;
            foreach (int index in indices)
            {
                if (index < 0 || index >= mesh.Vertices.Count) continue;
                centerA += PlaneA(mesh.Vertices[index]);
                centerB += PlaneB(mesh.Vertices[index]);
                count++;
            }
            if (count == 0) return;
            centerA /= count; centerB /= count;
            double length = Math.Max(maxX - minX, maxY - minY) * 0.12;
            GL.LineWidth(4.0f);
            GL.Begin(BeginMode.Lines);
            GL.Color3(Color.FromArgb(25, 25, 25));
            GL.Vertex2(centerA - length, centerB); GL.Vertex2(centerA + length, centerB);
            GL.Vertex2(centerA, centerB - length); GL.Vertex2(centerA, centerB + length);
            GL.End();
            GL.LineWidth(2.5f);
            GL.Begin(BeginMode.Lines);
            GL.Color3(viewPlane == ViewPlane.YZ ? Color.FromArgb(70, 210, 100) : Color.FromArgb(225, 70, 70));
            GL.Vertex2(centerA, centerB); GL.Vertex2(centerA + length, centerB);
            GL.Color3(viewPlane == ViewPlane.XY ? Color.FromArgb(70, 210, 100) : Color.FromArgb(70, 125, 235));
            GL.Vertex2(centerA, centerB); GL.Vertex2(centerA, centerB + length);
            GL.End();
            if (activeGizmo == TransformGizmo.Move)
            {
                Color xColor = viewPlane == ViewPlane.YZ ? Color.FromArgb(70, 210, 100) : Color.FromArgb(225, 70, 70);
                Color yColor = viewPlane == ViewPlane.XY ? Color.FromArgb(70, 210, 100) : Color.FromArgb(70, 125, 235);
                DrawOrthographicArrowhead(centerA + length, centerB, centerA, centerB, xColor, length * 0.18);
                DrawOrthographicArrowhead(centerA, centerB + length, centerA, centerB, yColor, length * 0.18);
            }
            GL.PointSize(8.0f);
            GL.Begin(BeginMode.Points);
            GL.Color3(Color.FromArgb(255, 210, 70));
            GL.Vertex2(centerA, centerB);
            GL.End();
        }

        private static void DrawOrthographicArrowhead(double tipA, double tipB, double baseA, double baseB, Color color, double size)
        {
            double dx = tipA - baseA;
            double dy = tipB - baseB;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.0001) return;
            double ux = dx / length;
            double uy = dy / length;
            double px = -uy;
            double py = ux;
            double bx = tipA - ux * size;
            double by = tipB - uy * size;
            GL.LineWidth(2.5f);
            GL.Color3(color);
            GL.Begin(BeginMode.Lines);
            GL.Vertex2(tipA, tipB); GL.Vertex2(bx + px * size * 0.58, by + py * size * 0.58);
            GL.Vertex2(tipA, tipB); GL.Vertex2(bx - px * size * 0.58, by - py * size * 0.58);
            GL.End();
        }

        private HashSet<int> SelectedVertexIndices()
        {
            HashSet<int> indices = new HashSet<int>(mesh.SelectedVertices);
            foreach (int edge in mesh.SelectedEdges)
            {
                int start = MeshEditorData.EdgeStart(edge), end = MeshEditorData.EdgeEnd(edge);
                if (start >= 0 && start < mesh.Vertices.Count) indices.Add(start);
                if (end >= 0 && end < mesh.Vertices.Count) indices.Add(end);
            }
            foreach (int face in mesh.SelectedFaces)
            {
                if (face >= 0 && face < mesh.Triangles.Count)
                    indices.UnionWith(mesh.Triangles[face].VertIndex);
            }
            return indices;
        }

        private Color MaterialPreviewColor(string materialName)
        {
            if (unlitView) return Color.FromArgb(170, 175, 185);
            if (model != null && model.Materials != null && !string.IsNullOrEmpty(materialName))
            {
                foreach (ObjFile.Material material in model.Materials)
                {
                    if (material == null || material.Name != materialName || material.Kd == null || material.Kd.Length < 3) continue;
                    return Color.FromArgb(ToColorByte(material.Kd[0]), ToColorByte(material.Kd[1]), ToColorByte(material.Kd[2]));
                }
            }
            return Color.FromArgb(105, 125, 145);
        }

        private static int ToColorByte(float value)
        {
            return Math.Max(0, Math.Min(255, (int)Math.Round(value * 255.0f)));
        }

        private PointF WorldToScreen(ObjFile.Vertex vertex)
        {
            if (perspectiveView)
            {
                Vector3 camera, target, right, up, forward;
                PerspectiveCamera(out camera, out target, out right, out up, out forward);
                Vector3 point = new Vector3((float)vertex.X, (float)vertex.Y, (float)vertex.Z);
                Vector3 relative = point - camera;
                double depth = Vector3.Dot(relative, forward);
                if (depth <= 0.001) return new PointF(float.NaN, float.NaN);
                double focal = viewport.ClientSize.Height / (2.0 * Math.Tan(MathHelper.DegreesToRadians(55.0) * 0.5));
                double horizontal = Vector3.Dot(relative, right) * focal / depth;
                double vertical = Vector3.Dot(relative, up) * focal / depth;
                return new PointF((float)(viewport.ClientSize.Width * 0.5 + horizontal),
                    (float)(viewport.ClientSize.Height * 0.5 - vertical));
            }
            float x = (float)((PlaneA(vertex) - minX) / Math.Max(0.001, maxX - minX) * viewport.ClientSize.Width);
            float y = (float)(viewport.ClientSize.Height - (PlaneB(vertex) - minY) / Math.Max(0.001, maxY - minY) * viewport.ClientSize.Height);
            return new PointF(x, y);
        }

        private Vector3d ScreenDelta(Point current, Point previous)
        {
            if (perspectiveView)
            {
                Vector3 camera, target, right, up, forward;
                PerspectiveCamera(out camera, out target, out right, out up, out forward);
                double scale = orbitDistance / Math.Max(1, viewport.ClientSize.Height) * 1.8;
                Vector3 delta = right * (float)((current.X - previous.X) * scale) +
                    up * (float)(-(current.Y - previous.Y) * scale);
                if (dragConstraint == 'X') { delta.Y = 0; delta.Z = 0; }
                else if (dragConstraint == 'Y') { delta.X = 0; delta.Z = 0; }
                else if (dragConstraint == 'Z') { delta.X = 0; delta.Y = 0; }
                return new Vector3d(delta.X, delta.Y, delta.Z);
            }
            double a = (current.X - previous.X) * (maxX - minX) / Math.Max(1, viewport.ClientSize.Width);
            double b = -(current.Y - previous.Y) * (maxY - minY) / Math.Max(1, viewport.ClientSize.Height);
            if (dragConstraint == 'X')
            {
                if (viewPlane == ViewPlane.XY || viewPlane == ViewPlane.XZ) b = 0;
                else { a = 0; b = 0; }
            }
            else if (dragConstraint == 'Y')
            {
                if (viewPlane == ViewPlane.XY) a = 0;
                else if (viewPlane == ViewPlane.XZ) { a = 0; b = 0; }
            }
            else if (dragConstraint == 'Z')
            {
                if (viewPlane == ViewPlane.XY) { a = 0; b = 0; }
                else if (viewPlane == ViewPlane.XZ) a = 0;
                else a = 0;
            }
            if (viewPlane == ViewPlane.YZ) return new Vector3d(0, a, b);
            if (viewPlane == ViewPlane.XZ) return new Vector3d(a, 0, b);
            return new Vector3d(a, b, 0);
        }

        private void Viewport_MouseDown(object sender, MouseEventArgs e)
        {
            if (perspectiveView && e.Button == MouseButtons.Right && (ModifierKeys & Keys.Alt) == Keys.Alt)
            {
                orbiting = true;
                lastMouse = e.Location;
                return;
            }
            if (perspectiveView && e.Button == MouseButtons.Middle)
            {
                orbiting = true;
                lastMouse = e.Location;
                return;
            }
            if (e.Button == MouseButtons.Middle)
            {
                panning = true;
                lastMouse = e.Location;
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (workspaceMode != WorkspaceMode.Edit)
            {
                mesh.ClearSelection();
                for (int index = 0; index < mesh.Vertices.Count; index++) mesh.SelectedVertices.Add(index);
                dragging = true;
                dragUndoSaved = false;
                lastMouse = e.Location;
                viewport.Invalidate();
                UpdateStatus();
                return;
            }
            bool additive = (ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None;
            if (!additive) mesh.ClearSelection();
            int selected = -1;
            if (mode == EditMode.Vertex)
            {
                selected = NearestVertex(e.Location);
                if (selected < 0)
                {
                    int face = FaceAt(e.Location);
                    if (face >= 0) selected = NearestVertexOnFace(face, e.Location);
                }
                if (selected >= 0) ToggleSelection(mesh.SelectedVertices, selected, additive);
            }
            else if (mode == EditMode.Edge)
            {
                selected = NearestEdge(e.Location);
                if (selected < 0)
                {
                    int face = FaceAt(e.Location);
                    if (face >= 0) selected = NearestEdgeOnFace(face, e.Location);
                }
                if (selected >= 0) ToggleSelection(mesh.SelectedEdges, selected, additive);
            }
            else
            {
                selected = FaceAt(e.Location);
                if (selected >= 0) ToggleSelection(mesh.SelectedFaces, selected, additive);
            }
            dragging = selected >= 0 && (!additive || HasSelection(selected));
            dragUndoSaved = false;
            lastMouse = e.Location;
            viewport.Invalidate();
            UpdateStatus();
        }

        private void Viewport_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle) orbiting = false;
            if (e.Button == MouseButtons.Middle) panning = false;
            if (e.Button == MouseButtons.Left)
            {
                dragging = false;
                dragConstraint = '\0';
                UpdateStatus();
            }
        }

        private bool HasSelection(int selected)
        {
            if (mode == EditMode.Vertex) return mesh.SelectedVertices.Contains(selected);
            if (mode == EditMode.Edge) return mesh.SelectedEdges.Contains(selected);
            return mesh.SelectedFaces.Contains(selected);
        }

        private static void ToggleSelection(HashSet<int> selection, int index, bool additive)
        {
            if (additive && selection.Contains(index)) selection.Remove(index);
            else selection.Add(index);
        }

        private void SelectAllCurrentMode()
        {
            mesh.ClearSelection();
            if (workspaceMode != WorkspaceMode.Edit)
            {
                for (int i = 0; i < mesh.Vertices.Count; i++) mesh.SelectedVertices.Add(i);
                return;
            }
            if (mode == EditMode.Vertex)
                for (int i = 0; i < mesh.Vertices.Count; i++) mesh.SelectedVertices.Add(i);
            else if (mode == EditMode.Edge)
                foreach (ObjFile.Triangle triangle in mesh.Triangles)
                    for (int corner = 0; corner < 3; corner++)
                        mesh.SelectedEdges.Add(MeshEditorData.EdgeKey(triangle.VertIndex[corner], triangle.VertIndex[(corner + 1) % 3]));
            else
                for (int i = 0; i < mesh.Triangles.Count; i++) mesh.SelectedFaces.Add(i);
        }

        private bool PromoteCurrentSelectionToFaces()
        {
            if (workspaceMode != WorkspaceMode.Edit) return false;
            if (mode == EditMode.Face) return mesh.SelectedFaces.Count > 0;
            HashSet<int> faces = new HashSet<int>();
            if (mode == EditMode.Edge)
            {
                foreach (int edge in mesh.SelectedEdges)
                {
                    int start = MeshEditorData.EdgeStart(edge), end = MeshEditorData.EdgeEnd(edge);
                    for (int face = 0; face < mesh.Triangles.Count; face++)
                    {
                        ObjFile.Triangle triangle = mesh.Triangles[face];
                        for (int corner = 0; corner < 3; corner++)
                        {
                            if (MeshEditorData.EdgeKey(triangle.VertIndex[corner], triangle.VertIndex[(corner + 1) % 3]) ==
                                MeshEditorData.EdgeKey(start, end))
                            {
                                faces.Add(face);
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                foreach (int vertex in mesh.SelectedVertices)
                {
                    for (int face = 0; face < mesh.Triangles.Count; face++)
                    {
                        ObjFile.Triangle triangle = mesh.Triangles[face];
                        if (triangle != null && triangle.VertIndex != null && Array.IndexOf(triangle.VertIndex, vertex) >= 0)
                            faces.Add(face);
                    }
                }
            }
            if (faces.Count == 0) return false;
            mesh.SelectedFaces.Clear();
            mesh.SelectedFaces.UnionWith(faces);
            return true;
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (perspectiveView && orbiting && (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle))
            {
                orbitYaw += (e.Location.X - lastMouse.X) * 0.5;
                orbitPitch = Math.Max(-85, Math.Min(85, orbitPitch + (e.Location.Y - lastMouse.Y) * 0.5));
                lastMouse = e.Location;
                viewport.Invalidate();
                return;
            }
            if (panning && e.Button == MouseButtons.Middle)
            {
                double deltaA = (e.Location.X - lastMouse.X) * (maxX - minX) / Math.Max(1, viewport.ClientSize.Width);
                double deltaB = -(e.Location.Y - lastMouse.Y) * (maxY - minY) / Math.Max(1, viewport.ClientSize.Height);
                minX -= deltaA; maxX -= deltaA;
                minY -= deltaB; maxY -= deltaB;
                lastMouse = e.Location;
                viewport.Invalidate();
                return;
            }
            if (!dragging || e.Button != MouseButtons.Left) return;
            if (!dragUndoSaved && (mesh.SelectedVertices.Count > 0 || mesh.SelectedEdges.Count > 0 || mesh.SelectedFaces.Count > 0))
            {
                SaveUndoState();
                dragUndoSaved = true;
            }
            Vector3d delta = ScreenDelta(e.Location, lastMouse);
            if (workspaceMode == WorkspaceMode.Object)
            {
                if (activeGizmo == TransformGizmo.Move)
                    mesh.TranslateSelection(delta);
                else if (activeGizmo == TransformGizmo.Rotate)
                    mesh.RotateSelection(new Vector3d(-delta.Y * 0.5, delta.X * 0.5, 0));
                else
                {
                    double factor = Math.Max(0.05, 1.0 - delta.Y * 0.01);
                    mesh.ScaleSelection(new Vector3d(factor, factor, factor));
                }
            }
            else
            {
                mesh.TranslateSelection(delta);
            }
            lastMouse = e.Location;
            viewport.Invalidate();
        }

        private void Viewport_MouseWheel(object sender, MouseEventArgs e)
        {
            if (perspectiveView)
            {
                orbitDistance = Math.Max(1.0, orbitDistance * (e.Delta > 0 ? 0.85 : 1.18));
                viewport.Invalidate();
                return;
            }
            if (viewport.ClientSize.Width <= 0 || viewport.ClientSize.Height <= 0) return;
            double focusA = minX + e.Location.X * (maxX - minX) / viewport.ClientSize.Width;
            double focusB = maxY - e.Location.Y * (maxY - minY) / viewport.ClientSize.Height;
            double factor = e.Delta > 0 ? 0.8 : 1.25;
            double width = Math.Max(0.001, (maxX - minX) * factor);
            double height = Math.Max(0.001, (maxY - minY) * factor);
            double horizontal = e.Location.X / (double)viewport.ClientSize.Width;
            double vertical = 1.0 - e.Location.Y / (double)viewport.ClientSize.Height;
            minX = focusA - width * horizontal;
            maxX = minX + width;
            minY = focusB - height * vertical;
            maxY = minY + height;
            viewport.Invalidate();
        }

        private int NearestVertex(Point point)
        {
            int result = -1; double best = 28 * 28;
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                PointF projected = WorldToScreen(mesh.Vertices[i]);
                double dx = projected.X - point.X, dy = projected.Y - point.Y, distance = dx * dx + dy * dy;
                if (distance < best) { best = distance; result = i; }
            }
            return result;
        }

        private int NearestVertexOnFace(int faceIndex, Point point)
        {
            if (faceIndex < 0 || faceIndex >= mesh.Triangles.Count) return -1;
            int result = -1; double best = double.MaxValue;
            foreach (int index in mesh.Triangles[faceIndex].VertIndex)
            {
                PointF projected = WorldToScreen(mesh.Vertices[index]);
                double dx = projected.X - point.X, dy = projected.Y - point.Y, distance = dx * dx + dy * dy;
                if (distance < best) { best = distance; result = index; }
            }
            return result;
        }

        private int NearestEdge(Point point)
        {
            int result = -1; double best = 18 * 18;
            for (int i = 0; i < mesh.Triangles.Count; i++)
            {
                ObjFile.Triangle triangle = mesh.Triangles[i];
                for (int corner = 0; corner < 3; corner++)
                {
                    PointF a = WorldToScreen(mesh.Vertices[triangle.VertIndex[corner]]);
                    PointF b = WorldToScreen(mesh.Vertices[triangle.VertIndex[(corner + 1) % 3]]);
                    double distance = SegmentDistance(point, a, b);
                    if (distance < best)
                    {
                        best = distance;
                        result = MeshEditorData.EdgeKey(triangle.VertIndex[corner], triangle.VertIndex[(corner + 1) % 3]);
                    }
                }
            }
            return result;
        }

        private int NearestEdgeOnFace(int faceIndex, Point point)
        {
            if (faceIndex < 0 || faceIndex >= mesh.Triangles.Count) return -1;
            ObjFile.Triangle triangle = mesh.Triangles[faceIndex];
            int result = -1; double best = double.MaxValue;
            for (int corner = 0; corner < 3; corner++)
            {
                int aIndex = triangle.VertIndex[corner];
                int bIndex = triangle.VertIndex[(corner + 1) % 3];
                double distance = SegmentDistance(point, WorldToScreen(mesh.Vertices[aIndex]), WorldToScreen(mesh.Vertices[bIndex]));
                if (distance < best)
                {
                    best = distance;
                    result = MeshEditorData.EdgeKey(aIndex, bIndex);
                }
            }
            return result;
        }

        private int FaceAt(Point point)
        {
            if (perspectiveView) return PerspectiveFaceAt(point);
            for (int i = mesh.Triangles.Count - 1; i >= 0; i--)
            {
                ObjFile.Triangle t = mesh.Triangles[i];
                PointF a = WorldToScreen(mesh.Vertices[t.VertIndex[0]]);
                PointF b = WorldToScreen(mesh.Vertices[t.VertIndex[1]]);
                PointF c = WorldToScreen(mesh.Vertices[t.VertIndex[2]]);
                double d1 = Cross(point, a, b), d2 = Cross(point, b, c), d3 = Cross(point, c, a);
                if ((d1 >= 0 && d2 >= 0 && d3 >= 0) || (d1 <= 0 && d2 <= 0 && d3 <= 0)) return i;
            }
            return -1;
        }

        private int PerspectiveFaceAt(Point point)
        {
            Vector3 camera, target, right, up, forward;
            PerspectiveCamera(out camera, out target, out right, out up, out forward);
            double focal = viewport.ClientSize.Height /
                (2.0 * Math.Tan(MathHelper.DegreesToRadians(55.0) * 0.5));
            float x = (float)((point.X - viewport.ClientSize.Width * 0.5) / focal);
            float y = (float)((viewport.ClientSize.Height * 0.5 - point.Y) / focal);
            Vector3 direction = Vector3.Normalize(forward + right * x + up * y);

            int result = -1;
            float nearest = float.MaxValue;
            for (int face = 0; face < mesh.Triangles.Count; face++)
            {
                ObjFile.Triangle triangle = mesh.Triangles[face];
                if (triangle == null || triangle.VertIndex == null || triangle.VertIndex.Length < 3) continue;
                Vector3 a = VertexPosition(triangle.VertIndex[0]);
                Vector3 b = VertexPosition(triangle.VertIndex[1]);
                Vector3 c = VertexPosition(triangle.VertIndex[2]);
                float distance;
                if (RayTriangleDistance(camera, direction, a, b, c, out distance) && distance < nearest)
                {
                    nearest = distance;
                    result = face;
                }
            }
            return result;
        }

        private Vector3 VertexPosition(int index)
        {
            ObjFile.Vertex vertex = mesh.Vertices[index];
            return new Vector3((float)vertex.X, (float)vertex.Y, (float)vertex.Z);
        }

        private static bool RayTriangleDistance(Vector3 origin, Vector3 direction,
            Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            const float epsilon = 0.00001f;
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 p = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            distance = 0;
            if (Math.Abs(determinant) < epsilon) return false;
            float inverse = 1.0f / determinant;
            Vector3 offset = origin - a;
            float u = Vector3.Dot(offset, p) * inverse;
            if (u < 0 || u > 1) return false;
            Vector3 q = Vector3.Cross(offset, edge1);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < 0 || u + v > 1) return false;
            float t = Vector3.Dot(edge2, q) * inverse;
            if (t <= epsilon) return false;
            distance = t;
            return true;
        }

        private static double Cross(PointF p, PointF a, PointF b)
        {
            return (p.X - a.X) * (b.Y - a.Y) - (p.Y - a.Y) * (b.X - a.X);
        }

        private static double SegmentDistance(Point p, PointF a, PointF b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            if (dx == 0 && dy == 0) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
            t = Math.Max(0, Math.Min(1, t));
            double x = a.X + t * dx, y = a.Y + t * dy;
            return Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));
        }
    }
}
