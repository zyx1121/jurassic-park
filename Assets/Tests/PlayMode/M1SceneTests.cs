using System.Collections;
using System.Collections.Generic;
using System.Linq;
using JurassicPark.Presentation;
using JurassicPark.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EntityId = JurassicPark.Simulation.EntityId;

namespace JurassicPark.Tests.PlayMode
{
    /// <summary>The only thing EditMode cannot see: that the scene's MonoBehaviours are wired to each other and to the match.</summary>
    public sealed class M1SceneTests
    {
        [UnityTest]
        public IEnumerator TheSceneShowsEveryEntityAndAnOrderMovesItsView()
        {
            yield return SceneManager.LoadSceneAsync("M1", LoadSceneMode.Single);
            yield return null;
            yield return null;

            var session = Object.FindFirstObjectByType<GameSession>();
            var views = Object.FindFirstObjectByType<EntityViewRegistry>();
            var selection = Object.FindFirstObjectByType<SelectionController>();
            Assert.That(session, Is.Not.Null);
            Assert.That(views.Count, Is.EqualTo(session.Runtime.World.Entities.Count), "one view per entity, created from the setup event batch");
            Assert.That(Object.FindFirstObjectByType<TerrainView>().GetComponent<MeshFilter>().sharedMesh, Is.Not.Null);
            Assert.That(Camera.main.orthographic, Is.True);

            Entity worker = session.Runtime.World.Entities.First(e => e.DefinitionId == "survivor" && e.Owner == session.Runtime.LocalSeat);
            Assert.That(views.TryGetTransform(worker.Id, out Transform view), Is.True);
            Vector3 startedAt = view.position;
            selection.Select(new List<EntityId> { worker.Id });
            Assert.That(view.Find("Selection").gameObject.activeSelf, Is.True);

            SimVector2 target = worker.Position + new SimVector2(8f, 0f);
            session.Runtime.LocalSender.Send(CommandKind.Move, selection.Selection, target);
            float deadline = Time.time + 8f;
            while (Time.time < deadline && session.Runtime.Tasks.CurrentOf(worker.Id) == null) yield return null;
            while (Time.time < deadline && session.Runtime.Tasks.CurrentOf(worker.Id) != null) yield return null;
            // Views interpolate between the last two ticks, so they trail the simulation by up to one tick and settle on the next.
            yield return new WaitForSeconds(0.35f);

            Assert.That(worker.Position, Is.EqualTo(target));
            Assert.That(Vector3.Distance(view.position, EntityViewRegistry.ToWorld(target)), Is.LessThan(0.01f), "the view ends exactly on the simulated position");
            Assert.That(view.position, Is.Not.EqualTo(startedAt));
        }
    }
}
