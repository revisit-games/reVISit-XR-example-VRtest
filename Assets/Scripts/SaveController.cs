using System.IO;
using System.Collections.Generic;
using UnityEngine;
using XCharts.Runtime; // <¡ª NEW: XCharts runtime API

public class Save : MonoBehaviour
{
    #region === Inspector: Objects & Cameras ===

    [Header("Normal GameObjects To Record (Position Data)")]
    [SerializeField] private List<GameObject> targets = new List<GameObject>();

    [Header("Cameras (or Pointers) To Record (Position & Forward Data)")]
    [SerializeField] private List<GameObject> cameras = new List<GameObject>();

    [Header("Record Threshold Settings")]
    [SerializeField] private float objectPositionThreshold = 0.04f;
    [SerializeField] private float cameraPositionThreshold = 0.04f;
    [SerializeField] private float cameraForwardThreshold = 0.02f;

    #endregion

    #region === Inspector: Charts ===

    [System.Serializable]
    public class ChartRecordTarget
    {
        [Tooltip("The LineChart to record (XCharts Runtime).")]
        public LineChart chart;

        [Tooltip("Which serie index to record in the chart (0 = 'Serie 0').")]
        public int serieIndex = 0;

        [Tooltip("Which data indices in the serie to record. Leave empty to record ALL points in the serie.")]
        public List<int> dataIndices = new List<int>();

        [Tooltip("Threshold for value change to create a new sample (absolute delta).")]
        public float valueThreshold = 0.5f;

        [Tooltip("Optional: name to use in JSON (defaults to chart.gameObject.name if empty).")]
        public string jsonChartNameOverride = "";
    }

    [Header("Charts To Record (SerieData Values)")]
    [SerializeField] private List<ChartRecordTarget> charts = new List<ChartRecordTarget>();

    #endregion

    #region === Runtime state ===

    // Object position history
    private Dictionary<string, List<PositionSample>> positionHistory = new Dictionary<string, List<PositionSample>>();
    private Dictionary<string, Vector3> lastPosition = new Dictionary<string, Vector3>();

    // Camera history
    private Dictionary<string, List<CameraSample>> cameraHistory = new Dictionary<string, List<CameraSample>>();
    private Dictionary<string, Vector3> lastCameraPosition = new Dictionary<string, Vector3>();
    private Dictionary<string, Vector3> lastCameraForward = new Dictionary<string, Vector3>();

    // Chart history:
    // key: chartKey (chartName|serieIndex) -> (dataIndex -> list of samples)
    private Dictionary<string, Dictionary<int, List<ChartValueSample>>> chartHistory =
        new Dictionary<string, Dictionary<int, List<ChartValueSample>>>();

    // Last values for thresholding:
    // key: chartKey -> (dataIndex -> lastValue)
    private Dictionary<string, Dictionary<int, float>> lastChartValues =
        new Dictionary<string, Dictionary<int, float>>();

    private bool isRecording = false;
    private float startTime = 0f;

    #endregion

    private void Awake()
    {
        SaveManager.Init();
    }

    void Update()
    {
        if (!isRecording) return;

        int timeMs = (int)((Time.time - startTime) * 1000);

        // ---------- 1) Normal GameObjects ----------
        foreach (var go in targets)
        {
            if (go == null) continue;
            Vector3 current = go.transform.position;
            if (!lastPosition.ContainsKey(go.name) ||
                ExceedThresholdVec3(current, lastPosition[go.name], objectPositionThreshold))
            {
                if (!positionHistory.ContainsKey(go.name))
                    positionHistory[go.name] = new List<PositionSample>();

                positionHistory[go.name].Add(new PositionSample { position = current, timeMs = timeMs });
                lastPosition[go.name] = current;
            }
        }

        // ---------- 2) Cameras (position + forward) ----------
        foreach (var cam in cameras)
        {
            if (cam == null) continue;

            Vector3 currentPos = cam.transform.position;
            Vector3 currentFwd = cam.transform.forward;

            bool needRecord = false;
            if (!lastCameraPosition.ContainsKey(cam.name) ||
                ExceedThresholdVec3(currentPos, lastCameraPosition[cam.name], cameraPositionThreshold))
                needRecord = true;

            if (!lastCameraForward.ContainsKey(cam.name) ||
                ExceedThresholdVec3(currentFwd, lastCameraForward[cam.name], cameraForwardThreshold))
                needRecord = true;

            if (needRecord)
            {
                if (!cameraHistory.ContainsKey(cam.name))
                    cameraHistory[cam.name] = new List<CameraSample>();

                cameraHistory[cam.name].Add(new CameraSample
                {
                    position = currentPos,
                    forward = currentFwd,
                    timeMs = timeMs
                });

                lastCameraPosition[cam.name] = currentPos;
                lastCameraForward[cam.name] = currentFwd;
            }
        }

        // ---------- 3) Charts (serie data values) ----------
        foreach (var ct in charts)
        {
            if (ct == null || ct.chart == null) continue;

            string chartName = string.IsNullOrEmpty(ct.jsonChartNameOverride)
                ? ct.chart.gameObject.name
                : ct.jsonChartNameOverride;

            string chartKey = MakeChartKey(chartName, ct.serieIndex);

            var serie = ct.chart.GetSerie(ct.serieIndex);
            if (serie == null) continue;

            // Determine which indices to read
            List<int> indices = (ct.dataIndices != null && ct.dataIndices.Count > 0)
                ? ct.dataIndices
                : BuildAllIndices(serie.dataCount);

            // Ensure dictionaries
            if (!chartHistory.ContainsKey(chartKey))
                chartHistory[chartKey] = new Dictionary<int, List<ChartValueSample>>();
            if (!lastChartValues.ContainsKey(chartKey))
                lastChartValues[chartKey] = new Dictionary<int, float>();

            foreach (int idx in indices)
            {
                if (idx < 0 || idx >= serie.dataCount) continue;
                var sd = serie.GetSerieData(idx);
                if (sd == null) continue;

                float y = (float)sd.GetData(1); // Y dimension

                bool needRecord = false;
                if (!lastChartValues[chartKey].ContainsKey(idx))
                {
                    needRecord = true; // first time
                }
                else
                {
                    float last = lastChartValues[chartKey][idx];
                    if (Mathf.Abs(y - last) > ct.valueThreshold)
                        needRecord = true;
                }

                if (needRecord)
                {
                    if (!chartHistory[chartKey].ContainsKey(idx))
                        chartHistory[chartKey][idx] = new List<ChartValueSample>();

                    chartHistory[chartKey][idx].Add(new ChartValueSample { value = y, timeMs = timeMs });
                    lastChartValues[chartKey][idx] = y;
                }
            }
        }
    }

