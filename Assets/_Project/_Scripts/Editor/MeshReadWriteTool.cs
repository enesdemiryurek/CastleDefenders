using UnityEngine;
using UnityEditor;
using System.IO;

public class MeshReadWriteTool : EditorWindow
{
    [MenuItem("Tools/Fix Mesh Read-Write")]
    public static void EnableReadWrite()
    {
        string[] connectionGuids = Selection.assetGUIDs;
        
        if (connectionGuids.Length == 0)
        {
            Debug.LogWarning("Lütfen Project penceresinden model dosyalarını veya klasörü seçin.");
            return;
        }

        int count = 0;
        foreach (string guid in connectionGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            
            // Klasör ise içindekileri bul
            if (Directory.Exists(path))
            {
                string[] files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    if (file.EndsWith(".meta")) continue;
                    ProcessFile(file);
                    count++;
                }
            }
            else
            {
                ProcessFile(path);
                count++;
            }
        }
        
        AssetDatabase.Refresh();
        Debug.Log($"İşlem Tamamlandı! {count} dosya tarandı. Oyunu tekrar başlat.");
    }

    private static void ProcessFile(string path)
    {
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer != null)
        {
            if (!importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
                Debug.Log($"DÜZELTİLDİ: {path}");
            }
        }
    }
}
