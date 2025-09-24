using UnityEngine;

public class Billboard : MonoBehaviour
{
    [Header("Target Camera (Drag your camera here)")]
    [SerializeField]
    private Camera targetCamera;

    [Header("Custom Rotation Offset (degrees)")]
    [Tooltip("Rotation offset applied after LookAt.")]
    [SerializeField]
    private Vector3 rotationOffset = new Vector3(0f, 180f, 0f);

    void LateUpdate()
    {
        if (targetCamera != null)
        {
            transform.LookAt(targetCamera.transform);
            transform.Rotate(rotationOffset);
        }
    }
}
