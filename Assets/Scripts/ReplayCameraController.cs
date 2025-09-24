using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ReplayCameraController : MonoBehaviour
{
    public float moveSpeed = 10f;
    public float lookSpeed = 2f;
    public GameObject OnMovingFrame;
    public GameObject InfoPanel;
    public GameObject Logo;

    private float yaw = 0f;
    private float pitch = 0f;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = angles.x;

        // Start with cursor locked and invisible
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Start with the on-moving-frame active
        OnMovingFrame.SetActive(true);
    }

    void Update()
    {
        // Only move and look around if the cursor is locked and invisible
        if (Cursor.lockState == CursorLockMode.Locked && Cursor.visible == false)
        {
            // Movement
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            Vector3 move = (transform.right * h + transform.forward * v) * moveSpeed * Time.deltaTime;
            transform.position += move;

            // Rotation
            float mouseX = Input.GetAxis("Mouse X") * lookSpeed;
            float mouseY = Input.GetAxis("Mouse Y") * lookSpeed;
            yaw += mouseX;
            pitch -= mouseY;
            pitch = Mathf.Clamp(pitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        // Enter key toggles cursor lock state and visibility
        if (Input.GetKeyDown(KeyCode.Return))
        {
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;

                OnMovingFrame.SetActive(true);
                InfoPanel.SetActive(false);
                Logo.SetActive(false);

                Debug.Log("Cursor locked");
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                OnMovingFrame.SetActive(false);
                InfoPanel.SetActive(true);
                Logo.SetActive(true);

                Debug.Log("Cursor unlocked");
            }
        }
    }
}
