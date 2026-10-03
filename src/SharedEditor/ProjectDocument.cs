using System;

namespace SharpOcarina.Shared
{
    /// <summary>
    /// Shared document lifecycle used by the scene and dungeon editors.
    /// Native scene and editor-side state remain typed by each host; this
    /// layer owns identity, dirty state, and change notifications.
    /// </summary>
    public abstract class ProjectDocument
    {
        private readonly object sync = new object();
        private int updateDepth;
        private string pendingReason;

        protected ProjectDocument(string sourcePath)
        {
            DocumentId = Guid.NewGuid();
            SourcePath = sourcePath ?? string.Empty;
        }

        public Guid DocumentId { get; private set; }
        public string SourcePath { get; private set; }
        public bool IsDirty { get; private set; }
        public int Revision { get; private set; }

        public event EventHandler<DocumentChangedEventArgs> Changed;

        public void SetSourcePath(string sourcePath)
        {
            SourcePath = sourcePath ?? string.Empty;
        }

        public void MarkSaved()
        {
            IsDirty = false;
        }

        public void MarkChanged(string reason)
        {
            EventHandler<DocumentChangedEventArgs> handler = null;
            DocumentChangedEventArgs args = null;
            lock (sync)
            {
                IsDirty = true;
                if (updateDepth > 0)
                {
                    if (string.IsNullOrWhiteSpace(pendingReason))
                        pendingReason = string.IsNullOrWhiteSpace(reason) ? "Document changed" : reason;
                    return;
                }
                pendingReason = string.IsNullOrWhiteSpace(reason) ? "Document changed" : reason;
                Revision++;
                args = new DocumentChangedEventArgs(pendingReason, Revision);
                pendingReason = null;
                handler = Changed;
            }
            if (handler != null) handler(this, args);
        }

        public IDisposable BeginUpdate(string reason)
        {
            lock (sync)
            {
                updateDepth++;
                if (!string.IsNullOrWhiteSpace(reason)) pendingReason = reason;
            }
            return new UpdateScope(this);
        }

        private void EndUpdate()
        {
            EventHandler<DocumentChangedEventArgs> handler = null;
            DocumentChangedEventArgs args = null;
            lock (sync)
            {
                if (updateDepth == 0) return;
                updateDepth--;
                if (updateDepth != 0 || string.IsNullOrWhiteSpace(pendingReason)) return;
                IsDirty = true;
                Revision++;
                args = new DocumentChangedEventArgs(pendingReason, Revision);
                pendingReason = null;
                handler = Changed;
            }
            if (handler != null) handler(this, args);
        }

        private sealed class UpdateScope : IDisposable
        {
            private ProjectDocument document;

            internal UpdateScope(ProjectDocument document)
            {
                this.document = document;
            }

            public void Dispose()
            {
                if (document == null) return;
                ProjectDocument target = document;
                document = null;
                target.EndUpdate();
            }
        }
    }

    public sealed class DocumentChangedEventArgs : EventArgs
    {
        public DocumentChangedEventArgs(string reason, int revision)
        {
            Reason = reason;
            Revision = revision;
        }

        public string Reason { get; private set; }
        public int Revision { get; private set; }
    }

    public class ProjectDocument<TNativeScene, TSidecarState> : ProjectDocument
    {
        public ProjectDocument(TNativeScene nativeScene, TSidecarState sidecarState, string sourcePath = null)
            : base(sourcePath)
        {
            NativeScene = nativeScene;
            SidecarState = sidecarState;
        }

        public TNativeScene NativeScene { get; private set; }
        public TSidecarState SidecarState { get; private set; }

        public void ReplaceNativeScene(TNativeScene scene, string reason)
        {
            SetNativeScene(scene);
            MarkChanged(reason);
        }

        public void ReplaceSidecarState(TSidecarState state, string reason)
        {
            SetSidecarState(state);
            MarkChanged(reason);
        }

        /// <summary>
        /// Updates the owned native scene while a host is loading or restoring
        /// state. Use ReplaceNativeScene for an authored edit that should dirty
        /// the document and notify other editor contexts.
        /// </summary>
        public void SetNativeScene(TNativeScene scene)
        {
            NativeScene = scene;
        }

        /// <summary>
        /// Updates the owned editor-side state without manufacturing an edit.
        /// Hosts use this when synchronizing a sidecar loaded from disk.
        /// </summary>
        public void SetSidecarState(TSidecarState state)
        {
            SidecarState = state;
        }
    }

    /// <summary>
    /// Per-window editor ownership. UI controls, undo stacks, and GL contexts
    /// stay outside this class; only document ownership is shared.
    /// </summary>
    public sealed class EditorContext<TNativeScene, TSidecarState>
    {
        public EditorContext(ProjectDocument<TNativeScene, TSidecarState> document, string editorName)
        {
            if (document == null) throw new ArgumentNullException("document");
            Document = document;
            EditorName = string.IsNullOrWhiteSpace(editorName) ? "Editor" : editorName;
            ContextId = Guid.NewGuid();
        }

        public Guid ContextId { get; private set; }
        public string EditorName { get; private set; }
        public ProjectDocument<TNativeScene, TSidecarState> Document { get; private set; }

        public void MarkChanged(string reason)
        {
            Document.MarkChanged(EditorName + ": " + (reason ?? "changed"));
        }
    }
}
