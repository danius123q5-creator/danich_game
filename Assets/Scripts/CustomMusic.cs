using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Plays a user-supplied music file on loop across the whole game. Set the path in
/// Settings ("Своя музыка"). Persisted in PlayerPrefs. Supports MP3/OGG/WAV — OGG/WAV are the most
/// reliable to decode at runtime on desktop; MP3 is attempted too.</summary>
public class CustomMusic : MonoBehaviour
{
    public static CustomMusic Instance;
    AudioSource src;
    public string Status = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance != null) return;
        var go = new GameObject("CustomMusic");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<CustomMusic>();
        Instance.src = go.AddComponent<AudioSource>();
        Instance.src.loop = true;
        Instance.src.volume = PlayerPrefs.GetFloat("custom_music_vol", 0.6f);
        string p = PlayerPrefs.GetString("custom_music_path", "");
        if (!string.IsNullOrEmpty(p)) Instance.Play(p);
    }

    public void Play(string path)
    {
        path = (path ?? "").Trim().Trim('"');
        PlayerPrefs.SetString("custom_music_path", path);
        PlayerPrefs.Save();
        StopAllCoroutines();
        if (string.IsNullOrEmpty(path)) { if (src != null) src.Stop(); Status = ""; return; }
        StartCoroutine(Load(path));
    }

    public void Stop()
    {
        StopAllCoroutines();
        if (src != null) src.Stop();
        Status = Lang.T("выключено", "off");
    }

    public float Volume => src != null ? src.volume : 0.6f;
    public void SetVolume(float v)
    {
        if (src != null) src.volume = Mathf.Clamp01(v);
        PlayerPrefs.SetFloat("custom_music_vol", Mathf.Clamp01(v));
    }

    IEnumerator Load(string path)
    {
        Status = Lang.T("загрузка…", "loading…");
        if (!File.Exists(path)) { Status = Lang.T("файл не найден", "file not found"); yield break; }

        string ext = Path.GetExtension(path).ToLowerInvariant();
        AudioType at = ext == ".ogg" ? AudioType.OGGVORBIS
                     : ext == ".wav" ? AudioType.WAV
                     : AudioType.MPEG; // .mp3 and everything else
        string url = "file:///" + path.Replace("\\", "/");

        using (var uwr = UnityWebRequestMultimedia.GetAudioClip(url, at))
        {
            yield return uwr.SendWebRequest();
            if (uwr.result != UnityWebRequest.Result.Success)
            {
                Status = Lang.T("ошибка: ", "error: ") + uwr.error;
                yield break;
            }
            var clip = DownloadHandlerAudioClip.GetContent(uwr);
            if (clip == null || clip.length < 0.05f)
            {
                Status = Lang.T("не декодируется — попробуй .ogg или .wav", "can't decode — try .ogg or .wav");
                yield break;
            }
            src.clip = clip;
            src.Play();
            Status = Lang.T("играет: ", "playing: ") + Path.GetFileName(path);
        }
    }
}
