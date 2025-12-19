using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.Rendering;

public class TrialController : MonoBehaviour
{
    [Header("Configuration")]
    public string trialsCsvName = "Trials.csv";
    public string productsCsvName = "Products.csv";

    [Header("Shelf Locations (Where items spawn)")]
    public Transform shelfOnePosition;
    public Transform shelfTwoPosition;
    public Transform shelfThreePosition;
    public Transform shelfFourPosition;

    [Header("Shelf Price Tags")]
    public TextMeshPro shelfOneLabel;
    public TextMeshPro shelfTwoLabel;
    public TextMeshPro shelfThreeLabel;
    public TextMeshPro shelfFourLabel;

    [Header("Post Processing")]
    public Volume postProcessingVolume;

    [Header("Condition Settings")]
    [Tooltip("Exposure value for conditions 1 & 2")]
    public float exposure1 = -0.5f;
    [Tooltip("Exposure value for conditions 3 & 4")]
    public float exposure2 = 0.5f;
    [Tooltip("Temperature value for conditions 1 & 3")]
    public float temperature1 = -20f;
    [Tooltip("Temperature value for conditions 2 & 4")]
    public float temperature2 = 20f;

    [Header("Status")]
    [Tooltip("Leave at -1 to use saved value. Enter number to override.")]
    [SerializeField] private int overrideParticipantID = -1;
    [Tooltip("Leave at -1 to use saved value. Enter number to override.")]
    [SerializeField] private int overrideTrialNumber = -1;

    [Header("Active Values (Read Only)")]
    [SerializeField] private int currentParticipantID;
    [SerializeField] private int currentTrialNumber;
    [SerializeField] private int currentBlock;
    [SerializeField] private int currentCondition;
    [SerializeField] private float currentPostExposure;
    [SerializeField] private float currentTemperature;

    // Keep public getters for other scripts
    public int CurrentParticipantID => currentParticipantID;
    public int CurrentTrialNumber => currentTrialNumber;
    public int CurrentBlock => currentBlock;
    public int CurrentCondition => currentCondition;
    public float CurrentPostExposure => currentPostExposure;
    public float CurrentTemperature => currentTemperature;

    // --- NEW: Track purchased products ---
    private HashSet<TrialProduct> purchasedProducts = new HashSet<TrialProduct>();

    // --- Internal Data Structures ---
    [System.Serializable]
    public class TrialData
    {
        public int participantID;
        public int block;
        public int trialNumber;
        public int condition;
        public int shelfOneProductID;
        public int shelfTwoProductID;
        public int shelfThreeProductID;
        public int shelfFourProductID;
    }
    private List<TrialData> allTrials = new List<TrialData>();

    private Dictionary<int, float> priceDatabase = new Dictionary<int, float>();
    private Dictionary<int, TrialProduct> sceneInventory = new Dictionary<int, TrialProduct>();
    private List<TrialProduct> activeProducts = new List<TrialProduct>();

    // --- NEW: Public method for AddToCart to call ---
    public void MarkProductAsPurchased(TrialProduct product)
    {
        if (!purchasedProducts.Contains(product))
        {
            purchasedProducts.Add(product);
            activeProducts.Remove(product);
            Debug.Log($"Product {product.productID} marked as purchased");
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void InitializeLogFilter()
    {
        new OVRLogFilter();
    }

    void Start()
    {

        Application.targetFrameRate = 90;

        // Also request 90Hz from OVR Plugin if available
        #if UNITY_ANDROID && !UNITY_EDITOR
                OVRPlugin.systemDisplayFrequency = 90.0f;
        #endif

        // Apply override logic
        if (overrideParticipantID > 0)
        {
            currentParticipantID = overrideParticipantID;
            PlayerPrefs.SetInt("ParticipantID", currentParticipantID);
            Debug.LogWarning($" OVERRIDE: Using Participant ID {currentParticipantID}");
        }
        else
        {
            currentParticipantID = PlayerPrefs.GetInt("ParticipantID", 1);
        }

        if (overrideTrialNumber > 0)
        {
            currentTrialNumber = overrideTrialNumber;
            PlayerPrefs.SetInt("TrialNumber", currentTrialNumber);
            Debug.LogWarning($" OVERRIDE: Using Trial Number {currentTrialNumber}");
        }
        else
        {
            currentTrialNumber = PlayerPrefs.GetInt("TrialNumber", 1);
        }

        PlayerPrefs.Save();

        Debug.Log($"Initializing TrialController... P:{currentParticipantID}, T:{currentTrialNumber}");

        IndexSceneInventory();
        StartCoroutine(InitializeExperiment());
    }

    IEnumerator InitializeExperiment()
    {
        // Load Products.csv
        string productsPath = Path.Combine(Application.streamingAssetsPath, productsCsvName);
        string productsContent = "";

        if (productsPath.Contains("://") || productsPath.Contains("jar:"))
        {
            UnityWebRequest www = UnityWebRequest.Get(productsPath);
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to load Products.csv: {www.error}");
            }
            else
            {
                productsContent = www.downloadHandler.text;
            }
        }
        else
        {
            if (File.Exists(productsPath)) productsContent = File.ReadAllText(productsPath);
            else Debug.LogError($"Products.csv not found at {productsPath}");
        }

        ParseProductDatabase(productsContent);

        // Load Trials.csv
        string trialsPath = Path.Combine(Application.streamingAssetsPath, trialsCsvName);
        string trialsContent = "";

        if (trialsPath.Contains("://") || trialsPath.Contains("jar:"))
        {
            UnityWebRequest www = UnityWebRequest.Get(trialsPath);
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Failed to load Trials.csv: {www.error}");
            }
            else
            {
                trialsContent = www.downloadHandler.text;
            }
        }
        else
        {
            if (File.Exists(trialsPath)) trialsContent = File.ReadAllText(trialsPath);
            else Debug.LogError($"Trials.csv not found at {trialsPath}");
        }

        if (ParseTrialsCSV(trialsContent))
        {
            RunTrial(currentParticipantID, currentTrialNumber);
        }
    }

