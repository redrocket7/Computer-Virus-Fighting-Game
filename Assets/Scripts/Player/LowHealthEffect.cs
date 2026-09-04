using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Low-health screen feedback: vignette, chromatic aberration, and digital glitch.
/// Uses a runtime Volume profile clone so the shared GameplayProfile asset is not dirtied.
/// </summary>
public class LowHealthEffect : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] PlayerController player;
    [SerializeField] Volume volume;

    [Header("Vignette")]
    [SerializeField] float maxVignetteIntensity = 0.55f;
    [SerializeField] float minSmoothness = 0.25f;
    [SerializeField] float maxSmoothness = 0.45f;
    [SerializeField] Color healthyColor = Color.black;
    [SerializeField] Color criticalColor = Color.black;

    [Header("Glitch")]
    [SerializeField] float maxGlitchIntensity = 0.7f;
    [SerializeField] float maxChromaticIntensity = 0.65f;
    [SerializeField] float maxFilmGrainIntensity = 0.55f;
    [Tooltip("How often glitch bursts try to fire at full damage (per second).")]
    [SerializeField] float burstAttemptsPerSecond = 7f;
    [SerializeField] float burstDecaySpeed = 4.5f;

    [Header("Shared")]
    [Tooltip("Higher = stays mild longer, then ramps harder near death.")]
    [SerializeField] float intensityPower = 1.35f;
    [SerializeField] float blendSpeed = 4f;

    Vignette vignette;
    ChromaticAberration chromatic;
    FilmGrain filmGrain;

    float damageAmount;
    float currentVignette;
    float currentGlitch;
    float currentChromatic;
    float currentGrain;
    float burstAmount;
    float nextBurstCheck;

    public static float GlitchIntensity { get; private set; }
    public static float GlitchBurst { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterSceneHook()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        EnsureExists();
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        EnsureExists();
    }

    public static void EnsureExists()
    {
        if (FindAnyObjectByType<LowHealthEffect>() != null)
            return;

        if (FindAnyObjectByType<PlayerController>() == null)
            return;

        var go = new GameObject("Low Health Effect");
        go.AddComponent<LowHealthEffect>();
    }

    bool boundToPlayer;

    void OnEnable()
    {
        BindToPlayer();
    }

    void Start()
    {
        BindToPlayer();
    }

    void OnDisable()
    {
        UnbindFromPlayer();
        GlitchIntensity = 0f;
        GlitchBurst = 0f;
    }

    void BindToPlayer()
    {
        PlayerController found = player != null
            ? player
            : FindAnyObjectByType<PlayerController>();

        if (found == null)
            return;

        if (boundToPlayer && player == found)
        {
            OnHealthChanged(player.CurrentHealth, player.MaxHealth);
            return;
        }

        UnbindFromPlayer();
        player = found;
        player.HealthChanged += OnHealthChanged;
        boundToPlayer = true;
        OnHealthChanged(player.CurrentHealth, player.MaxHealth);
    }

    void UnbindFromPlayer()
    {
        if (!boundToPlayer || player == null)
        {
            boundToPlayer = false;
            return;
        }

        player.HealthChanged -= OnHealthChanged;
        boundToPlayer = false;
    }

    void Awake()
    {
        if (player == null)
            player = FindAnyObjectByType<PlayerController>();

        if (volume == null)
            volume = FindAnyObjectByType<Volume>();

        EnsureEffects();
    }

    void OnDestroy()
    {
        GlitchIntensity = 0f;
        GlitchBurst = 0f;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float targetVignette = maxVignetteIntensity * damageAmount;
        float targetGlitch = maxGlitchIntensity * damageAmount;
        float targetChromatic = maxChromaticIntensity * damageAmount;
        float targetGrain = maxFilmGrainIntensity * damageAmount;

        currentVignette = Mathf.MoveTowards(currentVignette, targetVignette, blendSpeed * dt);
        currentGlitch = Mathf.MoveTowards(currentGlitch, targetGlitch, blendSpeed * dt);
        currentChromatic = Mathf.MoveTowards(currentChromatic, targetChromatic, blendSpeed * dt);
        currentGrain = Mathf.MoveTowards(currentGrain, targetGrain, blendSpeed * dt);

        TickBurst(dt);

        float vignetteT = maxVignetteIntensity > 0.001f
            ? Mathf.Clamp01(currentVignette / maxVignetteIntensity)
            : 0f;

        if (vignette != null)
        {
            vignette.intensity.Override(currentVignette);
            vignette.smoothness.Override(Mathf.Lerp(minSmoothness, maxSmoothness, vignetteT));
            vignette.color.Override(Color.Lerp(healthyColor, criticalColor, vignetteT));
        }

        if (chromatic != null)
        {
            chromatic.active = currentChromatic > 0.001f || burstAmount > 0.01f;
            chromatic.intensity.Override(Mathf.Clamp01(currentChromatic + burstAmount * 0.45f));
        }

        if (filmGrain != null)
        {
            filmGrain.active = currentGrain > 0.001f || burstAmount > 0.01f;
            filmGrain.intensity.Override(Mathf.Clamp01(currentGrain + burstAmount * 0.35f));
        }

        GlitchIntensity = Mathf.Clamp01(currentGlitch);
        GlitchBurst = Mathf.Clamp01(burstAmount);
    }

    void TickBurst(float dt)
    {
        burstAmount = Mathf.MoveTowards(burstAmount, 0f, burstDecaySpeed * dt);

        if (damageAmount < 0.08f)
            return;

        nextBurstCheck -= dt;
        if (nextBurstCheck > 0f)
            return;

        nextBurstCheck = 1f / Mathf.Max(0.5f, burstAttemptsPerSecond);

        // Low health = higher chance of a sharp digital spike.
        float chance = Mathf.Lerp(0.04f, 0.55f, damageAmount);
        if (Random.value > chance)
            return;

        burstAmount = Mathf.Max(burstAmount, Mathf.Lerp(0.25f, 1f, damageAmount) * Random.Range(0.7f, 1f));
    }

    void OnHealthChanged(float current, float max)
    {
        float healthFraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        float rawDamage = 1f - healthFraction;
        damageAmount = Mathf.Pow(rawDamage, Mathf.Max(0.01f, intensityPower));
    }

    void EnsureEffects()
    {
        if (volume == null)
            return;

        VolumeProfile profile = volume.profile;
        if (profile == null)
            return;

        if (!profile.TryGet(out vignette))
            vignette = profile.Add<Vignette>(true);

        vignette.active = true;
        vignette.intensity.overrideState = true;
        vignette.smoothness.overrideState = true;
        vignette.color.overrideState = true;
        vignette.center.Override(new Vector2(0.5f, 0.5f));
        vignette.rounded.Override(false);

        if (!profile.TryGet(out chromatic))
            chromatic = profile.Add<ChromaticAberration>(true);

        chromatic.intensity.overrideState = true;
        chromatic.intensity.Override(0f);

        if (!profile.TryGet(out filmGrain))
            filmGrain = profile.Add<FilmGrain>(true);

        filmGrain.type.Override(FilmGrainLookup.Medium1);
        filmGrain.intensity.overrideState = true;
        filmGrain.response.Override(0.8f);
        filmGrain.intensity.Override(0f);
    }
}
