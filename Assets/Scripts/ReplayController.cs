using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class KeyBinding
{
    [Tooltip("Key name, e.g. M, P, 1, etc.")]
    public string keyName;

    [Tooltip("Function to invoke when the key is pressed.")]
    public UnityEvent onKeyPressed;
}

public class ReplayController : MonoBehaviour
{
    [Header("Key Bindings")]
    public List<KeyBinding> keyBindings = new List<KeyBinding>();

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        foreach (var binding in keyBindings)
        {
            if (!string.IsNullOrEmpty(binding.keyName))
            {
                // TryParse string to KeyCode
                if (System.Enum.TryParse<KeyCode>(binding.keyName, true, out var keyCode))
                {
                    if (Input.GetKeyDown(keyCode))
                    {
                        binding.onKeyPressed?.Invoke();
                    }
                }
            }
        }
    }
}
