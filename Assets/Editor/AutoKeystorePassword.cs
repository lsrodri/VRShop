using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public class AutoKeystorePassword
{
    static AutoKeystorePassword()
    {
        // Replace with your actual keystore and alias passwords
        PlayerSettings.Android.keystorePass = "123456";
        PlayerSettings.Android.keyaliasPass = "123456";
    }
}