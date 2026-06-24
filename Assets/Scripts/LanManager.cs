using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Minimal LAN co-op foundation (Step 1). UDP-based: every peer sends its player
/// position to the host, and the host relays everyone's positions back to all peers.
/// Remote players show up as coloured capsule avatars. Zombies, base and waves are
/// NOT synced yet (the client's wave sim is paused) — that comes in later steps.
/// </summary>
public class LanManager : MonoBehaviour
{
    public static LanManager Instance;
    public const int Port = 56022;

    public bool Active { get; private set; }
    public bool IsHost { get; private set; }

    // ---- ZvZ (2.0) over LAN ----
    public bool ZvZActive;            // client: host reports we're in a ZvZ match
    public float ZvZCore0, ZvZCore1;  // client: latest core HPs streamed by the host
    public int ZvZWinner = -1;        // client: match result (-1 ongoing, else the winning team)
    public bool HostZvZActive;        // host: ZvZManager sets these; SendWorldState streams them
    public float HostZvZCore0, HostZvZCore1;
    public int HostZvZWinner = -1;
    int zvzSpawns;                    // host: queued "release my zombie" requests from the client

    UdpClient udp;
    Thread recvThread;
    volatile bool running;

    int myId;
    IPEndPoint hostEndpoint;                              // client: where to send
    readonly object gate = new object();

    struct NetState { public Vector3 pos; public float yaw; public int team; }
    readonly Dictionary<int, NetState> incoming = new Dictionary<int, NetState>(); // id -> latest (locked)
    readonly Dictionary<int, IPEndPoint> peers = new Dictionary<int, IPEndPoint>(); // host: id -> endpoint (locked)
    readonly Dictionary<int, GameObject> avatars = new Dictionary<int, GameObject>(); // main thread only

    // --- co-op world sync (host-authoritative zombies + waves) ---
    public Vector3[] RemotePlayers = new Vector3[0];          // host: remote player positions (main thread)
    struct Hit { public int netId; public float dmg; }
    readonly List<Hit> pendingHits = new List<Hit>();          // host: client→host hit reports (locked)
    string pendingSnapshot;                                    // client: latest zombie snapshot payload (locked)
    bool haveWave; int wWave; bool wPrep; float wLeft; int wAlive; int wMap; // client: latest wave state (locked)
    struct PHit { public int target; public float dmg; }
    readonly List<PHit> pendingPlayerHits = new List<PHit>();  // PvP: player-vs-player damage (locked)
    readonly Dictionary<int, Zombie> puppets = new Dictionary<int, Zombie>(); // client main thread
    readonly List<int> puppetGc = new List<int>();             // scratch for despawn (main thread)
    float zSendTimer;

    // building sync: clients request, host owns & streams back
    struct Place { public int type; public Vector3 pos; public float yaw; }
    struct Act { public int netId, action, amount; }
    readonly List<Place> pendingPlace = new List<Place>();     // host: client→host build requests (locked)
    readonly List<Act> pendingActs = new List<Act>();          // host: client→host edit requests (locked)
    string pendingBuildSnapshot;                               // client: latest building snapshot (locked)
    readonly Dictionary<int, Buildable> bpuppets = new Dictionary<int, Buildable>(); // client main thread
    readonly List<int> bpuppetGc = new List<int>();
    float bSendTimer;

    struct BirdMsg { public Vector3 start, dir; }
    readonly List<BirdMsg> pendingBirds = new List<BirdMsg>(); // client: cosmetic fly-overs (locked)

    // Generic effect/world-event replication (explosions, tracers, rockets, digging, ...).
    struct FxMsg { public string raw; public IPEndPoint from; }
    readonly List<FxMsg> pendingFx = new List<FxMsg>(); // locked

    PlayerController localPlayer;
    float sendTimer;

    void Awake()
    {
        Instance = this;
        myId = UnityEngine.Random.Range(1, int.MaxValue);
    }