    void ParseProductDatabase(string csvText)
    {
        if (string.IsNullOrEmpty(csvText)) return;

        string[] lines = csvText.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);

        for (int i = 1; i < lines.Length; i++)
        {
            string[] cols = lines[i].Split(',');
            if (cols.Length < 3) continue;

            if (int.TryParse(cols[0], out int id) && float.TryParse(cols[2], out float price))
            {
                if (!priceDatabase.ContainsKey(id))
                    priceDatabase.Add(id, price);
            }
        }
        Debug.Log($"Product DB Loaded: {priceDatabase.Count} entries.");
    }

    bool ParseTrialsCSV(string csvText)
    {
        if (string.IsNullOrEmpty(csvText)) return false;

        string[] lines = csvText.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);

        for (int i = 1; i < lines.Length; i++)
        {
            string[] cols = lines[i].Split(',');
            if (cols.Length < 8) continue;

            TrialData t = new TrialData();
            int.TryParse(cols[0], out t.participantID);
            int.TryParse(cols[1], out t.block);
            int.TryParse(cols[2], out t.trialNumber);
            int.TryParse(cols[3], out t.condition);
            int.TryParse(cols[4], out t.shelfOneProductID);
            int.TryParse(cols[5], out t.shelfTwoProductID);
            int.TryParse(cols[6], out t.shelfThreeProductID);
            int.TryParse(cols[7], out t.shelfFourProductID);

            allTrials.Add(t);
        }
        Debug.Log($"Trials Loaded: {allTrials.Count} trials queued.");
        return true;
    }

    void IndexSceneInventory()
    {
        var products = FindObjectsByType<TrialProduct>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (var p in products)
        {
            p.gameObject.SetActive(false);
            if (!sceneInventory.ContainsKey(p.productID))
                sceneInventory.Add(p.productID, p);
        }
    }

    public void RunTrial(int pID, int trialNum)
    {
        // FIXED: Deactivate ALL active products
        foreach (var p in activeProducts)
        {
            p.gameObject.SetActive(false);
        }
        
        // CRITICAL FIX: Also deactivate purchased products (they were removed from activeProducts)
        foreach (var p in purchasedProducts)
        {
            p.gameObject.SetActive(false);
        }
        
        activeProducts.Clear();
        purchasedProducts.Clear();

        TrialData data = allTrials.Find(t => t.participantID == pID && t.trialNumber == trialNum);

        if (data == null)
        {
            Debug.Log($"No data for P:{pID} T:{trialNum}. Experiment Complete?");
            if (shelfOneLabel) shelfOneLabel.text = "";
            if (shelfTwoLabel) shelfTwoLabel.text = "";
            if (shelfThreeLabel) shelfThreeLabel.text = "";
            if (shelfFourLabel) shelfFourLabel.text = "";
            return;
        }

        // Update current state
        currentBlock = data.block;
        currentCondition = data.condition;

        Debug.Log($"Running Trial {trialNum} (Block {currentBlock}, Condition {currentCondition})...");

        // Apply condition settings when trial loads
        ApplyConditionSettings(data.condition);

        PlaceProductOnShelf(data.shelfOneProductID, shelfOnePosition, shelfOneLabel);
        PlaceProductOnShelf(data.shelfTwoProductID, shelfTwoPosition, shelfTwoLabel);
        PlaceProductOnShelf(data.shelfThreeProductID, shelfThreePosition, shelfThreeLabel);
        PlaceProductOnShelf(data.shelfFourProductID, shelfFourPosition, shelfFourLabel);
    }

    // Apply condition-based post-processing settings using global variables
    void ApplyConditionSettings(int condition)
    {
        Debug.Log($"Applying settings for Condition {condition}");

        // Validate post-processing volume
        if (postProcessingVolume == null)
        {
            Debug.LogWarning("No Post Processing Volume assigned! Skipping condition application.");
            return;
        }

        // Get the Color Adjustments component for Exposure
        UnityEngine.Rendering.Universal.ColorAdjustments colorAdjustments;
        if (!postProcessingVolume.profile.TryGet(out colorAdjustments))
        {
            Debug.LogError("ColorAdjustments component not found in Post Processing Volume profile!");
            return;
        }

        // Get the White Balance component for Temperature
        UnityEngine.Rendering.Universal.WhiteBalance whiteBalance;
        if (!postProcessingVolume.profile.TryGet(out whiteBalance))
        {
            Debug.LogError("WhiteBalance component not found in Post Processing Volume profile!");
            return;
        }

        // Apply settings based on condition using the global variables
        switch (condition)
        {
            case 1:
                // Condition 1: exposure1, temperature1
                currentPostExposure = exposure1;
                currentTemperature = temperature1;
                break;

            case 2:
                // Condition 2: exposure1, temperature2
                currentPostExposure = exposure1;
                currentTemperature = temperature2;
                break;

            case 3:
                // Condition 3: exposure2, temperature1
                currentPostExposure = exposure2;
                currentTemperature = temperature1;
                break;

            case 4:
                // Condition 4: exposure2, temperature2
                currentPostExposure = exposure2;
                currentTemperature = temperature2;
                break;

            default:
                Debug.LogWarning($"Unknown condition {condition}. Using default settings (0, 0).");
                currentPostExposure = 0f;
                currentTemperature = 0f;
                break;
        }

        // Apply the current values to the post-processing effects
        colorAdjustments.postExposure.value = currentPostExposure;
        whiteBalance.temperature.value = currentTemperature;

        Debug.Log($"Applied Condition {condition}: Exposure = {currentPostExposure}, Temperature = {currentTemperature}");
    }

    void PlaceProductOnShelf(int productID, Transform shelfSlot, TextMeshPro shelfLabel)
    {
        if (sceneInventory.TryGetValue(productID, out TrialProduct product))
        {
            // ADDED: Force deactivate first to ensure clean state
            if (product.gameObject.activeSelf)
            {
                product.gameObject.SetActive(false);
            }

            // Now reposition and activate
            product.transform.position = shelfSlot.position;
            product.transform.rotation = shelfSlot.rotation;
            product.gameObject.SetActive(true);
            activeProducts.Add(product);

            foreach (Transform child in product.transform)
            {
                Rigidbody rb = child.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = false;
                    rb.useGravity = true;
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                }
            }

            // ADDED: Re-enable interaction components (reversed from AddToCart)
            ReEnableProductInteraction(product.gameObject);

            float price = priceDatabase.ContainsKey(productID) ? priceDatabase[productID] : 0f;

            if (shelfLabel != null) shelfLabel.text = price.ToString("F2");
        }
        else
        {
            Debug.LogError($"Missing Product ID {productID}!");
            if (shelfLabel != null) shelfLabel.text = "---";
        }
    }

    // NEW: Re-enable interaction components when product is placed back on shelf
    void ReEnableProductInteraction(GameObject product)
    {
        // Re-enable Meta Interaction SDK components
        var handGrab = product.GetComponentInChildren<Oculus.Interaction.HandGrab.HandGrabInteractable>();
        var grabInteractable = product.GetComponentInChildren<Oculus.Interaction.GrabInteractable>();
        var grabbable = product.GetComponent<Oculus.Interaction.Grabbable>();

        if (handGrab != null) handGrab.enabled = true;
        if (grabInteractable != null) grabInteractable.enabled = true;
        if (grabbable != null) grabbable.enabled = true;
    }

    public void NextTrial()
    {
        currentTrialNumber++;
        PlayerPrefs.SetInt("TrialNumber", currentTrialNumber);
        PlayerPrefs.Save();
        RunTrial(currentParticipantID, currentTrialNumber);
    }

    // Optional - Reset purchased products for new participant
    public void ResetPurchasedProducts()
    {
        foreach (var p in purchasedProducts)
        {
            p.gameObject.SetActive(false);
        }
        purchasedProducts.Clear();
        Debug.Log("Purchased products reset.");
    }
}