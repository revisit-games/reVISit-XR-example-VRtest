using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// UI controller for displaying and scrubbing replay progress,
/// swapping a state icon (before start / playing / paused),
/// and showing temporary icons for fast-forward / rewind based on timeline velocity.
/// Also updates an optional status TMP text with "Playing", "Paused", "Fast Forward", "Rewind".
/// Requires a ReplayManager reference in the scene.
/// </summary>
public class ReplayProgressUI : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Header("References")]
    [Tooltip("ReplayManager instance that provides progress and seek API.")]
    public ReplayManager replayManager;

    [Tooltip("UI Slider used as the progress bar (value in [0..1]).")]
    public Slider slider;

    [Tooltip("Optional TextMeshPro label to show current/total time (mm:ss / mm:ss).")]
    public TMP_Text timeLabel;

    [Header("Behavior")]
    [Tooltip("If true, the user can drag the slider to seek (scrub) the replay.")]
    public bool allowScrub = true;

    [Tooltip("If true, seek continuously while dragging; if false, only seek on release.")]
    public bool seekWhileDragging = true;

    [Tooltip("If true, auto-hide the slider when not replaying.")]
    public bool hideWhenNotReplaying = false;

    [Tooltip("Optional: Update the time label even when not replaying (shows 00:00 / total).")]
    public bool updateLabelWhenIdle = true;

    [Header("State Icon (optional)")]
    [Tooltip("UI Image whose sprite will be swapped based on playback state.")]
    public Image stateImage;

    [Tooltip("Sprite to use when replay has not started yet.")]
    public Sprite iconBeforeStart;

    [Tooltip("Sprite to use when replay is playing.")]
    public Sprite iconPlaying;

    [Tooltip("Sprite to use when replay is paused (or stopped after started).")]
    public Sprite iconPaused;

    [Tooltip("Treat 'finished (progress ~1)' as Paused state.")]
    public bool finishedCountsAsPaused = true;

    [Header("Fast Forward / Rewind Icons (optional)")]
    [Tooltip("Sprite to show temporarily while fast-forwarding (timeline velocity above threshold).")]
    public Sprite iconFastForward;

    [Tooltip("Sprite to show temporarily while rewinding (timeline velocity below threshold).")]
    public Sprite iconRewind;

    [Tooltip("Velocity threshold (ms per second) above which we treat it as fast-forward.")]
    public float velocityFFThreshold = 1200f;

    [Tooltip("Velocity threshold (ms per second) below which we treat it as rewind.")]
    public float velocityRWThreshold = -200f;

    [Tooltip("How long (seconds) to keep the FF/RW icon visible after detecting an event.")]
    public float ffRwHoldSeconds = 0.25f;

    // ---------- NEW: Status text (TMP) ----------
    [Header("Status Text (optional)")]
    [Tooltip("Optional TMP text that shows current state text (Playing / Paused / Fast Forward / Rewind).")]
    public TMP_Text statusLabel;

    [Tooltip("Text shown while playing.")]
    public string textPlaying = "Playing";

    [Tooltip("Text shown while paused.")]
    public string textPaused = "Paused";

    [Tooltip("Text shown while fast-forwarding.")]
    public string textFastForward = "Fast Forward";

    [Tooltip("Text shown while rewinding.")]
    public string textRewind = "Rewind";

    [Tooltip("Text shown before any replay has started (can be empty).")]
    public string textBeforeStart = "Ready";
    // -------------------------------------------

    // Internal state
    private bool _isDragging = false;
    private bool _pointerHeldOnThis = false;

    // We track whether replay has ever started and the last known progress
    private bool _hasEverStarted = false;
    private float _lastProgress01 = 0f;

    // FF/RW detection
    private float _lastTimeMs = 0f;
    private float _ffTimer = 0f;
    private float _rwTimer = 0f;

    // A small epsilon for comparing near-end progress
    private const float kEndEpsilon = 0.999f;

    void Reset()
    {
        if (slider == null) slider = GetComponentInChildren<Slider>();
    }

    void Awake()
    {
        if (slider != null)
        {
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            slider.onValueChanged.AddListener(OnSliderValueChanged);
        }
    }

    void OnDestroy()
    {
        if (slider != null)
            slider.onValueChanged.RemoveListener(OnSliderValueChanged);
    }

    void Update()
    {
        if (replayManager == null || slider == null) return;

        // Toggle visibility when not replaying
        if (hideWhenNotReplaying)
        {
            slider.gameObject.SetActive(replayManager.IsReplaying);
            if (timeLabel != null)
                timeLabel.gameObject.SetActive(replayManager.IsReplaying || updateLabelWhenIdle);
            if (statusLabel != null)
                statusLabel.gameObject.SetActive(true); // keep visible; content reflects state
        }

        // When not dragging, follow the replay progress
        if (!_isDragging)
        {
            slider.value = replayManager.Progress01;
        }

        // Track "has ever started" and remember progress
        if (replayManager.IsReplaying) _hasEverStarted = true;
        _lastProgress01 = slider.value;

        // Estimate current timeline time (ms) for velocity detection
        float total = Mathf.Max(1, replayManager.TotalDurationMs);
        float currentTimeMsEstimate = Mathf.Clamp01(slider.value) * total;

        // Velocity ms/s
        float dt = Mathf.Max(Time.deltaTime, 1e-6f);
        float velocityMsPerSec = (currentTimeMsEstimate - _lastTimeMs) / dt;

        if (velocityMsPerSec >= velocityFFThreshold && iconFastForward != null)
            _ffTimer = ffRwHoldSeconds;
        else
            _ffTimer = Mathf.Max(0f, _ffTimer - dt);

        if (velocityMsPerSec <= velocityRWThreshold && iconRewind != null)
            _rwTimer = ffRwHoldSeconds;
        else
            _rwTimer = Mathf.Max(0f, _rwTimer - dt);

        _lastTimeMs = currentTimeMsEstimate;

        // Time label (mm:ss / mm:ss)
        if (timeLabel != null)
        {
            int curMs = Mathf.RoundToInt(currentTimeMsEstimate);
            timeLabel.text = $"{FormatMs(curMs)} / {FormatMs((int)total)}";
        }

        // Update visuals
        UpdateStateIcon();
        UpdateStatusText(); // <<--- NEW
    }

    /// <summary>
    /// Decide current visual state and swap the stateImage sprite if assigned.
    /// Priority: FF/RW (if timers active) > Playing > Paused > BeforeStart.
    /// </summary>
    private void UpdateStateIcon()
    {
        if (stateImage == null || replayManager == null) return;

        bool nearStart = _lastProgress01 <= 0.0001f;
        bool nearEnd = _lastProgress01 >= kEndEpsilon;

        bool isReplaying = replayManager.IsReplaying; // true for both playing and paused
        bool isPaused = isReplaying && HasPropertyIsPausedTrue();
        bool isPlaying = isReplaying && !isPaused;

        if (_ffTimer > 0f && iconFastForward != null)
        {
            if (stateImage.sprite != iconFastForward) stateImage.sprite = iconFastForward;
            return;
        }
        if (_rwTimer > 0f && iconRewind != null)
        {
            if (stateImage.sprite != iconRewind) stateImage.sprite = iconRewind;
            return;
        }

        Sprite target = null;

        if (!_hasEverStarted && nearStart)
        {
            target = iconBeforeStart != null ? iconBeforeStart : stateImage.sprite;
        }
        else if (isPlaying)
        {
            target = iconPlaying != null ? iconPlaying : stateImage.sprite;
        }
        else if (isPaused)
        {
            target = iconPaused != null ? iconPaused : stateImage.sprite;
        }
        else
        {
            if (nearEnd && finishedCountsAsPaused)
                target = iconPaused != null ? iconPaused : stateImage.sprite;
            else
                target = iconBeforeStart != null ? iconBeforeStart : stateImage.sprite;
        }

        if (target != null && stateImage.sprite != target)
            stateImage.sprite = target;
    }

    // ---------- NEW: status text updater ----------
    /// <summary>
    /// Updates the optional TMP status label with "Playing", "Paused", "Fast Forward", "Rewind" (or "Ready" before start).
    /// Priority: FF/RW text > Playing > Paused > BeforeStart.
    /// </summary>
    private void UpdateStatusText()
    {
        if (statusLabel == null || replayManager == null) return;

        bool nearStart = _lastProgress01 <= 0.0001f;
        bool nearEnd = _lastProgress01 >= kEndEpsilon;

        bool isReplaying = replayManager.IsReplaying;
        bool isPaused = isReplaying && HasPropertyIsPausedTrue();
        bool isPlaying = isReplaying && !isPaused;

        // FF/RW take precedence while their timers are active
        if (_ffTimer > 0f)
        {
            statusLabel.text = textFastForward;
            return;
        }
        if (_rwTimer > 0f)
        {
            statusLabel.text = textRewind;
            return;
        }

        if (!_hasEverStarted && nearStart)
        {
            statusLabel.text = textBeforeStart;
        }
        else if (isPlaying)
        {
            statusLabel.text = textPlaying;
        }
        else if (isPaused)
        {
            statusLabel.text = textPaused;
        }
        else
        {
            // Stopped: choose paused at end (if configured) or before-start text
            statusLabel.text = (nearEnd && finishedCountsAsPaused) ? textPaused : textBeforeStart;
        }
    }
    // ----------------------------------------------

    /// <summary>
    /// Try to read ReplayManager.IsPaused if available; fallback to false if the property does not exist.
    /// </summary>
    private bool HasPropertyIsPausedTrue()
    {
        try
        {
            return (bool)replayManager.GetType().GetProperty("IsPaused")?.GetValue(replayManager, null) == true;
        }
        catch { return false; }
    }

    public void BeginDrag()
    {
        if (!allowScrub) return;
        _isDragging = true;
    }

    public void EndDrag()
    {
        if (!allowScrub) return;
        _isDragging = false;

        if (!seekWhileDragging && replayManager != null)
        {
            int targetMs = Mathf.RoundToInt(slider.value * replayManager.TotalDurationMs);
            replayManager.SeekToMs(targetMs);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!allowScrub) return;
        _pointerHeldOnThis = true;
        _isDragging = true;

        if (!seekWhileDragging) return;

        if (replayManager != null)
        {
            int targetMs = Mathf.RoundToInt(slider.value * replayManager.TotalDurationMs);
            replayManager.SeekToMs(targetMs);
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!allowScrub) return;
        _pointerHeldOnThis = false;
        _isDragging = false;

        if (!seekWhileDragging && replayManager != null)
        {
            int targetMs = Mathf.RoundToInt(slider.value * replayManager.TotalDurationMs);
            replayManager.SeekToMs(targetMs);
        }
    }

    private void OnSliderValueChanged(float v)
    {
        if (!allowScrub || !seekWhileDragging) return;
        if (!_isDragging && !_pointerHeldOnThis) return;
        if (replayManager == null) return;

        int targetMs = Mathf.RoundToInt(v * replayManager.TotalDurationMs);
        replayManager.SeekToMs(targetMs);
    }

    private string FormatMs(int ms)
    {
        int totalSec = Mathf.RoundToInt(ms / 1000f);
        int m = totalSec / 60;
        int s = totalSec % 60;
        return $"{m:00}:{s:00}";
    }

    // Optional helpers
    public void SetSliderSilently(float normalizedValue)
    {
        if (slider == null) return;
        var prev = slider.onValueChanged;
        slider.onValueChanged = new Slider.SliderEvent();
        slider.value = Mathf.Clamp01(normalizedValue);
        slider.onValueChanged = prev;
    }

    public void JumpTo(float normalizedValue01)
    {
        if (replayManager == null || slider == null) return;
        slider.value = Mathf.Clamp01(normalizedValue01);
        int targetMs = Mathf.RoundToInt(slider.value * replayManager.TotalDurationMs);
        replayManager.SeekToMs(targetMs);
    }
}
