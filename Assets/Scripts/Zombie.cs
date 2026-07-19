using System.Collections.Generic;
using UnityEngine;

/// <summary>Walks toward the nearest player; melee or ranged attack depending on Kind.
/// Normal (melee), Pistol (ranged hitscan), Tank (huge HP, slow, hard melee),
/// Grenadier (lobs explosive grenades), Runner (fast, fragile, rushes in melee),
/// Bloater (slow gas-bag: bursts a toxic cloud on death), Screamer (hangs back and
/// summons packs of normals), Brute (rare late-game mini-boss: massive HP, smashes walls).</summary>
public class Zombie : MonoBehaviour
{
    public enum Kind { Normal, Pistol, Tank, Grenadier, Runner, Bloater, Screamer, Brute }

    public float MaxHealth = 110f; // 60 base + 50 (wave 1)
    public float MoveSpeed = 4f;
    public float AttackRange = 2.2f;
    public float AttackDamage = 12f;
    public float AttackCooldown = 1f;

    // Each new wave makes zombies tougher and faster.
    public float HealthPerWave = 25f;
    public float SpeedPerWave = 0.3f;
    public float MaxMoveSpeed = 9f;

    public Kind kind = Kind.Normal;
    public int team = -1; // -1 = normal AI-wave zombie (chases players); 0/1 = a ZvZ side's unit

    float health;
    float lastAttack = -99f;
    float vSpeed;
    float speedMul = 1f;     // <1 while caught in barbed wire
    float slowUntil = -99f;
    float frozenUntil = -99f; // hard stop from the freeze tower
    bool Frozen => Time.time < frozenUntil;
    CharacterController cc;
    PlayerController player;
    Buildable nearBuildable;
    float nextBScan;

    // Ranged kinds (pistol / grenadier)
    bool ranged;
    float shootRange;
    float rangedCooldown;
    float rangedDamage;
    float nextRanged;

    // --- networking (co-op): host owns zombies; clients render puppets and report hits ---
    public int NetId;
    bool puppet;                 // true on a client: a visual copy driven by the host
    Vector3 netPos; float netYaw; bool netInit;
    static int nextNetId = 1;
    public bool IsPuppet => puppet;

    // Live registry of all zombies (real + puppets). Replaces per-frame
    // FindObjectsByType<Zombie>() scans across many scripts — a huge perf win when
    // dozens of turrets/traps each scanned the whole scene every frame.
    public static readonly List<Zombie> All = new List<Zombie>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => All.Clear();

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // Reused by ranged zombies' line-of-fire check (no per-shot array allocation).
    static readonly RaycastHit[] _shotHits = new RaycastHit[32];

    public static Zombie Create(Vector3 pos, Kind kind = Kind.Normal)
    {
        var root = new GameObject("Zombie");
        root.SetActive(false); // hold Awake until Kind is set, so stats/model match
        if (GameBootstrap.World != null) root.transform.SetParent(GameBootstrap.World);
        root.transform.position = pos;

        var z = root.AddComponent<Zombie>();
        z.kind = kind;
        z.NetId = nextNetId++;

        var vis = Models.BuildZombieVisual((int)kind);
        vis.transform.SetParent(root.transform, false);

        root.SetActive(true); // now Awake runs with the right Kind
        return z;
    }

    /// <summary>Client-side visual stand-in for a host-owned zombie (id assigned by the host).
    /// Keeps a collider so the local player can shoot it, but runs no AI.</summary>
    public static Zombie CreatePuppet(int netId, Kind kind, Vector3 pos)
    {
        var root = new GameObject("ZombiePuppet");
        root.SetActive(false);
        if (GameBootstrap.World != null) root.transform.SetParent(GameBootstrap.World);
        root.transform.position = pos;

        var z = root.AddComponent<Zombie>();
        z.kind = kind; z.NetId = netId; z.puppet = true;
        z.netPos = pos; z.netInit = true;

        var vis = Models.BuildZombieVisual((int)kind);
        vis.transform.SetParent(root.transform, false);

        root.SetActive(true);
        return z;
    }

    /// <summary>Host pushes this puppet's latest transform (called from LanManager).</summary>
    public void SetNet(Vector3 pos, float yaw) { netPos = pos; netYaw = yaw; netInit = true; }

