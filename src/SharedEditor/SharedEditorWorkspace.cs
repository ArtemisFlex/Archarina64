using System;
using System.Collections.Generic;
using System.IO;

namespace SharpOcarina.Shared
{
    /// <summary>
    /// Shared services for one running editor session. Native scene models,
    /// controls, undo stacks, and renderer contexts remain owned by each
    /// editor; this object is the safe handoff point for shared facilities.
    /// </summary>
    public sealed class SharedEditorWorkspace
    {
        public SharedEditorWorkspace(string authoredGeometryDirectory, string project64SettingsDirectory,
            string legacyAuthoredGeometryDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(authoredGeometryDirectory))
                throw new ArgumentNullException("authoredGeometryDirectory");
            if (string.IsNullOrWhiteSpace(project64SettingsDirectory))
                throw new ArgumentNullException("project64SettingsDirectory");

            WorkspaceId = Guid.NewGuid();
            AuthoredGeometry = new SharedAssetRepository(authoredGeometryDirectory, legacyAuthoredGeometryDirectory);
            Project64 = new Project64PlaytestService(project64SettingsDirectory);
        }

        public Guid WorkspaceId { get; private set; }
        public SharedAssetRepository AuthoredGeometry { get; private set; }
        public Project64PlaytestService Project64 { get; private set; }

        private readonly object handoffSync = new object();
        private SharedSceneHandoff lastSceneHandoff;
        private readonly Dictionary<string, SharedSceneHandoff> sceneHandoffsByPublisher =
            new Dictionary<string, SharedSceneHandoff>(StringComparer.Ordinal);

        public event EventHandler<SharedSceneHandoffEventArgs> SceneHandoffPublished;

        public SharedSceneHandoff LastSceneHandoff
        {
            get
            {
                lock (handoffSync) return lastSceneHandoff == null ? null : lastSceneHandoff.Clone();
            }
        }

        public static SharedEditorWorkspace CreateDefault(string legacyAuthoredGeometryDirectory = null)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string authored = Path.Combine(local, "SharpOcarina", "authored_geometry");
            string project64 = Path.Combine(local, "Archarina64", "library");
            return new SharedEditorWorkspace(authored, project64, legacyAuthoredGeometryDirectory);
        }

        public void Ensure()
        {
            AuthoredGeometry.Ensure();
        }

        /// <summary>
        /// Publishes a neutral snapshot to the other editor surfaces in this
        /// process. The event receives a clone so a renderer cannot mutate the
        /// publisher's native/editor state through the shared boundary.
        /// </summary>
        public void PublishSceneHandoff(SharedSceneHandoff handoff)
        {
            if (handoff == null) throw new ArgumentNullException("handoff");
            handoff.Validate(WorkspaceId);
            SharedSceneHandoff snapshot = handoff.Clone();
            lock (handoffSync)
            {
                SharedSceneHandoff previous;
                if (sceneHandoffsByPublisher.TryGetValue(snapshot.Publisher, out previous) &&
                    previous != null && previous.DocumentId == snapshot.DocumentId && snapshot.Revision < previous.Revision)
                    return;
                lastSceneHandoff = snapshot;
                sceneHandoffsByPublisher[snapshot.Publisher] = snapshot;
            }
            EventHandler<SharedSceneHandoffEventArgs> handler = SceneHandoffPublished;
            if (handler != null) handler(this, new SharedSceneHandoffEventArgs(snapshot.Clone()));
        }

        public bool TryGetSceneHandoff(out SharedSceneHandoff handoff)
        {
            handoff = LastSceneHandoff;
            return handoff != null;
        }

        public bool TryGetSceneHandoff(string publisher, out SharedSceneHandoff handoff)
        {
            SharedSceneHandoff snapshot = null;
            lock (handoffSync)
            {
                if (!string.IsNullOrWhiteSpace(publisher))
                    sceneHandoffsByPublisher.TryGetValue(publisher, out snapshot);
            }
            if (snapshot == null)
            {
                handoff = null;
                return false;
            }
            handoff = snapshot.Clone();
            return true;
        }
    }
}
