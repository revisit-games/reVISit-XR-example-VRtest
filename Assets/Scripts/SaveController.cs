using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using Unity.VisualScripting;
using UnityEngine;
using static UnityEditor.PlayerSettings;

public class Save : MonoBehaviour
{
    [Header("Normal GameObjects To Record (Position Data)")]
    [SerializeField]
    private List<GameObject> targets = new List<GameObject>();

    [Header("Cameras (or Pointers) To Record (Position & Forward Data)")]
    [SerializeField]
    private List<GameObject> cameras = new List<GameObject>();

    [Header("Record Threshold Settings")]
    [SerializeField] private float objectPositionThreshold = 0.04f;
    [SerializeField] private float cameraPositionThreshold = 0.04f;
    [SerializeField] private float cameraForwardThreshold = 0.02f;

    private Dictionary<string, List<PositionSample>> positionHistory = new Dictionary<string, List<PositionSample>>();
    private Dictionary<string, List<CameraSample>> cameraHistory = new Dictionary<string, List<CameraSample>>();

    private Dictionary<string, Vector3> lastPosition = new Dictionary<string, Vector3>();
    private Dictionary<string, Vector3> lastCameraPosition = new Dictionary<string, Vector3>();
    private Dictionary<string, Vector3> lastCameraForward = new Dictionary<string, Vector3>();

    private bool isRecording = false;
    private float startTime = 0f;

    private void Awake()
    {
        SaveManager.Init();
    }

    void Update()
    {
        if (!isRecording) return;

        int timeMs = (int)((Time.time - startTime) * 1000);

        // Check and record for normal GameObjects
        foreach (var go in targets)
        {
            if (go == null) continue;
            Vector3 current = go.transform.position;
            if (!lastPosition.ContainsKey(go.name) || ExceedThreshold(current, lastPosition[go.name], objectPositionThreshold))
            {
                if (!positionHistory.ContainsKey(go.name))
                    positionHistory[go.name] = new List<PositionSample>();

                // Record the new position with timestamp
                positionHistory[go.name].Add(new PositionSample { position = current, timeMs = timeMs });
                lastPosition[go.name] = current;

                // Debug info for normal GameObjects
                //Debug.Log($"[Record] GameObject '{go.name}' at {timeMs} ms: Position = ({current.x:F4}, {current.y:F4}, {current.z:F4})");
            }
        }

        // Check and record for cameras
        foreach (var cam in cameras)
        {
            // Skip if the camera GameObject is null or does not have a Camera component
            if (cam == null) continue;

            // Get current position and forward direction of the camera
            Vector3 currentPos = cam.transform.position;
            Vector3 currentFwd = cam.transform.forward;

            // Determine if we need to record this sample based on position/forward changes
            bool needRecord = false;
            if (!lastCameraPosition.ContainsKey(cam.name) || ExceedThreshold(currentPos, lastCameraPosition[cam.name], cameraPositionThreshold))
                needRecord = true;
            if (!lastCameraForward.ContainsKey(cam.name) || ExceedThreshold(currentFwd, lastCameraForward[cam.name], cameraForwardThreshold))
                needRecord = true;

            if (needRecord)
            {
                // Initialize list if not already present
                if (!cameraHistory.ContainsKey(cam.name))
                    cameraHistory[cam.name] = new List<CameraSample>();

                // Record the new camera sample with timestamp
                cameraHistory[cam.name].Add(new CameraSample { position = currentPos, forward = currentFwd, timeMs = timeMs });
                lastCameraPosition[cam.name] = currentPos;
                lastCameraForward[cam.name] = currentFwd;

                // Debug info for cameras
                //Debug.Log($"[Record] Camera '{cam.name}' at {timeMs} ms: Position = ({currentPos.x:F4}, {currentPos.y:F4}, {currentPos.z:F4}), Forward = ({currentFwd.x:F4}, {currentFwd.y:F4}, {currentFwd.z:F4})");
            }
        }
    }

    public void StartRecording()
    {
        // For normal GameObjects: Clear previous data if any
        positionHistory.Clear();
        lastPosition.Clear();

        foreach (var go in targets)
        {
            if (go != null)
                positionHistory[go.name] = new List<PositionSample>();
        }

        // For camera GameObjects: Clear previous data if any
        cameraHistory.Clear();
        lastCameraPosition.Clear();
        lastCameraForward.Clear();

        foreach (var cam in cameras)
        {
            if (cam != null)
                cameraHistory[cam.name] = new List<CameraSample>();
        }

        isRecording = true;
        startTime = Time.time;
        Debug.Log("Start recording");
    }

    public void StopRecordingAndSave()
    {
        isRecording = false;
        SaveTrajectory();
        Debug.Log("Stop recording and save");
    }

    // Check if the difference between two Vector3 exceeds the threshold in any dimension
    private bool ExceedThreshold(Vector3 a, Vector3 b, float threshold)
    {
        return Mathf.Abs(a.x - b.x) > threshold ||
               Mathf.Abs(a.y - b.y) > threshold ||
               Mathf.Abs(a.z - b.z) > threshold;
    }

    private void SaveTrajectory()
    {
        TrajectorySaveData saveData = new TrajectorySaveData();

        // Save normal GameObject data
        foreach (var kvp in positionHistory)
        {
            ObjectTrajectory obj = new ObjectTrajectory();
            obj.objectName = kvp.Key;
            obj.samples = kvp.Value;
            saveData.objects.Add(obj);
        }

        // Save camera data
        foreach (var kvp in cameraHistory)
        {
            CameraTrajectory cam = new CameraTrajectory();
            cam.cameraName = kvp.Key;
            cam.samples = kvp.Value;
            saveData.cameras.Add(cam);
        }

        string json = JsonUtility.ToJson(saveData, true);
        SaveManager.Save(json);
        Debug.Log("Save trajectory");
    }

    // Data structures for serialization
    [System.Serializable]
    public class PositionSample
    {
        public Vector3 position;
        public int timeMs;
    }

    // Data structure for camera samples, including position, forward direction, and timestamp
    [System.Serializable]
    public class CameraSample
    {
        public Vector3 position;
        public Vector3 forward;
        public int timeMs;
    }

    // Data structure for storing trajectory of a single GameObject
    [System.Serializable]
    public class ObjectTrajectory
    {
        public string objectName;
        public List<PositionSample> samples = new List<PositionSample>();
    }

    // Data structure for storing trajectory of a single camera
    [System.Serializable]
    public class CameraTrajectory
    {
        public string cameraName;
        public List<CameraSample> samples = new List<CameraSample>();
    }

    // Overall save data structure containing all GameObject and camera trajectories
    [System.Serializable]
    public class TrajectorySaveData
    {
        public List<ObjectTrajectory> objects = new List<ObjectTrajectory>();
        public List<CameraTrajectory> cameras = new List<CameraTrajectory>();
    }
}
