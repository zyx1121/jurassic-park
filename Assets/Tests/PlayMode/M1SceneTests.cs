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
    /// <summary>
    /// What EditMode cannot see: the scene's MonoBehaviours wired to each other, and selection, orders and the camera driven
    /// from screen coordinates through the same methods the mouse calls.
    /// </summary>
    public sealed class M1SceneTests
    {
        private GameSession session;
        private EntityViewRegistry views;
        private SelectionController selection;
        private RtsCamera rtsCamera;
        private Camera viewCamera;

        private IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("M1", LoadSceneMode.Single);
            yield return null;
            yield return null;
            session = Object.FindFirstObjectByType<GameSession>();
            views = Object.FindFirstObjectByType<EntityViewRegistry>();
            selection = Object.FindFirstObjectByType<SelectionController>();
            rtsCamera = Object.FindFirstObjectByType<RtsCamera>();
            viewCamera = Camera.main;
            // The scene waits for the launcher's lobby; a test takes the "play alone" button's path.
            if (session.Role == MatchRole.None) session.BeginOffline();
            yield return null;
            Assert.That(session.IsReady, Is.True, session.Failure);
            Assert.That(session.Role, Is.EqualTo(MatchRole.Offline));
        }

        private Entity First(string definitionId, bool local = false) => session.Runtime.World.Entities.First(e =>
            e.DefinitionId == definitionId && (!local || e.Owner == session.Runtime.LocalSeat));

        private Vector2 ScreenOf(Entity entity, float heightFraction)
        {
            session.CatalogAsset.TryGet(entity.DefinitionId, out EntityCatalogAsset.Entry entry);
            Vector3 world = EntityViewRegistry.ToWorld(entity.Position) + Vector3.up * entry.DrawnHeight * heightFraction;
            return viewCamera.WorldToScreenPoint(world);
        }

        private IEnumerator WaitForIdle(EntityId actor, float seconds)
        {
            float deadline = Time.time + seconds;
            while (Time.time < deadline && session.Runtime.Tasks.CurrentOf(actor) == null) yield return null;
            while (Time.time < deadline && session.Runtime.Tasks.CurrentOf(actor) != null) yield return null;
            // Views interpolate between the last two ticks, so they trail the simulation by up to one tick and settle on the next.
            yield return new WaitForSeconds(0.35f);
        }

        [UnityTest]
        public IEnumerator TheSceneShowsEveryEntityAndAnOrderMovesItsView()
        {
            yield return LoadScene();
            Assert.That(views.Count, Is.EqualTo(session.Runtime.World.Entities.Count), "one view per entity, created from the setup event batch");
            Assert.That(Object.FindFirstObjectByType<TerrainView>().GetComponent<MeshFilter>().sharedMesh, Is.Not.Null);
            Assert.That(viewCamera.orthographic, Is.False, "the original's camera is a perspective one");

            Entity worker = First("survivor", local: true);
            Assert.That(views.TryGetTransform(worker.Id, out Transform view), Is.True);
            Vector3 startedAt = view.position;
            rtsCamera.LookAt(startedAt);
            yield return null;

            selection.ClickSelect(ScreenOf(worker, 0.5f), additive: false);
            Assert.That(selection.Selection, Is.EqualTo(new[] { worker.Id }), "a click on the drawn body selects it");
            Assert.That(view.Find("Selection").gameObject.activeSelf, Is.True);

            Vector3 groundTarget = startedAt + new Vector3(8f, 0f, 0f);
            OrderResolver.Order? order = selection.OrderAt(viewCamera.WorldToScreenPoint(groundTarget), queue: false);
            Assert.That(order.Value.Kind, Is.EqualTo(CommandKind.Move));
            yield return WaitForIdle(worker.Id, 8f);

            Assert.That(SimVector2.Distance(worker.Position, EntityViewRegistry.ToSim(groundTarget)), Is.LessThan(0.05f));
            Assert.That(Vector3.Distance(view.position, EntityViewRegistry.ToWorld(worker.Position)), Is.LessThan(0.01f), "the view settles exactly on the simulated position");
        }

        [UnityTest]
        public IEnumerator ClickingEmptyGroundDeselectsAndADragBoxSelectsOnlyOwnUnits()
        {
            yield return LoadScene();
            Entity[] own = session.Runtime.World.Entities.Where(e => e.DefinitionId == "survivor" && e.Owner == session.Runtime.LocalSeat).ToArray();
            Entity ally = session.Runtime.World.Entities.First(e => e.DefinitionId == "survivor" && e.Owner != session.Runtime.LocalSeat);
            rtsCamera.LookAt(EntityViewRegistry.ToWorld(First("depot").Position));
            yield return null;

            selection.BoxSelect(new Rect(0, 0, Screen.width, Screen.height), additive: false);
            Assert.That(selection.Selection, Is.EquivalentTo(own.Select(e => e.Id)));
            Assert.That(selection.Selection, Has.No.Member(ally.Id), "an ally's unit is never yours to select");

            selection.ClickSelect(ScreenOf(ally, 0.5f), additive: false);
            Assert.That(selection.Selection, Is.Empty, "clicking something that is not yours clears the selection");

            selection.ClickSelect(ScreenOf(own[0], 0.5f), additive: false);
            selection.ClickSelect(ScreenOf(own[1], 0.5f), additive: true);
            Assert.That(selection.Selection, Is.EqualTo(new[] { own[0].Id, own[1].Id }));
        }

        [UnityTest]
        public IEnumerator ARightClickOnTheTopOfATreeGathersAndOnTheRoofOfTheDepotDelivers()
        {
            yield return LoadScene();
            Entity worker = First("survivor", local: true), tree = First("tree"), depot = First("depot");
            selection.Select(new List<EntityId> { worker.Id });

            rtsCamera.LookAt(EntityViewRegistry.ToWorld(tree.Position));
            yield return null;
            // The top of the trunk is drawn about two metres up-screen of the foot: the part people actually click.
            Assert.That(selection.TryPickAt(ScreenOf(tree, 0.95f), null, out EntitySnapshot pickedTree), Is.True);
            Assert.That(pickedTree.Id, Is.EqualTo(tree.Id));
            OrderResolver.Order? gather = selection.OrderAt(ScreenOf(tree, 0.95f), queue: false);
            Assert.That(gather.Value.Kind, Is.EqualTo(CommandKind.Gather));
            Assert.That(gather.Value.Target, Is.EqualTo(tree.Id));

            float deadline = Time.time + 30f;
            session.Runtime.Logistics.TryGetContainer(worker.Id, out Container pack);
            while (Time.time < deadline && pack.Total == 0) yield return null;
            Assert.That(pack.Total, Is.GreaterThan(0), "the gather order, issued by a click, put wood in the pack");

            rtsCamera.LookAt(EntityViewRegistry.ToWorld(depot.Position));
            yield return null;
            session.CatalogAsset.TryGet("depot", out EntityCatalogAsset.Entry depotEntry);
            Vector3 roofFarCorner = EntityViewRegistry.ToWorld(depot.Position) + new Vector3(depotEntry.size.x * 0.4f, depotEntry.DrawnHeight, depotEntry.size.z * 0.4f);
            OrderResolver.Order? deliver = selection.OrderAt(viewCamera.WorldToScreenPoint(roofFarCorner), queue: false);
            Assert.That(deliver.Value.Kind, Is.EqualTo(CommandKind.Deliver), "most of what you see of a depot is its roof");
        }

        [UnityTest]
        public IEnumerator TheCameraZoomsInProportionStaysInsideItsLimitsAndCannotLeaveTheMap()
        {
            yield return LoadScene();
            float start = rtsCamera.ZoomLevel;

            // A trackpad gesture: a small delta on every frame for a quarter of a second.
            for (int i = 0; i < 15; i++) rtsCamera.Zoom(-0.05f);
            Assert.That(rtsCamera.ZoomLevel, Is.GreaterThan(start));
            Assert.That(rtsCamera.ZoomLevel - start, Is.LessThan(2f), "a light trackpad scroll must not slam the zoom to its limit");

            for (int i = 0; i < 200; i++) rtsCamera.Zoom(-120f);
            float zoomedOut = rtsCamera.ZoomLevel;
            rtsCamera.Zoom(-120f);
            Assert.That(rtsCamera.ZoomLevel, Is.EqualTo(zoomedOut), "clamped at the far limit");
            for (int i = 0; i < 200; i++) rtsCamera.Zoom(120f);
            Assert.That(rtsCamera.ZoomLevel, Is.LessThan(start));
            Assert.That(Vector3.Distance(viewCamera.transform.position, rtsCamera.Focus), Is.EqualTo(rtsCamera.ZoomLevel).Within(1e-3f), "the zoom level is the camera's distance from its focus");

            GridMap map = session.Runtime.Map;
            // Pans are screen-relative under the camera's yaw; find the screen direction that heads for the map's far corner.
            Vector2 ToScreen(Vector3 world) { Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, rtsCamera.transform.eulerAngles.y, 0f)) * world; return new Vector2(local.x, local.z); }
            Vector2 toFarCorner = ToScreen(new Vector3(1f, 0f, 1f));
            Assert.That(Vector3.Distance(rtsCamera.PanToWorld(toFarCorner).normalized, new Vector3(1f, 0f, 1f).normalized), Is.LessThan(1e-4f));
            for (int i = 0; i < 600; i++) rtsCamera.Pan(toFarCorner, 0.1f);
            Assert.That(rtsCamera.Focus.x, Is.EqualTo(map.Width * map.CellSize).Within(1e-3f));
            Assert.That(rtsCamera.Focus.z, Is.EqualTo(map.Height * map.CellSize).Within(1e-3f));
            for (int i = 0; i < 600; i++) rtsCamera.Pan(-toFarCorner, 0.1f);
            Assert.That(rtsCamera.Focus.x, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(rtsCamera.Focus.z, Is.EqualTo(0f).Within(1e-3f));

            Vector3 before = rtsCamera.transform.position;
            rtsCamera.Pan(toFarCorner, 5f);
            Assert.That(Vector3.Distance(before, rtsCamera.transform.position), Is.LessThan(10f), "one long frame does not throw the camera across the map");
        }
    }
}
