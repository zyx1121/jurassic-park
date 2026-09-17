using System;
using System.Collections.Generic;
using JurassicPark.Simulation;
using UnityEngine;

namespace JurassicPark.Presentation
{
    /// <summary>
    /// The scene's owner of the match. Builds the simulation from assets, advances it once per frame, drains its events once
    /// and hands that one batch to everyone who shows things. Nothing here or downstream changes simulation state except by
    /// sending commands.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class GameSession : MonoBehaviour
    {
        [SerializeField] private SimulationSettingsAsset settings;
        [SerializeField] private MapDefinitionAsset map;
        [SerializeField] private EntityCatalogAsset catalog;
        [SerializeField] private ScenarioAsset scenario;

        public SimulationRuntime Runtime { get; private set; }
        public EntityCatalogAsset CatalogAsset => catalog;

        /// <summary>Ticks run by the most recent frame. Views snapshot positions when this is not zero.</summary>
        public int TicksThisFrame { get; private set; }

        /// <summary>Raised once per frame with everything that happened in it, including the setup batch on the first frame.</summary>
        public event Action<IReadOnlyList<SimEvent>> EventsDrained;

        public void Configure(SimulationSettingsAsset settingsAsset, MapDefinitionAsset mapAsset, EntityCatalogAsset catalogAsset, ScenarioAsset scenarioAsset)
        {
            settings = settingsAsset;
            map = mapAsset;
            catalog = catalogAsset;
            scenario = scenarioAsset;
        }

        private void Awake()
        {
            Runtime = SimulationRuntime.Build(settings, map, catalog, scenario);
            // A fanless MacBook throttles when a loop runs flat out: cap the frame rate, and much lower when nobody is watching.
            QualitySettings.vSyncCount = Application.isBatchMode ? 0 : 1;
            Application.targetFrameRate = Application.isBatchMode ? settings.batchModeFrameRate : settings.targetFrameRate;
        }

        private void Update()
        {
            if (Runtime.World.IsFaulted) return;
            TicksThisFrame = Runtime.World.Advance(Time.deltaTime);
            if (Runtime.World.PendingEventCount == 0) return;
            IReadOnlyList<SimEvent> events = Runtime.World.DrainEvents();
            for (int i = 0; i < events.Count; i++)
                if (events[i] is CommandResolved answer) Runtime.LocalSender.Observe(answer);
            EventsDrained?.Invoke(events);
        }
    }
}
