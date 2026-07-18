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
    public struct Result { public Vector3 spawn; public bool hasSpawn; public float spawnYaw; public int brushes; public int tris; public int entities; }

    public static Result Import(string vmfText, Transform parent)
    {
        var root = Parse(vmfText);
        var res = new Result();
        var combined = new Dictionary<Color32, MeshBuild>(); // batch faces by colour for fewer objects

        // World brushes.
        foreach (var world in root.Where("world"))
            foreach (var solid in world.Where("solid"))
                res.brushes += BuildSolid(solid, combined, ref res);

        // EVERY entity — brush geometry (func_detail/func_wall/…) plus a VmfEntity carrier so the
        // game knows the entity, its key/values and its I/O outputs (foundation for map scripting).
        foreach (var ent in root.Where("entity"))
        {
            string cls = ent.Get("classname");
            bool movable = IsMovableEntity(cls);
            var entBatches = movable ? new Dictionary<Color32, MeshBuild>() : combined; // движимые → в СВОЙ меш
            if (!IsNonRenderEntity(cls))
                foreach (var solid in ent.Where("solid"))
                    res.brushes += BuildSolid(solid, entBatches, ref res);

            Vector3 epos = TryVec(ent.Get("origin"), out var eo) ? ToUnity(eo) : ToUnity(EntityCentroid(ent));

            var ego = new GameObject("ent:" + cls);
            if (parent != null) ego.transform.SetParent(parent, false);
            ego.transform.position = epos;
            var ve = ego.AddComponent<VmfEntity>();
            ve.classname = cls;
            foreach (var kvp in ent.kv) ve.kv[kvp.Key] = kvp.Value;
            foreach (var conn in ent.Where("connections"))
                foreach (var kvp in conn.kv) AddConnection(ve, kvp.Key, kvp.Value);
            if (EntityAABBUnity(ent, out var _bc, out var _bh))   // AABB зоны (Unity) для бокс-теста триггеров
            { ve.boundsCenter = _bc; ve.boundsHalf = _bh; ve.boundsRadius = _bh.magnitude; }
            res.entities++;

            // Движимые брашевые энтити (дверь/кнопка/movelinear) — СВОЙ меш+коллайдер под ego,
            // чтобы рантайм (VmfRuntime) их двигал независимо от статичной карты.
            if (movable && entBatches.Count > 0)
            {
                foreach (var kvp in entBatches)
                {
                    var mgo = new GameObject("brush");
                    mgo.transform.SetParent(ego.transform, true);
                    var mmesh = kvp.Value.ToMesh();
                    mgo.AddComponent<MeshFilter>().sharedMesh = mmesh;
                    var mmr = mgo.AddComponent<MeshRenderer>();
                    var mmat = new Material(GameBootstrap.StdShader());
                    Color mc = (Color)kvp.Key;
                    if (mmat.HasProperty("_BaseColor")) mmat.SetColor("_BaseColor", mc);
                    mmat.color = mc;
                    mmr.material = mmat;
                    mgo.AddComponent<MeshCollider>().sharedMesh = mmesh;
                    res.tris += mmesh.triangles.Length / 3;
                }
                ve.moveRoot = ego.transform;
            }

            // Well-known entities also get a real in-game effect on top of the carrier.
            if (cls == "info_player_start" || cls == "info_player_deathmatch")
            {
                res.spawn = epos + Vector3.up * 0.1f; res.hasSpawn = true;
                // Угол взгляда: Source "angles"="pitch yaw roll". Source yaw (вокруг Z-up) → Unity yaw
                // (вокруг Y-up): forward +Y(Source)=+Z(Unity) → Unity_yaw = 90 - source_yaw.
                var ap = (ent.Get("angles") ?? "").Split(' ');
                if (ap.Length >= 2 && float.TryParse(ap[1], NumberStyles.Float, CI, out float syaw))
                    res.spawnYaw = 90f - syaw;
            }
            else if (cls == "light" || cls == "light_spot" || cls == "light_environment")
            {
                SpawnLight(epos, ent.Get("_light"), ego.transform);
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
            // РЕШЁТКА: цвет (88,92,98) от MatColor → клеим процедурную сетку-текстуру.
            if (kvp.Key.r == 88 && kvp.Key.g == 92 && kvp.Key.b == 98)
            {
                var gt = GrateTex();
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", gt);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", gt);
                mat.mainTexture = gt;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
                // ПРОЗРАЧНОСТЬ: альфа-клип (дырки альфа 0 вырезаются) + двусторонний рендер.
                if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f);
                if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.5f);
                mat.EnableKeyword("_ALPHATEST_ON");
                if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f); // видно сетку с обеих сторон
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
            mr.sharedMaterial = mat;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            res.tris += mesh.triangles.Length / 3;
        }
        return res;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Brush → convex polyhedron mesh (from its side planes)
    // ─────────────────────────────────────────────────────────────────────────
    struct Pl { public Vector3 n; public float d; public Color32 col; public Node side; } // n·x = d, outward normal

    static int BuildSolid(Node solid, Dictionary<Color32, MeshBuild> batches, ref Result res)
    {
        var planes = new List<Pl>();
        Vector3 inside = Vector3.zero; int samples = 0;
        bool anyDisp = false;
        foreach (var side in solid.Where("side"))
        {
            if (!TryPlane(side.Get("plane"), out var a, out var b, out var c)) continue;
            Vector3 nrm = Vector3.Cross(c - a, b - a);
            if (nrm.sqrMagnitude < 1e-9f) continue;
            nrm.Normalize();
            if (Disp(side) != null) anyDisp = true;
            planes.Add(new Pl { n = nrm, d = Vector3.Dot(nrm, a), col = MatColor(side.Get("material")), side = side });
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

        // Per face: gather the verts on that plane, order them.
        foreach (var pl in planes)
        {
            var onFace = new List<Vector3>();
            foreach (var v in verts)
                if (Mathf.Abs(Vector3.Dot(pl.n, v) - pl.d) < 0.05f && !ContainsApprox(onFace, v))
                    onFace.Add(v);
            if (onFace.Count < 3) continue;
            SortFace(onFace, pl.n);

            // A brush with ANY displacement renders ONLY its displaced faces (the flat brush is
            // "hollowed") — so skip flat faces when anyDisp, and build a disp surface where present.
            if (anyDisp)
            {
                var disp = Disp(pl.side);
                if (disp != null && onFace.Count == 4) BuildDisplacement(disp, pl, onFace, batches);
                continue;
            }

            if (SkipMaterial(pl.side.Get("material"))) continue; // tool/sky/trigger/nodraw → don't render

            // РЕШЁТКА: строим СЕТКУ ИЗ БРУСЬЕВ с настоящими дырками (сквозь видно), а не сплошную грань.
            if (IsGrateMat(pl.side.Get("material"))) { BuildGrateFace(onFace, pl.n, batches); continue; }

            if (!batches.TryGetValue(pl.col, out var mb)) { mb = new MeshBuild(); batches[pl.col] = mb; }
            Vector3 u0 = ToUnity(onFace[0]);
            for (int t = 1; t < onFace.Count - 1; t++)
                mb.AddTri(u0, ToUnity(onFace[t]), ToUnity(onFace[t + 1]), pl.n);
        }
        return 1;
    }

    static bool IsGrateMat(string mat)
    {
        var m = (mat ?? "").ToUpperInvariant();
        return m.Contains("GRATE") || m.Contains("GRID");
    }

    // Решётка как ГЕОМЕТРИЯ: 4-угольная грань → сетка из брусьев (тонкие полосы по линиям
    // сетки), между ними РЕАЛЬНЫЕ дырки (пустота) — сквозь видно, без альфа-шейдера. 2026-07-13.
    static void BuildGrateFace(List<Vector3> face, Vector3 srcN, Dictionary<Color32, MeshBuild> batches)
    {
        Color32 barCol = new Color32(120, 126, 134, 255); // сплошной металл прутьев
        if (!batches.TryGetValue(barCol, out var mb)) { mb = new MeshBuild(); batches[barCol] = mb; }
        if (face.Count != 4) // не квад → просто солид-заливка (fallback)
        {
            Vector3 u0 = ToUnity(face[0]);
            for (int t = 1; t < face.Count - 1; t++) mb.AddTri(u0, ToUnity(face[t]), ToUnity(face[t + 1]), srcN);
            return;
        }
        Vector3 P0 = face[0], P1 = face[1], P2 = face[2], P3 = face[3];
        System.Func<float, float, Vector3> B = (u, v) =>
            Vector3.Lerp(Vector3.Lerp(P0, P1, u), Vector3.Lerp(P3, P2, u), v);
        int N = 4; float w = 0.06f; // 4 ячейки, толщина прутка ~6%
        for (int i = 0; i <= N; i++) // вертикальные прутья
        {
            float uc = (float)i / N, ua = Mathf.Clamp01(uc - w), ub = Mathf.Clamp01(uc + w);
            GrateQuad(mb, B(ua, 0f), B(ub, 0f), B(ub, 1f), B(ua, 1f), srcN);
        }
        for (int j = 0; j <= N; j++) // горизонтальные прутья
        {
            float vc = (float)j / N, va = Mathf.Clamp01(vc - w), vb = Mathf.Clamp01(vc + w);
            GrateQuad(mb, B(0f, va), B(1f, va), B(1f, vb), B(0f, vb), srcN);
        }
    }
    static void GrateQuad(MeshBuild mb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 srcN)
    {
        mb.AddTri(ToUnity(a), ToUnity(b), ToUnity(c), srcN);
        mb.AddTri(ToUnity(a), ToUnity(c), ToUnity(d), srcN);
    }

    static Node Disp(Node side)
    {
        if (side == null) return null;
        foreach (var ch in side.children) if (ch.name == "dispinfo") return ch;
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Displacement surface: subdivide a quad face into a (2^power+1) grid and push each
    //  grid vertex out along its stored normal by its stored distance (+ offset). This is how
    //  Hammer builds terrain / uneven floors.
    // ─────────────────────────────────────────────────────────────────────────
    static void BuildDisplacement(Node disp, Pl pl, List<Vector3> face4, Dictionary<Color32, MeshBuild> batches)
    {
        if (!int.TryParse(disp.Get("power"), out int power)) return;
        power = Mathf.Clamp(power, 1, 4);
        int size = (1 << power) + 1;              // 3, 5, 9 or 17 verts per edge

        if (!TryVec(disp.Get("startposition"), out Vector3 start)) start = face4[0];

        // Order the 4 corners so c0 = the one nearest 'startposition', keeping winding.
        int s = 0; float bestSq = float.MaxValue;
        for (int i = 0; i < 4; i++) { float dd = (face4[i] - start).sqrMagnitude; if (dd < bestSq) { bestSq = dd; s = i; } }
        Vector3 c0 = face4[s], c1 = face4[(s + 1) % 4], c2 = face4[(s + 2) % 4], c3 = face4[(s + 3) % 4];

        var normals = disp.Where("normals").GetEnumerator();  normals.MoveNext();
        var distances = disp.Where("distances").GetEnumerator(); distances.MoveNext();
        var offsets = disp.Where("offsets").GetEnumerator();  bool hasOff = offsets.MoveNext();
        Node nNode = normals.Current, dNode = distances.Current, oNode = hasOff ? offsets.Current : null;
        if (nNode == null || dNode == null) return;

        var grid = new Vector3[size, size];
        for (int r = 0; r < size; r++)
        {
            float[] nrow = ParseFloats(nNode.Get("row" + r));
            float[] drow = ParseFloats(dNode.Get("row" + r));
            float[] orow = oNode != null ? ParseFloats(oNode.Get("row" + r)) : null;
            float fv = r / (float)(size - 1);
            Vector3 eL = Vector3.Lerp(c0, c3, fv), eR = Vector3.Lerp(c1, c2, fv);
            for (int c = 0; c < size; c++)
            {
                float fu = c / (float)(size - 1);
                Vector3 basePt = Vector3.Lerp(eL, eR, fu);
                Vector3 nrm = (nrow != null && c * 3 + 2 < nrow.Length) ? new Vector3(nrow[c * 3], nrow[c * 3 + 1], nrow[c * 3 + 2]) : Vector3.zero;
                float dist = (drow != null && c < drow.Length) ? drow[c] : 0f;
                Vector3 off = (orow != null && c * 3 + 2 < orow.Length) ? new Vector3(orow[c * 3], orow[c * 3 + 1], orow[c * 3 + 2]) : Vector3.zero;
                grid[r, c] = basePt + nrm * dist + off;
            }
        }

        if (!batches.TryGetValue(pl.col, out var mb)) { mb = new MeshBuild(); batches[pl.col] = mb; }
        for (int r = 0; r < size - 1; r++)
            for (int c = 0; c < size - 1; c++)
            {
                Vector3 a = ToUnity(grid[r, c]), b = ToUnity(grid[r + 1, c]), cc = ToUnity(grid[r + 1, c + 1]), dd = ToUnity(grid[r, c + 1]);
                mb.AddTri(a, b, cc, pl.n);
                mb.AddTri(a, cc, dd, pl.n);
            }
    }

    static float[] ParseFloats(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var parts = s.Split(new[] { ' ', '\t', '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
        var f = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++) float.TryParse(parts[i], NumberStyles.Float, CI, out f[i]);
        return f;
    }

    // Centre of a brush entity (average of its side plane sample points) — a marker position for
    // entities that have geometry instead of an 'origin'.
    // AABB энтити в Unity-юнитах (центр + полуразмеры) — рантайм-триггеры юзают как ТОЧНУЮ зону.
    static bool EntityAABBUnity(Node ent, out Vector3 center, out Vector3 half)
    {
        Vector3 mn = Vector3.one * 1e9f, mx = Vector3.one * -1e9f; bool any = false;
        foreach (var solid in ent.Where("solid"))
            foreach (var side in solid.Where("side"))
                if (TryPlane(side.Get("plane"), out var a, out var b, out var c))
                {
                    Vector3 ua = ToUnity(a), ub = ToUnity(b), uc = ToUnity(c);
                    mn = Vector3.Min(mn, Vector3.Min(ua, Vector3.Min(ub, uc)));
                    mx = Vector3.Max(mx, Vector3.Max(ua, Vector3.Max(ub, uc)));
                    any = true;
                }
        center = any ? (mn + mx) * 0.5f : Vector3.zero;
        half = any ? (mx - mn) * 0.5f : Vector3.zero;
        return any;
    }

    static Vector3 EntityCentroid(Node ent)
    {
        Vector3 sum = Vector3.zero; int n = 0;
        foreach (var solid in ent.Where("solid"))
            foreach (var side in solid.Where("side"))
                if (TryPlane(side.Get("plane"), out var a, out var b, out var c)) { sum += a + b + c; n += 3; }
        return n > 0 ? sum / n : Vector3.zero;
    }

    // A Source I/O connection value: "target,input,param,delay,times". The field delimiter is a
    // comma in old maps, or the ESC char (0x1B) in newer Hammer — handle both.
    static void AddConnection(VmfEntity ve, string outputName, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var parts = value.Split(',', '\u001b');
        var c = new VmfEntity.Connection { outputName = outputName, times = -1 };
        c.target = parts.Length > 0 ? parts[0] : "";
        c.input = parts.Length > 1 ? parts[1] : "";
        c.param = parts.Length > 2 ? parts[2] : "";
        if (parts.Length > 3) float.TryParse(parts[3], NumberStyles.Float, CI, out c.delay);
        if (parts.Length > 4) int.TryParse(parts[4], NumberStyles.Integer, CI, out c.times);
        ve.outputs.Add(c);
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
        // handles "x y z" and Hammer's bracketed "[x y z]" (startposition).
        var p = s.Replace("[", " ").Replace("]", " ").Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
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

    // Valve TOOL / invisible / sky materials that must NOT render — otherwise the map's
    // skybox brush encloses the whole level in a grey box and nodraw/clip/trigger/hint
    // volumes clutter it. These faces are skipped from the render+collision mesh. Real
    // world surfaces (concrete/metal/wood/textured) keep rendering. 2026-07-12.
    static bool SkipMaterial(string mat)
    {
        string m = (mat ?? "").ToUpperInvariant();
        if (m.Length == 0) return false;
        if (m.StartsWith("TOOLS/") || m.StartsWith("TOOLS\\")) return true; // all toolsX textures
        if (m.StartsWith("SKY/") || m.StartsWith("SKYBOX")) return true;    // 2D sky faces
        return m.Contains("NODRAW") || m.Contains("SKYBOX") || m.Contains("TRIGGER")
            || m.Contains("CLIP") || m.Contains("HINT") || m.Contains("SKIP")
            || m.Contains("INVISIBLE") || m.Contains("AREAPORTAL") || m.Contains("OCCLUDER")
            || m.Contains("BLOCKLIGHT") || m.Contains("BLOCK_LOS") || m.Contains("FOG")
            || m.Contains("PLAYERCLIP") || m.Contains("NPCCLIP");
    }

    // Brush entities whose geometry should NOT render (invisible logic volumes). Their VmfEntity
    // carrier is still created (for scripting) — only the visible mesh is skipped.
    // Брашевые энтити, которые РАНТАЙМ двигает (дверь/кнопка/лифт) — им нужен свой меш.
    static bool IsMovableEntity(string cls)
    {
        switch (cls)
        {
            case "func_door":
            case "func_door_rotating":
            case "prop_door_rotating":
            case "func_movelinear":
            case "func_button":
            case "func_rot_button":
            case "func_rotating":
            case "func_platrot":
            case "func_tracktrain":
                return true;
            default: return false;
        }
    }

    static bool IsNonRenderEntity(string cls)
    {
        if (string.IsNullOrEmpty(cls)) return false;
        if (cls.StartsWith("trigger_")) return true;
        switch (cls)
        {
            case "func_areaportal":
            case "func_areaportalwindow":
            case "func_occluder":
            case "func_clip_vphysics":
            case "func_nav_blocker":
            case "func_nav_avoid":
            case "func_nav_prefer":
            case "func_viscluster":
            case "func_precipitation":
            case "env_fog_controller":
                return true;
        }
        return false;
    }

    // Процедурная текстура РЕШЁТКИ — сетка металлических прутков + тёмные ячейки, тайлится.
    static Texture2D _grateTex;
    static Texture2D GrateTex()
    {
        if (_grateTex != null) return _grateTex;
        int N = 32; var t = new Texture2D(N, N, TextureFormat.RGBA32, true);
        t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Bilinear;
        var px = new Color32[N * N];
        int bar = 5; // толщина прутка (по краям ячейки → при тайлинге образует сетку)
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                bool isBar = (x < bar) || (y < bar);
                px[y * N + x] = isBar ? new Color32(158, 163, 170, 255)  // пруток — светлый металл, непрозрачный
                                      : new Color32(0, 0, 0, 0);         // ячейка — ПРОЗРАЧНАЯ (сквозная дырка, альфа 0)
            }
        t.SetPixels32(px); t.Apply();
        _grateTex = t;
        return t;
    }

    // Цвет грани по имени Source-материала. Порядок ВАЖЕН: кирпич до "WALL", решётка до металла.
    // (VTF-текстур нет — красим категориями, чтобы карта читалась: кирпич красный, металл стальной…)
    static Color32 MatColor(string mat)
    {
        string m = (mat ?? "").ToUpperInvariant();
        if (m.Contains("BRICK"))                            return new Color32(156, 88, 68, 255);   // кирпич — тёпло-красный
        if (m.Contains("GRATE") || m.Contains("GRID"))      return new Color32(88, 92, 98, 255);    // решётка — тёмный металл
        if (m.Contains("METALFLOOR"))                       return new Color32(96, 100, 108, 255);
        if (m.Contains("METAL") || m.Contains("STEEL") || m.Contains("CITADEL")) return new Color32(122, 128, 140, 255); // металл — сталь с синевой
        if (m.Contains("CONCRETEFLOOR"))                    return new Color32(118, 118, 116, 255);  // бетонный пол — темнее
        if (m.Contains("CONCRETE"))                         return new Color32(152, 150, 144, 255);  // бетон стен — светло-серый тёплый
        if (m.Contains("FLOOR") || m.Contains("TILE"))      return new Color32(104, 106, 110, 255);
        if (m.Contains("PROP") || m.Contains("COMBINE") || m.Contains("DISPLAY")) return new Color32(66, 78, 88, 255);   // пропсы/техно — тёмно-сине-серый
        if (m.Contains("WOOD") || m.Contains("PLASTER"))    return new Color32(150, 120, 84, 255);
        if (m.Contains("DIRT") || m.Contains("GROUND") || m.Contains("SAND")) return new Color32(104, 88, 64, 255);
        if (m.Contains("GLASS"))                            return new Color32(150, 190, 210, 255);
        if (m.Contains("WALL"))                             return new Color32(140, 140, 138, 255);  // общая стена — после кирпича
        if (m.Contains("NODRAW") || m.Contains("TOOLS"))    return new Color32(70, 70, 74, 255);
        return new Color32(130, 130, 128, 255);
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
        public readonly List<Vector2> uv = new List<Vector2>();
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
            // Планарные UV (world-space проекция на плоскость грани) — для тайл-текстур (решётка и т.п.)
            Vector3 up = Mathf.Abs(un.y) > 0.9f ? Vector3.right : Vector3.up;
            Vector3 ua = Vector3.Normalize(Vector3.Cross(un, up));
            Vector3 va = Vector3.Normalize(Vector3.Cross(un, ua));
            const float SC = 0.7f; // тайлинг
            uv.Add(new Vector2(Vector3.Dot(a, ua), Vector3.Dot(a, va)) * SC);
            uv.Add(new Vector2(Vector3.Dot(b, ua), Vector3.Dot(b, va)) * SC);
            uv.Add(new Vector2(Vector3.Dot(c, ua), Vector3.Dot(c, va)) * SC);
            tri.Add(i0); tri.Add(i0 + 1); tri.Add(i0 + 2);
        }
        public Mesh ToMesh()
        {
            var m = new Mesh();
            m.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.SetVertices(v); m.SetNormals(nrm); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
