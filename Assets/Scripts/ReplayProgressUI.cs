using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// UI controller for displaying and scrubbing replay progress,
/// and for swapping a state icon (before start / playing / paused).
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

    // Internal state
    private bool _isDragging = false;
    private bool _pointerHeldOnThis = false;

    // We track whether replay has ever started and the last known progress
    private bool _hasEverStarted = false;
    private float _lastProgress01 = 0f;

    // A small epsilon for comparing near-end progress
    private const float kEndEpsilon = 0.999f;

    void Reset()
    {
        // Try to auto-find a Slider on this GameObject or its children.
        if (slider == null) slider = GetComponentInChildren<Slider>();
    }

    void Awake()
    {
        if (slider != null)
        {
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;

            // Observe value changes; when dragging, optionally seek in real-time.
            slider.onValueChanged.AddListener(OnSliderValueChanged);
        }
    }

    void OnDestroy()
    {
        if (slider != null)
        {
            slider.onValueChanged.RemoveListener(OnSliderValueChanged);
        }
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
        }

        // When not dragging, follow the replay progress
        if (!_isDragging)
        {
            slider.value = replayManager.Progress01;
        }

        // Track "has ever started" and remember progress
        if (replayManager.IsReplaying) _hasEverStarted = true;
        _lastProgress01 = slider.value;

        // Update time label
        if (timeLabel != null)
        {
            int total = Mathf.Max(1, replayManager.TotalDurationMs);
            int curMs = Mathf.RoundToInt(slider.value * total);
            timeLabel.text = $"{FormatMs(curMs)} / {FormatMs(total)}";
        }

        // Update state icon
        UpdateStateIcon();
    }

    /// <summary>
    /// Determine current visual state and swap the stateImage sprite if assigned.
    /// We infer three states:
    /// - BeforeStart: never started and progress is ~0
    /// - Playing: ReplayManager.IsReplaying == true
    /// - Paused: not playing but has started before (or finished if configured)
    /// </summary>
    private void UpdateStateIcon()
    {
        if (stateImage == null) return;

        // Decide state
        bool isPlaying = replayManager != null && replayManager.IsReplaying;
        bool nearStart = _lastProgress01 <= 0.0001f;
        bool nearEnd = _lastProgress01 >= kEndEpsilon;

        Sprite target = null;

        if (!_hasEverStarted && nearStart)
        {
            // Before start
            target = iconBeforeStart != null ? iconBeforeStart : stateImage.sprite;
        }
        else if (isPlaying)
        {
            // Playing
            target = iconPlaying != null ? iconPlaying : stateImage.sprite;
        }
        else
        {
            // Not playing: either paused mid-way, or (optionally) finished
            if (nearEnd && !finishedCountsAsPaused)
            {
                // If you don't want "finished" to be treated as paused,
                // you can fall back to the "before start" icon or keep current.
                target = iconBeforeStart != null ? iconBeforeStart : stateImage.sprite;
            }
            else
            {
                // Paused (or finished if opted-in)
                target = iconPaused != null ? iconPaused : stateImage.sprite;
            }
        }

        if (stateImage.sprite != target && target != null)
            stateImage.sprite = target;
    }

    /// <summary>
    /// Public API: Call this from EventTrigger (BeginDrag) if you prefer explicit wiring.
    /// </summary>
    public void BeginDrag()
    {
        if (!allowScrub) return;
        _isDragging = true;
    }

    /// <summary>
    /// Public API: Call this from EventTrigger (EndDrag) if you prefer explicit wiring.
    /// </summary>
    public void EndDrag()
    {
        if (!allowScrub) return;
        _isDragging = false;

        // Final seek on release if we do not seek continuously
        if (!seekWhileDragging && replayManager != null)
        {
            int targetMs = Mathf.RoundToInt(slider.value * replayManager.TotalDurationMs);
            replayManager.SeekToMs(targetMs);
        }
    }

    /// <summary>
    /// IPointerDownHandler: Begin dragging when the pointer presses on this UI.
    /// Works for mouse, touch, and XR/UI pointer events.
    /// </summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        if (!allowScrub) return;
        _pointerHeldOnThis = true;
        _isDragging = true;

        if (!seekWhileDragging) return;

        // Immediate seek to the pointer-down value
        if (replayManager != null)
        {
            int targetMs = Mathf.RoundToInt(slider.value * replayManager.TotalDurationMs);
            replayManager.SeekToMs(targetMs);
        }
    }

    /// <summary>
    /// IPointerUpHandler: Finish dragging on pointer release.
    /// </summary>
    public void OnPointerUp(PointerEventData eventData)
    {
        if (!allowScrub) return;
        _pointerHeldOnThis = false;
        _isDragging = false;

        // Seek once on release if not seeking continuously
        if (!seekWhileDragging && replayManager != null)
        {
            int targetMs = Mathf.RoundToInt(slider.value * replayManager.TotalDurationMs);
            replayManager.SeekToMs(targetMs);
        }
    }

    /// <summary>
    /// Slider callback. When dragging, optionally seek continuously.
    /// </summary>
    private void OnSliderValueChanged(float v)
    {
        if (!allowScrub || !seekWhileDragging) return;
        if (!_isDragging && !_pointerHeldOnThis) return;
        if (replayManager == null) return;

        int targetMs = Mathf.RoundToInt(v * replayManager.TotalDurationMs);
        replayManager.SeekToMs(targetMs);
    }

    /// <summary>
    /// Formats milliseconds as mm:ss (rounded to the nearest second).
    /// </summary>
    private string FormatMs(int ms)
    {
        int totalSec = Mathf.RoundToInt(ms / 1000f);
        int m = totalSec / 60;
        int s = totalSec % 60;
        return $"{m:00}:{s:00}";
    }

    // ---------- Optional convenience methods ----------

    /// <summary>
    /// Programmatically set the slider without triggering a seek (e.g., external UI sync).
    /// </summary>
    public void SetSliderSilently(float normalizedValue)
    {
        if (slider == null) return;
        var prev = slider.onValueChanged;
        slider.onValueChanged = new Slider.SliderEvent(); // temporarily detach
        slider.value = Mathf.Clamp01(normalizedValue);
        slider.onValueChanged = prev; // restore
    }

    /// <summary>
    /// Jumps to the given normalized position [0..1] and seeks immediately.
    /// </summary>
    public void JumpTo(float normalizedValue01)
    {
        if (replayManager == null || slider == null) return;
        slider.value = Mathf.Clamp01(normalizedValue01);
        int targetMs = Mathf.RoundToInt(slider.value * replayManager.TotalDurationMs);
        replayManager.SeekToMs(targetMs);
    }
}
