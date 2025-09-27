using System.IO;
using System.Collections.Generic;
using UnityEngine;
using XCharts.Runtime; // XCharts runtime API

public class Save : MonoBehaviour
{
    #region === Inspector: Objects & Cameras ===

    [System.Serializable]
    public class ObjectRecordTarget
    {
        [Tooltip("The GameObject to record (position only).")]
        public GameObject target;

        [Tooltip("Optional JSON name override. If empty, target.name will be used.")]
        public string jsonNameOverride = "";
    }

    [System.Serializable]
    public class CameraRecordTarget
    {
        [Tooltip("The Camera/Pointer GameObject to record (position + forward).")]
        public GameObject target;

        [Tooltip("Optional JSON name override. If empty, target.name will be used.")]
        public string jsonNameOverride = "";
    }

    [Header("Normal GameObjects To Record (Position Data)")]
    [SerializeField] private List<ObjectRecordTarget> targets = new List<ObjectRecordTarget>();

    [Header("Cameras (or Pointers) To Record (Position & Forward Data)")]
    [SerializeField] private List<CameraRecordTarget> cameras = new List<CameraRecordTarget>();

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

    // Object position history (key = json name key)
    private Dictionary<string, List<PositionSample>> positionHistory = new Dictionary<string, List<PositionSample>>();
    private Dictionary<string, Vector3> lastPosition = new Dictionary<string, Vector3>();

    // Camera history (key = json name key)
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

        // ---------- 1) Normal GameObjects (position only) ----------
        foreach (var entry in targets)
        {
            if (entry == null || entry.target == null) continue;
            string key = GetObjectKey(entry);
            Vector3 current = entry.target.transform.position;

            if (!lastPosition.ContainsKey(key) ||
                ExceedThresholdVec3(current, lastPosition[key], objectPositionThreshold))
            {
                if (!positionHistory.ContainsKey(key))
                    positionHistory[key] = new List<PositionSample>();

                positionHistory[key].Add(new PositionSample { position = current, timeMs = timeMs });
                lastPosition[key] = current;
            }
        }

        // ---------- 2) Cameras / Pointers (position + forward) ----------
        foreach (var entry in cameras)
        {
            if (entry == null || entry.target == null) continue;
            string key = GetCameraKey(entry);

            Vector3 currentPos = entry.target.transform.position;
            Vector3 currentFwd = entry.target.transform.forward;

            bool needRecord = false;
            if (!lastCameraPosition.ContainsKey(key) ||
                ExceedThresholdVec3(currentPos, lastCameraPosition[key], cameraPositionThreshold))
                needRecord = true;

            if (!lastCameraForward.ContainsKey(key) ||
                ExceedThresholdVec3(currentFwd, lastCameraForward[key], cameraForwardThreshold))
                needRecord = true;

            if (needRecord)
            {
                if (!cameraHistory.ContainsKey(key))
                    cameraHistory[key] = new List<CameraSample>();

                cameraHistory[key].Add(new CameraSample
                {
                    position = currentPos,
                    forward = currentFwd,
                    timeMs = timeMs
                });

                lastCameraPosition[key] = currentPos;
                lastCameraForward[key] = currentFwd;
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
        foreach (var entry in targets)
            if (entry != null && entry.target != null)
                positionHistory[GetObjectKey(entry)] = new List<PositionSample>();

        // 2) Cameras
        cameraHistory.Clear();
        lastCameraPosition.Clear();
        lastCameraForward.Clear();
        foreach (var entry in cameras)
            if (entry != null && entry.target != null)
                cameraHistory[GetCameraKey(entry)] = new List<CameraSample>();

        // 3) Charts
        chartHistory.Clear();
        lastChartValues.Clear(); // will be created on the fly

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

    private static string GetObjectKey(ObjectRecordTarget entry)
    {
        return string.IsNullOrEmpty(entry.jsonNameOverride) ? entry.target.name : entry.jsonNameOverride;
    }

    private static string GetCameraKey(CameraRecordTarget entry)
    {
        return string.IsNullOrEmpty(entry.jsonNameOverride) ? entry.target.name : entry.jsonNameOverride;
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

    // ---- Charts ----

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

        // Block for charts
        public List<ChartSerieTrajectory> charts = new List<ChartSerieTrajectory>();
    }

    #endregion
}
