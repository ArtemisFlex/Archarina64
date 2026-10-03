using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SharpOcarina.Shared;

namespace SharpOcarina
{
    /// <summary>
    /// In-process compatibility shell for the Dungeon Editor. The legacy
    /// Archarina form and its OpenTK context remain isolated inside this
    /// control while the shared host moves toward a true editor workspace.
    /// </summary>
    public sealed class ArcharinaEditorHost : UserControl
    {
        private MainForm editor;
        private Timer editorLoop;
        private SharedEditorWorkspace sharedWorkspace;
        private SharedSceneHandoff latestExternalSceneHandoff;

        public event EventHandler<SharedSceneHandoffEventArgs> SceneHandoffReceived;

        public ArcharinaEditorHost(SharedEditorWorkspace workspace = null)
        {
            if (LicenseManager.UsageMode == LicenseUsageMode.Designtime) return;
            sharedWorkspace = workspace ?? SharedEditorWorkspace.CreateDefault(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "authored_geometry"));
            latestExternalSceneHandoff = sharedWorkspace.LastSceneHandoff;
            sharedWorkspace.SceneHandoffPublished += SharedWorkspace_SceneHandoffPublished;
            ArcharinaWorkspace.Initialize(sharedWorkspace);
            editor = new MainForm(new string[0]);
            Program.MF = editor;
            editor.TopLevel = false;
            editor.FormBorderStyle = FormBorderStyle.None;
            editor.ShowInTaskbar = false;
            editor.Dock = DockStyle.Fill;
            editor.StartPosition = FormStartPosition.Manual;
            editor.Size = new Size(Width, Height);
            Controls.Add(editor);
            // The Dungeon Builder embeds the legacy editor as a child form, so
            // it is not guaranteed to receive the normal top-level theme pass
            // before its native text controls are first painted.
            ArcharinaUiTheme.Apply(editor);
            editor.Show();

            editorLoop = new Timer { Interval = 16 };
            editorLoop.Tick += delegate { if (!editor.IsDisposed) editor.ProgramMainLoop(); };
            editorLoop.Start();
        }

        public MainForm Editor { get { return editor; } }

        public ArcharinaSceneDocument Document { get { return MainForm.CurrentDocument; } }

        public SharedEditorWorkspace Workspace { get { return sharedWorkspace; } }

        public SharedSceneHandoff LatestExternalSceneHandoff
        {
            get { return latestExternalSceneHandoff == null ? null : latestExternalSceneHandoff.Clone(); }
        }

        private void SharedWorkspace_SceneHandoffPublished(object sender, SharedSceneHandoffEventArgs e)
        {
            if (e == null || e.Handoff == null || string.Equals(e.Handoff.Publisher, "Archarina64", StringComparison.Ordinal)) return;
            latestExternalSceneHandoff = e.Handoff.Clone();
            EventHandler<SharedSceneHandoffEventArgs> handler = SceneHandoffReceived;
            if (handler != null) handler(this, new SharedSceneHandoffEventArgs(latestExternalSceneHandoff.Clone()));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (sharedWorkspace != null) sharedWorkspace.SceneHandoffPublished -= SharedWorkspace_SceneHandoffPublished;
                if (editorLoop != null)
                {
                    editorLoop.Stop();
                    editorLoop.Dispose();
                }
                if (editor != null && !editor.IsDisposed)
                {
                    // The parent window owns the close decision. The legacy
                    // form's application-level prompt must not block disposal.
                    MainForm.savechanges = false;
                    editor.Hide();
                    editor.Close();
                    editor.Dispose();
                }
                if (object.ReferenceEquals(Program.MF, editor)) Program.MF = null;
            }
            base.Dispose(disposing);
        }
    }
}
