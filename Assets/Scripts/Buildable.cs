using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base for the engineer buildables, ported from the GMod sent_engi_* entities:
/// build phase, level/health, world label, death explosion. The visual model is
/// rebuilt to match the level (like sentry1/2/3.mdl). Upgrades have a cooldown.
/// </summary>
public class Buildable : MonoBehaviour
{
    public int Type;
    public int BuildCost = 100;
    public int UpgradeCost = 200;   // metal needed to gain a level
    public int InvestAmount = 50;   // metal added per E press (upgrades)
    public virtual int FundChunk => 136; // metal poured into a super-weapon's funding per E press
    public int Team = 0;            // ZvZ side: 0 = player, 1 = enemy. Ignored outside ZvZ mode.
    public int Invested = 0;        // accumulated toward the next level
    public int MaxLevel = 3;
    public int Level = 1;

    public float Health = 100f;
    public float MaxHealth = 100f;
    public bool Building = true;
    public float BuildTime = 2.5f;
    public float UpgradeCooldown = 1f; // seconds between investments

    // ---- networking (co-op): buildings are host-authoritative; clients see puppets ----
    public int NetId;
    bool puppet;                       // true on a client: a non-functional visual copy
    public bool IsPuppet => puppet;
    static int nextNetId = 1;

    // Live registry of all buildings (real + puppets). Replaces per-frame / per-message
    // FindObjectsByType<Buildable>() scene scans in turrets, zombies and the net layer.
    public static readonly List<Buildable> All = new List<Buildable>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => All.Clear();

    protected virtual void OnEnable() { All.Add(this); }
    protected virtual void OnDisable() { All.Remove(this); }

    public bool NeedsRepair => Health < MaxHealth;
    public bool CanUpgrade => Level < MaxLevel && !Building;
    public virtual bool IsTrap => false; // traps (mines) are not attacked by zombies

    // Set by the player each frame: true only while this building is aimed at.
    // The floating LVL/health label is shown only then, to avoid map clutter.
    [HideInInspector] public bool Hovered;
    public float UpgradeReadyIn => Mathf.Max(0f, upgReady - Time.time);

    // ---- Special weapons: funding ----
    // These mega-weapons cost several thousand metal, far above the wallet cap, so
    // you fund them incrementally (press E to dump metal in) before they switch on.
    public virtual int FundingRequired => 0;       // 0 = ordinary building, no funding gate
    public int FundingPaid { get; protected set; }
    // Super-weapons also need OIL (captured from refineries) on top of the metal funding.
    public virtual int OilRequired => 0;           // 0 = no oil needed
    public int OilPaid { get; protected set; }
    public int OilRemaining => Mathf.Max(0, OilRequired - OilPaid);
    public bool IsFunding => !Building && (FundingRequired > 0 || OilRequired > 0)
                             && (FundingPaid < FundingRequired || OilPaid < OilRequired);
    public int FundingRemaining => Mathf.Max(0, FundingRequired - FundingPaid);

    // ---- Special weapons: ammo reserve ----
    // Special weapons fire from their own metal "bak", refilled by the player (E),
    // so firing never silently drains the walking-around wallet.
    public virtual int ReserveMax => 0;        // 0 = doesn't use a reserve
    public int Reserve { get; protected set; }
    public bool UsesReserve => ReserveMax > 0;
    // Super-weapons run entirely on OIL (no metal): their funding, ammo reserve and upgrades
    // are all paid in oil. Hardcore turret ammo keeps using metal (this stays false there).
    public virtual bool ReserveIsOil => false;

    float noMetalUntil;                                   // set when a shot is denied for lack of metal
    bool NoMetalWarning => Time.time < noMetalUntil;      // drives the floating "NO METAL" alert

    protected float buildEnd;
    protected PlayerController owner;
    bool dead;            // set once OnDeath runs, so death/splash can't fire twice
    float upgReady;
    TextMesh label;
    protected Transform visual;
    int visualLevel = -1;
    Vector3 baseScale = Vector3.one;

    public static GameObject Create(int type, Vector3 groundPos, Quaternion rot, PlayerController owner)
    {
        var root = new GameObject("Buildable");
        if (GameBootstrap.World != null) root.transform.SetParent(GameBootstrap.World);
        root.transform.position = groundPos + Vector3.up * 0.02f;
        root.transform.rotation = rot;

        AddColliders(root, type);
        var b = AddBehaviour(root, type);
        b.Type = type;
        b.owner = owner;
        b.NetId = nextNetId++;
        return root;
    }

