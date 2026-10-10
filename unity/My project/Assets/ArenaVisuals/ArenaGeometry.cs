using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural meshes for the arena, built for real lighting rather than flat colour: every hard
/// edge is chamfered so it catches a thin highlight, and rings are true tubes rather than lines.
/// </summary>
public static class ArenaGeometry
{
    static readonly float Phi = (1f + Mathf.Sqrt(5f)) / 2f;

    static readonly int[] IcosahedronFaces =
    {
        0, 11, 5,   0, 5, 1,    0, 1, 7,    0, 7, 10,   0, 10, 11,
        1, 5, 9,    5, 11, 4,   11, 10, 2,  10, 7, 6,   7, 1, 8,
        3, 9, 4,    3, 4, 2,    3, 2, 6,    3, 6, 8,    3, 8, 9,
        4, 9, 5,    2, 4, 11,   6, 2, 10,   8, 6, 7,    9, 8, 1
    };

    static Vector3[] IcosahedronVertices() => new[]
    {
        new Vector3(-1f,  Phi, 0f).normalized, new Vector3( 1f,  Phi, 0f).normalized,
        new Vector3(-1f, -Phi, 0f).normalized, new Vector3( 1f, -Phi, 0f).normalized,
        new Vector3(0f, -1f,  Phi).normalized, new Vector3(0f,  1f,  Phi).normalized,
        new Vector3(0f, -1f, -Phi).normalized, new Vector3(0f,  1f, -Phi).normalized,
        new Vector3( Phi, 0f, -1f).normalized, new Vector3( Phi, 0f,  1f).normalized,
        new Vector3(-Phi, 0f, -1f).normalized, new Vector3(-Phi, 0f,  1f).normalized
    };

    /// <summary>Collects flat-shaded triangles, each wound to face a given outward direction.</summary>
    class FlatBuilder
    {
        readonly List<Vector3> _positions = new List<Vector3>();
        readonly List<int>[] _submeshes;

        public FlatBuilder(int submeshCount = 1)
        {
            _submeshes = new List<int>[submeshCount];
            for (int i = 0; i < submeshCount; i++) _submeshes[i] = new List<int>();
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, int submesh = 0)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
            {
                Vector3 swap = b;
                b = c;
                c = swap;
            }
            int start = _positions.Count;
            _positions.Add(a);
            _positions.Add(b);
            _positions.Add(c);
            _submeshes[submesh].Add(start);
            _submeshes[submesh].Add(start + 1);
            _submeshes[submesh].Add(start + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, int submesh = 0)
        {
            Tri(a, b, c, outward, submesh);
            Tri(a, c, d, outward, submesh);
        }

