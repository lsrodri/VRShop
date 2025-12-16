using UnityEngine;

public class TrialProduct : MonoBehaviour
{
    [ReadOnly] public int productID;

    void Start()
    {
        
    }

    void Update()
    {
        
    }

    void OnEnable()
    {
        // Lock the transform to prevent accidental edits
        transform.hideFlags = HideFlags.NotEditable;
    }

#if UNITY_EDITOR
    // Lock transform in the editor
    void OnValidate()
    {
        transform.hideFlags = HideFlags.NotEditable;
    }
#endif
}
