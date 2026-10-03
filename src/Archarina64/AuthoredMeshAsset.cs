using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace SharpOcarina
{
    public sealed class AuthoredMeshAssetDocument
    {
        public string Name;
        public bool BackfaceCulling;
        public bool AddToCollision = true;
        public int CollisionPolyType;
        public uint TintAlpha = 0xFFFFFFFF;
        public int TileS;
        public int TileT;
        public int PolyType;
        public bool AlphaMask;
        public bool Pixelated;
        public bool RenderLast;
        public bool IgnoreFog;
        public bool Metallic;
        public bool Decal;
        public bool Billboard;
        public bool TwoAxisBillboard;
        public bool EnvColor;
        public bool SmoothRGBAEdges;
        public bool VertexNormals;
        public bool ScaledNormals;
        public bool TexPointerPlus1;
        public bool ReverseLight;
        public bool Animated;
        public int AnimationBank = 8;
        public uint MultiTexAlpha = 0xFFFFFFFF;
        public string SourceBasePath;
        // These fields are shared with the modeling editor. Archarina does
        // not edit polygon topology or modifiers yet, but it must preserve
        // them when a shared asset is opened and its settings are saved.
        public double SourceSceneScale = 1.0;
        public bool HasSourceSceneScale;
        public bool HasMirrorModifier;
        public AuthoredMeshAssetMirrorModifier MirrorModifier;
        public List<AuthoredMeshAssetVertex> Vertices = new List<AuthoredMeshAssetVertex>();
        public List<AuthoredMeshAssetFace> Faces = new List<AuthoredMeshAssetFace>();
        public List<AuthoredMeshAssetPolygon> Polygons = new List<AuthoredMeshAssetPolygon>();
        public List<int> SuppressedEdges = new List<int>();
        public List<AuthoredMeshAssetMaterial> Materials = new List<AuthoredMeshAssetMaterial>();
        public List<AuthoredMeshAssetCollection> Collections = new List<AuthoredMeshAssetCollection>();
    }

    public sealed class AuthoredMeshAssetVertex
    {
        public double X;
        public double Y;
        public double Z;
        public bool HasColor;
        public double R = 1.0;
        public double G = 1.0;
        public double B = 1.0;
        public double A = 1.0;
    }

    public sealed class AuthoredMeshAssetFace
    {
        public int A;
        public int B;
        public int C;
        public string MaterialName;
        public bool HasUv;
        public double UA;
        public double VA;
        public double UB;
        public double VB;
        public double UC;
        public double VC;
    }

    public sealed class AuthoredMeshAssetPolygon
    {
        public string MaterialName;
        public int CollectionIndex = -1;
        public List<int> TriangleIndices = new List<int>();
        public List<AuthoredMeshAssetCorner> Corners = new List<AuthoredMeshAssetCorner>();
    }

    public sealed class AuthoredMeshAssetCorner
    {
        public int VertexIndex;
        public bool HasUv;
        public double U;
        public double V;
    }

    public sealed class AuthoredMeshAssetMaterial
    {
        public string Name;
        public float[] Ka;
        public float[] Kd;
        public float[] Ks;
        public float Tr;
        public int Illum;
        public string MapKa;
        public string MapKd;
        public string MapKs;
        public string MapD;
        public string MapBump;
        public string Tags;
        public string Tags1;
        public bool ForceRGBA;
        public string ForcedFormat;
    }

    public sealed class AuthoredMeshAssetMirrorModifier
    {
        public bool Enabled = true;
        public bool MirrorX;
        public bool MirrorY;
        public bool MirrorZ;
        public bool Merge;
        public bool Clipping;
        public double OriginX;
        public double OriginY;
        public double OriginZ;
        public double MergeDistance = 0.5;
    }

    public sealed class AuthoredMeshAssetCollection
    {
        public string Name;
        public bool Visible = true;
        public bool Selectable = true;
        public List<int> FaceIndices = new List<int>();
        public List<int> PolygonIndices = new List<int>();
    }

    internal static class AuthoredMeshAssetIO
    {
        public static void Save(string path, string name, MeshEditorData mesh, ObjFile model, ObjFile.Group sourceGroup)
        {
            string assetDirectory = Path.GetDirectoryName(Path.GetFullPath(path));
            string textureDirectory = Path.Combine(assetDirectory, SanitizeName(name) + ".textures");
            AuthoredMeshAssetDocument document = new AuthoredMeshAssetDocument
            {
                Name = name,
                BackfaceCulling = sourceGroup != null && sourceGroup.BackfaceCulling,
                TintAlpha = sourceGroup == null ? 0xFFFFFFFF : sourceGroup.TintAlpha,
                TileS = sourceGroup == null ? 0 : sourceGroup.TileS,
                TileT = sourceGroup == null ? 0 : sourceGroup.TileT,
                PolyType = sourceGroup == null ? 0 : sourceGroup.PolyType,
                AlphaMask = sourceGroup != null && sourceGroup.AlphaMask,
                Pixelated = sourceGroup != null && sourceGroup.Pixelated,
                RenderLast = sourceGroup != null && sourceGroup.RenderLast,
                IgnoreFog = sourceGroup != null && sourceGroup.IgnoreFog,
                Metallic = sourceGroup != null && sourceGroup.Metallic,
                Decal = sourceGroup != null && sourceGroup.Decal,
                Billboard = sourceGroup != null && sourceGroup.Billboard,
                TwoAxisBillboard = sourceGroup != null && sourceGroup.TwoAxisBillboard,
                EnvColor = sourceGroup != null && sourceGroup.EnvColor,
                SmoothRGBAEdges = sourceGroup != null && sourceGroup.SmoothRGBAEdges,
                VertexNormals = sourceGroup != null && sourceGroup.VertexNormals,
                ScaledNormals = sourceGroup != null && sourceGroup.ScaledNormals,
                TexPointerPlus1 = sourceGroup != null && sourceGroup.TexPointerPlus1,
                ReverseLight = sourceGroup != null && sourceGroup.ReverseLight,
                Animated = sourceGroup != null && sourceGroup.Animated,
                AnimationBank = sourceGroup == null ? 8 : sourceGroup.AnimationBank,
                MultiTexAlpha = sourceGroup == null ? 0xFFFFFFFF : sourceGroup.MultiTexAlpha,
                SourceBasePath = model == null ? "" : model.BasePath,
                SourceSceneScale = MainForm.CurrentScene == null ? 1.0 : MainForm.CurrentScene.Scale,
                HasSourceSceneScale = MainForm.CurrentScene != null
            };
            foreach (ObjFile.Vertex vertex in mesh.Vertices)
                document.Vertices.Add(new AuthoredMeshAssetVertex { X = vertex.X, Y = vertex.Y, Z = vertex.Z });
            foreach (ObjFile.Triangle triangle in mesh.Triangles)
            {
                if (triangle.VertIndex == null || triangle.VertIndex.Length < 3) continue;
                document.Faces.Add(new AuthoredMeshAssetFace
                {
                    A = triangle.VertIndex[0], B = triangle.VertIndex[1], C = triangle.VertIndex[2],
                    MaterialName = triangle.MaterialName
                });
            }
            if (model != null && model.Materials != null)
            {
                HashSet<string> usedMaterials = new HashSet<string>();
                foreach (AuthoredMeshAssetFace face in document.Faces)
                    if (!string.IsNullOrEmpty(face.MaterialName)) usedMaterials.Add(face.MaterialName);
                foreach (ObjFile.Material material in model.Materials)
                {
                    if (material == null || string.IsNullOrEmpty(material.Name) || !usedMaterials.Contains(material.Name)) continue;
                    document.Materials.Add(new AuthoredMeshAssetMaterial
                    {
                        Name = material.Name,
                        Ka = material.Ka,
                        Kd = material.Kd,
                        Ks = material.Ks,
                        Tr = material.Tr,
                        Illum = material.illum,
                        MapKa = PackageTexture(material.map_Ka, model.BasePath, assetDirectory, textureDirectory),
                        MapKd = PackageTexture(material.map_Kd, model.BasePath, assetDirectory, textureDirectory),
                        MapKs = PackageTexture(material.map_Ks, model.BasePath, assetDirectory, textureDirectory),
                        MapD = PackageTexture(material.map_d, model.BasePath, assetDirectory, textureDirectory),
                        MapBump = PackageTexture(material.map_bump, model.BasePath, assetDirectory, textureDirectory),
                        Tags = material.tags,
                        Tags1 = material.tags_1,
                        ForceRGBA = material.ForceRGBA,
                        ForcedFormat = material.ForcedFormat
                    });
                }
            }
            XmlSerializer serializer = new XmlSerializer(typeof(AuthoredMeshAssetDocument));
            SaveDocument(path, document, serializer);
        }

        public static void SaveDocument(string path, AuthoredMeshAssetDocument document)
        {
            SaveDocument(path, document, new XmlSerializer(typeof(AuthoredMeshAssetDocument)));
        }

        private static void SaveDocument(string path, AuthoredMeshAssetDocument document, XmlSerializer serializer)
        {
            using (FileStream stream = File.Create(path)) serializer.Serialize(stream, document);
        }

        private static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "AuthoredMesh";
            foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            return name;
        }

        private static string PackageTexture(string texturePath, string sourceBasePath, string assetDirectory, string textureDirectory)
        {
            if (string.IsNullOrEmpty(texturePath)) return texturePath;
            string source = texturePath;
            if (!Path.IsPathRooted(source) && !string.IsNullOrEmpty(sourceBasePath))
                source = Path.Combine(sourceBasePath, source);
            if (!File.Exists(source)) return texturePath;
            if (!Directory.Exists(textureDirectory)) Directory.CreateDirectory(textureDirectory);
            string fileName = Path.GetFileName(source);
            string destination = Path.Combine(textureDirectory, fileName);
            if (!File.Exists(destination) || File.GetLastWriteTimeUtc(source) > File.GetLastWriteTimeUtc(destination))
                File.Copy(source, destination, true);
            return Path.Combine(Path.GetFileName(textureDirectory), fileName);
        }

        public static MeshEditorData Load(string path)
        {
            MeshEditorData mesh;
            LoadDocument(path, out mesh);
            return mesh;
        }

        public static AuthoredMeshAssetDocument LoadDocument(string path, out MeshEditorData mesh)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(AuthoredMeshAssetDocument));
            AuthoredMeshAssetDocument document;
            using (FileStream stream = File.OpenRead(path)) document = (AuthoredMeshAssetDocument)serializer.Deserialize(stream);
            mesh = new MeshEditorData();
            if (document == null) return null;
            foreach (AuthoredMeshAssetVertex vertex in document.Vertices)
                mesh.Vertices.Add(new ObjFile.Vertex(vertex.X, vertex.Y, vertex.Z));
            foreach (AuthoredMeshAssetFace face in document.Faces)
                if (face.A >= 0 && face.A < mesh.Vertices.Count && face.B >= 0 && face.B < mesh.Vertices.Count && face.C >= 0 && face.C < mesh.Vertices.Count)
                    mesh.Triangles.Add(new ObjFile.Triangle(face.MaterialName, new[] { face.A, face.B, face.C }, new int[3], new int[3]));
            mesh.OriginalVertexCount = mesh.Vertices.Count;
            return document;
        }
    }
}
