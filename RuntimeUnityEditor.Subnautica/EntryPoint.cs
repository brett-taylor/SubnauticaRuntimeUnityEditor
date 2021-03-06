﻿using Harmony;
using RuntimeUnityEditor.Core;
using System.Reflection;
using QModManager.API.ModLoading;
using UnityEngine;

namespace RuntimeUnityEditor.Subnautica 
{
    [QModCore]
    public static class EntryPoint
    {
        public static RuntimeUnityEditorCore INSTANCE { get; private set; }

        [QModPatch]
        public static void Entry()
        {
            var harmony = HarmonyInstance.Create("taylor.brett.RuntimeUnityEditor.Subnautica.mod");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        internal static void CreateRuntimeEditor(GameObject objectToAddTo)
        {
            if (INSTANCE == null)
            {
                INSTANCE = objectToAddTo.AddComponent<RuntimeUnityEditorCore>();
                INSTANCE.Setup(
                    new RuntimeEditorLogger()
                );
            }
        }
    }
}
