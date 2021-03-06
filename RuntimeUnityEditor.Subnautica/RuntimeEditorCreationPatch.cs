﻿using Harmony;

namespace RuntimeUnityEditor.Subnautica
{
    [HarmonyPatch(typeof(Player))]
    [HarmonyPatch("Start")]
    public static class RuntimeEditorCreationPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Player __instance)
        {
            EntryPoint.CreateRuntimeEditor(__instance.gameObject);
        }
    }
}
