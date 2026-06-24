using UnityEngine;

/// <summary>ZvZ base "core" — a big destructible objective. Each side has one; destroying the
/// ENEMY core wins the match (losing your own = defeat). Team 0 = player (blue), 1 = enemy (red).</summary>
public class Core : MonoBehaviour
{
    public int team;
    public float MaxHealth = 2500f;
    public float Health;

    Transform crystal;

    public static Core Create(Vector3 groundPos, int team)
    {
        var go = new GameObject(team == 0 ? "PlayerCore" : "EnemyCore");
        if (GameBootstrap.World != null) go.transform.SetParent(GameBootstrap.World);
        go.transform.position = groundPos;
        var c = go.AddComponent<Core>();
        c.team = team;
        c.Health = c.MaxHealth;
        c.Build();
        return c;
    }

    void Build()
    {
        Color col = team == 0 ? new Color(0.3f, 0.6f, 1f) : new Color(1f, 0.35f, 0.3f);
        Prim(PrimitiveType.Cube, new Vector3(0f, 1.2f, 0f), new Vector3(4.2f, 2.4f, 4.2f), new Color(0.28f, 0.3f, 0.34f), true);   // pylon base (solid)
        crystal = Prim(PrimitiveType.Cube, new Vector3(0f, 4.0f, 0f), new Vector3(2.1f, 2.8f, 2.1f), col, false, new Vector3(45f, 0f, 45f)).transform; // glowing crystal
    }

    void Update()
    {
        if (crystal != null) crystal.Rotate(0f, 40f * Time.deltaTime, 0f, Space.World); // slow spin for juice
    }

    public void TakeDamage(float amount)
    {
        if (Health <= 0f) return;
        Health = Mathf.Max(0f, Health - amount);
        if (Health <= 0f)
        {
            Effects.AirBlast(transform.position + Vector3.up * 2.5f, 32f);
            Effects.AirBlast(transform.position + Vector3.up * 2.5f, 18f);
            if (ZvZManager.Instance != null) ZvZManager.Instance.OnCoreDestroyed(team);
            foreach (Transform ch in transform) Destroy(ch.gameObject); // rubble it visually
        }
    }

    GameObject Prim(PrimitiveType t, Vector3 lp, Vector3 ls, Color c, bool collider, Vector3 euler = default)
    {
        var g = GameObject.CreatePrimitive(t);
        if (!collider) Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(transform, false);
        g.transform.localPosition = lp;
        g.transform.localEulerAngles = euler;
        g.transform.localScale = ls;
        GameBootstrap.SetColor(g, c);
        return g;
    }
}
