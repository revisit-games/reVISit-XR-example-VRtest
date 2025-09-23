using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using Unity.VisualScripting;
using UnityEngine;
using static UnityEditor.PlayerSettings;

public class Save : MonoBehaviour
{
    [Header("GameObjects To Record (Position Data)")]
    [SerializeField]
    private List<GameObject> targets = new List<GameObject>();

    [Header("Cameras To Record (Position & Forward Data)")]
    [SerializeField]
    private List<GameObject> cameras = new List<GameObject>();

    private Dictionary<string, List<Vector3>> positionHistory = new Dictionary<string, List<Vector3>>();

    // For cameras, to store both position and forward direction
    private Dictionary<string, List<CameraSample>> cameraHistory = new Dictionary<string, List<CameraSample>>();
    private Coroutine recordCoroutine;

    private bool isRecording = false;

    private void Awake()
    {
        SaveManager.Init();
    }

    void Update()
    {

    }

    // Call this method to start recording positions (Call by setting buttons in the UI)
    public void StartRecording()
    {
        positionHistory.Clear();
        foreach (var go in targets)
        {
            if (go != null)
                positionHistory[go.name] = new List<Vector3>();
        }

        cameraHistory.Clear();
        foreach (var cam in cameras)
        {
            if (cam != null)
                cameraHistory[cam.name] = new List<CameraSample>();
        }

        isRecording = true;
        recordCoroutine = StartCoroutine(RecordPositionCoroutine());
        Debug.Log("Start recording");
    }

    // Call this method to stop recording and save the trajectory (Call by setting buttons in the UI)
    public void StopRecordingAndSave()
    {
        isRecording = false;
        if (recordCoroutine != null)
        {
            StopCoroutine(recordCoroutine);
        }
        SaveTrajectory();
        Debug.Log("Stop recording and save");
    }

    private IEnumerator RecordPositionCoroutine()
    {
        while (true)
        {
            // Record positions of normal GameObjects
            foreach (var go in targets)
            {
                if (go != null)
                {
                    if (!positionHistory.ContainsKey(go.name))
                        positionHistory[go.name] = new List<Vector3>();
                    positionHistory[go.name].Add(go.transform.position);
                }
            }

            // Record position and forward direction of cameras
            foreach (var cam in cameras)
            {
                if (cam != null)
                {
                    if (!cameraHistory.ContainsKey(cam.name))
                        cameraHistory[cam.name] = new List<CameraSample>();
                    cameraHistory[cam.name].Add(new CameraSample
                    {
                        position = cam.transform.position,
                        forward = cam.transform.forward
                    });
                }
            }

            // Record every 0.5 seconds
            yield return new WaitForSeconds(0.5f);
        }
    }

    private void SaveTrajectory()
    {
        TrajectorySaveData saveData = new TrajectorySaveData();

        // Save normal GameObject data
        foreach (var kvp in positionHistory)
        {
            ObjectTrajectory obj = new ObjectTrajectory();
            obj.objectName = kvp.Key;
            obj.positions = kvp.Value;
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

    [System.Serializable]
    public class ObjectTrajectory
    {
        public string objectName;
        public List<Vector3> positions = new List<Vector3>();
    }

    [System.Serializable]
    public class CameraSample
    {
        public Vector3 position;
        public Vector3 forward;
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
}
