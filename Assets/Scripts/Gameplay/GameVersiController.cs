using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;
using UnityEngine.SceneManagement;

public class GameVersiController : MonoBehaviour
{
    [Header("Gameplay Settings")]
    public MusicController musicController;
    public GameObject buttonPrefab;
    public TMP_Text scoreLabel;
    public float delayBeforeScoreScene = 2f;
    public string gameDataFileName;

    [Header("Config")]
    public bool loadDefaultData = true;

    [Header("Sync Settings")]
    [Tooltip("Positif: tombol muncul lebih cepat. Negatif: muncul lebih lambat. (ms)")]
    public float spawnLeadTimeMs = 0f;

    [Header("Timing")]
    [Tooltip("Jeda sebelum game dimulai setelah semua siap (detik).")]
    public float delayBeforeStart = 2f;

    [Tooltip("Lebar timing window tiap tombol (ms) → dikirim ke ButtonController.duration")]
    public float timingWindowMs = 800f;

    private int currentScore = 0;
    private int roundedButtonCount;
    private SortedList<float, ButtonItem> gameButtons = new SortedList<float, ButtonItem>();

    private bool gameRunning = false;
    private bool endSequenceStarted = false;

    private VideoPlayer videoPlayer;

    // ==== PAUSE FLAG ====
    private bool isPaused = false;
    public bool IsPaused => isPaused;

    void Start()
    {
        ButtonController.OnClicked += OnGameButtonClick;

        videoPlayer = FindObjectOfType<VideoPlayer>();

        if (loadDefaultData)
        {
            StartCoroutine(LoadGameData());
        }
    }

    void Update()
    {
        if (!gameRunning) return;
        if (musicController == null || musicController.audio == null) return;

        // Saat pause: hentikan SPAWN tombol baru (tombol yang sudah ada dibekukan via ButtonController.SetPaused)
        if (isPaused) return;

        if (!musicController.audio.isPlaying) return;

        // Master clock pakai audio time (detik → ms), plus offset lead
        float currentTimeMs = (musicController.audio.time * 1000f) + spawnLeadTimeMs;

        // (Opsional) Pastikan video sedang play
        if (videoPlayer != null && !videoPlayer.isPlaying)
            videoPlayer.Play();

        // Spawn tombol
        while (gameButtons.Count > 0 && currentTimeMs > gameButtons.Keys[0])
        {
            float keyTime = gameButtons.Keys[0];
            ButtonItem data = gameButtons[keyTime];

            int buttonNum = 4 - Mathf.Abs(roundedButtonCount) % 4;

            CreateButton(currentTimeMs, data, buttonNum);

            if (data.isDrag)
                roundedButtonCount--;

            gameButtons.RemoveAt(0);
            roundedButtonCount--;
        }

        // Habis semua → pindah ke score (sekali saja)
        if (!endSequenceStarted && gameButtons.Count == 0)
        {
            endSequenceStarted = true;
            StartCoroutine(HandleGameEnd());
        }
    }

    public void StartGame(List<ButtonItem> customButtons = null)
    {
        if (customButtons != null)
        {
            SetupButtons(customButtons);
        }

        StartCoroutine(StartGameCoroutine());
    }

    private IEnumerator StartGameCoroutine()
    {
        endSequenceStarted = false;

        // Pastikan state resume (kalau sebelumnya pause)
        ButtonController.SetPaused(false);
        isPaused = false;

        if (videoPlayer == null) videoPlayer = FindObjectOfType<VideoPlayer>();

        // Delay sebelum mulai
        yield return new WaitForSeconds(delayBeforeStart);

        // Reset & play audio
        if (musicController != null && musicController.audio != null)
        {
            musicController.audio.time = 0f;
            musicController.audio.Play();

            // tunggu sampai benar-benar playing
            while (!musicController.audio.isPlaying)
                yield return null;
        }

        // Reset & play video
        if (videoPlayer != null)
        {
            videoPlayer.time = 0f;
            videoPlayer.Play();
        }

        gameRunning = true;
        Debug.Log("🎵 Game (custom) started.");
    }

    private IEnumerator LoadGameData()
    {
        string filePath = Path.Combine(Application.streamingAssetsPath, gameDataFileName);
        string json = "";

#if UNITY_WEBGL
        UnityWebRequest req = UnityWebRequest.Get(filePath);
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Failed to load game data: " + req.error);
            yield break;
        }
        json = req.downloadHandler.text;
#else
        if (!File.Exists(filePath))
        {
            Debug.LogError("File not found: " + filePath);
            yield break;
        }
        json = File.ReadAllText(filePath);
#endif

