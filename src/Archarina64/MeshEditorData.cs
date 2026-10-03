using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OpenTK;

namespace SharpOcarina
{
    /// <summary>
    /// Native render state that must travel with a mesh edit. These fields are
    /// normally kept on ObjFile.Group as runtime display-list state, so a mesh
    /// edit must snapshot them before the preview buffer is detached.
    /// </summary>
    internal sealed class MeshEditorGroupState
    {
        public string Name;
        public uint TintAlpha;
        public int TileS, TileT, PolyType;
        public bool BackfaceCulling, Animated, Metallic, EnvColor, Decal, IgnoreFog;
        public bool SmoothRGBAEdges, Pixelated, Billboard, TwoAxisBillboard, ReverseLight;
        public int MultiTexMaterial, ShiftS, ShiftT, BaseShiftS, BaseShiftT, AnimationBank;
        public string MultiTexMaterialName;
        public int LodGroup, LodDistance;
        public uint MultiTexAlpha;
        public bool LOD, AlphaMask, RenderLast, VertexNormals, Custom, ScaledNormals, TexPointerPlus1;
        public int Type2Group;
        public ulong[] CustomDL;
        public ZScene.Vector3s PivotPoint;

        public static MeshEditorGroupState Capture(ObjFile.Group group)
        {
            if (group == null) return null;
            return new MeshEditorGroupState
            {
                Name = group.Name,
                TintAlpha = group.TintAlpha,
                TileS = group.TileS,
                TileT = group.TileT,
                PolyType = group.PolyType,
                BackfaceCulling = group.BackfaceCulling,
                Animated = group.Animated,
                Metallic = group.Metallic,
                EnvColor = group.EnvColor,
                Decal = group.Decal,
                IgnoreFog = group.IgnoreFog,
                SmoothRGBAEdges = group.SmoothRGBAEdges,
                Pixelated = group.Pixelated,
                Billboard = group.Billboard,
                TwoAxisBillboard = group.TwoAxisBillboard,
                ReverseLight = group.ReverseLight,
                MultiTexMaterial = group.MultiTexMaterial,
                ShiftS = group.ShiftS,
                ShiftT = group.ShiftT,
                BaseShiftS = group.BaseShiftS,
                BaseShiftT = group.BaseShiftT,
                AnimationBank = group.AnimationBank,
                MultiTexMaterialName = group.MultiTexMaterialName,
                LodGroup = group.LodGroup,
                LodDistance = group.LodDistance,
                MultiTexAlpha = group.MultiTexAlpha,
                LOD = group.LOD,
                AlphaMask = group.AlphaMask,
                RenderLast = group.RenderLast,
                VertexNormals = group.VertexNormals,
                Custom = group.Custom,
                ScaledNormals = group.ScaledNormals,
                TexPointerPlus1 = group.TexPointerPlus1,
                Type2Group = group.Type2Group,
                CustomDL = group.CustomDL == null ? null : (ulong[])group.CustomDL.Clone(),
                PivotPoint = group.PivotPoint
            };
        }

        public MeshEditorGroupState Clone()
        {
            MeshEditorGroupState clone = (MeshEditorGroupState)MemberwiseClone();
            clone.CustomDL = CustomDL == null ? null : (ulong[])CustomDL.Clone();
            return clone;
        }

        public void Apply(ObjFile.Group group)
        {
            if (group == null) return;
            group.Name = Name;
            group.TintAlpha = TintAlpha;
            group.TileS = TileS;
            group.TileT = TileT;
            group.PolyType = PolyType;
            group.BackfaceCulling = BackfaceCulling;
            group.Animated = Animated;
            group.Metallic = Metallic;
            group.EnvColor = EnvColor;
            group.Decal = Decal;
            group.IgnoreFog = IgnoreFog;
            group.SmoothRGBAEdges = SmoothRGBAEdges;
            group.Pixelated = Pixelated;
            group.Billboard = Billboard;
            group.TwoAxisBillboard = TwoAxisBillboard;
            group.ReverseLight = ReverseLight;
            group.MultiTexMaterial = MultiTexMaterial;
            group.ShiftS = ShiftS;
            group.ShiftT = ShiftT;
            group.BaseShiftS = BaseShiftS;
            group.BaseShiftT = BaseShiftT;
            group.AnimationBank = AnimationBank;
            group.MultiTexMaterialName = MultiTexMaterialName;
            group.LodGroup = LodGroup;
            group.LodDistance = LodDistance;
            group.MultiTexAlpha = MultiTexAlpha;
            group.LOD = LOD;
            group.AlphaMask = AlphaMask;
            group.RenderLast = RenderLast;
            group.VertexNormals = VertexNormals;
            group.Custom = Custom;
            group.ScaledNormals = ScaledNormals;
            group.TexPointerPlus1 = TexPointerPlus1;
            group.Type2Group = Type2Group;
            group.CustomDL = CustomDL == null ? null : (ulong[])CustomDL.Clone();
            group.PivotPoint = PivotPoint;
        }
    }

    /// <summary>
    /// Lightweight editable mesh buffer used by the native mesh workspace.
    /// It deliberately stays independent from OpenGL display lists while the
    /// user is editing, then can be committed back to an ObjFile group.
    /// </summary>
    internal sealed class MeshEditorData
    {
        public static int EdgeKey(int a, int b)
        {
            int low = Math.Min(a, b), high = Math.Max(a, b);
            return (low << 16) ^ (high & 0xFFFF);
        }

        public static int EdgeStart(int key) { return (int)((uint)key >> 16); }
        public static int EdgeEnd(int key) { return key & 0xFFFF; }

