using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>At launch, finds the latest release version and flags whether a newer one exists.
/// Resolves it via the github.com "releases/latest" REDIRECT (it 302s to .../releases/tag/danichgameX.Y)
/// rather than api.github.com — same host as releases/download, and no API rate limits. Runs once,
/// non-blocking, and fails silently when offline. The main menu reads UpdateAvailable / Latest /
/// ReleasesUrl to show the "download update" notice.</summary>
public class UpdateChecker : MonoBehaviour
{
    const string LatestUrl = "https://github.com/danius123q5-creator/danich_game/releases/latest";
    public const string ReleasesUrl = "https://github.com/danius123q5-creator/danich_game/releases";

    public static bool Checked { get; private set; }
    public static bool UpdateAvailable { get; private set; }
    public static string Latest { get; private set; } = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("UpdateChecker");
        DontDestroyOnLoad(go);
        go.AddComponent<UpdateChecker>();
    }

    void Start() { StartCoroutine(Check()); }

    IEnumerator Check()
    {
        for (int attempt = 0; attempt < 3 && string.IsNullOrEmpty(Latest); attempt++)
        {
            using (var req = UnityWebRequest.Get(LatestUrl))
            {
                req.SetRequestHeader("User-Agent", "ZombieShooter");
                req.redirectLimit = 0;   // don't follow — we only want the Location header (the tag URL)
                req.timeout = 8;
                yield return req.SendWebRequest();
                Checked = true;

                // releases/latest 302s to .../releases/tag/danichgameX.Y — pull the version off it.
                string loc = req.GetResponseHeader("Location");
                if (string.IsNullOrEmpty(loc)) loc = req.url; // fallback if a proxy already resolved it
                string ver = TrailingVersion(loc);
                if (!string.IsNullOrEmpty(ver))
                {
                    Latest = ver;
                    UpdateAvailable = IsNewer(Latest, TrailingVersion(GameVersion.Current));
                    yield break;
                }
            }
        }
    }

    // Trailing run of digits/dots, e.g. ".../tag/danichgame2.0" -> "2.0", "danichgame1.4" -> "1.4".
    static string TrailingVersion(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder();
        for (int i = s.Length - 1; i >= 0; i--)
        {
            char c = s[i];
            if (char.IsDigit(c) || c == '.') sb.Insert(0, c);
            else if (sb.Length > 0) break; // stop once the version run ends
        }
        return sb.ToString().Trim('.');
    }

    // Compare dotted version numbers segment by segment (1.10 > 1.4).
    static bool IsNewer(string remote, string local)
    {
        var a = remote.Split('.');
        var b = local.Split('.');
        int n = Mathf.Max(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            int ai = i < a.Length && int.TryParse(a[i], out int x) ? x : 0;
            int bi = i < b.Length && int.TryParse(b[i], out int y) ? y : 0;
            if (ai != bi) return ai > bi;
        }
        return false;
    }
}
