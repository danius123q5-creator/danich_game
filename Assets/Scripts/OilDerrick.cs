using UnityEngine;

/// <summary>Oil derrick (НЕФТ. ВЫШКА) — a player-built oil source. Unlike a refinery you don't
/// capture it; you pay 870 metal to raise it and it pumps oil into its own pool. Connect a pipe
/// to it (within pipe range) and a doser draws oil straight from your own well.</summary>
public class OilDerrick : Buildable, IOilSource
{
    const float Cap = 280f;   // pool capacity
    const float Rate = 6f;    // oil/sec pumped

    float pool;
    Transform beam;           // the pumpjack walking beam (rocks up/down)
    float nod;

    protected override void OnEnable() { base.OnEnable(); OilSources.Add(this); }
    protected override void OnDisable() { base.OnDisable(); OilSources.Remove(this); }

    protected override void Awake()
    {
        BuildCost = 870;
        MaxLevel = 1;
        BuildTime = 4f;
        base.Awake();
    }

    protected override void ApplyLevel() { MaxHealth = 700f; Health = MaxHealth; }

    protected override void BuildableTick()
    {
        pool = Mathf.Min(Cap, pool + Rate * Time.deltaTime); // keep pumping while it stands

        // Rock the walking beam (find it once; it's rebuilt with the visual).
        if (beam == null) { foreach (var tr in GetComponentsInChildren<Transform>()) if (tr.name == "Beam") { beam = tr; break; } }
        if (beam != null) { nod += Time.deltaTime * 1.6f; beam.localRotation = Quaternion.Euler(Mathf.Sin(nod) * 13f, 0f, 0f); }
    }

    // IOilSource: a built (non-puppet) derrick yields oil from its pool.
    public bool OilActive => !Building && !IsPuppet;
    public Transform OilTransform => transform;
    public float DrawOil(float amount)
    {
        if (pool <= 0f || amount <= 0f) return 0f;
        float take = Mathf.Min(amount, pool);
        pool -= take;
        return take;
    }
}
