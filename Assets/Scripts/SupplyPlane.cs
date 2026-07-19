using UnityEngine;

/// <summary>
/// Кукурузник (Ан-2): с 37-й волны биплан пролетает над игроком и на closest-approach
/// сбрасывает на парашюте жирный ящик снабжения (нефть + металл). Летит выше и медленнее
/// птиц. Хост/оффлайн (кооп-клиенты его не видят — ресурсы получает хост).
/// </summary>
public class SupplyPlane : MonoBehaviour
{
    const float FlyHeight = 46f;
    const float MaxTravel = 340f;

    Vector3 dir;
    float speed = 30f;
    float traveled;
    Transform prop;
    PlayerController target;
    bool dropped;
    float lastDistSq = float.MaxValue;

    /// <summary>Хост/оффлайн: запустить пролёт кукурузника над игроком.</summary>
    public static void SpawnOver(PlayerController player)
    {
        if (player == null) return;
        float ang = Random.value * Mathf.PI * 2f;
        var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
        Vector3 start = player.transform.position + Vector3.up * FlyHeight - dir * 180f;

        var root = new GameObject("SupplyPlane");
        if (GameBootstrap.World != null) root.transform.SetParent(GameBootstrap.World);
        root.transform.position = start;
        root.transform.rotation = Quaternion.LookRotation(dir);
        var p = root.AddComponent<SupplyPlane>();
        p.dir = dir;
        p.target = player;
        p.BuildModel();
    }

    void BuildModel()
    {
        Color body = new Color(0.32f, 0.42f, 0.20f);  // хаки/олива Ан-2
        Color dark = new Color(0.18f, 0.24f, 0.12f);
        Color wing = new Color(0.36f, 0.46f, 0.24f);

        // фюзеляж
        var fus = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(fus.GetComponent<Collider>());
        fus.transform.SetParent(transform, false);
        fus.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // лежит носом вперёд
        fus.transform.localScale = new Vector3(1.1f, 3.2f, 1.1f);
        GameBootstrap.SetColor(fus, body);

        // биплан: верхнее и нижнее крыло
        MakeWing(new Vector3(0f, 1.1f, 0.4f), 9f, wing);   // верхнее
        MakeWing(new Vector3(0f, -0.4f, 0.4f), 8f, wing);  // нижнее (чуть короче)

        // стойки между крыльями
        MakeStrut(new Vector3(-2.6f, 0.35f, 0.4f), dark);
        MakeStrut(new Vector3(2.6f, 0.35f, 0.4f), dark);

        // хвост: киль + стабилизатор
        var fin = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(fin.GetComponent<Collider>());
        fin.transform.SetParent(transform, false);
        fin.transform.localScale = new Vector3(0.2f, 1.4f, 1.4f);
        fin.transform.localPosition = new Vector3(0f, 0.6f, -3.4f);
        GameBootstrap.SetColor(fin, body);
        var stab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(stab.GetComponent<Collider>());
        stab.transform.SetParent(transform, false);
        stab.transform.localScale = new Vector3(3.4f, 0.16f, 1.2f);
        stab.transform.localPosition = new Vector3(0f, 0f, -3.4f);
        GameBootstrap.SetColor(stab, body);

        // винт на носу (крутится)
        prop = new GameObject("Prop").transform;
        prop.SetParent(transform, false);
        prop.localPosition = new Vector3(0f, 0f, 3.6f);
        var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(blade.GetComponent<Collider>());
        blade.transform.SetParent(prop, false);
        blade.transform.localScale = new Vector3(0.15f, 3.0f, 0.15f);
        GameBootstrap.SetColor(blade, dark);
    }

    void MakeWing(Vector3 pos, float span, Color c)
    {
        var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(w.GetComponent<Collider>());
        w.transform.SetParent(transform, false);
        w.transform.localScale = new Vector3(span, 0.16f, 1.5f);
        w.transform.localPosition = pos;
        GameBootstrap.SetColor(w, c);
    }

    void MakeStrut(Vector3 pos, Color c)
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(s.GetComponent<Collider>());
        s.transform.SetParent(transform, false);
        s.transform.localScale = new Vector3(0.12f, 1.5f, 0.12f);
        s.transform.localPosition = pos;
        GameBootstrap.SetColor(s, c);
    }

    void Update()
    {
        transform.position += dir * speed * Time.deltaTime;
        traveled += speed * Time.deltaTime;

        if (prop != null) prop.localRotation = Quaternion.Euler(0f, 0f, Time.time * 1400f);

        // Сброс ящика на ближайшем подлёте к игроку (когда начинаем удаляться).
        if (!dropped && target != null)
        {
            Vector3 a = transform.position; a.y = 0f;
            Vector3 t = target.transform.position; t.y = 0f;
            float dsq = (a - t).sqrMagnitude;
            if (traveled > 40f && dsq > lastDistSq) { dropped = true; SupplyCrate.Drop(transform.position, target); }
            lastDistSq = dsq;
        }

        if (traveled >= MaxTravel) Destroy(gameObject);
    }
}