    /// <summary>Client-side: a non-functional visual copy of a host-owned building
    /// (id from the host). Keeps colliders so the player can aim at / collide with it,
    /// but runs no firing/build/damage logic — its state is pushed by the host.</summary>
    public static Buildable CreatePuppet(int netId, int type, Vector3 pos, Quaternion rot)
    {
        var root = new GameObject("BuildablePuppet");
        root.SetActive(false); // hold Awake until the puppet flag is set
        if (GameBootstrap.World != null) root.transform.SetParent(GameBootstrap.World);
        root.transform.position = pos;
        root.transform.rotation = rot;

        AddColliders(root, type);
        var b = AddBehaviour(root, type);
        b.Type = type;
        b.NetId = netId;
        b.puppet = true;

        root.SetActive(true); // Awake now runs with puppet = true
        return b;
    }

    static void AddColliders(GameObject root, int type)
    {
        if (type == 6) return; // stairs/ramp carries its own tilted collider on the visual
        void AddBox(Vector3 c, Vector3 s, bool trig = false)
        {
            var bx = root.AddComponent<BoxCollider>();
            bx.center = c; bx.size = s; bx.isTrigger = trig;
        }
        switch (type)
        {
            case 3: case 4: AddBox(new Vector3(0f, 0.85f, 0f), new Vector3(2.2f, 1.7f, 0.45f)); break; // wall / door
            case 16: AddBox(new Vector3(0f, 0.85f, 0f), new Vector3(4.4f, 1.7f, 0.5f)); break;        // long wall
            case 17: AddBox(new Vector3(0f, 1.5f, 0f), new Vector3(2.2f, 3.0f, 0.5f)); break;         // tall wall
            case 5: AddBox(new Vector3(0f, 2.0f, 0f), new Vector3(2.6f, 0.4f, 3.4f)); break;           // straight bridge deck (walkable)
            case 7: AddBox(new Vector3(0f, 0.12f, 0f), new Vector3(1.2f, 0.24f, 1.2f)); break;         // flat landmine (step over it)
            case 8: AddBox(new Vector3(0f, 0.45f, 0f), new Vector3(2.4f, 0.9f, 0.7f), true); break;    // barbed wire (trigger: walk through)
            case 20: AddBox(new Vector3(0f, Ladder.Height * 0.5f, 0f), new Vector3(1.4f, Ladder.Height, 1.0f), true); break; // vertical ladder: trigger climb zone
            case 9: AddBox(new Vector3(0f, 1.2f, 0f), new Vector3(1.7f, 2.4f, 1.7f)); break;           // air strike beacon
            case 10: AddBox(new Vector3(0f, 1.2f, 0f), new Vector3(1.4f, 2.4f, 1.4f)); break;          // tesla coil
            case 11: AddBox(new Vector3(0f, 0.7f, 0f), new Vector3(2.0f, 1.4f, 2.0f)); break;          // artillery cannon
            case 15: AddBox(new Vector3(0f, 0.8f, 0f), new Vector3(1.4f, 1.6f, 1.4f)); break;          // anti-air (ПВО)
            case 21: AddBox(new Vector3(0f, 1.0f, 0f), new Vector3(1.5f, 2.0f, 1.5f)); break;          // freeze tower
            case 22: AddBox(new Vector3(0f, 0.7f, 0f), new Vector3(1.8f, 1.4f, 1.8f)); break;          // orbital control block
            case 23: // watchtower: huge top platform on 4 columns + a climb column up the OUTSIDE front
                AddBox(new Vector3(0f, 19.7f, 0f), new Vector3(WatchTower.Half * 2f, 0.4f, WatchTower.Half * 2f)); // walkable platform
                var cz = new GameObject("ClimbZone");
                cz.transform.SetParent(root.transform, false);
                cz.transform.localPosition = new Vector3(0f, 10.4f, WatchTower.Front);  // front, just outside the platform
                var czc = cz.AddComponent<BoxCollider>();
                czc.isTrigger = true;
                czc.size = new Vector3(1.2f, 20.8f, 1.0f);                              // full-height climb column
                cz.AddComponent<ClimbZone>();
                break;
            case 24: AddBox(new Vector3(0f, 0.55f, 0f), new Vector3(1.4f, 1.1f, 1.4f)); break;          // spinning blades hub
            case 27: AddBox(new Vector3(0f, 0.7f, 0f), new Vector3(0.7f, 0.9f, 3.0f)); break;          // oil pipe segment
            case 28: AddBox(new Vector3(0f, 0.8f, 0f), new Vector3(1.7f, 1.6f, 1.7f)); break;          // oil doser tank
            case 29: AddBox(new Vector3(0f, 1.6f, 0f), new Vector3(2.6f, 3.6f, 5.4f)); break;          // oil derrick (pumpjack)
            case 30: AddBox(new Vector3(0f, 0.5f, 0f), new Vector3(1.2f, 0.8f, 3.0f)); break;          // conveyor segment
            case 31: AddBox(new Vector3(0f, 0.8f, 0f), new Vector3(1.8f, 1.6f, 1.8f)); break;          // metal vat tank
            case 32: AddBox(new Vector3(0f, 2.2f, 0f), new Vector3(2.2f, 4.4f, 2.2f)); break;          // drilling rig
            case 33: AddBox(new Vector3(0f, 0.7f, 0f), new Vector3(2.0f, 1.4f, 2.0f)); break;          // oil pocket tank
            case 34: AddBox(new Vector3(0f, 0.8f, 0f), new Vector3(1.4f, 1.6f, 1.4f)); break;          // flamethrower
            case 35: AddBox(new Vector3(0f, 0.9f, 0f), new Vector3(2.2f, 1.8f, 2.2f)); break;          // oil hub
            case 36: AddBox(new Vector3(0f, 0.8f, 0f), new Vector3(1.6f, 1.6f, 1.6f)); break;          // SAM launcher
            case 26: // big platform: huge top deck on 4 columns + a climb column up the OUTSIDE front
                AddBox(new Vector3(0f, BigPlatform.Height - 0.3f, 0f), new Vector3(BigPlatform.Half * 2f, 0.4f, BigPlatform.Half * 2f)); // walkable deck
                var cz2 = new GameObject("ClimbZone");
                cz2.transform.SetParent(root.transform, false);
                cz2.transform.localPosition = new Vector3(0f, BigPlatform.Height * 0.5f + 0.2f, BigPlatform.Front);
                var cz2c = cz2.AddComponent<BoxCollider>();
                cz2c.isTrigger = true;
                cz2c.size = new Vector3(1.2f, BigPlatform.Height + 1.2f, 1.0f);          // full-height climb column
                cz2.AddComponent<ClimbZone>();
                break;
            case 25: AddBox(new Vector3(0f, 1.1f, 0f), new Vector3(1.7f, 2.2f, 1.7f)); break;          // missile silo
            case 18: AddBox(new Vector3(0f, 0.6f, 0f), new Vector3(2.0f, 1.2f, 4.0f)); break;          // car
            case 19: AddBox(new Vector3(0f, 0.6f, 0f), new Vector3(1.0f, 1.2f, 1.2f)); break;          // rpg turret
            case 12: // corner bridge (L): north arm + east arm
                AddBox(new Vector3(0f, 2.0f, 1.0f), new Vector3(2.6f, 0.4f, 2.0f));
                AddBox(new Vector3(1.0f, 2.0f, 0f), new Vector3(2.0f, 0.4f, 2.6f));
                break;
            case 13: // T-junction bridge: full E-W deck + north arm
                AddBox(new Vector3(0f, 2.0f, 0f), new Vector3(3.4f, 0.4f, 2.6f));
                AddBox(new Vector3(0f, 2.0f, 1.0f), new Vector3(2.6f, 0.4f, 1.4f));
                break;
            case 14: // cross bridge (+): two perpendicular decks
                AddBox(new Vector3(0f, 2.0f, 0f), new Vector3(2.6f, 0.4f, 3.4f));
                AddBox(new Vector3(0f, 2.0f, 0f), new Vector3(3.4f, 0.4f, 2.6f));
                break;
            default: AddBox(new Vector3(0f, 0.6f, 0f), new Vector3(1.0f, 1.2f, 1.0f)); break;
        }
    }

