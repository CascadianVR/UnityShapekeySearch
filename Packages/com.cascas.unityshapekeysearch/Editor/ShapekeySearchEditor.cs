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

namespace Cascadian.ShapekeySearch
{

    [InitializeOnLoad]
    public static class MeshRendererEditorPatcher
    {
        // State
        private static readonly bool IsInitialized = false;
        private static bool _sortAlphabetically = false;
        private static int _searchTypePopup = 1;
        private static string _searchQuery = "";
        private static string[] _searchQueryWords = Array.Empty<string>();
        private static SkinnedMeshRenderer _skinnedMeshRenderer;

        private static List<KeyValuePair<int, string>> _blendshapeNamesAlphabetical =
            new List<KeyValuePair<int, string>>();

        private static string _currentObjectName;
        private static readonly List<int> BlendshapeDisplayIndices = new List<int>();
        private static readonly Dictionary<int, string> BlendshapeNames = new Dictionary<int, string>();

        // UI
        private static Vector2 _displayScrollView;
        private static readonly GUIStyle ButtonStyle1 = new GUIStyle();
        private static readonly GUIStyle ButtonStyle2 = new GUIStyle();

        // Constants
        private static readonly string[][] SimilarWords =
        {
            new[] { "breast", "boob", "boobs", "tits", "chest" },
            new[] { "butt", "ass", "booty" },
            new[] { "stomach", "belly", "tummy", "abdomen" },
            new[] { "torso", "waist" },
            new[] { "thigh", "leg" },
            new[] { "hand", "palm" },
            new[] { "face", "head" },
        };

