using UnityEngine;
using System.Collections;

public class BeatDetector : MonoBehaviour
{
    public AudioSource audioSource;
    public CameraShake cameraShake;

    [Header("Beat Detection Settings")]
    public float sensitivity = 1.5f;     // Sensitivitas deteksi beat
    public int sampleDataLength = 1024;  // Panjang data sample
    public float cooldown = 0.2f;        // Waktu jeda antar beat

    private float[] audioSamples;
    private float timeSinceLastBeat;
    private float previousSum;

    void Start()
    {
        audioSamples = new float[sampleDataLength];
        timeSinceLastBeat = cooldown;
    }

    void Update()
    {
        if (!audioSource.isPlaying) return;

        audioSource.GetSpectrumData(audioSamples, 0, FFTWindow.BlackmanHarris);

        float sum = 0f;
        for (int i = 0; i < audioSamples.Length; i++)
        {
            sum += audioSamples[i];
        }

        // Deteksi beat ketika perubahan energi tiba-tiba tinggi
        if (sum > previousSum * sensitivity && timeSinceLastBeat >= cooldown)
        {
            OnBeatDetected();
            timeSinceLastBeat = 0;
        }

        previousSum = sum;
        timeSinceLastBeat += Time.deltaTime;
    }

    void OnBeatDetected()
    {
        Debug.Log("Beat Detected!");
        cameraShake.TriggerShake();
    }
}
