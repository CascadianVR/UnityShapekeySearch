using System;
using System.Collections.Generic;
using System.Linq;
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
    
    private static string searchQuery = "";
    private static bool sortAlphabetically = false;
    private static int searchTypePopup = 1;
    private static SkinnedMeshRenderer skinnedMeshRenderer;
    private static readonly Dictionary<int, string> blendshapeNames = new Dictionary<int, string>();
    private static List<KeyValuePair<int, string>> blendshapeNamesAlphabetical = new List<KeyValuePair<int, string>>();
    private static readonly List<int> blendshapeDisplayIndices = new List<int>();
    
    // This will run BEFORE Unity's inspector code
    private static void OnInspectorGUIPrefix(Editor __instance)
    {
        if (skinnedMeshRenderer == null)
        {
            skinnedMeshRenderer = __instance.target as SkinnedMeshRenderer;
        }
        if (blendshapeNames.Count <= 0 || blendshapeNamesAlphabetical.Count <= 0)
        {
            CacheBlendshapes();
        }
        DisplaySearchBar();
        
        // Find all blendshapes that match the current search query
        if (string.IsNullOrEmpty(searchQuery))
        {
            blendshapeDisplayIndices.Clear();
            return;
        }
        
        GetShapekeyIndexToDisplay();
        DisplayShapekeys();
    }
    
    private static void CacheBlendshapes()
    {
        blendshapeNames.Clear();
        for (int i = 0; i < skinnedMeshRenderer.sharedMesh.blendShapeCount; i++)
        {
            string name = skinnedMeshRenderer.sharedMesh.GetBlendShapeName(i);
            blendshapeNames.Add(i, name);
        }
        
        blendshapeNamesAlphabetical.Clear();
        blendshapeNamesAlphabetical = blendshapeNames
            .OrderBy(x => x.Value)
            .ToList(); // Maintains order
    }
    
    
    private static void DisplaySearchBar()
    {
        // Draw the search bar
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
        
        Texture icon = EditorGUIUtility.IconContent("d_TrueTypeFontImporter Icon").image;
        GUIContent buttonContent = new GUIContent(icon,  "Sorts the results of the search alphabetically");
        sortAlphabetically = GUILayout.Toggle(sortAlphabetically, buttonContent, 
            "Button", GUILayout.Height(24), GUILayout.Width(24));
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Search Mode", GUILayout.Width(80));
        searchTypePopup = EditorGUILayout.Popup(searchTypePopup, new[] { "Starts With", "Contains" },
            GUILayout.Height(24), GUILayout.Width(120));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    
    private static void GetShapekeyIndexToDisplay()
    {
        blendshapeDisplayIndices.Clear();
        //f (displayAll)
        //
        //   blendshapeDisplayIndices = blendshapeNames.Keys.ToList();
        //
        //lse
        {
            if (sortAlphabetically)
            {
                foreach (KeyValuePair<int, string> pair in blendshapeNamesAlphabetical)
                {
                    if (DoesMatchSearchQuery(pair.Value))
                    {
                        blendshapeDisplayIndices.Add(pair.Key);
                    }
                }
            }
            else
            {
                for (int i = 0; i < blendshapeNames.Count; i++)
                {
                    if (DoesMatchSearchQuery(blendshapeNames[i]))
                    {
                        blendshapeDisplayIndices.Add(i);
                    }
                }
            }
        }
    }

    private static bool DoesMatchSearchQuery(string name)
    {
        return searchTypePopup switch
        {
            0 => name.StartsWith(searchQuery, StringComparison.OrdinalIgnoreCase),
            1 => name.IndexOf(searchQuery, StringComparison.OrdinalIgnoreCase) >= 0,
            _ => false
        };
    }
    
    private static void DisplayShapekeys()
    {
        GUILayout.BeginVertical("box");
        foreach (int index in blendshapeDisplayIndices)
        {
            // Draw index with range float slider
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(blendshapeNames[index], GUILayout.Width(150));
            float value = skinnedMeshRenderer!.GetBlendShapeWeight(index);
            bool swap = GUILayout.Button(EditorGUIUtility.IconContent("d_preAudioLoopOff"));
            float newValue = EditorGUILayout.Slider(value, 0, 100);
            
            // If swap button is pressed, swap the value
            newValue = swap ? Mathf.Abs(newValue - 100) : newValue;
            
            if (!Mathf.Approximately(newValue, value))
            {
                skinnedMeshRenderer.SetBlendShapeWeight(index, newValue);
            }
            EditorGUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
    }
}