using UnityEngine;

/// <summary>Комбо: ОБЫЧНАЯ СТЕНА + ТУРЕЛЬ. Автотурель (наследует всю логику
/// прицела/стрельбы Sentry), но с толстым стена-коллайдером (блокирует зомби как
/// стена) и повышенным HP. Одна постройка, цена = стена + турель. 2026-07-18.</summary>
public class WallTurret : Sentry
{
    protected override void Awake()
    {
        base.Awake();           // Sentry: MaxLevel=3, стрельба
        BuildCost = 115;        // стена (25) + турель (90)
    }

    protected override void ApplyLevel()
    {
        base.ApplyLevel();      // турельные стата (урон/скорострел/дальность) + Sentry HP
        // + стена: солидный прирост HP, чтобы держала натиск как барьер
        MaxHealth += 500f + 350f * Mathf.Clamp(Level, 1, 3);
        MaxHealth *= ModRuntime.WallHpMult;   // тот же мод-множитель, что и у стен
        Health = MaxHealth;
    }
}