    void Awake()
    {
        // Base scaling with the wave: +HP and +speed each wave (speed capped).
        int wave = GameManager.Instance != null ? Mathf.Max(1, GameManager.Instance.WaveNumber) : 1;
        float baseHP = MaxHealth + (wave - 1) * HealthPerWave;
        float baseSpd = Mathf.Min(MaxMoveSpeed, MoveSpeed + (wave - 1) * SpeedPerWave);

        switch (kind)
        {
            case Kind.Pistol:
                MaxHealth = baseHP * 0.85f; MoveSpeed = baseSpd;
                ranged = true; shootRange = 22f; rangedCooldown = 1.3f; rangedDamage = 7f;
                break;
            case Kind.Tank:
                MaxHealth = baseHP * 3f; MoveSpeed = baseSpd * 0.5f;
                AttackDamage = 26f; // crushing melee
                break;
            case Kind.Grenadier:
                MaxHealth = baseHP * 1.1f; MoveSpeed = baseSpd * 0.7f;
                ranged = true; shootRange = 28f; rangedCooldown = 3.2f;
                break;
            case Kind.Runner:
                MaxHealth = baseHP * 0.45f;                // fragile — drop it before it reaches you
                MoveSpeed = baseSpd + 5f;                  // very fast (sprints past the normal cap)
                AttackDamage = 14f;
                AttackCooldown = 0.8f;                     // quick, harrying hits
                break;
            case Kind.Bloater:
                MaxHealth = baseHP * 1.6f; MoveSpeed = baseSpd * 0.55f; // slow, bloated sack of gas
                AttackDamage = 16f;
                break;
            case Kind.Screamer:
                MaxHealth = baseHP * 0.6f; MoveSpeed = baseSpd * 0.9f;  // fragile — kill it fast
                AttackDamage = 8f;
                // Reuse the ranged "hold at distance and act on cooldown" logic to keep its
                // distance and SUMMON (see FireRanged → SummonPack) instead of shooting.
                ranged = true; shootRange = 20f; rangedCooldown = 7f; rangedDamage = 0f;
                break;
            case Kind.Brute:
                MaxHealth = baseHP * 8f; MoveSpeed = baseSpd * 0.6f;    // mini-boss wall of HP
                AttackDamage = 45f;                        // caves in walls and players alike
                AttackCooldown = 1.2f;
                break;
            default: // Normal
                MaxHealth = baseHP; MoveSpeed = baseSpd;
                break;
        }

        health = MaxHealth;
        cc = gameObject.AddComponent<CharacterController>();
        cc.height = 2f;
        cc.radius = 0.4f;
        cc.center = new Vector3(0f, 1f, 0f); // capsule spans 0..2 from the feet-origin root
    }

    void Start()
    {
        player = FindFirstObjectByType<PlayerController>();
    }