        public Mesh Build(string name, Vector3 scale)
        {
            var mesh = new Mesh { name = name };
            var scaled = new Vector3[_positions.Count];
            for (int i = 0; i < scaled.Length; i++) scaled[i] = Vector3.Scale(_positions[i], scale);
            mesh.vertices = scaled;
            mesh.subMeshCount = _submeshes.Length;
            for (int i = 0; i < _submeshes.Length; i++) mesh.SetTriangles(_submeshes[i], i);
            mesh.RecalculateNormals(); // every vertex belongs to one triangle, so normals stay flat
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// An icosahedron with every edge and corner chamfered: twenty faces, thirty narrow bevels and
    /// twelve small pentagons, so the silhouette reads as a machined solid. Radius is to the corners.
    /// </summary>
    public static Mesh ChamferedIcosahedron(float radius, float heightScale, float chamfer = 0.14f)
    {
        Vector3[] corners = IcosahedronVertices();
        int faceCount = IcosahedronFaces.Length / 3;

        // Wind every source face outward first, so face-local corner order is consistent
        var faces = new int[faceCount, 3];
        var faceNormals = new Vector3[faceCount];
        var faceCentres = new Vector3[faceCount];
        for (int f = 0; f < faceCount; f++)
        {
            int a = IcosahedronFaces[f * 3], b = IcosahedronFaces[f * 3 + 1], c = IcosahedronFaces[f * 3 + 2];
            Vector3 normal = Vector3.Cross(corners[b] - corners[a], corners[c] - corners[a]);
            if (Vector3.Dot(normal, corners[a]) < 0f)
            {
                int swap = b;
                b = c;
                c = swap;
            }
            faces[f, 0] = a;
            faces[f, 1] = b;
            faces[f, 2] = c;
            faceCentres[f] = (corners[a] + corners[b] + corners[c]) / 3f;
            faceNormals[f] = faceCentres[f].normalized;
        }

        Vector3 Inset(int face, int corner) =>
            faceCentres[face] + (corners[corner] - faceCentres[face]) * (1f - chamfer);

        var builder = new FlatBuilder();

        // The faces, shrunk toward their centres
        for (int f = 0; f < faceCount; f++)
            builder.Tri(Inset(f, faces[f, 0]), Inset(f, faces[f, 1]), Inset(f, faces[f, 2]), faceNormals[f]);

        // A bevel strip along every edge, bridging the two faces that share it
        var edgeFaces = new Dictionary<long, List<int>>();
        var cornerFaces = new List<int>[corners.Length];
        for (int i = 0; i < corners.Length; i++) cornerFaces[i] = new List<int>();
        for (int f = 0; f < faceCount; f++)
        {
            for (int k = 0; k < 3; k++)
            {
                int a = faces[f, k], b = faces[f, (k + 1) % 3];
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (!edgeFaces.TryGetValue(key, out List<int> list)) edgeFaces[key] = list = new List<int>();
                list.Add(f);
                cornerFaces[faces[f, k]].Add(f);
            }
        }
        foreach (var pair in edgeFaces)
        {
            if (pair.Value.Count != 2) continue;
            int a = (int)(pair.Key >> 32), b = (int)(pair.Key & 0xffffffff);
            int f1 = pair.Value[0], f2 = pair.Value[1];
            Vector3 outward = faceNormals[f1] + faceNormals[f2];
            builder.Quad(Inset(f1, a), Inset(f1, b), Inset(f2, b), Inset(f2, a), outward);
        }

        // A small pentagon at every corner, where five bevels meet
        for (int v = 0; v < corners.Length; v++)
        {
            Vector3 axis = corners[v];
            Vector3 tangent = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 bitangent = Vector3.Cross(axis, tangent);

            var ring = new List<Vector3>();
            foreach (int f in cornerFaces[v]) ring.Add(Inset(f, v));
            Vector3 centre = Vector3.zero;
            foreach (Vector3 p in ring) centre += p;
            centre /= ring.Count;
            ring.Sort((p, q) =>
                Mathf.Atan2(Vector3.Dot(p - centre, bitangent), Vector3.Dot(p - centre, tangent))
                    .CompareTo(Mathf.Atan2(Vector3.Dot(q - centre, bitangent), Vector3.Dot(q - centre, tangent))));

            for (int i = 0; i < ring.Count; i++)
                builder.Tri(centre, ring[i], ring[(i + 1) % ring.Count], axis);
        }

        return builder.Build("Arena Chamfered Icosahedron", new Vector3(radius, radius * heightScale, radius));
    }

    /// <summary>
    /// A hexagonal prism with chamfered top and bottom edges, a corner pointing along +X (so one
    /// flat side faces -Z), and an optional raised band around its middle on a second submesh.
    /// </summary>
    public static Mesh BevelledHex(float radius, float height, float bevel, float bandHeight = 0f, float bandDepth = 0f)
    {
        bool hasBand = bandHeight > 0f && bandDepth > 0f;
        var builder = new FlatBuilder(hasBand ? 2 : 1);
        float half = height * 0.5f;

        Vector3 Corner(int i, float r, float y)
        {
            float angle = (i % 6) * Mathf.PI / 3f;
            return new Vector3(Mathf.Cos(angle) * r, y, Mathf.Sin(angle) * r);
        }

        Vector3 SideNormal(int i)
        {
            float angle = (i + 0.5f) * Mathf.PI / 3f;
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        Vector3 top = new Vector3(0f, half, 0f), bottom = new Vector3(0f, -half, 0f);
        float inner = radius - bevel;

        for (int i = 0; i < 6; i++)
        {
            int n = i + 1;
            Vector3 side = SideNormal(i);

            builder.Tri(top, Corner(i, inner, half), Corner(n, inner, half), Vector3.up);
            builder.Quad(Corner(i, inner, half), Corner(n, inner, half), Corner(n, radius, half - bevel), Corner(i, radius, half - bevel), side + Vector3.up);
            builder.Quad(Corner(i, radius, half - bevel), Corner(n, radius, half - bevel), Corner(n, radius, -half + bevel), Corner(i, radius, -half + bevel), side);
            builder.Quad(Corner(i, radius, -half + bevel), Corner(n, radius, -half + bevel), Corner(n, inner, -half), Corner(i, inner, -half), side + Vector3.down);
            builder.Tri(bottom, Corner(i, inner, -half), Corner(n, inner, -half), Vector3.down);

            if (hasBand)
            {
                float outer = radius + bandDepth, bandHalf = bandHeight * 0.5f;
                builder.Quad(Corner(i, outer, bandHalf), Corner(n, outer, bandHalf), Corner(n, outer, -bandHalf), Corner(i, outer, -bandHalf), side, 1);
                builder.Quad(Corner(i, radius, bandHalf), Corner(n, radius, bandHalf), Corner(n, outer, bandHalf), Corner(i, outer, bandHalf), Vector3.up, 1);
                builder.Quad(Corner(i, radius, -bandHalf), Corner(n, radius, -bandHalf), Corner(n, outer, -bandHalf), Corner(i, outer, -bandHalf), Vector3.down, 1);
            }
        }

        return builder.Build("Arena Bevelled Hex", Vector3.one);
    }

    /// <summary>
    /// A smooth tube bent into a ring in the XZ plane. With arc below 1 it becomes an open arc
    /// starting at +Z, with rounded-off flat caps at both ends.
    /// </summary>
    public static Mesh Torus(float majorRadius, float minorRadius, int majorSegments = 96, int minorSegments = 12, float arc = 1f)
    {
        arc = Mathf.Clamp(arc, 0.002f, 1f);
        bool closed = arc >= 0.999f;
        int rings = closed ? majorSegments + 1 : Mathf.Max(2, Mathf.CeilToInt(majorSegments * arc) + 1);

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();

        for (int i = 0; i < rings; i++)
        {
            float u = (closed ? i / (float)majorSegments : i / (float)(rings - 1) * arc) * Mathf.PI * 2f;
            Vector3 radial = new Vector3(Mathf.Sin(u), 0f, Mathf.Cos(u));
            for (int j = 0; j <= minorSegments; j++)
            {
                float v = j / (float)minorSegments * Mathf.PI * 2f;
                Vector3 normal = radial * Mathf.Cos(v) + Vector3.up * Mathf.Sin(v);
                positions.Add(radial * majorRadius + normal * minorRadius);
                normals.Add(normal);
            }
        }

        int stride = minorSegments + 1;
        for (int i = 0; i < rings - 1; i++)
        {
            for (int j = 0; j < minorSegments; j++)
            {
                int a = i * stride + j, b = a + 1, c = a + stride, d = c + 1;
                AddOriented(triangles, positions, normals, a, c, d);
                AddOriented(triangles, positions, normals, a, d, b);
            }
        }

        if (!closed)
        {
            AddCap(positions, normals, triangles, 0, stride, minorSegments, majorRadius, 0f, -1f);
            AddCap(positions, normals, triangles, (rings - 1) * stride, stride, minorSegments, majorRadius, arc * Mathf.PI * 2f, 1f);
        }

        var mesh = new Mesh { name = closed ? "Arena Torus" : "Arena Arc" };
        mesh.SetVertices(positions);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static void AddOriented(List<int> triangles, List<Vector3> positions, List<Vector3> normals, int a, int b, int c)
    {
        Vector3 face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
        if (Vector3.Dot(face, normals[a] + normals[b] + normals[c]) < 0f)
        {
            int swap = b;
            b = c;
            c = swap;
        }
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
    }

    static void AddCap(List<Vector3> positions, List<Vector3> normals, List<int> triangles,
                       int ringStart, int stride, int minorSegments, float majorRadius, float u, float direction)
    {
        // Direction of travel along the ring at angle u, pointing out of the open end
        Vector3 tangent = new Vector3(Mathf.Cos(u), 0f, -Mathf.Sin(u)) * direction;
        Vector3 centre = new Vector3(Mathf.Sin(u), 0f, Mathf.Cos(u)) * majorRadius;

        int centreIndex = positions.Count;
        positions.Add(centre);
        normals.Add(tangent);
        int first = positions.Count;
        for (int j = 0; j <= minorSegments; j++)
        {
            positions.Add(positions[ringStart + j]);
            normals.Add(tangent);
        }
        for (int j = 0; j < minorSegments; j++)
            AddOriented(triangles, positions, normals, centreIndex, first + j, first + j + 1);
    }

    /// <summary>
    /// Accumulates smooth tubes swept along polylines (with parallel-transport frames, so they
    /// never twist) into one mesh. Used for tree branches and message pipes.
    /// </summary>
    public class TubeBuilder
    {
        readonly List<Vector3> _positions = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<int> _triangles = new List<int>();

        public int VertexCount => _positions.Count;

        public void Clear()
        {
            _positions.Clear();
            _normals.Clear();
            _triangles.Clear();
        }

        /// <summary>Adds a tube through the points, tapering from startRadius to endRadius.</summary>
        public void Add(IList<Vector3> points, float startRadius, float endRadius, int sides = 8, bool caps = true)
        {
            int count = points.Count;
            if (count < 2) return;

            Vector3 firstTangent = (points[1] - points[0]).normalized;
            Vector3 frame = Vector3.Cross(firstTangent, Mathf.Abs(firstTangent.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            int ringStart = _positions.Count;
            int stride = sides + 1;

            for (int i = 0; i < count; i++)
            {
                Vector3 tangent = i == 0 ? points[1] - points[0]
                                : i == count - 1 ? points[count - 1] - points[count - 2]
                                : points[i + 1] - points[i - 1];
                tangent = tangent.normalized;

                // Carry the frame along the curve, removing any part that now points along it
                Vector3 transported = frame - tangent * Vector3.Dot(frame, tangent);
                if (transported.sqrMagnitude > 1e-8f) frame = transported.normalized;
                Vector3 binormal = Vector3.Cross(tangent, frame);

                float radius = Mathf.Lerp(startRadius, endRadius, i / (float)(count - 1));
                for (int j = 0; j <= sides; j++)
                {
                    float angle = j / (float)sides * Mathf.PI * 2f;
                    Vector3 normal = frame * Mathf.Cos(angle) + binormal * Mathf.Sin(angle);
                    _positions.Add(points[i] + normal * radius);
                    _normals.Add(normal);
                }
            }

            for (int i = 0; i < count - 1; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    int a = ringStart + i * stride + j, b = a + 1, c = a + stride, d = c + 1;
                    AddOriented(_triangles, _positions, _normals, a, c, d);
                    AddOriented(_triangles, _positions, _normals, a, d, b);
                }
            }

            if (!caps) return;
            AddFlatCap(ringStart, stride, sides, points[0], -(points[1] - points[0]).normalized);
            AddFlatCap(ringStart + (count - 1) * stride, stride, sides, points[count - 1], (points[count - 1] - points[count - 2]).normalized);
        }

        void AddFlatCap(int ringStart, int stride, int sides, Vector3 centre, Vector3 normal)
        {
            int centreIndex = _positions.Count;
            _positions.Add(centre);
            _normals.Add(normal);
            int first = _positions.Count;
            for (int j = 0; j <= sides; j++)
            {
                _positions.Add(_positions[ringStart + j]);
                _normals.Add(normal);
            }
            for (int j = 0; j < sides; j++)
                AddOriented(_triangles, _positions, _normals, centreIndex, first + j, first + j + 1);
        }

        /// <summary>Writes the tubes into a mesh, reusing it if one is given.</summary>
        public Mesh Build(string name, Mesh target = null)
        {
            Mesh mesh = target != null ? target : new Mesh { name = name };
            mesh.Clear();
            mesh.indexFormat = _positions.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(_positions);
            mesh.SetNormals(_normals);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>A smooth-sided cylinder along Y, centred on the origin, with flat caps.</summary>
    public static Mesh Cylinder(float radius, float height, int segments = 24)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        float half = height * 0.5f;

        for (int i = 0; i <= segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            positions.Add(radial * radius + Vector3.up * half);
            normals.Add(radial);
            positions.Add(radial * radius - Vector3.up * half);
            normals.Add(radial);
        }
        for (int i = 0; i < segments; i++)
        {
            int a = i * 2;
            AddOriented(triangles, positions, normals, a, a + 1, a + 3);
            AddOriented(triangles, positions, normals, a, a + 3, a + 2);
        }

        foreach (float y in new[] { half, -half })
        {
            Vector3 normal = new Vector3(0f, Mathf.Sign(y), 0f);
            int centre = positions.Count;
            positions.Add(new Vector3(0f, y, 0f));
            normals.Add(normal);
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                positions.Add(new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius));
                normals.Add(normal);
            }
            for (int i = 0; i < segments; i++)
                AddOriented(triangles, positions, normals, centre, centre + 1 + i, centre + 2 + i);
        }

        var mesh = new Mesh { name = "Arena Cylinder" };
        mesh.SetVertices(positions);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
