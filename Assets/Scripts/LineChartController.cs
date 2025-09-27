// LineChartController.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;
using XCharts.Runtime;

public class LineChartController : MonoBehaviour
{
    [Header("Chart")]
    [Tooltip("Reference to the XCharts LineChart in the scene.")]
    public LineChart chart;
    [Tooltip("Which serie to control (0 = 'Serie 0: Line').")]
    public int serieIndex = 0;

    [Header("Label Formatting")]
    [Tooltip("Value text format, e.g. {0:0}, {0:0.0}, {0:0.##}")]
    public string valueFormat = "{0:0}";
    public string valuePrefix = "";
    public string valueSuffix = "";

    [Header("Initialization")]
    [Tooltip("On Start, read current Y values from chart and set the sliders to match.")]
    public bool readInitialFromChart = true;

    [System.Serializable]
    public class PointBinding
    {
        [Tooltip("Data index in the serie (0-based).")]
        public int dataIndex;

        [Tooltip("Slider that controls this data point.")]
        public Slider slider;

        [Tooltip("TMP text to display the current value (usually a child of the handle).")]
        public TMP_Text valueText;

        // cached listener so we can remove it cleanly
        [System.NonSerialized] public UnityAction<float> listener;
    }

    [Header("Bindings")]
    [Tooltip("Create one element per data point you want to control.")]
    public List<PointBinding> bindings = new List<PointBinding>();

    void Reset()
    {
        // Try to auto-find a LineChart in children if not set.
        if (chart == null) chart = GetComponentInChildren<LineChart>(true);
    }

    void Awake()
    {
        if (chart == null) chart = GetComponentInChildren<LineChart>(true);
    }

    void Start()
    {
        if (chart == null)
        {
            Debug.LogError("[LineChartController] Chart is not assigned.");
            return;
        }

        var serie = chart.GetSerie(serieIndex);
        if (serie == null)
        {
            Debug.LogError($"[LineChartController] Serie {serieIndex} not found on chart.");
            return;
        }

        foreach (var b in bindings)
        {
            if (b == null || b.slider == null) continue;

            int idx = b.dataIndex;
            if (idx < 0 || idx >= serie.dataCount)
            {
                Debug.LogWarning($"[LineChartController] dataIndex {idx} out of range (0..{serie.dataCount - 1}). Skipped.");
                continue;
            }

            // 1) Set initial slider value from chart data (optional)
            if (readInitialFromChart)
            {
                var sd = serie.GetSerieData(idx);
                if (sd != null)
                {
                    double y = sd.GetData(1);          // dimension 1 = Y
                    float v = (float)y;
                    // clamp to slider range so UI stays valid
                    v = Mathf.Clamp(v, b.slider.minValue, b.slider.maxValue);
                    b.slider.SetValueWithoutNotify(v);
                    UpdateValueLabel(b.valueText, v);
                }
            }

            // 2) Hook the slider ¡ú chart update
            //    Capture local copies for the closure
            var localIdx = idx;
            var localBinding = b;
            b.listener = (val) =>
            {
                ApplyToChart(localIdx, val);
                UpdateValueLabel(localBinding.valueText, val);
            };
            b.slider.onValueChanged.AddListener(b.listener);
        }

        chart.RefreshChart();
    }

    void OnDestroy()
    {
        // Cleanly remove listeners we attached
        foreach (var b in bindings)
        {
            if (b?.slider != null && b.listener != null)
                b.slider.onValueChanged.RemoveListener(b.listener);
        }
    }

    private void ApplyToChart(int dataIndex, float yValue)
    {
        if (chart == null) return;
        // Write Y (dimension = 1)
        chart.UpdateData(serieIndex, dataIndex, 1, yValue);
        chart.RefreshChart();
    }

    private void UpdateValueLabel(TMP_Text label, float value)
    {
        if (label == null) return;
        string core = string.Format(valueFormat, value);
        label.text = $"{valuePrefix}{core}{valueSuffix}";
    }

    // ----- Optional helpers -----

    /// <summary>Push all current slider values into the chart.</summary>
    public void PushSlidersToChart()
    {
        foreach (var b in bindings)
        {
            if (b?.slider == null) continue;
            ApplyToChart(b.dataIndex, b.slider.value);
            UpdateValueLabel(b.valueText, b.slider.value);
        }
    }

    /// <summary>Pull current values from the chart into sliders/labels.</summary>
    public void PullChartToSliders()
    {
        var serie = chart?.GetSerie(serieIndex);
        if (serie == null) return;

        foreach (var b in bindings)
        {
            if (b?.slider == null) continue;
            if (b.dataIndex < 0 || b.dataIndex >= serie.dataCount) continue;

            var sd = serie.GetSerieData(b.dataIndex);
            if (sd == null) continue;

            float v = (float)sd.GetData(1);
            v = Mathf.Clamp(v, b.slider.minValue, b.slider.maxValue);
            b.slider.SetValueWithoutNotify(v);
            UpdateValueLabel(b.valueText, v);
        }
    }
}
