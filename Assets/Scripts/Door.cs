using UnityEngine;

/// <summary>
/// A base door. Closed = solid barrier (blocks zombies). Press the interact key
/// (У / E) while looking at it to retract the leaf into the ground; press again
/// to close. A separate trigger collider keeps it click-able even while open.
/// </summary>
public class Door : Buildable
{
    bool isOpen;
    BoxCollider solid;

    protected override void Awake()
    {
        BuildCost = 40;
        MaxLevel = 3;
        BuildTime = 1.2f;
        base.Awake();

        // The root box (from Buildable.Create) is the solid blocker.
        solid = GetComponent<BoxCollider>();

        // Always-on trigger so the door can be aimed at / toggled even when open.
        var ig = new GameObject("Interact");
        ig.transform.SetParent(transform, false);
        var trig = ig.AddComponent<BoxCollider>();
        trig.isTrigger = true;
        trig.center = new Vector3(0f, 0.9f, 0f);
        trig.size = new Vector3(2.3f, 1.9f, 0.7f);
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            case 1: MaxHealth = 250f; break;
            case 2: MaxHealth = 400f; break;
            default: MaxHealth = 650f; break;
        }
        Health = MaxHealth;
    }

    public bool IsOpen => isOpen;

    public void Toggle()
    {
        if (Building) return;
        isOpen = !isOpen;
        if (solid != null) solid.enabled = !isOpen; // open => zombies can pass
    }

    /// <summary>Co-op: force the open state to match the host's authoritative door.</summary>
    public void SetOpen(bool open)
    {
        if (open == isOpen) return;
        isOpen = open;
        if (solid != null) solid.enabled = !isOpen;
    }

    protected override void Update()
    {
        base.Update();

        // Slide the leaf down when open, up when closed.
        if (visual != null)
        {
            float targetY = isOpen ? -1.9f : 0f;
            Vector3 lp = visual.localPosition;
            lp.y = Mathf.Lerp(lp.y, targetY, 8f * Time.deltaTime);
            visual.localPosition = lp;
        }
    }
}