        public readonly List<ObjFile.Vertex> Vertices = new List<ObjFile.Vertex>();
        public readonly List<ObjFile.Triangle> Triangles = new List<ObjFile.Triangle>();
        // Original triangle objects retain their source UV/normal/color indices.
        // New topology faces are intentionally absent and receive generated data.
        public readonly HashSet<ObjFile.Triangle> SourceTriangles = new HashSet<ObjFile.Triangle>();
        public MeshEditorGroupState SourceGroupState;
        public readonly List<ObjFile.TextureCoord> SourceTextureCoordinates = new List<ObjFile.TextureCoord>();
        public readonly List<ObjFile.Normal> SourceNormals = new List<ObjFile.Normal>();
        public readonly List<ObjFile.VertexColor> SourceVertexColors = new List<ObjFile.VertexColor>();
        public readonly List<ObjFile.Material> SourceMaterials = new List<ObjFile.Material>();
        public readonly List<ObjFile.Material> SourceAdditionalTextures = new List<ObjFile.Material>();
        public int OriginalVertexCount;
        public readonly HashSet<int> SelectedVertices = new HashSet<int>();
        public readonly HashSet<int> SelectedEdges = new HashSet<int>();
        public readonly HashSet<int> SelectedFaces = new HashSet<int>();

        public static MeshEditorData FromGroup(ObjFile model, ObjFile.Group group)
        {
            MeshEditorData data = new MeshEditorData();
            if (model == null || group == null || group.Triangles == null) return data;

            data.SourceGroupState = MeshEditorGroupState.Capture(group);
            foreach (ObjFile.TextureCoord textureCoordinate in model.TextureCoordinates)
                data.SourceTextureCoordinates.Add(textureCoordinate == null ? null : textureCoordinate.Clone());
            foreach (ObjFile.Normal normal in model.Normals)
                data.SourceNormals.Add(normal == null ? null : normal.Clone());
            foreach (ObjFile.VertexColor color in model.VertexColors)
                data.SourceVertexColors.Add(color == null ? null : color.Clone());
            foreach (ObjFile.Material material in model.Materials)
                data.SourceMaterials.Add(CloneMaterial(material));
            foreach (ObjFile.Material material in model.AdditionalTextures)
                data.SourceAdditionalTextures.Add(CloneMaterial(material));

            Dictionary<int, int> remap = new Dictionary<int, int>();
            foreach (ObjFile.Triangle source in group.Triangles)
            {
                if (source == null || source.VertIndex == null || source.VertIndex.Length < 3) continue;
                int[] indices = new int[3];
                bool valid = true;
                for (int corner = 0; corner < 3; corner++)
                {
                    int oldIndex = source.VertIndex[corner];
                    int newIndex;
                    if (!remap.TryGetValue(oldIndex, out newIndex))
                    {
                        if (oldIndex < 0 || oldIndex >= model.Vertices.Count)
                        {
                            valid = false;
                            break;
                        }
                        newIndex = data.Vertices.Count;
                        remap.Add(oldIndex, newIndex);
                        data.Vertices.Add(model.Vertices[oldIndex].Clone());
                    }
                    indices[corner] = newIndex;
                }
                if (!valid) continue;
                ObjFile.Triangle copy = new ObjFile.Triangle
                {
                    MaterialName = source.MaterialName,
                    VertIndex = indices,
                    TexCoordIndex = source.TexCoordIndex == null ? null : (int[])source.TexCoordIndex.Clone(),
                    NormalIndex = source.NormalIndex == null ? null : (int[])source.NormalIndex.Clone(),
                    VertColor = source.VertColor == null ? null : (int[])source.VertColor.Clone()
                };
                data.Triangles.Add(copy);
                data.SourceTriangles.Add(copy);
            }
            data.OriginalVertexCount = data.Vertices.Count;
            return data;
        }

        public static MeshEditorData CreateDefaultCube(double size)
        {
            MeshEditorData data = new MeshEditorData();
            double half = size * 0.5;
            data.Vertices.Add(new ObjFile.Vertex(-half, -half, -half));
            data.Vertices.Add(new ObjFile.Vertex(half, -half, -half));
            data.Vertices.Add(new ObjFile.Vertex(half, half, -half));
            data.Vertices.Add(new ObjFile.Vertex(-half, half, -half));
            data.Vertices.Add(new ObjFile.Vertex(-half, -half, half));
            data.Vertices.Add(new ObjFile.Vertex(half, -half, half));
            data.Vertices.Add(new ObjFile.Vertex(half, half, half));
            data.Vertices.Add(new ObjFile.Vertex(-half, half, half));
            int[][] faces =
            {
                new[] { 0, 1, 2 }, new[] { 0, 2, 3 },
                new[] { 4, 6, 5 }, new[] { 4, 7, 6 },
                new[] { 0, 4, 5 }, new[] { 0, 5, 1 },
                new[] { 3, 2, 6 }, new[] { 3, 6, 7 },
                new[] { 0, 3, 7 }, new[] { 0, 7, 4 },
                new[] { 1, 5, 6 }, new[] { 1, 6, 2 }
            };
            foreach (int[] face in faces)
                data.Triangles.Add(new ObjFile.Triangle("Default", face, new int[3], new int[3]));
            data.OriginalVertexCount = data.Vertices.Count;
            return data;
        }

        public void ClearSelection()
        {
            SelectedVertices.Clear();
            SelectedEdges.Clear();
            SelectedFaces.Clear();
        }

