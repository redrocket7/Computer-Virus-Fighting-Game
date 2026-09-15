using UnityEngine;

/// <summary>
/// Persists display / quality preferences via PlayerPrefs and applies them at boot.
/// </summary>
public static class GameSettingsStore
{
    const string PrefsQuality = "cvfg.settings.quality";
    const string PrefsFullscreen = "cvfg.settings.fullscreen";
    const string PrefsWidth = "cvfg.settings.width";
    const string PrefsHeight = "cvfg.settings.height";
    const string PrefsRefresh = "cvfg.settings.refresh";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplyOnBoot()
    {
        ApplySavedSettings();
    }

    public static void ApplySavedSettings()
    {
        int quality = PlayerPrefs.GetInt(PrefsQuality, QualitySettings.GetQualityLevel());
        quality = Mathf.Clamp(quality, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
        if (quality != QualitySettings.GetQualityLevel())
            QualitySettings.SetQualityLevel(quality, true);

        bool fullscreen = PlayerPrefs.GetInt(PrefsFullscreen, Screen.fullScreen ? 1 : 0) != 0;
        int width = PlayerPrefs.GetInt(PrefsWidth, Screen.width);
        int height = PlayerPrefs.GetInt(PrefsHeight, Screen.height);
        int refresh = PlayerPrefs.GetInt(PrefsRefresh, GetCurrentRefreshRate());

        if (width < 640 || height < 480)
        {
            width = Screen.width;
            height = Screen.height;
        }

        FullScreenMode mode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        var rate = new RefreshRate
        {
            numerator = (uint)Mathf.Max(1, refresh),
            denominator = 1
        };
        Screen.SetResolution(width, height, mode, rate);
    }

    public static int GetSavedQuality() =>
        PlayerPrefs.GetInt(PrefsQuality, QualitySettings.GetQualityLevel());

    public static void SetQuality(int index)
    {
        index = Mathf.Clamp(index, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
        QualitySettings.SetQualityLevel(index, true);
        PlayerPrefs.SetInt(PrefsQuality, index);
        PlayerPrefs.Save();
    }

    public static bool GetSavedFullscreen() =>
        PlayerPrefs.GetInt(PrefsFullscreen, Screen.fullScreen ? 1 : 0) != 0;

    public static void SetFullscreen(bool fullscreen)
    {
        FullScreenMode mode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        Screen.fullScreenMode = mode;
        PlayerPrefs.SetInt(PrefsFullscreen, fullscreen ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static void SetResolution(int width, int height, int refreshRate)
    {
        bool fullscreen = GetSavedFullscreen();
        FullScreenMode mode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        var rate = new RefreshRate
        {
            numerator = (uint)Mathf.Max(1, refreshRate),
            denominator = 1
        };
        Screen.SetResolution(width, height, mode, rate);
        PlayerPrefs.SetInt(PrefsWidth, width);
        PlayerPrefs.SetInt(PrefsHeight, height);
        PlayerPrefs.SetInt(PrefsRefresh, refreshRate);
        PlayerPrefs.Save();
    }

    public static int GetCurrentRefreshRate()
    {
        try
        {
            return Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
        }
        catch
        {
            return 60;
        }
    }
}
