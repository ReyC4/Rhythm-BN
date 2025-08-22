using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ButtonController : MonoBehaviour
{
    // ========= GLOBAL PAUSE (broadcast ke semua instance) =========
    public static bool IsPaused { get; private set; } = false;

    public static void SetPaused(bool paused)
    {
        if (IsPaused == paused) return;
        IsPaused = paused;

        foreach (var inst in _instances)
        {
            if (inst == null) continue;
            if (paused) inst.OnPaused();
            else        inst.OnResumed();
        }
    }

    private static readonly HashSet<ButtonController> _instances = new HashSet<ButtonController>();

    // ========= REFS =========
    [Header("Refs")]
    public Button startButton;
    public Button endButton;
    public Image dragRegion;
    public Text startButtonText;
    public Text endButtonText;
    public Image indicator;
    public IndicatorCollision indicatorCollision;

    // ========= TIMING & STATE =========
    [Header("Timing (ms)")]
    [Tooltip("Lebar timing window per tombol (dalam milidetik).")]
    public float duration = 800f;            // ms
    public float buttonScore = 0f;

    private float spawnTimeSec = 0f;         // Time.time saat tombol dibuat
    private bool isDrag = false;
    private bool beginDragEvent = false;
    private bool wasClicked = false;

    [Header("Input")]
    public KeyCode inputKey = KeyCode.E;

    public delegate void ButtonClick(ButtonController button);
    public static event ButtonClick OnClicked;

    // ========= SCORING =========
    [Header("Scoring")]
    [Range(0f, 1f)] public float perfectThreshold = 0.85f;
    [Range(0f, 1f)] public float greatThreshold   = 0.60f;

    // ========= PRESS EFFECT =========
    [Header("Press Effect Settings")]
    public float pressScale = 0.9f;
    public float pressDuration = 0.08f;
    public AnimationCurve pressEase;

    // ========= FADE + ZOOM =========
    [Header("Fade Zoom Settings")]
    public float fadeDuration = 0.45f;
    public float zoomScale = 1.35f;
    public float textRise = 50f;
    public AnimationCurve fadeEase;

    // ========= AFTER IMAGE (DRAG ONLY) =========
    [Header("After Image (Drag Only)")]
    public bool enableAfterImage = true;
    public float afterImageInterval = 0.03f;
    public float afterImageLifetime = 0.6f;
    [Range(0f, 1f)] public float afterImageStartAlpha = 0.6f;
    public float afterImageEndScale = 1.2f;
    public Sprite afterImageOverrideSprite;

    // ========= RESULT TINTING =========
    [Header("Result Tinting")]
    public bool enableResultTint = true;
    public Color perfectTint = new Color(0.35f, 1f, 0.35f, 1f); // hijau muda
    public Color greatTint   = new Color(1f, 0.9f, 0.35f, 1f);  // kuning
    public Color missTint    = new Color(1f, 0.4f, 0.4f, 1f);   // merah
    [Tooltip("Warnai startButton image saat hasil keluar")]
    public bool tintStartButton = true;
    [Tooltip("Warnai endButton image (relevan untuk drag)")]
    public bool tintEndButton = true;
    [Tooltip("Warnai garis dragRegion (untuk drag)")]
    public bool tintDragRegion = false;
    [Tooltip("Warnai indicator image")]
    public bool tintIndicator = false;
    [Tooltip("Warnai teks label hasil (startButtonText / endButtonText)")]
    public bool tintText = false;

    private Coroutine afterImageCo;
    private Coroutine moveIndicatorCo;

    private void OnEnable()  { _instances.Add(this); }
    private void OnDisable() { _instances.Remove(this); }

    private void OnValidate()
    {
        if (perfectThreshold < greatThreshold) perfectThreshold = greatThreshold;
        if (afterImageInterval < 0.005f) afterImageInterval = 0.005f;
        if (afterImageLifetime < 0.05f) afterImageLifetime = 0.05f;
        if (afterImageEndScale < 1f)    afterImageEndScale = 1f;
        if (duration < 1f)              duration = 1f;
    }

    // =============================================================
    // INITIALIZE
    // =============================================================
    public void InitializeButton(float /*currentTimeMs (ignored)*/ _, float startX, float startY, bool isDrag, float endX, float endY)
    {
        transform.SetAsFirstSibling();
        transform.position = new Vector3(startX, startY);
        if (startButton != null) startButton.transform.SetParent(transform, false);

        this.isDrag = isDrag;

        if (this.isDrag)
            SetupDragRegion(startX, endX, startY, endY);

        if (startButton != null) startButton.gameObject.SetActive(true);

        spawnTimeSec = Time.time; // patokan timing (ikut Time.timeScale)
        StartCoroutine(ScaleIndicator());
    }

    public void SetupDragRegion(float x1, float x2, float y1, float y2)
    {
        if (dragRegion == null || endButton == null) return;

        Vector3 centerPos = new Vector3(x1 + x2, y1 + y2) / 2f;
        float scaleX = Mathf.Abs(x2 - x1);
        float scaleY = Mathf.Abs(y2 - y1);

        dragRegion.transform.localScale = new Vector3((scaleX + scaleY) / 100f, 1f);
        dragRegion.transform.position = centerPos;

        float angle = Mathf.Atan2(y2 - y1, x2 - x1);
        dragRegion.transform.rotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);

        endButton.transform.SetParent(transform, false);
        endButton.transform.position = new Vector3(x2, y2);

        dragRegion.gameObject.SetActive(true);
        endButton.gameObject.SetActive(true);
    }

    // =============================================================
    // UPDATE
    // =============================================================
    void Update()
    {
        if (IsPaused) return;

        bool inputDown = Input.GetKeyDown(inputKey) || Input.GetMouseButtonDown(0);
        bool inputHeld = Input.GetKey(inputKey)      || Input.GetMouseButton(0);
        bool inputUp   = Input.GetKeyUp(inputKey)    || Input.GetMouseButtonUp(0);

        if (inputDown && indicatorCollision != null && indicatorCollision.isHit &&
            startButton != null && startButton.gameObject.activeSelf && !wasClicked)
        {
            ButtonClicked();
        }

        // DRAG logic
        if (isDrag && beginDragEvent)
        {
            if (inputHeld && indicatorCollision != null && indicatorCollision.isHit)
            {
                if (moveIndicatorCo == null)
                    moveIndicatorCo = StartCoroutine(MoveIndicator());
                buttonScore += 0.05f; // DragScoreModifier
            }

            if (!inputHeld || inputUp)
            {
                StopAfterImageSpawner();
                if (moveIndicatorCo != null) { StopCoroutine(moveIndicatorCo); moveIndicatorCo = null; }

                wasClicked = true;
                OnClicked?.Invoke(this);
                StartCoroutine(FadeAway());
            }
        }

        // TIMEOUT (pakai Time.time → auto berhenti saat pause)
        float elapsedMs = (Time.time - spawnTimeSec) * 1000f;
        if (startButton != null && startButton.gameObject.activeSelf &&
            elapsedMs > duration && !wasClicked)
        {
            StopAfterImageSpawner();
            OnClicked?.Invoke(this);
            StartCoroutine(FadeAway());
        }
    }

    // =============================================================
    // CLICK
    // =============================================================
    public void ButtonClicked()
    {
        if (IsPaused) return;

        if (isDrag)
            StartCoroutine(ClickSequenceDrag());
        else
            StartCoroutine(ClickSequenceNonDrag());
    }

    private IEnumerator ClickSequenceNonDrag()
    {
        yield return StartCoroutine(PressEffect());

        float clickTimeMs = (Time.time - spawnTimeSec) * 1000f;
        buttonScore = CalcScore(clickTimeMs);
        wasClicked = true;
        OnClicked?.Invoke(this);

        StartCoroutine(FadeAway());
    }

    private IEnumerator ClickSequenceDrag()
    {
        yield return StartCoroutine(PressEffect());

        beginDragEvent = true;
        if (enableAfterImage && afterImageCo == null)
            afterImageCo = StartCoroutine(SpawnAfterImages());
    }

    // =============================================================
    // SCORING / ANIMS
    // =============================================================
    public float CalcPerfectTimeMs() => duration / 2f;

    public float CalcScore(float clickTimeMs)
    {
        float perfect = CalcPerfectTimeMs();
        return 1f - Mathf.Abs(clickTimeMs - perfect) / perfect; // 0..1
    }

    private IEnumerator ScaleIndicator()
    {
        if (indicator == null) yield break;

        Vector3 originalScale = indicator.transform.localScale;
        Vector3 destinationScale = new Vector3(0.6f, 0.6f, 0.6f);

        while (((Time.time - spawnTimeSec) * 1000f) < (duration / 2f))
        {
            if (IsPaused) { yield return null; continue; }
            float t = ((Time.time - spawnTimeSec) * 1000f) / (duration / 2f);
            indicator.transform.localScale = Vector3.Lerp(originalScale, destinationScale, t);
            yield return null;
        }
    }

    private IEnumerator MoveIndicator()
    {
        if (indicator == null || endButton == null) yield break;

        Vector3 originalLocation = indicator.transform.position;
        Vector3 destination = endButton.transform.position;

        while (((Time.time - spawnTimeSec) * 1000f) < duration)
        {
            if (IsPaused) { yield return null; continue; }
            float t = ((Time.time - spawnTimeSec) * 1000f) / duration;
            indicator.transform.position = Vector3.Lerp(originalLocation, destination, t);
            yield return null;
        }

        moveIndicatorCo = null;
    }

    private IEnumerator PressEffect()
    {
        Transform t = transform;
        Vector3 startScale = t.localScale;
        Vector3 pressedScale = startScale * Mathf.Clamp(pressScale, 0.5f, 1f);
        float dur = Mathf.Max(0.01f, pressDuration);
        AnimationCurve curve = pressEase ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);

        float elapsed = 0f;
        while (elapsed < dur)
        {
            if (IsPaused) { yield return null; continue; }
            elapsed += Time.deltaTime;
            float k = curve.Evaluate(Mathf.Clamp01(elapsed / dur));
            t.localScale = Vector3.LerpUnclamped(startScale, pressedScale, k);
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < dur)
        {
            if (IsPaused) { yield return null; continue; }
            elapsed += Time.deltaTime;
            float k = curve.Evaluate(Mathf.Clamp01(elapsed / dur));
            t.localScale = Vector3.LerpUnclamped(pressedScale, startScale, k);
            yield return null;
        }

        t.localScale = startScale;
    }

    private IEnumerator SpawnAfterImages()
    {
        if (indicator == null) yield break;

        RectTransform indRT = indicator.rectTransform;
        Transform parent = indicator.transform.parent;

        while (beginDragEvent && (Input.GetKey(inputKey) || Input.GetMouseButton(0)))
        {
            if (IsPaused) { yield return null; continue; }

            GameObject ghost = new GameObject("AfterImage");
            ghost.transform.SetParent(parent, false);

            var ghostRT = ghost.AddComponent<RectTransform>();
            ghostRT.anchorMin = indRT.anchorMin;
            ghostRT.anchorMax = indRT.anchorMax;
            ghostRT.pivot     = indRT.pivot;
            ghostRT.sizeDelta = indRT.sizeDelta;
            ghostRT.rotation  = indRT.rotation;
            ghostRT.position  = indRT.position;
            ghostRT.localScale= indRT.localScale;

            var img = ghost.AddComponent<Image>();
            img.raycastTarget = false;
            img.sprite = afterImageOverrideSprite != null ? afterImageOverrideSprite : indicator.sprite;

            Color baseCol = indicator.color;
            img.color = new Color(baseCol.r, baseCol.g, baseCol.b, afterImageStartAlpha);

            StartCoroutine(FadeAndScaleOut(img, afterImageLifetime, afterImageEndScale));

            // Catatan: WaitForSeconds ikut pause hanya jika Time.timeScale=0.
            // Di GameVersiController kamu tidak set timeScale, maka spawner dihentikan via StopAfterImageSpawner() di OnPaused().
            yield return new WaitForSeconds(afterImageInterval);
        }

        afterImageCo = null;
    }

    private IEnumerator FadeAndScaleOut(Image img, float lifetime, float endScaleMul)
    {
        if (img == null) yield break;

        RectTransform rt = img.rectTransform;
        float t = 0f;
        float dur = Mathf.Max(0.05f, lifetime);

        Color c0 = img.color;
        Color c1 = new Color(c0.r, c0.g, c0.b, 0f);

        Vector3 s0 = rt.localScale;
        Vector3 s1 = s0 * Mathf.Max(1f, endScaleMul);

        while (t < dur)
        {
            if (IsPaused) { yield return null; continue; }
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);

            img.color = Color.Lerp(c0, c1, k);
            rt.localScale = Vector3.LerpUnclamped(s0, s1, k);
            yield return null;
        }

        if (img != null) Destroy(img.gameObject);
    }

    private void StopAfterImageSpawner()
    {
        if (afterImageCo != null)
        {
            StopCoroutine(afterImageCo);
            afterImageCo = null;
        }
    }

    public IEnumerator FadeAway()
    {
        if (indicator != null)
        {
            var col = indicator.GetComponent<CircleCollider2D>();
            if (col != null) col.enabled = false;
        }

        StopAfterImageSpawner();

        // ====== TENTUKAN HASIL & TINT SEBELUM AMBIL WARNA START ======
        string resultText;
        Color resultTint = Color.white;
        bool isPerfect = false;

        if (!wasClicked)
        {
            resultText = "Miss";
            resultTint = missTint;
        }
        else
        {
            isPerfect = (buttonScore >= perfectThreshold);
            if (isPerfect)
            {
                resultText = "Perfect";
                resultTint = perfectTint;
            }
            else
            {
                resultText = "Great";
                resultTint = greatTint;
            }
        }

        if (enableResultTint)
        {
            // pilih target utama (untuk single tap → startButton, untuk drag → endButton)
            if (!isDrag && tintStartButton && startButton != null)
                startButton.image.color = resultTint;

            if (isDrag && tintEndButton && endButton != null)
                endButton.image.color = resultTint;

            if (tintDragRegion && dragRegion != null)
                dragRegion.color = resultTint;

            if (tintIndicator && indicator != null)
                indicator.color = resultTint;

            if (tintText)
            {
                if (isDrag && endButtonText != null) endButtonText.color = resultTint;
                else if (!isDrag && startButtonText != null) startButtonText.color = resultTint;
            }
        }

        // ====== SET TEKS HASIL ======
        if (isDrag)
        {
            if (endButtonText != null) endButtonText.text = resultText;
        }
        else
        {
            if (startButtonText != null) startButtonText.text = resultText;
        }

        // ====== BARU AMBIL WARNA START (supaya fade dari warna hasil) ======
        Color sbCol = startButton != null ? startButton.image.color : Color.white;
        Color ebCol = endButton   != null ? endButton.image.color   : Color.white;
        Color drCol = dragRegion  != null ? dragRegion.color        : Color.white;
        Color inCol = indicator   != null ? indicator.color         : Color.white;

        Color stCol = startButtonText != null ? startButtonText.color : Color.white;
        Color etCol = endButtonText   != null ? endButtonText.color   : Color.white;

        Color sbEnd = new Color(sbCol.r, sbCol.g, sbCol.b, 0f);
        Color ebEnd = new Color(ebCol.r, ebCol.g, ebCol.b, 0f);
        Color drEnd = new Color(drCol.r, drCol.g, drCol.b, 0f);
        Color inEnd = new Color(inCol.r, inCol.g, inCol.b, 0f);
        Color stEnd = new Color(stCol.r, stCol.g, stCol.b, 0f);
        Color etEnd = new Color(etCol.r, etCol.g, etCol.b, 0f);

        Transform t = transform;
        Vector3 scaleStart = t.localScale;
        Vector3 scaleEnd = scaleStart * Mathf.Max(1f, zoomScale);

        Vector3 txtStart, txtEnd;
        if (isDrag && endButtonText != null)
        {
            txtStart = endButtonText.transform.position;
            txtEnd   = txtStart + new Vector3(0f, textRise, 0f);
        }
        else
        {
            txtStart = startButtonText != null ? startButtonText.transform.position : Vector3.zero;
            txtEnd   = txtStart + new Vector3(0f, textRise, 0f);
        }

        float elapsed = 0f;
        float dur = Mathf.Max(0.01f, fadeDuration);
        AnimationCurve curve = fadeEase ?? AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        while (elapsed < dur)
        {
            if (IsPaused) { yield return null; continue; }

            elapsed += Time.deltaTime;
            float e = curve.Evaluate(Mathf.Clamp01(elapsed / dur));

            // Zoom
            t.localScale = Vector3.LerpUnclamped(scaleStart, scaleEnd, e);

            // Fade
            if (startButton != null) startButton.image.color = Color.Lerp(sbCol, sbEnd, e);
            if (endButton   != null) endButton.image.color   = Color.Lerp(ebCol, ebEnd, e);
            if (dragRegion  != null) dragRegion.color        = Color.Lerp(drCol, drEnd, e);
            if (indicator   != null) indicator.color         = Color.Lerp(inCol, inEnd, e);

            if (startButtonText != null) startButtonText.color = Color.Lerp(stCol, stEnd, e);
            if (endButtonText   != null) endButtonText.color   = Color.Lerp(etCol, etEnd, e);

            if (isDrag && endButtonText != null)
                endButtonText.transform.position = Vector3.LerpUnclamped(txtStart, txtEnd, e);
            else if (!isDrag && startButtonText != null)
                startButtonText.transform.position = Vector3.LerpUnclamped(txtStart, txtEnd, e);

            yield return null;
        }

        transform.localScale = scaleEnd;
        Destroy(gameObject);
    }

    // ====== dipanggil saat SetPaused(true/false) ======
    private void OnPaused()
    {
        // HANYA hentikan spawner after-image.
        // Jangan StopCoroutine animasi lain → biar lanjut lagi saat resume.
        StopAfterImageSpawner();
        // (Sengaja TIDAK menghentikan moveIndicatorCo / scale / fade)
    }

    private void OnResumed()
    {
        // tidak perlu apa-apa; semua coroutine akan lanjut karena IsPaused=false dan Time.time berjalan lagi
    }
}