        static MeshRendererEditorPatcher()
        {
            if (IsInitialized) return;
            Harmony harmony = new Harmony("com.cascas.skinnedmeshrenderer.patch");

            // Get internal type: UnityEditor.SkinnedMeshRendererEditor
            Type editorType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.SkinnedMeshRendererEditor");
            if (editorType == null) return;
            MethodInfo methodToPatch = editorType.GetMethod("OnInspectorGUI",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(MeshRendererEditorPatcher).GetMethod(nameof(OnInspectorGUIPrefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo methodToPatch2 = editorType.GetMethod("OnEnable",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo prefix2 = typeof(MeshRendererEditorPatcher).GetMethod(nameof(OnEnablePrefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            harmony.Patch(methodToPatch, prefix: new HarmonyMethod(prefix) { priority = Priority.First });
            harmony.Patch(methodToPatch2, prefix: new HarmonyMethod(prefix2) { priority = Priority.First });
            IsInitialized = true;
        }

        private static void OnEnablePrefix(UnityEditor.Editor __instance)
        {
            if (_currentObjectName == __instance.target.name) return;
            Startup(__instance.target);
        }

        // This will run BEFORE Unity's inspector code
        private static void OnInspectorGUIPrefix(UnityEditor.Editor __instance)
        {
            if (_skinnedMeshRenderer == null || BlendshapeNames.Count <= 0 || _blendshapeNamesAlphabetical.Count <= 0)
            {
                Startup(__instance.target);
                if (_skinnedMeshRenderer == null || BlendshapeNames.Count <= 0 || _blendshapeNamesAlphabetical.Count <= 0)
                    return;
            }

            if (_skinnedMeshRenderer.sharedMesh.blendShapeCount <= 0) return;

            // Draw the search bar and search settings
            DisplaySearchBar();

            // Find all blendshapes that match the current search query
            if (string.IsNullOrEmpty(_searchQuery))
            {
                if (BlendshapeDisplayIndices.Count > 0) BlendshapeDisplayIndices.Clear();
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
            _currentObjectName = target.name;
            _skinnedMeshRenderer = target as SkinnedMeshRenderer;
            CacheBlendshapes();

            Texture2D hoverTex =
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Packages/com.cascas.unityshapekeysearch/Editor/hover_color.png");

            ButtonStyle1.padding = new RectOffset(0, 0, 3, 3);
            ButtonStyle1.hover.background = hoverTex;

            ButtonStyle2.padding = new RectOffset(0, 0, 3, 3);
            ButtonStyle2.normal.background =
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Packages/com.cascas.unityshapekeysearch/Editor/dark_color.png");
            ButtonStyle2.hover.background = hoverTex;
        }

        private static void CacheBlendshapes()
        {
            BlendshapeNames.Clear();
            for (int i = 0; i < _skinnedMeshRenderer.sharedMesh.blendShapeCount; i++)
            {
                string name = _skinnedMeshRenderer.sharedMesh.GetBlendShapeName(i);
                BlendshapeNames.Add(i, name);
            }

            _blendshapeNamesAlphabetical.Clear();
            _blendshapeNamesAlphabetical = BlendshapeNames
                .OrderBy(x => x.Value)
                .ToList(); // Maintains order
        }


        private static void DisplaySearchBar()
        {
            // Draw the search bar
            EditorGUILayout.BeginHorizontal();
            GUIStyle searchStyle = new GUIStyle(GUI.skin.FindStyle("ToolbarSearchTextField"))
            {
                fixedHeight = 0, // Let GUILayout control the height
                stretchHeight = true,
                fontSize = 13 // Optional: make text bigger too
            };

            Rect rect = EditorGUILayout.GetControlRect(GUILayout.Height(24));

            _searchQuery = EditorGUI.TextField(rect, _searchQuery, searchStyle);

            // If the field is empty, draw the placeholder label over it
            if (string.IsNullOrEmpty(_searchQuery))
            {
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 1f); // gray color
                Rect paddedRect = new Rect(rect.x + 6, rect.y, rect.width - 6, rect.height);
                GUI.Label(paddedRect, "  Search for shapekeys...");
                GUI.color = Color.white; // reset color
            }

            GUIStyle cancelButtonStyle = new GUIStyle(GUI.skin.FindStyle("ToolbarSearchCancelButton"))
            {
                fixedHeight = 0, // Let GUILayout control the height
                stretchHeight = true,
                fontSize = 13 // Optional: make text bigger too
            };
            if (GUILayout.Button("", cancelButtonStyle, GUILayout.Height(24)))
            {
                _searchQuery = "";
                GUI.FocusControl(null); // Unfocus the field
            }

            Texture icon = EditorGUIUtility.IconContent("d_TrueTypeFontImporter Icon").image;
            GUIContent buttonContent = new GUIContent(icon, "Sorts the results of the search alphabetically");
            _sortAlphabetically = GUILayout.Toggle(_sortAlphabetically, buttonContent,
                "Button", GUILayout.Height(24), GUILayout.Width(24));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUIContent popupContent = new GUIContent("Search Mode",
                "Starts with mode will only return results that start with the " +
                "search query.\nContains mode will return results that are contained anywhere withing the search query.");
            GUILayout.Label(popupContent, GUILayout.Width(85));
            _searchTypePopup = EditorGUILayout.Popup(_searchTypePopup, new[] { "Starts With", "Contains" },
                GUILayout.Height(24), GUILayout.Width(120));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private static void GetShapekeyIndexToDisplay()
        {
            // Add each space-separated word to the search query
            _searchQueryWords = _searchQuery.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // Clear the list of indices to display and find all the new indices to display based on the search query and search settings.
            BlendshapeDisplayIndices.Clear();
            {
                if (_sortAlphabetically)
                {
                    foreach (KeyValuePair<int, string> pair in _blendshapeNamesAlphabetical)
                    {
                        bool doesContain = _searchQueryWords.All(word => ContainInSearch(pair.Value, word));
                        if (doesContain) BlendshapeDisplayIndices.Add(pair.Key);
                    }
                }
                else
                {
                    for (int i = 0; i < BlendshapeNames.Count; i++)
                    {
                        bool doesContain = _searchQueryWords.All(word => ContainInSearch(BlendshapeNames[i], word));
                        if (doesContain) BlendshapeDisplayIndices.Add(i);
                    }
                }
            }
        }

        private static bool ContainInSearch(string shapekeyName, string searchString)
        {
            // FInd if there is a similar word in the SimilarWords array

            // Check if the search string belongs to any similar word group
            foreach (var similarWordGroup in SimilarWords)
            {
                if (similarWordGroup.Any(w => w.StartsWith(searchString, StringComparison.OrdinalIgnoreCase)))
                {
                    // Replace searchString with the first word in the group
                    searchString = similarWordGroup[0];
                    break;
                }
            }

            return _searchTypePopup switch
            {
                0 => shapekeyName.StartsWith(searchString, StringComparison.OrdinalIgnoreCase), // Starts With
                1 => shapekeyName.IndexOf(searchString, StringComparison.OrdinalIgnoreCase) >= 0, // Contains
                _ => false
            };
        }

        private static void DisplayShapekeys()
        {
            if (BlendshapeDisplayIndices.Count <= 0)
            {
                GUILayout.Space(5);
                GUILayout.Label("No blendshapes found with the current search query.", EditorStyles.boldLabel);
                return;
            }

            GUILayout.FlexibleSpace();
            GUIStyle style = new GUIStyle(GUI.skin.FindStyle("window"))
            {
                padding = new RectOffset(5, 5, 5, 5)
            };

            float elementHeight = 26f;
            float scrollViewHeight = Mathf.Min(BlendshapeDisplayIndices.Count * elementHeight, 300f);

            EditorGUILayout.BeginVertical(style);
            _displayScrollView =
                GUILayout.BeginScrollView(_displayScrollView, "scrollView", GUILayout.Height(scrollViewHeight));

            foreach (int index in BlendshapeDisplayIndices) // Draw index with range float slider
            {
                // If odd, display with box. Otherwise, display with default style
                EditorGUILayout.BeginHorizontal(index % 2 == 0 ? ButtonStyle2 : ButtonStyle1);

                // Display the name of the blendshape with the search query highlighted
                string blendshapeName = BlendshapeNames[index];
                string pattern = string.Join("|", _searchQueryWords.Select(Regex.Escape));
                blendshapeName = Regex.Replace(blendshapeName, pattern,
                    match => $"<color=cyan><b>{match.Value}</b></color>", RegexOptions.IgnoreCase);
                GUIStyle richLabel = new GUIStyle(EditorStyles.label) { richText = true };
                EditorGUILayout.LabelField(blendshapeName, richLabel, GUILayout.Width(150));

                EditorGUI.BeginChangeCheck();
                float value = _skinnedMeshRenderer!.GetBlendShapeWeight(index);
                bool swap = GUILayout.Button(EditorGUIUtility.IconContent("d_preAudioLoopOff"));
                float newValue = EditorGUILayout.Slider(value, 0, 100);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_skinnedMeshRenderer, "Change BlendShape Weight");
                    // If swap button is pressed, swap the value
                    newValue = swap ? Mathf.Abs(newValue - 100) : newValue;
                    if (!Mathf.Approximately(newValue, value))
                    {
                        _skinnedMeshRenderer.SetBlendShapeWeight(index, newValue);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }
    }
}

#endif