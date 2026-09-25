#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;

[InitializeOnLoad]
public class EnsureShaders
{
    static EnsureShaders()
    {
        // Ensure the Resources folder exists
        if (!Directory.Exists("Assets/Resources"))
        {
            Directory.CreateDirectory("Assets/Resources");
            AssetDatabase.Refresh();
        }
            
        // Create a dummy material to force URP Particles Unlit to be included in the build
        if (AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/DummyURPParticleMat.mat") == null)
        {
            Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (particleShader != null)
            {
                Material mat = new Material(particleShader);
                AssetDatabase.CreateAsset(mat, "Assets/Resources/DummyURPParticleMat.mat");
            }
        }

        // Create a dummy material to force URP Lit to be included in the build
        if (AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/DummyURPLitMat.mat") == null)
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader != null)
            {
                Material mat = new Material(litShader);
                AssetDatabase.CreateAsset(mat, "Assets/Resources/DummyURPLitMat.mat");
            }
        }
        
        AssetDatabase.SaveAssets();
    }
}
#endif
