using UnityEngine;

/// <summary>Электрифицированная решётка — ловушка-забор. Как колючка (BarbedWire) её
/// коллайдер — триггер, поэтому зомби идут СКВОЗЬ и не атакуют её, но металлическая
/// сетка бьёт током: постоянный урон + замедление всем в узкой ДЛИННОЙ полосе (это
/// забор, а не круг — зона проверяется в локальных осях, вдоль X), плюс раз в ~1с —
/// усиленный РАЗРЯД. Прочнее и длиннее колючки. Уровни 1-3 растят урон, разряд,
/// замедление и прочность.</summary>
public class LatticeFence : Buildable
{
    float lenX = 2.6f;      // длина полосы вдоль локального X (в размер коллайдера/визуала)
    float halfZ = 0.45f;    // половина толщины зоны по Z
    float dps = 22f;        // постоянный ток по зомби в зоне
    float slowMul = 0.35f;  // множитель скорости внутри (ниже = медленнее)
    float zapEvery = 1.1f;  // период усиленного разряда, сек
    float zapDmg = 40f;     // урон разряда
    float zapTimer = 0f;

    public override bool IsTrap => true; // зомби игнорируют — идут сквозь

    protected override void Awake()
    {
        BuildCost = 35;
        MaxLevel = 3;
        BuildTime = 1.6f;
        base.Awake();
    }

    protected override void ApplyLevel()
    {
        switch (Mathf.Clamp(Level, 1, 3))
        {
            case 1: MaxHealth = 320f; dps = 22f; slowMul = 0.35f; zapDmg = 40f; zapEvery = 1.1f; break;
            case 2: MaxHealth = 480f; dps = 34f; slowMul = 0.28f; zapDmg = 65f; zapEvery = 0.9f; break;
            default: MaxHealth = 680f; dps = 50f; slowMul = 0.20f; zapDmg = 95f; zapEvery = 0.7f; break;
        }
        Health = MaxHealth;
    }

    protected override void BuildableTick()
    {
        zapTimer -= Time.deltaTime;
        bool zap = zapTimer <= 0f;
        if (zap) zapTimer = zapEvery;

        float hx = lenX * 0.5f;
        foreach (var z in Zombie.All)
        {
            // Проверяем зону в ЛОКАЛЬНЫХ осях забора: длинная полоса вдоль X, тонкая по Z.
            Vector3 lp = transform.InverseTransformPoint(z.transform.position);
            if (Mathf.Abs(lp.x) <= hx && Mathf.Abs(lp.z) <= halfZ)
            {
                z.Slow(slowMul, 0.5f);
                z.TakeDamage(dps * Time.deltaTime);
                if (zap) z.TakeDamage(zapDmg);   // усиленный разряд
            }
        }
    }
}
