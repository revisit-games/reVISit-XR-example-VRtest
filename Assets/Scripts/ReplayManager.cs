using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using XCharts.Runtime; // NEW: XCharts

// ---------- Existing data models (objects/cameras) ----------
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

// ---------- NEW: charts data models (must match Save output) ----------
[System.Serializable]
public class ChartValueSample
{
    public float value;
    public int timeMs;
}

[System.Serializable]
public class ChartPointSeries
{
    public int dataIndex; // point index inside the serie
    public List<ChartValueSample> samples = new List<ChartValueSample>();
}

[System.Serializable]
public class ChartSerieTrajectory
{
    public string chartName;      // e.g., "LineChart"
    public int serieIndex;        // e.g., 0
    public List<ChartPointSeries> points = new List<ChartPointSeries>();
}

[System.Serializable]
public class TrajectorySaveData
{
    public List<ObjectTrajectory> objects = new List<ObjectTrajectory>();
    public List<CameraTrajectory> cameras = new List<CameraTrajectory>();

    // NEW: charts block
    public List<ChartSerieTrajectory> charts = new List<ChartSerieTrajectory>();
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

// ---------- NEW: chart binding (scene LineChart <-> JSON name/serie) ----------
[System.Serializable]
public class ChartReplayBinding
{
    [Header("Match JSON")]
    [Tooltip("JSON chartName (defaults to the LineChart GameObject name if empty).")]
    public string jsonChartNameOverride;

    [Tooltip("Which serieIndex in that chart to replay (0 = 'Serie 0').")]
    public int serieIndex = 0;

    [Header("Scene")]
    [Tooltip("Scene LineChart component to drive.")]
    public LineChart chart;

    [Header("Options")]
    [Tooltip("If true, when the serie does not have enough points, add data rows automatically.")]
    public bool autoCreateMissingPoints = true;

    [Tooltip("Interpolate values between samples for smoother curves.")]
    public bool interpolateValues = true;

    // ---- runtime caches ----
    [System.NonSerialized] public ChartSerieTrajectory matchedData;
    [System.NonSerialized] public Dictionary<int, int> pointIndices = new Dictionary<int, int>(); // per dataIndex -> current sample idx
}

public class ReplayManager : MonoBehaviour
{
    [Header("Replay Targets (Objects / Cameras)")]
    [Tooltip("List of GameObjects to replay, and their corresponding JSON names")]
    public List<ReplayTarget> replayTargets = new List<ReplayTarget>();

    [Header("Charts Replay")]
    [Tooltip("Bind scene LineCharts to JSON chart data by name + serieIndex.")]
    public List<ChartReplayBinding> chartBindings = new List<ChartReplayBinding>();

    [Tooltip("Call chart.RefreshChart() each frame after applying updates.")]
    public bool refreshChartsEachFrame = true;

    [Header("Replay Data File")]
    [Tooltip("The JSON file name to load from /Saves/")]
    public string replayFileName = "save_1.json";

    [Header("Replay Settings (Objects/Cameras)")]
    [Tooltip("If true, will interpolate between samples for smoother replay of objects/cameras")]
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

    // Per-target sample indices cache (objects/cameras)
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
            // NEW: charts duration
            if (loadedData.charts != null)
            {
                foreach (var ch in loadedData.charts)
                {
                    if (ch?.points == null) continue;
                    foreach (var p in ch.points)
                        if (p?.samples != null && p.samples.Count > 0)
                            maxMs = Mathf.Max(maxMs, p.samples[p.samples.Count - 1].timeMs);
                }
            }
        }
        TotalDurationMs = Mathf.Max(0, maxMs);

        // Reset state
        isReplaying = false;
        isPaused = false;
        pausedAtMs = 0f;
        replayIndices.Clear();