        public MeshEditorData Clone()
        {
            MeshEditorData clone = new MeshEditorData();
            clone.OriginalVertexCount = OriginalVertexCount;
            clone.SourceGroupState = SourceGroupState == null ? null : SourceGroupState.Clone();
            foreach (ObjFile.TextureCoord textureCoordinate in SourceTextureCoordinates)
                clone.SourceTextureCoordinates.Add(textureCoordinate == null ? null : textureCoordinate.Clone());
            foreach (ObjFile.Normal normal in SourceNormals)
                clone.SourceNormals.Add(normal == null ? null : normal.Clone());
            foreach (ObjFile.VertexColor color in SourceVertexColors)
                clone.SourceVertexColors.Add(color == null ? null : color.Clone());
            foreach (ObjFile.Material material in SourceMaterials)
                clone.SourceMaterials.Add(CloneMaterial(material));
            foreach (ObjFile.Material material in SourceAdditionalTextures)
                clone.SourceAdditionalTextures.Add(CloneMaterial(material));
            Dictionary<ObjFile.Triangle, ObjFile.Triangle> triangleMap = new Dictionary<ObjFile.Triangle, ObjFile.Triangle>();
            foreach (ObjFile.Vertex vertex in Vertices) clone.Vertices.Add(vertex.Clone());
            foreach (ObjFile.Triangle triangle in Triangles)
            {
                ObjFile.Triangle copy = new ObjFile.Triangle
                {
                    MaterialName = triangle.MaterialName,
                    VertIndex = triangle.VertIndex == null ? null : (int[])triangle.VertIndex.Clone(),
                    TexCoordIndex = triangle.TexCoordIndex == null ? null : (int[])triangle.TexCoordIndex.Clone(),
                    NormalIndex = triangle.NormalIndex == null ? null : (int[])triangle.NormalIndex.Clone(),
                    VertColor = triangle.VertColor == null ? null : (int[])triangle.VertColor.Clone()
                };
                clone.Triangles.Add(copy);
                triangleMap[triangle] = copy;
                if (SourceTriangles.Contains(triangle)) clone.SourceTriangles.Add(copy);
            }
            clone.SelectedVertices.UnionWith(SelectedVertices);
            clone.SelectedEdges.UnionWith(SelectedEdges);
            clone.SelectedFaces.UnionWith(SelectedFaces);
            return clone;
        }

        public void Restore(MeshEditorData source)
        {
            if (source == null) return;
            MeshEditorData copy = source.Clone();
            OriginalVertexCount = copy.OriginalVertexCount;
            SourceGroupState = copy.SourceGroupState;
            SourceTextureCoordinates.Clear(); SourceTextureCoordinates.AddRange(copy.SourceTextureCoordinates);
            SourceNormals.Clear(); SourceNormals.AddRange(copy.SourceNormals);
            SourceVertexColors.Clear(); SourceVertexColors.AddRange(copy.SourceVertexColors);
            SourceMaterials.Clear(); SourceMaterials.AddRange(copy.SourceMaterials);
            SourceAdditionalTextures.Clear(); SourceAdditionalTextures.AddRange(copy.SourceAdditionalTextures);
            Vertices.Clear(); Vertices.AddRange(copy.Vertices);
            Triangles.Clear(); Triangles.AddRange(copy.Triangles);
            SourceTriangles.Clear(); SourceTriangles.UnionWith(copy.SourceTriangles);
            ClearSelection();
            SelectedVertices.UnionWith(copy.SelectedVertices);
            SelectedEdges.UnionWith(copy.SelectedEdges);
            SelectedFaces.UnionWith(copy.SelectedFaces);
        }

        public void RestoreSourceSurfaceState(ObjFile model)
        {
            if (model == null) return;
            model.TextureCoordinates.Clear();
            model.TextureCoordinates.AddRange(SourceTextureCoordinates.Select(x => x == null ? null : x.Clone()));
            model.Normals.Clear();
            model.Normals.AddRange(SourceNormals.Select(x => x == null ? null : x.Clone()));
            model.VertexColors.Clear();
            model.VertexColors.AddRange(SourceVertexColors.Select(x => x == null ? null : x.Clone()));
            model.Materials.Clear();
            model.Materials.AddRange(SourceMaterials.Select(CloneMaterial));
            model.AdditionalTextures.Clear();
            model.AdditionalTextures.AddRange(SourceAdditionalTextures.Select(CloneMaterial));
        }

        private static ObjFile.Material CloneMaterial(ObjFile.Material source)
        {
            if (source == null) return null;
            ObjFile.Material clone = source.Clone();
            clone.Ka = source.Ka == null ? null : (float[])source.Ka.Clone();
            clone.Kd = source.Kd == null ? null : (float[])source.Kd.Clone();
            clone.Ks = source.Ks == null ? null : (float[])source.Ks.Clone();
            clone.TexImage = source.TexImage == null ? null : new System.Drawing.Bitmap(source.TexImage);
            return clone;
        }

