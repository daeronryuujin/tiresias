using UnityEditor;
using UnityEngine;
using System.IO;

namespace Tiresias
{
    [InitializeOnLoad]
    public static class TiresiasInstaller
    {
        private const string CLAUDE_MD_DEST_KEY = "Tiresias_ClaudeMdInstalled";

        static TiresiasInstaller()
        {
            // Only run once per project (not every domain reload)
            if (SessionState.GetBool(CLAUDE_MD_DEST_KEY, false)) return;
            SessionState.SetBool(CLAUDE_MD_DEST_KEY, true);

            CopyClaudeMdIfMissing();
        }

        private static void CopyClaudeMdIfMissing()
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var dest = Path.Combine(projectRoot, "CLAUDE.md");

            if (File.Exists(dest)) return; // Already there, don't overwrite

            var source = Path.GetFullPath("Packages/com.daeronryuujin.tiresias/CLAUDE.md");

            if (!File.Exists(source))
            {
                Debug.LogWarning("[Tiresias] Could not find CLAUDE.md in package — skipping auto-install.");
                return;
            }

            File.Copy(source, dest);
            Debug.Log($"[Tiresias] CLAUDE.md installed to project root: {dest}");
        }

        [MenuItem("Tools/Tiresias/Reinstall CLAUDE.md")]
        public static void ForceReinstall()
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var dest = Path.Combine(projectRoot, "CLAUDE.md");
            var source = Path.GetFullPath("Packages/com.daeronryuujin.tiresias/CLAUDE.md");

            if (!File.Exists(source))
            {
                Debug.LogError("[Tiresias] Source CLAUDE.md not found in package.");
                return;
            }

            File.Copy(source, dest, overwrite: true);
            Debug.Log($"[Tiresias] CLAUDE.md reinstalled to {dest}");
        }
    }
}
