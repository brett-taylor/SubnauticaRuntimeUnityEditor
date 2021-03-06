﻿using System;
using System.Collections.Generic;
using RuntimeUnityEditor.Core.Gizmos;
using RuntimeUnityEditor.Core.ObjectTree;
using RuntimeUnityEditor.Core.PinnedVariables;
using RuntimeUnityEditor.Core.REPL;
using RuntimeUnityEditor.Core.Settings;
using RuntimeUnityEditor.Core.MaterialEditor;
using RuntimeUnityEditor.Core.Popup.Dialogs;
using RuntimeUnityEditor.Core.UI;
using RuntimeUnityEditor.Core.Utils;
using UnityEngine;

namespace RuntimeUnityEditor.Core
{
    public class RuntimeUnityEditorCore : MonoBehaviour
    {
        public static RuntimeUnityEditorCore INSTANCE { get; private set; }
        internal static KeyCode SHOW_HOT_KEY => KeyCode.F7;
        internal static ILoggerWrapper LOGGER { get; private set; }
        private const float UPDATE_OBJECT_TREE_EVERY_X_SECONDS = 5f;

        public Inspector.Inspector Inspector { get; private set; }
        public ObjectTreeViewer TreeViewer { get; private set; }
        public ReplWindow Repl { get; private set; }
        public SettingsData SettingsData { get; private set; }
        public SettingsViewer SettingsViewer { get; private set; }
        public PinnedVariablesData PinnedVariablesData { get; private set; }
        public PinnedVariablesViewer PinnedVariablesViewer { get; private set; }

        private GizmoDrawer _gizmoDrawer;
        private GameObjectSearcher _gameObjectSearcher = new GameObjectSearcher();
        private CursorLockMode _previousCursorLockState;
        private bool _previousCursorVisible;
        private readonly List<Window> windows = new List<Window>();
        private bool _show;
        private float updateObjectTreeTimer = 0f;

        public void Setup(ILoggerWrapper logger)
        {
            INSTANCE = this;
            LOGGER = logger;

            SettingsData = SettingsManager.LoadOrCreate();
            DnSpyHelper.SetPath(SettingsData.DNSpyPath, false);

            SettingsViewer = new SettingsViewer();
            TreeViewer = new ObjectTreeViewer(this, _gameObjectSearcher)
            {
                InspectorOpenCallback = items =>
                {
                    Inspector.InspectorClear();
                    foreach (var stackEntry in items)
                        Inspector.InspectorPush(stackEntry);
                }
            };

            _gizmoDrawer = new GizmoDrawer(this);
            TreeViewer.TreeSelectionChangedCallback = transform => _gizmoDrawer.UpdateState(transform);
            RegisterWindow(TreeViewer);

            if (UnityFeatureHelper.SupportsCursorIndex && UnityFeatureHelper.SupportsXml)
            {
                try
                {
                    Repl = new ReplWindow("RuntimeUnityEditor.Autostart.cs");
                    Repl.RunAutostart();
                    windows.Add(Repl);
                }
                catch (Exception ex)
                {
                    LOGGER.Log(LogLevel.Warning, "Failed to load REPL - " + ex.Message);
                }
            }

            Inspector = new Inspector.Inspector(targetTransform => TreeViewer.SelectAndShowObject(targetTransform), Repl);
            RegisterWindow(Inspector);

            PinnedVariablesData = new PinnedVariablesData();
            PinnedVariablesViewer = new PinnedVariablesViewer(PinnedVariablesData);
            RegisterWindow(PinnedVariablesViewer);
        }

        internal void OnGUI()
        {
            var originalSkin = GUI.skin;
            GUI.skin = InterfaceMaker.CustomSkin;
            ShowCursorIfVisible();

            foreach (var window in windows)
            {
                if (IsInCorrectState(window.RenderOnlyInWindowState))
                    window.RenderWindow();
            }

            GUI.skin = originalSkin;
        }

        public bool Show
        {
            get => _show;
            set
            {
                if (Show != value)
                {
                    if (value)
                    {
                        _previousCursorLockState = Cursor.lockState;
                        _previousCursorVisible = Cursor.visible;
                    }
                    else
                    {
                        if (!_previousCursorVisible || _previousCursorLockState != CursorLockMode.None)
                        {
                            Cursor.lockState = _previousCursorLockState;
                            Cursor.visible = _previousCursorVisible;
                        }
                    }
                }

                if (_gizmoDrawer != null)
                {
                    _gizmoDrawer.Show = value;
                    _gizmoDrawer.UpdateState(TreeViewer.SelectedTransform);
                }

                if (value)
                {
                    RefreshGameObjectSearcher(true);
                }

                _show = value;
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(SHOW_HOT_KEY))
                Show = !Show;

            ShowCursorIfVisible();

            var screenRect = new Rect(
                Window.PADDING,
                Window.PADDING,
                Screen.width - Window.PADDING * 2,
                Screen.height - Window.PADDING * 2
            );

            foreach (var window in windows)
            {
                if (IsInCorrectState(window.RenderOnlyInWindowState))
                    window.UpdateWindowSize(screenRect);

                if (IsInCorrectState(window.UpdateOnlyInWindowState))
                    window.Update();
            }

            if (updateObjectTreeTimer >= UPDATE_OBJECT_TREE_EVERY_X_SECONDS && Show)
            {
                RefreshGameObjectSearcher(false);
                updateObjectTreeTimer = 0f;
            }
            else
                updateObjectTreeTimer += Time.deltaTime;
        }

        private void LateUpdate()
        {
            ShowCursorIfVisible();
        }

        private void RefreshGameObjectSearcher(bool full)
        {
            bool GizmoFilter(GameObject o) => o.name.StartsWith(GizmoDrawer.GizmoObjectName);
            var gizmosExist = _gizmoDrawer != null && _gizmoDrawer.Lines.Count > 0;
            _gameObjectSearcher.Refresh(full, gizmosExist ? GizmoFilter : (Predicate<GameObject>)null);
        }

        private bool IsInCorrectState(WindowState windowState)
        {
            if (windowState == WindowState.ALL || (windowState == WindowState.HIDDEN && Show == false))
                return true;
            
            return windowState == WindowState.VISIBLE && Show == true;
        }

        private void ShowCursorIfVisible()
        {
            if (Show)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        public void RegisterWindow(Window window)
        {
            windows.Add(window);
        }

        public bool UnregisterWindow(Window window)
        {
            return windows.Remove(window);
        }
    }
}
