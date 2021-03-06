﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using RuntimeUnityEditor.Core.REPL.MCS;
using RuntimeUnityEditor.Core.UI;
using RuntimeUnityEditor.Core.Utils;
using UnityEngine;

namespace RuntimeUnityEditor.Core.REPL
{
    public sealed class ReplWindow : Window
    {
        private static readonly char[] _inputSplitChars = { ',', ';', '<', '>', '(', ')', '[', ']', '=', '|', '&' };

        private const int HistoryLimit = 50;

        private readonly string _autostartFilename;
        private readonly ScriptEvaluator _evaluator;

        private readonly List<string> _history = new List<string>();
        private int _historyPosition;

        private readonly StringBuilder _sb = new StringBuilder();

        public string InputField { get; set; } = "";
        private string _prevInput = "";
        private Vector2 _scrollPosition = Vector2.zero;

        private readonly int _windowId;
        private TextEditor _textEditor;
        private int _newCursorLocation = -1;

        private HashSet<string> _namespaces;
        private HashSet<string> Namespaces
        {
            get
            {
                if (_namespaces == null)
                {
                    _namespaces = new HashSet<string>(
                        AppDomain.CurrentDomain.GetAssemblies()
                            .SelectMany(Extensions.GetTypesSafe)
                            .Where(x => x.IsPublic && !string.IsNullOrEmpty(x.Namespace))
                            .Select(x => x.Namespace));
                    RuntimeUnityEditorCore.LOGGER.Log(LogLevel.Debug, $"[REPL] Found {_namespaces.Count} public namespaces");
                }
                return _namespaces;
            }
        }

        internal override WindowState RenderOnlyInWindowState => WindowState.VISIBLE;

        internal override WindowState UpdateOnlyInWindowState => WindowState.VISIBLE;

        protected override bool ShouldEatInput => true;

        protected override bool IsWindowDraggable => true;

        protected override string WindowTitle => "C# REPL Console";

        private readonly List<Suggestion> _suggestions = new List<Suggestion>();

        public ReplWindow(string autostartFilename)
        {
            _windowId = GetHashCode();

            _sb.AppendLine("Welcome to C# REPL (read-evaluate-print loop)! Enter \"help\" to get a list of common methods.");

            _evaluator = new ScriptEvaluator(new StringWriter(_sb)) { InteractiveBaseClass = typeof(REPL) };

            var envSetup = new[]
            {
                "using System;",
                "using UnityEngine;",
                "using System.Linq;",
                "using System.Collections;",
                "using System.Collections.Generic;",
                "using RuntimeUnityEditor.Core;",
                "using RuntimeUnityEditor.Core.Inspector.Entries;",
            };

            foreach (var define in envSetup)
                Evaluate(define);

            _autostartFilename = autostartFilename;
        }

        internal void RunAutostart()
        {
            if (File.Exists(_autostartFilename))
            {
                var allLines = File.ReadAllLines(_autostartFilename).Select(x => x.Trim('\t', ' ', '\r', '\n')).Where(x => !string.IsNullOrEmpty(x) && !x.StartsWith("//")).ToArray();
                if (allLines.Length > 0)
                {
                    var message = "Executing code from " + _autostartFilename;
                    RuntimeUnityEditorCore.LOGGER.Log(LogLevel.Info, message);
                    _sb.AppendLine(message);
                    foreach (var line in allLines)
                        Evaluate(line);
                }
            }
        }

        private GUIStyle _completionsListingStyle;
        private bool _refocus;
        private int _refocusCursorIndex = -1;
        private int _refocusSelectIndex;

        private void AcceptSuggestion(string suggestion)
        {
            InputField = InputField.Insert(_textEditor.cursorIndex, suggestion);
            _newCursorLocation = _textEditor.cursorIndex + suggestion.Length;
            ClearSuggestions();
        }

        public object Evaluate(string str)
        {
            object ret = VoidType.Value;
            _evaluator.Compile(str, out var compiled);
            try
            {
                compiled?.Invoke(ref ret);
            }
            catch (Exception e)
            {
                _sb.AppendLine(e.ToString());
            }

            return ret;
        }

        private void FetchHistory(int move)
        {
            _historyPosition += move;
            _historyPosition %= _history.Count;
            if (_historyPosition < 0)
                _historyPosition = _history.Count - 1;

            InputField = _history[_historyPosition];
        }

