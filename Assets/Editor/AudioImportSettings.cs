using UnityEditor;
using UnityEngine;

/// <summary>
/// Forces every battle clip to be preloaded and decompressed on load.
///
/// Without this the first Play of a clip hits loadState=Unloaded and produces no sound — so the
/// first volley, first explosion and first ground impact of a battle are all silent, and the
/// audio only "starts working" from the second occurrence. These clips total well under a
/// megabyte, so keeping them resident costs nothing worth measuring.
/// Run with: -executeMethod AudioImportSettings.Apply
/// </summary>
public static class AudioImportSettings
{
    public static void Apply()
    {
        int changed = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) continue;

            var s = importer.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.preloadAudioData = true;
            importer.defaultSampleSettings = s;
            importer.forceToMono = true;      // 2D fire-and-forget; stereo buys nothing here
            importer.SaveAndReimport();
            changed++;
        }
        Debug.Log($"[AudioImport] preloaded {changed} clips");
    }
}
