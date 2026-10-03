using System;
using System.IO;
using System.Windows.Forms;
using SharpOcarina.Shared;

namespace SharpOcarina
{
    internal sealed class AuthoredMeshLibraryForm : Form
    {
        private readonly MainForm owner;
        private readonly SharedAssetRepository repository;
        private readonly ListBox assets = new ListBox { Dock = DockStyle.Fill };
        private readonly Label details = new Label { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(6) };

        public AuthoredMeshLibraryForm(MainForm owner)
        {
            this.owner = owner;
            repository = owner.GetAuthoredGeometryRepository();
            repository.AssetsChanged += Repository_AssetsChanged;
            FormClosed += delegate { repository.AssetsChanged -= Repository_AssetsChanged; };
            Text = "Authored Model Library";
            StartPosition = FormStartPosition.CenterParent;
            Width = 560;
            Height = 420;
            FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false, Padding = new Padding(4) };
            Button refresh = new Button { Text = "Refresh", AutoSize = true };
            Button insert = new Button { Text = "Insert into room", AutoSize = true };
            Button edit = new Button { Text = "Edit N64 settings", AutoSize = true };
            refresh.Click += delegate { RefreshAssets(); };
            insert.Click += delegate { InsertSelected(); };
            edit.Click += delegate { EditSelected(); };
            assets.SelectedIndexChanged += delegate { UpdateDetails(); };
            assets.DoubleClick += delegate { InsertSelected(); };
            toolbar.Controls.Add(refresh);
            toolbar.Controls.Add(insert);
            toolbar.Controls.Add(edit);
            Controls.Add(assets);
            Controls.Add(details);
            Controls.Add(toolbar);
            Shown += delegate { RefreshAssets(); };
        }

        private void RefreshAssets()
        {
            assets.Items.Clear();
            foreach (string path in repository.EnumerateAuthoredMeshes()) assets.Items.Add(path);
            UpdateDetails();
        }

        private void Repository_AssetsChanged(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new MethodInvoker(RefreshAssets)); } catch (InvalidOperationException) { }
                return;
            }
            RefreshAssets();
        }

        private void UpdateDetails()
        {
            string path = assets.SelectedItem as string;
            if (string.IsNullOrEmpty(path)) { details.Text = "No authored assets found."; return; }
            AuthoredMeshAssetManifest manifest;
            if (AuthoredMeshAssetManifestReader.TryRead(path, out manifest))
            {
                string features = string.Format("{0}{1}{2}",
                    manifest.HasUvData ? " | UV" : string.Empty,
                    manifest.HasVertexColors ? " | colors" : string.Empty,
                    manifest.HasMirrorModifier ? " | mirror" : string.Empty);
                details.Text = string.Format("{0} | {1:N0} vertices | {2:N0} triangles{3}",
                    manifest.DisplayName, manifest.VertexCount, manifest.FaceCount, features);
            }
            else details.Text = "Unable to read asset: " + Path.GetFileName(path);
        }

        private void InsertSelected()
        {
            string path = assets.SelectedItem as string;
            if (string.IsNullOrEmpty(path)) return;
            if (owner.InsertAuthoredMeshAsset(path)) UpdateDetails();
        }

        private void EditSelected()
        {
            string path = assets.SelectedItem as string;
            if (string.IsNullOrEmpty(path)) return;
            using (AuthoredMeshAssetEditorForm editor = new AuthoredMeshAssetEditorForm(path, repository))
                if (editor.ShowDialog(this) == DialogResult.OK) UpdateDetails();
        }
    }
}