    void Update()
    {
        if (puppet) { PuppetUpdate(); return; }

        // Safety: if a zombie ever falls through the world, clean it up so it
        // can't softlock the wave (the wave waits until none remain).
        if (transform.position.y < -30f)
        {
            Destroy(gameObject);
            return;
        }

        if (player == null)
        {
            player = FindFirstObjectByType<PlayerController>();
        }

        if (Frozen) // stopped cold by the freeze tower: no move, no attack
        {
            if (cc != null)
            {
                if (cc.isGrounded) vSpeed = -1f; else vSpeed -= 18f * Time.deltaTime;
                cc.Move(Vector3.up * vSpeed * Time.deltaTime); // just settle to the ground
            }
            return;
        }

        if (Time.time >= slowUntil) speedMul = 1f; // slow wears off once clear of the wire

        if (team >= 0) { ZvZMove(); return; } // ZvZ unit: march on the enemy core instead of chasing players

        // Re-acquire the nearest attackable building only occasionally (the scan is
        // the costly part with many zombies); attack/move checks below are cheap.
        if (Time.time >= nextBScan)
        {
            nextBScan = Time.time + 0.5f;
            nearBuildable = NearestBuildable();
        }

        Vector3 move = Vector3.zero;
        GameObject attack = FindAttackTarget();

        if (attack != null)
        {
            // Something is in melee reach (the player, or a wall/building in the way).
            FaceTowards(attack.transform.position);
            Melee(attack);
        }
        else if (player != null)
        {
            // Walk toward the NEAREST player (host counts remote co-op players too).
            Vector3 tgt = NearestPlayerPos();
            Vector3 to = tgt - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            FaceTowards(tgt);

            float localDist = Vector3.Distance(transform.position, player.transform.position);
            if (ranged && localDist <= shootRange && HasShot())
            {
                // Hold position and open fire from range.
                if (Time.time >= nextRanged)
                {
                    nextRanged = Time.time + rangedCooldown;
                    FireRanged();
                }
            }
            else
            {
                move = (dist > 0.01f ? to / dist : Vector3.zero) * MoveSpeed * speedMul;
            }
        }

        // Standoff: never let a (big) zombie bury its body inside the player's camera.
        // It still attacks from melee range, just keeps ~1.5 m of personal space.
        if (player != null && !player.IsDead)
        {
            Vector3 away = transform.position - player.transform.position;
            away.y = 0f;
            float d = away.magnitude;
            const float standoff = 1.5f;
            if (d > 0.01f && d < standoff)
                move += away / d * (standoff - d) * 8f; // firm push back out to the standoff
        }

        if (cc.isGrounded) vSpeed = -1f;
        else vSpeed -= 18f * Time.deltaTime;

        cc.Move((move + Vector3.up * vSpeed) * Time.deltaTime);
    }

    GameObject FindAttackTarget()
    {
        float rangeSq = AttackRange * AttackRange;
        if (player != null && !player.IsDead &&
            (player.transform.position - transform.position).sqrMagnitude < rangeSq)
            return player.gameObject;
        if (nearBuildable != null &&
            (nearBuildable.transform.position - transform.position).sqrMagnitude < rangeSq)
            return nearBuildable.gameObject;
        return null;
    }

    // Costly scan, called on a throttle (every 0.5s).
    Buildable NearestBuildable()
    {
        Buildable best = null;
        float bestSq = (AttackRange + 3f) * (AttackRange + 3f);
        foreach (var b in Buildable.All)
        {
            if (b.IsTrap) continue; // mines aren't attacked
            float d = (b.transform.position - transform.position).sqrMagnitude;
            if (d < bestSq) { best = b; bestSq = d; }
        }
        return best;
    }

    void Melee(GameObject target)
    {
        if (Time.time - lastAttack < AttackCooldown) return;
        lastAttack = Time.time;
        var p = target.GetComponent<PlayerController>();
        if (p != null) p.TakeDamage(AttackDamage);
        var b = target.GetComponent<Buildable>();
        if (b != null) b.TakeDamage(AttackDamage);
    }

    // ---- ZvZ behaviour: march on the enemy core; fight enemy units/buildings in the way ----
    void ZvZMove()
    {
        float dt = Time.deltaTime;
        var enemyCore = ZvZManager.Instance != null ? ZvZManager.Instance.CoreOf(1 - team) : null;
        Vector3 target = enemyCore != null ? enemyCore.transform.position : transform.position;

        Vector3 move = Vector3.zero;
        GameObject atk = ZvZFindTarget(enemyCore);
        if (atk != null)
        {
            FaceTowards(atk.transform.position);
            ZvZMelee(atk);
        }
        else
        {
            Vector3 to = target - transform.position; to.y = 0f;
            float dist = to.magnitude;
            FaceTowards(target);
            move = (dist > 1f ? to / dist : Vector3.zero) * MoveSpeed * speedMul;
        }

        if (cc.isGrounded) vSpeed = -1f; else vSpeed -= 18f * dt;
        cc.Move((move + Vector3.up * vSpeed) * dt);
    }

