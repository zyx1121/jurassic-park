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

        private readonly List<EntitySnapshot> receivedEntities = new List<EntitySnapshot>();
        private readonly List<SeatSnapshot> receivedSeats = new List<SeatSnapshot>();
        private readonly List<ulong> remoteClients = new List<ulong>();
        private SeatBinder binder;
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
            network.CustomMessagingManager.RegisterNamedMessageHandler(NetMessages.Command, OnCommandFromClient);
            session.EventsDrained += RouteAnswers;
            session.SnapshotCaptured += BroadcastSnapshot;
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
            SeatId seat = binder.Unbind(clientId);
            if (!seat.IsNone) Debug.Log($"[NetSession] client {clientId} left; the computer takes {seat}");
        }

        private void OnCommandFromClient(ulong senderClientId, FastBufferReader reader)
        {
            // The seat comes from the binding, never from the payload.
            if (!binder.TryGetSeat(senderClientId, out SeatId seat)) return;
            if (!NetMessages.TryReadCommand(ref reader, seat, out Command command))
            {
                Debug.LogWarning($"[NetSession] dropped an unreadable command from client {senderClientId}");
                return;
            }
            SubmitOutcome outcome = session.Runtime.Router.Submit(command);
            if (outcome == SubmitOutcome.Queued) return;
            // A dropped command raises no event, so the answer is written here. It carries where the sender's ids must continue.
            session.Runtime.Router.TryGetSync(seat, out int epoch, out long nextCommandId);
            SendAnswer(senderClientId, new CommandResolved(seat, command.Epoch, command.CommandId, CommandRejection.DroppedFlood, false, epoch, nextCommandId));
        }

        private void RouteAnswers(IReadOnlyList<SimEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
                if (events[i] is CommandResolved answer && binder.TryGetClient(answer.Seat, out ulong clientId)) SendAnswer(clientId, answer);
        }

        private void SendAnswer(ulong clientId, CommandResolved answer)
        {
            using (FastBufferWriter writer = NetMessages.WriteAnswer(answer))
                network.CustomMessagingManager.SendNamedMessage(NetMessages.Answer, clientId, writer, NetworkDelivery.Reliable);
        }

        private void BroadcastSnapshot(long tick, IReadOnlyList<EntitySnapshot> entities)
        {
            if (remoteClients.Count == 0) return;
            // Reliable and fragmented for now: a full snapshot outgrows one datagram as soon as the map fills up. Delta snapshots
            // over an unreliable channel are the follow-up once there is content to measure.
            using (FastBufferWriter writer = NetMessages.WriteSnapshot(tick, entities))
                network.CustomMessagingManager.SendNamedMessage(NetMessages.Snapshot, remoteClients, writer, NetworkDelivery.ReliableFragmentedSequenced);
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
            messages.RegisterNamedMessageHandler(NetMessages.MatchFull, (_, __) => session.Fail("The match is full."));
            return true;
        }

        private void OnWelcome(ulong sender, FastBufferReader reader)
        {
            if (!NetMessages.TryReadWelcome(ref reader, out ushort version, out SeatId seat, out int epoch, out long nextCommandId))
            {
                session.Fail("The host's welcome could not be read.");
                return;
            }
            if (version != NetMessages.ProtocolVersion)
            {
                session.Fail($"The host speaks protocol {version}, this build speaks {NetMessages.ProtocolVersion}.");
                return;
            }
            session.AssignRemoteSeat(seat, epoch, nextCommandId, SendCommand);
            Debug.Log($"[NetSession] playing {seat} under epoch {epoch}");
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
            lastSnapshotAt = Time.unscaledTime;
            session.ApplyRemoteSnapshot(tick, receivedEntities);
        }

        private void OnSeats(ulong sender, FastBufferReader reader)
        {
            if (NetMessages.TryReadSeats(ref reader, receivedSeats)) session.ApplyRemoteSeats(receivedSeats);
        }

        private void OnAnswer(ulong sender, FastBufferReader reader)
        {
            if (NetMessages.TryReadAnswer(ref reader, out CommandResolved answer)) session.ObserveAnswer(answer);
        }

        private void OnDisconnectedFromHost(ulong clientId)
        {
            string reason = string.IsNullOrEmpty(network.DisconnectReason) ? "Lost the connection to the host." : network.DisconnectReason;
            session.Fail(reason);
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
                session.EventsDrained -= RouteAnswers;
                session.SnapshotCaptured -= BroadcastSnapshot;
                session.SeatsChanged -= BroadcastSeats;
            }
            if (running && network.IsListening) network.Shutdown();
        }
    }
}
