using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Imports a Valve Hammer .vmf map (text) and rebuilds its brush geometry as Unity meshes with
/// colliders — so a bunker built in Hammer can be dropped straight into the game. Parses the VMF
/// block tree, turns every world/entity SOLID (a convex set of side planes) into a mesh, and maps
/// Source's Z-up right-handed inch space into the game's Y-up metre space.
///
/// Usage:  var spawn = VmfImporter.Import(File.ReadAllText(path), parent);
/// Materials (VMT/VTF) aren't available, so faces are flat-coloured by material-name heuristic.
/// </summary>
public static class VmfImporter
{
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    // 1 metre = 39.37 Hammer units (1 unit = 1 inch). Keeps corridors/players at real scale.
    public const float Scale = 1f / 39.37f;

    // ─────────────────────────────────────────────────────────────────────────
    //  VMF tree
    // ─────────────────────────────────────────────────────────────────────────
    public class Node
    {
        public string name;
        public readonly Dictionary<string, string> kv = new Dictionary<string, string>();
        public readonly List<Node> children = new List<Node>();
        public string Get(string key, string def = "") => kv.TryGetValue(key, out var v) ? v : def;
        public IEnumerable<Node> Where(string n) { foreach (var c in children) if (c.name == n) yield return c; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Parser — tokenizer + recursive descent over { } blocks and "key" "value" pairs
    // ─────────────────────────────────────────────────────────────────────────
    public static Node Parse(string text)
    {
        var toks = Tokenize(text);
        int i = 0;
        var root = new Node { name = "root" };
        while (i < toks.Count)
            ParseMember(root, toks, ref i);
        return root;
    }

    static void ParseMember(Node parent, List<string> toks, ref int i)
    {
        if (i >= toks.Count) return;
        string t = toks[i];
        if (t == "{" || t == "}") { i++; return; } // stray brace guard
        // A block: identifier followed by '{', OR a key followed by a value string.
        if (i + 1 < toks.Count && toks[i + 1] == "{")
        {
            var node = new Node { name = t };
            i += 2; // skip name + '{'
            while (i < toks.Count && toks[i] != "}")
                ParseMember(node, toks, ref i);
            if (i < toks.Count) i++; // skip '}'
            parent.children.Add(node);
        }
        else if (i + 1 < toks.Count && toks[i + 1] != "{" && toks[i + 1] != "}")
        {
            parent.kv[t] = toks[i + 1]; // "key" "value"
            i += 2;
        }
        else i++;
    }

    static List<string> Tokenize(string s)
    {
        var list = new List<string>();
        int i = 0, n = s.Length;
        while (i < n)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < n && s[i + 1] == '/') { while (i < n && s[i] != '\n') i++; continue; } // // comment
            if (c == '{' || c == '}') { list.Add(c.ToString()); i++; continue; }
            if (c == '"')
            {
                int j = i + 1; var sb = new System.Text.StringBuilder();
                while (j < n && s[j] != '"') { sb.Append(s[j]); j++; }
                list.Add(sb.ToString()); i = j + 1; continue;
            }
            // bare identifier (block names like world/solid/side/entity)
            int k = i;
            while (k < n && !char.IsWhiteSpace(s[k]) && s[k] != '{' && s[k] != '}' && s[k] != '"') k++;
            list.Add(s.Substring(i, k - i)); i = k;
        }
        return list;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Import — walk the tree, build every solid, return the player spawn point
    // ─────────────────────────────────────────────────────────────────────────
    public struct Result { public Vector3 spawn; public bool hasSpawn; public int brushes; public int tris; }

