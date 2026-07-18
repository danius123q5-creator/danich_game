using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Bright-stylized look, set up entirely from code (the game has no scene assets):
///  • a global post-processing Volume — Bloom, ACES tonemapping, punchy colour grading, vignette;
///  • soft shadows on the sun;
/// Call <see cref="Apply"/> once after the world's sun is created, and
/// <see cref="EnablePostFx"/> on the gameplay camera so the Volume actually renders.
/// </summary>
public static class VisualFx
{
    /// <summary>World-level setup: sunset sky/mood + soft shadows + the global post-processing volume.</summary>
    public static void Apply(Transform worldParent, Light sun)
    {
        StyleLight(sun);
        SetupSunsetSky(sun);
        CreateGlobalVolume(worldParent);
    }

    /// <summary>Sunset / apocalypse mood: a warm procedural sky, a low orange sun, and a
    /// crimson haze so distant hills melt into the horizon. Uses Unity's built-in procedural
    /// skybox shader (always shipped — no custom shader to get stripped from the build).</summary>
    static void SetupSunsetSky(Light sun)
    {
        // НЕБО = реальное фото поля (Resources/game_sky) как ПАНОРАМНЫЙ скайбокс —
        // видно во все стороны. Нет фото/шейдера → fallback на синее процедурное.
        // Свет переведён с оранжевого заката на ЯСНЫЙ ДЕНЬ под фото. 2026-07-13.
        var photo = Resources.Load<Texture2D>("game_sky");
        var pano = Shader.Find("Skybox/Panoramic");
        // ⚠️ Плоское фото как панорамный скайбокс = «небо сбоку» (фото стоит стеной, не куполом).
        // Поэтому используем ПРОЦЕДУРНЫЙ купол (синь сверху → светлее к горизонту, верх=верх). 2026-07-13.
        if (false && photo != null && pano != null)
        {
            var m = new Material(pano);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", photo);
            if (m.HasProperty("_Tex")) m.SetTexture("_Tex", photo);
            if (m.HasProperty("_Mapping")) m.SetFloat("_Mapping", 1);     // Latitude-Longitude
            if (m.HasProperty("_ImageType")) m.SetFloat("_ImageType", 0); // 360
            if (m.HasProperty("_Exposure")) m.SetFloat("_Exposure", 1.0f);
            if (m.HasProperty("_Tint")) m.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f)); // 0.5 = без подкраски
            RenderSettings.skybox = m;
        }
        else
        {
            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                var sky = new Material(skyShader);
                if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", new Color(0.45f, 0.62f, 0.90f)); // ясный день (было закат)
                if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", new Color(0.35f, 0.40f, 0.45f));
                if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", 1.0f);
                if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", 1.25f);
                RenderSettings.skybox = sky;
            }
        }
        RenderSettings.sun = sun;

        if (sun != null)
        {
            sun.color = new Color(1f, 0.97f, 0.90f);           // нейтральный дневной свет (было оранжевый)
            sun.intensity = 1.25f;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f); // выше = день, тени короче
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.50f, 0.54f, 0.60f); // прохладный дневной подсвет (было тёплый тусклый)
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.66f, 0.74f, 0.86f);     // светло-голубая дымка (было crimson)
        RenderSettings.fogDensity = 0.006f;                           // легче — день ясный

        DynamicGI.UpdateEnvironment();
    }

    static void StyleLight(Light sun)
    {
        if (sun == null) return;
        sun.shadows = LightShadows.Soft;   // soft edges read better than hard primitive shadows
        sun.shadowStrength = 0.65f;
        sun.shadowNormalBias = 0.4f;
    }

    static void CreateGlobalVolume(Transform parent)
    {
        var go = new GameObject("PostFX Volume");
        if (parent != null) go.transform.SetParent(parent);

        var volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;

        // Build the profile at runtime (no .asset on disk).
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.sharedProfile = profile;

        // Bloom — bright surfaces and tracers/explosions glow. The heart of the "juicy" look.
        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(1.1f);
        bloom.threshold.Override(0.9f);
        bloom.scatter.Override(0.7f);

        // ACES tonemapping — filmic curve, keeps bright explosions from clipping to flat white.
        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.ACES);

        // Colour grading — extra contrast + saturation for a vivid, stylized palette.
        var color = profile.Add<ColorAdjustments>(true);
        color.contrast.Override(12f);
        color.saturation.Override(20f);
        color.postExposure.Override(0.1f);

        // Gentle vignette to focus the eye toward the centre of the action.
        var vig = profile.Add<Vignette>(true);
        vig.intensity.Override(0.28f);
        vig.smoothness.Override(0.45f);
    }

    /// <summary>Turn on post-processing + anti-aliasing for a camera (otherwise the
    /// global Volume above has no effect on what that camera renders).</summary>
    public static void EnablePostFx(Camera cam)
    {
        if (cam == null) return;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data == null) return;
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.Medium;
    }
}
