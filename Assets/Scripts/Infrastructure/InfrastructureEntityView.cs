using JurassicPark.Core;
using UnityEngine;

namespace JurassicPark.Infrastructure
{
    public sealed class InfrastructureEntityView : MonoBehaviour
    {
        private InfrastructureConfig config;
        private InfraEntityKind kind;
        private Transform body;
        private Transform cargo;
        private MeshRenderer sprite;
        private SpriteSheetSet clips;
        private LineRenderer selection;
        private MaterialPropertyBlock properties;
        private Vector2 previous;
        private int facing;
        private float clipTime;
        private float lastMoveTime = float.NegativeInfinity;
        private string clipName;

        public void Configure(InfrastructureConfig settings, InfraEntity entity)
        {
            config = settings;
            properties = new MaterialPropertyBlock();
            previous = entity.Position;
            var ring = new GameObject("Selection");
            ring.transform.SetParent(transform, false);
            selection = ring.AddComponent<LineRenderer>();
            selection.sharedMaterial = config.structureMaterial;
            selection.useWorldSpace = false;
            selection.loop = true;
            selection.widthMultiplier = .08f;
            selection.positionCount = 32;
            float radius = config.map.cellSize * .48f;
            for (int i = 0; i < 32; i++)
            {
                float angle = i * Mathf.PI * 2f / 32;
                selection.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, .07f, Mathf.Sin(angle) * radius));
            }
            Tint(selection, config.workerColor);
            BuildBody(entity.Kind);
        }

        private void BuildBody(InfraEntityKind value)
        {
            if (body != null) Destroy(body.gameObject);
            kind = value;
            body = new GameObject(kind.ToString()).transform;
            body.SetParent(transform, false);
            sprite = null;
            cargo = null;
            float cell = config.map.cellSize;
            if (kind == InfraEntityKind.Worker || kind == InfraEntityKind.Dinosaur)
            {
                clips = kind == InfraEntityKind.Worker ? config.survivorSprites : config.dinosaurSprites;
                float size = kind == InfraEntityKind.Worker ? 2.5f : 3.8f;
                GameObject quad = Primitive(PrimitiveType.Quad, body, new Vector3(0, size * .48f, 0), new Vector3(size, size, 1));
                quad.name = "AnimatedSprite";
                sprite = quad.GetComponent<MeshRenderer>();
                sprite.sharedMaterial = config.spriteMaterial;
                quad.AddComponent<Billboard>();
                if (kind == InfraEntityKind.Worker)
                {
                    cargo = Primitive(PrimitiveType.Cube, body, new Vector3(.5f, .8f, -.15f), Vector3.one * .45f).transform;
                    Tint(cargo.GetComponent<Renderer>(), new Color(.9f, .66f, .23f));
                }
            }
            else if (kind == InfraEntityKind.Source)
            {
                GameObject quad = Primitive(PrimitiveType.Quad, body, new Vector3(0, 2f, 0), new Vector3(4f, 4f, 1));
                quad.GetComponent<Renderer>().sharedMaterial = config.trees[0];
                quad.AddComponent<Billboard>();
                Marker(body, new Color(.83f, .7f, .3f), cell * .68f);
            }
            else if (kind == InfraEntityKind.Depot)
            {
                for (int i = 0; i < 4; i++)
                {
                    GameObject box = Primitive(PrimitiveType.Cube, body,
                        new Vector3((i % 2 - .5f) * .65f, .4f + (i / 2) * .55f, 0), new Vector3(.6f, .55f, 1f));
                    Tint(box.GetComponent<Renderer>(), new Color(.61f, .42f, .22f));
                }
                Marker(body, new Color(.38f, .72f, .8f), cell * .9f);
            }
            else if (kind == InfraEntityKind.Wall)
            {
                for (int i = -1; i <= 1; i++)
                    Tint(Primitive(PrimitiveType.Cube, body, new Vector3(i * cell * .34f, .8f, 0),
                        new Vector3(.2f, 1.6f, cell * .7f)).GetComponent<Renderer>(), new Color(.46f, .31f, .16f));
                for (int i = 0; i < 2; i++)
                    Tint(Primitive(PrimitiveType.Cube, body, new Vector3(0, .6f + i * .65f, 0),
                        new Vector3(cell * .96f, .2f, cell * .5f)).GetComponent<Renderer>(), new Color(.64f, .45f, .24f));
            }
            else Marker(body, kind == InfraEntityKind.Blueprint ? new Color(.35f, .85f, .69f) :
                new Color(.9f, .68f, .25f), cell * .9f);
        }

        public void Present(InfraEntity entity, bool selected)
        {
            if (kind != entity.Kind) BuildBody(entity.Kind);
            transform.position = new Vector3(entity.Position.x, 0f, entity.Position.y);
            selection.gameObject.SetActive(selected);
            if (cargo != null) cargo.gameObject.SetActive(entity.Carried > 0);
            if (sprite != null && clips != null)
            {
                Vector2 direction = entity.Position - previous;
                if (direction.sqrMagnitude > .00001f)
                {
                    lastMoveTime = Time.time;
                    facing = Mathf.Abs(direction.x) > Mathf.Abs(direction.y) ? (direction.x > 0 ? 2 : 1) :
                        (direction.y > 0 ? 3 : 0);
                }
                bool walking = Time.time - lastMoveTime < config.tickSeconds * 2f;
                string next = walking ? "Walk" : entity.Action.IndexOf("attack", System.StringComparison.OrdinalIgnoreCase) >= 0 ? "Bite" : "Idle";
                SpriteSheetClip clip = clips.Find(next) ?? clips.Find("Idle");
                if (clip == null) throw new System.InvalidOperationException($"Missing Idle sprite clip for {kind}.");
                if (clipName != clip.name) { clipName = clip.name; clipTime = 0f; }
                clipTime += Time.deltaTime;
                int frame = SpriteSheetMath.FrameAt(clipTime, clip.framesPerSecond, clip.columns, clip.loop || next == "Bite");
                properties.Clear();
                properties.SetTexture("_BaseMap", clip.sheet);
                properties.SetVector("_BaseMap_ST", SpriteSheetMath.CellScaleOffset(frame, Mathf.Min(facing, clip.rows - 1), clip.columns, clip.rows));
                sprite.SetPropertyBlock(properties);
            }
            previous = entity.Position;
        }

        private GameObject Primitive(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = config.structureMaterial;
            Destroy(go.GetComponent<Collider>());
            return go;
        }

        private void Marker(Transform parent, Color color, float size)
        {
            GameObject marker = Primitive(PrimitiveType.Cube, parent, new Vector3(0, .08f, 0), new Vector3(size, .12f, size));
            Tint(marker.GetComponent<Renderer>(), color);
        }

        private void Tint(Renderer renderer, Color color)
        {
            properties.Clear();
            properties.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(properties);
        }
    }
}
