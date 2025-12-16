using Oculus.Interaction; // Meta Interaction SDK namespace
using Oculus.Interaction.HandGrab;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class ProductStocker : EditorWindow
{
    // State variables
    string targetID = "";
    bool makeGrabbable = true; // Default checked
    const string PREF_LAST_DIR = "VRClass_LastProductDir";

    [MenuItem("Tools/VR Research/Stock Shelf with Product %g")] // Ctrl+G
    public static void ShowWindow()
    {
        ProductStocker window = GetWindow<ProductStocker>("Stock Shelf");
        window.minSize = new Vector2(300, 180);
        window.maxSize = new Vector2(500, 180);
    }

    void OnGUI()
    {
        GUILayout.Space(10);
        GUILayout.Label("Product Assignment Settings", EditorStyles.boldLabel);
        GUILayout.Space(5);

        targetID = EditorGUILayout.TextField("Specific ID (Optional):", targetID);
        GUILayout.Label("Leave empty to fill the next available slot.", EditorStyles.miniLabel);

        GUILayout.Space(10);
        makeGrabbable = EditorGUILayout.Toggle("Make Grabbable (VR)", makeGrabbable);

        GUILayout.Space(15);

        if (GUILayout.Button("Select Prefab & Assign", GUILayout.Height(40)))
        {
            ExecuteStocking();
        }
    }

    void ExecuteStocking()
    {
        Transform targetSlot = null;

        // Determine Target Slot
        if (!string.IsNullOrEmpty(targetID))
        {
            GameObject foundObj = GameObject.Find(targetID);

            if (foundObj == null || !foundObj.CompareTag("ProductBundle"))
            {
                EditorUtility.DisplayDialog("Error", $"Slot ID '{targetID}' not found (or not tagged ProductBundle).", "OK");
                return;
            }

            if (foundObj.transform.childCount > 0)
            {
                EditorUtility.DisplayDialog("Slot Occupied",
                    $"Slot '{targetID}' is already full.\n\nPlease manually remove the items if you wish to replace them.",
                    "OK");
                return;
            }

            targetSlot = foundObj.transform;
        }
        else
        {
            GameObject[] allShelves = GameObject.FindGameObjectsWithTag("ProductBundle");
            System.Array.Sort(allShelves, (a, b) => CompareNatural(a.name, b.name));

            foreach (var shelf in allShelves)
            {
                if (shelf.transform.childCount == 0)
                {
                    targetSlot = shelf.transform;
                    break;
                }
            }

            if (targetSlot == null)
            {
                EditorUtility.DisplayDialog("Warehouse Full", "No empty slots available!", "OK");
                return;
            }
        }

        // File Picker
        string lastDir = EditorPrefs.GetString(PREF_LAST_DIR, "Assets");
        if (!System.IO.Directory.Exists(lastDir)) lastDir = "Assets";

        string path = EditorUtility.OpenFilePanel("Select Product Prefab", lastDir, "prefab,fbx,obj");
        if (string.IsNullOrEmpty(path)) return;

        EditorPrefs.SetString(PREF_LAST_DIR, System.IO.Path.GetDirectoryName(path));

        // Load Asset
        path = FileUtil.GetProjectRelativePath(path);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) return;

        // Instantiate & Parent
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(instance, "Stock Shelf");

        Undo.SetTransformParent(instance.transform, targetSlot, "Parent to Slot");

        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localScale = Vector3.one;

        // Unpack prefab
        if (makeGrabbable)
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            MakeGrabbable(instance);
        }

        // Add ProductData and assign ID
        AddProductData(instance, targetSlot);

        // Z-Alignment
        ApplyZAlignment(instance);

        // Focus
        Selection.activeGameObject = targetSlot.gameObject;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();

        Close();
    }

    void MakeGrabbable(GameObject obj)
    {
        // 1. Ensure Collider exists with proper mesh assignment
        Collider collider = obj.GetComponent<Collider>();

        if (collider == null)
        {
            // Try to add MeshCollider with mesh from MeshFilter
            MeshFilter meshFilter = obj.GetComponent<MeshFilter>();

            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                MeshCollider meshCollider = Undo.AddComponent<MeshCollider>(obj);
                meshCollider.sharedMesh = meshFilter.sharedMesh; // CRITICAL: Assign the mesh
                meshCollider.convex = true;
                collider = meshCollider;
                Debug.Log($"✓ Added MeshCollider with mesh '{meshFilter.sharedMesh.name}' to {obj.name}");
            }
            else
            {
                // No valid mesh - try primitive collider fallback
                Renderer renderer = obj.GetComponent<Renderer>();
                if (renderer != null)
                {
                    BoxCollider boxCollider = Undo.AddComponent<BoxCollider>(obj);
                    collider = boxCollider;
                    Debug.LogWarning($"⚠ No MeshFilter found on {obj.name}. Added BoxCollider as fallback.");
                }
                else
                {
                    // CRITICAL ERROR: Can't make grabbable without collider
                    EditorUtility.DisplayDialog("Cannot Make Grabbable",
                        $"Object '{obj.name}' has no MeshFilter or Renderer.\n\n" +
                        "Cannot create collider. Please add geometry to this object or uncheck 'Make Grabbable'.",
                        "OK");

                    Debug.LogError($"❌ FAILED: {obj.name} cannot be made grabbable - no mesh or renderer found!");
                    return; // Abort grabbable setup
                }
            }
        }
        else if (collider is MeshCollider meshCol)
        {
            // Existing MeshCollider - validate it has a mesh
            if (meshCol.sharedMesh == null)
            {
                MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    Undo.RecordObject(meshCol, "Assign Mesh to Collider");
                    meshCol.sharedMesh = meshFilter.sharedMesh;
                    meshCol.convex = true;
                    Debug.Log($"✓ Assigned mesh '{meshFilter.sharedMesh.name}' to existing MeshCollider on {obj.name}");
                }
                else
                {
                    EditorUtility.DisplayDialog("Invalid MeshCollider",
                        $"Object '{obj.name}' has a MeshCollider but no mesh assigned.\n\n" +
                        "Cannot make grabbable without a valid mesh.",
                        "OK");

                    Debug.LogError($"❌ FAILED: {obj.name} has MeshCollider but no mesh available!");
                    return;
                }
            }
            else
            {
                Undo.RecordObject(meshCol, "Set Convex");
                meshCol.convex = true;
                Debug.Log($"✓ Set existing MeshCollider to convex on {obj.name}");
            }
        }

        // 2. Add/Configure Rigidbody
        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = Undo.AddComponent<Rigidbody>(obj);
        }
        else
        {
            Undo.RecordObject(rb, "Configure Rigidbody");
        }
        rb.useGravity = false;
        rb.isKinematic = true;

        // 3. Add Grabbable Component
        Grabbable grabbable = Undo.AddComponent<Grabbable>(obj);

        SerializedObject soGrabbable = new SerializedObject(grabbable);
        soGrabbable.FindProperty("_targetTransform").objectReferenceValue = obj.transform;
        soGrabbable.ApplyModifiedProperties();

        grabbable.InjectOptionalTargetTransform(obj.transform);

        Debug.Log($"✓ Added Grabbable to {obj.name}");

        // 4. Create Child Interaction GameObject
        GameObject interactionChild = new GameObject("ISDK_HandGrabInteraction");
        Undo.RegisterCreatedObjectUndo(interactionChild, "Create Interaction Child");
        Undo.SetTransformParent(interactionChild.transform, obj.transform, "Parent Interaction");

        interactionChild.transform.localPosition = Vector3.zero;
        interactionChild.transform.localRotation = Quaternion.identity;
        interactionChild.transform.localScale = Vector3.one;

        // 5. Add HandGrabInteractable
        HandGrabInteractable handGrab = Undo.AddComponent<HandGrabInteractable>(interactionChild);

        SerializedObject soHandGrab = new SerializedObject(handGrab);
        soHandGrab.FindProperty("_pointableElement").objectReferenceValue = grabbable;
        soHandGrab.FindProperty("_rigidbody").objectReferenceValue = rb;
        soHandGrab.ApplyModifiedProperties();

        // 6. Add GrabInteractable
        GrabInteractable grabInteractable = Undo.AddComponent<GrabInteractable>(interactionChild);

        SerializedObject soGrabInteractable = new SerializedObject(grabInteractable);
        soGrabInteractable.FindProperty("_pointableElement").objectReferenceValue = grabbable;
        soGrabInteractable.FindProperty("_rigidbody").objectReferenceValue = rb;
        soGrabInteractable.ApplyModifiedProperties();

        Debug.Log($"✓✓✓ Made {obj.name} grabbable with Hand & Controller support");
    }

    void ApplyZAlignment(GameObject obj)
    {
        Bounds combinedBounds = new Bounds(obj.transform.position, Vector3.zero);
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0) return;

        foreach (Renderer r in renderers)
        {
            if (combinedBounds.size == Vector3.zero) combinedBounds = r.bounds;
            else combinedBounds.Encapsulate(r.bounds);
        }

        // Z-axis alignment (existing code)
        float pivotToMaxZ = combinedBounds.max.z - obj.transform.position.z;
        float pushAmountZ = -pivotToMaxZ;

        // Y-axis alignment (NEW)
        // Calculate how far the pivot is from the bottom of the object
        float pivotToMinY = combinedBounds.min.y - obj.transform.position.y;
        // Move the object up so its bottom sits exactly at Y=0
        float pushAmountY = -pivotToMinY;

        // Apply both offsets
        obj.transform.localPosition = new Vector3(0, pushAmountY, pushAmountZ);

        Debug.Log($"Aligned {obj.name}: Y offset={pushAmountY:F4}m, Z offset={pushAmountZ:F4}m");
    }

    void AddProductData(GameObject obj, Transform slot)
    {
        ProductData productData = Undo.AddComponent<ProductData>(obj);

        // Extract numeric ID from slot name (e.g., "Slot_12" -> 12)
        string slotName = slot.gameObject.name;
        if (int.TryParse(System.Text.RegularExpressions.Regex.Match(slotName, @"\d+").Value, out int id))
        {
            productData.productID = id;
            Debug.Log($"Assigned Product ID: {id} to {obj.name}");
        }
        else
        {
            Debug.LogWarning($"Could not parse ID from slot name: {slotName}");
        }
    }
    int CompareNatural(string a, string b)
    {
        return EditorUtility.NaturalCompare(a, b);
    }
}