    GameObject ZvZFindTarget(Core enemyCore)
    {
        float rSq = AttackRange * AttackRange;
        // enemy core (big, so use a slightly longer reach)
        if (enemyCore != null)
        {
            float cr = AttackRange + 2.6f;
            if ((enemyCore.transform.position - transform.position).sqrMagnitude < cr * cr) return enemyCore.gameObject;
        }
        // an enemy-side zombie in melee
        foreach (var z in All)
            if (z != null && z.team >= 0 && z.team != team && !z.puppet &&
                (z.transform.position - transform.position).sqrMagnitude < rSq) return z.gameObject;
        // an enemy-side building in melee
        foreach (var b in Buildable.All)
            if (b != null && !b.IsTrap && b.Team != team &&
                (b.transform.position - transform.position).sqrMagnitude < rSq) return b.gameObject;
        return null;
    }

    void ZvZMelee(GameObject target)
    {
        if (Time.time - lastAttack < 0.5f) return; // ZvZ swings faster than the 1s player-melee
        lastAttack = Time.time;
        Effects.Burst(target.transform.position + Vector3.up * 1f, new Color(1f, 0.55f, 0.3f), 5); // visible clash
        var core = target.GetComponent<Core>();
        if (core != null) { core.TakeDamage(AttackDamage * 1.5f); return; }
        var z = target.GetComponent<Zombie>();
        if (z != null) { z.TakeDamage(AttackDamage * 2.2f); return; } // hordes shred each other quickly
        var b = target.GetComponent<Buildable>();
        if (b != null) b.TakeDamage(AttackDamage * 1.5f);
    }

    // Clear line of sight to the player (nothing solid in between except other zombies)?
    bool HasShot()
    {
        if (player == null || player.IsDead) return false;
        Vector3 from = transform.position + Vector3.up * 1.4f;
        Vector3 toP = player.transform.position + Vector3.up * 0.6f;
        Vector3 dir = toP - from;
        int n = Physics.RaycastNonAlloc(from, dir.normalized, _shotHits, dir.magnitude);

        // Nearest hit that isn't a zombie (single pass, no sort/alloc).
        float bestD = float.MaxValue; Collider best = null;
        for (int i = 0; i < n; i++)
        {
            if (_shotHits[i].collider.GetComponentInParent<Zombie>() != null) continue; // ignore zombies (incl. self)
            if (_shotHits[i].distance < bestD) { bestD = _shotHits[i].distance; best = _shotHits[i].collider; }
        }
        if (best == null) return true; // open air
        return best.GetComponentInParent<PlayerController>() != null; // nearest solid blocker must be the player
    }

    void FireRanged()
    {
        if (player == null) return;
        Vector3 from = transform.position + Vector3.up * 1.4f;
        if (kind == Kind.Pistol)
        {
            Effects.Tracer(from, player.transform.position + Vector3.up * 0.6f);
            Effects.GunShot(from);
            if (!player.IsDead) player.TakeDamage(rangedDamage);
        }
        else if (kind == Kind.Screamer)
        {
            SummonPack();
        }
        else // Grenadier
        {
            Effects.GunShot(from);
            Grenade.Launch(from, player.transform.position);
        }
    }

    /// <summary>Screamer: shriek and spawn a small pack of normal zombies at its feet.
    /// Capped by MaxAlive so a lingering screamer can never flood the scene.</summary>
    void SummonPack()
    {
        var gm = GameManager.Instance;
        int cap = gm != null ? gm.MaxAlive : 120;
        if (Zombie.All.Count >= cap) return;
        Effects.Burst(transform.position + Vector3.up * 1.4f, new Color(0.75f, 0.3f, 1f), 24); // scream pop
        for (int i = 0; i < 3 && Zombie.All.Count < cap; i++)
        {
            Vector2 off = Random.insideUnitCircle * 3f;
            Vector3 p = transform.position + new Vector3(off.x, 0f, off.y);
            p.y = GameBootstrap.Hill(p.x, p.z) + 1f;
            Zombie.Create(p, Kind.Normal);
        }
    }

    /// <summary>Bloater: on death, rupture into a toxic cloud that hurts the player and
    /// nearby buildings. Host-side only (matches the melee/ranged damage model).</summary>
    void GasBurst()
    {
        Vector3 c = transform.position + Vector3.up * 1f;
        Effects.AirBlast(c, 6f);                                   // shockwave
        Effects.Burst(c, new Color(0.5f, 1f, 0.3f), 40);          // green toxic haze
        const float radius = 6f, dmg = 28f;
        float rSq = radius * radius;
        if (player != null && !player.IsDead &&
            (player.transform.position - transform.position).sqrMagnitude < rSq)
            player.TakeDamage(dmg);
        foreach (var b in Buildable.All)
            if (b != null && !b.IsTrap &&
                (b.transform.position - transform.position).sqrMagnitude < rSq)
                b.TakeDamage(dmg);
    }