    static Buildable AddBehaviour(GameObject root, int type)
    {
        switch (type)
        {
            case 0: return root.AddComponent<Sentry>();
            case 1: return root.AddComponent<Dispenser>();
            case 2: return root.AddComponent<Mine>();
            case 3: return root.AddComponent<Wall>();
            case 4: return root.AddComponent<Door>();
            case 5: return root.AddComponent<Bridge>();
            case 6: return root.AddComponent<Stairs>();
            case 20: return root.AddComponent<Ladder>();
            case 12: case 13: case 14: return root.AddComponent<Bridge>(); // corner / T / cross decks behave like a bridge
            case 8: return root.AddComponent<BarbedWire>();
            case 9: return root.AddComponent<AirStrike>();
            case 10: return root.AddComponent<TeslaCoil>();
            case 11: return root.AddComponent<Artillery>();
            case 15: return root.AddComponent<AntiAir>();
            case 21: return root.AddComponent<FreezeGun>();
            case 22: return root.AddComponent<OrbitalStation>();
            case 23: return root.AddComponent<WatchTower>();
            case 24: return root.AddComponent<BladeTrap>();
            case 26: return root.AddComponent<BigPlatform>();
            case 27: return root.AddComponent<OilPipe>();
            case 28: return root.AddComponent<OilDispenser>();
            case 29: return root.AddComponent<OilDerrick>();
            case 30: return root.AddComponent<Conveyor>();
            case 31: return root.AddComponent<MetalVat>();
            case 32: return root.AddComponent<MetalDrill>();
            case 33: return root.AddComponent<OilPocket>();
            case 34: return root.AddComponent<Flamethrower>();
            case 35: return root.AddComponent<OilHub>();
            case 36: return root.AddComponent<Sam>();
            case 25: return root.AddComponent<MissileSilo>();
            case 18: return root.AddComponent<Car>();
            case 19: return root.AddComponent<Rpg>();
            case 16: case 17: return root.AddComponent<Wall>(); // long / tall wall behave like a wall
            default: return root.AddComponent<ProxyMine>();
        }
    }

