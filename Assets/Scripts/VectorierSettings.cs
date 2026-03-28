using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

#nullable enable

// -=-=-=- //

public static class VectorierSettings
{
    public const string SettingsPath = "Assets/Settings/VectorierSettings.asset";

    internal const string RoomsDirectoryKey = "VectorierSettings.RoomsDirectory";

    public static string? RoomsDirectory
    {
        get
        {
#if UNITY_EDITOR
            return EditorPrefs.GetString(RoomsDirectoryKey, "");
#else
            return PlayerPrefs.GetString(RoomsDirectoryKey, "");
#endif
        }
    }
}
