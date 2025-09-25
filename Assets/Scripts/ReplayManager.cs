using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[System.Serializable]
public class PositionSample
{
    public Vector3 position;
    public int timeMs;
}

[System.Serializable]
public class CameraSample
{
    public Vector3 position;
    public Vector3 forward;
    public int timeMs;
}

[System.Serializable]
public class ObjectTrajectory
{
    public string objectName;
    public List<PositionSample> samples = new List<PositionSample>();
}

[System.Serializable]
public class CameraTrajectory
{
    public string cameraName;
    public List<CameraSample> samples = new List<CameraSample>();
}

[System.Serializable]
public class TrajectorySaveData
{
    public List<ObjectTrajectory> objects = new List<ObjectTrajectory>();
    public List<CameraTrajectory> cameras = new List<CameraTrajectory>();
}

[System.Serializable]
public class ReplayTarget
{
    [Tooltip("The name of the object/camera in the JSON file (objectName or cameraName)")]
    public string jsonName;
    [Tooltip("The GameObject in the scene to replay the trajectory")]
    public GameObject target;
    [Tooltip("Is this a camera (position + forward) or a normal object (position only)?")]
    public bool isCamera;
}

public class ReplayManager : MonoBehaviour
{
    [Header("Replay Targets")]
    [Tooltip("List of GameObjects to replay, and their corresponding JSON names")]
    public List<ReplayTarget> replayTargets = new List<ReplayTarget>();

    [Header("Replay Data File")]
    [Tooltip("The JSON file name to load from /Saves/")]
    public string replayFileName = "save_1.json";

    [Header("Replay Settings")]
    [Tooltip("If true, will interpolate between samples for smoother replay")]
    public bool interpolate = false;

    // --- Public properties for external UI/controls ---
    public bool IsReplaying => isReplaying;
    public bool IsPaused => isPaused;

    /// <summary>Current timeline time in milliseconds (clamped to [0, TotalDurationMs]).</summary>
    public float CurrentTimeMs
    {
        get
        {
            float ms;
            if (!isReplaying) ms = 0f;
            else ms = isPaused ? pausedAtMs : (Time.time - replayStartTime) * 1000f;
            return Mathf.Clamp(ms, 0f, TotalDurationMs);
        }
    }

    /// <summary>Current elapsed time in milliseconds since replay started (same as CurrentTimeMs).</summary>
    public float ElapsedMs => CurrentTimeMs;

    /// <summary>Total duration in milliseconds (calculated after data load).</summary>
    public int TotalDurationMs { get; private set; } = 0;

    /// <summary>Normalized 0¨C1 progress of the replay.</summary>
    public float Progress01 => (TotalDurationMs > 0) ? Mathf.Clamp01(CurrentTimeMs / TotalDurationMs) : 0f;
    // ---------------------------------------------------

    private TrajectorySaveData loadedData;
    private bool isReplaying = false;
    private bool isPaused = false;

    // When playing: Time.time at which replay "time 0" started.
    private float replayStartTime;

    // When paused: timeline time at which we paused (ms).
    private float pausedAtMs = 0f;

    // Per-target sample indices cache
    private Dictionary<ReplayTarget, int> replayIndices = new Dictionary<ReplayTarget, int>();

    void Start()
    {
        LoadReplayData();
    }

    /// <summary>
    /// Loads the replay data from the specified JSON file.
    /// </summary>
    public void LoadReplayData()
    {
        string path = Application.dataPath + "/Saves/" + replayFileName;
        if (!File.Exists(path))
        {
            Debug.LogWarning("Replay file not found: " + path);
            loadedData = null;
            TotalDurationMs = 0;
            return;
        }
        string json = File.ReadAllText(path);
        loadedData = JsonUtility.FromJson<TrajectorySaveData>(json);
        Debug.Log("Replay data loaded from: " + path);

        // Calculate total duration as the max last sample time across all tracks
        int maxMs = 0;
        if (loadedData != null)
        {
            if (loadedData.objects != null)
            {
                foreach (var o in loadedData.objects)
                    if (o != null && o.samples != null && o.samples.Count > 0)
                        maxMs = Mathf.Max(maxMs, o.samples[o.samples.Count - 1].timeMs);
            }
            if (loadedData.cameras != null)
            {
                foreach (var c in loadedData.cameras)
                    if (c != null && c.samples != null && c.samples.Count > 0)
                        maxMs = Mathf.Max(maxMs, c.samples[c.samples.Count - 1].timeMs);
            }
        }
        TotalDurationMs = Mathf.Max(0, maxMs);

        // Reset state
        isReplaying = false;
        isPaused = false;
        pausedAtMs = 0f;
        replayIndices.Clear();
    }

    /// <summary>
    /// Starts the replay from the beginning (sets time to 0).
    /// </summary>
    public void StartReplay()
    {
        if (loadedData == null)
        {
            Debug.LogWarning("No replay data loaded.");
            return;
        }
        isReplaying = true;
        isPaused = false;
        pausedAtMs = 0f;
        replayStartTime = Time.time; // play from t=0
        replayIndices.Clear();
        foreach (var t in replayTargets)
            replayIndices[t] = 0;
    }

    /// <summary>
    /// Stops the replay (not paused; timeline resets to 0).
    /// </summary>
    public void StopReplay()
    {
        isReplaying = false;
        isPaused = false;
        pausedAtMs = 0f;
    }