    protected virtual void Awake()
    {
        if (GameRoot.Hardcore) InvestAmount = 35; // hardcore: smaller deposits (cooldown stays 2 s)
        ApplyLevel();
        Building = true;
        buildEnd = Time.time + BuildTime;

        var lgo = new GameObject("Label");
        lgo.transform.SetParent(transform, false);
        lgo.transform.localPosition = new Vector3(0f, 1.9f, 0f);
        label = lgo.AddComponent<TextMesh>();
        label.anchor = TextAnchor.MiddleCenter;
        label.characterSize = 0.12f;
        label.fontSize = 64;
        label.color = Color.white;
        lgo.SetActive(false); // hidden until the player aims at this building
    }

    void RebuildVisual()
    {
        if (visual != null) Destroy(visual.gameObject);
        var go = Models.BuildVisual(Type, Level);
        go.transform.SetParent(transform, false);
        visual = go.transform;
        baseScale = visual.localScale;
        visual.localScale = baseScale * (Building ? 0.3f : 1f);
        visualLevel = Level;
    }

    protected virtual void Update()
    {
        if (puppet) { PuppetUpdate(); return; }

        if (visualLevel != Level) RebuildVisual();

        float target = Building ? 0.3f : 1f;
        if (visual != null)
        {
            visual.localScale = Vector3.Lerp(visual.localScale, baseScale * target, 6f * Time.deltaTime);
        }

        // Show the world label while the player aims at this building (Hovered is
        // consumed every frame so it can't get "stuck" on after you look away), OR
        // whenever a special weapon is starved of metal — that warning shows always.
        bool hovered = Hovered;
        Hovered = false;
        bool warn = NoMetalWarning;
        bool showLabel = hovered || warn;
        if (label != null && label.gameObject.activeSelf != showLabel)
            label.gameObject.SetActive(showLabel);

        if (showLabel && label != null && Camera.main != null)
        {
            label.transform.rotation = Quaternion.LookRotation(label.transform.position - Camera.main.transform.position);
            if (Building)
            {
                float pct = Mathf.Clamp01(1f - (buildEnd - Time.time) / BuildTime) * 100f;
                label.text = $"BUILDING {Mathf.RoundToInt(pct)}%";
                label.color = new Color(1f, 0.7f, 0.2f);
            }
            else if (warn)
            {
                label.text = UsesReserve ? "RELOAD" : "NO METAL";
                label.color = new Color(1f, 0.3f, 0.25f);
            }
            else if (IsFunding)
            {
                // Combined metal+oil progress, so the label never reads 100% while oil's still due.
                int paid = FundingPaid + OilPaid, need = Mathf.Max(1, FundingRequired + OilRequired);
                int pct = Mathf.RoundToInt(100f * paid / need);
                bool oilStage = FundingPaid >= FundingRequired && OilPaid < OilRequired;
                label.text = oilStage ? Lang.T($"НЕФТЬ {pct}%", $"OIL {pct}%") : $"FUNDING {pct}%";
                label.color = oilStage ? new Color(1f, 0.8f, 0.3f) : new Color(0.4f, 0.8f, 1f);
            }
            else
            {
                string hp = NeedsRepair ? $"  {Mathf.Max(0, Mathf.RoundToInt(Health))}/{Mathf.RoundToInt(MaxHealth)}" : string.Empty;
                label.text = (Level > 1 ? $"LVL {Level}" : string.Empty) + hp;
                label.color = NeedsRepair ? new Color(1f, 0.5f, 0.3f) : Color.white;
            }
        }

        if (Building)
        {
            if (Time.time >= buildEnd)
            {
                Building = false;
                if (visual != null) visual.localScale = baseScale;
                OnActivated();
            }
            return;
        }

        if (Health <= 0f) { OnDeath(); return; }         // safety net: never linger at <=0 HP
        if (!IsFunding) BuildableTick();                  // special weapons stay dark until funded
    }