    void FaceTowards(Vector3 worldPos)
    {
        Vector3 to = worldPos - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 8f * Time.deltaTime);
        }
    }

    // Nearest player to chase: the local player, plus remote co-op players (host only).
    Vector3 NearestPlayerPos()
    {
        Vector3 best = player != null ? player.transform.position : transform.position;
        float bestSq = player != null ? (best - transform.position).sqrMagnitude : float.MaxValue;
        var lan = LanManager.Instance;
        if (lan != null && lan.IsHost)
        {
            var rp = lan.RemotePlayers;
            for (int i = 0; i < rp.Length; i++)
            {
                float d = (rp[i] - transform.position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = rp[i]; }
            }
        }
        return best;
    }

    // Client-side puppet: follow the host's transform, and let a zombie standing on the
    // local player hurt them (damage-to-self is computed on each client).
    void PuppetUpdate()
    {
        if (netInit)
        {
            float k = 12f * Time.deltaTime;
            transform.position = Vector3.Lerp(transform.position, netPos, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, netYaw, 0f), k);
        }
        if (player == null || player.IsDead) return;
        float dSq = (player.transform.position - transform.position).sqrMagnitude;

        if (dSq < AttackRange * AttackRange && Time.time - lastAttack >= AttackCooldown)
        {
            lastAttack = Time.time;
            player.TakeDamage(AttackDamage);
        }
    }

    /// <summary>Caught in barbed wire: clamp to the strongest active slow and keep
    /// it alive for <paramref name="duration"/> seconds (re-applied every frame inside).</summary>
    public void Slow(float mul, float duration)
    {
        speedMul = Mathf.Min(speedMul, mul);
        slowUntil = Mathf.Max(slowUntil, Time.time + duration);
    }

    /// <summary>Freeze tower: stop this zombie dead (no move, no attack) for the duration.</summary>
    public void Freeze(float duration)
    {
        speedMul = 0f;
        slowUntil = Mathf.Max(slowUntil, Time.time + duration);
        frozenUntil = Mathf.Max(frozenUntil, Time.time + duration);
    }

    bool dead;

    // Death-spark colour per zombie kind (glows via bloom).
    Color VaporizeTint() => kind switch
    {
        Kind.Tank => new Color(1f, 0.35f, 0.25f),      // red
        Kind.Pistol => new Color(1f, 0.9f, 0.35f),     // yellow
        Kind.Grenadier => new Color(1f, 0.6f, 0.2f),   // orange
        Kind.Runner => new Color(1f, 0.55f, 0.3f),     // orange (runner)
        Kind.Bloater => new Color(0.5f, 1f, 0.3f),     // toxic green
        Kind.Screamer => new Color(0.75f, 0.3f, 1f),   // purple
        Kind.Brute => new Color(1f, 0.3f, 0.2f),       // deep red
        _ => new Color(0.5f, 1f, 0.45f),               // green (normal)
    };

    public void TakeDamage(float amount)
    {
        // On a co-op client the host owns this zombie — report the hit instead of applying
        // it locally (so weapons, turrets and mines all stay in sync, with no flicker).
        var lan = LanManager.Instance;
        if (lan != null && lan.Active && !lan.IsHost)
        {
            lan.SendZombieHit(NetId, amount);
            return;
        }

        if (dead) return; // overlapping AoE (airstrike/artillery) must not double-count the kill
        health -= amount;
        if (health <= 0f)
        {
            dead = true;
            if (kind == Kind.Bloater) GasBurst(); // rupture BEFORE the object is torn down
            Effects.Vaporize(transform.position + Vector3.up * 1f, VaporizeTint()); // stylized death pop
            if (GameManager.Instance != null) GameManager.Instance.OnZombieKilled(player);
            Destroy(gameObject);
        }
    }
}
