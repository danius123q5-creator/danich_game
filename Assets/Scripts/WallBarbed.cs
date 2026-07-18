using UnityEngine;

/// <summary>Комбо: ДЛИННАЯ СТЕНА + КОЛЮЧКА перед собой в ~2 м. Стена (сплошной
/// барьер, HP как у длинной стены) плюс полоса колючей проволоки в 2 метрах
/// ПЕРЕД стеной — тормозит и медленно режет зомби на подходе. Одна постройка,
/// цена = длинная стена + колючка. 2026-07-18.</summary>
public class WallBarbed : Wall
{
    float wireFront   = 2.0f;   // центр полосы колючки, м впереди стены
    float wireHalfLen = 2.2f;   // полудлина полосы вдоль стены (длинная стена ~4.4)
    float wireHalfDep = 1.1f;   // полуглубина полосы по ходу
    float dps         = 22f;    // урон/сек по зомби в полосе
    float slowMul     = 0.4f;   // замедление в полосе

    protected override void Awake()
    {
        base.Awake();           // Wall: HP-стена, MaxLevel=3
        BuildCost = 55;         // длинная стена (45) + колючка (10)
    }

    protected override void ApplyLevel()
    {
        base.ApplyLevel();      // HP как у стены (550/950/1500 × уровень)
        // с уровнем — злее колючка
        switch (Mathf.Clamp(Level, 1, 3))
        {
            case 1: dps = 22f; slowMul = 0.35f; break;
            case 2: dps = 34f; slowMul = 0.28f; break;
            default: dps = 50f; slowMul = 0.20f; break;
        }
    }

    protected override void BuildableTick()
    {
        // Полоса колючки в ЛОКАЛЬНЫХ осях: |x| вдоль стены, z ~ +wireFront (перед).
        foreach (var z in Zombie.All)
        {
            Vector3 lp = transform.InverseTransformPoint(z.transform.position);
            if (Mathf.Abs(lp.x) <= wireHalfLen &&
                lp.z >= wireFront - wireHalfDep && lp.z <= wireFront + wireHalfDep)
            {
                z.Slow(slowMul, 0.5f);
                z.TakeDamage(dps * Time.deltaTime);
            }
        }
    }
}