    protected virtual void ApplyLevel() { Health = MaxHealth; }
    protected virtual void OnActivated() { }
    protected virtual void BuildableTick() { }

    // ---- co-op puppet (client side): visuals/label only, driven by the host ----
    void PuppetUpdate()
    {
        if (visualLevel != Level) RebuildVisual();
        float target = Building ? 0.3f : 1f;
        if (visual != null) visual.localScale = Vector3.Lerp(visual.localScale, baseScale * target, 6f * Time.deltaTime);

        bool hovered = Hovered; Hovered = false;
        if (label != null && label.gameObject.activeSelf != hovered) label.gameObject.SetActive(hovered);
        if (hovered && label != null && Camera.main != null)
        {
            label.transform.rotation = Quaternion.LookRotation(label.transform.position - Camera.main.transform.position);
            string hp = NeedsRepair ? $"  {Mathf.Max(0, Mathf.RoundToInt(Health))}/{Mathf.RoundToInt(MaxHealth)}" : string.Empty;
            label.text = (Building ? "BUILDING" : (Level > 1 ? $"LVL {Level}" : string.Empty)) + hp;
            label.color = NeedsRepair ? new Color(1f, 0.5f, 0.3f) : Color.white;
        }
    }

    /// <summary>Client: adopt the host's authoritative state for this building.</summary>
    public void SetNetState(int level, float health, float maxHealth, bool building,
                            Vector3 pos, float yaw, bool doorOpen, int reserve, int funding)
    {
        Level = Mathf.Clamp(level, 1, MaxLevel);
        MaxHealth = maxHealth;
        Health = health;
        Building = building;
        Reserve = reserve;
        FundingPaid = funding;
        transform.position = pos;                                   // buildings are static — snapping is fine
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (this is Door d) d.SetOpen(doorOpen);
    }

    /// <summary>Host: apply an action requested by a client (0 invest, 1 fund, 2 reserve, 3 repair, 5 door).</summary>
    public void NetApply(int action, int amount)
    {
        switch (action)
        {
            case 0: Invest(amount); break;
            case 1: Fund(amount); break;
            case 2: Refill(amount); break;
            case 3: Repair(amount); break;
            case 5: (this as Door)?.Toggle(); break;
        }
    }

    /// <summary>Client: mirror the upgrade/fund cooldown locally so the UI gates re-sends.</summary>
    public void MarkNetCooldown() { upgReady = Time.time + UpgradeCooldown; }

