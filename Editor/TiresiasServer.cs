using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Net;
using System.Threading;

namespace Tiresias
{
    [InitializeOnLoad]
    public static class TiresiasServer
    {
        private static HttpListener _listener;
        private static Thread _listenerThread;
        private static bool _running = false;

        public const int PORT_MIN = 7890;
        public const int PORT_MAX = 7899;

        /// <summary>The actual port we bound to (may differ from 7890 if that was busy).</summary>
        public static int BoundPort { get; private set; } = PORT_MIN;

        /// <summary>The full prefix including the actual bound port.</summary>
        public static string Prefix => $"http://localhost:{BoundPort}/";

        // Legacy compat — kept so existing code referencing PORT still compiles
        public const int PORT = 7890;
        public const string PREFIX = "http://localhost:7890/";

        private static readonly string PortFilePath =
            Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "Tiresias.port");

        static TiresiasServer()
        {
            Start();
            EditorApplication.quitting += Stop;
        }

        public static void Start()
        {
            if (_running) return;

            // Try ports 7890-7899 until one binds
            for (int port = PORT_MIN; port <= PORT_MAX; port++)
            {
                try
                {
                    var prefix = $"http://localhost:{port}/";
                    _listener = new HttpListener();
                    _listener.Prefixes.Add(prefix);
                    _listener.Start();
                    _running = true;
                    BoundPort = port;

                    _listenerThread = new Thread(Listen) { IsBackground = true, Name = "TiresiasListener" };
                    _listenerThread.Start();

                    WritePortFile(port);

                    if (port == PORT_MIN)
                        Debug.Log($"[Tiresias] Listening on {prefix}");
                    else
                        Debug.LogWarning($"[Tiresias] Port {PORT_MIN} was busy — listening on {prefix}");

                    return;
                }
                catch (Exception ex) when (
                    ex is HttpListenerException ||
                    ex is System.Net.Sockets.SocketException ||
                    ex.Message.Contains("socket address"))
                {
                    // Port in use, try next
                    try { _listener?.Close(); } catch { }
                    _listener = null;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Tiresias] Failed to bind port {port}: {ex.Message}");
                    try { _listener?.Close(); } catch { }
                    _listener = null;
                }
            }

            Debug.LogError($"[Tiresias] Could not bind any port in range {PORT_MIN}-{PORT_MAX}");
        }

        public static void Stop()
        {
            if (!_running) return;
            _running = false;

            try
            {
                _listener?.Stop();
                _listener?.Close();
                _listenerThread?.Abort();
            }
            catch { /* ignore abort exceptions */ }

            DeletePortFile();
            Debug.Log("[Tiresias] Stopped.");
        }

        private static void Listen()
        {
            while (_running)
            {
                try
                {
                    var context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => TiresiasRouter.Handle(context));
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_running)
                        Debug.LogError($"[Tiresias] Listener error: {ex.Message}");
                }
            }
        }

        private static void WritePortFile(int port)
        {
            try
            {
                var dir = Path.GetDirectoryName(PortFilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(PortFilePath, port.ToString());
            }
            catch { /* Library/ might not be writable in rare cases */ }
        }

        private static void DeletePortFile()
        {
            try { if (File.Exists(PortFilePath)) File.Delete(PortFilePath); } catch { }
        }

        public static bool IsRunning => _running;
    }
}
