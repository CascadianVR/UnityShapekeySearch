#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class MeshRendererEditorPatcher
{
    private static bool isInitialized = false;
    static MeshRendererEditorPatcher()
    {
        if (isInitialized) return;
        Harmony harmony = new Harmony("com.cascas.skinnedmeshrenderer.patch");

        // Get internal type: UnityEditor.SkinnedMeshRendererEditor
        Type editorType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.SkinnedMeshRendererEditor");
        if (editorType == null) return;
        MethodInfo methodToPatch = editorType.GetMethod("OnInspectorGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo prefix = typeof(MeshRendererEditorPatcher).GetMethod(nameof(OnInspectorGUIPrefix), BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo methodToPatch2 = editorType.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo prefix2 = typeof(MeshRendererEditorPatcher).GetMethod(nameof(OnEnablePrefix), BindingFlags.Static | BindingFlags.NonPublic);
        harmony.Patch(methodToPatch, prefix: new HarmonyMethod(prefix){priority = Priority.First});
        harmony.Patch(methodToPatch2, prefix: new HarmonyMethod(prefix2){priority = Priority.First});
        isInitialized = true;
    }
    
    private static bool sortAlphabetically = false;
    private static int searchTypePopup = 1;
    private static string searchQuery = "";
    private static string[] searchQueryWords = Array.Empty<string>();
    private static SkinnedMeshRenderer skinnedMeshRenderer;
    private static readonly List<int> blendshapeDisplayIndices = new List<int>();
    private static readonly Dictionary<int, string> blendshapeNames = new Dictionary<int, string>();
    private static List<KeyValuePair<int, string>> blendshapeNamesAlphabetical = new List<KeyValuePair<int, string>>();
    private static string currentObjectName;
    private static Vector2 displayScrollView;
    private static readonly GUIStyle buttonStyle1 = new GUIStyle();
    private static readonly GUIStyle buttonStyle2 = new GUIStyle();
    
    private static void OnEnablePrefix(UnityEditor.Editor __instance)
    {
        if (currentObjectName == __instance.target.name) return;
        Startup(__instance.target);
    }
    
    // This will run BEFORE Unity's inspector code
    private static void OnInspectorGUIPrefix(UnityEditor.Editor __instance)
    {
        if (skinnedMeshRenderer == null || blendshapeNames.Count <= 0 || blendshapeNamesAlphabetical.Count <= 0)
        {
            Startup(__instance.target);
        }
        
        if (skinnedMeshRenderer.sharedMesh.blendShapeCount <= 0) return;
        
        // Draw the search bar and search settings
        DisplaySearchBar();
        
        // Find all blendshapes that match the current search query
        if (string.IsNullOrEmpty(searchQuery))
        {
            if (blendshapeDisplayIndices.Count > 0) blendshapeDisplayIndices.Clear();
            GUILayout.Space(10);
            return;
        }
        GetShapekeyIndexToDisplay();
        
        // Display all blendshapes that match the current search query in a list below the search bar
        DisplayShapekeys();
        
        GUILayout.Space(10);
        
        // Apply changes to the serialized object to make sure the changes are saved
        __instance.serializedObject.ApplyModifiedProperties();
    }

    private static void Startup(Object target)
    {
        currentObjectName = target.name;
        skinnedMeshRenderer = target as SkinnedMeshRenderer;
        CacheBlendshapes();

        Texture2D hoverTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.cascas.unityshapekeysearch/Editor/hover_color.png");
        
        buttonStyle1.padding = new RectOffset(0, 0, 3, 3);
        buttonStyle1.hover.background = hoverTex;
        
        buttonStyle2.padding = new RectOffset(0, 0, 3, 3);
        buttonStyle2.normal.background = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.cascas.unityshapekeysearch/Editor/dark_color.png");
        buttonStyle2.hover.background = hoverTex;
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
        GUIContent popupContent = new GUIContent("Search Mode",  "Starts with mode will only return results that start with the " +
            "search query.\nContains mode will return results that are contained anywhere withing the search query.");
        GUILayout.Label(popupContent, GUILayout.Width(85));
        searchTypePopup = EditorGUILayout.Popup(searchTypePopup, new[] { "Starts With", "Contains" },
            GUILayout.Height(24), GUILayout.Width(120));
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    
    private static void GetShapekeyIndexToDisplay()
    {
        // If the search query is the same as the last one, and we don't need to update the list from sorting, don't do anything.
        //if (searchQuery == lastSearchQuery && sortAlphabetically == lastSortAlphabetically) return;
        
        // Add each space-separated word to the search query
        searchQueryWords = searchQuery.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        
        // Clear the list of indices to display and find all the new indices to display based on the search query and search settings.
        blendshapeDisplayIndices.Clear();
        {
            if (sortAlphabetically)
            {
                foreach (KeyValuePair<int, string> pair in blendshapeNamesAlphabetical)
                {
                    bool doesContain = searchQueryWords.All(word => ContainInSearch(pair.Value, word));
                    if (doesContain) blendshapeDisplayIndices.Add(pair.Key);
                }
            }
            else
            {
                for (int i = 0; i < blendshapeNames.Count; i++)
                {
                    bool doesContain = searchQueryWords.All(word => ContainInSearch(blendshapeNames[i], word));
                    if (doesContain) blendshapeDisplayIndices.Add(i);
                }
            }
        }
    }

    private static bool ContainInSearch(string shapekeyName, string searchString)
    {
        return searchTypePopup switch
        {
            0 => shapekeyName.StartsWith(searchString, StringComparison.OrdinalIgnoreCase),
            1 => shapekeyName.IndexOf(searchString, StringComparison.OrdinalIgnoreCase) >= 0,
            _ => false
        };
    }
    
    private static void DisplayShapekeys()
    {
        if (blendshapeDisplayIndices.Count <= 0)
        {
            GUILayout.Space(5);
            GUILayout.Label("No blendshapes found with the current search query.", EditorStyles.boldLabel);
            return;
        }
        
        GUILayout.FlexibleSpace();
        GUIStyle style = new GUIStyle(GUI.skin.FindStyle("window"))
        {
            padding = new RectOffset(5,5,5,5)
        };

        float elementHeight = 26f;
        float scrollViewHeight = Mathf.Min(blendshapeDisplayIndices.Count * elementHeight, 300f);
        
        EditorGUILayout.BeginVertical(style);
        displayScrollView = GUILayout.BeginScrollView(displayScrollView, "scrollView", GUILayout.Height(scrollViewHeight));
        
        foreach (int index in blendshapeDisplayIndices) // Draw index with range float slider
        {
            // If odd, display with box. Otherwise, display with default style
            EditorGUILayout.BeginHorizontal(index % 2 == 0 ? buttonStyle2 : buttonStyle1);
            //Debug.Log(test.height);
            // Display the name of the blendshape with the search query highlighted
            string blendshapeName = blendshapeNames[index];
            string pattern = string.Join("|", searchQueryWords.Select(Regex.Escape));
            blendshapeName = Regex.Replace(blendshapeName, pattern,
                match => $"<color=cyan><b>{match.Value}</b></color>",
                RegexOptions.IgnoreCase);           
            GUIStyle richLabel = new GUIStyle(EditorStyles.label) { richText = true };
            EditorGUILayout.LabelField(blendshapeName, richLabel, GUILayout.Width(150));
            
            EditorGUI.BeginChangeCheck();
            float value = skinnedMeshRenderer!.GetBlendShapeWeight(index);
            bool swap = GUILayout.Button(EditorGUIUtility.IconContent("d_preAudioLoopOff"));
            float newValue = EditorGUILayout.Slider(value, 0, 100);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(skinnedMeshRenderer, "Change BlendShape Weight");
                // If swap button is pressed, swap the value
                newValue = swap ? Mathf.Abs(newValue - 100) : newValue;
                if (!Mathf.Approximately(newValue, value))
                {
                    skinnedMeshRenderer.SetBlendShapeWeight(index, newValue);
                }
            }
            EditorGUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }
}

#endif