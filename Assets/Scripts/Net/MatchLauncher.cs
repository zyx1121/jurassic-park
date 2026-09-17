using System;
using System.Collections.Generic;
using JurassicPark.Presentation;
using JurassicPark.Simulation;
using UnityEngine;
using EntityId = JurassicPark.Simulation.EntityId;

namespace JurassicPark.Net
{
    /// <summary>
    /// Decides how this run plays: alone, as host, or joining one. Command-line flags decide for builds and automated checks
    /// (--offline, --host [port], --join address[:port]); without one, a three-button lobby is drawn until a choice is made.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class MatchLauncher : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField] private NetSession net;
        [SerializeField] private ushort defaultPort = 7777;

        private string address = "127.0.0.1";
        private bool demoGather;
        private bool demoSent;
        private float nextStatusAt;

        public void Configure(GameSession gameSession, NetSession netSession)
        {
            session = gameSession;
            net = netSession;
        }

        private void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : null;
                if (args[i] == "--demo-gather") demoGather = true;
                else if (args[i] == "--offline") session.BeginOffline();
                else if (args[i] == "--host") net.Host(ushort.TryParse(next, out ushort port) ? port : defaultPort);
                else if (args[i] == "--join" && next != null)
                {
                    string[] parts = next.Split(':');
                    net.Join(parts[0], parts.Length > 1 && ushort.TryParse(parts[1], out ushort joinPort) ? joinPort : defaultPort);
                }
            }
        }

        private void Update()
        {
            if (Application.isBatchMode && session.Model != null && Time.unscaledTime >= nextStatusAt) LogStatus();
            if (!demoGather || demoSent || !session.IsReady || session.Model.Entities.Count == 0) return;
            // Scripted check for a second process with no mouse: order every own unit onto the first tree, through the same sender a click uses.
            var own = new List<EntityId>();
            EntityId tree = EntityId.None;
            SimVector2 treeAt = default;
            foreach (EntitySnapshot entity in session.Model.Entities)
            {
                if (entity.Kind == EntityKind.Unit && entity.Owner == session.Model.LocalSeat) own.Add(entity.Id);
                else if (tree.IsNone && entity.Kind == EntityKind.ResourceNode && entity.NodeRemaining > 0) { tree = entity.Id; treeAt = entity.Position; }
            }
            if (own.Count == 0 || tree.IsNone) return;
            demoSent = true;
            session.Commands.Send(CommandKind.Gather, own, treeAt, tree);
            Debug.Log($"[MatchLauncher] demo: {own.Count} unit(s) of {session.Model.LocalSeat} ordered to gather {tree}");
        }

        /// <summary>Headless runs have no screen, so they say what the screen would show: enough to check a two-process match from its logs.</summary>
        private void LogStatus()
        {
            nextStatusAt = Time.unscaledTime + 5f;
            MatchReadModel model = session.Model;
            var line = new System.Text.StringBuilder();
            line.Append("[Status] ").Append(session.Role).Append(" seat=").Append(model.LocalSeat.Value).Append(" tick=").Append(model.Tick)
                .Append(" entities=").Append(model.Entities.Count).Append(" remoteClients=").Append(net.RemoteClientCount).Append(" snapshots=").Append(net.SnapshotsReceived);
            foreach (SeatSnapshot seat in model.Seats) line.Append(" seat").Append(seat.Id.Value).Append('=').Append(seat.Controller);
            foreach (EntitySnapshot entity in model.Entities)
            {
                if (entity.Kind == EntityKind.Unit)
                    line.Append(" | unit").Append(entity.Id.Value).Append(" owner=").Append(entity.Owner.Value).Append(" at=").Append(entity.Position)
                        .Append(" pack=").Append(entity.PackTotal).Append(' ').Append(entity.Task).Append('/').Append(entity.TaskState);
                else if (entity.PackCapacity > 0) line.Append(" | store").Append(entity.Id.Value).Append('=').Append(entity.PackTotal);
            }
            if (session.Failure != null) line.Append(" FAILURE: ").Append(session.Failure);
            Debug.Log(line.ToString());
        }

        private void OnGUI()
        {
            if (session.Role != MatchRole.None || session.Failure != null) return;
            GUILayout.BeginArea(new Rect(Screen.width * 0.5f - 150f, Screen.height * 0.5f - 90f, 300f, 180f), GUI.skin.box);
            GUILayout.Label("Jurassic Park");
            if (GUILayout.Button("Play alone")) session.BeginOffline();
            if (GUILayout.Button($"Host on port {defaultPort}")) net.Host(defaultPort);
            GUILayout.BeginHorizontal();
            address = GUILayout.TextField(address, GUILayout.Width(180f));
            if (GUILayout.Button("Join")) net.Join(address, defaultPort);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
