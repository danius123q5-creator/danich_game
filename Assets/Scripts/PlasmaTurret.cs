using UnityEngine;

/// <summary>ДЛС «Не далёкое будущее»: ПЛАЗМА-ТУРЕЛЬ повстанцев (захваченный плазма-тех
/// ЮпитерГаз). Стреляет быстрым ПРОБИВАЮЩИМ плазма-разрядом — цианный луч прошивает
/// ЦЕЛУЮ ЛИНИЮ врагов в узком коридоре до дальности (а не одну цель), поэтому топ против
/// колонн в коридорах базы. Работает сама, без нефти/патронов. По образцу Rpg.cs, но
/// не ракета-по-площади, а мгновенный луч-пробой. Аддитивно, ядро не трогает.</summary>
public class PlasmaTurret : Buildable
{
    float range = 30f;
    float rate = 0.5f;            // сек между разрядами
    float damage = 60f;
    float beamHalfWidth = 0.9f;   // полуширина коридора пробоя
    float next, nextScan;
    Zombie target;

    protected override void Awake()
    {
        BuildCost = 300;
        MaxLevel = 3;
        UpgradeCost = 220;
        BuildTime = 2.2f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            case 1: MaxHealth = 200f; rate = 0.50f; damage = 60f;  range = 30f; beamHalfWidth = 0.9f; break;
            case 2: MaxHealth = 260f; rate = 0.38f; damage = 95f;  range = 34f; beamHalfWidth = 1.1f; break;
            default:MaxHealth = 330f; rate = 0.28f; damage = 140f; range = 38f; beamHalfWidth = 1.3f; break;
        }
        damage *= ModRuntime.TurretDmgMult; // мод-множитель, как у остальных турелей
        Health = MaxHealth;
    }

    protected override void BuildableTick()
    {
        if (Time.time >= nextScan) { nextScan = Time.time + 0.25f; target = NearestInRange(); }
        if (target == null) return;

        // Довернуть на цель (yaw), как sentry/rpg.
        Vector3 aim = target.transform.position - transform.position; aim.y = 0f;
        if (aim.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(aim), 9f * Time.deltaTime);

        if (Time.time < next) return;
        next = Time.time + rate;
        FirePlasma();
    }

    void FirePlasma()
    {
        Vector3 muzzle = transform.position + transform.forward * 0.9f + Vector3.up * 1.0f;
        Vector3 dir = target.transform.position - muzzle; dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();
        Vector3 end = muzzle + dir * range;

        Effects.GunShot(muzzle);
        Effects.Tracer(muzzle, end);                          // цианный плазма-луч
        Effects.Burst(muzzle, new Color(0.5f, 1f, 1f), 5);    // вспышка дула

        // ПРОБОЙ: урон ВСЕМ врагам в узком коридоре вдоль луча (плазма прошивает линию).
        float hwSq = beamHalfWidth * beamHalfWidth;
        foreach (var z in Zombie.All)
        {
            if (z == null) continue;
            Vector3 to = z.transform.position - muzzle; to.y = 0f;
            float t = Vector3.Dot(to, dir);
            if (t < 0f || t > range) continue;                // позади/за дальностью
            Vector3 perp = to - dir * t;                      // поперечное отклонение от луча
            if (perp.sqrMagnitude <= hwSq)
            {
                z.TakeDamage(damage);
                Effects.Burst(z.transform.position + Vector3.up * 1f, new Color(0.6f, 1f, 1f), 4);
            }
        }
    }

    Zombie NearestInRange()
    {
        Zombie best = null;
        float bestSq = range * range;
        foreach (var z in Zombie.All)
        {
            if (z == null) continue;
            float d = (z.transform.position - transform.position).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = z; }
        }
        return best;
    }
}