        private void FetchSuggestions(string input)
        {
            try
            {
                _suggestions.Clear();

                var completions = _evaluator.GetCompletions(input, out string prefix);
                if (completions != null)
                {
                    if (prefix == null)
                        prefix = input;

                    _suggestions.AddRange(completions
                        .Where(x => !string.IsNullOrEmpty(x))
                        .Select(x => new Suggestion(x, prefix, SuggestionKind.Unknown))
                        //.Where(x => !_namespaces.Contains(x.Full))
                        );
                }

                _suggestions.AddRange(GetNamespaceSuggestions(input).OrderBy(x => x.Full));

                _refocus = true;
            }
            catch (Exception ex)
            {
                RuntimeUnityEditorCore.LOGGER.Log(LogLevel.Debug, "[REPL] " + ex);
                ClearSuggestions();
            }
        }

        private IEnumerable<Suggestion> GetNamespaceSuggestions(string input)
        {
            var trimmedInput = input.Trim();
            if (trimmedInput.StartsWith("using"))
                trimmedInput = trimmedInput.Remove(0, 5).Trim();

            return Namespaces.Where(x => x.StartsWith(trimmedInput) && x.Length > trimmedInput.Length)
                .Select(x => new Suggestion(x.Substring(trimmedInput.Length), x.Substring(0, trimmedInput.Length), SuggestionKind.Namespace));
        }

        private void CheckReplInput()
        {
            if (GUI.GetNameOfFocusedControl() != "replInput")
                return;

            _textEditor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
            if (_newCursorLocation >= 0)
            {
                _textEditor.cursorIndex = _newCursorLocation;
                _textEditor.selectIndex = _newCursorLocation;
                _newCursorLocation = -1;
            }

            var currentEvent = Event.current;
            if (currentEvent.isKey)
            {
                if (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter)
                {
                    if (!currentEvent.shift)
                    {
                        // Fix pressing enter adding a newline in textarea
                        if (_textEditor.cursorIndex - 1 >= 0)
                            InputField = InputField.Remove(_textEditor.cursorIndex - 1, 1);

                        AcceptInput();
                        currentEvent.Use();
                    }
                }
                else if (currentEvent.keyCode == KeyCode.UpArrow)
                {
                    FetchHistory(-1);
                    currentEvent.Use();
                    ClearSuggestions();
                }
                else if (currentEvent.keyCode == KeyCode.DownArrow)
                {
                    FetchHistory(1);
                    currentEvent.Use();
                    ClearSuggestions();
                }
            }

            var input = InputField;
            if (!string.IsNullOrEmpty(input))
            {
                try
                {
                    // Separate input into parts, grab only the part with cursor in it
                    var cursorIndex = _refocusCursorIndex >= 0 ? _refocusCursorIndex : _textEditor.cursorIndex;
                    var start = cursorIndex <= 0 ? 0 : input.LastIndexOfAny(_inputSplitChars, cursorIndex - 1) + 1;
                    var end = cursorIndex <= 0 ? input.Length : input.IndexOfAny(_inputSplitChars, cursorIndex - 1);
                    if (end < 0 || end < start) end = input.Length;
                    input = input.Substring(start, end - start);
                }
                catch (ArgumentException) { }

                if (input != _prevInput)
                {
                    if (!string.IsNullOrEmpty(input))
                        FetchSuggestions(input);
                }
            }
            else
            {
                ClearSuggestions();
            }

            _prevInput = input;
        }

        private void ClearSuggestions()
        {
            if (_suggestions.Any())
            {
                _suggestions.Clear();
                _refocus = true;
            }
        }

        public void AcceptInput()
        {
            InputField = InputField.Trim();

            if (InputField == "") return;

            _history.Add(InputField);
            if (_history.Count > HistoryLimit)
                _history.RemoveRange(0, _history.Count - HistoryLimit);
            _historyPosition = 0;

            if (InputField.Contains("geti()"))
            {
                try
                {
                    var val = REPL.geti();
                    if (val != null)
                        InputField = InputField.Replace("geti()", $"geti<{val.GetType().GetSourceCodeRepresentation()}>()");
                }
                catch (SystemException) { }
            }

            _sb.AppendLine($"> {InputField}");
            var result = Evaluate(InputField);
            if (result != null && !Equals(result, VoidType.Value))
                _sb.AppendLine(result.ToString());

            ScrollToBottom();

            InputField = string.Empty;
            ClearSuggestions();
        }