        ButtonData data = JsonUtility.FromJson<ButtonData>(json);
        SetupButtons(data.buttons);
        StartGame();
    }

    private void SetupButtons(List<ButtonItem> buttons)
    {
        gameButtons.Clear();

        foreach (var b in buttons)
        {
            // Pastikan key unik untuk SortedList
            float t = b.time;
            while (gameButtons.ContainsKey(t)) t += 0.001f;
            gameButtons.Add(t, b);
        }

        roundedButtonCount = CalculateButtonCount();
        Debug.Log($"✅ Loaded {buttons.Count} custom buttons.");
    }

    private void CreateButton(float currentTimeMs, ButtonItem data, int buttonNum)
    {
        GameObject obj = Instantiate(buttonPrefab, Vector3.zero, Quaternion.identity);

        Transform parent = null;
        var holder = GameObject.FindGameObjectWithTag("GameController");
        parent = holder != null ? holder.transform : transform;

        obj.transform.SetParent(parent, false);

        var ctrl = obj.GetComponent<ButtonController>();
        if (ctrl == null)
        {
            Debug.LogError("❌ buttonPrefab tidak memiliki ButtonController!");
            Destroy(obj);
            return;
        }

        if (ctrl.startButtonText != null) ctrl.startButtonText.text = buttonNum.ToString();
        if (data.isDrag && ctrl.endButtonText != null) ctrl.endButtonText.text = (buttonNum + 1).ToString();

        // Timing window (ms)
        ctrl.duration = timingWindowMs;

        // currentTimeMs dipass tapi tidak dipakai (ButtonController pakai Time.time)
        ctrl.InitializeButton(currentTimeMs, data.position[0], data.position[1], data.isDrag, data.endPosition[0], data.endPosition[1]);

        // Jika dibuat saat pause, langsung freeze (ikuti flag global)
        if (isPaused)
            ButtonController.SetPaused(true);
    }

    private void OnGameButtonClick(ButtonController btn)
    {
        int scoreGain = Mathf.RoundToInt((btn.buttonScore * 1000) / 100) * 100;
        currentScore += scoreGain;

        if (scoreLabel != null)
            scoreLabel.text = currentScore.ToString();
    }

    private int CalculateButtonCount()
    {
        int count = gameButtons.Count;
        int nearestMultiple = Mathf.RoundToInt(count / 4f) * 4;
        return nearestMultiple - 1;
    }

    private IEnumerator HandleGameEnd()
    {
        gameRunning = false;

        yield return new WaitForSeconds(delayBeforeScoreScene);
        yield return StartCoroutine(FadeOutMusic(1f));

        PlayerPrefs.SetInt("FinalScore", currentScore);
        PlayerPrefs.SetString("PreviousScene", SceneManager.GetActiveScene().name);
        PlayerPrefs.Save();

        Debug.Log("🎮 Game end, loading score scene...");
        SceneManager.LoadScene("ScoreScene");
    }

    private IEnumerator FadeOutMusic(float duration)
    {
        AudioSource audio = musicController != null ? musicController.audio : null;
        if (audio == null)
        {
            Debug.LogError("AudioSource not found!");
            yield break;
        }

        float startVolume = audio.volume;
        while (audio.volume > 0f)
        {
            audio.volume -= startVolume * (Time.deltaTime / duration);
            yield return null;
        }
        audio.Stop();
    }

    private void OnDestroy()
    {
        ButtonController.OnClicked -= OnGameButtonClick;
        // pastikan tidak tersisa state pause global
        ButtonController.SetPaused(false);
    }

    // ====== PAUSE CONTROL ======
    public void PauseGame()
    {
        if (isPaused) return;
        isPaused = true;

        // Audio & Video pause
        if (musicController != null && musicController.audio != null)
            musicController.audio.Pause();

        if (videoPlayer == null) videoPlayer = FindObjectOfType<VideoPlayer>();
        if (videoPlayer != null)
            videoPlayer.Pause();

        // Freeze semua tombol yang sudah ada (tanpa timeScale=0)
        ButtonController.SetPaused(true);
    }

    public void ResumeGame()
    {
        if (!isPaused) return;
        isPaused = false;

        // Audio jalan lagi
        if (musicController != null && musicController.audio != null)
            musicController.audio.UnPause();

        // Video jalan lagi + resync ke waktu audio
        if (videoPlayer == null) videoPlayer = FindObjectOfType<VideoPlayer>();
        if (videoPlayer != null)
        {
            double target = (musicController != null && musicController.audio != null)
                ? musicController.audio.time
                : videoPlayer.time;
            videoPlayer.time = target;
            videoPlayer.Play();
        }

        // Lanjutkan tombol
        ButtonController.SetPaused(false);
    }
}
