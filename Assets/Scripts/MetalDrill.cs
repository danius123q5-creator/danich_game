using UnityEngine;

/// <summary>Drilling rig (БУРОВАЯ) — a player-built metal source, the metal twin of the oil
/// derrick. Pay metal to raise it and it drills ore into its own pool. Connect a conveyor to it
/// (within conveyor range) and a metal vat draws ore straight from your own rig — no mine capture.</summary>
public class MetalDrill : Buildable, IMetalSource
{
    const float Cap = 300f;   // pool capacity
    const float Rate = 7f;    // ore/sec drilled

    float pool;
    Transform bit;            // the spinning drill bit
    float spin;

    protected override void OnEnable() { base.OnEnable(); MetalSources.Add(this); }
    protected override void OnDisable() { base.OnDisable(); MetalSources.Remove(this); }

    protected override void Awake()
    {
        BuildCost = 820;
        MaxLevel = 1;
        BuildTime = 4f;
        base.Awake();
    }

    protected override void ApplyLevel() { MaxHealth = 700f; Health = MaxHealth; }

    protected override void BuildableTick()
    {
        pool = Mathf.Min(Cap, pool + Rate * Time.deltaTime); // keep drilling while it stands

        if (bit == null) { foreach (var tr in GetComponentsInChildren<Transform>()) if (tr.name == "Bit") { bit = tr; break; } }
        if (bit != null) { spin += Time.deltaTime * 220f; bit.localRotation = Quaternion.Euler(0f, spin, 0f); }
    }

    // IMetalSource: a built (non-puppet) rig yields ore from its pool.
    public bool MetalActive => !Building && !IsPuppet;
    public Transform MetalTransform => transform;
    public float DrawMetal(float amount)
    {
        if (pool <= 0f || amount <= 0f) return 0f;
        float take = Mathf.Min(amount, pool);
        pool -= take;
        return take;
    }
}