        private void ScrollToBottom()
        {
            _scrollPosition.y = float.MaxValue;
        }

        private class VoidType
        {
            public static readonly VoidType Value = new VoidType();
            private VoidType() { }
        }

        internal void AppendLogLine(string message)
        {
            _sb.AppendLine(message);
        }

        internal override void Update()
        {
        }

        protected override bool PreCreatedWindow()
        {
            if (_completionsListingStyle == null)
            {
                _completionsListingStyle = new GUIStyle(GUI.skin.button)
                {
                    border = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                    padding = new RectOffset(0, 0, 0, 0),
                    hover = { background = Texture2D.whiteTexture, textColor = Color.black },
                    normal = { background = null },
                    focused = { background = Texture2D.whiteTexture, textColor = Color.black },
                    active = { background = Texture2D.whiteTexture, textColor = Color.black }
                };
            }

            return true;
        }

        protected override void PostCreatedWindow()
        {
        }

        protected override void DrawWindowContents()
        {
            GUILayout.BeginVertical();
            {
                _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, false, true, GUIStyle.none, GUI.skin.verticalScrollbar, GUI.skin.textArea);
                {
                    GUILayout.FlexibleSpace();

                    if (_suggestions.Count > 0)
                    {
                        foreach (var suggestion in _suggestions)
                        {
                            _completionsListingStyle.normal.textColor = suggestion.GetTextColor();
                            if (!GUILayout.Button(suggestion.Full, _completionsListingStyle, GUILayout.ExpandWidth(true)))
                                continue;
                            AcceptSuggestion(suggestion.Addition);
                            break;
                        }
                    }
                    else
                    {
                        GUILayout.TextArea(_sb.ToString(), GUI.skin.label);
                    }
                }
                GUILayout.EndScrollView();

                GUILayout.BeginHorizontal();
                {
                    GUI.SetNextControlName("replInput");
                    InputField = GUILayout.TextArea(InputField);

                    if (_refocus)
                    {
                        _refocusCursorIndex = _textEditor.cursorIndex;
                        _refocusSelectIndex = _textEditor.selectIndex;
                        GUI.FocusControl("replInput");
                        _refocus = false;
                    }
                    else if (_refocusCursorIndex >= 0)
                    {
                        _textEditor.cursorIndex = _refocusCursorIndex;
                        _textEditor.selectIndex = _refocusSelectIndex;
                        _refocusCursorIndex = -1;
                    }

                    if (GUILayout.Button("Run", GUILayout.ExpandWidth(false)))
                        AcceptInput();

                    if (GUILayout.Button("History", GUILayout.ExpandWidth(false)))
                    {
                        _sb.AppendLine();
                        _sb.AppendLine("# History of reed commands:");
                        foreach (var h in _history)
                            _sb.AppendLine(h);

                        ScrollToBottom();
                    }

                    if (GUILayout.Button("Autostart", GUILayout.ExpandWidth(false)))
                    {
                        _sb.AppendLine("Opening autostart file at " + _autostartFilename);

                        if (!File.Exists(_autostartFilename))
                            File.WriteAllText(_autostartFilename, "// This C# code will be executed by the REPL near the end of plugin initialization. Only single-line statements are supported. Use echo(string) to write to REPL log and message(string) to write to global log.\n\n");

                        try { Process.Start(_autostartFilename); }
                        catch (Exception e) { _sb.AppendLine(e.Message); }

                        ScrollToBottom();
                    }

                    if (GUILayout.Button("Clear log", GUILayout.ExpandWidth(false)))
                        _sb.Length = 0;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            CheckReplInput();
        }

        internal override Rect GetStartingRect(Rect screenSize, float centerWidth, float centerX)
        {
            float replPadding = 8;
            float inspectorHeight = RuntimeUnityEditorCore.INSTANCE.Inspector.GetStartingRect(screenSize, centerWidth, centerX).height;

            return new Rect(
                centerX,
                screenSize.yMin + inspectorHeight + replPadding,
                centerWidth,
                screenSize.height - inspectorHeight - replPadding
            );
        }
    }
}