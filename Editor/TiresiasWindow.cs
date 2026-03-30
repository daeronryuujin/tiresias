using UnityEditor;
using UnityEngine;

namespace Tiresias
{
    public class TiresiasWindow : EditorWindow
    {
        [MenuItem("Tools/Tiresias/Open Panel")]
        public static void ShowWindow()
        {
            var win = GetWindow<TiresiasWindow>("Tiresias");
            win.minSize = new Vector2(280, 200);
            win.Show();
        }

        private void OnGUI()
        {
            GUILayout.Space(8);
            GUILayout.Label("Tiresias — Unity Bridge", EditorStyles.boldLabel);
            GUILayout.Space(4);

            bool running = TiresiasServer.IsRunning;
            var statusColor = running ? new Color(0.3f, 0.9f, 0.3f) : new Color(0.9f, 0.3f, 0.3f);

            var prevColor = GUI.color;
            GUI.color = statusColor;
            GUILayout.Label(running
                ? $"● Listening on port {TiresiasServer.BoundPort}"
                : "● Stopped",
                EditorStyles.boldLabel);
            GUI.color = prevColor;

            if (running && TiresiasServer.BoundPort != TiresiasServer.PORT_MIN)
            {
                EditorGUILayout.HelpBox(
                    $"Port {TiresiasServer.PORT_MIN} was busy. Using fallback port {TiresiasServer.BoundPort}.",
                    MessageType.Warning);
            }

            GUILayout.Space(8);

            if (running)
            {
                if (GUILayout.Button("Stop Server")) TiresiasServer.Stop();
            }
            else
            {
                if (GUILayout.Button("Start Server")) TiresiasServer.Start();
            }

            GUILayout.Space(8);

            if (GUILayout.Button("Copy Base URL"))
            {
                var url = TiresiasServer.Prefix;
                EditorGUIUtility.systemCopyBuffer = url;
                Debug.Log($"[Tiresias] Copied: {url}");
            }

            GUILayout.Space(8);
            GUILayout.Label("Read Endpoints", EditorStyles.boldLabel);
            GUILayout.Label("/status  /scene/hierarchy  /scene/object", EditorStyles.miniLabel);
            GUILayout.Label("/assets/scripts  /assets/prefabs  /assets/search", EditorStyles.miniLabel);
            GUILayout.Label("/assets/dependencies  /compiler/status  /compiler/errors", EditorStyles.miniLabel);
            GUILayout.Label("/build/stats  /batch (POST)", EditorStyles.miniLabel);

            GUILayout.Space(4);
            GUILayout.Label("Write Endpoints", EditorStyles.boldLabel);
            GUILayout.Label("/api/scene/objects  /api/scene/{name}/components", EditorStyles.miniLabel);
            GUILayout.Label("/api/scene/{name}/transform  /api/scene/{name}/active", EditorStyles.miniLabel);
            GUILayout.Label("/api/assets/refresh  /api/assets/prefabs/{path}", EditorStyles.miniLabel);
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }
    }
}
