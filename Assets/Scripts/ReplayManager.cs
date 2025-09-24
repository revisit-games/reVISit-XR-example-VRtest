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

    private TrajectorySaveData loadedData;
    private bool isReplaying = false;
    private float replayStartTime;
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
            return;
        }
        string json = File.ReadAllText(path);
        loadedData = JsonUtility.FromJson<TrajectorySaveData>(json);
        Debug.Log("Replay data loaded from: " + path);
    }

    /// <summary>
    /// Starts the replay. All targets will follow their recorded trajectories.
    /// </summary>
    public void StartReplay()
    {
        if (loadedData == null)
        {
            Debug.LogWarning("No replay data loaded.");
            return;
        }
        isReplaying = true;
        replayStartTime = Time.time;
        replayIndices.Clear();
        foreach (var t in replayTargets)
            replayIndices[t] = 0;
    }

    /// <summary>
    /// Stops the replay.
    /// </summary>
    public void StopReplay()
    {
        isReplaying = false;
    }

    void Update()
    {
        if (!isReplaying || loadedData == null) return;

        float elapsedMs = (Time.time - replayStartTime) * 1000f;

        foreach (var t in replayTargets)
        {
            if (t.target == null || string.IsNullOrEmpty(t.jsonName)) continue;

            if (t.isCamera)
            {
                var camData = loadedData.cameras.Find(c => c.cameraName == t.jsonName);
                if (camData == null || camData.samples.Count == 0) continue;
                int idx = replayIndices[t];

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
                int idx = replayIndices[t];

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