        // Prepare chart bindings: match JSON block by name+serie, init indices, ensure data rows
        PrepareChartBindings();
    }

    /// <summary>
    /// Map chartBindings to loadedData.charts and setup per-point indices.
    /// </summary>
    private void PrepareChartBindings()
    {
        if (loadedData == null || chartBindings == null) return;

        foreach (var b in chartBindings)
        {
            b.matchedData = null;
            b.pointIndices.Clear();
            if (b.chart == null) continue;

            string wantName = string.IsNullOrEmpty(b.jsonChartNameOverride)
                ? b.chart.gameObject.name
                : b.jsonChartNameOverride;

            // find chart serie block in data
            var block = loadedData.charts?.Find(ch =>
                ch != null &&
                ch.serieIndex == b.serieIndex &&
                ch.chartName == wantName);

            if (block == null)
            {
                Debug.LogWarning($"[Replay][Charts] No chart data found for '{wantName}' serieIndex={b.serieIndex}");
                continue;
            }

            b.matchedData = block;

            // Ensure the serie has enough points
            var serie = b.chart.GetSerie(b.serieIndex);
            if (serie == null)
            {
                Debug.LogWarning($"[Replay][Charts] LineChart has no serie at index {b.serieIndex}.");
                continue;
            }

            // Find max dataIndex we will touch
            int maxIdx = -1;
            foreach (var p in block.points)
                if (p != null) maxIdx = Mathf.Max(maxIdx, p.dataIndex);

            if (b.autoCreateMissingPoints && maxIdx >= 0)
            {
                while (serie.dataCount <= maxIdx)
                {
                    // Add data rows as (x = dataCount, y = 0 by default)
                    b.chart.AddData(b.serieIndex, serie.dataCount, 0);
                }
            }

            // init per-point indices
            foreach (var p in block.points)
            {
                if (p == null || p.samples == null || p.samples.Count == 0) continue;
                b.pointIndices[p.dataIndex] = 0;
            }
        }
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

        // Reset chart per-point indices to 0
        foreach (var b in chartBindings)
        {
            if (b?.matchedData == null) continue;
            b.pointIndices.Clear();
            foreach (var p in b.matchedData.points)
                if (p?.samples != null && p.samples.Count > 0)
                    b.pointIndices[p.dataIndex] = 0;
        }
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
    /// Also reseeks chart sample indices.
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

        // Rebuild per-track indices to <= targetMs (objects/cameras)
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

        // Reseek chart per-point indices
        foreach (var b in chartBindings)
        {
            if (b?.matchedData == null) continue;
            foreach (var p in b.matchedData.points)
            {
                if (p?.samples == null || p.samples.Count == 0) continue;
                int idx = 0;
                while (idx + 1 < p.samples.Count && p.samples[idx + 1].timeMs <= targetMs)
                    idx++;
                b.pointIndices[p.dataIndex] = idx;
            }
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

        // ---------- A) Objects / Cameras ----------
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

        // ---------- B) Charts (SerieData values) ----------
        bool anyChartUpdated = false;

        foreach (var b in chartBindings)
        {
            if (b == null || b.chart == null || b.matchedData == null) continue;

            // Ensure serie exists
            var serie = b.chart.GetSerie(b.serieIndex);
            if (serie == null) continue;

            foreach (var p in b.matchedData.points)
            {
                if (p?.samples == null || p.samples.Count == 0) continue;

                // Ensure the serie has enough data rows
                if (b.autoCreateMissingPoints)
                {
                    while (serie.dataCount <= p.dataIndex)
                        b.chart.AddData(b.serieIndex, serie.dataCount, 0);
                }
                if (p.dataIndex < 0 || p.dataIndex >= serie.dataCount) continue;

                // Advance index
                int idx = 0;
                if (b.pointIndices.TryGetValue(p.dataIndex, out idx))
                {
                    while (idx + 1 < p.samples.Count && p.samples[idx + 1].timeMs <= elapsedMs)
                        idx++;
                    b.pointIndices[p.dataIndex] = idx;
                }
                else
                {
                    // first use
                    while (idx + 1 < p.samples.Count && p.samples[idx + 1].timeMs <= elapsedMs)
                        idx++;
                    b.pointIndices[p.dataIndex] = idx;
                }

                float value;
                if (b.interpolateValues && idx + 1 < p.samples.Count)
                {
                    var a = p.samples[idx];
                    var c = p.samples[idx + 1];
                    float tLerp = Mathf.InverseLerp(a.timeMs, c.timeMs, elapsedMs);
                    value = Mathf.Lerp(a.value, c.value, tLerp);
                }
                else
                {
                    value = p.samples[b.pointIndices[p.dataIndex]].value;
                }

                // Write Y (dimension 1)
                b.chart.UpdateData(b.serieIndex, p.dataIndex, 1, value);
                anyChartUpdated = true;
            }
        }

        if (anyChartUpdated && refreshChartsEachFrame)
        {
            // If you have many charts, you may cache unique charts and refresh once each.
            foreach (var b in chartBindings)
                if (b?.chart != null) b.chart.RefreshChart();
        }
    }
}