        public void SaveObj(string path, string objectName)
        {
            using (StreamWriter writer = new StreamWriter(path, false))
            {
                writer.WriteLine("# SharpOcarina authored mesh");
                writer.WriteLine("o " + (string.IsNullOrEmpty(objectName) ? "AuthoredMesh" : objectName));
                foreach (ObjFile.Vertex vertex in Vertices)
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "v {0} {1} {2}", vertex.X, vertex.Y, vertex.Z));
                foreach (ObjFile.Triangle triangle in Triangles)
                {
                    if (triangle.VertIndex == null || triangle.VertIndex.Length < 3) continue;
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "f {0} {1} {2}",
                        triangle.VertIndex[0] + 1, triangle.VertIndex[1] + 1, triangle.VertIndex[2] + 1));
                }
            }
        }

        public void TranslateSelection(Vector3d delta)
        {
            HashSet<int> moved = GetSelectedVertexIndices();
            foreach (int index in moved)
            {
                if (index < 0 || index >= Vertices.Count) continue;
                Vertices[index].X += delta.X;
                Vertices[index].Y += delta.Y;
                Vertices[index].Z += delta.Z;
            }
        }

        public void ScaleSelection(Vector3d scale)
        {
            HashSet<int> moved = GetSelectedVertexIndices();
            if (moved.Count == 0) return;
            Vector3d center = Vector3d.Zero;
            int count = 0;
            foreach (int index in moved)
            {
                if (index < 0 || index >= Vertices.Count) continue;
                center += Vertices[index].ToVector3d();
                count++;
            }
            if (count == 0) return;
            center /= count;
            foreach (int index in moved)
            {
                if (index < 0 || index >= Vertices.Count) continue;
                Vector3d offset = Vertices[index].ToVector3d() - center;
                Vertices[index].X = center.X + offset.X * scale.X;
                Vertices[index].Y = center.Y + offset.Y * scale.Y;
                Vertices[index].Z = center.Z + offset.Z * scale.Z;
            }
        }

        public void RotateSelection(Vector3d degrees)
        {
            HashSet<int> moved = GetSelectedVertexIndices();
            if (moved.Count == 0) return;
            Vector3d center = Vector3d.Zero;
            int count = 0;
            foreach (int index in moved)
            {
                if (index < 0 || index >= Vertices.Count) continue;
                center += Vertices[index].ToVector3d();
                count++;
            }
            if (count == 0) return;
            center /= count;
            double rx = degrees.X * Math.PI / 180.0;
            double ry = degrees.Y * Math.PI / 180.0;
            double rz = degrees.Z * Math.PI / 180.0;
            double cx = Math.Cos(rx), sx = Math.Sin(rx), cy = Math.Cos(ry), sy = Math.Sin(ry), cz = Math.Cos(rz), sz = Math.Sin(rz);
            foreach (int index in moved)
            {
                if (index < 0 || index >= Vertices.Count) continue;
                Vector3d p = Vertices[index].ToVector3d() - center;
                double y = p.Y * cx - p.Z * sx;
                double z = p.Y * sx + p.Z * cx;
                p.Y = y; p.Z = z;
                double x = p.X * cy + p.Z * sy;
                z = -p.X * sy + p.Z * cy;
                p.X = x; p.Z = z;
                x = p.X * cz - p.Y * sz;
                y = p.X * sz + p.Y * cz;
                p.X = x; p.Y = y;
                Vertices[index].X = center.X + p.X;
                Vertices[index].Y = center.Y + p.Y;
                Vertices[index].Z = center.Z + p.Z;
            }
        }

        private HashSet<int> GetSelectedVertexIndices()
        {
            HashSet<int> moved = new HashSet<int>(SelectedVertices);
            foreach (int edge in SelectedEdges)
            {
                int start = EdgeStart(edge), end = EdgeEnd(edge);
                if (start >= 0 && start < Vertices.Count) moved.Add(start);
                if (end >= 0 && end < Vertices.Count) moved.Add(end);
            }
            foreach (int face in SelectedFaces)
            {
                if (face < 0 || face >= Triangles.Count) continue;
                foreach (int vertex in Triangles[face].VertIndex) moved.Add(vertex);
            }
            return moved;
        }

        public void AssignMaterial(string materialName)
        {
            if (string.IsNullOrEmpty(materialName)) return;
            foreach (int faceIndex in SelectedFaces)
            {
                if (faceIndex < 0 || faceIndex >= Triangles.Count) continue;
                Triangles[faceIndex].MaterialName = materialName;
            }
        }

        public void SelectLinked(EditSelectionMode mode)
        {
            if (mode == EditSelectionMode.Vertex)
            {
                HashSet<int> linked = new HashSet<int>(SelectedVertices);
                bool changed;
                do
                {
                    changed = false;
                    foreach (ObjFile.Triangle triangle in Triangles)
                    {
                        if (triangle == null || triangle.VertIndex == null) continue;
                        bool touches = linked.Contains(triangle.VertIndex[0]) || linked.Contains(triangle.VertIndex[1]) || linked.Contains(triangle.VertIndex[2]);
                        if (touches)
                            for (int corner = 0; corner < 3; corner++) changed |= linked.Add(triangle.VertIndex[corner]);
                    }
                } while (changed);
                SelectedVertices.Clear();
                SelectedVertices.UnionWith(linked);
                return;
            }

            if (mode == EditSelectionMode.Edge)
            {
                HashSet<int> linkedEdges = new HashSet<int>(SelectedEdges);
                HashSet<int> linkedVertices = new HashSet<int>();
                foreach (int edge in linkedEdges) { linkedVertices.Add(EdgeStart(edge)); linkedVertices.Add(EdgeEnd(edge)); }
                bool changed;
                do
                {
                    changed = false;
                    foreach (ObjFile.Triangle triangle in Triangles)
                    {
                        for (int corner = 0; corner < 3; corner++)
                        {
                            int a = triangle.VertIndex[corner], b = triangle.VertIndex[(corner + 1) % 3];
                            if (!linkedVertices.Contains(a) && !linkedVertices.Contains(b)) continue;
                            changed |= linkedEdges.Add(EdgeKey(a, b));
                            changed |= linkedVertices.Add(a);
                            changed |= linkedVertices.Add(b);
                        }
                    }
                } while (changed);
                SelectedEdges.Clear();
                SelectedEdges.UnionWith(linkedEdges);
                return;
            }

            HashSet<int> selectedFaces = mode == EditSelectionMode.Face
                ? new HashSet<int>(SelectedFaces)
                : new HashSet<int>();
            bool expanded;
            do
            {
                expanded = false;
                for (int a = 0; a < Triangles.Count; a++)
                {
                    if (!selectedFaces.Contains(a) || Triangles[a] == null) continue;
                    for (int b = 0; b < Triangles.Count; b++)
                    {
                        if (selectedFaces.Contains(b) || Triangles[b] == null) continue;
                        if (ShareVertex(Triangles[a], Triangles[b])) expanded |= selectedFaces.Add(b);
                    }
                }
            } while (expanded);
            if (mode == EditSelectionMode.Face)
            {
                SelectedFaces.Clear();
                SelectedFaces.UnionWith(selectedFaces);
            }
            else SelectedEdges.Clear();
        }

        public void SelectFaceLoop()
        {
            HashSet<int> loop = new HashSet<int>(SelectedFaces);
            if (loop.Count == 0) return;
            HashSet<int> expanded = new HashSet<int>(loop);
            foreach (int selected in loop)
            {
                if (selected < 0 || selected >= Triangles.Count || Triangles[selected] == null) continue;
                for (int face = 0; face < Triangles.Count; face++)
                {
                    if (!loop.Contains(face) && Triangles[face] != null && SharesEdge(Triangles[selected], Triangles[face]))
                        expanded.Add(face);
                }
            }
            SelectedFaces.Clear();
            SelectedFaces.UnionWith(expanded);
        }

        public void SelectEdgeLoop()
        {
            HashSet<int> loop = new HashSet<int>(SelectedEdges);
            if (loop.Count == 0) return;
            HashSet<int> expanded = new HashSet<int>(loop);
            foreach (ObjFile.Triangle triangle in Triangles)
            {
                for (int corner = 0; corner < 3; corner++)
                {
                    int a = triangle.VertIndex[corner], b = triangle.VertIndex[(corner + 1) % 3];
                    int edge = EdgeKey(a, b);
                    if (!loop.Contains(edge)) continue;
                    for (int next = 0; next < 3; next++)
                    {
                        int neighbor = EdgeKey(triangle.VertIndex[next], triangle.VertIndex[(next + 1) % 3]);
                        expanded.Add(neighbor);
                    }
                }
            }
            SelectedEdges.Clear();
            SelectedEdges.UnionWith(expanded);
        }

        public bool CloseSelectedBoundaryLoop()
        {
            if (SelectedFaces.Count == 0) return false;
            Dictionary<int, int> edgeUse = new Dictionary<int, int>();
            List<int[]> edges = new List<int[]>();
            foreach (int faceIndex in SelectedFaces)
            {
                if (faceIndex < 0 || faceIndex >= Triangles.Count) continue;
                int[] indices = Triangles[faceIndex].VertIndex;
                for (int corner = 0; corner < 3; corner++)
                {
                    int a = indices[corner], b = indices[(corner + 1) % 3];
                    int key = Math.Min(a, b) * 1000000 + Math.Max(a, b);
                    if (!edgeUse.ContainsKey(key)) { edgeUse[key] = edges.Count; edges.Add(new[] { a, b }); }
                    else edgeUse[key] = -1;
                }
            }
            List<int[]> boundary = new List<int[]>();
            foreach (int[] edge in edges)
            {
                int key = Math.Min(edge[0], edge[1]) * 1000000 + Math.Max(edge[0], edge[1]);
                if (edgeUse[key] >= 0) boundary.Add(edge);
            }
            if (boundary.Count < 3) return false;
            List<int> loop = new List<int> { boundary[0][0], boundary[0][1] };
            HashSet<int> usedEdges = new HashSet<int> { 0 };
            while (loop[loop.Count - 1] != loop[0] && usedEdges.Count < boundary.Count)
            {
                int current = loop[loop.Count - 1];
                int nextEdge = -1;
                for (int edge = 0; edge < boundary.Count; edge++)
                {
                    if (usedEdges.Contains(edge)) continue;
                    if (boundary[edge][0] == current || boundary[edge][1] == current) { nextEdge = edge; break; }
                }
                if (nextEdge < 0) return false;
                usedEdges.Add(nextEdge);
                int next = boundary[nextEdge][0] == current ? boundary[nextEdge][1] : boundary[nextEdge][0];
                loop.Add(next);
            }
            if (loop.Count < 4 || loop[loop.Count - 1] != loop[0]) return false;
            loop.RemoveAt(loop.Count - 1);
            Vector3d center = Vector3d.Zero;
            foreach (int vertex in loop) center += Vertices[vertex].ToVector3d();
            center /= loop.Count;
            int centerIndex = Vertices.Count;
            Vertices.Add(new ObjFile.Vertex(center.X, center.Y, center.Z));
            string material = Triangles[SelectedFaces.Count > 0 ? new List<int>(SelectedFaces)[0] : 0].MaterialName;
            int firstFace = Triangles.Count;
            for (int index = 0; index < loop.Count; index++)
                Triangles.Add(new ObjFile.Triangle(material,
                    new[] { centerIndex, loop[index], loop[(index + 1) % loop.Count] }, new int[3], new int[3]));
            ClearSelection();
            for (int face = firstFace; face < Triangles.Count; face++) SelectedFaces.Add(face);
            return true;
        }

        public void GrowSelection(EditSelectionMode mode)
        {
            if (mode == EditSelectionMode.Vertex)
            {
                HashSet<int> expanded = new HashSet<int>(SelectedVertices);
                foreach (ObjFile.Triangle triangle in Triangles)
                {
                    if (triangle == null || triangle.VertIndex == null) continue;
                    bool touches = false;
                    for (int corner = 0; corner < 3; corner++) touches |= SelectedVertices.Contains(triangle.VertIndex[corner]);
                    if (touches)
                        for (int corner = 0; corner < 3; corner++) expanded.Add(triangle.VertIndex[corner]);
                }
                SelectedVertices.Clear();
                SelectedVertices.UnionWith(expanded);
                return;
            }

            if (mode == EditSelectionMode.Edge)
            {
                HashSet<int> selectedEdges = new HashSet<int>(SelectedEdges);
                HashSet<int> selectedVertices = new HashSet<int>();
                foreach (int edge in selectedEdges) { selectedVertices.Add(EdgeStart(edge)); selectedVertices.Add(EdgeEnd(edge)); }
                foreach (ObjFile.Triangle triangle in Triangles)
                {
                    bool touches = false;
                    for (int corner = 0; corner < 3; corner++) touches |= selectedVertices.Contains(triangle.VertIndex[corner]);
                    if (!touches) continue;
                    for (int corner = 0; corner < 3; corner++)
                        selectedEdges.Add(EdgeKey(triangle.VertIndex[corner], triangle.VertIndex[(corner + 1) % 3]));
                }
                SelectedEdges.Clear();
                SelectedEdges.UnionWith(selectedEdges);
                return;
            }

            HashSet<int> selected = mode == EditSelectionMode.Face
                ? new HashSet<int>(SelectedFaces)
                : new HashSet<int>();
            HashSet<int> expandedFaces = new HashSet<int>(selected);
            foreach (int faceIndex in selected)
            {
                if (faceIndex < 0 || faceIndex >= Triangles.Count || Triangles[faceIndex] == null) continue;
                for (int neighbor = 0; neighbor < Triangles.Count; neighbor++)
                    if (!selected.Contains(neighbor) && Triangles[neighbor] != null && ShareVertex(Triangles[faceIndex], Triangles[neighbor]))
                        expandedFaces.Add(neighbor);
            }
            if (mode == EditSelectionMode.Face)
            {
                SelectedFaces.Clear();
                SelectedFaces.UnionWith(expandedFaces);
            }
            else
            {
                SelectedEdges.Clear();
                SelectedEdges.UnionWith(expandedFaces);
            }
        }

        public void ShrinkSelection(EditSelectionMode mode)
        {
            if (mode == EditSelectionMode.Vertex)
            {
                HashSet<int> keep = new HashSet<int>(SelectedVertices);
                foreach (ObjFile.Triangle triangle in Triangles)
                {
                    if (triangle == null || triangle.VertIndex == null) continue;
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int vertex = triangle.VertIndex[corner];
                        if (!SelectedVertices.Contains(vertex)) continue;
                        for (int other = 0; other < 3; other++)
                            if (!SelectedVertices.Contains(triangle.VertIndex[other])) keep.Remove(vertex);
                    }
                }
                SelectedVertices.Clear();
                SelectedVertices.UnionWith(keep);
                return;
            }

            if (mode == EditSelectionMode.Edge)
            {
                HashSet<int> selectedEdges = new HashSet<int>(SelectedEdges);
                HashSet<int> incident = new HashSet<int>();
                foreach (ObjFile.Triangle triangle in Triangles)
                    for (int corner = 0; corner < 3; corner++)
                        incident.Add(EdgeKey(triangle.VertIndex[corner], triangle.VertIndex[(corner + 1) % 3]));
                HashSet<int> keep = new HashSet<int>();
                foreach (int edge in selectedEdges)
                {
                    int start = EdgeStart(edge), end = EdgeEnd(edge);
                    bool boundary = false;
                    foreach (int candidate in incident)
                    {
                        if (candidate == edge) continue;
                        int a = EdgeStart(candidate), b = EdgeEnd(candidate);
                        if ((a == start || b == start || a == end || b == end) && !selectedEdges.Contains(candidate))
                        {
                            boundary = true;
                            break;
                        }
                    }
                    if (!boundary) keep.Add(edge);
                }
                SelectedEdges.Clear();
                SelectedEdges.UnionWith(keep);
                return;
            }

            HashSet<int> selected = mode == EditSelectionMode.Face
                ? new HashSet<int>(SelectedFaces)
                : new HashSet<int>();
            HashSet<int> keepFaces = new HashSet<int>(selected);
            foreach (int faceIndex in selected)
            {
                if (faceIndex < 0 || faceIndex >= Triangles.Count || Triangles[faceIndex] == null) continue;
                for (int neighbor = 0; neighbor < Triangles.Count; neighbor++)
                {
                    if (neighbor == faceIndex || Triangles[neighbor] == null) continue;
                    if (ShareVertex(Triangles[faceIndex], Triangles[neighbor]) && !selected.Contains(neighbor))
                    {
                        keepFaces.Remove(faceIndex);
                        break;
                    }
                }
            }
            if (mode == EditSelectionMode.Face)
            {
                SelectedFaces.Clear();
                SelectedFaces.UnionWith(keepFaces);
            }
            else
            {
                SelectedEdges.Clear();
                SelectedEdges.UnionWith(keepFaces);
            }
        }

        public enum EditSelectionMode { Vertex, Edge, Face }

        private static bool ShareVertex(ObjFile.Triangle a, ObjFile.Triangle b)
        {
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    if (a.VertIndex[i] == b.VertIndex[j]) return true;
            return false;
        }

        private static bool SharesEdge(ObjFile.Triangle a, ObjFile.Triangle b)
        {
            int shared = 0;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    if (a.VertIndex[i] == b.VertIndex[j]) shared++;
            return shared >= 2;
        }

        public void ExtrudeFace(int faceIndex, double distance)
        {
            if (faceIndex < 0 || faceIndex >= Triangles.Count) return;
            ExtrudeFaceInternal(faceIndex, distance);
            ClearSelection();
            SelectedFaces.Add(Triangles.Count - 7);
        }

        public void ExtrudeSelectedFaces(double distance)
        {
            List<int> faces = new List<int>(SelectedFaces);
            faces.Sort();
            ClearSelection();
            foreach (int faceIndex in faces)
            {
                if (faceIndex < 0 || faceIndex >= Triangles.Count) continue;
                int topFace = Triangles.Count;
                ExtrudeFaceInternal(faceIndex, distance);
                if (Triangles.Count > topFace) SelectedFaces.Add(topFace);
            }
        }

        public bool CreateFaceFromSelectedVertices(string materialName)
        {
            if (SelectedVertices.Count < 3) return false;
            List<int> indices = new List<int>(SelectedVertices);
            indices.Sort();
            foreach (int index in indices)
                if (index < 0 || index >= Vertices.Count) return false;
            string material = string.IsNullOrEmpty(materialName) ? "Default" : materialName;
            int firstFace = Triangles.Count;
            for (int index = 1; index < indices.Count - 1; index++)
                Triangles.Add(new ObjFile.Triangle(material,
                    new[] { indices[0], indices[index], indices[index + 1] }, new int[3], new int[3]));
            ClearSelection();
            for (int face = firstFace; face < Triangles.Count; face++) SelectedFaces.Add(face);
            return true;
        }

        public bool CreateQuadFromSelectedVertices(string materialName)
        {
            if (SelectedVertices.Count != 4) return false;
            List<int> indices = new List<int>(SelectedVertices);
            indices.Sort();
            foreach (int index in indices)
                if (index < 0 || index >= Vertices.Count) return false;
            string material = string.IsNullOrEmpty(materialName) ? "Default" : materialName;
            int firstFace = Triangles.Count;
            Triangles.Add(new ObjFile.Triangle(material,
                new[] { indices[0], indices[1], indices[2] }, new int[3], new int[3]));
            Triangles.Add(new ObjFile.Triangle(material,
                new[] { indices[0], indices[2], indices[3] }, new int[3], new int[3]));
            ClearSelection();
            SelectedFaces.Add(firstFace);
            SelectedFaces.Add(firstFace + 1);
            return true;
        }

        private void ExtrudeFaceInternal(int faceIndex, double distance)
        {
            ObjFile.Triangle face = Triangles[faceIndex];
            Vector3d a = Vertices[face.VertIndex[0]].ToVector3d();
            Vector3d b = Vertices[face.VertIndex[1]].ToVector3d();
            Vector3d c = Vertices[face.VertIndex[2]].ToVector3d();
            Vector3d normal = Vector3d.Cross(b - a, c - a);
            if (normal.LengthSquared < 0.000001) return;
            normal.Normalize();
            normal *= distance;

            int[] top = new int[3];
            for (int corner = 0; corner < 3; corner++)
            {
                ObjFile.Vertex source = Vertices[face.VertIndex[corner]];
                top[corner] = Vertices.Count;
                Vertices.Add(new ObjFile.Vertex(source.X + normal.X, source.Y + normal.Y, source.Z + normal.Z));
            }

            string material = face.MaterialName;
            Triangles.Add(new ObjFile.Triangle(material, top, new int[3], new int[3]));
            for (int corner = 0; corner < 3; corner++)
            {
                int next = (corner + 1) % 3;
                Triangles.Add(new ObjFile.Triangle(material,
                    new[] { face.VertIndex[corner], face.VertIndex[next], top[next] }, new int[3], new int[3]));
                Triangles.Add(new ObjFile.Triangle(material,
                    new[] { face.VertIndex[corner], top[next], top[corner] }, new int[3], new int[3]));
            }
        }

        public void InsetFace(int faceIndex, double amount)
        {
            if (faceIndex < 0 || faceIndex >= Triangles.Count) return;
            ObjFile.Triangle face = Triangles[faceIndex];
            Vector3d a = Vertices[face.VertIndex[0]].ToVector3d();
            Vector3d b = Vertices[face.VertIndex[1]].ToVector3d();
            Vector3d c = Vertices[face.VertIndex[2]].ToVector3d();
            Vector3d center = (a + b + c) / 3.0;
            int[] inner = new int[3];
            for (int corner = 0; corner < 3; corner++)
            {
                Vector3d point = Vertices[face.VertIndex[corner]].ToVector3d();
                Vector3d inset = Vector3d.Lerp(point, center, amount);
                inner[corner] = Vertices.Count;
                Vertices.Add(new ObjFile.Vertex(inset.X, inset.Y, inset.Z));
            }

            string material = face.MaterialName;
            List<ObjFile.Triangle> replacement = new List<ObjFile.Triangle>
            {
                new ObjFile.Triangle(material, inner, new int[3], new int[3])
            };
            for (int corner = 0; corner < 3; corner++)
            {
                int next = (corner + 1) % 3;
                replacement.Add(new ObjFile.Triangle(material,
                    new[] { face.VertIndex[corner], face.VertIndex[next], inner[next] }, new int[3], new int[3]));
                replacement.Add(new ObjFile.Triangle(material,
                    new[] { face.VertIndex[corner], inner[next], inner[corner] }, new int[3], new int[3]));
            }
            Triangles.RemoveAt(faceIndex);
            Triangles.InsertRange(faceIndex, replacement);
            ClearSelection();
            SelectedFaces.Add(faceIndex);
        }

        public void InsetSelectedFaces(double amount)
        {
            List<int> faces = new List<int>(SelectedFaces);
            faces.Sort((a, b) => b.CompareTo(a));
            ClearSelection();
            foreach (int faceIndex in faces)
                if (faceIndex >= 0 && faceIndex < Triangles.Count) InsetFace(faceIndex, amount);
            ClearSelection();
        }

        public void SubdivideSelectedFaces()
        {
            if (SelectedFaces.Count == 0) return;
            List<ObjFile.Triangle> replacement = new List<ObjFile.Triangle>();
            for (int faceIndex = 0; faceIndex < Triangles.Count; faceIndex++)
            {
                ObjFile.Triangle face = Triangles[faceIndex];
                if (!SelectedFaces.Contains(faceIndex) || face == null || face.VertIndex == null || face.VertIndex.Length < 3)
                {
                    replacement.Add(face);
                    continue;
                }

                ObjFile.Vertex a = Vertices[face.VertIndex[0]];
                ObjFile.Vertex b = Vertices[face.VertIndex[1]];
                ObjFile.Vertex c = Vertices[face.VertIndex[2]];
                int ab = AddMidpoint(a, b);
                int bc = AddMidpoint(b, c);
                int ca = AddMidpoint(c, a);
                string material = face.MaterialName;
                replacement.Add(new ObjFile.Triangle(material, new[] { face.VertIndex[0], ab, ca }, new int[3], new int[3]));
                replacement.Add(new ObjFile.Triangle(material, new[] { ab, face.VertIndex[1], bc }, new int[3], new int[3]));
                replacement.Add(new ObjFile.Triangle(material, new[] { ca, bc, face.VertIndex[2] }, new int[3], new int[3]));
                replacement.Add(new ObjFile.Triangle(material, new[] { ab, bc, ca }, new int[3], new int[3]));
            }
            Triangles.Clear();
            Triangles.AddRange(replacement);
            ClearSelection();
        }

        private int AddMidpoint(ObjFile.Vertex a, ObjFile.Vertex b)
        {
            int index = Vertices.Count;
            Vertices.Add(new ObjFile.Vertex((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5));
            return index;
        }

        public void DeleteSelection()
        {
            HashSet<int> facesToDelete = new HashSet<int>(SelectedFaces);
            foreach (int edge in SelectedEdges)
            {
                int start = EdgeStart(edge), end = EdgeEnd(edge);
                for (int face = 0; face < Triangles.Count; face++)
                {
                    int[] indices = Triangles[face].VertIndex;
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int a = indices[corner], b = indices[(corner + 1) % 3];
                        if (EdgeKey(a, b) == EdgeKey(start, end)) { facesToDelete.Add(face); break; }
                    }
                }
            }
            foreach (int vertex in SelectedVertices)
            {
                for (int face = 0; face < Triangles.Count; face++)
                    if (Array.IndexOf(Triangles[face].VertIndex, vertex) >= 0) facesToDelete.Add(face);
            }

            List<int> orderedFaces = new List<int>(facesToDelete);
            orderedFaces.Sort((a, b) => b.CompareTo(a));
            foreach (int face in orderedFaces)
                if (face >= 0 && face < Triangles.Count) Triangles.RemoveAt(face);

            HashSet<int> used = new HashSet<int>();
            foreach (ObjFile.Triangle triangle in Triangles)
                foreach (int vertex in triangle.VertIndex) used.Add(vertex);

            Dictionary<int, int> remap = new Dictionary<int, int>();
            List<ObjFile.Vertex> compacted = new List<ObjFile.Vertex>();
            for (int oldIndex = 0; oldIndex < Vertices.Count; oldIndex++)
            {
                if (!used.Contains(oldIndex)) continue;
                remap[oldIndex] = compacted.Count;
                compacted.Add(Vertices[oldIndex]);
            }
            foreach (ObjFile.Triangle triangle in Triangles)
                for (int corner = 0; corner < 3; corner++) triangle.VertIndex[corner] = remap[triangle.VertIndex[corner]];
            Vertices.Clear();
            Vertices.AddRange(compacted);
            ClearSelection();
        }

        public void DuplicateSelection()
        {
            List<int> faces = new List<int>(SelectedFaces);
            faces.Sort();
            ClearSelection();
            foreach (int faceIndex in faces)
            {
                if (faceIndex < 0 || faceIndex >= Triangles.Count) continue;
                ObjFile.Triangle source = Triangles[faceIndex];
                int[] copied = new int[3];
                for (int corner = 0; corner < 3; corner++)
                {
                    ObjFile.Vertex vertex = Vertices[source.VertIndex[corner]];
                    copied[corner] = Vertices.Count;
                    Vertices.Add(vertex.Clone());
                }
                Triangles.Add(new ObjFile.Triangle(source.MaterialName, copied, new int[3], new int[3]));
                SelectedFaces.Add(Triangles.Count - 1);
            }
        }

        public void FlipSelectedFaces()
        {
            foreach (int faceIndex in SelectedFaces)
            {
                if (faceIndex < 0 || faceIndex >= Triangles.Count) continue;
                int[] indices = Triangles[faceIndex].VertIndex;
                int swap = indices[1];
                indices[1] = indices[2];
                indices[2] = swap;
            }
        }

        public void MergeNearbyVertices(double distance)
        {
            double limitSquared = distance * distance;
            int[] remap = new int[Vertices.Count];
            List<ObjFile.Vertex> merged = new List<ObjFile.Vertex>();
            for (int sourceIndex = 0; sourceIndex < Vertices.Count; sourceIndex++)
            {
                ObjFile.Vertex source = Vertices[sourceIndex];
                int targetIndex = -1;
                for (int candidate = 0; candidate < merged.Count; candidate++)
                {
                    ObjFile.Vertex target = merged[candidate];
                    double dx = source.X - target.X, dy = source.Y - target.Y, dz = source.Z - target.Z;
                    if (dx * dx + dy * dy + dz * dz <= limitSquared)
                    {
                        targetIndex = candidate;
                        break;
                    }
                }
                if (targetIndex < 0)
                {
                    targetIndex = merged.Count;
                    merged.Add(source);
                }
                remap[sourceIndex] = targetIndex;
            }

            List<ObjFile.Triangle> filtered = new List<ObjFile.Triangle>();
            foreach (ObjFile.Triangle triangle in Triangles)
            {
                for (int corner = 0; corner < 3; corner++) triangle.VertIndex[corner] = remap[triangle.VertIndex[corner]];
                if (triangle.VertIndex[0] == triangle.VertIndex[1] ||
                    triangle.VertIndex[1] == triangle.VertIndex[2] ||
                    triangle.VertIndex[2] == triangle.VertIndex[0]) continue;
                filtered.Add(triangle);
            }
            Vertices.Clear();
            Vertices.AddRange(merged);
            Triangles.Clear();
            Triangles.AddRange(filtered);
            ClearSelection();
        }

        public bool MergeSelectedVertices()
        {
            if (SelectedVertices.Count < 2) return false;
            List<int> selected = new List<int>(SelectedVertices);
            selected.Sort();
            Vector3d center = Vector3d.Zero;
            int validCount = 0;
            foreach (int index in selected)
            {
                if (index < 0 || index >= Vertices.Count) continue;
                center += Vertices[index].ToVector3d();
                validCount++;
            }
            if (validCount < 2) return false;
            center /= validCount;
            int target = selected[0];
            Vertices[target].X = center.X;
            Vertices[target].Y = center.Y;
            Vertices[target].Z = center.Z;
            HashSet<int> remove = new HashSet<int>(selected);
            remove.Remove(target);
            foreach (ObjFile.Triangle triangle in Triangles)
                for (int corner = 0; corner < 3; corner++)
                    if (remove.Contains(triangle.VertIndex[corner])) triangle.VertIndex[corner] = target;
            for (int face = Triangles.Count - 1; face >= 0; face--)
            {
                int[] indices = Triangles[face].VertIndex;
                if (indices[0] == indices[1] || indices[1] == indices[2] || indices[2] == indices[0])
                    Triangles.RemoveAt(face);
            }
            CompactUnusedVertices();
            ClearSelection();
            return true;
        }

        private void CompactUnusedVertices()
        {
            HashSet<int> used = new HashSet<int>();
            foreach (ObjFile.Triangle triangle in Triangles)
                foreach (int index in triangle.VertIndex) used.Add(index);
            Dictionary<int, int> remap = new Dictionary<int, int>();
            List<ObjFile.Vertex> compacted = new List<ObjFile.Vertex>();
            for (int index = 0; index < Vertices.Count; index++)
            {
                if (!used.Contains(index)) continue;
                remap[index] = compacted.Count;
                compacted.Add(Vertices[index]);
            }
            foreach (ObjFile.Triangle triangle in Triangles)
                for (int corner = 0; corner < 3; corner++) triangle.VertIndex[corner] = remap[triangle.VertIndex[corner]];
            Vertices.Clear();
            Vertices.AddRange(compacted);
        }
    }
}
