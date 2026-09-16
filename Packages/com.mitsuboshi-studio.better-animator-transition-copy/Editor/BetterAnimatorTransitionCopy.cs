// BetterAnimatorTransitionCopy_2022_3_v1_3.cs
// Unity 2022.3.x
// Better Animator Transition Copy
//
// Place this file anywhere under an Editor folder.
//
// Purpose:
//   Adds these two items to the BOTTOM of the Animator graph's existing
//   right-click context menu when an AnimatorStateTransition is selected,
//   and maps Ctrl+C / Ctrl+V to the same Collection operations:
//
//     Copy Transition Collection
//     Paste Transition Collection
//
// A "Transition Collection" means all parallel AnimatorStateTransitions
// that share the same Source and Destination.
//
// Copy/Paste intentionally transfers CONDITIONS only.
// Existing destination transition settings are preserved.
// When the destination collection has fewer transitions than the copied
// collection, missing transitions are automatically created using the
// right-clicked destination transition as the settings template.
//
// This file does NOT add anything under Tools.
// This file does NOT use CONTEXT/AnimatorStateTransition MenuItems.
//
// Menu / hotkey injection:
//   Unity's Animator graph builds a private GenericMenu.
//   This script patches GenericMenu.ShowAsContext through Harmony at runtime
//   and appends the two items only when the menu is recognized as the
//   Animator graph context menu.
//
//   Ctrl+C / Ctrl+V are intercepted at GUIUtility.ProcessEvent, before the
//   event is dispatched into Animator. This prevents Unity's native Paste
//   handling from clearing the selected Transition.
//
// Harmony is resolved by reflection, so this script has no compile-time
// dependency on HarmonyLib. In VRChat projects, Harmony is normally provided
// by the current VRChat SDK / related packages.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Rinya.AnimatorTools
{
    [InitializeOnLoad]
    internal static class BetterAnimatorTransitionCopy
    {
        private const string ExtensionVersion = "1.3";

        private const string HarmonyId =
            "rinya.better-animator-transition-copy.2022_3";

        private const string CopyMenuName =
            "Copy Transition Collection";

        private const string PasteMenuName =
            "Paste Transition Collection";

        private static readonly BindingFlags InstanceFlags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        private static readonly BindingFlags StaticFlags =
            BindingFlags.Static |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        private static FieldInfo s_GenericMenuItemsField;
        private static bool s_PatchInstalled;
        private static object s_HarmonyInstance;

        private static ClipboardData s_Clipboard;

        // ---------------------------------------------------------------------
        // Data
        // ---------------------------------------------------------------------

        private struct ConditionData
        {
            public AnimatorConditionMode mode;
            public float threshold;
            public string parameter;

            public ConditionData(AnimatorCondition condition)
            {
                mode = condition.mode;
                threshold = condition.threshold;
                parameter = condition.parameter;
            }
        }

        private sealed class ClipboardData
        {
            public readonly List<ConditionData[]> transitionConditions =
                new List<ConditionData[]>();

            public string sourceDescription;

            public int Count
            {
                get { return transitionConditions.Count; }
            }
        }

        private sealed class TransitionGroup
        {
            public AnimatorController controller;
            public AnimatorStateMachine sourceStateMachine;
            public AnimatorState sourceState;
            public bool isAnyState;

            public readonly List<AnimatorStateTransition> transitions =
                new List<AnimatorStateTransition>();

            public string SourceLabel
            {
                get
                {
                    if (isAnyState)
                        return "Any State";

                    return sourceState != null
                        ? sourceState.name
                        : "(Unknown Source)";
                }
            }
        }

        // ---------------------------------------------------------------------
        // Bootstrap
        // ---------------------------------------------------------------------

        static BetterAnimatorTransitionCopy()
        {
            EditorApplication.delayCall += InstallPatch;
        }

        private static void InstallPatch()
        {
            if (s_PatchInstalled)
                return;

            try
            {
                Type harmonyType = FindType("HarmonyLib.Harmony");
                Type harmonyMethodType = FindType("HarmonyLib.HarmonyMethod");

                if (harmonyType == null || harmonyMethodType == null)
                {
                    Debug.LogWarning(
                        "[Better Animator Transition Copy] HarmonyLib was not found. " +
                        "The Animator context-menu / hotkey extension could not be installed.");
                    return;
                }

                ConstructorInfo harmonyCtor =
                    harmonyType.GetConstructor(new[] { typeof(string) });

                if (harmonyCtor == null)
                {
                    Debug.LogError(
                        "[Better Animator Transition Copy] Harmony constructor was not found.");
                    return;
                }

                object harmony =
                    harmonyCtor.Invoke(new object[] { HarmonyId });

                // Remove an older patch with the same ID, if one survived a script reload.
                MethodInfo unpatchSelf =
                    harmonyType.GetMethod(
                        "UnpatchSelf",
                        InstanceFlags,
                        null,
                        Type.EmptyTypes,
                        null);

                if (unpatchSelf != null)
                {
                    try
                    {
                        unpatchSelf.Invoke(harmony, null);
                    }
                    catch
                    {
                        // Non-fatal. Continue and attempt to patch.
                    }
                }

                MethodInfo genericMenuOriginal =
                    typeof(GenericMenu).GetMethod(
                        "ShowAsContext",
                        InstanceFlags,
                        null,
                        Type.EmptyTypes,
                        null);

                MethodInfo genericMenuPrefix =
                    typeof(BetterAnimatorTransitionCopy)
                        .GetMethod(
                            "GenericMenuShowAsContextPrefix",
                            StaticFlags);

                MethodInfo processEventOriginal =
                    typeof(GUIUtility).GetMethod(
                        "ProcessEvent",
                        StaticFlags,
                        null,
                        new[]
                        {
                            typeof(int),
                            typeof(IntPtr),
                            typeof(bool).MakeByRefType()
                        },
                        null);

                MethodInfo processEventPrefix =
                    typeof(BetterAnimatorTransitionCopy)
                        .GetMethod(
                            "GUIUtilityProcessEventPrefix",
                            StaticFlags);

                if (genericMenuOriginal == null ||
                    genericMenuPrefix == null ||
                    processEventOriginal == null ||
                    processEventPrefix == null)
                {
                    Debug.LogError(
                        "[Better Animator Transition Copy] One or more patch targets could not be resolved.");
                    return;
                }

                if (!PatchPrefix(
                    harmony,
                    harmonyType,
                    harmonyMethodType,
                    genericMenuOriginal,
                    genericMenuPrefix,
                    0)) // Harmony Priority.Last
                {
                    return;
                }

                if (!PatchPrefix(
                    harmony,
                    harmonyType,
                    harmonyMethodType,
                    processEventOriginal,
                    processEventPrefix,
                    800)) // Harmony Priority.First
                {
                    return;
                }

                s_HarmonyInstance = harmony;
                s_PatchInstalled = true;

                Debug.Log(
                    "[Better Animator Transition Copy] v" +
                    ExtensionVersion +
                    " installed: context menu + early Ctrl+C/Ctrl+V interception.");
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    "[Better Animator Transition Copy] Failed to install Animator extensions.\n" +
                    ex);
            }
        }

        private static bool PatchPrefix(
            object harmony,
            Type harmonyType,
            Type harmonyMethodType,
            MethodInfo original,
            MethodInfo prefix,
            int priority)
        {
            object harmonyMethod =
                CreateHarmonyMethod(
                    harmonyMethodType,
                    prefix,
                    priority);

            if (harmonyMethod == null)
            {
                Debug.LogError(
                    "[Better Animator Transition Copy] HarmonyMethod could not be created for " +
                    prefix.Name +
                    ".");
                return false;
            }

            MethodInfo patchMethod =
                FindHarmonyPatchMethod(
                    harmonyType);

            if (patchMethod == null)
            {
                Debug.LogError(
                    "[Better Animator Transition Copy] Harmony.Patch method was not found.");
                return false;
            }

            ParameterInfo[] patchParameters =
                patchMethod.GetParameters();

            object[] args =
                new object[
                    patchParameters.Length];

            for (int i = 0;
                 i < patchParameters.Length;
                 i++)
            {
                string parameterName =
                    patchParameters[i].Name;

                if (i == 0 ||
                    string.Equals(
                        parameterName,
                        "original",
                        StringComparison.OrdinalIgnoreCase))
                {
                    args[i] =
                        original;
                }
                else if (string.Equals(
                    parameterName,
                    "prefix",
                    StringComparison.OrdinalIgnoreCase))
                {
                    args[i] =
                        harmonyMethod;
                }
                else
                {
                    args[i] =
                        null;
                }
            }

            patchMethod.Invoke(
                harmony,
                args);

            return true;
        }

        private static Type FindType(string fullName)
        {
            Assembly[] assemblies =
                AppDomain.CurrentDomain.GetAssemblies();

            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type =
                    assemblies[i].GetType(
                        fullName,
                        false);

                if (type != null)
                    return type;
            }

            return null;
        }

        private static object CreateHarmonyMethod(
            Type harmonyMethodType,
            MethodInfo method,
            int priority)
        {
            object harmonyMethod = null;

            ConstructorInfo ctor =
                harmonyMethodType.GetConstructor(
                    new[] { typeof(MethodInfo) });

            if (ctor != null)
            {
                harmonyMethod =
                    ctor.Invoke(new object[] { method });
            }
            else
            {
                ConstructorInfo emptyCtor =
                    harmonyMethodType.GetConstructor(
                        Type.EmptyTypes);

                if (emptyCtor == null)
                    return null;

                harmonyMethod =
                    emptyCtor.Invoke(null);

                FieldInfo methodField =
                    harmonyMethodType.GetField(
                        "method",
                        InstanceFlags);

                PropertyInfo methodProperty =
                    harmonyMethodType.GetProperty(
                        "method",
                        InstanceFlags);

                if (methodField != null)
                {
                    methodField.SetValue(
                        harmonyMethod,
                        method);
                }
                else if (methodProperty != null &&
                         methodProperty.CanWrite)
                {
                    methodProperty.SetValue(
                        harmonyMethod,
                        method,
                        null);
                }
                else
                {
                    return null;
                }
            }

            SetHarmonyPriority(
                harmonyMethodType,
                harmonyMethod,
                priority);

            return harmonyMethod;
        }

        private static void SetHarmonyPriority(
            Type harmonyMethodType,
            object harmonyMethod,
            int priority)
        {
            try
            {
                FieldInfo priorityField =
                    harmonyMethodType.GetField(
                        "priority",
                        InstanceFlags);

                if (priorityField != null)
                {
                    priorityField.SetValue(
                        harmonyMethod,
                        priority);
                    return;
                }

                PropertyInfo priorityProperty =
                    harmonyMethodType.GetProperty(
                        "priority",
                        InstanceFlags);

                if (priorityProperty != null &&
                    priorityProperty.CanWrite)
                {
                    priorityProperty.SetValue(
                        harmonyMethod,
                        priority,
                        null);
                }
            }
            catch
            {
                // Priority is optional. Default Harmony priority is still usable.
            }
        }

        private static MethodInfo FindHarmonyPatchMethod(
            Type harmonyType)
        {
            MethodInfo[] methods =
                harmonyType.GetMethods(InstanceFlags);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];

                if (method.Name != "Patch")
                    continue;

                ParameterInfo[] parameters =
                    method.GetParameters();

                if (parameters.Length < 2)
                    continue;

                if (!typeof(MethodBase).IsAssignableFrom(
                        parameters[0].ParameterType))
                    continue;

                bool hasPrefix = false;

                for (int p = 1; p < parameters.Length; p++)
                {
                    if (string.Equals(
                        parameters[p].Name,
                        "prefix",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        hasPrefix = true;
                        break;
                    }
                }

                if (hasPrefix)
                    return method;
            }

            return null;
        }

        // ---------------------------------------------------------------------
        // Early Ctrl+C / Ctrl+V interception
        // ---------------------------------------------------------------------

        // Unity 2022.3's GUIUtility.ProcessEvent is called before the event is
        // dispatched into the target Editor window. By intercepting it here,
        // Transition Collection hotkeys can be handled BEFORE Animator's native
        // Copy/Paste path has a chance to clear the selected Transition.
        //
        // The original GUIUtility.ProcessEvent is skipped ONLY when:
        //   - the incoming event is Ctrl/Cmd + C or V,
        //   - the Animator window is focused,
        //   - an AnimatorStateTransition is currently selected,
        //   - no text field is being edited.
        //
        // State hotkeys, text editing, and Ctrl+C/V in all other windows are
        // untouched.

        private static MethodInfo s_EventCopyFromPtrMethod;
        private static bool s_HotkeyOperationInProgress;

        // Harmony prefix for:
        // GUIUtility.ProcessEvent(int instanceID, IntPtr nativeEventPtr, out bool result)
        private static bool GUIUtilityProcessEventPrefix(
            int instanceID,
            IntPtr nativeEventPtr,
            ref bool result)
        {
            if (s_HotkeyOperationInProgress)
                return true;

            if (nativeEventPtr == IntPtr.Zero)
                return true;

            if (!IsAnimatorWindowFocused())
                return true;

            if (EditorGUIUtility.editingTextField)
                return true;

            AnimatorStateTransition selected =
                Selection.activeObject
                as AnimatorStateTransition;

            if (selected == null)
                return true;

            Event incoming =
                ReadNativeEvent(
                    nativeEventPtr);

            if (incoming == null)
                return true;

            if (incoming.type != EventType.KeyDown)
                return true;

            if (incoming.keyCode != KeyCode.C &&
                incoming.keyCode != KeyCode.V)
            {
                return true;
            }

            EventModifiers modifiers =
                incoming.modifiers;

            bool actionModifier =
                (modifiers & EventModifiers.Control) != 0 ||
                (modifiers & EventModifiers.Command) != 0;

            bool unwantedModifier =
                (modifiers & EventModifiers.Alt) != 0 ||
                (modifiers & EventModifiers.Shift) != 0;

            if (!actionModifier ||
                unwantedModifier)
            {
                return true;
            }

            try
            {
                s_HotkeyOperationInProgress = true;

                if (incoming.keyCode == KeyCode.C)
                {
                    CopyCurrentCollection();
                }
                else
                {
                    // When no Collection has been copied yet, Ctrl+V becomes a
                    // harmless no-op while a Transition is selected. We still
                    // consume it so Unity's standard Paste path cannot deselect
                    // the Transition line.
                    if (s_Clipboard != null &&
                        s_Clipboard.Count > 0)
                    {
                        PasteCurrentCollection();
                    }
                }

                // GUIUtility.ProcessEvent has an out bool named "result".
                // Setting it true and returning false from the Harmony prefix
                // marks this event as handled and prevents Unity from processing
                // the original Ctrl+C / Ctrl+V event.
                result = true;
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    "[Better Animator Transition Copy] Ctrl+C/Ctrl+V handling failed.\n" +
                    ex);

                // Let Unity handle the event normally if our operation failed.
                return true;
            }
            finally
            {
                s_HotkeyOperationInProgress = false;
            }
        }

        private static Event ReadNativeEvent(
            IntPtr nativeEventPtr)
        {
            try
            {
                if (s_EventCopyFromPtrMethod == null)
                {
                    s_EventCopyFromPtrMethod =
                        typeof(Event).GetMethod(
                            "CopyFromPtr",
                            InstanceFlags,
                            null,
                            new[] { typeof(IntPtr) },
                            null);
                }

                if (s_EventCopyFromPtrMethod == null)
                    return null;

                Event copy =
                    new Event();

                s_EventCopyFromPtrMethod.Invoke(
                    copy,
                    new object[] { nativeEventPtr });

                return copy;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsAnimatorWindowFocused()
        {
            EditorWindow focused =
                EditorWindow.focusedWindow;

            return
                focused != null &&
                focused.GetType().FullName ==
                    "UnityEditor.Graphs.AnimatorControllerTool";
        }

        // ---------------------------------------------------------------------
        // GenericMenu injection
        // ---------------------------------------------------------------------

        // Called by Harmony immediately before GenericMenu.ShowAsContext().
        // Do not rename the __instance parameter; Harmony uses this special name.
        private static void GenericMenuShowAsContextPrefix(
            GenericMenu __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                AnimatorStateTransition transition =
                    Selection.activeObject
                    as AnimatorStateTransition;

                if (transition == null)
                    return;

                // Only touch the specific Animator graph context menu.
                // This prevents the two items appearing in unrelated GenericMenus.
                if (!IsAnimatorGraphContextMenu(__instance))
                    return;

                if (MenuContains(
                    __instance,
                    CopyMenuName))
                {
                    return;
                }

                // No separator on purpose:
                // the user requested the same feel as Unity's normal copy/paste items.
                __instance.AddItem(
                    new GUIContent(CopyMenuName),
                    false,
                    CopyCurrentCollection);

                if (s_Clipboard != null &&
                    s_Clipboard.Count > 0)
                {
                    __instance.AddItem(
                        new GUIContent(PasteMenuName),
                        false,
                        PasteCurrentCollection);
                }
                else
                {
                    __instance.AddDisabledItem(
                        new GUIContent(PasteMenuName));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    "[Better Animator Transition Copy] Failed to extend Animator context menu.\n" +
                    ex);
            }
        }

        private static bool IsAnimatorGraphContextMenu(
            GenericMenu menu)
        {
            // The Animator graph's empty-area / transition context menu contains
            // these characteristic entries in Unity 2022.3.
            bool hasCreateSubStateMachine =
                MenuContains(
                    menu,
                    "Create Sub-State Machine");

            bool hasCopyCurrentStateMachine =
                MenuContains(
                    menu,
                    "Copy current StateMachine");

            if (!hasCreateSubStateMachine ||
                !hasCopyCurrentStateMachine)
            {
                return false;
            }

            EditorWindow focused =
                EditorWindow.focusedWindow;

            if (focused != null)
            {
                string typeName =
                    focused.GetType().FullName;

                if (typeName ==
                    "UnityEditor.Graphs.AnimatorControllerTool")
                {
                    return true;
                }
            }

            // Fallback:
            // the two characteristic menu items are already specific enough
            // for Unity 2022.3's Animator graph menu.
            return true;
        }

        private static bool MenuContains(
            GenericMenu menu,
            string itemName)
        {
            IList items =
                GetGenericMenuItems(menu);

            if (items == null)
                return false;

            for (int i = 0; i < items.Count; i++)
            {
                object menuItem =
                    items[i];

                if (menuItem == null)
                    continue;

                FieldInfo contentField =
                    menuItem.GetType().GetField(
                        "content",
                        InstanceFlags);

                if (contentField == null)
                    continue;

                GUIContent content =
                    contentField.GetValue(menuItem)
                    as GUIContent;

                if (content == null)
                    continue;

                if (content.text == itemName)
                    return true;
            }

            return false;
        }

        private static IList GetGenericMenuItems(
            GenericMenu menu)
        {
            if (s_GenericMenuItemsField == null)
            {
                s_GenericMenuItemsField =
                    typeof(GenericMenu).GetField(
                        "m_MenuItems",
                        InstanceFlags);
            }

            if (s_GenericMenuItemsField == null)
                return null;

            return s_GenericMenuItemsField.GetValue(menu)
                as IList;
        }

        // ---------------------------------------------------------------------
        // Menu callbacks
        // ---------------------------------------------------------------------

        private static void CopyCurrentCollection()
        {
            AnimatorStateTransition selected =
                Selection.activeObject
                as AnimatorStateTransition;

            if (selected == null)
                return;

            TransitionGroup group;

            if (!TryFindTransitionGroup(
                selected,
                out group))
            {
                EditorUtility.DisplayDialog(
                    CopyMenuName,
                    "対象のTransition CollectionをAnimatorController内で見つけられませんでした。",
                    "OK");
                return;
            }

            ClipboardData clipboard =
                new ClipboardData();

            clipboard.sourceDescription =
                BuildGroupDescription(
                    group,
                    selected);

            for (int i = 0;
                 i < group.transitions.Count;
                 i++)
            {
                AnimatorCondition[] conditions =
                    group.transitions[i].conditions;

                ConditionData[] copiedConditions =
                    new ConditionData[
                        conditions.Length];

                for (int c = 0;
                     c < conditions.Length;
                     c++)
                {
                    copiedConditions[c] =
                        new ConditionData(
                            conditions[c]);
                }

                clipboard.transitionConditions.Add(
                    copiedConditions);
            }

            s_Clipboard = clipboard;

            Debug.Log(
                "[Better Animator Transition Copy] Copied " +
                clipboard.Count +
                " transition(s): " +
                clipboard.sourceDescription);
        }

        private static void PasteCurrentCollection()
        {
            if (s_Clipboard == null ||
                s_Clipboard.Count == 0)
            {
                return;
            }

            AnimatorStateTransition selected =
                Selection.activeObject
                as AnimatorStateTransition;

            if (selected == null)
                return;

            TransitionGroup targetGroup;

            if (!TryFindTransitionGroup(
                selected,
                out targetGroup))
            {
                EditorUtility.DisplayDialog(
                    PasteMenuName,
                    "貼り付け先のTransition CollectionをAnimatorController内で見つけられませんでした。",
                    "OK");
                return;
            }

            if (targetGroup.transitions.Count >
                s_Clipboard.Count)
            {
                EditorUtility.DisplayDialog(
                    PasteMenuName,
                    "貼り付け先のTransition数の方が多いため、貼り付けを中止しました。\n\n" +
                    "コピー元: " +
                    s_Clipboard.Count +
                    " 本\n" +
                    "貼り付け先: " +
                    targetGroup.transitions.Count +
                    " 本\n\n" +
                    "余分なTransitionを自動削除しない安全仕様です。",
                    "OK");
                return;
            }

            int undoGroup =
                Undo.GetCurrentGroup();

            Undo.SetCurrentGroupName(
                PasteMenuName);

            int addedCount = 0;

            try
            {
                while (targetGroup.transitions.Count <
                       s_Clipboard.Count)
                {
                    AnimatorStateTransition newTransition =
                        CreateParallelTransition(
                            targetGroup,
                            selected);

                    if (newTransition == null)
                    {
                        EditorUtility.DisplayDialog(
                            PasteMenuName,
                            "不足分のTransitionを作成できませんでした。\n" +
                            "ここまでの変更はUndoできます。",
                            "OK");
                        return;
                    }

                    Undo.RegisterCreatedObjectUndo(
                        newTransition,
                        PasteMenuName);

                    Undo.RecordObject(
                        newTransition,
                        "Copy Transition Settings");

                    CopyTransitionSettings(
                        selected,
                        newTransition);

                    EditorUtility.SetDirty(
                        newTransition);

                    targetGroup.transitions.Add(
                        newTransition);

                    addedCount++;
                }

                ApplyClipboard(
                    targetGroup.transitions);

                MarkGroupOwnerDirty(
                    targetGroup);

                AssetDatabase.SaveAssets();

                RebuildAnimatorGraph();
                UnityEditorInternal.InternalEditorUtility
                    .RepaintAllViews();
            }
            finally
            {
                Undo.CollapseUndoOperations(
                    undoGroup);
            }

            Debug.Log(
                "[Better Animator Transition Copy] Pasted " +
                s_Clipboard.Count +
                " transition(s) to: " +
                BuildGroupDescription(
                    targetGroup,
                    selected) +
                (addedCount > 0
                    ? " (auto-added " +
                      addedCount +
                      " transition(s))"
                    : ""));
        }

        // ---------------------------------------------------------------------
        // Collection discovery
        // ---------------------------------------------------------------------

        private static bool TryFindTransitionGroup(
            AnimatorStateTransition selected,
            out TransitionGroup result)
        {
            result = null;

            if (selected == null)
                return false;

            string assetPath =
                AssetDatabase.GetAssetPath(
                    selected);

            if (string.IsNullOrEmpty(assetPath))
                return false;

            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath
                    <AnimatorController>(
                        assetPath);

            if (controller == null)
                return false;

            AnimatorControllerLayer[] layers =
                controller.layers;

            for (int i = 0;
                 i < layers.Length;
                 i++)
            {
                AnimatorStateMachine root =
                    layers[i].stateMachine;

                if (root == null)
                    continue;

                TransitionGroup found;

                if (TryFindTransitionGroupRecursive(
                    controller,
                    root,
                    selected,
                    out found))
                {
                    result = found;
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindTransitionGroupRecursive(
            AnimatorController controller,
            AnimatorStateMachine stateMachine,
            AnimatorStateTransition selected,
            out TransitionGroup result)
        {
            result = null;

            // State -> State / StateMachine / Exit
            ChildAnimatorState[] states =
                stateMachine.states;

            for (int s = 0;
                 s < states.Length;
                 s++)
            {
                AnimatorState state =
                    states[s].state;

                if (state == null)
                    continue;

                AnimatorStateTransition[] transitions =
                    state.transitions;

                if (!ContainsTransition(
                    transitions,
                    selected))
                {
                    continue;
                }

                TransitionGroup group =
                    new TransitionGroup();

                group.controller =
                    controller;

                group.sourceStateMachine =
                    stateMachine;

                group.sourceState =
                    state;

                group.isAnyState =
                    false;

                for (int i = 0;
                     i < transitions.Length;
                     i++)
                {
                    if (SameDestination(
                        transitions[i],
                        selected))
                    {
                        group.transitions.Add(
                            transitions[i]);
                    }
                }

                result = group;
                return true;
            }

            // Any State -> State / StateMachine
            AnimatorStateTransition[] anyTransitions =
                stateMachine.anyStateTransitions;

            if (ContainsTransition(
                anyTransitions,
                selected))
            {
                TransitionGroup group =
                    new TransitionGroup();

                group.controller =
                    controller;

                group.sourceStateMachine =
                    stateMachine;

                group.sourceState =
                    null;

                group.isAnyState =
                    true;

                for (int i = 0;
                     i < anyTransitions.Length;
                     i++)
                {
                    if (SameDestination(
                        anyTransitions[i],
                        selected))
                    {
                        group.transitions.Add(
                            anyTransitions[i]);
                    }
                }

                result = group;
                return true;
            }

            ChildAnimatorStateMachine[] children =
                stateMachine.stateMachines;

            for (int i = 0;
                 i < children.Length;
                 i++)
            {
                AnimatorStateMachine child =
                    children[i].stateMachine;

                if (child == null)
                    continue;

                TransitionGroup found;

                if (TryFindTransitionGroupRecursive(
                    controller,
                    child,
                    selected,
                    out found))
                {
                    result = found;
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsTransition(
            AnimatorStateTransition[] transitions,
            AnimatorStateTransition selected)
        {
            for (int i = 0;
                 i < transitions.Length;
                 i++)
            {
                if (transitions[i] == selected)
                    return true;
            }

            return false;
        }

        private static bool SameDestination(
            AnimatorStateTransition a,
            AnimatorStateTransition b)
        {
            if (a == null ||
                b == null)
            {
                return false;
            }

            if (a.isExit != b.isExit)
                return false;

            if (a.isExit)
                return true;

            return
                a.destinationState ==
                    b.destinationState &&
                a.destinationStateMachine ==
                    b.destinationStateMachine;
        }

        // ---------------------------------------------------------------------
        // Paste implementation
        // ---------------------------------------------------------------------

        private static void ApplyClipboard(
            List<AnimatorStateTransition> targets)
        {
            if (targets.Count !=
                s_Clipboard.Count)
            {
                throw new InvalidOperationException(
                    "Clipboard and target transition counts do not match.");
            }

            UnityEngine.Object[] undoTargets =
                new UnityEngine.Object[
                    targets.Count];

            for (int i = 0;
                 i < targets.Count;
                 i++)
            {
                undoTargets[i] =
                    targets[i];
            }

            Undo.RecordObjects(
                undoTargets,
                "Paste Transition Collection Conditions");

            for (int i = 0;
                 i < targets.Count;
                 i++)
            {
                ReplaceConditions(
                    targets[i],
                    s_Clipboard.transitionConditions[i]);

                EditorUtility.SetDirty(
                    targets[i]);
            }
        }

        private static void ReplaceConditions(
            AnimatorStateTransition transition,
            ConditionData[] newConditions)
        {
            AnimatorCondition[] oldConditions =
                transition.conditions;

            for (int i = 0;
                 i < oldConditions.Length;
                 i++)
            {
                transition.RemoveCondition(
                    oldConditions[i]);
            }

            for (int i = 0;
                 i < newConditions.Length;
                 i++)
            {
                ConditionData condition =
                    newConditions[i];

                transition.AddCondition(
                    condition.mode,
                    condition.threshold,
                    condition.parameter);
            }
        }

        private static AnimatorStateTransition
            CreateParallelTransition(
                TransitionGroup targetGroup,
                AnimatorStateTransition template)
        {
            if (targetGroup.isAnyState)
            {
                if (targetGroup.sourceStateMachine == null)
                    return null;

                Undo.RecordObject(
                    targetGroup.sourceStateMachine,
                    PasteMenuName);

                if (template.destinationState != null)
                {
                    return targetGroup
                        .sourceStateMachine
                        .AddAnyStateTransition(
                            template.destinationState);
                }

                if (template.destinationStateMachine != null)
                {
                    return targetGroup
                        .sourceStateMachine
                        .AddAnyStateTransition(
                            template.destinationStateMachine);
                }

                return null;
            }

            if (targetGroup.sourceState == null)
                return null;

            Undo.RecordObject(
                targetGroup.sourceState,
                PasteMenuName);

            if (template.isExit)
            {
                return targetGroup
                    .sourceState
                    .AddExitTransition();
            }

            if (template.destinationState != null)
            {
                return targetGroup
                    .sourceState
                    .AddTransition(
                        template.destinationState);
            }

            if (template.destinationStateMachine != null)
            {
                return targetGroup
                    .sourceState
                    .AddTransition(
                        template.destinationStateMachine);
            }

            return null;
        }

        private static void CopyTransitionSettings(
            AnimatorStateTransition source,
            AnimatorStateTransition destination)
        {
            // Conditions are intentionally NOT copied here.
            destination.mute =
                source.mute;

            destination.solo =
                source.solo;

            destination.canTransitionToSelf =
                source.canTransitionToSelf;

            destination.duration =
                source.duration;

            destination.exitTime =
                source.exitTime;

            destination.hasExitTime =
                source.hasExitTime;

            destination.hasFixedDuration =
                source.hasFixedDuration;

            destination.interruptionSource =
                source.interruptionSource;

            destination.offset =
                source.offset;

            destination.orderedInterruption =
                source.orderedInterruption;
        }

        private static void MarkGroupOwnerDirty(
            TransitionGroup group)
        {
            if (group == null)
                return;

            if (group.sourceState != null)
            {
                EditorUtility.SetDirty(
                    group.sourceState);
            }

            if (group.sourceStateMachine != null)
            {
                EditorUtility.SetDirty(
                    group.sourceStateMachine);
            }

            if (group.controller != null)
            {
                EditorUtility.SetDirty(
                    group.controller);
            }
        }

        private static void RebuildAnimatorGraph()
        {
            try
            {
                Type toolType =
                    FindType(
                        "UnityEditor.Graphs.AnimatorControllerTool");

                if (toolType == null)
                    return;

                FieldInfo toolField =
                    toolType.GetField(
                        "tool",
                        StaticFlags);

                if (toolField == null)
                    return;

                object tool =
                    toolField.GetValue(null);

                if (tool == null)
                    return;

                MethodInfo rebuild =
                    toolType.GetMethod(
                        "RebuildGraph",
                        InstanceFlags,
                        null,
                        new[] { typeof(bool) },
                        null);

                if (rebuild == null)
                    return;

                rebuild.Invoke(
                    tool,
                    new object[] { false });
            }
            catch
            {
                // RepaintAllViews is still called by the caller.
            }
        }

        // ---------------------------------------------------------------------
        // Labels
        // ---------------------------------------------------------------------

        private static string BuildGroupDescription(
            TransitionGroup group,
            AnimatorStateTransition selected)
        {
            return
                group.SourceLabel +
                " -> " +
                GetDestinationLabel(
                    selected) +
                " [" +
                group.transitions.Count +
                " transition(s)]";
        }

        private static string GetDestinationLabel(
            AnimatorStateTransition transition)
        {
            if (transition.isExit)
                return "Exit";

            if (transition.destinationState != null)
            {
                return transition
                    .destinationState
                    .name;
            }

            if (transition.destinationStateMachine != null)
            {
                return transition
                    .destinationStateMachine
                    .name;
            }

            return "(Unknown Destination)";
        }
    }
}
