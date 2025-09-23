using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using Unity.VisualScripting;
using UnityEngine;
using static UnityEditor.PlayerSettings;

public class Save : MonoBehaviour
{
    [SerializeField]
    private List<GameObject> targets = new List<GameObject>(); // Inspector 拖入

    private Dictionary<string, List<Vector3>> positionHistory = new Dictionary<string, List<Vector3>>();
    private Coroutine recordCoroutine;
    private bool isRecording = false;

    private void Awake()
    {
        SaveManager.Init();
    }

    void Update()
    {
        
    }

    public void StartRecording()
    {
        positionHistory.Clear();
        foreach (var go in targets)
        {
            if (go != null)
                positionHistory[go.name] = new List<Vector3>();
        }
        isRecording = true;
        recordCoroutine = StartCoroutine(RecordPositionCoroutine());
        Debug.Log("Start recording");
    }

    // Stop recording and save trajectory
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

    // Recording methods
    private IEnumerator RecordPositionCoroutine()
    {
        while (true)
        {
            foreach (var go in targets)
            {
                if (go != null)
                {
                    if (!positionHistory.ContainsKey(go.name))
                        positionHistory[go.name] = new List<Vector3>();
                    positionHistory[go.name].Add(go.transform.position);
                }
            }
            yield return new WaitForSeconds(0.5f);
        }
    }

    // Save trajectory to JOSN file
    private void SaveTrajectory()
    {
        // 转换为可序列化结构
        TrajectorySaveData saveData = new TrajectorySaveData();
        foreach (var kvp in positionHistory)
        {
            ObjectTrajectory obj = new ObjectTrajectory();
            obj.objectName = kvp.Key;
            obj.positions = kvp.Value;
            saveData.objects.Add(obj);
        }
        string json = JsonUtility.ToJson(saveData, true);
        SaveManager.Save(json);
        Debug.Log("Save trajectory");
    }

    // Data structure for saving data (positions and key events)
    [System.Serializable]
    public class ObjectTrajectory
    {
        public string objectName;
        public List<Vector3> positions = new List<Vector3>();
    }

    [System.Serializable]
    public class TrajectorySaveData
    {
        public List<ObjectTrajectory> objects = new List<ObjectTrajectory>();
    }
}
