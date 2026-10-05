using System;
using System.IO;
using System.Text;

namespace Tyrant.Framework.Core
{
    public sealed class TMeshException : Exception
    {
        public TMeshException(string message) : base(message) { }
    }

    /// <summary>One shape key at full weight: deltas for every vertex (Unity space).</summary>
    public sealed class TMeshShape
    {
        public string Name { get; set; } = "";
        public float[] PositionDeltas { get; set; } = Array.Empty<float>();
        public float[] NormalDeltas { get; set; } = Array.Empty<float>();
    }

    /// <summary>
    /// A replacement mesh ready for Unity, written by Tyrant and read by the framework. Bone indices follow the game
    /// renderer's bone order; bind poses are the game's (copied from the original mesh in game) unless the model has a rig
    /// edit, whose edited bind poses are stored (format 2; files without them stay format 1 for older frameworks).
    /// </summary>
    public sealed class TMesh
    {
        public const int FileVersion = 2;
        private const int MaxArray = 200_000_000;
        private static readonly byte[] Magic = { (byte)'T', (byte)'M', (byte)'S', (byte)'H' };

        public string Name { get; set; } = "";

        /// <summary>The .glb it was built from: "&lt;length&gt;|&lt;last write UTC ticks&gt;".</summary>
        public string SourceStamp { get; set; } = "";

        public int VertexCount { get; set; }
        public float[] Positions { get; set; } = Array.Empty<float>();
        public float[] Normals { get; set; } = Array.Empty<float>();
        public float[] Uv0 { get; set; } = Array.Empty<float>();
        public float[] Colors { get; set; } = Array.Empty<float>();
        public int[] BoneIndices { get; set; } = Array.Empty<int>();
        public float[] BoneWeights { get; set; } = Array.Empty<float>();
        public int BoneCount { get; set; }
        public int[] Indices { get; set; } = Array.Empty<int>();
        public int[] SubMeshStarts { get; set; } = Array.Empty<int>();
        public int[] SubMeshCounts { get; set; } = Array.Empty<int>();

        /// <summary>32-bit indices (more than 65,535 vertices).</summary>
        public bool Index32 { get; set; }

        public TMeshShape[] Shapes { get; set; } = Array.Empty<TMeshShape>();
        public float[] BoundsMin { get; set; } = new float[3];
        public float[] BoundsMax { get; set; } = new float[3];

        /// <summary>16 per bone, Unity's m[row, col] row by row; empty = the game's bind poses.</summary>
        public float[] BindPoses { get; set; } = Array.Empty<float>();

        public void Write(Stream stream)
        {
            using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            w.Write(Magic);
            w.Write(BindPoses.Length > 0 ? 2 : 1);
            w.Write(Name);
            w.Write(SourceStamp);
            w.Write(VertexCount);
            WriteFloats(w, Positions);
            WriteFloats(w, Normals);
            WriteFloats(w, Uv0);
            WriteFloats(w, Colors);
            WriteInts(w, BoneIndices);
            WriteFloats(w, BoneWeights);
            w.Write(BoneCount);
            WriteInts(w, Indices);
            WriteInts(w, SubMeshStarts);
            WriteInts(w, SubMeshCounts);
            w.Write(Index32);
            w.Write(Shapes.Length);
            foreach (var shape in Shapes)
            {
                w.Write(shape.Name);
                WriteFloats(w, shape.PositionDeltas);
                WriteFloats(w, shape.NormalDeltas);
            }
            WriteFloats(w, BoundsMin);
            WriteFloats(w, BoundsMax);
            if (BindPoses.Length > 0) WriteFloats(w, BindPoses);
        }

