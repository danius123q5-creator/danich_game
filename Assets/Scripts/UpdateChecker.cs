using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>At launch, asks GitHub for the latest release tag and flags whether a newer
/// version exists. Runs once, non-blocking, and fails silently when offline (or when the
/// repo isn't reachable). The main menu reads UpdateAvailable / Latest / ReleasesUrl to
/// show a "download update" notice.</summary>
public class UpdateChecker : MonoBehaviour
{
    // GitHub "latest release" API for the repo. NOTE: this only returns data while the
    // repo (its releases) is reachable without auth — i.e. the repo is public. On a
    // private repo the request 404s and no update is ever shown (handled gracefully).
    const string ApiUrl = "https://api.github.com/repos/danius123q5-creator/danich_game/releases/latest";
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
        using (var req = UnityWebRequest.Get(ApiUrl))
        {
            req.SetRequestHeader("User-Agent", "ZombieShooter");
            req.SetRequestHeader("Accept", "application/vnd.github+json");
            req.timeout = 8;
            yield return req.SendWebRequest();
            Checked = true;
            if (req.result != UnityWebRequest.Result.Success) yield break;

            string tag = ParseTag(req.downloadHandler.text);
            if (string.IsNullOrEmpty(tag)) yield break;
            Latest = tag;
            UpdateAvailable = IsNewer(VersionNumber(tag), VersionNumber(GameVersion.Current));
        }
    }

    // Pull "tag_name":"danichgame1.4" out of the JSON without a full parser.
    static string ParseTag(string json)
    {
        const string key = "\"tag_name\"";
        int i = json.IndexOf(key);
        if (i < 0) return null;
        i = json.IndexOf('"', i + key.Length);
        if (i < 0) return null;
        int j = json.IndexOf('"', i + 1);
        return j > i ? json.Substring(i + 1, j - i - 1) : null;
    }

    // Keep only digits and dots, so a tag like "danichgame1.4" → "1.4".
    static string VersionNumber(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s) if (char.IsDigit(c) || c == '.') sb.Append(c);
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
