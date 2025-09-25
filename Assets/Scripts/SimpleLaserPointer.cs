using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class SimpleLaserPointer : MonoBehaviour
{
    [Header("Camera Reference")]
    [Tooltip("The camera used for billboarding/offset. Drag the camera here. If null, falls back to Camera.main at runtime.")]
    public Camera targetCamera;

    [Header("Ray Settings")]
    [Tooltip("Maximum ray length (meters)")]
    public float maxDistance = 20f;

    [Tooltip("Layer mask for physics raycast")]
    public LayerMask hitMask = ~0;  // Hit all layers

    [Tooltip("Should the ray hit Trigger Colliders")]
    public QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;

    [Header("Ray Origin Offset")]
    [Tooltip("Offset (x, y, z) from this GameObject's position for the calibration of the ray start")]
    public Vector3 rayOriginOffset = Vector3.zero;

    [Header("Line Visual")]
    [Tooltip("Line width (start)")]
    public float startWidth = 0.0025f;

    [Tooltip("Line width (end)")]
    public float endWidth = 0.0025f;

    [Tooltip("Clamp line end to hit point if hit; otherwise extend to maxDistance")]
    public bool clampToHitPoint = true;

    [Header("Reticle (Hit Dot)")]
    [Tooltip("Extra offset along the ray direction (positive = closer to origin, negative = farther)")]
    public float reticleDistanceOffset = 0f;

    [Tooltip("Reticle prefab (Quad/Sprite, etc.)")]
    public GameObject reticlePrefab;

    [Tooltip("Offset from hit point to surface to avoid Z-fighting")]
    public float reticleSurfaceOffset = 0.002f;

    [Tooltip("Base scale of the reticle at the hit point (meters)")]
    public float reticleBaseScale = 0.02f;

    [Tooltip("Slightly scale reticle with distance (larger when farther)")]
    public bool reticleScaleWithDistance = true;

    [Tooltip("Should the reticle face the main camera (billboard effect). If off, align to surface normal.")]
    public bool reticleFaceCamera = false;

    private LineRenderer _line;
    private Transform _reticleInstance;
    private Camera _mainCam;

    void Awake()
    {
        _line = GetComponent<LineRenderer>();
        _line.positionCount = 2;
        _line.startWidth = startWidth;
        _line.endWidth = endWidth;
        _line.useWorldSpace = true;

        // If no material is set, try to use a default one (optional)
        if (_line.sharedMaterial == null)
        {
            var mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = new Color(0f, 0.8f, 1f, 0.75f);
            _line.material = mat;
        }

        if (reticlePrefab != null)
        {
            var go = Instantiate(reticlePrefab);
            go.name = reticlePrefab.name + " (Runtime)";
            _reticleInstance = go.transform;
            _reticleInstance.gameObject.SetActive(false);
        }

        _mainCam = targetCamera != null ? targetCamera : Camera.main; // Using a regular camera, make sure there is a MainCamera tag in the scene
    }

    void Update()
    {
        // Apply the offset in local space
        Vector3 origin = transform.TransformPoint(rayOriginOffset);
        Vector3 dir = transform.forward;

        RaycastHit hit;
        bool hasHit = Physics.Raycast(origin, dir, out hit, maxDistance, hitMask, triggerInteraction);

        Vector3 endPoint = hasHit ? hit.point : origin + dir * maxDistance;

        // Draw line
        _line.SetPosition(0, origin);
        _line.SetPosition(1, clampToHitPoint ? endPoint : origin + dir * maxDistance);

        // Reticle (hit dot)
        if (_reticleInstance != null)
        {
            if (hasHit)
            {
                _reticleInstance.gameObject.SetActive(true);

                Vector3 n = hit.normal;
                if (Vector3.Dot(dir, n) > 0f) n = -n;

                // Offset reticle slightly toward the camera to avoid hiding behind the surface
                Vector3 towardCam = (_mainCam != null)
                    ? (_mainCam.transform.position - endPoint).normalized
                    : -dir;

                Vector3 pos = endPoint + towardCam * reticleSurfaceOffset;
                pos -= dir.normalized * reticleDistanceOffset;
                _reticleInstance.position = pos;

                // Orientation: align to surface normal or face camera
                if (reticleFaceCamera && _mainCam != null)
                {
                    // Face camera (billboard)
                    _reticleInstance.rotation = Quaternion.LookRotation( -(_reticleInstance.position - _mainCam.transform.position), Vector3.up);

                }
                else
                {
                    // Align to surface
                    _reticleInstance.rotation = Quaternion.LookRotation(n);
                }

                // Scale
                float s = reticleBaseScale;
                if (reticleScaleWithDistance)
                {
                    float d = Vector3.Distance(origin, endPoint);
                    // Slightly larger when farther, but clamp range
                    s *= Mathf.Clamp01(0.2f + d / maxDistance); // 0.2x ~ 1x
                }
                _reticleInstance.localScale = Vector3.one * s;
            }
            else
            {
                _reticleInstance.gameObject.SetActive(false);
            }
        }
    }

    // Allow runtime width adjustment
    void OnValidate()
    {
        if (_line == null) _line = GetComponent<LineRenderer>();
        if (_line != null)
        {
            _line.startWidth = startWidth;
            _line.endWidth = endWidth;
        }
    }
}