    protected virtual void OnDeath()
    {
        if (dead) return; // idempotent — guards against a second lethal hit the same frame
        dead = true;
        foreach (var z in Zombie.All)
        {
            if ((z.transform.position - transform.position).sqrMagnitude < 2.25f) z.TakeDamage(30f);
        }
        Destroy(gameObject);
    }

    /// <summary>Invest metal toward the next level. Returns true if the metal was taken.</summary>
    public bool Invest(int amount)
    {
        if (!CanUpgrade || Time.time < upgReady) return false;
        Invested += amount;
        upgReady = Time.time + UpgradeCooldown; // pace between investments
        if (Invested >= UpgradeCost)
        {
            Invested -= UpgradeCost;            // carry any remainder
            Level++;
            ApplyLevel();                       // new level heals to full
            Effects.Upgrade(transform.position + Vector3.up * 0.7f);
        }
        return true;
    }

    /// <summary>Pour metal into a special weapon's construction. Capped per press by the
    /// caller and gated by the same cooldown as upgrades. Returns true if accepted.</summary>
    public bool Fund(int amount)
    {
        if (!IsFunding || amount <= 0 || Time.time < upgReady || FundingPaid >= FundingRequired) return false;
        upgReady = Time.time + UpgradeCooldown; // reuse the upgrade reload pacing
        FundingPaid = Mathf.Min(FundingRequired, FundingPaid + amount);
        CheckOnline();
        return true;
    }

    /// <summary>Pour oil (from the player's reserve) into a super-weapon's construction.
    /// Needed ON TOP of the metal funding before the weapon switches on.</summary>
    public bool FundOil(int amount)
    {
        if (!IsFunding || amount <= 0 || Time.time < upgReady || OilPaid >= OilRequired) return false;
        upgReady = Time.time + UpgradeCooldown;
        OilPaid = Mathf.Min(OilRequired, OilPaid + amount);
        CheckOnline();
        return true;
    }

    // The weapon only comes online once BOTH metal and oil are fully funded.
    void CheckOnline()
    {
        if (FundingPaid >= FundingRequired && OilPaid >= OilRequired)
            Effects.Upgrade(transform.position + Vector3.up * 1f); // "online" flourish
    }

    /// <summary>Restore a saved building: instantly built, at the given level / health /
    /// funding. Skips the build animation and runs activation (mines wire up, etc.).</summary>
    public void LoadState(int level, float health, int fundingPaid)
    {
        Level = Mathf.Clamp(level, 1, MaxLevel);
        ApplyLevel();                                  // sets MaxHealth for this level (and full Health)
        Health = Mathf.Clamp(health, 1f, MaxHealth);
        FundingPaid = Mathf.Clamp(fundingPaid, 0, FundingRequired);
        // Oil funding isn't persisted: a weapon saved with its metal funding complete was
        // online, so treat its oil as paid too (mid-funding saves just lose oil progress).
        OilPaid = FundingPaid >= FundingRequired ? OilRequired : 0;
        Building = false;
        buildEnd = Time.time;
        OnActivated();
    }

    /// <summary>Special weapons burn metal from their own reserve to fire. Returns false
    /// (firing nothing) when the reserve is empty, flashing a RELOAD warning.</summary>
    protected bool SpendMetal(int cost)
    {
        if (UsesReserve)
        {
            if (Reserve < cost) { noMetalUntil = Time.time + 1.5f; return false; }
            Reserve -= cost;
            return true;
        }
        // Fallback (no reserve configured): draw from the owner's wallet.
        if (owner == null || owner.Metal < cost)
        {
            noMetalUntil = Time.time + 1.5f;
            return false;
        }
        owner.AddMetal(-cost);
        return true;
    }

    /// <summary>Load metal into a special weapon's reserve. Returns the amount accepted.</summary>
    public int Refill(int amount)
    {
        int space = ReserveMax - Reserve;
        if (space <= 0 || amount <= 0) return 0;
        int give = Mathf.Min(amount, space);
        Reserve += give;
        return give;
    }

    public void Repair(float amount)
    {
        if (Health <= 0f) return;
        Health = Mathf.Min(MaxHealth, Health + amount);
    }

    public void TakeDamage(float amount)
    {
        if (Health <= 0f) return;
        Health -= amount;
        if (Health <= 0f) OnDeath();
    }
}
