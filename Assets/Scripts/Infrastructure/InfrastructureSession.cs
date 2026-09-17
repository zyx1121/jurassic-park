using System;
using System.Collections.Generic;
using JurassicPark.Core;
using UnityEngine;

namespace JurassicPark.Infrastructure
{
    public enum InfrastructureScenarioPhase { Idle, Hauling, Building, Breaching, Entering, Passed, Failed }

    public sealed class InfrastructureSession : MonoBehaviour
    {
        [SerializeField] private InfrastructureConfig config;
        private readonly Dictionary<int, InfrastructureEntityView> views = new Dictionary<int, InfrastructureEntityView>();
        private readonly List<int> selection = new List<int>();
        private long nextCommand;
        private float accumulator;
        private float scenarioStarted;
        private int scenarioWall;
        private int scenarioDinosaur;
        private System.Random eventRandom;

        public InfrastructureConfig Config => config;
        public InfrastructureLayout Layout { get; private set; }
        public InfraWorld World { get; private set; }
        public IReadOnlyList<int> Selection => selection;
        public int SelectedId => selection.Count > 0 ? selection[0] : 0;
        public int WorkerId { get; private set; }
        public int SecondWorkerId { get; private set; }
        public int MainSourceId { get; private set; }
        public int MainDepotId { get; private set; }
        public string Feedback { get; private set; } = "Loading fixed map...";
        public InfrastructureScenarioPhase ScenarioPhase { get; private set; }
        public string ScenarioEvidence { get; private set; } = "Not run";
        public bool Paused { get; set; }
        public bool PlacingWall { get; set; }
        public int VisibleEntityCount => views.Count;
        public event Action WorldReset;

        public void Configure(InfrastructureConfig value) => config = value;

        private void Start() => ResetWorld();

        public void ResetWorld()
        {
            if (config == null || config.map == null || config.tickSeconds <= 0f || config.sourceStock <= 0)
                throw new InvalidOperationException("Infrastructure scene requires valid persistent map and simulation settings.");
            if (Authority.IsNetworked)
                throw new InvalidOperationException("Infrastructure is an offline slice; do not load it alongside a network lobby.");
            if (!Authority.IsAuthority)
                throw new InvalidOperationException("Only the simulation authority may initialize this scene.");

            foreach (InfrastructureEntityView view in views.Values) Destroy(view.gameObject);
            views.Clear();
            selection.Clear();
            Layout = config.map.CreateLayout();
            World = new InfraWorld(Layout.Map, config.rules);
            nextCommand = 0;
            accumulator = 0f;
            eventRandom = new System.Random(config.eventSeed);
            for (int i = 0; i < config.map.camps.Length; i++)
            {
                InfrastructureMapDefinition.Camp camp = config.map.camps[i];
                InfraEntity depot = World.AddEntity(InfraEntityKind.Depot, camp.Depot);
                InfraEntity source = World.AddEntity(InfraEntityKind.Source, camp.Source, 0, config.sourceStock);
                if (i == config.map.demonstrationCamp)
                {
                    MainDepotId = depot.Id;
                    MainSourceId = source.Id;
                }
            }
            WorkerId = World.AddEntity(InfraEntityKind.Worker, config.map.arrival).Id;
            SecondWorkerId = World.AddEntity(InfraEntityKind.Worker, config.map.arrival + Vector2Int.right).Id;
            Select(WorkerId, false);
            Paused = false;
            PlacingWall = false;
            ScenarioPhase = InfrastructureScenarioPhase.Idle;
            ScenarioEvidence = "Not run";
            Feedback = "Ready. Select a survivor; right-click supplies to haul, or ground to move.";
            RefreshViews();
            WorldReset?.Invoke();
        }

        private void Update()
        {
            if (World == null || Paused || !Authority.IsAuthority) return;
            accumulator += Mathf.Min(Time.deltaTime, .25f);
            while (accumulator >= config.tickSeconds)
            {
                Advance(config.tickSeconds);
                accumulator -= config.tickSeconds;
            }
            RefreshViews();
        }

        public void Advance(float seconds)
        {
            if (World == null || !Authority.IsAuthority)
                throw new InvalidOperationException("Cannot advance an uninitialized or non-authoritative world.");
            World.Tick(seconds);
            if (World.TotalPhysicalMaterials + World.ConsumedMaterials != World.InitialMaterials)
                FailScenario($"Material invariant violated: physical={World.TotalPhysicalMaterials}, consumed={World.ConsumedMaterials}, initial={World.InitialMaterials}.");
            AdvanceScenario();
        }

        public void Select(int id, bool additive)
        {
            if (!additive) selection.Clear();
            if (World != null && World.Entities.ContainsKey(id) && !selection.Contains(id)) selection.Add(id);
        }

