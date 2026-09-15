using JurassicPark.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicPark.World
{

    public static class PickupFactory
    {
        public static Pickup Spawn(PickupLibrary lib, ResourceKind kind, int amount, Vector3 groundPosition, Transform parent = null, int boatPartId = -1)
        {
            GameObject go = new GameObject(kind == ResourceKind.BoatPart ? $"BoatPart{boatPartId}" : $"{kind}x{amount}");
            go.transform.SetParent(parent, false);
            go.transform.position = groundPosition + Vector3.up * 0.35f;

            Texture2D icon = lib.Icon(kind);
            float size = icon != null ? icon.width / lib.pixelsPerUnit : 0.5f;
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Icon";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(go.transform, false);
            quad.transform.localScale = new Vector3(size, size, 1f);
            MeshRenderer mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = lib.spriteMaterial != null ? lib.spriteMaterial : PropPlacer.Template();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            var block = new MaterialPropertyBlock();
            if (icon != null) block.SetTexture("_BaseMap", icon);
            block.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, 0f));
            mr.SetPropertyBlock(block);
            quad.AddComponent<JurassicPark.Core.Billboard>();

            SphereCollider trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = lib.collectRadius;

            Pickup pickup = go.AddComponent<Pickup>();
            pickup.Configure(kind, amount, boatPartId);
            return pickup;
        }

        /// <summary>Drops everything an inventory holds around a position, e.g. when a player goes down.</summary>
        public static void DropAll(PickupLibrary lib, ResourceInventory inv, Vector3 groundPosition, Transform parent = null)
        {
            var items = inv.TakeEverything();
            for (int i = 0; i < items.Count; i++)
            {
                float a = i * Mathf.PI * 2f / Mathf.Max(1, items.Count);
                Vector3 p = groundPosition + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.7f;
                if (items[i].kind == ResourceKind.BoatPart)
                {
                    Spawn(lib, ResourceKind.BoatPart, 1, p, parent, items[i].amount);
                }
                else
                {
                    Spawn(lib, items[i].kind, items[i].amount, p, parent);
                }
            }
        }
    }
}