        public static TMesh Read(Stream stream)
        {
            try
            {
                using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
                var magic = r.ReadBytes(4);
                if (magic.Length < 4) throw new EndOfStreamException();
                if (magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
                    throw new TMeshException("not a Tyrant model file (.tmesh)");
                var version = r.ReadInt32();
                if (version > FileVersion) throw new TMeshException($"made by a newer Tyrant (format {version}; this framework reads {FileVersion})");
                var mesh = new TMesh { Name = r.ReadString(), SourceStamp = r.ReadString(), VertexCount = r.ReadInt32() };
                mesh.Positions = ReadFloats(r);
                mesh.Normals = ReadFloats(r);
                mesh.Uv0 = ReadFloats(r);
                mesh.Colors = ReadFloats(r);
                mesh.BoneIndices = ReadInts(r);
                mesh.BoneWeights = ReadFloats(r);
                mesh.BoneCount = r.ReadInt32();
                mesh.Indices = ReadInts(r);
                mesh.SubMeshStarts = ReadInts(r);
                mesh.SubMeshCounts = ReadInts(r);
                mesh.Index32 = r.ReadBoolean();
                var shapes = r.ReadInt32();
                if (shapes < 0 || shapes > 1024) throw new TMeshException("damaged (shape key count)");
                mesh.Shapes = new TMeshShape[shapes];
                for (var i = 0; i < shapes; i++)
                    mesh.Shapes[i] = new TMeshShape { Name = r.ReadString(), PositionDeltas = ReadFloats(r), NormalDeltas = ReadFloats(r) };
                mesh.BoundsMin = ReadFloats(r);
                mesh.BoundsMax = ReadFloats(r);
                if (version >= 2) mesh.BindPoses = ReadFloats(r);
                Validate(mesh);
                return mesh;
            }
            catch (EndOfStreamException)
            {
                throw new TMeshException("cut short (the file is incomplete)");
            }
        }

        private static void Validate(TMesh m)
        {
            var n = m.VertexCount;
            if (n <= 0 || m.Positions.Length != n * 3 || m.Normals.Length != n * 3 || (m.Uv0.Length != 0 && m.Uv0.Length != n * 2)
                || (m.Colors.Length != 0 && m.Colors.Length != n * 4) || (m.BoneIndices.Length != 0 && m.BoneIndices.Length != n * 4)
                || m.BoneWeights.Length != m.BoneIndices.Length || m.SubMeshStarts.Length != m.SubMeshCounts.Length)
                throw new TMeshException("damaged (array sizes do not match the vertex count)");
            foreach (var index in m.Indices)
                if (index < 0 || index >= n) throw new TMeshException("damaged (an index points past the vertices)");
            for (var s = 0; s < m.SubMeshStarts.Length; s++)
                if (m.SubMeshStarts[s] < 0 || m.SubMeshCounts[s] < 0 || m.SubMeshStarts[s] + m.SubMeshCounts[s] > m.Indices.Length)
                    throw new TMeshException("damaged (a part points past the indices)");
            if (m.BindPoses.Length != 0 && m.BindPoses.Length != m.BoneCount * 16)
                throw new TMeshException("damaged (bind poses do not match the bone count)");
            foreach (var shape in m.Shapes)
                if (shape.PositionDeltas.Length != n * 3 || shape.NormalDeltas.Length != n * 3) throw new TMeshException($"damaged (shape key {shape.Name})");
        }

        private static void WriteFloats(BinaryWriter w, float[] values)
        {
            w.Write(values.Length);
            foreach (var v in values) w.Write(v);
        }

        private static void WriteInts(BinaryWriter w, int[] values)
        {
            w.Write(values.Length);
            foreach (var v in values) w.Write(v);
        }

        private static float[] ReadFloats(BinaryReader r)
        {
            var count = r.ReadInt32();
            if (count < 0 || count > MaxArray) throw new TMeshException("damaged (array length)");
            var values = new float[count];
            for (var i = 0; i < count; i++) values[i] = r.ReadSingle();
            return values;
        }

        private static int[] ReadInts(BinaryReader r)
        {
            var count = r.ReadInt32();
            if (count < 0 || count > MaxArray) throw new TMeshException("damaged (array length)");
            var values = new int[count];
            for (var i = 0; i < count; i++) values[i] = r.ReadInt32();
            return values;
        }
    }

    /// <summary>Where a model's converted files sit: next to the user's .glb.</summary>
    public static class ModelFiles
    {
        public static string Lod(string glbFile, int lod) => Stem(glbFile) + ".lod" + lod + ".tmesh";

        public static string Report(string glbFile) => Stem(glbFile) + ".model.json";

        private static string Stem(string glbFile) =>
            glbFile.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ? glbFile.Substring(0, glbFile.Length - 4) : glbFile;
    }
}
