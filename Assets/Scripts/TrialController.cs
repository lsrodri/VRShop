using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine.Networking;
using TMPro;

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

    [Header("Status")]
    [Tooltip("Leave at -1 to use saved value. Enter number to override.")]
    [SerializeField] private int overrideParticipantID = -1;
    [Tooltip("Leave at -1 to use saved value. Enter number to override.")]
    [SerializeField] private int overrideTrialNumber = -1;

    [Header("Active Values (Read Only)")]
    [SerializeField] private int currentParticipantID; // Remove 'public', make serialized for inspector viewing
    [SerializeField] private int currentTrialNumber;

    // Keep public getters for other scripts
    public int CurrentParticipantID => currentParticipantID;
    public int CurrentTrialNumber => currentTrialNumber;

    // --- NEW: Track purchased products ---
    private HashSet<TrialProduct> purchasedProducts = new HashSet<TrialProduct>();

    // --- Internal Data Structures ---
    [System.Serializable]
    public class TrialData
    {
        public int participantID;
        public int trialNumber;
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
            activeProducts.Remove(product); // Remove from active list
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
            if (cols.Length < 6) continue;

            TrialData t = new TrialData();
            int.TryParse(cols[0], out t.participantID);
            int.TryParse(cols[1], out t.trialNumber);
            int.TryParse(cols[2], out t.shelfOneProductID);
            int.TryParse(cols[3], out t.shelfTwoProductID);
            int.TryParse(cols[4], out t.shelfThreeProductID);
            int.TryParse(cols[5], out t.shelfFourProductID);

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
        // MODIFIED: Only deactivate non-purchased products
        foreach (var p in activeProducts)
        {
            if (!purchasedProducts.Contains(p))
            {
                p.gameObject.SetActive(false);
            }
        }
        activeProducts.Clear();

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

        Debug.Log($"Running Trial {trialNum}...");

        PlaceProductOnShelf(data.shelfOneProductID, shelfOnePosition, shelfOneLabel);
        PlaceProductOnShelf(data.shelfTwoProductID, shelfTwoPosition, shelfTwoLabel);
        PlaceProductOnShelf(data.shelfThreeProductID, shelfThreePosition, shelfThreeLabel);
        PlaceProductOnShelf(data.shelfFourProductID, shelfFourPosition, shelfFourLabel);
    }

    void PlaceProductOnShelf(int productID, Transform shelfSlot, TextMeshPro shelfLabel)
    {
        if (sceneInventory.TryGetValue(productID, out TrialProduct product))
        {
            // MODIFIED: Skip if product was already purchased
            if (purchasedProducts.Contains(product))
            {
                Debug.Log($"Product {productID} already purchased, skipping placement.");
                if (shelfLabel != null) shelfLabel.text = "SOLD";
                return;
            }

            product.transform.SetParent(null);
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

            float price = priceDatabase.ContainsKey(productID) ? priceDatabase[productID] : 0f;

            if (shelfLabel != null) shelfLabel.text = price.ToString("F2");
        }
        else
        {
            Debug.LogError($"Missing Product ID {productID}!");
            if (shelfLabel != null) shelfLabel.text = "---";
        }
    }

    public void NextTrial()
    {
        currentTrialNumber++;
        PlayerPrefs.SetInt("TrialNumber", currentTrialNumber);
        PlayerPrefs.Save();
        RunTrial(currentParticipantID, currentTrialNumber);
    }

    // NEW: Optional - Reset purchased products for new participant
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
