using System.Collections.Generic;
using JurassicPark.Simulation;
using UnityEngine;
using EntityId = JurassicPark.Simulation.EntityId;

namespace JurassicPark.Presentation
{
    /// <summary>
    /// One stand-in GameObject per entity in the read model. Views follow snapshots and never feed back into anything. All views
    /// are moved from this one loop, positions are interpolated between the last two snapshots so a 10 Hz match looks smooth at
    /// 60 fps, and entities of one definition share a mesh and an instanced material. It neither knows nor cares whether the
    /// snapshots came from a local simulation or from a host.
    /// </summary>
    public sealed class EntityViewRegistry : MonoBehaviour
    {
        private sealed class View
        {
            public EntityId Id;
            public Transform Transform;
            public GameObject Ring;
            public Vector3 Previous, Current;
            public float HalfHeight;
        }

        [SerializeField] private GameSession session;
        [SerializeField] private Material entityMaterial;
        [SerializeField] private Material ringMaterial;

        private readonly Dictionary<EntityId, View> views = new Dictionary<EntityId, View>();
        private readonly List<View> ordered = new List<View>();
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private readonly Dictionary<PlaceholderShape, Mesh> meshes = new Dictionary<PlaceholderShape, Mesh>();
        private Mesh ringMesh;
        private MatchReadModel model;
        private int shownRevision = -1;

        public void Configure(GameSession gameSession, Material entity, Material ring)
        {
            session = gameSession;
            entityMaterial = entity;
            ringMaterial = ring;
        }

        public int Count => ordered.Count;

        public bool TryGetTransform(EntityId id, out Transform viewTransform)
        {
            bool found = views.TryGetValue(id, out View view);
            viewTransform = found ? view.Transform : null;
            return found;
        }

        public void SetSelected(EntityId id, bool selected)
        {
            if (views.TryGetValue(id, out View view)) view.Ring.SetActive(selected);
        }

        private void OnEnable()
        {
            session.MatchBegan += Hook;
            // A session that begins in its own Awake has already begun by the time anything else is enabled.
            if (session.Model != null) Hook();
        }

        private void OnDisable()
        {
            session.MatchBegan -= Hook;
            if (model == null) return;
            model.EntityAppeared -= Add;
            model.EntityVanished -= Remove;
            model = null;
        }

        private void Hook()
        {
            if (model != null) return;
            model = session.Model;
            model.EntityAppeared += Add;
            model.EntityVanished += Remove;
            for (int i = 0; i < model.Entities.Count; i++) Add(model.Entities[i]);
        }

        private void Add(EntitySnapshot entity)
        {
            EntityId id = entity.Id;
            if (views.ContainsKey(id)) return;
            EntityCatalogAsset.Entry entry = model.EntryOf(entity);
            PlaceholderShape shape = entry != null ? entry.shape : PlaceholderShape.Sphere;
            Vector3 size = entry != null ? entry.size : Vector3.one * 0.6f;

            var root = new GameObject($"{model.DefinitionIdOf(entity)} {id.Value}");
            root.transform.SetParent(transform, false);
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = size;
            body.AddComponent<MeshFilter>().sharedMesh = MeshFor(shape);
            body.AddComponent<MeshRenderer>().sharedMaterial = MaterialFor(model.DefinitionIdOf(entity), entry != null ? entry.color : Color.magenta);
            float halfHeight = shape == PlaceholderShape.Capsule || shape == PlaceholderShape.Cylinder ? size.y : size.y * 0.5f;
            body.transform.localPosition = new Vector3(0f, halfHeight, 0f);

            var ring = new GameObject("Selection");
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            float ringSize = Mathf.Max(size.x, size.z) * 1.5f;
            ring.transform.localScale = new Vector3(ringSize, 1f, ringSize);
            ring.AddComponent<MeshFilter>().sharedMesh = RingMesh();
            ring.AddComponent<MeshRenderer>().sharedMaterial = ringMaterial;
            ring.SetActive(false);

            var view = new View { Id = id, Transform = root.transform, Ring = ring, HalfHeight = halfHeight };
            view.Previous = view.Current = ToWorld(entity.Position);
            root.transform.position = view.Current;
            views.Add(id, view);
            ordered.Add(view);
        }

        private void Remove(EntityId id)
        {
            if (!views.TryGetValue(id, out View view)) return;
            views.Remove(id);
            ordered.Remove(view);
            Destroy(view.Transform.gameObject);
        }

        private void LateUpdate()
        {
            if (model == null) return;
            bool fresh = model.Revision != shownRevision;
            shownRevision = model.Revision;
            float t = Mathf.Clamp01(model.Fraction());
            for (int i = 0; i < ordered.Count; i++)
            {
                View view = ordered[i];
                bool wasMoving = view.Previous != view.Current;
                if (fresh && model.TryGet(view.Id, out EntitySnapshot snapshot))
                {
                    view.Current = ToWorld(snapshot.Position);
                    view.Previous = ToWorld(model.PreviousPositionOf(view.Id, snapshot.Position));
                }
                bool moving = view.Previous != view.Current;
                // Idle entities are skipped entirely; one that just stopped is snapped onto its final position once.
                if (moving) view.Transform.position = Vector3.Lerp(view.Previous, view.Current, t);
                else if (wasMoving) view.Transform.position = view.Current;
            }
        }

        public static Vector3 ToWorld(SimVector2 position) => new Vector3(position.X, 0f, position.Y);
        public static SimVector2 ToSim(Vector3 position) => new SimVector2(position.x, position.z);

        private Material MaterialFor(string definitionId, Color color)
        {
            if (materials.TryGetValue(definitionId, out Material material)) return material;
            material = new Material(entityMaterial) { name = definitionId, color = color, enableInstancing = true };
            materials.Add(definitionId, material);
            return material;
        }

        private Mesh MeshFor(PlaceholderShape shape)
        {
            if (meshes.TryGetValue(shape, out Mesh mesh)) return mesh;
            PrimitiveType type = shape == PlaceholderShape.Box ? PrimitiveType.Cube : shape == PlaceholderShape.Cylinder ? PrimitiveType.Cylinder
                : shape == PlaceholderShape.Sphere ? PrimitiveType.Sphere : PrimitiveType.Capsule;
            // Borrow the built-in mesh and throw the primitive away: its collider and renderer are not wanted.
            GameObject primitive = GameObject.CreatePrimitive(type);
            mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            Destroy(primitive);
            meshes.Add(shape, mesh);
            return mesh;
        }

        private Mesh RingMesh()
        {
            if (ringMesh != null) return ringMesh;
            const int segments = 32;
            const float inner = 0.42f, outer = 0.5f;
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = direction * inner;
                vertices[i * 2 + 1] = direction * outer;
                int next = (i + 1) % segments;
                int t = i * 6;
                triangles[t] = i * 2; triangles[t + 1] = next * 2; triangles[t + 2] = i * 2 + 1;
                triangles[t + 3] = i * 2 + 1; triangles[t + 4] = next * 2; triangles[t + 5] = next * 2 + 1;
            }
            ringMesh = new Mesh { name = "SelectionRing", vertices = vertices, triangles = triangles };
            ringMesh.RecalculateNormals();
            return ringMesh;
        }

        private void OnDestroy()
        {
            foreach (Material material in materials.Values) Destroy(material);
            if (ringMesh != null) Destroy(ringMesh);
        }
    }
}
