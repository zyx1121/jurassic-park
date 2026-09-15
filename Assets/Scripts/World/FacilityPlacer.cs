using JurassicPark.Core;
using JurassicPark.Scene;
using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicPark.World
{
    /// <summary>Assembles a facility kit under a slot transform: one billboard quad per piece, box colliders for solid pieces.</summary>
    public static class FacilityPlacer
    {
        public static int Place(FacilityKit kit, Transform slot, System.Func<Vector3, float> groundHeight = null)
        {
            int placed = 0;
            foreach (FacilityPiece piece in kit.pieces)
            {
                if (piece.sprite == null) continue;
                GameObject root = new GameObject(piece.sprite.name);
                root.transform.SetParent(slot, false);
                root.transform.localPosition = new Vector3(piece.offset.x, 0f, piece.offset.y);
                root.transform.localRotation = Quaternion.Euler(0f, piece.yaw, 0f);
                if (groundHeight != null)
                {
                    Vector3 position = root.transform.position;
                    position.y = groundHeight(position);
                    root.transform.position = position;
                }

                float size = piece.QuadSize;
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Sprite";
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.transform.SetParent(root.transform, false);
                quad.transform.localPosition = new Vector3(0f, size * 0.5f - 0.05f, 0f);
                quad.transform.localScale = new Vector3(size, size, 1f);
                MeshRenderer mr = quad.GetComponent<MeshRenderer>();
                Material mat = piece.material;
                if (mat == null)
                {
                    mat = new Material(PropPlacer.FallbackTemplate()) { name = piece.sprite.name + " (runtime)" };
                    mat.SetTexture("_BaseMap", piece.sprite);
                }

                mr.sharedMaterial = mat;
                mr.shadowCastingMode = piece.solid ? ShadowCastingMode.TwoSided : ShadowCastingMode.Off;
                mr.receiveShadows = true;
                quad.AddComponent<Billboard>();

                if (piece.solid && piece.colliders != null)
                {
                    foreach (FacilityCollider proxy in piece.colliders)
                    {
                        BoxCollider box = root.AddComponent<BoxCollider>();
                        box.size = proxy.size;
                        box.center = proxy.center;
                    }
                }

                if (piece.fadeMaterial != null && size >= 1.2f)
                {
                    // Trigger volume over the sprite so the see-through cast can fade the piece
                    CapsuleCollider vol = root.AddComponent<CapsuleCollider>();
                    vol.isTrigger = true;
                    vol.radius = Mathf.Max(0.6f, piece.widthMeters * 0.35f);
                    vol.height = size;
                    vol.center = new Vector3(0f, size * 0.5f, 0f);
                    root.AddComponent<Occluder>().Configure(mr, mat, piece.fadeMaterial);
                }

                placed++;
            }

            return placed;
        }
    }
}
