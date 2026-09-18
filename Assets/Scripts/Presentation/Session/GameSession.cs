using System;
using System.Collections.Generic;
using JurassicPark.Simulation;
using UnityEngine;

namespace JurassicPark.Presentation
{
    public enum MatchRole
    {
        None = 0,

        /// <summary>Playing alone. The same path as a host, with nobody connected.</summary>
        Offline = 1,

        /// <summary>Runs the simulation and is also a player.</summary>
        Host = 2,

        /// <summary>Runs no simulation: sends commands and shows snapshots.</summary>
        Client = 3,
    }

    /// <summary>
    /// The scene's owner of the match. As authority (offline or host) it builds the simulation from assets, advances it once per
    /// frame, drains its events once and captures a snapshot. As a client it builds nothing and is fed snapshots. Either way the
    /// screen reads <see cref="Model"/> and sends through <see cref="Commands"/>, and nothing downstream changes simulation state.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameSession : MonoBehaviour
    {
        [SerializeField] private SimulationSettingsAsset settings;
        [SerializeField] private MapDefinitionAsset map;
        [SerializeField] private EntityCatalogAsset catalog;
        [SerializeField] private ScenarioAsset scenario;
        [SerializeField] private MatchRulesAsset matchRules;
        [Tooltip("Start playing alone as soon as the scene loads. Off when a launcher decides between offline, host and join.")]
        [SerializeField] private bool beginOfflineOnAwake = true;

        private readonly List<EntitySnapshot> captured = new List<EntitySnapshot>();
        private readonly List<SeatSnapshot> capturedSeats = new List<SeatSnapshot>();
        private bool seatsDirty;

        public MatchRole Role { get; private set; }

        /// <summary>The simulation. Null on a client.</summary>
        public SimulationRuntime Runtime { get; private set; }

        /// <summary>What the screen reads. Null until a match begins.</summary>
        public MatchReadModel Model { get; private set; }

        /// <summary>Where the local player's orders go. Null until the local seat is known; on a client that is after the host's handshake.</summary>
        public CommandSender Commands { get; private set; }

        /// <summary>Authority only: what each team remembers of things it can no longer see. Shared by the host's screen and every client capture.</summary>
        public SnapshotMemory Memory { get; } = new SnapshotMemory();

        public EntityCatalogAsset CatalogAsset => catalog;
        public SimulationSettingsAsset Settings => settings;

        /// <summary>The match rules the HUD reads for what it cannot see in a snapshot, such as when night falls. Null when the scene runs without them.</summary>
        public MatchRulesAsset MatchRules => matchRules;

        /// <summary>True while there is a match on screen that can take orders.</summary>
        public bool IsReady => Model != null && Failure == null && Commands != null;

        /// <summary>Why the match is not running: the build errors, the fault, or the lost connection. Null while it runs. The overlay puts it on screen.</summary>
        public string Failure { get; private set; }

        /// <summary>Raised when a match begins, in any role, after <see cref="Model"/> exists. Views subscribe to the model here.</summary>
        public event Action MatchBegan;

        /// <summary>Authority only: everything the simulation did this frame, drained once. The network layer routes answers from it.</summary>
        public event Action<IReadOnlyList<SimEvent>> EventsDrained;

        /// <summary>Authority only: a fresh tick was captured this frame. The network layer captures per seat from the runtime.</summary>
        public event Action<long, IReadOnlyList<EntitySnapshot>> SnapshotCaptured;

        /// <summary>Authority only: the match state as captured this frame, for the wire.</summary>
        public event Action<MatchSnapshot> MatchCaptured;

        /// <summary>Authority only: the seat table changed.</summary>
        public event Action<IReadOnlyList<SeatSnapshot>> SeatsChanged;

        /// <summary>The match stopped with a message. A host uses it to let go of its clients instead of leaving them frozen.</summary>
        public event Action<string> Failed;

        /// <summary>An answer to one of the local player's commands, in any role.</summary>
        public event Action<CommandResolved> CommandAnswered;

        public void Configure(SimulationSettingsAsset settingsAsset, MapDefinitionAsset mapAsset, EntityCatalogAsset catalogAsset, ScenarioAsset scenarioAsset, bool beginOffline, MatchRulesAsset matchRulesAsset = null)
        {
            matchRules = matchRulesAsset;
            settings = settingsAsset;
            map = mapAsset;
            catalog = catalogAsset;
            scenario = scenarioAsset;
            beginOfflineOnAwake = beginOffline;
        }

        private void Awake()
        {
            // A fanless MacBook throttles when a loop runs flat out: cap the frame rate, and much lower when nobody is watching.
            QualitySettings.vSyncCount = Application.isBatchMode ? 0 : 1;
            Application.targetFrameRate = Application.isBatchMode ? settings.batchModeFrameRate : settings.targetFrameRate;
            if (beginOfflineOnAwake) BeginOffline();
        }

        public void BeginOffline() => BeginAsAuthority(MatchRole.Offline);
        public void BeginHost() => BeginAsAuthority(MatchRole.Host);

        private void BeginAsAuthority(MatchRole role)
        {
            if (Role != MatchRole.None) throw new InvalidOperationException($"A match already began as {Role}.");
            try
            {
                Runtime = SimulationRuntime.Build(settings, map, catalog, scenario, matchRules);
            }
            catch (Exception exception)
            {
                // One clear message instead of a null-reference flood from every component that wanted the match.
                Fail("The match could not start.\n" + exception.Message);
                return;
            }
            Role = role;
            Model = new MatchReadModel(catalog, Runtime.Map.Definition) { LocalSeat = Runtime.LocalSeat, Fraction = () => Runtime.World.TickFraction };
            Runtime.Seats.TryGet(Runtime.LocalSeat, out Seat local);
            Commands = new CommandSender(Runtime.Router, Runtime.LocalSeat, local.ControllerEpoch);
            seatsDirty = true;
            MatchBegan?.Invoke();
            // Show the starting position at once instead of an empty map until the first tick.
            DrainAndCapture(force: true);
        }

        /// <summary>Begins a match this machine does not simulate. The network layer then feeds <see cref="ApplyRemoteSnapshot"/> and <see cref="AssignRemoteSeat"/>.</summary>
        public void BeginClient(Func<float> snapshotFraction)
        {
            if (Role != MatchRole.None) throw new InvalidOperationException($"A match already began as {Role}.");
            if (!map.TryToDefinition(out MapDefinition definition, out IReadOnlyList<string> problems))
            {
                Fail("The map is not usable:\n- " + string.Join("\n- ", problems));
                return;
            }
            Role = MatchRole.Client;
            Model = new MatchReadModel(catalog, definition) { Fraction = snapshotFraction ?? (() => 1f) };
            MatchBegan?.Invoke();
        }

        /// <summary>Client only: the host said which seat this connection plays, under which epoch, and where its command ids continue.</summary>
        public void AssignRemoteSeat(SeatId seat, int epoch, long nextCommandId, Func<Command, SubmitOutcome> transport)
        {
            Model.LocalSeat = seat;
            Commands = new CommandSender(transport, seat, epoch, nextCommandId);
        }

        public void ApplyRemoteSeats(IReadOnlyList<SeatSnapshot> seats) => Model?.SetSeats(seats);

        public void ApplyRemoteFog(int width, int height, IReadOnlyList<byte> cells, long revision) => Model?.Fog.Set(width, height, cells, revision);

        public void ApplyRemoteMatch(MatchSnapshot match)
        {
            if (Model != null) Model.Match = match;
        }

        /// <summary>Returns true when the snapshot was newer than what is shown and replaced it.</summary>
        public bool ApplyRemoteSnapshot(long tick, IReadOnlyList<EntitySnapshot> entities) => Model != null && Model.Apply(tick, entities);

        /// <summary>An answer to a local command, from the local router or from the wire.</summary>
        public void ObserveAnswer(CommandResolved answer)
        {
            if (Commands == null || answer.Seat != Commands.Seat) return;
            Commands.Observe(answer);
            CommandAnswered?.Invoke(answer);
        }

        /// <summary>Stops the match with a message, for example when the connection to the host is lost.</summary>
        public void Fail(string message)
        {
            if (Failure != null) return;
            Failure = message;
            Debug.LogError("[GameSession] " + message, this);
            Failed?.Invoke(message);
        }

        private void Update()
        {
            if (Runtime == null || Failure != null) return;
            int ticks;
            try
            {
                ticks = Runtime.World.Advance(Time.deltaTime);
            }
            catch (Exception exception)
            {
                // A faulted world refuses to simulate further. Say so once, loudly, rather than looking like a paused game.
                Fail("The simulation faulted at tick " + Runtime.World.Tick + " and the match is over.\n" + exception);
                return;
            }
            DrainAndCapture(force: ticks > 0);
        }

        private void DrainAndCapture(bool force)
        {
            if (Runtime.World.PendingEventCount > 0)
            {
                IReadOnlyList<SimEvent> events = Runtime.World.DrainEvents();
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i] is CommandResolved answer) ObserveAnswer(answer);
                    else if (events[i] is SeatControllerChanged) seatsDirty = true;
                }
                EventsDrained?.Invoke(events);
            }
            if (seatsDirty)
            {
                seatsDirty = false;
                SnapshotCapture.Seats(Runtime, capturedSeats);
                Model.SetSeats(capturedSeats);
                SeatsChanged?.Invoke(capturedSeats);
            }
            if (!force) return;
            SnapshotCapture.Entities(Runtime, catalog, captured, Runtime.LocalSeat, Memory);
            Model.Apply(Runtime.World.Tick, captured);
            int team = Runtime.Knowledge.TeamOf(Runtime.LocalSeat);
            if (Model.Fog.Revision != Runtime.Knowledge.RevisionOf(team) || Model.Fog.Cells.Length == 0)
                Model.Fog.Set(Runtime.Map.Width, Runtime.Map.Height, Runtime.Knowledge.CellsOf(team), Runtime.Knowledge.RevisionOf(team));
            SnapshotCaptured?.Invoke(Runtime.World.Tick, captured);
            // The host's own screen sees its own seat; each client gets its seat's view from the network layer.
            Model.Match = SnapshotCapture.Match(Runtime, Runtime.LocalSeat);
            MatchCaptured?.Invoke(Model.Match);
        }
    }
}
