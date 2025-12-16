using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public class FreeCamera : MonoBehaviour
{
    public float movementSpeed = 10f;
    public float fastMovementSpeed = 100f;
    public float freeLookSensitivity = 3f;
    public float zoomSensitivity = 10f;
    public float fastZoomSensitivity = 50f;
    private bool looking = false;

    private Camera cachedCamera;

    void Awake()
    {
        cachedCamera = GetComponent<Camera>();
        if (cachedCamera == null)
            cachedCamera = Camera.main;
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;

        // If Input System isn't initialized yet, bail out.
        if (kb == null || mouse == null)
            return;

        bool fastMode = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        float activeMoveSpeed = fastMode ? fastMovementSpeed : movementSpeed;

        Vector3 move = Vector3.zero;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move += -transform.right;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move += transform.right;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move += transform.forward;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move += -transform.forward;
        if (kb.qKey.isPressed) move += transform.up;
        if (kb.eKey.isPressed) move += -transform.up;
        if (kb.rKey.isPressed || kb.pageUpKey.isPressed) move += Vector3.up;
        if (kb.fKey.isPressed || kb.pageDownKey.isPressed) move += -Vector3.up;

        transform.position += move * activeMoveSpeed * Time.deltaTime;

        if (looking)
        {
            Vector2 delta = mouse.delta.ReadValue();
            float newRotationX = transform.localEulerAngles.y + delta.x * freeLookSensitivity;
            float newRotationY = transform.localEulerAngles.x - delta.y * freeLookSensitivity;
            transform.localEulerAngles = new Vector3(newRotationY, newRotationX, 0f);
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.001f && cachedCamera != null)
        {
            float zoomSpeed = fastMode ? fastZoomSensitivity : zoomSensitivity;
            // scale scroll input to sensible FOV change
            cachedCamera.fieldOfView = Mathf.Clamp(cachedCamera.fieldOfView - scroll * zoomSpeed, 1f, 179f);
        }

        if (mouse.rightButton.wasPressedThisFrame)
        {
            StartLooking();
        }
        else if (mouse.rightButton.wasReleasedThisFrame)
        {
            StopLooking();
        }
    }

    void OnDisable()
    {
        StopLooking();
    }

    public void StartLooking()
    {
        looking = true;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    public void StopLooking()
    {
        looking = false;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }
}