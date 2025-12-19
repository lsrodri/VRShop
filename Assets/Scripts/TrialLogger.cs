using System;
using System.IO;
using System.Text;
using UnityEngine;

public class TrialLogger : MonoBehaviour
{
    [Header("Logging Configuration")]
    [SerializeField] private string logFileName = "TrialData.csv";
    [SerializeField] private bool logToQuestStorage = true; // Toggle for Quest vs PC

    private string logFilePath;
    private bool isInitialized = false;

    void Awake()
    {
        InitializeLogger();
    }

    void InitializeLogger()
    {
        // Determine storage path based on platform
        string basePath;

#if UNITY_ANDROID && !UNITY_EDITOR
        // Quest/Android: Use persistent data path (accessible via USB when connected)
        basePath = Application.persistentDataPath;
        Debug.Log($"Running on Quest - Logs will be saved to: {basePath}");
#else
        // PC/Editor: Use persistent data path or Desktop
        if (logToQuestStorage)
        {
            basePath = Application.persistentDataPath;
        }
        else
        {
            // Save to Desktop for easy access during development
            basePath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }
        Debug.Log($"Running in Editor/PC - Logs will be saved to: {basePath}");
#endif

        logFilePath = Path.Combine(basePath, logFileName);

        // Create header if file doesn't exist
        if (!File.Exists(logFilePath))
        {
            CreateCSVHeader();
        }

        isInitialized = true;
        Debug.Log($"TrialLogger initialized. Log file: {logFilePath}");
    }

    void CreateCSVHeader()
    {
        StringBuilder header = new StringBuilder();
        header.Append("ParticipantID,");
        header.Append("Block,");
        header.Append("TrialNumber,");
        header.Append("Condition,");
        header.Append("TrialStartTime,");
        header.Append("TrialEndTime,");
        header.Append("TrialDuration_Seconds,");
        header.Append("ProductPurchased,"); // 0 = no purchase, ProductID if purchased
        header.Append("ShelfLocation,"); // ✓ CHANGED: 0 = None, 1-4 = Shelf number
        header.Append("ShelfOneProductID,");
        header.Append("ShelfTwoProductID,");
        header.Append("ShelfThreeProductID,");
        header.Append("ShelfFourProductID");

        try
        {
            File.WriteAllText(logFilePath, header.ToString() + Environment.NewLine);
            Debug.Log($"✓ Created CSV header at: {logFilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to create CSV header: {e.Message}");
        }
    }

    public void LogTrial(TrialLogEntry entry)
    {
        if (!isInitialized)
        {
            Debug.LogError("TrialLogger not initialized!");
            return;
        }

        StringBuilder logLine = new StringBuilder();

        logLine.Append($"{entry.participantID},");
        logLine.Append($"{entry.block},");
        logLine.Append($"{entry.trialNumber},");
        logLine.Append($"{entry.condition},");
        logLine.Append($"{entry.trialStartTime:yyyy-MM-dd HH:mm:ss.fff},");
        logLine.Append($"{entry.trialEndTime:yyyy-MM-dd HH:mm:ss.fff},");
        logLine.Append($"{entry.trialDuration:F3},");
        logLine.Append($"{entry.productPurchased},");
        logLine.Append($"{entry.shelfLocation},");
        logLine.Append($"{entry.shelfOneProductID},");
        logLine.Append($"{entry.shelfTwoProductID},");
        logLine.Append($"{entry.shelfThreeProductID},");
        logLine.Append($"{entry.shelfFourProductID}");

        try
        {
            File.AppendAllText(logFilePath, logLine.ToString() + Environment.NewLine);
            Debug.Log($"Logged trial: P{entry.participantID} T{entry.trialNumber} - Product: {entry.productPurchased} - Shelf: {entry.shelfLocation}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to write log entry: {e.Message}");
        }
    }

    public string GetLogFilePath()
    {
        return logFilePath;
    }

    // Helper method to open log folder (PC only)
    [ContextMenu("Open Log Folder")]
    public void OpenLogFolder()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        string folderPath = Path.GetDirectoryName(logFilePath);
        Application.OpenURL($"file://{folderPath}");
        Debug.Log($"Opening folder: {folderPath}");
#else
        Debug.Log($"Log file location on Quest: {logFilePath}");
        Debug.Log("Connect Quest via USB and browse to Android/data/com.YourCompany.YourApp/files/");
#endif
    }
}

[System.Serializable]
public class TrialLogEntry
{
    public int participantID;
    public int block;
    public int trialNumber;
    public int condition;
    public DateTime trialStartTime;
    public DateTime trialEndTime;
    public double trialDuration; // in seconds
    public int productPurchased; // 0 = no purchase, ProductID if purchased
    public int shelfLocation; // ✓ CHANGED: 0 = None, 1 = Shelf1, 2 = Shelf2, 3 = Shelf3, 4 = Shelf4
    public int shelfOneProductID;
    public int shelfTwoProductID;
    public int shelfThreeProductID;
    public int shelfFourProductID;
}