    public void StartHost()
    {
        Shutdown();
        try
        {
            IsHost = true;
            udp = new UdpClient(Port);
            running = true; Active = true;
            recvThread = new Thread(RecvLoop) { IsBackground = true };
            recvThread.Start();
        }
        catch (Exception e) { Debug.LogWarning("LAN host failed: " + e.Message); Shutdown(); }
    }

    public bool StartClient(string ip)
    {
        Shutdown();
        try
        {
            IsHost = false;
            udp = new UdpClient(0); // ephemeral local port
            hostEndpoint = new IPEndPoint(IPAddress.Parse(ip.Trim()), Port);
            running = true; Active = true;
            recvThread = new Thread(RecvLoop) { IsBackground = true };
            recvThread.Start();
            return true;
        }
        catch (Exception e) { Debug.LogWarning("LAN join failed: " + e.Message); Shutdown(); return false; }
    }

    void RecvLoop()
    {
        var from = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                byte[] data = udp.Receive(ref from);
                Parse(Encoding.ASCII.GetString(data), from);
            }
            catch { if (!running) break; }
        }
    }

    // Messages are space-separated ASCII, tagged by the first token:
    //   P id x y z yaw            player position (peer ↔ host)
    //   Z a,k,x,y,z,yaw;b,...      zombie snapshot      (host → clients)
    //   W wave prep timeLeft alive wave/HUD state       (host → clients)
    //   H netid dmg               a hit to apply        (client → host)
    void Parse(string msg, IPEndPoint from)
    {
        if (string.IsNullOrEmpty(msg)) return;
        char tag = msg[0];
        var ci = CultureInfo.InvariantCulture;
        var f = msg.Split(' ');
        switch (tag)
        {
            case 'P': ParsePlayer(f, from, ci); break;
            case 'Z':
                if (!IsHost) lock (gate) { pendingSnapshot = msg.Length > 2 ? msg.Substring(2) : ""; }
                break;
            case 'W':
                if (!IsHost && f.Length >= 6) lock (gate)
                {
                    wWave = int.Parse(f[1], ci); wPrep = f[2] == "1";
                    wLeft = float.Parse(f[3], ci); wAlive = int.Parse(f[4], ci);
                    wMap = int.Parse(f[5], ci); haveWave = true;
                    if (f.Length >= 10) // ZvZ trailer: zvz core0 core1 winner
                    {
                        ZvZActive = f[6] == "1";
                        ZvZCore0 = float.Parse(f[7], ci);
                        ZvZCore1 = float.Parse(f[8], ci);
                        ZvZWinner = int.Parse(f[9], ci);
                    }
                }
                break;
            case 'G': // client → host: release one of my (team-1) zombies
                if (IsHost) lock (gate) zvzSpawns++;
                break;
            case 'H':
                if (IsHost && f.Length >= 3) lock (gate)
                {
                    pendingHits.Add(new Hit { netId = int.Parse(f[1], ci), dmg = float.Parse(f[2], ci) });
                }
                break;
            case 'B': // client → host: place a building   B type x y z yaw
                if (IsHost && f.Length >= 6) lock (gate)
                {
                    pendingPlace.Add(new Place
                    {
                        type = int.Parse(f[1], ci),
                        pos = new Vector3(float.Parse(f[2], ci), float.Parse(f[3], ci), float.Parse(f[4], ci)),
                        yaw = float.Parse(f[5], ci)
                    });
                }
                break;
            case 'U': // client → host: edit a building     U netid action amount
                if (IsHost && f.Length >= 4) lock (gate)
                {
                    pendingActs.Add(new Act { netId = int.Parse(f[1], ci), action = int.Parse(f[2], ci), amount = int.Parse(f[3], ci) });
                }
                break;
            case 'S': // host → client: building snapshot
                if (!IsHost) lock (gate) { pendingBuildSnapshot = msg.Length > 2 ? msg.Substring(2) : ""; }
                break;
            case 'X': // PvP player-vs-player hit (shooter → host → target)
                if (f.Length >= 3) lock (gate)
                {
                    pendingPlayerHits.Add(new PHit { target = int.Parse(f[1], ci), dmg = float.Parse(f[2], ci) });
                }
                break;
            case 'R': // host → client: cosmetic bird fly-over   R sx sy sz dx dy dz
                if (!IsHost && f.Length >= 7) lock (gate)
                {
                    pendingBirds.Add(new BirdMsg
                    {
                        start = new Vector3(float.Parse(f[1], ci), float.Parse(f[2], ci), float.Parse(f[3], ci)),
                        dir = new Vector3(float.Parse(f[4], ci), float.Parse(f[5], ci), float.Parse(f[6], ci))
                    });
                }
                break;
            case 'F': // effect / world event — play locally; host also relays to other peers
                lock (gate) pendingFx.Add(new FxMsg { raw = msg, from = IsHost ? new IPEndPoint(from.Address, from.Port) : null });
                break;
        }
    }

    void ParsePlayer(string[] f, IPEndPoint from, CultureInfo ci)
    {
        if (f.Length < 6) return;
        if (!int.TryParse(f[1], out int id) || id == myId) return;
        NetState n;
        n.pos = new Vector3(float.Parse(f[2], ci), float.Parse(f[3], ci), float.Parse(f[4], ci));
        n.yaw = float.Parse(f[5], ci);
        n.team = f.Length >= 7 ? int.Parse(f[6], ci) : 0;
        lock (gate)
        {
            incoming[id] = n;
            if (IsHost) peers[id] = new IPEndPoint(from.Address, from.Port);
        }
    }

    static string Pack(int id, Vector3 pos, float yaw, int team)
    {
        return string.Format(CultureInfo.InvariantCulture, "P {0} {1:0.##} {2:0.##} {3:0.##} {4:0.#} {5}",
            id, pos.x, pos.y, pos.z, yaw, team);
    }

    void Update()
    {
        if (!Active || udp == null) return;
        if (localPlayer == null) localPlayer = FindFirstObjectByType<PlayerController>();

        sendTimer += Time.unscaledDeltaTime;
        if (sendTimer >= 0.05f && localPlayer != null) // ~20 Hz
        {
            sendTimer = 0f;
            Broadcast();
        }
        ApplyRemote();

        if (IsHost)
        {
            RefreshRemotePlayers();
            ApplyPendingHits();
            ApplyPendingBuilds();
            zSendTimer += Time.unscaledDeltaTime;
            if (zSendTimer >= 0.1f) { zSendTimer = 0f; SendWorldState(); } // ~10 Hz
            bSendTimer += Time.unscaledDeltaTime;
            if (bSendTimer >= 0.25f) { bSendTimer = 0f; SendBuildState(); } // ~4 Hz (buildings are static)
        }
        else
        {
            ApplySnapshot();
            ApplyWave();
            ApplyBuildSnapshot();
            ApplyBirds();
        }

        ApplyPlayerHits(); // PvP damage (both host and clients)
        ApplyFx();         // replicated effects / world events
    }

    // ---- host: create / edit buildings requested by clients ----
    void ApplyPendingBuilds()
    {
        Place[] places = null; Act[] acts = null;
        lock (gate)
        {
            if (pendingPlace.Count > 0) { places = pendingPlace.ToArray(); pendingPlace.Clear(); }
            if (pendingActs.Count > 0) { acts = pendingActs.ToArray(); pendingActs.Clear(); }
        }
        if (places != null)
            foreach (var p in places)
            {
                var go = Buildable.Create(p.type, p.pos, Quaternion.Euler(0f, p.yaw, 0f), localPlayer);
                if (GameRoot.IsZvZ && go != null) { var bb = go.GetComponent<Buildable>(); if (bb != null) bb.Team = 1; } // client = side 1
            }
        if (acts != null)
            foreach (var a in acts)
            {
                var b = FindBuildable(a.netId);
                if (b == null) continue;
                if (a.action == 4) Destroy(b.gameObject); // sell
                else b.NetApply(a.action, a.amount);
            }
    }

    static Buildable FindBuildable(int netId)
    {
        foreach (var b in Buildable.All)
            if (b.NetId == netId && !b.IsPuppet) return b;
        return null;
    }

    // ---- host → clients: full building snapshot ----
    void SendBuildState()
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("S ");
        bool first = true;
        foreach (var b in Buildable.All)
        {
            if (b.IsPuppet) continue;
            if (!first) sb.Append(';');
            first = false;
            var p = b.transform.position;
            int open = (b is Door d && d.IsOpen) ? 1 : 0;
            sb.AppendFormat(ci, "{0},{1},{2:0.##},{3:0.##},{4:0.##},{5:0.#},{6},{7:0},{8:0},{9},{10},{11},{12}",
                b.NetId, b.Type, p.x, p.y, p.z, b.transform.eulerAngles.y,
                b.Level, b.Health, b.MaxHealth, b.Building ? 1 : 0, open, b.Reserve, b.FundingPaid);
        }
        byte[] data = Encoding.ASCII.GetBytes(sb.ToString());
        lock (gate)
            foreach (var kv in peers) { try { udp.Send(data, data.Length, kv.Value); } catch { } }
    }

    // ---- client: reconcile puppet buildings against the latest snapshot ----
    void ApplyBuildSnapshot()
    {
        string payload;
        lock (gate) { payload = pendingBuildSnapshot; pendingBuildSnapshot = null; }
        if (payload == null) return;

        var ci = CultureInfo.InvariantCulture;
        var seen = new HashSet<int>();
        if (payload.Length > 0)
        {
            foreach (var e in payload.Split(';'))
            {
                if (e.Length == 0) continue;
                var c = e.Split(',');
                if (c.Length < 13) continue;
                int netId = int.Parse(c[0], ci);
                int type = int.Parse(c[1], ci);
                var pos = new Vector3(float.Parse(c[2], ci), float.Parse(c[3], ci), float.Parse(c[4], ci));
                float yaw = float.Parse(c[5], ci);
                int level = int.Parse(c[6], ci);
                float hp = float.Parse(c[7], ci), mhp = float.Parse(c[8], ci);
                bool building = c[9] == "1";
                bool door = c[10] == "1";
                int reserve = int.Parse(c[11], ci);
                int funding = int.Parse(c[12], ci);
                seen.Add(netId);
                if (!bpuppets.TryGetValue(netId, out var b) || b == null)
                {
                    b = Buildable.CreatePuppet(netId, type, pos, Quaternion.Euler(0f, yaw, 0f));
                    bpuppets[netId] = b;
                }
                b.SetNetState(level, hp, mhp, building, pos, yaw, door, reserve, funding);
            }
        }
        bpuppetGc.Clear();
        foreach (var kv in bpuppets)
            if (!seen.Contains(kv.Key)) { if (kv.Value != null) Destroy(kv.Value.gameObject); bpuppetGc.Add(kv.Key); }
        foreach (var id in bpuppetGc) bpuppets.Remove(id);
    }

    /// <summary>Client → host: request to place a building.</summary>
    public void SendBuildPlace(int type, Vector3 groundPos, float yaw)
    {
        if (!Active || IsHost || udp == null) return;
        try
        {
            byte[] b = Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture,
                "B {0} {1:0.##} {2:0.##} {3:0.##} {4:0.#}", type, groundPos.x, groundPos.y, groundPos.z, yaw));
            udp.Send(b, b.Length, hostEndpoint);
        }
        catch { }
    }

    /// <summary>Client → host: request to edit a building (0 invest,1 fund,2 reserve,3 repair,4 sell,5 door).</summary>
    public void SendBuildAction(int netId, int action, int amount)
    {
        if (!Active || IsHost || udp == null) return;
        try
        {
            byte[] b = Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture,
                "U {0} {1} {2}", netId, action, amount));
            udp.Send(b, b.Length, hostEndpoint);
        }
        catch { }
    }

    /// <summary>Client → host: request to release one of my (team-1) zombies.</summary>
    public void SendZvZSpawn()
    {
        if (!Active || IsHost || udp == null) return;
        try { byte[] b = Encoding.ASCII.GetBytes("G"); udp.Send(b, b.Length, hostEndpoint); } catch { }
    }

    /// <summary>Host: drain queued client zombie-release requests since the last call.</summary>
    public int TakeZvZSpawns() { lock (gate) { int n = zvzSpawns; zvzSpawns = 0; return n; } }

    // ---- host: expose remote player positions for zombie targeting ----
    void RefreshRemotePlayers()
    {
        lock (gate)
        {
            if (RemotePlayers.Length != incoming.Count) RemotePlayers = new Vector3[incoming.Count];
            int i = 0;
            foreach (var kv in incoming) RemotePlayers[i++] = kv.Value.pos;
        }
    }

    // ---- host: apply hits reported by clients to the authoritative zombies ----
    void ApplyPendingHits()
    {
        Hit[] hits = null;
        lock (gate) { if (pendingHits.Count > 0) { hits = pendingHits.ToArray(); pendingHits.Clear(); } }
        if (hits == null) return;
        foreach (var h in hits)
        {
            var z = FindZombie(h.netId);
            if (z != null) z.TakeDamage(h.dmg);
        }
    }

    static Zombie FindZombie(int netId)
    {
        foreach (var z in Zombie.All)
            if (z.NetId == netId && !z.IsPuppet) return z;
        return null;
    }

    // ---- host → clients: zombie snapshot + wave/HUD state ----
    void SendWorldState()
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("Z ");
        bool first = true;
        foreach (var z in Zombie.All)
        {
            if (z.IsPuppet) continue;
            if (!first) sb.Append(';');
            first = false;
            var p = z.transform.position;
            sb.AppendFormat(ci, "{0},{1},{2:0.#},{3:0.#},{4:0.#},{5:0.#}",
                z.NetId, (int)z.kind, p.x, p.y, p.z, z.transform.eulerAngles.y);
        }
        byte[] zb = Encoding.ASCII.GetBytes(sb.ToString());

        var gm = GameManager.Instance;
        byte[] wb = Encoding.ASCII.GetBytes(string.Format(ci, "W {0} {1} {2:0.#} {3} {4} {5} {6:0.#} {7:0.#} {8}",
            gm != null ? gm.WaveNumber : 0,
            gm != null && gm.IsPrep ? 1 : 0,
            gm != null ? gm.PhaseTimeLeft : 0f,
            gm != null ? gm.ZombiesLeft : 0,
            GameBootstrap.MapVariant,
            HostZvZActive ? 1 : 0, HostZvZCore0, HostZvZCore1, HostZvZWinner)); // ZvZ trailer

        lock (gate)
        {
            foreach (var kv in peers)
            {
                try { udp.Send(zb, zb.Length, kv.Value); if (wb != null) udp.Send(wb, wb.Length, kv.Value); }
                catch { }
            }
        }
    }

    // ---- client: reconcile puppet zombies against the latest snapshot ----
    void ApplySnapshot()
    {
        string payload;
        lock (gate) { payload = pendingSnapshot; pendingSnapshot = null; }
        if (payload == null) return; // nothing new this frame

        var ci = CultureInfo.InvariantCulture;
        var seen = new HashSet<int>();
        if (payload.Length > 0)
        {
            foreach (var e in payload.Split(';'))
            {
                if (e.Length == 0) continue;
                var c = e.Split(',');
                if (c.Length < 6) continue;
                int netId = int.Parse(c[0], ci);
                int kind = int.Parse(c[1], ci);
                var pos = new Vector3(float.Parse(c[2], ci), float.Parse(c[3], ci), float.Parse(c[4], ci));
                float yaw = float.Parse(c[5], ci);
                seen.Add(netId);
                if (!puppets.TryGetValue(netId, out var z) || z == null)
                {
                    z = Zombie.CreatePuppet(netId, (Zombie.Kind)kind, pos);
                    puppets[netId] = z;
                }
                z.SetNet(pos, yaw);
            }
        }
        // Despawn puppets the host no longer reports (they died / left).
        puppetGc.Clear();
        foreach (var kv in puppets)
            if (!seen.Contains(kv.Key)) { if (kv.Value != null) Destroy(kv.Value.gameObject); puppetGc.Add(kv.Key); }
        foreach (var id in puppetGc) puppets.Remove(id);
    }

    // ---- client: adopt host wave/HUD state (and the host's map) ----
    void ApplyWave()
    {
        bool have; int w; bool prep; float left; int alive; int map;
        lock (gate) { have = haveWave; w = wWave; prep = wPrep; left = wLeft; alive = wAlive; map = wMap; haveWave = false; }
        if (!have) return;

        // Map-sync: the host dictates the map. If ours differs, rebuild the world once so
        // terrain lines up (zombies/buildings stream in by absolute host coordinates).
        if (GameBootstrap.MapVariant != map && GameBootstrap.World != null)
        {
            GameBootstrap.MapVariant = map;
            GameBootstrap.DestroyWorld();
            GameBootstrap.BuildWorld();
            return; // puppets re-create from the next snapshots
        }

        var gm = GameManager.Instance;
        if (gm != null) gm.ApplyNetWave(w, prep, left, alive);
    }

    /// <summary>Client → host: report damage dealt to a host-owned zombie.</summary>
    public void SendZombieHit(int netId, float dmg)
    {
        if (!Active || IsHost || udp == null) return;
        try
        {
            byte[] b = Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture, "H {0} {1:0.#}", netId, dmg));
            udp.Send(b, b.Length, hostEndpoint);
        }
        catch { }
    }

    /// <summary>PvP: deal damage to another player. The host routes it to the target;
    /// a client sends it to the host, who forwards it.</summary>
    public void SendPlayerHit(int targetId, float dmg)
    {
        if (!Active || udp == null) return;
        if (IsHost)
        {
            if (targetId == myId) { if (localPlayer != null) localPlayer.TakeDamage(dmg); return; }
            IPEndPoint ep; lock (gate) { peers.TryGetValue(targetId, out ep); }
            SendX(ep, targetId, dmg);
        }
        else
        {
            SendX(hostEndpoint, targetId, dmg);
        }
    }

    /// <summary>Host → clients: replicate a cosmetic bird fly-over.</summary>
    public void SendBird(Vector3 start, Vector3 dir)
    {
        if (!Active || !IsHost || udp == null) return;
        byte[] b = Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture,
            "R {0:0.#} {1:0.#} {2:0.#} {3:0.##} {4:0.##} {5:0.##}", start.x, start.y, start.z, dir.x, dir.y, dir.z));
        lock (gate)
            foreach (var kv in peers) { try { udp.Send(b, b.Length, kv.Value); } catch { } }
    }

    void ApplyBirds()
    {
        BirdMsg[] arr = null;
        lock (gate) { if (pendingBirds.Count > 0) { arr = pendingBirds.ToArray(); pendingBirds.Clear(); } }
        if (arr == null) return;
        foreach (var m in arr) Bird.SpawnCosmetic(m.start, m.dir);
    }

    // ---- generic effect / world-event replication ----
    void SendFx(string payload)
    {
        if (!Active || udp == null) return;
        byte[] b = Encoding.ASCII.GetBytes(payload);
        if (IsHost) { lock (gate) foreach (var kv in peers) { try { udp.Send(b, b.Length, kv.Value); } catch { } } }
        else { try { udp.Send(b, b.Length, hostEndpoint); } catch { } }
    }

    public void FxPoint(char code, Vector3 p)
        => SendFx(string.Format(CultureInfo.InvariantCulture, "F {0} {1:0.##} {2:0.##} {3:0.##}", code, p.x, p.y, p.z));
    public void FxLine(Vector3 a, Vector3 b)
        => SendFx(string.Format(CultureInfo.InvariantCulture, "F T {0:0.##} {1:0.##} {2:0.##} {3:0.##} {4:0.##} {5:0.##}", a.x, a.y, a.z, b.x, b.y, b.z));
    public void FxRocket(Vector3 s, Vector3 t)
        => SendFx(string.Format(CultureInfo.InvariantCulture, "F K {0:0.##} {1:0.##} {2:0.##} {3:0.##} {4:0.##} {5:0.##}", s.x, s.y, s.z, t.x, t.y, t.z));
    public void FxDig(Vector3 p, float radius, float depth)
        => SendFx(string.Format(CultureInfo.InvariantCulture, "F D {0:0.##} {1:0.##} {2:0.##} {3:0.##} {4:0.##}", p.x, p.y, p.z, radius, depth));
    public void FxAirBlast(Vector3 p, float radius)
        => SendFx(string.Format(CultureInfo.InvariantCulture, "F A {0:0.##} {1:0.##} {2:0.##} {3:0.##}", p.x, p.y, p.z, radius));

    void ApplyFx()
    {
        FxMsg[] arr = null;
        lock (gate) { if (pendingFx.Count > 0) { arr = pendingFx.ToArray(); pendingFx.Clear(); } }
        if (arr == null) return;
        foreach (var m in arr)
        {
            PlayFx(m.raw);
            if (IsHost && m.from != null) // relay a client's event to the OTHER peers
            {
                byte[] b = Encoding.ASCII.GetBytes(m.raw);
                lock (gate)
                    foreach (var kv in peers)
                        if (!kv.Value.Equals(m.from)) { try { udp.Send(b, b.Length, kv.Value); } catch { } }
            }
        }
    }

    void PlayFx(string msg)
    {
        var f = msg.Split(' ');
        if (f.Length < 5) return;
        var ci = CultureInfo.InvariantCulture;
        char code = f[1][0];
        Vector3 P(int i) => new Vector3(float.Parse(f[i], ci), float.Parse(f[i + 1], ci), float.Parse(f[i + 2], ci));

        Effects.NetSuppress = true; // replaying — don't echo back onto the network
        try
        {
            switch (code)
            {
                case 'X': Effects.Explosion(P(2)); break;
                case 'G': Effects.GunShot(P(2)); break;
                case 'S': Effects.TurretShot(P(2)); break;
                case 'Z': Effects.Zap(P(2)); break;
                case 'C': Effects.CannonFire(P(2)); break;
                case 'U': Effects.Upgrade(P(2)); break;
                case 'I': Effects.Dirt(P(2)); break;
                case 'T': if (f.Length >= 8) Effects.Tracer(P(2), P(5)); break;
                case 'K': if (f.Length >= 8) Rocket.LaunchCosmetic(P(2), P(5)); break;
                case 'A': if (f.Length >= 6) Effects.AirBlast(P(2), float.Parse(f[5], ci)); break;
                case 'D': if (f.Length >= 7) GameBootstrap.Dig(P(2), float.Parse(f[5], ci), float.Parse(f[6], ci)); break;
            }
        }
        catch { }
        finally { Effects.NetSuppress = false; }
    }

    void SendX(IPEndPoint to, int targetId, float dmg)
    {
        if (to == null) return;
        try
        {
            byte[] b = Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture, "X {0} {1:0.#}", targetId, dmg));
            udp.Send(b, b.Length, to);
        }
        catch { }
    }

    // ---- apply / route PvP hits on the main thread ----
    void ApplyPlayerHits()
    {
        PHit[] arr = null;
        lock (gate) { if (pendingPlayerHits.Count > 0) { arr = pendingPlayerHits.ToArray(); pendingPlayerHits.Clear(); } }
        if (arr == null) return;
        foreach (var h in arr)
        {
            if (h.target == myId) { if (localPlayer != null) localPlayer.TakeDamage(h.dmg); }
            else if (IsHost) { IPEndPoint ep; lock (gate) { peers.TryGetValue(h.target, out ep); } SendX(ep, h.target, h.dmg); }
        }
    }

    void Broadcast()
    {
        var p = localPlayer.transform;
        byte[] mine = Encoding.ASCII.GetBytes(Pack(myId, p.position, p.eulerAngles.y, GameRoot.PvpTeam));
        try
        {
            if (IsHost)
            {
                lock (gate)
                {
                    foreach (var kv in peers)
                    {
                        udp.Send(mine, mine.Length, kv.Value);              // my state to this peer
                        foreach (var s in incoming)                          // + every other peer's state
                        {
                            if (s.Key == kv.Key) continue;
                            byte[] rb = Encoding.ASCII.GetBytes(Pack(s.Key, s.Value.pos, s.Value.yaw, s.Value.team));
                            udp.Send(rb, rb.Length, kv.Value);
                        }
                    }
                }
            }
            else
            {
                udp.Send(mine, mine.Length, hostEndpoint);
            }
        }
        catch { /* peer gone; ignore */ }
    }

    void ApplyRemote()
    {
        lock (gate)
        {
            foreach (var kv in incoming)
            {
                if (!avatars.TryGetValue(kv.Key, out var go) || go == null)
                {
                    go = MakeAvatar();
                    avatars[kv.Key] = go;
                }
                float k = 14f * Time.unscaledDeltaTime;
                go.transform.position = Vector3.Lerp(go.transform.position, kv.Value.pos, k);
                go.transform.rotation = Quaternion.Slerp(go.transform.rotation, Quaternion.Euler(0f, kv.Value.yaw, 0f), k);

                // Tag the avatar with its id/team; recolour ally vs enemy in PvP.
                var rp = go.GetComponent<RemotePlayer>();
                if (rp != null)
                {
                    rp.Id = kv.Key; rp.Team = kv.Value.team;
                    if (GameRoot.IsPvp && rp.ColoredTeam != rp.Team && go.transform.childCount > 0)
                    {
                        bool ally = rp.Team == GameRoot.PvpTeam;
                        GameBootstrap.SetColor(go.transform.GetChild(0).gameObject,
                            ally ? new Color(0.3f, 0.6f, 1f) : new Color(0.9f, 0.25f, 0.2f));
                        rp.ColoredTeam = rp.Team;
                    }
                }
            }
        }
    }

    static GameObject MakeAvatar()
    {
        var go = new GameObject("RemotePlayer");
        if (GameBootstrap.World != null) go.transform.SetParent(GameBootstrap.World);
        go.AddComponent<RemotePlayer>();

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule); // centred on the root (matches player origin)
        body.transform.SetParent(go.transform, false);
        body.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
        // Keep the collider as a trigger so the player can SHOOT this avatar (raycast hits
        // triggers) for PvP, without it physically shoving the local CharacterController.
        var bc = body.GetComponent<Collider>(); if (bc != null) bc.isTrigger = true;
        GameBootstrap.SetColor(body, new Color(0.9f, 0.4f, 0.3f));

        var head = GameObject.CreatePrimitive(PrimitiveType.Cube); // nub shows facing direction
        head.transform.SetParent(go.transform, false);
        head.transform.localPosition = new Vector3(0f, 0.55f, 0.25f);
        head.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
        UnityEngine.Object.Destroy(head.GetComponent<Collider>());
        GameBootstrap.SetColor(head, new Color(0.95f, 0.6f, 0.5f));
        return go;
    }

    public void Shutdown()
    {
        running = false;
        try { udp?.Close(); } catch { }
        udp = null;
        Active = false;
        IsHost = false;
        lock (gate)
        {
            incoming.Clear(); peers.Clear(); pendingHits.Clear(); pendingSnapshot = null; haveWave = false;
            pendingPlace.Clear(); pendingActs.Clear(); pendingBuildSnapshot = null; pendingPlayerHits.Clear();
            pendingBirds.Clear(); pendingFx.Clear();
        }
        foreach (var kv in avatars) if (kv.Value != null) Destroy(kv.Value);
        avatars.Clear();
        foreach (var kv in puppets) if (kv.Value != null) Destroy(kv.Value.gameObject);
        puppets.Clear();
        foreach (var kv in bpuppets) if (kv.Value != null) Destroy(kv.Value.gameObject);
        bpuppets.Clear();
        RemotePlayers = new Vector3[0];
    }

    void OnDestroy() { Shutdown(); }
    void OnApplicationQuit() { Shutdown(); }
}

/// <summary>Marker on a remote player's avatar: who it is and which team, so the local
/// player's weapons can damage enemies (PvP) and the avatar can be coloured ally/enemy.</summary>
public class RemotePlayer : MonoBehaviour
{
    public int Id;
    public int Team;
    public int ColoredTeam = -1;
}
