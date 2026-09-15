using System;
using JurassicPark.Core;
using JurassicPark.Player;
using JurassicPark.Scene;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace JurassicPark.Net
{
    /// <summary>
    /// Starts the game as host, client or offline. Direct UnityTransport (LAN / localhost) for now;
    /// Relay join codes need a Unity Cloud project (see the follow-up issue). Command line:
    /// --host [port], --join ip[:port], --offline, --autowalk (test clients keep walking); with no
    /// arguments the debug lobby GUI waits for a choice.
    /// </summary>
    public sealed class NetLobby : MonoBehaviour
    {
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private ushort defaultPort = 7777;
        [SerializeField] private bool showDebugGui = true;

        public string Status { get; private set; } = "offline";
        public bool Started { get; private set; }

        private NetworkManager nm;
        private string joinAddress = "127.0.0.1";
        private float fpsAccum;
        private int fpsFrames;
        private float fps;
        private float nextFpsLog;

        private void Awake()
        {
            // Headless Editors and test clients do not need to spin at full speed
            if (Application.isBatchMode) Application.targetFrameRate = 20;
            nm = GetComponent<NetworkManager>();
            if (nm != null)
            {
                nm.OnClientConnectedCallback += id => Status = $"{(nm.IsHost ? "host" : "client")} connected={nm.ConnectedClientsIds.Count}";
                nm.OnClientDisconnectCallback += id => Status = $"disconnected {id}";
            }
        }

        private void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--host")
                {
                    ushort port = i + 1 < args.Length && ushort.TryParse(args[i + 1], out ushort p) ? p : defaultPort;
                    Host(port);
                    return;
                }

                if (args[i] == "--join" && i + 1 < args.Length)
                {
                    Join(args[i + 1]);
                    return;
                }

                if (args[i] == "--offline")
                {
                    StartOffline();
                    return;
                }
            }

            // No arguments: wait for the lobby GUI (Host / Join / Play offline)
            Status = "lobby";
        }

        /// <summary>No network: spawn the local player straight away like a single-player game.</summary>
        public void StartOffline()
        {
            if (Started) return;
            Started = true;
            Status = "offline";
            Transform spawn = FindSpawn();
            GameObject player = Instantiate(playerPrefab, spawn != null ? spawn.position + Vector3.up * 0.1f : Vector3.zero, Quaternion.identity);
            player.name = "Player (offline)";
            FollowCamera cam = FindFirstObjectByType<FollowCamera>();
            if (cam != null) cam.Target = player.transform;
        }

        public bool Host(ushort port = 0)
        {
            if (Started || nm == null) return false;
            var utp = nm.GetComponent<UnityTransport>();
            utp.SetConnectionData("0.0.0.0", port == 0 ? defaultPort : port, "0.0.0.0");
            Started = nm.StartHost();
            Status = Started ? $"host on {utp.ConnectionData.Port}" : "host failed";
            return Started;
        }

        public bool Join(string address)
        {
            if (Started || nm == null) return false;
            string[] parts = address.Split(':');
            ushort port = parts.Length > 1 && ushort.TryParse(parts[1], out ushort p) ? p : defaultPort;
            var utp = nm.GetComponent<UnityTransport>();
            utp.SetConnectionData(parts[0], port);
            Started = nm.StartClient();
            Status = Started ? $"joining {parts[0]}:{port}" : "join failed";
            return Started;
        }

        public static Transform FindSpawn()
        {
            GameObject spawn = GameObject.Find("PlayerSpawn");
            return spawn != null ? spawn.transform : null;
        }

        private void Update()
        {
            fpsAccum += Time.unscaledDeltaTime;
            fpsFrames++;
            if (fpsAccum >= 0.5f)
            {
                fps = fpsFrames / fpsAccum;
                fpsAccum = 0f;
                fpsFrames = 0;
            }

            if (!Application.isEditor && Time.unscaledTime >= nextFpsLog)
            {
                nextFpsLog = Time.unscaledTime + 10f;
                Debug.Log($"[FPS] {fps:F0} at {Screen.width}x{Screen.height}, billboards={BillboardManager.Count}");
            }
        }

        private void OnGUI()
        {
            if (!showDebugGui) return;
            GUILayout.BeginArea(new Rect(10, 10, 260, 120), GUI.skin.box);
            GUILayout.Label($"Net: {Status}   {fps:F0} fps");
            if (!Started)
            {
                if (GUILayout.Button("Host")) Host();
                GUILayout.BeginHorizontal();
                joinAddress = GUILayout.TextField(joinAddress, GUILayout.Width(140));
                if (GUILayout.Button("Join")) Join(joinAddress);
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Play offline")) StartOffline();
            }

            GUILayout.EndArea();
        }
    }
}
