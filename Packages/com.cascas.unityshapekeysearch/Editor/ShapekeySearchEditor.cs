using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class MeshRendererEditorPatcher
{
    static MeshRendererEditorPatcher()
    {
        Harmony harmony = new Harmony("com.cascas.skinnedmeshrenderer.patch");

        // Get internal type: UnityEditor.SkinnedMeshRendererEditor
        Type editorType = typeof(Editor).Assembly.GetType("UnityEditor.SkinnedMeshRendererEditor");
        if (editorType == null) return;
        MethodInfo methodToPatch = editorType.GetMethod("OnInspectorGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo prefix = typeof(MeshRendererEditorPatcher).GetMethod(nameof(OnInspectorGUIPrefix), BindingFlags.Static | BindingFlags.NonPublic);
        harmony.Patch(methodToPatch, prefix: new HarmonyMethod(prefix));
    }
    
    // This will run BEFORE Unity's inspector code
    private static void OnInspectorGUIPrefix(Editor __instance)
    {
        RenderSearchBar(__instance);
    }
    
    private static string searchQuery = "";
    private static SkinnedMeshRenderer skinnedMeshRenderer;
    private static readonly Dictionary<int, string> blendshapeNames = new Dictionary<int, string>();
    private static void RenderSearchBar(Editor __instance)
    {
        // Draw the search bar
        EditorGUILayout.BeginVertical("Box", GUILayout.Height(24));
        EditorGUILayout.BeginHorizontal();
        GUIStyle searchStyle = new GUIStyle(GUI.skin.FindStyle("ToolbarSearchTextField"))
        {
            fixedHeight = 0,               // Let GUILayout control the height
            stretchHeight = true,
            fontSize = 13                 // Optional: make text bigger too
        };
        searchQuery = EditorGUILayout.TextField(searchQuery, searchStyle, GUILayout.Height(24));

        GUIStyle cancelButtonStyle = new GUIStyle(GUI.skin.FindStyle("ToolbarSearchCancelButton"))
        {
            fixedHeight = 0,               // Let GUILayout control the height
            stretchHeight = true,
            fontSize = 13                 // Optional: make text bigger too
        };
        if (GUILayout.Button("", cancelButtonStyle, GUILayout.Height(24)))
        {
            searchQuery = "";
            GUI.FocusControl(null); // Unfocus the field
        }
        EditorGUILayout.EndHorizontal();

        if (skinnedMeshRenderer == null)
        {
            skinnedMeshRenderer = __instance.target as SkinnedMeshRenderer;
        }
        if (blendshapeNames.Count <= 0)
        {
            CacheBlendshapes();
        }
        
        List<int> blendshapeIndices = new List<int>();
        
        // Find all blendshapes that match the current search query
        if (!string.IsNullOrEmpty(searchQuery))
        {
            EditorGUILayout.Space();
            
            for (int i = 0; i < blendshapeNames.Count; i++)
            {
                if (blendshapeNames[i].IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    blendshapeIndices.Add(i);
                }
            }
        }

        foreach (int index in blendshapeIndices)
        {
            // Draw index with range float slider
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(blendshapeNames[index], GUILayout.Width(150));
            float value = skinnedMeshRenderer!.GetBlendShapeWeight(index);
            bool swap = GUILayout.Button("O");
            float newValue = EditorGUILayout.Slider(value, 0, 100);
            newValue = swap ? Mathf.Abs(newValue - 100) : newValue;
            Debug.Log(newValue);
            if (!Mathf.Approximately(newValue, value))
            {
                skinnedMeshRenderer.SetBlendShapeWeight(index, newValue);
            }
            EditorGUILayout.EndHorizontal();
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private static void CacheBlendshapes()
    {
        for (int i = 0; i < skinnedMeshRenderer.sharedMesh.blendShapeCount; i++)
        {
            string name = skinnedMeshRenderer.sharedMesh.GetBlendShapeName(i);
            blendshapeNames.Add(i, name);
        }
    }
    
}