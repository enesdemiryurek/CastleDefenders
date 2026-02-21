using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Texture Optimizer - Tüm texture'ları otomatik sıkıştırır
/// Unity Menu: Tools → Optimize Textures
/// </summary>
public class TextureOptimizer : EditorWindow
{
    private int maxTextureSize = 1024;
    private bool compressTextures = true;
    
    [MenuItem("Tools/Optimize Textures (Performans)")]
    public static void ShowWindow()
    {
        GetWindow<TextureOptimizer>("Texture Optimizer");
    }

    private void OnGUI()
    {
        GUILayout.Label("🎯 Texture Optimizasyonu", EditorStyles.boldLabel);
        GUILayout.Space(10);
        
        GUILayout.Label("Bu araç tüm texture'ların boyutunu düşürür.\n" +
                        "2.3 GB → ~500 MB'a düşürebilir!", EditorStyles.wordWrappedLabel);
        GUILayout.Space(10);
        
        maxTextureSize = EditorGUILayout.IntPopup("Max Texture Size", maxTextureSize, 
            new string[] { "512", "1024", "2048", "4096" }, 
            new int[] { 512, 1024, 2048, 4096 });
        
        compressTextures = EditorGUILayout.Toggle("Sıkıştırma Uygula", compressTextures);
        
        GUILayout.Space(15);
        
        if (GUILayout.Button("🚀 TÜM TEXTURE'LARI OPTİMİZE ET", GUILayout.Height(40)))
        {
            OptimizeAllTextures();
        }
        
        GUILayout.Space(10);
        GUILayout.Label("⚠️ Bu işlem biraz sürebilir. Unity donabilir.", EditorStyles.miniLabel);
    }

    private void OptimizeAllTextures()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" });
        int total = guids.Length;
        int changed = 0;
        
        for (int i = 0; i < total; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            
            // Progress bar
            EditorUtility.DisplayProgressBar("Texture Optimizasyonu", 
                $"İşleniyor: {Path.GetFileName(path)} ({i+1}/{total})", 
                (float)i / total);
            
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;
            
            bool needsReimport = false;
            
            // Max Size düşür
            if (importer.maxTextureSize > maxTextureSize)
            {
                importer.maxTextureSize = maxTextureSize;
                needsReimport = true;
            }
            
            // Sıkıştırma uygula
            if (compressTextures && importer.textureCompression != TextureImporterCompression.Compressed)
            {
                importer.textureCompression = TextureImporterCompression.Compressed;
                needsReimport = true;
            }
            
            // Mipmap aç (uzaktaki objeler için performans)
            if (!importer.mipmapEnabled && importer.textureType != TextureImporterType.Sprite)
            {
                importer.mipmapEnabled = true;
                needsReimport = true;
            }
            
            if (needsReimport)
            {
                importer.SaveAndReimport();
                changed++;
            }
        }
        
        EditorUtility.ClearProgressBar();
        EditorUtility.DisplayDialog("Tamamlandı!", 
            $"✅ {changed} texture optimize edildi!\n" +
            $"Toplam taranan: {total}\n" +
            $"Max boyut: {maxTextureSize}px", "Tamam");
        
        Debug.Log($"[TextureOptimizer] {changed}/{total} texture optimize edildi. Max: {maxTextureSize}px");
    }
}