    #region === Recording Control ===

    public void StartRecording()
    {
        // 1) Objects
        positionHistory.Clear();
        lastPosition.Clear();
        foreach (var go in targets)
            if (go != null) positionHistory[go.name] = new List<PositionSample>();

        // 2) Cameras
        cameraHistory.Clear();
        lastCameraPosition.Clear();
        lastCameraForward.Clear();
        foreach (var cam in cameras)
            if (cam != null) cameraHistory[cam.name] = new List<CameraSample>();

        // 3) Charts
        chartHistory.Clear();
        lastChartValues.Clear();
        // (no prefill needed; will create on the fly during Update)

        isRecording = true;
        startTime = Time.time;
        Debug.Log("[Save] Start recording");
    }

    public void StopRecordingAndSave()
    {
        isRecording = false;
        SaveTrajectory();
        Debug.Log("[Save] Stop recording and save");
    }

    #endregion

    #region === Helpers ===

    private static bool ExceedThresholdVec3(Vector3 a, Vector3 b, float threshold)
    {
        return Mathf.Abs(a.x - b.x) > threshold ||
               Mathf.Abs(a.y - b.y) > threshold ||
               Mathf.Abs(a.z - b.z) > threshold;
    }

    private static string MakeChartKey(string chartName, int serieIndex)
    {
        return $"{chartName}|{serieIndex}";
    }

    private static List<int> BuildAllIndices(int count)
    {
        var list = new List<int>(count);
        for (int i = 0; i < count; i++) list.Add(i);
        return list;
    }

    #endregion

    #region === Save JSON ===

    private void SaveTrajectory()
    {
        TrajectorySaveData saveData = new TrajectorySaveData();

        // 1) Objects
        foreach (var kvp in positionHistory)
        {
            ObjectTrajectory obj = new ObjectTrajectory
            {
                objectName = kvp.Key,
                samples = kvp.Value
            };
            saveData.objects.Add(obj);
        }

        // 2) Cameras
        foreach (var kvp in cameraHistory)
        {
            CameraTrajectory cam = new CameraTrajectory
            {
                cameraName = kvp.Key,
                samples = kvp.Value
            };
            saveData.cameras.Add(cam);
        }

        // 3) Charts
        foreach (var ct in charts)
        {
            if (ct == null || ct.chart == null) continue;

            string chartName = string.IsNullOrEmpty(ct.jsonChartNameOverride)
                ? ct.chart.gameObject.name
                : ct.jsonChartNameOverride;

            string chartKey = MakeChartKey(chartName, ct.serieIndex);
            if (!chartHistory.ContainsKey(chartKey)) continue;

            var serieBlock = new ChartSerieTrajectory
            {
                chartName = chartName,
                serieIndex = ct.serieIndex
            };

            foreach (var idxToSamples in chartHistory[chartKey])
            {
                var point = new ChartPointSeries
                {
                    dataIndex = idxToSamples.Key,
                    samples = idxToSamples.Value
                };
                serieBlock.points.Add(point);
            }

            saveData.charts.Add(serieBlock);
        }

        string json = JsonUtility.ToJson(saveData, true);
        SaveManager.Save(json);
        Debug.Log("[Save] Saved trajectory (objects, cameras, charts).");
    }

    #endregion

    #region === Serializable Data Structures ===

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

    // ---- NEW: Charts ----

    [System.Serializable]
    public class ChartValueSample
    {
        public float value;
        public int timeMs;
    }

    [System.Serializable]
    public class ChartPointSeries
    {
        public int dataIndex; // index within the serie
        public List<ChartValueSample> samples = new List<ChartValueSample>();
    }

    [System.Serializable]
    public class ChartSerieTrajectory
    {
        public string chartName;     // e.g., "LineChart"
        public int serieIndex;       // e.g., 0
        public List<ChartPointSeries> points = new List<ChartPointSeries>();
    }

    [System.Serializable]
    public class TrajectorySaveData
    {
        public List<ObjectTrajectory> objects = new List<ObjectTrajectory>();
        public List<CameraTrajectory> cameras = new List<CameraTrajectory>();

        // NEW block for charts
        public List<ChartSerieTrajectory> charts = new List<ChartSerieTrajectory>();
    }

    #endregion
}
