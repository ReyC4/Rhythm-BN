using UnityEngine;

public class CameraShake : MonoBehaviour
{
    [Header("Shake Settings")]
    public float shakeIntensity = 0.2f;
    public float shakeDuration = 0.1f;

    private Vector3 originalPosition;
    private float shakeTimer = 0;

    void Start()
    {
        originalPosition = transform.localPosition;
    }

    void Update()
    {
        if (shakeTimer > 0)
        {
            transform.localPosition = originalPosition + Random.insideUnitSphere * shakeIntensity;
            shakeTimer -= Time.deltaTime;
        }
        else
        {
            transform.localPosition = originalPosition;
        }
    }

    public void TriggerShake()
    {
        shakeTimer = shakeDuration;
    }
}
