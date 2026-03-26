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
            win.minSize = new Vector2(260, 140);
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
            GUILayout.Label(running ? $"● Listening on :{TiresiasServer.PORT}" : "● Stopped", EditorStyles.boldLabel);
            GUI.color = prevColor;

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
                EditorGUIUtility.systemCopyBuffer = TiresiasServer.PREFIX;
                Debug.Log($"[Tiresias] Copied: {TiresiasServer.PREFIX}");
            }

            GUILayout.Space(4);
            GUILayout.Label("Endpoints: /status  /scene/hierarchy  /scene/object", EditorStyles.miniLabel);
            GUILayout.Label("/assets/scripts  /assets/prefabs  /compiler/errors", EditorStyles.miniLabel);
        }

        private void OnInspectorUpdate()
        {
            Repaint(); // Keep status indicator live
        }
    }
}
