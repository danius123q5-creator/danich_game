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

    const string ApiUrl = "https://api.github.com/repos/danius123q5-creator/danich_game/releases/latest";

    void Start() { StartCoroutine(Check()); }

    IEnumerator Check()
    {
        // Primary: follow the github.com releases/latest redirect and read the final tag URL
        // (no api.github.com, no rate limits). Fallback: the API's tag_name.
        yield return Resolve(LatestUrl, false);
        if (string.IsNullOrEmpty(Latest)) yield return Resolve(ApiUrl, true);

        Checked = true;
        if (!string.IsNullOrEmpty(Latest))
            UpdateAvailable = IsNewer(Latest, TrailingVersion(GameVersion.Current));
    }

    IEnumerator Resolve(string url, bool api)
    {
        using (var req = UnityWebRequest.Get(url))
        {
            req.SetRequestHeader("User-Agent", "ZombieShooter");
            if (api) req.SetRequestHeader("Accept", "application/vnd.github+json");
            req.timeout = 8; // follow redirects (default): the final URL lands on the tag page
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) yield break;

            if (!api)
            {
                string ver = TrailingVersion(req.url); // final URL after redirects = .../tag/danichgameX.Y
                if (!string.IsNullOrEmpty(ver) && ver.Contains(".")) Latest = ver;
            }
            else
            {
                string body = req.downloadHandler.text;
                int i = body.IndexOf("\"tag_name\"");
                if (i >= 0)
                {
                    int q1 = body.IndexOf('"', i + 10);
                    int q2 = q1 >= 0 ? body.IndexOf('"', q1 + 1) : -1;
                    if (q2 > q1) Latest = TrailingVersion(body.Substring(q1 + 1, q2 - q1 - 1));
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
