using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class UnityEventFloat : UnityEvent<float> { }

public enum KeyTrigger
{
    Press,   // fired on GetKeyDown
    Release, // fired on GetKeyUp
    Hold     // fired every frame while key is held (GetKey)
}

[Serializable]
public class KeyBinding
{
    [Tooltip("Key name, e.g. M, P, Alpha1, RightArrow, etc.")]
    public string keyName = "RightArrow";

    [Tooltip("When should the event be fired? Press = KeyDown, Release = KeyUp, Hold = every frame while held.")]
    public KeyTrigger trigger = KeyTrigger.Press;

    [Header("Events (no args)")]
    [Tooltip("Invoked when the trigger condition is met (no arguments).")]
    public UnityEvent onTriggered;

    [Header("Events (float arg, seconds)")]
    [Tooltip("Invoked with a float argument (seconds). Useful to call ReplayManager.NudgeBySeconds(float).")]
    public UnityEventFloat onTriggeredSeconds;

    [Header("Hold Settings")]
    [Tooltip("Only used when trigger = Hold. Units: seconds per second. Positive = fast-forward, negative = rewind.")]
    public float holdSecondsPerSecond = 2f;

    [Tooltip("If true, also fire the no-arg event each frame while holding.")]
    public bool alsoInvokeNoArgWhileHold = false;
}

public class ReplayController : MonoBehaviour
{
    [Header("Key Bindings")]
    public List<KeyBinding> keyBindings = new List<KeyBinding>();

    void Update()
    {
        foreach (var binding in keyBindings)
        {
            if (string.IsNullOrEmpty(binding.keyName)) continue;

            if (!Enum.TryParse<KeyCode>(binding.keyName, true, out var keyCode))
                continue;

            switch (binding.trigger)
            {
                case KeyTrigger.Press:
                    if (Input.GetKeyDown(keyCode))
                    {
                        // no-arg event
                        binding.onTriggered?.Invoke();
                        // float-arg event: commonly use +X seconds or any custom value
                        if (binding.onTriggeredSeconds != null)
                            binding.onTriggeredSeconds.Invoke(0f); // 0 by default; set via custom method if needed
                    }
                    break;

                case KeyTrigger.Release:
                    if (Input.GetKeyUp(keyCode))
                    {
                        binding.onTriggered?.Invoke();
                        if (binding.onTriggeredSeconds != null)
                            binding.onTriggeredSeconds.Invoke(0f);
                    }
                    break;

                case KeyTrigger.Hold:
                    if (Input.GetKey(keyCode))
                    {
                        // Continuous delta in seconds this frame (e.g., 2 sec/s => +2 * dt)
                        float deltaSeconds = binding.holdSecondsPerSecond * Time.deltaTime;

                        if (binding.onTriggeredSeconds != null)
                            binding.onTriggeredSeconds.Invoke(deltaSeconds);

                        if (binding.alsoInvokeNoArgWhileHold)
                            binding.onTriggered?.Invoke();
                    }
                    break;
            }
        }
    }
}