    /// <summary>Pause the replay at the current timeline time.</summary>
    public void PauseReplay()
    {
        if (!isReplaying || isPaused) return;
        pausedAtMs = (Time.time - replayStartTime) * 1000f;
        pausedAtMs = Mathf.Clamp(pausedAtMs, 0f, TotalDurationMs);
        isPaused = true;
    }

    /// <summary>Resume the replay from the paused timeline time.</summary>
    public void ResumeReplay()
    {
        if (!isReplaying || !isPaused) return;
        // Shift start time so that CurrentTimeMs continues from pausedAtMs
        replayStartTime = Time.time - (pausedAtMs / 1000f);
        isPaused = false;
    }

    /// <summary>Toggle between paused and playing.</summary>
    public void TogglePause()
    {
        if (!isReplaying) return;
        if (isPaused) ResumeReplay();
        else PauseReplay();
    }

    /// <summary>
    /// Seeks the replay to the specified timestamp in milliseconds.
    /// Works in both playing and paused states.
    /// </summary>
    public void SeekToMs(int targetMs)
    {
        if (loadedData == null) return;
        targetMs = Mathf.Clamp(targetMs, 0, Mathf.Max(0, TotalDurationMs));

        if (isPaused)
        {
            // When paused: just set pausedAtMs and keep paused
            pausedAtMs = targetMs;
        }
        else
        {
            // When playing: adjust replayStartTime so elapsed aligns to target
            replayStartTime = Time.time - (targetMs / 1000f);
        }

        // Rebuild per-track indices to <= targetMs
        if (replayIndices == null) replayIndices = new Dictionary<ReplayTarget, int>();
        replayIndices.Clear();

        foreach (var t in replayTargets)
        {
            int idx = 0;
            if (t.isCamera)
            {
                var camData = loadedData.cameras.Find(c => c.cameraName == t.jsonName);
                if (camData != null && camData.samples != null && camData.samples.Count > 0)
                {
                    while (idx + 1 < camData.samples.Count && camData.samples[idx + 1].timeMs <= targetMs)
                        idx++;
                }
            }
            else
            {
                var objData = loadedData.objects.Find(o => o.objectName == t.jsonName);
                if (objData != null && objData.samples != null && objData.samples.Count > 0)
                {
                    while (idx + 1 < objData.samples.Count && objData.samples[idx + 1].timeMs <= targetMs)
                        idx++;
                }
            }
            replayIndices[t] = idx;
        }
    }

    /// <summary>
    /// Nudge the timeline by a signed amount of seconds. Positive = forward, negative = backward.
    /// Safe to call every frame while a hotkey is held.
    /// </summary>
    public void NudgeBySeconds(float seconds)
    {
        if (loadedData == null || !isReplaying) return;
        int targetMs = Mathf.RoundToInt(CurrentTimeMs + seconds * 1000f);
        SeekToMs(targetMs);
    }

    void Update()
    {
        if (!isReplaying || loadedData == null) return;

        // Use CurrentTimeMs so that both playing and paused modes are supported
        float elapsedMs = CurrentTimeMs;

        foreach (var t in replayTargets)
        {
            if (t.target == null || string.IsNullOrEmpty(t.jsonName)) continue;

            if (t.isCamera)
            {
                var camData = loadedData.cameras.Find(c => c.cameraName == t.jsonName);
                if (camData == null || camData.samples.Count == 0) continue;

                int idx = replayIndices.ContainsKey(t) ? replayIndices[t] : 0;

                // Advance to the latest sample whose timeMs <= elapsedMs
                while (idx + 1 < camData.samples.Count && camData.samples[idx + 1].timeMs <= elapsedMs)
                    idx++;
                replayIndices[t] = idx;

                // Interpolation (optional)
                if (interpolate && idx + 1 < camData.samples.Count)
                {
                    var a = camData.samples[idx];
                    var b = camData.samples[idx + 1];
                    float tLerp = Mathf.InverseLerp(a.timeMs, b.timeMs, elapsedMs);
                    t.target.transform.position = Vector3.Lerp(a.position, b.position, tLerp);
                    t.target.transform.forward = Vector3.Slerp(a.forward, b.forward, tLerp);
                }
                else
                {
                    var sample = camData.samples[idx];
                    t.target.transform.position = sample.position;
                    t.target.transform.forward = sample.forward;
                }
            }
            else
            {
                var objData = loadedData.objects.Find(o => o.objectName == t.jsonName);
                if (objData == null || objData.samples.Count == 0) continue;

                int idx = replayIndices.ContainsKey(t) ? replayIndices[t] : 0;

                while (idx + 1 < objData.samples.Count && objData.samples[idx + 1].timeMs <= elapsedMs)
                    idx++;
                replayIndices[t] = idx;

                // Interpolation (optional)
                if (interpolate && idx + 1 < objData.samples.Count)
                {
                    var a = objData.samples[idx];
                    var b = objData.samples[idx + 1];
                    float tLerp = Mathf.InverseLerp(a.timeMs, b.timeMs, elapsedMs);
                    t.target.transform.position = Vector3.Lerp(a.position, b.position, tLerp);
                }
                else
                {
                    var sample = objData.samples[idx];
                    t.target.transform.position = sample.position;
                }
            }
        }
    }
}
