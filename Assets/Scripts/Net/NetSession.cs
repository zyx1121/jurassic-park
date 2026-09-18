using System;
using System.Collections.Generic;
using JurassicPark.Presentation;
using JurassicPark.Simulation;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace JurassicPark.Net
{
    /// <summary>
    /// Connects a GameSession to other machines. As host it seats each connection, turns its command messages into router
    /// submissions under the seat that connection is bound to, returns each answer to its sender only, and broadcasts the
    /// snapshot the host's own screen uses. As client it runs no simulation: it sends commands and feeds what arrives to the session.
    /// </summary>
    public sealed class NetSession : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField] private NetworkManager network;
        [SerializeField] private UnityTransport transport;
        [Tooltip("Sustained command messages per second one connection may send before the excess is dropped unread.")]
        [SerializeField] private float commandsPerSecond = 40f;
        [Tooltip("Commands a connection may send at once on top of the sustained rate, for a burst of clicks.")]
        [SerializeField] private float commandBurst = 60f;

        private readonly List<EntitySnapshot> receivedEntities = new List<EntitySnapshot>();
        private readonly List<SeatSnapshot> receivedSeats = new List<SeatSnapshot>();
        private readonly List<byte> receivedFog = new List<byte>();
        private readonly List<EntitySnapshot> perSeat = new List<EntitySnapshot>();
        private readonly Dictionary<ulong, long> fogRevisionSent = new Dictionary<ulong, long>();
        private readonly List<ulong> remoteClients = new List<ulong>();
        private SeatBinder binder;
        private HostProtocol host;
        private float lastSnapshotAt;
        private float snapshotInterval = 0.1f;
        private bool running;

        public bool IsRunning => running;
        public int RemoteClientCount => remoteClients.Count;

        /// <summary>Snapshots received as a client, for the launcher's status line and scripted checks.</summary>
        public int SnapshotsReceived { get; private set; }

        public void Configure(GameSession gameSession, NetworkManager manager, UnityTransport utp)
        {
            session = gameSession;
            network = manager;
            transport = utp;
        }

        // ------------------------------------------------------------------ host

        public bool Host(ushort port)
        {
            if (running) return false;
            session.BeginHost();
            if (session.Runtime == null) return false;
            transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");
            network.OnClientConnectedCallback += OnClientConnectedToHost;
            network.OnClientDisconnectCallback += OnClientDisconnectedFromHost;
            if (!network.StartHost())
            {
                session.Fail($"Could not listen on port {port}.");
                return false;
            }
            running = true;
            SimulationRuntime runtime = session.Runtime;
            binder = new SeatBinder(runtime.Seats, runtime.PlayableSeats, runtime.LocalSeat);
            host = new HostProtocol(runtime.Router, binder, SendAnswer, message => Debug.LogWarning("[NetSession] " + message), commandsPerSecond, commandBurst);
            network.CustomMessagingManager.RegisterNamedMessageHandler(NetMessages.Command, OnCommandFromClient);
            session.EventsDrained += host.RouteAnswers;
            // A host whose simulation stopped lets its clients go, so they see a message instead of a frozen screen.
            session.Failed += OnHostFailed;
            session.SnapshotCaptured += BroadcastSnapshot;
            session.SnapshotCaptured += SendMatchToEach;
            session.SeatsChanged += BroadcastSeats;
            return true;
        }

        private void OnClientConnectedToHost(ulong clientId)
        {
            if (clientId == NetworkManager.ServerClientId) return;
            SeatId seat = binder.Bind(clientId);
            if (seat.IsNone)
            {
                using (var full = new FastBufferWriter(1, Unity.Collections.Allocator.Temp))
                    network.CustomMessagingManager.SendNamedMessage(NetMessages.MatchFull, clientId, full, NetworkDelivery.Reliable);
                network.DisconnectClient(clientId, "The match is full.");
                return;
            }
            remoteClients.Add(clientId);
            session.Runtime.Router.TryGetSync(seat, out int epoch, out long nextCommandId);
            using (FastBufferWriter welcome = NetMessages.WriteWelcome(seat, epoch, nextCommandId))
                network.CustomMessagingManager.SendNamedMessage(NetMessages.Welcome, clientId, welcome, NetworkDelivery.Reliable);
            Debug.Log($"[NetSession] client {clientId} plays {seat} under epoch {epoch}");
            // Binding changed a controller; the session notices on its next drain and broadcasts the seat table to everyone.
        }

        private void OnClientDisconnectedFromHost(ulong clientId)
        {
            remoteClients.Remove(clientId);
            fogRevisionSent.Remove(clientId);
            bool hadSeat = binder.TryGetSeat(clientId, out SeatId seat);
            host.OnClientLeft(clientId);
            if (hadSeat) Debug.Log($"[NetSession] client {clientId} left; the computer takes {seat}");
        }

        private void OnCommandFromClient(ulong senderClientId, FastBufferReader reader) =>
            host.OnCommand(senderClientId, ref reader, Time.unscaledTimeAsDouble);

        private void OnHostFailed(string message)
        {
            if (network != null && network.IsListening) network.Shutdown();
        }

        private void SendAnswer(ulong clientId, CommandResolved answer)
        {
            using (FastBufferWriter writer = NetMessages.WriteAnswer(answer))
                // Sequenced: an answer can move the sender's id counter, so two of them must never apply out of order.
                network.CustomMessagingManager.SendNamedMessage(NetMessages.Answer, clientId, writer, NetworkDelivery.ReliableSequenced);
        }

        /// <summary>Each client gets what its seat may see, and its team's fog when that changed. The host's own list is only the host's view and is not reused.</summary>
        private void BroadcastSnapshot(long tick, IReadOnlyList<EntitySnapshot> hostView)
        {
            if (remoteClients.Count == 0) return;
            SimulationRuntime runtime = session.Runtime;
            for (int i = 0; i < remoteClients.Count; i++)
            {
                ulong client = remoteClients[i];
                if (!binder.TryGetSeat(client, out SeatId seat)) continue;
                SnapshotCapture.Entities(runtime, session.CatalogAsset, perSeat, seat, session.Memory);
                // Reliable and fragmented for now: a full snapshot outgrows one datagram as soon as the map fills up. Delta snapshots
                // over an unreliable channel are the follow-up once there is content to measure.
                using (FastBufferWriter writer = NetMessages.WriteSnapshot(tick, perSeat))
                    network.CustomMessagingManager.SendNamedMessage(NetMessages.Snapshot, client, writer, NetworkDelivery.ReliableFragmentedSequenced);
                int team = runtime.Knowledge.TeamOf(seat);
                long revision = runtime.Knowledge.RevisionOf(team);
                if (fogRevisionSent.TryGetValue(client, out long sent) && sent == revision) continue;
                fogRevisionSent[client] = revision;
                using (FastBufferWriter fog = NetMessages.WriteFog(runtime.Map.Width, runtime.Map.Height, runtime.Knowledge.CellsOf(team), revision))
                    network.CustomMessagingManager.SendNamedMessage(NetMessages.Fog, client, fog, NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        /// <summary>Each client gets the match as its own seat sees it: boarded count and outcome are per seat.</summary>
        private void SendMatchToEach(long tick, IReadOnlyList<EntitySnapshot> entities)
        {
            if (session.Runtime.Match == null) return;
            for (int i = 0; i < remoteClients.Count; i++)
            {
                if (!binder.TryGetSeat(remoteClients[i], out SeatId seat)) continue;
                using (FastBufferWriter writer = NetMessages.WriteMatch(SnapshotCapture.Match(session.Runtime, seat)))
                    network.CustomMessagingManager.SendNamedMessage(NetMessages.Match, remoteClients[i], writer, NetworkDelivery.ReliableSequenced);
            }
        }

        private void BroadcastSeats(IReadOnlyList<SeatSnapshot> seats)
        {
            if (remoteClients.Count == 0) return;
            using (FastBufferWriter writer = NetMessages.WriteSeats(seats))
                network.CustomMessagingManager.SendNamedMessage(NetMessages.Seats, remoteClients, writer, NetworkDelivery.ReliableSequenced);
        }

        // ------------------------------------------------------------------ client

        public bool Join(string address, ushort port)
        {
            if (running) return false;
            snapshotInterval = 1f / Mathf.Max(1, session.Settings.ticksPerSecond);
            session.BeginClient(() => (Time.unscaledTime - lastSnapshotAt) / snapshotInterval);
            if (session.Model == null) return false;
            transport.SetConnectionData(address, port);
            network.OnClientDisconnectCallback += OnDisconnectedFromHost;
            if (!network.StartClient())
            {
                session.Fail($"Could not start a connection to {address}:{port}.");
                return false;
            }
            running = true;
            CustomMessagingManager messages = network.CustomMessagingManager;
            messages.RegisterNamedMessageHandler(NetMessages.Welcome, OnWelcome);
            messages.RegisterNamedMessageHandler(NetMessages.Snapshot, OnSnapshot);
            messages.RegisterNamedMessageHandler(NetMessages.Seats, OnSeats);
            messages.RegisterNamedMessageHandler(NetMessages.Answer, OnAnswer);
            messages.RegisterNamedMessageHandler(NetMessages.Match, OnMatch);
            messages.RegisterNamedMessageHandler(NetMessages.Fog, OnFog);
            messages.RegisterNamedMessageHandler(NetMessages.MatchFull, OnMatchFull);
            return true;
        }

        private void OnWelcome(ulong sender, FastBufferReader reader)
        {
            if (!NetMessages.TryReadWelcome(ref reader, out ushort version, out SeatId seat, out int epoch, out long nextCommandId))
            {
                LeaveWith("The host's welcome could not be read.");
                return;
            }
            if (version != NetMessages.ProtocolVersion)
            {
                LeaveWith($"The host speaks protocol {version}, this build speaks {NetMessages.ProtocolVersion}.");
                return;
            }
            session.AssignRemoteSeat(seat, epoch, nextCommandId, SendCommand);
            Debug.Log($"[NetSession] playing {seat} under epoch {epoch}");
        }

        private void OnMatchFull(ulong sender, FastBufferReader reader) => LeaveWith("The match is full.");

        /// <summary>Gives up the connection as well as the match. Staying connected would keep a seat nobody drives and a snapshot stream nobody reads.</summary>
        private void LeaveWith(string message)
        {
            session.Fail(message);
            if (network != null && network.IsListening) network.Shutdown();
        }

        /// <summary>The client's transport for CommandSender. On its way is all a client can know; a drop comes back as an answer.</summary>
        private SubmitOutcome SendCommand(Command command)
        {
            using (FastBufferWriter writer = NetMessages.WriteCommand(command))
                network.CustomMessagingManager.SendNamedMessage(NetMessages.Command, NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
            return SubmitOutcome.Queued;
        }

        private void OnSnapshot(ulong sender, FastBufferReader reader)
        {
            if (!NetMessages.TryReadSnapshot(ref reader, out long tick, receivedEntities)) return;
            SnapshotsReceived++;
            // Only a snapshot that was actually shown restarts the interpolation clock.
            if (session.ApplyRemoteSnapshot(tick, receivedEntities)) lastSnapshotAt = Time.unscaledTime;
        }

        private void OnSeats(ulong sender, FastBufferReader reader)
        {
            if (NetMessages.TryReadSeats(ref reader, receivedSeats)) session.ApplyRemoteSeats(receivedSeats);
        }

        private void OnFog(ulong sender, FastBufferReader reader)
        {
            if (NetMessages.TryReadFog(ref reader, out int width, out int height, receivedFog, out long revision)) session.ApplyRemoteFog(width, height, receivedFog, revision);
            else Debug.LogWarning("[Net] A fog mask from the host could not be read; the screen keeps the previous one.");
        }

        private void OnMatch(ulong sender, FastBufferReader reader)
        {
            if (NetMessages.TryReadMatch(ref reader, out MatchSnapshot match)) session.ApplyRemoteMatch(match);
        }

        private void OnAnswer(ulong sender, FastBufferReader reader)
        {
            if (NetMessages.TryReadAnswer(ref reader, out CommandResolved answer)) session.ObserveAnswer(answer);
        }

        private void OnDisconnectedFromHost(ulong clientId)
        {
            // Netcode's own reason is a transport diagnostic; lead with what it means for the player.
            string detail = string.IsNullOrEmpty(network.DisconnectReason) ? string.Empty : "\n" + network.DisconnectReason;
            session.Fail("Lost the connection to the host." + detail);
        }

        // ------------------------------------------------------------------

        private void OnDestroy()
        {
            if (network == null) return;
            network.OnClientConnectedCallback -= OnClientConnectedToHost;
            network.OnClientDisconnectCallback -= OnClientDisconnectedFromHost;
            network.OnClientDisconnectCallback -= OnDisconnectedFromHost;
            if (session != null)
            {
                if (host != null) session.EventsDrained -= host.RouteAnswers;
                session.Failed -= OnHostFailed;
                session.SnapshotCaptured -= BroadcastSnapshot;
                session.SnapshotCaptured -= SendMatchToEach;
                session.SeatsChanged -= BroadcastSeats;
            }
            if (running && network.IsListening) network.Shutdown();
            // The manager outlives scene loads by Netcode's own choice. It belongs to this match, so it leaves with it; otherwise
            // every reload would stack another manager behind a singleton that points at the first.
            Destroy(network.gameObject);
        }
    }
}
