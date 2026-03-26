using UnityEditor;
using UnityEngine;
using System;
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

        public const int PORT = 7890;
        public const string PREFIX = "http://localhost:7890/";

        static TiresiasServer()
        {
            Start();
            EditorApplication.quitting += Stop;
        }

        public static void Start()
        {
            if (_running) return;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(PREFIX);
                _listener.Start();
                _running = true;

                _listenerThread = new Thread(Listen) { IsBackground = true, Name = "TiresiasListener" };
                _listenerThread.Start();

                Debug.Log($"[Tiresias] Listening on {PREFIX}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Tiresias] Failed to start: {ex.Message}");
            }
        }

        public static void Stop()
        {
            if (!_running) return;
            _running = false;

            try
            {
                _listener?.Stop();
                _listenerThread?.Abort();
            }
            catch { /* ignore abort exceptions */ }

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
                    // Listener was stopped, exit cleanly
                    break;
                }
                catch (Exception ex)
                {
                    if (_running)
                        Debug.LogError($"[Tiresias] Listener error: {ex.Message}");
                }
            }
        }

        public static bool IsRunning => _running;
    }
}
