using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dev.ExternalTools.Editor
{
    /// <summary>
    /// 外部工具管理窗口：清单版本 vs 已装版本一览、强制重装、创建清单模板。
    /// </summary>
    public class ToolManagerWindow : EditorWindow
    {
        const string ManifestTemplate = @"{
  ""registry"": ""https://your-server.com/tools"",
  ""pypiIndex"": ""https://pypi.yourcompany.com/simple"",
  ""tools"": [
    {
      ""name"": ""ffmpeg"",
      ""type"": ""binary"",
      ""version"": ""7.1"",
      ""executable"": ""ffmpeg"",
      ""platforms"": {
        ""win-x64"":   { ""file"": ""ffmpeg-7.1-win-x64.zip"" },
        ""osx-arm64"": { ""file"": ""ffmpeg-7.1-osx-arm64.zip"" }
      }
    },
    {
      ""name"": ""asset-packer"",
      ""type"": ""pypi"",
      ""version"": ""1.4.0"",
      ""package"": ""asset-packer"",
      ""entryPoint"": ""asset-packer"",
      ""python"": ""3.12""
    }
  ]
}";

        Vector2 _scroll;

        [MenuItem("Tools/External Tools/Manager")]
        public static void Open() => GetWindow<ToolManagerWindow>("External Tools");

        void OnGUI()
        {
            DrawToolbar();

            var manifest = ToolManager.LoadManifest();
            if (manifest == null)
            {
                EditorGUILayout.HelpBox(
                    "未找到工具清单。点击上方 \"Create Manifest\" 创建模板，并按项目需求修改。",
                    MessageType.Info);
                return;
            }

            var state = ToolManager.LoadState();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var tool in manifest.Tools)
                DrawToolRow(tool, state);
            EditorGUILayout.EndScrollView();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Sync All", EditorStyles.toolbarButton, GUILayout.Width(70)))
                _ = ToolManager.Sync(force: true);

            if (GUILayout.Button("Create Manifest", EditorStyles.toolbarButton, GUILayout.Width(110)))
                CreateManifest();

            if (GUILayout.Button("Open Tools Folder", EditorStyles.toolbarButton, GUILayout.Width(120)))
                OpenFolder(ToolManager.ToolsRoot);

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        void DrawToolRow(ToolEntry tool, Dictionary<string, string> state)
        {
            state.TryGetValue(tool.Name, out string installed);
            bool upToDate = installed == tool.Version;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();

            var style = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
            GUILayout.Label(tool.Name, style, GUILayout.Width(140));
            GUILayout.Label(tool.IsPypi ? "pypi" : "binary", GUILayout.Width(50));
            GUILayout.Label($"清单: {tool.Version}", GUILayout.Width(110));
            GUILayout.Label($"已装: {installed ?? "-"}", GUILayout.Width(110));

            GUILayout.Label(upToDate ? "✔ 最新" : "● 待同步",
                upToDate ? Styles.Ok : Styles.Warn, GUILayout.Width(70));

            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(upToDate))
            {
                if (GUILayout.Button("同步", GUILayout.Width(50)))
                    _ = SyncOne(tool);
            }
            if (GUILayout.Button("重装", GUILayout.Width(50)))
                _ = SyncOne(tool, force: true);

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        static async System.Threading.Tasks.Task SyncOne(ToolEntry tool, bool force = false)
        {
            var state = ToolManager.LoadState();
            EditorUtility.DisplayProgressBar("同步外部工具", tool.Name, -1f);
            try
            {
                // 通过整体 Sync 触发（清单内单个工具版本不一致时自然只装它）
                if (force) state.Remove(tool.Name);
                ToolManager.SaveState(state);
                await ToolManager.Sync(force: false);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static void CreateManifest()
        {
            string dir = Path.GetDirectoryName(ToolManager.ManifestPath);
            Directory.CreateDirectory(dir);
            if (!File.Exists(ToolManager.ManifestPath))
                File.WriteAllText(ToolManager.ManifestPath, ManifestTemplate);
            AssetDatabase.Refresh();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<TextAsset>(ToolManager.ManifestRelativePath);
        }

        static void OpenFolder(string path)
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        static class Styles
        {
            public static readonly GUIStyle Ok = new GUIStyle(EditorStyles.label)
                { normal = { textColor = new Color(0.3f, 0.75f, 0.3f) } };
            public static readonly GUIStyle Warn = new GUIStyle(EditorStyles.label)
                { normal = { textColor = new Color(0.95f, 0.65f, 0.2f) } };
        }
    }
}
