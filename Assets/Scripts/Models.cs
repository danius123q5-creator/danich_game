using UnityEngine;

/// <summary>
/// Builds simple "models" out of primitives (no external assets). Each model's
/// origin is at its feet (y=0). The shape changes with level (like the Lua
/// sentry1/2/3.mdl). Used for buildables, zombies and the placement preview.
/// </summary>
public static class Models
{
    static GameObject Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color c, Vector3 euler = default)
    {
        var g = GameObject.CreatePrimitive(type);
        Object.Destroy(g.GetComponent<Collider>()); // visuals only — collision is on the parent
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localEulerAngles = euler;
        g.transform.localScale = scale;
        GameBootstrap.SetColor(g, c);
        return g;
    }

    public static GameObject BuildVisual(int type, int level)
    {
        switch (type)
        {
            case 0: return BuildSentry(level);
            case 1: return BuildDispenser(level);
            case 2: return BuildMine(level);
            case 3: return BuildWall(level);
            case 16: return BuildWallLong(level);
            case 17: return BuildWallTall(level);
            case 4: return BuildDoor(level);
            case 5: return BuildBridge(level);
            case 6: return BuildStairs(level);
            case 20: return BuildLadder(level);
            case 12: return BuildBridgeCorner(level);
            case 13: return BuildBridgeT(level);
            case 14: return BuildBridgeCross(level);
            case 8: return BuildBarbedWire(level);
            case 9: return BuildAirStrike(level);
            case 10: return BuildTeslaCoil(level);
            case 11: return BuildArtillery(level);
            case 15: return BuildAntiAir(level);
            case 21: return BuildFreezeGun(level);
            case 22: return BuildOrbitalControl(level);
            case 23: return BuildWatchTower(level);
            case 18: return BuildCar(level);
            case 19: return BuildRpg(level);
            default: return BuildProxyMine(level);
        }
    }

    // Tall watchtower: 4 legs, the top platform (with a central hatch), railings, and a
    // ladder up the middle. Walls/colliders are defined in Buildable.AddColliders.
    public static GameObject BuildWatchTower(int level)
    {
        var root = new GameObject("WatchTowerModel");
        var t = root.transform;
        float H = 20f;
        Color wood = new Color(0.42f, 0.30f, 0.18f);
        Color darkw = new Color(0.34f, 0.24f, 0.15f);
        Color rail = new Color(0.50f, 0.36f, 0.20f);

        // 4 corner legs (slightly splayed look kept simple: vertical posts)
        float lx = 1.55f;
        Prim(PrimitiveType.Cube, t, new Vector3(-lx, H * 0.5f, -lx), new Vector3(0.2f, H, 0.2f), wood);
        Prim(PrimitiveType.Cube, t, new Vector3(lx, H * 0.5f, -lx), new Vector3(0.2f, H, 0.2f), wood);
        Prim(PrimitiveType.Cube, t, new Vector3(-lx, H * 0.5f, lx), new Vector3(0.2f, H, 0.2f), wood);
        Prim(PrimitiveType.Cube, t, new Vector3(lx, H * 0.5f, lx), new Vector3(0.2f, H, 0.2f), wood);

        // cross braces at a few heights (front/back/sides), for a built look
        for (float y = 4f; y < H; y += 4f)
        {
            Prim(PrimitiveType.Cube, t, new Vector3(0f, y, -lx), new Vector3(lx * 2f, 0.12f, 0.12f), darkw); // front rung
            Prim(PrimitiveType.Cube, t, new Vector3(0f, y, lx), new Vector3(lx * 2f, 0.12f, 0.12f), darkw);  // back rung
            Prim(PrimitiveType.Cube, t, new Vector3(-lx, y, 0f), new Vector3(0.12f, 0.12f, lx * 2f), darkw); // left
            Prim(PrimitiveType.Cube, t, new Vector3(lx, y, 0f), new Vector3(0.12f, 0.12f, lx * 2f), darkw);  // right
        }

        // solid top platform
        Prim(PrimitiveType.Cube, t, new Vector3(0f, 19.7f, 0f), new Vector3(3.4f, 0.25f, 3.4f), darkw);

        // railings on the back and sides (the front is left open for the ladder)
        Prim(PrimitiveType.Cube, t, new Vector3(0f, 20.4f, -1.7f), new Vector3(3.4f, 0.7f, 0.1f), rail);
        Prim(PrimitiveType.Cube, t, new Vector3(-1.7f, 20.4f, 0f), new Vector3(0.1f, 0.7f, 3.4f), rail);
        Prim(PrimitiveType.Cube, t, new Vector3(1.7f, 20.4f, 0f), new Vector3(0.1f, 0.7f, 3.4f), rail);
        // short rails flanking the ladder gap at the front
        Prim(PrimitiveType.Cube, t, new Vector3(-1.35f, 20.4f, 1.7f), new Vector3(0.7f, 0.7f, 0.1f), rail);
        Prim(PrimitiveType.Cube, t, new Vector3(1.35f, 20.4f, 1.7f), new Vector3(0.7f, 0.7f, 0.1f), rail);

        // ladder up the outside front (two rails + rungs), reaching just above the platform
        Prim(PrimitiveType.Cube, t, new Vector3(-0.35f, 10f, 2.3f), new Vector3(0.08f, 20.4f, 0.08f), rail);
        Prim(PrimitiveType.Cube, t, new Vector3(0.35f, 10f, 2.3f), new Vector3(0.08f, 20.4f, 0.08f), rail);
        for (float y = 0.4f; y < 20.2f; y += 0.5f)
            Prim(PrimitiveType.Cube, t, new Vector3(0f, y, 2.3f), new Vector3(0.78f, 0.07f, 0.1f), rail);
        return root;
    }

    // Freeze tower: metal column with a glowing ice orb + crystal spikes.
    public static GameObject BuildFreezeGun(int level)
    {
        var root = new GameObject("FreezeGunModel");
        var t = root.transform;
        Color metal = new Color(0.45f, 0.5f, 0.58f);
        Color ice = new Color(0.5f, 0.85f, 1f);
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.12f, 0f), new Vector3(1.0f, 0.12f, 1.0f), metal); // base
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.7f, 0f), new Vector3(0.4f, 0.6f, 0.4f), metal);   // column
        Prim(PrimitiveType.Sphere, t, new Vector3(0f, 1.45f, 0f), new Vector3(0.85f, 0.85f, 0.85f), ice);   // emitter orb
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f, rad = a * Mathf.Deg2Rad;
            Prim(PrimitiveType.Cube, t, new Vector3(Mathf.Cos(rad) * 0.5f, 1.45f, Mathf.Sin(rad) * 0.5f),
                 new Vector3(0.12f, 0.5f, 0.12f), ice, new Vector3(0f, a, 35f)); // crystal spikes
        }
        return root;
    }

    // Orbital control block: console + tilted screen + a small dish antenna.
    public static GameObject BuildOrbitalControl(int level)
    {
        var root = new GameObject("OrbitalControlModel");
        var t = root.transform;
        Color metal = new Color(0.32f, 0.34f, 0.38f);
        Color screen = new Color(0.2f, 0.7f, 0.9f);
        Color dish = new Color(0.7f, 0.72f, 0.74f);
        Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.35f, 0f), new Vector3(1.7f, 0.7f, 1.7f), metal);            // console base
        Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.85f, -0.5f), new Vector3(1.5f, 0.7f, 0.15f), screen, new Vector3(-25f, 0f, 0f)); // screen
        Prim(PrimitiveType.Cylinder, t, new Vector3(0.55f, 1.0f, 0.3f), new Vector3(0.1f, 0.5f, 0.1f), metal);    // antenna mast
        Prim(PrimitiveType.Sphere, t, new Vector3(0.55f, 1.55f, 0.3f), new Vector3(0.5f, 0.2f, 0.5f), dish);      // dish
        return root;
    }

    // Rocket turret: base + body + a fat launch tube pointing forward (+Z) so yaw-aim works.
    public static GameObject BuildRpg(int level)
    {
        var root = new GameObject("RpgModel");
        var t = root.transform;
        Color metal = new Color(0.30f, 0.32f, 0.34f);
        Color olive = level >= 3 ? new Color(0.5f, 0.42f, 0.2f) : new Color(0.36f, 0.42f, 0.3f);

        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.1f, 0f), new Vector3(0.9f, 0.1f, 0.9f), metal); // base
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.45f, 0f), new Vector3(0.22f, 0.3f, 0.22f), metal); // post
        Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.85f, 0f), new Vector3(0.55f, 0.4f, 0.5f), olive);     // body
        // Launch tube along +Z.
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.95f, 0.5f), new Vector3(0.22f, 0.55f, 0.22f), new Color(0.16f, 0.16f, 0.18f), new Vector3(90f, 0f, 0f));
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.95f, 1.0f), new Vector3(0.3f, 0.1f, 0.3f), new Color(0.22f, 0.22f, 0.2f), new Vector3(90f, 0f, 0f)); // muzzle
        // A rocket tip poking out for flavour.
        Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.95f, 1.05f), new Vector3(0.18f, 0.18f, 0.18f), new Color(0.7f, 0.25f, 0.2f));
        return root;
    }

    // Simple drivable car. Forward is +Z so it matches transform.forward driving.
    public static GameObject BuildCar(int level)
    {
        var root = new GameObject("CarModel");
        var t = root.transform;
        Color body = new Color(0.7f, 0.18f, 0.15f);   // red
        Color glass = new Color(0.18f, 0.28f, 0.4f);
        Color tire = new Color(0.08f, 0.08f, 0.09f);

        Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.55f, 0f), new Vector3(2.0f, 0.7f, 4.0f), body);      // chassis
        Prim(PrimitiveType.Cube, t, new Vector3(0f, 1.05f, -0.2f), new Vector3(1.7f, 0.7f, 2.0f), glass);   // cabin
        Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.7f, 2.05f), new Vector3(1.9f, 0.35f, 0.15f), new Color(0.95f, 0.9f, 0.6f)); // headlights bar

        float wx = 1.0f, wz = 1.35f, wy = 0.4f;
        var wsc = new Vector3(0.8f, 0.25f, 0.8f);
        var wrot = new Vector3(0f, 0f, 90f); // lay the cylinder on its side (axle along X)
        Prim(PrimitiveType.Cylinder, t, new Vector3(-wx, wy,  wz), wsc, tire, wrot);
        Prim(PrimitiveType.Cylinder, t, new Vector3( wx, wy,  wz), wsc, tire, wrot);
        Prim(PrimitiveType.Cylinder, t, new Vector3(-wx, wy, -wz), wsc, tire, wrot);
        Prim(PrimitiveType.Cylinder, t, new Vector3( wx, wy, -wz), wsc, tire, wrot);
        return root;
    }

    public static GameObject BuildSentry(int level)
    {
        var root = new GameObject("SentryModel");
        var t = root.transform;
        Color olive = level >= 3 ? new Color(0.52f, 0.45f, 0.22f)
                    : level == 2 ? new Color(0.45f, 0.5f, 0.36f)
                                 : new Color(0.42f, 0.47f, 0.36f);
        Color metal = new Color(0.30f, 0.32f, 0.34f);
        float headW = 0.66f + 0.08f * level;
        float headH = 0.42f + 0.09f * level;

        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.1f, 0f), new Vector3(0.95f, 0.1f, 0.95f), metal); // base
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.45f, 0f), new Vector3(0.22f, 0.35f, 0.22f), metal); // post
        Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.9f, 0f), new Vector3(headW, headH, 0.7f), olive);       // head

        if (level == 1)
        {
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.95f, 0.5f), new Vector3(0.13f, 0.45f, 0.13f), metal, new Vector3(90f, 0f, 0f));
        }
        else
        {
            Prim(PrimitiveType.Cylinder, t, new Vector3(-0.16f, 0.95f, 0.52f), new Vector3(0.12f, 0.5f, 0.12f), metal, new Vector3(90f, 0f, 0f));
            Prim(PrimitiveType.Cylinder, t, new Vector3(0.16f, 0.95f, 0.52f), new Vector3(0.12f, 0.5f, 0.12f), metal, new Vector3(90f, 0f, 0f));
        }

        if (level >= 3)
        {
            // rocket pod on top
            Prim(PrimitiveType.Cube, t, new Vector3(0f, 1.28f, 0f), new Vector3(0.5f, 0.26f, 0.42f), new Color(0.5f, 0.2f, 0.2f));
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 1.32f, 0.32f), new Vector3(0.1f, 0.22f, 0.1f), Color.gray, new Vector3(90f, 0f, 0f));
        }
        return root;
    }

    public static GameObject BuildDispenser(int level)
    {
        var root = new GameObject("DispenserModel");
        var t = root.transform;
        Color body = new Color(0.20f, 0.58f, 0.58f);
        Color beacon = new Color(0.5f, 1f, 0.85f);
        float h = 1.0f + 0.22f * level;
        float w = 0.78f + 0.1f * level;

        Prim(PrimitiveType.Cube, t, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, 0.7f), body);
        for (int i = 0; i < level; i++)
        {
            float x = (i - (level - 1) * 0.5f) * 0.34f;
            Prim(PrimitiveType.Sphere, t, new Vector3(x, h + 0.1f, 0f), new Vector3(0.24f, 0.24f, 0.24f), beacon);
        }
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, h * 0.5f, 0.37f), new Vector3(0.28f, 0.02f, 0.28f), Color.white, new Vector3(90f, 0f, 0f));
        return root;
    }

    public static GameObject BuildMine(int level)
    {
        var root = new GameObject("MineModel");
        var t = root.transform;
        Color drum = level >= 2 ? new Color(0.85f, 0.2f, 0.1f) : new Color(0.75f, 0.25f, 0.15f);
        Color band = new Color(0.95f, 0.8f, 0.1f);
        float r = 0.5f + 0.12f * (level - 1);

        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.4f, 0f), new Vector3(r, 0.4f, r), drum);                 // drum
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.55f, 0f), new Vector3(r + 0.03f, 0.08f, r + 0.03f), band); // band
        if (level >= 2)
        {
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.25f, 0f), new Vector3(r + 0.03f, 0.08f, r + 0.03f), band);
        }
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.95f, 0f), new Vector3(0.05f, 0.2f, 0.05f), Color.gray);   // detonator antenna
        return root;
    }

    public static GameObject BuildWall(int level)
    {
        var root = new GameObject("WallModel");
        var t = root.transform;
        Color stone = level >= 3 ? new Color(0.45f, 0.46f, 0.5f) : new Color(0.5f, 0.5f, 0.52f);
        Color post = new Color(0.4f, 0.4f, 0.42f);
        float thick = 0.3f + 0.12f * level;
        float h = 1.4f + 0.12f * level;

        Prim(PrimitiveType.Cube, t, new Vector3(0f, h * 0.5f, 0f), new Vector3(2.2f, h, thick), stone);             // slab
        Prim(PrimitiveType.Cube, t, new Vector3(-1.0f, h * 0.55f, 0f), new Vector3(0.3f, h + 0.2f, thick + 0.1f), post); // posts
        Prim(PrimitiveType.Cube, t, new Vector3(1.0f, h * 0.55f, 0f), new Vector3(0.3f, h + 0.2f, thick + 0.1f), post);
        if (level >= 3)
        {
            Prim(PrimitiveType.Cube, t, new Vector3(0f, h + 0.15f, 0f), new Vector3(2.4f, 0.2f, thick + 0.1f), post); // top cap
        }
        return root;
    }

    public static GameObject BuildWallLong(int level)
    {
        // Double-width wall: wider slab, three posts, optional top cap at level 3.
        var root = new GameObject("WallLongModel");
        var t = root.transform;
        Color stone = level >= 3 ? new Color(0.45f, 0.46f, 0.5f) : new Color(0.5f, 0.5f, 0.52f);
        Color post = new Color(0.4f, 0.4f, 0.42f);
        float thick = 0.3f + 0.12f * level;
        float h = 1.4f + 0.12f * level;
        const float w = 4.4f;

        Prim(PrimitiveType.Cube, t, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, thick), stone);                  // slab
        Prim(PrimitiveType.Cube, t, new Vector3(-2.1f, h * 0.55f, 0f), new Vector3(0.3f, h + 0.2f, thick + 0.1f), post); // posts
        Prim(PrimitiveType.Cube, t, new Vector3(0f, h * 0.55f, 0f), new Vector3(0.3f, h + 0.2f, thick + 0.1f), post);
        Prim(PrimitiveType.Cube, t, new Vector3(2.1f, h * 0.55f, 0f), new Vector3(0.3f, h + 0.2f, thick + 0.1f), post);
        if (level >= 3)
            Prim(PrimitiveType.Cube, t, new Vector3(0f, h + 0.15f, 0f), new Vector3(w + 0.2f, 0.2f, thick + 0.1f), post); // top cap
        return root;
    }

    public static GameObject BuildWallTall(int level)
    {
        // Normal-width but much taller wall — blocks even the climbers' sight lines.
        var root = new GameObject("WallTallModel");
        var t = root.transform;
        Color stone = level >= 3 ? new Color(0.45f, 0.46f, 0.5f) : new Color(0.5f, 0.5f, 0.52f);
        Color post = new Color(0.4f, 0.4f, 0.42f);
        float thick = 0.32f + 0.12f * level;
        float h = 2.6f + 0.2f * level;

        Prim(PrimitiveType.Cube, t, new Vector3(0f, h * 0.5f, 0f), new Vector3(2.2f, h, thick), stone);                 // tall slab
        Prim(PrimitiveType.Cube, t, new Vector3(-1.0f, h * 0.5f, 0f), new Vector3(0.32f, h + 0.2f, thick + 0.1f), post); // posts
        Prim(PrimitiveType.Cube, t, new Vector3(1.0f, h * 0.5f, 0f), new Vector3(0.32f, h + 0.2f, thick + 0.1f), post);
        if (level >= 3)
            Prim(PrimitiveType.Cube, t, new Vector3(0f, h + 0.15f, 0f), new Vector3(2.4f, 0.2f, thick + 0.1f), post);   // top cap
        return root;
    }

    public static GameObject BuildDoor(int level)
    {
        var root = new GameObject("DoorModel");
        var t = root.transform;
        Color leaf = level >= 3 ? new Color(0.55f, 0.55f, 0.6f) : new Color(0.5f, 0.38f, 0.22f);
        Color band = new Color(0.3f, 0.3f, 0.32f);
        float thick = 0.3f + 0.08f * level;

        Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.9f, 0f), new Vector3(2.1f, 1.8f, thick), leaf);                 // slab
        Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.9f, 0f), new Vector3(2.2f, 0.18f, thick + 0.05f), band);        // mid band
        Prim(PrimitiveType.Sphere, t, new Vector3(0.75f, 0.9f, thick * 0.55f), new Vector3(0.13f, 0.13f, 0.13f), new Color(0.85f, 0.7f, 0.2f)); // handle
        return root;
    }

    public static GameObject BuildBridge(int level)
    {
        // A wall laid flat = a raised walkable deck on legs, spans the river.
        var root = new GameObject("BridgeModel");
        var t = root.transform;
        Color wood = new Color(0.45f, 0.32f, 0.18f);
        Color rail = new Color(0.35f, 0.25f, 0.14f);

        Prim(PrimitiveType.Cube, t, new Vector3(0f, 2.0f, 0f), new Vector3(2.6f, 0.3f, 3.4f), wood);   // deck
        Prim(PrimitiveType.Cube, t, new Vector3(-1.1f, 1.0f, -1.5f), new Vector3(0.25f, 2.0f, 0.25f), rail); // legs
        Prim(PrimitiveType.Cube, t, new Vector3(1.1f, 1.0f, -1.5f), new Vector3(0.25f, 2.0f, 0.25f), rail);
        Prim(PrimitiveType.Cube, t, new Vector3(-1.1f, 1.0f, 1.5f), new Vector3(0.25f, 2.0f, 0.25f), rail);
        Prim(PrimitiveType.Cube, t, new Vector3(1.1f, 1.0f, 1.5f), new Vector3(0.25f, 2.0f, 0.25f), rail);
        Prim(PrimitiveType.Cube, t, new Vector3(-1.25f, 2.4f, 0f), new Vector3(0.12f, 0.5f, 3.4f), rail); // side rails
        Prim(PrimitiveType.Cube, t, new Vector3(1.25f, 2.4f, 0f), new Vector3(0.12f, 0.5f, 3.4f), rail);
        return root;
    }

    static void BridgeLeg(Transform t, float x, float z, Color c)
    {
        Prim(PrimitiveType.Cube, t, new Vector3(x, 1.0f, z), new Vector3(0.25f, 2.0f, 0.25f), c); // leg to ground
    }

    public static GameObject BuildBridgeCorner(int level)
    {
        // L-shaped deck: a north arm and an east arm meeting at the corner.
        var root = new GameObject("BridgeCornerModel");
        var t = root.transform;
        Color wood = new Color(0.45f, 0.32f, 0.18f);
        Color rail = new Color(0.35f, 0.25f, 0.14f);

        Prim(PrimitiveType.Cube, t, new Vector3(0f, 2.0f, 1.0f), new Vector3(2.6f, 0.3f, 2.0f), wood); // north arm
        Prim(PrimitiveType.Cube, t, new Vector3(1.0f, 2.0f, 0f), new Vector3(2.0f, 0.3f, 2.6f), wood); // east arm
        BridgeLeg(t, -1.1f, -1.1f, rail);
        BridgeLeg(t, -1.1f, 1.9f, rail);
        BridgeLeg(t, 1.9f, -1.1f, rail);
        BridgeLeg(t, 1.9f, 1.9f, rail);
        return root;
    }

    public static GameObject BuildBridgeT(int level)
    {
        // T-junction: a full east-west deck with a north arm.
        var root = new GameObject("BridgeTModel");
        var t = root.transform;
        Color wood = new Color(0.45f, 0.32f, 0.18f);
        Color rail = new Color(0.35f, 0.25f, 0.14f);

        Prim(PrimitiveType.Cube, t, new Vector3(0f, 2.0f, 0f), new Vector3(3.4f, 0.3f, 2.6f), wood);   // E-W deck
        Prim(PrimitiveType.Cube, t, new Vector3(0f, 2.0f, 1.0f), new Vector3(2.6f, 0.3f, 1.4f), wood); // north arm
        BridgeLeg(t, -1.6f, -1.1f, rail);
        BridgeLeg(t, 1.6f, -1.1f, rail);
        BridgeLeg(t, -1.6f, 1.1f, rail);
        BridgeLeg(t, 1.6f, 1.1f, rail);
        BridgeLeg(t, 0f, 1.6f, rail);
        return root;
    }

    public static GameObject BuildBridgeCross(int level)
    {
        // Cross (+): two perpendicular decks sharing the centre.
        var root = new GameObject("BridgeCrossModel");
        var t = root.transform;
        Color wood = new Color(0.45f, 0.32f, 0.18f);
        Color rail = new Color(0.35f, 0.25f, 0.14f);

        Prim(PrimitiveType.Cube, t, new Vector3(0f, 2.0f, 0f), new Vector3(2.6f, 0.3f, 3.4f), wood); // N-S deck
        Prim(PrimitiveType.Cube, t, new Vector3(0f, 2.0f, 0f), new Vector3(3.4f, 0.3f, 2.6f), wood); // E-W deck
        BridgeLeg(t, -1.1f, -1.1f, rail);
        BridgeLeg(t, 1.1f, -1.1f, rail);
        BridgeLeg(t, -1.1f, 1.1f, rail);
        BridgeLeg(t, 1.1f, 1.1f, rail);
        return root;
    }

    public static GameObject BuildStairs(int level)
    {
        // A walkable ramp (tilted slab). KEEPS its collider — you walk up it.
        var root = new GameObject("StairsModel");
        var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ramp.transform.SetParent(root.transform, false);
        ramp.transform.localPosition = new Vector3(0f, 1.0f, 0f);
        ramp.transform.localEulerAngles = new Vector3(-30f, 0f, 0f); // rises toward +Z
        ramp.transform.localScale = new Vector3(2.4f, 0.3f, 4.0f);
        GameBootstrap.SetColor(ramp, new Color(0.5f, 0.36f, 0.2f));
        return root;
    }

    public static GameObject BuildLadder(int level)
    {
        // A plain vertical ladder: two upright rails with rungs between them. No
        // collider of its own (visuals only) — the climb zone is a trigger added in
        // Buildable.AddColliders, and the player climbs it in PlayerController.
        var root = new GameObject("LadderModel");
        var t = root.transform;
        Color rail = new Color(0.42f, 0.3f, 0.18f); // wood
        Color rung = new Color(0.55f, 0.4f, 0.24f);

        float h = Ladder.Height;
        float halfW = 0.55f;
        Prim(PrimitiveType.Cube, t, new Vector3(-halfW, h * 0.5f, 0f), new Vector3(0.12f, h, 0.12f), rail); // left rail
        Prim(PrimitiveType.Cube, t, new Vector3(halfW, h * 0.5f, 0f), new Vector3(0.12f, h, 0.12f), rail);  // right rail
        for (float y = 0.35f; y < h; y += 0.45f)
            Prim(PrimitiveType.Cube, t, new Vector3(0f, y, 0f), new Vector3(halfW * 2f, 0.08f, 0.1f), rung); // rungs
        return root;
    }

    public static GameObject BuildProxyMine(int level)
    {
        // Flat "pancake" landmine on the ground.
        var root = new GameObject("ProxyMineModel");
        var t = root.transform;
        Color body = new Color(0.30f, 0.35f, 0.28f); // military green-grey
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.06f, 0f), new Vector3(0.8f, 0.06f, 0.8f), body);                 // disc
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.14f, 0f), new Vector3(0.32f, 0.04f, 0.32f), new Color(0.2f, 0.22f, 0.2f)); // pressure plate
        Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.2f, 0f), new Vector3(0.1f, 0.1f, 0.1f), Color.red);                // trigger light
        return root;
    }

    public static GameObject BuildBarbedWire(int level)
    {
        // Coiled barbed wire: crossed wooden stakes at each end, horizontal
        // strands between them, and little angled "barb" cubes along each strand.
        // Higher levels add strands and height.
        var root = new GameObject("BarbedWireModel");
        var t = root.transform;
        Color stake = new Color(0.32f, 0.26f, 0.18f);
        Color wire = new Color(0.58f, 0.58f, 0.62f);
        Color barb = new Color(0.72f, 0.70f, 0.64f);

        float span = 2.0f;                 // length along local X
        int strands = 1 + level;           // 2..4 horizontal wires
        float top = 0.55f + 0.16f * level; // overall height

        // Crossed X-stakes at each end.
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * span * 0.5f;
            Prim(PrimitiveType.Cylinder, t, new Vector3(x, top * 0.5f, 0f), new Vector3(0.06f, top * 0.6f, 0.06f), stake, new Vector3(0f, 0f, 30f));
            Prim(PrimitiveType.Cylinder, t, new Vector3(x, top * 0.5f, 0f), new Vector3(0.06f, top * 0.6f, 0.06f), stake, new Vector3(0f, 0f, -30f));
        }

        // Horizontal strands (cylinders laid along X) with barbs.
        const int barbsPer = 5;
        for (int s = 0; s < strands; s++)
        {
            float y = strands > 1 ? 0.25f + s * (top - 0.25f) / (strands - 1) : top * 0.6f;
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, y, 0f), new Vector3(0.025f, span * 0.5f, 0.025f), wire, new Vector3(0f, 0f, 90f));
            for (int i = 0; i < barbsPer; i++)
            {
                float bx = -span * 0.4f + i * (span * 0.8f) / (barbsPer - 1);
                Prim(PrimitiveType.Cube, t, new Vector3(bx, y, 0f), new Vector3(0.13f, 0.035f, 0.035f), barb, new Vector3(45f, 0f, 0f));
                Prim(PrimitiveType.Cube, t, new Vector3(bx, y, 0f), new Vector3(0.035f, 0.035f, 0.13f), barb, new Vector3(0f, 45f, 0f));
            }
        }
        return root;
    }

    public static GameObject BuildAirStrike(int level)
    {
        // Radar/beacon mast: pad, striped mast, tilted dish, flashing beacon, antenna.
        var root = new GameObject("AirStrikeModel");
        var t = root.transform;
        Color steel = new Color(0.32f, 0.34f, 0.37f);
        Color dark = new Color(0.16f, 0.17f, 0.2f);
        Color warn = new Color(0.95f, 0.55f, 0.15f);

        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.12f, 0f), new Vector3(1.6f, 0.12f, 1.6f), dark);   // pad
        Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.9f, 0f), new Vector3(0.5f, 1.6f, 0.5f), steel);        // mast
        Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.45f, 0f), new Vector3(0.56f, 0.12f, 0.56f), warn);     // hazard stripe
        Prim(PrimitiveType.Cube, t, new Vector3(0f, 1.35f, 0f), new Vector3(0.56f, 0.12f, 0.56f), warn);     // hazard stripe
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 1.9f, 0.25f), new Vector3(0.9f, 0.08f, 0.9f), steel, new Vector3(60f, 0f, 0f)); // radar dish
        Prim(PrimitiveType.Sphere, t, new Vector3(0f, 2.05f, 0f), new Vector3(0.22f, 0.22f, 0.22f), warn);   // beacon light
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 2.45f, 0f), new Vector3(0.05f, 0.4f, 0.05f), steel); // antenna
        return root;
    }

    public static GameObject BuildTeslaCoil(int level)
    {
        // Coil tower: base, insulator, stacked copper rings, top electrode sphere.
        var root = new GameObject("TeslaCoilModel");
        var t = root.transform;
        Color baseC = new Color(0.25f, 0.26f, 0.3f);
        Color copper = new Color(0.62f, 0.42f, 0.22f);
        Color spark = new Color(0.45f, 0.8f, 1f);

        Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.2f, 0f), new Vector3(1.3f, 0.4f, 1.3f), baseC);        // base
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.55f, 0f), new Vector3(0.3f, 0.18f, 0.3f), baseC);  // insulator
        for (int i = 0; i < 4; i++)
            Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.85f + i * 0.28f, 0f), new Vector3(0.4f - i * 0.05f, 0.12f, 0.4f - i * 0.05f), copper); // coil rings
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 2.0f, 0f), new Vector3(0.12f, 0.18f, 0.12f), baseC); // top post
        Prim(PrimitiveType.Sphere, t, new Vector3(0f, 2.35f, 0f), new Vector3(0.75f, 0.45f, 0.75f), spark);  // toroid electrode
        return root;
    }

    public static GameObject BuildArtillery(int level)
    {
        // Heavy cannon: base ring, turret body, big angled barrel, side armor.
        var root = new GameObject("ArtilleryModel");
        var t = root.transform;
        Color armor = new Color(0.30f, 0.33f, 0.28f);
        Color dark = new Color(0.16f, 0.18f, 0.16f);

        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.15f, 0f), new Vector3(2.0f, 0.15f, 2.0f), dark);   // base ring
        Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.55f, 0f), new Vector3(1.5f, 0.7f, 1.7f), armor);       // turret body
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.95f, 1.0f), new Vector3(0.22f, 1.1f, 0.22f), dark, new Vector3(70f, 0f, 0f));  // barrel
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 1.4f, 1.5f), new Vector3(0.26f, 0.2f, 0.26f), armor, new Vector3(70f, 0f, 0f));  // muzzle brake
        Prim(PrimitiveType.Cube, t, new Vector3(-0.8f, 0.6f, 0f), new Vector3(0.15f, 0.6f, 1.4f), armor);    // side armor
        Prim(PrimitiveType.Cube, t, new Vector3(0.8f, 0.6f, 0f), new Vector3(0.15f, 0.6f, 1.4f), armor);
        return root;
    }

    public static GameObject BuildAntiAir(int level)
    {
        // Flak emplacement: base, turret box, twin barrels angled to the sky, radar dish.
        var root = new GameObject("AntiAirModel");
        var t = root.transform;
        Color metal = new Color(0.30f, 0.33f, 0.36f);
        Color dark = new Color(0.18f, 0.20f, 0.22f);
        Color barrel = new Color(0.22f, 0.24f, 0.26f);

        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.12f, 0f), new Vector3(1.1f, 0.12f, 1.1f), dark);  // base
        Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.4f, 0f), new Vector3(0.3f, 0.2f, 0.3f), metal);   // post
        Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.75f, 0f), new Vector3(0.8f, 0.4f, 0.8f), metal);      // turret body
        // twin barrels angled up toward the sky
        Prim(PrimitiveType.Cylinder, t, new Vector3(-0.18f, 1.15f, 0.28f), new Vector3(0.09f, 0.7f, 0.09f), barrel, new Vector3(40f, 0f, 0f));
        Prim(PrimitiveType.Cylinder, t, new Vector3(0.18f, 1.15f, 0.28f), new Vector3(0.09f, 0.7f, 0.09f), barrel, new Vector3(40f, 0f, 0f));
        // radar dish on the side
        Prim(PrimitiveType.Cylinder, t, new Vector3(0.5f, 1.0f, -0.2f), new Vector3(0.5f, 0.05f, 0.5f), new Color(0.5f, 0.55f, 0.5f), new Vector3(60f, 30f, 0f));
        return root;
    }

    // Per-kind shared materials for the Quaternius CC0 zombie model (palette-textured).
    static readonly Material[] _zMats = new Material[5];
    static bool _zModelFailed;

    /// <summary>Zombie visual: a Quaternius CC0 model when available, else the procedural
    /// primitive zombie. Runner (4) uses the same model, scaled leaner and tinted orange.</summary>
    public static GameObject BuildZombieVisual(int kind = 0)
    {
        var model = TryBuildZombieModel(kind);
        if (model != null) return model;
        return BuildZombie(kind);
    }

    static Material ZombieMat(int kind)
    {
        kind = Mathf.Clamp(kind, 0, 4);
        if (_zMats[kind] != null) return _zMats[kind];
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        var tex = Resources.Load<Texture2D>("Zombies/ZombieTexture");
        if (sh == null || tex == null) return null;
        tex.filterMode = FilterMode.Point; // crisp palette colours (no bilinear bleed)
        var m = new Material(sh);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", ZombieTint(kind));
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
        _zMats[kind] = m;
        return m;
    }

    static Color ZombieTint(int kind) => kind switch
    {
        2 => new Color(1f, 0.8f, 0.78f),   // tank — slightly bloodied
        3 => new Color(1f, 0.95f, 0.82f),  // grenadier
        4 => new Color(1f, 0.55f, 0.4f),   // runner — orange-red
        _ => Color.white,                   // normal/pistol — texture as-is
    };

    static float ZombieScaleMul(int kind) => kind == 2 ? 1.5f : kind == 4 ? 0.82f : 1f; // tank bigger, runner leaner

    static GameObject TryBuildZombieModel(int kind)
    {
        if (_zModelFailed) return null;
        var mat = ZombieMat(kind);
        var prefab = mat != null ? Resources.Load<GameObject>("Zombies/Zombie") : null;
        if (prefab == null) { _zModelFailed = true; return null; }

        var root = new GameObject("ZombieModel");
        var go = Object.Instantiate(prefab, root.transform, false);
        // Quaternius zombie is ~7 units tall, modelled facing -Z → scale to ~2.1 m and turn to face +Z.
        float scale = (2.1f / 7f) * ZombieScaleMul(kind);
        go.transform.localScale = Vector3.one * scale;
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
        return root;
    }

    // kind: 0 normal, 1 pistol, 2 tank, 3 grenadier
    public static GameObject BuildZombie(int kind = 0)
    {
        var root = new GameObject("ZombieModel");
        var t = root.transform;

        Color skin, dark;
        switch (kind)
        {
            case 1: skin = new Color(0.38f, 0.50f, 0.30f); dark = new Color(0.22f, 0.28f, 0.16f); break; // pistol
            case 2: skin = new Color(0.46f, 0.42f, 0.22f); dark = new Color(0.28f, 0.26f, 0.14f); break; // tank
            case 3: skin = new Color(0.50f, 0.45f, 0.24f); dark = new Color(0.30f, 0.26f, 0.15f); break; // grenadier
            case 4: skin = new Color(0.70f, 0.35f, 0.18f); dark = new Color(0.40f, 0.20f, 0.12f); break; // runner (orange)
            default: skin = new Color(0.40f, 0.55f, 0.25f); dark = new Color(0.24f, 0.30f, 0.16f); break; // normal
        }

        Prim(PrimitiveType.Capsule, t, new Vector3(-0.2f, 0.5f, 0f), new Vector3(0.22f, 0.5f, 0.22f), dark); // left leg
        Prim(PrimitiveType.Capsule, t, new Vector3(0.2f, 0.5f, 0f), new Vector3(0.22f, 0.5f, 0.22f), dark);  // right leg
        Prim(PrimitiveType.Capsule, t, new Vector3(0f, 1.3f, 0f), new Vector3(0.6f, 0.45f, 0.4f), skin);     // torso
        Prim(PrimitiveType.Sphere, t, new Vector3(0f, 1.95f, 0f), new Vector3(0.45f, 0.45f, 0.45f), skin);   // head
        Prim(PrimitiveType.Capsule, t, new Vector3(-0.35f, 1.45f, 0.3f), new Vector3(0.16f, 0.32f, 0.16f), dark, new Vector3(90f, 0f, 0f));
        Prim(PrimitiveType.Capsule, t, new Vector3(0.35f, 1.45f, 0.3f), new Vector3(0.16f, 0.32f, 0.16f), dark, new Vector3(90f, 0f, 0f));

        if (kind == 1) // pistol in the right hand
        {
            Color gun = new Color(0.12f, 0.12f, 0.14f);
            Prim(PrimitiveType.Cube, t, new Vector3(0.4f, 1.45f, 0.6f), new Vector3(0.09f, 0.09f, 0.26f), gun);
            Prim(PrimitiveType.Cube, t, new Vector3(0.4f, 1.36f, 0.52f), new Vector3(0.08f, 0.12f, 0.09f), gun); // grip
        }
        else if (kind == 3) // grenade launcher on the shoulder
        {
            Color tube = new Color(0.15f, 0.16f, 0.13f);
            Prim(PrimitiveType.Cylinder, t, new Vector3(0.32f, 1.6f, 0.45f), new Vector3(0.13f, 0.4f, 0.13f), tube, new Vector3(75f, 0f, 0f));
            Prim(PrimitiveType.Cylinder, t, new Vector3(0.32f, 1.72f, 0.75f), new Vector3(0.17f, 0.1f, 0.17f), new Color(0.22f, 0.22f, 0.2f), new Vector3(75f, 0f, 0f)); // muzzle
        }

        if (kind == 2) root.transform.localScale = Vector3.one * 1.35f; // tank: bigger and bulkier
        else if (kind == 4) root.transform.localScale = Vector3.one * 0.82f; // runner: smaller and leaner
        return root;
    }
}