        public void SelectWorkers(IEnumerable<int> ids)
        {
            selection.Clear();
            foreach (int id in ids)
                if (World.Entities.TryGetValue(id, out InfraEntity entity) &&
                    entity.Kind == InfraEntityKind.Worker && entity.Owner == 0 && entity.Health > 0f)
                    selection.Add(id);
        }

        public int Pick(Vector2 position)
        {
            int nearest = 0;
            float distance = Layout.Map.CellSize * .75f;
            foreach (InfraEntity entity in World.Entities.Values)
            {
                if (entity.Health <= 0f) continue;
                float candidate = Vector2.Distance(entity.Position, position);
                if (candidate < distance) { nearest = entity.Id; distance = candidate; }
            }
            return nearest;
        }

        public InfraCommandResult Issue(int actor, InfraCommandKind kind, Vector2Int cell, int target = 0, int owner = 0)
        {
            if (!Authority.IsAuthority)
                throw new InvalidOperationException("Only the simulation authority accepts commands.");
            InfraCommandResult result = World.Submit(new InfraCommand(++nextCommand, owner, actor, kind, cell, target));
            Feedback = result.Accepted ? $"{kind} accepted for #{actor}." : $"{kind} rejected: {result.Reason}";
            return result;
        }

        public void OrderSelection(InfraCommandKind kind, Vector2Int cell, int target = 0)
        {
            bool commanded = false;
            foreach (int id in selection)
            {
                if (!World.Entities.TryGetValue(id, out InfraEntity entity) ||
                    entity.Owner != 0 || entity.Kind != InfraEntityKind.Worker || entity.Health <= 0f) continue;
                Issue(id, kind, cell, target);
                commanded = true;
                if (kind == InfraCommandKind.Build) break;
            }
            if (!commanded) Feedback = "Select an owned survivor before issuing this command.";
        }

        public bool PreviewBuild(Vector2Int cell, out string reason) =>
            World.CanBuild(0, SelectedId, cell, out reason);

        public void BeginWallPlacement()
        {
            if (World.Entities.TryGetValue(SelectedId, out InfraEntity actor) && actor.Owner == 0 &&
                actor.Kind == InfraEntityKind.Worker && actor.Health > 0)
            {
                PlacingWall = true;
                Feedback = $"Place a wall: {config.rules.WallCost} material, physically delivered. Esc cancels preview.";
            }
            else Feedback = "Select an owned survivor to build.";
        }

        public void CancelSelectedBlueprint()
        {
            int builder = WorkerId;
            if (!World.Entities.TryGetValue(builder, out InfraEntity actor) || actor.Health <= 0f)
                builder = SecondWorkerId;
            Issue(builder, InfraCommandKind.CancelBuild, default, SelectedId);
        }

        public int SpawnRaid()
        {
            if (!World.Entities.TryGetValue(WorkerId, out InfraEntity worker) || worker.Health <= 0)
            {
                Feedback = "Raid rejected: the demonstration survivor is no longer alive.";
                return 0;
            }
            if (!World.IsWalkable(Layout.RaidSpawn))
            {
                Feedback = $"Raid postponed: spawn {Layout.RaidSpawn} is blocked.";
                return 0;
            }
            InfraEntity dinosaur = World.AddEntity(InfraEntityKind.Dinosaur, Layout.RaidSpawn, 1);
            Issue(dinosaur.Id, InfraCommandKind.Attack, default, WorkerId, 1);
            return dinosaur.Id;
        }

        public void DropSupply()
        {
            var candidates = new List<Vector2Int>();
            foreach (RectInt zone in config.map.eventZones)
                foreach (Vector2Int cell in zone.allPositionsWithin)
                    if (World.IsWalkable(cell) && !ActorAt(cell)) candidates.Add(cell);
            if (candidates.Count == 0)
            {
                Feedback = "Supply event postponed: no legal cell in any candidate zone.";
                return;
            }
            Vector2Int destination = candidates[eventRandom.Next(candidates.Count)];
            World.AddEntity(InfraEntityKind.GroundPile, destination, 0, config.supplyDropStock);
            Feedback = $"Supply event: {config.supplyDropStock} material at {destination}; terrain unchanged.";
        }

        private bool ActorAt(Vector2Int cell)
        {
            foreach (InfraEntity entity in World.Entities.Values)
                if (entity.Health > 0 && Layout.Map.WorldToCell(entity.Position) == cell) return true;
            return false;
        }

        public void BeginDemonstration()
        {
            ResetWorld();
            ScenarioPhase = InfrastructureScenarioPhase.Hauling;
            scenarioStarted = World.Time;
            ScenarioEvidence = "Hauling: arrival -> external resource -> camp depot";
            Issue(WorkerId, InfraCommandKind.Gather, default, MainSourceId);
            InfrastructureCamera controller = FindFirstObjectByType<InfrastructureCamera>();
            if (controller != null) controller.Frame(Layout.Map.CellCenter(Layout.MainCamp.center));
        }

