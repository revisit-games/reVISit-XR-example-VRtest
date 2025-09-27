using UnityEngine;

/// <summary>
/// Ray visualizer for replayed controllers:
/// - Uses this transform's position/forward (driven by your replay) to cast a Physics ray.
/// - Draws a LineRenderer (clamped to hit point if any).
/// - Shows a reticle only when there is a hit (logic borrowed from SimpleLaserPointer).
/// - NEW: Supports custom reticle color via inspector.
/// </summary>
[RequireComponent(typeof(Transform))]
public class ReplayedRayVisualizer : MonoBehaviour
{
    [Header("Camera Reference")]
    public Camera targetCamera;

    [Header("Ray Settings")]
    public float maxDistance = 20f;
    public LayerMask hitMask = ~0;
    public QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;

    [Header("Ray Origin Offset")]
    public Vector3 rayOriginOffset = Vector3.zero;

    [Header("Line Visual")]
    public LineRenderer line;
    public float startWidth = 0.0025f;
    public float endWidth = 0.0025f;
    public bool clampToHitPoint = true;

    [Header("Reticle (Hit Dot)")]
    public GameObject reticlePrefab;
    public float reticleDistanceOffset = 0f;
    public float reticleSurfaceOffset = 0.002f;
    public float reticleBaseScale = 0.02f;
    public bool reticleScaleWithDistance = true;
    public bool reticleFaceCamera = false;

    [Header("Reticle Appearance")]
    [Tooltip("Custom color applied to the reticle (if it has a Renderer).")]
    public Color reticleColor = Color.white;

    [Header("Reticle Layer (avoid self-hit)")]
    public string reticleLayerName = "Reticle";

    // --- runtime ---
    private Camera _cam;
    private Transform _reticleInstance;
    private int _reticleLayer = -1;

    void Awake()
    {
        _cam = targetCamera != null ? targetCamera : Camera.main;

        // Ensure LineRenderer
        if (line == null)
        {
            line = gameObject.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            var mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = new Color(0f, 0.8f, 1f, 0.75f);
            line.material = mat;
        }
        line.startWidth = startWidth;
        line.endWidth = endWidth;

        // Prepare reticle layer
        _reticleLayer = LayerMask.NameToLayer(reticleLayerName);
        if (_reticleLayer >= 0)
        {
            hitMask &= ~(1 << _reticleLayer);
        }

        // Instantiate reticle (if provided)
        if (reticlePrefab != null)
        {
            var go = Instantiate(reticlePrefab);
            go.name = reticlePrefab.name + " (Runtime)";
            if (_reticleLayer >= 0) go.layer = _reticleLayer;

            var col = go.GetComponent<Collider>();
            if (col) Destroy(col);

            _reticleInstance = go.transform;
            _reticleInstance.gameObject.SetActive(false);

            ApplyReticleColor(_reticleInstance.gameObject, reticleColor);
        }
    }

    void Update()
    {
        Vector3 origin = transform.TransformPoint(rayOriginOffset);
        Vector3 dir = transform.forward;

        bool hasHit = Physics.Raycast(origin, dir, out RaycastHit hit, maxDistance, hitMask, triggerInteraction);
        Vector3 endPoint = hasHit ? hit.point : origin + dir * maxDistance;

        // LineRenderer
        if (line != null)
        {
            line.SetPosition(0, origin);
            line.SetPosition(1, clampToHitPoint ? endPoint : origin + dir * maxDistance);
        }

        // Reticle
        if (_reticleInstance != null)
        {
            if (hasHit)
            {
                if (!_reticleInstance.gameObject.activeSelf)
                    _reticleInstance.gameObject.SetActive(true);

                Vector3 n = hit.normal;
                if (Vector3.Dot(dir, n) > 0f) n = -n;

                Vector3 towardCam = (_cam != null)
                    ? (_cam.transform.position - endPoint).normalized
                    : -dir;

                Vector3 pos = endPoint + towardCam * reticleSurfaceOffset;
                pos -= dir.normalized * reticleDistanceOffset;
                _reticleInstance.position = pos;

                if (reticleFaceCamera && _cam != null)
                {
                    _reticleInstance.rotation = Quaternion.LookRotation(-(_reticleInstance.position - _cam.transform.position), Vector3.up);
                }
                else
                {
                    _reticleInstance.rotation = Quaternion.LookRotation(n);
                }

                float s = reticleBaseScale;
                if (reticleScaleWithDistance)
                {
                    float d = Vector3.Distance(origin, endPoint);
                    s *= Mathf.Clamp01(0.2f + d / maxDistance);
                }
                _reticleInstance.localScale = Vector3.one * s;
            }
            else
            {
                if (_reticleInstance.gameObject.activeSelf)
                    _reticleInstance.gameObject.SetActive(false);
            }
        }
    }

    void OnValidate()
    {
        if (line != null)
        {
            line.startWidth = startWidth;
            line.endWidth = endWidth;
        }

        // If reticle already exists in editor, recolor
        if (_reticleInstance != null)
        {
            ApplyReticleColor(_reticleInstance.gameObject, reticleColor);
        }
    }

    private void ApplyReticleColor(GameObject reticle, Color color)
    {
        if (reticle == null) return;

        var renderer = reticle.GetComponent<Renderer>();
        if (renderer != null && renderer.material != null)
        {
            renderer.material.color = color;
        }
        else
        {
            // Create simple material if none
            var mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = color;
            if (renderer != null) renderer.material = mat;
        }
    }
}