    public static Result Import(string vmfText, Transform parent)
    {
        var root = Parse(vmfText);
        var res = new Result();
        var combined = new Dictionary<Color32, MeshBuild>(); // batch faces by colour for fewer objects

        // World brushes.
        foreach (var world in root.Where("world"))
            foreach (var solid in world.Where("solid"))
                res.brushes += BuildSolid(solid, combined, ref res);

        // Entity brushes (func_detail, func_wall, brush entities) + point entities (spawn/lights).
        foreach (var ent in root.Where("entity"))
        {
            string cls = ent.Get("classname");
            foreach (var solid in ent.Where("solid"))
                res.brushes += BuildSolid(solid, combined, ref res);

            if (cls == "info_player_start" || cls == "info_player_deathmatch")
            {
                if (TryVec(ent.Get("origin"), out var o)) { res.spawn = ToUnity(o) + Vector3.up * 0.1f; res.hasSpawn = true; }
            }
            else if (cls == "light" || cls == "light_spot")
            {
                if (TryVec(ent.Get("origin"), out var o)) SpawnLight(ToUnity(o), ent.Get("_light"), parent);
            }
        }

        // Emit one GameObject per colour batch.
        foreach (var kvp in combined)
        {
            var go = new GameObject("VmfBrushes");
            if (parent != null) go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var mesh = kvp.Value.ToMesh();
            mf.sharedMesh = mesh;
            var mat = new Material(GameBootstrap.StdShader());
            Color col = (Color)kvp.Key;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
            mr.sharedMaterial = mat;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            res.tris += mesh.triangles.Length / 3;
        }
        return res;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Brush → convex polyhedron mesh (from its side planes)
    // ─────────────────────────────────────────────────────────────────────────
    struct Pl { public Vector3 n; public float d; public Color32 col; } // n·x = d, outward normal

    static int BuildSolid(Node solid, Dictionary<Color32, MeshBuild> batches, ref Result res)
    {
        var planes = new List<Pl>();
        Vector3 inside = Vector3.zero; int samples = 0;
        foreach (var side in solid.Where("side"))
        {
            if (!TryPlane(side.Get("plane"), out var a, out var b, out var c)) continue;
            Vector3 nrm = Vector3.Cross(c - a, b - a);
            if (nrm.sqrMagnitude < 1e-9f) continue;
            nrm.Normalize();
            planes.Add(new Pl { n = nrm, d = Vector3.Dot(nrm, a), col = MatColor(side.Get("material")) });
            inside += a + b + c; samples += 3;
        }
        if (planes.Count < 4) return 0;

        // Orient every plane's normal to point OUTWARD (away from the brush centre), so the
        // half-space test below is correct regardless of the source winding order.
        inside /= samples;
        for (int p = 0; p < planes.Count; p++)
        {
            var pl = planes[p];
            if (Vector3.Dot(pl.n, inside) > pl.d) { pl.n = -pl.n; pl.d = -pl.d; planes[p] = pl; }
        }

        // Candidate vertices: intersection of every plane triple that lies inside all half-spaces.
        var verts = new List<Vector3>();
        int P = planes.Count;
        for (int i = 0; i < P; i++)
            for (int j = i + 1; j < P; j++)
                for (int k = j + 1; k < P; k++)
                    if (Intersect3(planes[i], planes[j], planes[k], out var pt) && InsideAll(pt, planes))
                        verts.Add(pt);
        if (verts.Count < 4) return 0;

        // Per face: gather the verts on that plane, order them, fan-triangulate into its colour batch.
        foreach (var pl in planes)
        {
            var onFace = new List<Vector3>();
            foreach (var v in verts)
                if (Mathf.Abs(Vector3.Dot(pl.n, v) - pl.d) < 0.05f && !ContainsApprox(onFace, v))
                    onFace.Add(v);
            if (onFace.Count < 3) continue;
            SortFace(onFace, pl.n);

            if (!batches.TryGetValue(pl.col, out var mb)) { mb = new MeshBuild(); batches[pl.col] = mb; }
            Vector3 u0 = ToUnity(onFace[0]);
            for (int t = 1; t < onFace.Count - 1; t++)
                mb.AddTri(u0, ToUnity(onFace[t]), ToUnity(onFace[t + 1]), pl.n);
        }
        return 1;
    }

    static bool InsideAll(Vector3 p, List<Pl> planes)
    {
        foreach (var pl in planes) if (Vector3.Dot(pl.n, p) - pl.d > 0.05f) return false;
        return true;
    }

    static bool Intersect3(Pl a, Pl b, Pl c, out Vector3 p)
    {
        Vector3 bc = Vector3.Cross(b.n, c.n);
        float denom = Vector3.Dot(a.n, bc);
        if (Mathf.Abs(denom) < 1e-6f) { p = default; return false; }
        p = (a.d * bc + b.d * Vector3.Cross(c.n, a.n) + c.d * Vector3.Cross(a.n, b.n)) / denom;
        return true;
    }

    // Order face verts CCW around the plane normal (so a triangle fan is coherent).
    static void SortFace(List<Vector3> face, Vector3 n)
    {
        Vector3 centre = Vector3.zero; foreach (var v in face) centre += v; centre /= face.Count;
        Vector3 refDir = (face[0] - centre).normalized;
        Vector3 tangent = Vector3.Cross(n, refDir);
        face.Sort((x, y) =>
        {
            Vector3 dx = x - centre, dy = y - centre;
            float ax = Mathf.Atan2(Vector3.Dot(dx, tangent), Vector3.Dot(dx, refDir));
            float ay = Mathf.Atan2(Vector3.Dot(dy, tangent), Vector3.Dot(dy, refDir));
            return ax.CompareTo(ay);
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers: coordinate transform, parsing, colours, lights, mesh accumulation
    // ─────────────────────────────────────────────────────────────────────────

    // Source (X east, Y north, Z up, inches) → Unity (Y up, metres). Swap Y/Z + scale.
    public static Vector3 ToUnity(Vector3 v) => new Vector3(v.x, v.z, v.y) * Scale;

    static bool TryVec(string s, out Vector3 v)
    {
        v = default;
        if (string.IsNullOrEmpty(s)) return false;
        var p = s.Split(' ');
        if (p.Length < 3) return false;
        return float.TryParse(p[0], NumberStyles.Float, CI, out v.x)
            && float.TryParse(p[1], NumberStyles.Float, CI, out v.y)
            && float.TryParse(p[2], NumberStyles.Float, CI, out v.z);
    }

    // "plane" "(x y z) (x y z) (x y z)"
    static bool TryPlane(string s, out Vector3 a, out Vector3 b, out Vector3 c)
    {
        a = b = c = default;
        if (string.IsNullOrEmpty(s)) return false;
        var parts = s.Replace("(", " ").Replace(")", " ").Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 9) return false;
        float[] f = new float[9];
        for (int i = 0; i < 9; i++) if (!float.TryParse(parts[i], NumberStyles.Float, CI, out f[i])) return false;
        a = new Vector3(f[0], f[1], f[2]);
        b = new Vector3(f[3], f[4], f[5]);
        c = new Vector3(f[6], f[7], f[8]);
        return true;
    }

    static Color32 MatColor(string mat)
    {
        string m = (mat ?? "").ToUpperInvariant();
        if (m.Contains("METAL") || m.Contains("STEEL")) return new Color32(120, 124, 132, 255);
        if (m.Contains("CONCRETE") || m.Contains("WALL"))  return new Color32(140, 140, 138, 255);
        if (m.Contains("FLOOR") || m.Contains("TILE"))     return new Color32(110, 112, 116, 255);
        if (m.Contains("DIRT") || m.Contains("GROUND"))    return new Color32(96, 82, 60, 255);
        if (m.Contains("GLASS"))                            return new Color32(150, 190, 210, 255);
        if (m.Contains("NODRAW") || m.Contains("TOOLS"))    return new Color32(70, 70, 74, 255);
        return new Color32(128, 128, 128, 255);
    }

    static void SpawnLight(Vector3 pos, string lightStr, Transform parent)
    {
        var go = new GameObject("VmfLight");
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = 8f; l.intensity = 1.6f;
        l.color = new Color(1f, 0.95f, 0.85f);
        if (!string.IsNullOrEmpty(lightStr))
        {
            var p = lightStr.Split(' ');
            if (p.Length >= 3 && byte.TryParse(p[0], out var r) && byte.TryParse(p[1], out var g) && byte.TryParse(p[2], out var bl))
                l.color = new Color(r / 255f, g / 255f, bl / 255f);
        }
    }

    static bool ContainsApprox(List<Vector3> list, Vector3 v)
    {
        foreach (var x in list) if ((x - v).sqrMagnitude < 1e-4f) return true;
        return false;
    }

    class MeshBuild
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> nrm = new List<Vector3>();
        public readonly List<int> tri = new List<int>();
        public void AddTri(Vector3 a, Vector3 b, Vector3 c, Vector3 sourceNormal)
        {
            // Face outward: Source normal in Unity space (swap y/z, no scale).
            Vector3 un = new Vector3(sourceNormal.x, sourceNormal.z, sourceNormal.y).normalized;
            Vector3 geo = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(geo, un) < 0f) { var t = b; b = c; c = t; } // keep winding facing outward
            int i0 = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            nrm.Add(un); nrm.Add(un); nrm.Add(un);
            tri.Add(i0); tri.Add(i0 + 1); tri.Add(i0 + 2);
        }
        public Mesh ToMesh()
        {
            var m = new Mesh();
            m.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.SetVertices(v); m.SetNormals(nrm); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
