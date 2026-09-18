using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// The shape of one match, as the original plays it: a short window to choose length and difficulty, survival under the
    /// spawn timers while the countdown runs, then the helicopter lands for a fixed window and whoever boards has escaped.
    /// Every number comes from <see cref="MatchRules"/>; this only turns time into phases, spawns and outcomes.
    /// </summary>
    public sealed class MatchFlow : ISimSystem
    {
        private sealed class TimerState
        {
            public SpawnTimerRule Rule;
            public double NextAt;
        }

        private readonly World world;
        private readonly MatchRules rules;
        private readonly SeatRegistry seats;
        private readonly GridMap map;
        private readonly DefinitionCatalog catalog;
        private readonly EntitySpawner spawner;
        private readonly List<TimerState> timers = new List<TimerState>();
        private readonly Dictionary<SeatId, int> boardedBySeat = new Dictionary<SeatId, int>();
        private readonly Dictionary<SeatId, SeatOutcome> outcomes = new Dictionary<SeatId, SeatOutcome>();
        private readonly List<CellBounds> spawnRegions = new List<CellBounds>();
        private readonly List<CellBounds> evacuationRegions = new List<CellBounds>();
        private double phaseStartedAt;
        private bool optionsChosen;

        public MatchPhase Phase { get; private set; } = MatchPhase.Setup;
        public int ModeIndex { get; private set; }
        public int Difficulty { get; private set; }
        public Clock Clock { get; }
        public EntityId Helicopter { get; private set; }
        public MatchRules Rules => rules;

        public MatchFlow(World world, MatchRules rules, SeatRegistry seats, GridMap map, DefinitionCatalog catalog, EntitySpawner spawner)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.seats = seats ?? throw new ArgumentNullException(nameof(seats));
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.spawner = spawner ?? throw new ArgumentNullException(nameof(spawner));
            ModeIndex = rules.DefaultModeIndex;
            Difficulty = rules.DefaultDifficulty;
            Clock = new Clock(rules);
            for (int i = 0; i < map.Definition.Regions.Count; i++)
            {
                RegionDefinition region = map.Definition.Regions[i];
                if (region.Kind == RegionKind.DinosaurSpawn) spawnRegions.Add(region.Bounds);
                else if (region.Kind == RegionKind.Evacuation) evacuationRegions.Add(region.Bounds);
            }
        }

        /// <summary>Seconds left in the current phase's countdown: the selection window, the survival countdown, or the helicopter's stay. Zero once ended.</summary>
        public double SecondsLeft
        {
            get
            {
                double elapsed = world.Time - phaseStartedAt;
                switch (Phase)
                {
                    case MatchPhase.Setup: return Math.Max(0, rules.SelectionWindowSeconds - elapsed);
                    case MatchPhase.Survival: return Math.Max(0, rules.Modes[ModeIndex].SurvivalSeconds - elapsed);
                    case MatchPhase.Evacuation: return Math.Max(0, rules.HelicopterWindowSeconds - elapsed);
                    default: return 0;
                }
            }
        }

        public int BoardedOf(SeatId seat) => boardedBySeat.TryGetValue(seat, out int count) ? count : 0;
        public SeatOutcome OutcomeOf(SeatId seat) => outcomes.TryGetValue(seat, out SeatOutcome outcome) ? outcome : SeatOutcome.Undecided;

        /// <summary>A playable seat chooses length and difficulty during the selection window. First choice wins, as the original gives the first player the say.</summary>
        public bool TryChoose(int modeIndex, int difficulty, SeatId by)
        {
            if (Phase != MatchPhase.Setup || optionsChosen) return false;
            if (modeIndex < 0 || modeIndex >= rules.Modes.Count || difficulty < 1 || difficulty > rules.DifficultyCount) return false;
            ModeIndex = modeIndex;
            Difficulty = difficulty;
            optionsChosen = true;
            world.Raise(new MatchOptionsChosen(modeIndex, difficulty, by));
            BeginSurvival();
            return true;
        }

        /// <summary>A survivor standing beside the helicopter climbs in: it leaves the world and counts for its seat.</summary>
        public bool TryBoard(Entity survivor)
        {
            if (Phase != MatchPhase.Evacuation || survivor == null || !survivor.IsAlive || survivor.Kind != EntityKind.Unit || Helicopter.IsNone) return false;
            if (!world.TryGet(Helicopter, out Entity helicopter) || !helicopter.IsAlive) return false;
            boardedBySeat[survivor.Owner] = BoardedOf(survivor.Owner) + 1;
            world.Despawn(survivor.Id, "boarded");
            world.Raise(new Boarded(survivor.Id, survivor.Owner));
            return true;
        }

        public void Tick(World tickedWorld)
        {
            float dt = (float)world.Config.TickSeconds;
            switch (Phase)
            {
                case MatchPhase.Setup:
                    if (world.Time - phaseStartedAt >= rules.SelectionWindowSeconds) BeginSurvival();
                    break;
                case MatchPhase.Survival:
                    Clock.Advance(dt);
                    RunTimers();
                    DecideEliminations();
                    // A wipe is the end of the match, not the start of a long wait for a helicopter nobody will meet.
                    if (EveryoneDecided()) End();
                    else if (SecondsLeft <= 0) BeginEvacuation();
                    break;
                case MatchPhase.Evacuation:
                    RunTimers();
                    DecideEliminations();
                    if (SecondsLeft <= 0 || EveryoneDecided()) End();
                    break;
            }
        }

        private void BeginSurvival()
        {
            Phase = MatchPhase.Survival;
            phaseStartedAt = world.Time;
            timers.Clear();
            for (int i = 0; i < rules.SpawnTimers.Count; i++)
            {
                SpawnTimerRule rule = rules.SpawnTimers[i];
                if (!rule.RunsAt(Difficulty)) continue;
                timers.Add(new TimerState { Rule = rule, NextAt = phaseStartedAt + rule.EnabledAtSeconds + world.Random.RangeInclusive(rule.PeriodMinSeconds, rule.PeriodMaxSeconds) });
            }
            world.Raise(new MatchPhaseChanged(Phase));
        }

        private void RunTimers()
        {
            for (int i = 0; i < timers.Count; i++)
            {
                TimerState timer = timers[i];
                if (world.Time < timer.NextAt) continue;
                timer.NextAt = world.Time + world.Random.RangeInclusive(timer.Rule.PeriodMinSeconds, timer.Rule.PeriodMaxSeconds);
                Fire(timer.Rule);
            }
        }

        private void Fire(SpawnTimerRule rule)
        {
            IReadOnlyList<KeyValuePair<string, int>> group = rule.Batch.Alternatives[rule.Batch.Pick(world.Random.Range(0, rule.Batch.TotalWeight))];
            int spawned = 0, wanted = 0;
            for (int g = 0; g < group.Count; g++)
            {
                if (!catalog.TryGet(group[g].Key, out EntityDefinition definition)) continue;
                for (int n = 0; n < group[g].Value; n++)
                {
                    wanted++;
                    Cell? cell = RandomSpawnCell(definition);
                    if (cell == null) continue;
                    if (spawner.Spawn(definition.Id, rules.DinosaurSeat, cell.Value, out _) != null) spawned++;
                }
            }
            if (spawned > 0) world.Raise(new DinosaursSpawned(rule.Id, spawned));
            if (spawned < wanted) world.Raise(new SpawnSkipped(rule.Id));
        }

        private Cell? RandomSpawnCell(EntityDefinition definition)
        {
            // The original picks anywhere on the playable map; ours has spawn regions, and falls back to the whole map without them.
            if (spawnRegions.Count == 0) return spawner.RandomCellFor(definition, WholeMap);
            // Try the rolled region first, then the others: a region that is built over does not stop the spawn.
            int first = world.Random.Range(0, spawnRegions.Count);
            for (int i = 0; i < spawnRegions.Count; i++)
            {
                Cell? cell = spawner.RandomCellFor(definition, spawnRegions[(first + i) % spawnRegions.Count]);
                if (cell != null) return cell;
            }
            return null;
        }

        private CellBounds WholeMap => new CellBounds(0, 0, map.Width - 1, map.Height - 1);

        private void BeginEvacuation()
        {
            Phase = MatchPhase.Evacuation;
            phaseStartedAt = world.Time;
            Clock.Freeze(rules.FreezeTimeOfDayAtEvacuation);
            if (catalog.TryGet(rules.HelicopterDefinitionId, out EntityDefinition helicopter))
            {
                // A rolled candidate region first, then the other candidates, then anywhere on the map: the helicopter lands
                // wherever it still can. Only a map with no room at all leaves it in the air, and that is said out loud.
                Cell? cell = null;
                if (evacuationRegions.Count > 0)
                {
                    int first = world.Random.Range(0, evacuationRegions.Count);
                    for (int i = 0; i < evacuationRegions.Count && cell == null; i++)
                        cell = spawner.RandomCellFor(helicopter, evacuationRegions[(first + i) % evacuationRegions.Count]);
                }
                if (cell == null) cell = spawner.RandomCellFor(helicopter, WholeMap);
                Entity landed = cell != null ? spawner.Spawn(helicopter.Id, SeatId.None, cell.Value, out _) : null;
                if (landed != null)
                {
                    Helicopter = landed.Id;
                    world.Raise(new HelicopterLanded(landed.Id));
                }
                else world.Raise(new HelicopterCouldNotLand());
            }
            world.Raise(new MatchPhaseChanged(Phase));
        }

        /// <summary>A seat with nobody left alive and nobody boarded has lost, whatever the clock says.</summary>
        private void DecideEliminations()
        {
            for (int i = 0; i < seats.Seats.Count; i++)
            {
                Seat seat = seats.Seats[i];
                if (seat.Id == rules.DinosaurSeat || OutcomeOf(seat.Id) != SeatOutcome.Undecided) continue;
                if (BoardedOf(seat.Id) == 0 && !HasLivingUnit(seat.Id)) Decide(seat.Id, SeatOutcome.Lost);
                else if (Phase == MatchPhase.Evacuation && BoardedOf(seat.Id) > 0 && !HasLivingUnit(seat.Id)) Decide(seat.Id, SeatOutcome.Won);
            }
        }

        private bool HasLivingUnit(SeatId seat)
        {
            IReadOnlyList<Entity> entities = world.Entities;
            for (int i = 0; i < entities.Count; i++)
                if (entities[i].IsAlive && entities[i].Kind == EntityKind.Unit && entities[i].Owner == seat) return true;
            return false;
        }

        private bool EveryoneDecided()
        {
            for (int i = 0; i < seats.Seats.Count; i++)
                if (seats.Seats[i].Id != rules.DinosaurSeat && OutcomeOf(seats.Seats[i].Id) == SeatOutcome.Undecided) return false;
            return true;
        }

        private void End()
        {
            // The helicopter leaves: whoever is aboard has escaped, whoever is not has not.
            for (int i = 0; i < seats.Seats.Count; i++)
            {
                Seat seat = seats.Seats[i];
                if (seat.Id == rules.DinosaurSeat || OutcomeOf(seat.Id) != SeatOutcome.Undecided) continue;
                Decide(seat.Id, BoardedOf(seat.Id) > 0 ? SeatOutcome.Won : SeatOutcome.Lost);
            }
            Phase = MatchPhase.Ended;
            phaseStartedAt = world.Time;
            world.Raise(new MatchPhaseChanged(Phase));
        }

        private void Decide(SeatId seat, SeatOutcome outcome)
        {
            outcomes[seat] = outcome;
            world.Raise(new SeatOutcomeDecided(seat, outcome));
        }
    }
}