        private void AdvanceScenario()
        {
            if (ScenarioPhase == InfrastructureScenarioPhase.Idle ||
                ScenarioPhase == InfrastructureScenarioPhase.Passed ||
                ScenarioPhase == InfrastructureScenarioPhase.Failed) return;
            if (World.Time - scenarioStarted > config.scenarioTimeout)
            {
                string action = World.Entities.TryGetValue(WorkerId, out InfraEntity worker) ?
                    $"{worker.TaskStatus}/{worker.Action}: {worker.Reason}" : "survivor missing";
                FailScenario($"Scenario timed out in {ScenarioPhase}: {action}");
                return;
            }
            if (ScenarioPhase == InfrastructureScenarioPhase.Hauling &&
                World.Entities.TryGetValue(MainDepotId, out InfraEntity depot) &&
                depot.Stored >= config.rules.WallCost + config.rules.CarryCapacity)
            {
                Issue(WorkerId, InfraCommandKind.Stop, default);
                InfraCommandResult result = Issue(WorkerId, InfraCommandKind.Build, Layout.MainCamp.Gate);
                if (!result.Accepted) { FailScenario($"Gate build rejected: {result.Reason}"); return; }
                scenarioWall = result.TargetId;
                ScenarioPhase = InfrastructureScenarioPhase.Building;
                ScenarioEvidence = "Depot stocked; hauling to reserved gate footprint and constructing";
            }
            else if (ScenarioPhase == InfrastructureScenarioPhase.Building &&
                World.Entities.TryGetValue(scenarioWall, out InfraEntity wall) && wall.Kind == InfraEntityKind.Wall)
            {
                InfraCommandResult retreat = Issue(WorkerId, InfraCommandKind.Move, Layout.MainCamp.center);
                if (!retreat.Accepted) { FailScenario($"Camp retreat rejected: {retreat.Reason}"); return; }
                scenarioDinosaur = SpawnRaid();
                if (scenarioDinosaur == 0) { FailScenario(Feedback); return; }
                ScenarioPhase = InfrastructureScenarioPhase.Breaching;
                ScenarioEvidence = "Wall completed and route blocked; dinosaur planning useful breach";
            }
            else if (ScenarioPhase == InfrastructureScenarioPhase.Breaching &&
                (!World.Entities.TryGetValue(scenarioWall, out InfraEntity blocker) || blocker.Health <= 0))
            {
                ScenarioPhase = InfrastructureScenarioPhase.Entering;
                ScenarioEvidence = "Wall destroyed; checking traversal through the reopened entrance";
            }
            else if (ScenarioPhase == InfrastructureScenarioPhase.Entering &&
                World.Entities.TryGetValue(scenarioDinosaur, out InfraEntity dinosaur))
            {
                Vector2 relative = dinosaur.Position - Layout.Map.CellCenter(Layout.MainCamp.Gate);
                if (Vector2.Dot(relative, Layout.MainCamp.entranceDirection) >= -Layout.Map.CellSize * .25f) return;
                Issue(scenarioDinosaur, InfraCommandKind.Stop, default, 0, 1);
                ScenarioPhase = InfrastructureScenarioPhase.Passed;
                ScenarioEvidence = $"PASS in {World.Time - scenarioStarted:0.0}s: hauled, built, breached, entered. " +
                    $"Physical {World.TotalPhysicalMaterials} + consumed {World.ConsumedMaterials} = initial {World.InitialMaterials}. " +
                    $"Spatial revision {World.Revision}.";
                Feedback = ScenarioEvidence;
            }
        }

        private void FailScenario(string reason)
        {
            if (ScenarioPhase == InfrastructureScenarioPhase.Failed) return;
            ScenarioPhase = InfrastructureScenarioPhase.Failed;
            ScenarioEvidence = reason;
            Feedback = reason;
            Paused = true;
            Debug.LogError(reason, this);
        }

        public void RefreshViews()
        {
            var stale = new List<int>();
            foreach (KeyValuePair<int, InfrastructureEntityView> pair in views)
                if (!World.Entities.TryGetValue(pair.Key, out InfraEntity entity) || entity.Health <= 0f)
                    stale.Add(pair.Key);
            foreach (int id in stale)
            {
                Destroy(views[id].gameObject);
                views.Remove(id);
                selection.Remove(id);
            }
            foreach (InfraEntity entity in World.Entities.Values)
            {
                if (entity.Health <= 0f) continue;
                if (!views.TryGetValue(entity.Id, out InfrastructureEntityView view))
                {
                    var root = new GameObject($"{entity.Kind} #{entity.Id}");
                    root.transform.SetParent(transform, false);
                    view = root.AddComponent<InfrastructureEntityView>();
                    view.Configure(config, entity);
                    views.Add(entity.Id, view);
                }
                view.Present(entity, selection.Contains(entity.Id));
            }
        }
    }
}
