using UnityEngine;

/// <summary>
/// Ящик снабжения, сброшенный кукурузником (SupplyPlane). Плавно опускается на парашюте,
/// приземляется на рельеф, светит маячком и подбирается при подходе игрока — даёт большой
/// запас НЕФТИ и МЕТАЛЛА. Хост/оффлайн.
/// </summary>
public class SupplyCrate : MonoBehaviour
{
    // Щедрый груз — на 37+ волне почти доверху пополняет запас (значения клампятся по капу).
    const int MetalReward = 2500;
    const int OilReward   = 1800;

    const float FallSpeed = 7f;     // мягкий спуск на парашюте
    const float PickupRadius = 4.5f;
    const float MaxLife = 120f;     // страховка: не висит на карте вечно

    PlayerController target;
    Transform chute, beacon;
    bool landed;
    float groundY, age;

    public static void Drop(Vector3 pos, PlayerController forPlayer)
    {
        var root = new GameObject("SupplyCrate");
        if (GameBootstrap.World != null) root.transform.SetParent(GameBootstrap.World);
        root.transform.position = pos;
        var c = root.AddComponent<SupplyCrate>();
        c.target = forPlayer;
        c.groundY = GameBootstrap.Hill(pos.x, pos.z);
        c.BuildModel();
    }

    void BuildModel()
    {
        Color wood = new Color(0.45f, 0.30f, 0.15f);
        Color band = new Color(0.85f, 0.70f, 0.15f); // жёлтая маркировка

        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(box.GetComponent<Collider>());
        box.transform.SetParent(transform, false);
        box.transform.localScale = new Vector3(1.6f, 1.4f, 1.6f);
        box.transform.localPosition = new Vector3(0f, 0.7f, 0f);
        GameBootstrap.SetColor(box, wood);

        // накрест жёлтые полосы
        var b1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(b1.GetComponent<Collider>());
        b1.transform.SetParent(transform, false);
        b1.transform.localScale = new Vector3(1.7f, 0.28f, 1.7f);
        b1.transform.localPosition = new Vector3(0f, 0.7f, 0f);
        GameBootstrap.SetColor(b1, band);

        // маячок сверху — видно, куда упало
        beacon = new GameObject("Beacon").transform;
        beacon.SetParent(transform, false);
        beacon.localPosition = new Vector3(0f, 1.5f, 0f);
        var mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(mark.GetComponent<Collider>());
        mark.transform.SetParent(beacon, false);
        mark.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
        GameBootstrap.SetColor(mark, new Color(0.2f, 1f, 0.4f));

        // парашют (пропадёт при приземлении)
        chute = new GameObject("Chute").transform;
        chute.SetParent(transform, false);
        chute.localPosition = new Vector3(0f, 3.6f, 0f);
        var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(canopy.GetComponent<Collider>());
        canopy.transform.SetParent(chute, false);
        canopy.transform.localScale = new Vector3(3.2f, 1.6f, 3.2f);
        GameBootstrap.SetColor(canopy, new Color(0.85f, 0.85f, 0.82f));
    }

    void Update()
    {
        age += Time.deltaTime;

        if (!landed)
        {
            transform.position += Vector3.down * FallSpeed * Time.deltaTime;
            if (transform.position.y <= groundY)
            {
                var p = transform.position; p.y = groundY; transform.position = p;
                landed = true;
                if (chute != null) Destroy(chute.gameObject);   // купол «сдулся»
                Effects.Burst(transform.position + Vector3.up * 0.5f, new Color(0.6f, 0.45f, 0.2f), 12); // пыль
            }
        }
        else
        {
            // лёгкое покачивание маячка для заметности
            if (beacon != null) beacon.localPosition = new Vector3(0f, 1.5f + Mathf.Sin(age * 3f) * 0.15f, 0f);
        }

        // Подбор: игрок подошёл близко (естественно ждёт, пока ящик опустится к земле).
        if (target != null && !target.IsDead)
        {
            Vector3 a = transform.position; a.y = 0f;
            Vector3 t = target.transform.position; t.y = 0f;
            if ((a - t).sqrMagnitude < PickupRadius * PickupRadius)
            {
                Collect();
                return;
            }
        }

        if (age > MaxLife || transform.position.y < -30f) Destroy(gameObject);
    }

    void Collect()
    {
        target.AddMetal(MetalReward);
        target.AddOil(OilReward);
        Effects.Upgrade(transform.position + Vector3.up * 0.7f);                       // динь + искры
        Effects.Burst(transform.position + Vector3.up * 0.7f, new Color(0.2f, 1f, 0.4f), 26);
        Destroy(gameObject);
    }
}
