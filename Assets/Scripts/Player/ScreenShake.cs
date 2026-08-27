using UnityEngine;

/// <summary>
/// Adds a decaying shake after the camera follow has positioned the camera.
/// Call ScreenShake.Shake from any gameplay effect.
/// </summary>
[DefaultExecutionOrder(1000)]
public class ScreenShake : MonoBehaviour
{
    static ScreenShake instance;

    float remaining;
    float duration;
    float positionStrength;
    float rotationStrength;
    bool sustained;
    float sustainedPositionStrength;
    float sustainedRotationStrength;

    void Awake()
    {
        instance = this;
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    void LateUpdate()
    {
        if (remaining > 0f)
            ApplyImpulseShake();

        if (sustained)
            ApplySustainedShake();
    }

    void ApplyImpulseShake()
    {
        remaining = Mathf.Max(0f, remaining - Time.unscaledDeltaTime);
        if (remaining <= 0f)
        {
            duration = 0f;
            positionStrength = 0f;
            rotationStrength = 0f;
            return;
        }

        float falloff = duration > 0f ? remaining / duration : 0f;
        falloff *= falloff;

        ApplyNoise(positionStrength * falloff, rotationStrength * falloff);
    }

    void ApplySustainedShake()
    {
        ApplyNoise(sustainedPositionStrength, sustainedRotationStrength);
    }

    void ApplyNoise(float positionalStrength, float rotationalStrength)
    {
        if (positionalStrength <= 0f && rotationalStrength <= 0f)
            return;

        Vector2 positionNoise = Random.insideUnitCircle * positionalStrength;
        transform.position += transform.right * positionNoise.x + transform.up * positionNoise.y;

        Vector3 rotationNoise = Random.insideUnitSphere * rotationalStrength;
        transform.rotation *= Quaternion.Euler(rotationNoise);
    }

    static void EnsureInstance()
    {
        if (instance != null)
            return;

        Camera camera = Camera.main;
        if (camera == null)
            return;

        instance = camera.GetComponent<ScreenShake>();
        if (instance == null)
            instance = camera.gameObject.AddComponent<ScreenShake>();
    }

    public static void Shake(float shakeDuration, float positionalStrength, float rotationalStrength = 0f)
    {
        if (shakeDuration <= 0f || (positionalStrength <= 0f && rotationalStrength <= 0f))
            return;

        EnsureInstance();
        if (instance == null)
            return;

        instance.remaining = Mathf.Max(instance.remaining, shakeDuration);
        instance.duration = Mathf.Max(instance.duration, shakeDuration);
        instance.positionStrength = Mathf.Max(instance.positionStrength, positionalStrength);
        instance.rotationStrength = Mathf.Max(instance.rotationStrength, rotationalStrength);
    }

    public static void BeginSustain(float positionalStrength, float rotationalStrength = 0f)
    {
        if (positionalStrength <= 0f && rotationalStrength <= 0f)
            return;

        EnsureInstance();
        if (instance == null)
            return;

        instance.sustained = true;
        instance.sustainedPositionStrength = positionalStrength;
        instance.sustainedRotationStrength = rotationalStrength;
    }

    public static void SetSustain(float positionalStrength, float rotationalStrength = 0f)
    {
        if (instance == null)
            return;

        instance.sustainedPositionStrength = positionalStrength;
        instance.sustainedRotationStrength = rotationalStrength;
    }

    public static void EndSustain()
    {
        if (instance == null)
            return;

        instance.sustained = false;
        instance.sustainedPositionStrength = 0f;
        instance.sustainedRotationStrength = 0f;
    }
}
