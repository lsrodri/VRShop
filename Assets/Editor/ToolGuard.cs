using UnityEngine;
using UnityEditor;

[InitializeOnLoad] // Runs as soon as Unity opens
public class ToolGuard
{
    static ToolGuard()
    {
        // Subscribe to the selection change event
        Selection.selectionChanged += OnSelectionChanged;
    }

    private static void OnSelectionChanged()
    {
        GameObject active = Selection.activeGameObject;

        if (active != null)
        {
            // Check if the selected object is a "Locked Slot"
            // We check if it has the TrialProduct component
            // AND if that component is intended to be locked (you can add a flag if needed)
            if (active.GetComponent<TrialProduct>() != null)
            {
                // Force the Move/Rotate/Scale tools to be HIDDEN
                Tools.hidden = true;
                return;
            }
        }

        // If we selected anything else (or nothing), RESTORE the tools
        Tools.hidden = false;
    }
}
