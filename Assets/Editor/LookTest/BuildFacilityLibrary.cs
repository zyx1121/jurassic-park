using JurassicPark.World;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace JurassicPark.EditorTools
{
    /// <summary>Creates one FacilityKit per landmark from Assets/Sprites/Facilities, laid out per the art handoff (out/facilities/LAYOUT.md).</summary>
    public static class BuildFacilityLibrary
    {
        public const string LibraryPath = "Assets/Data/FacilityLibrary.asset";

        private struct Def
        {
            public string file; public float x, z, yaw, width; public int cell, opaque; public bool solid; public FacilityCollider[] colliders;
            public Def(string f, float x, float z, float yaw, float width, int cell, int opaque, bool solid, Vector3 box, Vector3 boxOffset = default, FacilityCollider[] colliders = null)
            {
                file = f; this.x = x; this.z = z; this.yaw = yaw; this.width = width; this.cell = cell; this.opaque = opaque; this.solid = solid;
                this.colliders = colliders ?? (solid ? new[] { new FacilityCollider(box, boxOffset) } : new FacilityCollider[0]);
            }
        }

        private struct Kit
        {
            public string name; public float clear; public Vector2 pickup; public Def[] pieces;
            public Kit(string n, float c, Vector2 pickup, params Def[] p) { name = n; clear = c; this.pickup = pickup; pieces = p; }
        }

        // Offsets are layout meters (x east, z north); widths are the intended world width of the opaque
        // sprite; opaque pixel widths were measured from the delivered PNGs. Colliders are coarse boxes and
        // every kit keeps a 2 m route open, as the handoff asks.
        private static readonly Kit[] Kits =
        {
            new Kit("CrashSite", 13f, Vector2.zero,
                new Def("crash_fuselage", 0f, 0f, 0f, 12f, 384, 384, true, Vector3.zero, colliders: new[]
                {
                    new FacilityCollider(new Vector3(4.75f, 3f, 3.5f), new Vector3(-3.625f, 0f, 0f)),
                    new FacilityCollider(new Vector3(4.75f, 3f, 3.5f), new Vector3(3.625f, 0f, 0f)),
                }),
                new Def("crash_torn_wing", 3f, -6f, 12f, 10f, 256, 256, true, new Vector3(10f, 1.5f, 4f)),
                new Def("crash_luggage_scatter", -6f, -6f, -8f, 6f, 256, 256, false, Vector3.zero),
                new Def("crash_tail_section", 8f, 4f, 18f, 7f, 256, 255, true, new Vector3(6f, 4f, 3f))),
            new Kit("VisitorCenter", 11f, new Vector2(2f, 0f),
                new Def("visitor_center_facade", 0f, 0f, 0f, 12f, 256, 256, true, Vector3.zero, colliders: new[]
                {
                    new FacilityCollider(new Vector3(6.75f, 5f, 3f), new Vector3(-2.625f, 0f, 0f)),
                    new FacilityCollider(new Vector3(2.75f, 5f, 3f), new Vector3(4.625f, 0f, 0f)),
                }),
                new Def("visitor_center_broken_sign", -3f, 7f, 0f, 8f, 256, 256, true, new Vector3(8f, 1.2f, 2f)),
                new Def("visitor_center_column_pair", -7f, 0f, 0f, 5f, 256, 256, true, Vector3.zero, colliders: new[]
                {
                    new FacilityCollider(new Vector3(0.7f, 4f, 0.7f), new Vector3(-1.9f, 0f, 0f)),
                    new FacilityCollider(new Vector3(0.7f, 4f, 0.7f), new Vector3(1.9f, 0f, 0f)),
                }),
                new Def("visitor_center_entry_rubble", 2f, -5f, 0f, 8f, 256, 256, false, Vector3.zero)),
            new Kit("PowerStation", 10f, new Vector2(3.8f, 0f),
                new Def("power_station_transformer", 0f, 0f, 0f, 5f, 256, 241, true, new Vector3(5f, 3.5f, 3.5f)),
                new Def("power_station_pylons", -1f, 6f, 0f, 12f, 256, 256, false, Vector3.zero),
                new Def("power_station_cable_spools", -6f, -4f, 0f, 5f, 256, 256, true, new Vector3(5f, 1.5f, 2.5f)),
                new Def("power_station_control_cabinet", 5f, -3f, 0f, 3f, 256, 256, true, new Vector3(3f, 2.2f, 1.5f))),
            new Kit("Paddock", 14f, new Vector2(-4f, 4f),
                new Def("raptor_paddock_gate", 0f, 0f, 0f, 10f, 256, 255, true, new Vector3(10f, 3f, 0.8f)),
                new Def("raptor_paddock_fence_breach", -6f, 7f, 0f, 14f, 256, 255, false, Vector3.zero),
                new Def("raptor_paddock_fence_corner", 8f, 6f, 0f, 8f, 256, 256, true, new Vector3(8f, 2.5f, 0.8f)),
                new Def("raptor_paddock_feeding_crane", 3f, -6f, 0f, 6f, 256, 256, true, new Vector3(2f, 4f, 2f), new Vector3(-1.5f, 0f, 0f))),
            new Kit("Lookout", 8f, Vector2.zero,
                new Def("lookout_tower_frame", 0f, 0f, 0f, 5f, 256, 131, true, Vector3.zero, colliders: new[]
                {
                    new FacilityCollider(new Vector3(0.5f, 6f, 0.5f), new Vector3(-1.75f, 0f, -1.75f)),
                    new FacilityCollider(new Vector3(0.5f, 6f, 0.5f), new Vector3(1.75f, 0f, -1.75f)),
                    new FacilityCollider(new Vector3(0.5f, 6f, 0.5f), new Vector3(-1.75f, 0f, 1.75f)),
                    new FacilityCollider(new Vector3(0.5f, 6f, 0.5f), new Vector3(1.75f, 0f, 1.75f)),
                }),
                new Def("lookout_tower_ladder", 0f, -2.4f, 0f, 1f, 256, 63, false, Vector3.zero),
                new Def("lookout_tower_collapsed_roof", -5f, -4f, 0f, 5f, 256, 189, true, new Vector3(5f, 1f, 3f)),
                new Def("lookout_tower_floodlight", 5f, -3f, 0f, 3f, 256, 133, true, new Vector3(0.6f, 3f, 0.6f))),
            new Kit("Dock", 12f, Vector2.zero,
                new Def("dock_planks", 0f, -6f, 0f, 12f, 256, 256, true, new Vector3(1.8f, 0.25f, 12f), new Vector3(0f, -0.25f, 0f)),
                new Def("dock_posts", 0f, 1f, 0f, 5f, 256, 229, true, Vector3.zero, colliders: new[]
                {
                    new FacilityCollider(new Vector3(0.5f, 3f, 0.5f), new Vector3(-1.75f, 0f, 0f)),
                    new FacilityCollider(new Vector3(0.5f, 3f, 0.5f), new Vector3(1.75f, 0f, 0f)),
                }),
                new Def("dock_half_sunk_boat", 5f, -6f, 0f, 8f, 384, 384, true, new Vector3(8f, 2.5f, 3.5f)),
                new Def("dock_mooring_ropes", 2f, -3f, 0f, 3f, 256, 256, false, Vector3.zero)),
        };

        [CliCommand("build_facility_library", "Create FacilityKit assets from Assets/Sprites/Facilities and the FacilityLibrary")]
        public static string Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Data/Facilities")) AssetDatabase.CreateFolder("Assets/Data", "Facilities");
            if (!AssetDatabase.IsValidFolder("Assets/Materials/Facilities")) AssetDatabase.CreateFolder("Assets/Materials", "Facilities");
            FacilityLibrary lib = AssetDatabase.LoadAssetAtPath<FacilityLibrary>(LibraryPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<FacilityLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }

            var kits = new System.Collections.Generic.List<FacilityKit>();
            int pieces = 0;
            foreach (Kit k in Kits)
            {
                string path = $"Assets/Data/Facilities/{k.name}.asset";
                FacilityKit kit = AssetDatabase.LoadAssetAtPath<FacilityKit>(path);
                if (kit == null)
                {
                    kit = ScriptableObject.CreateInstance<FacilityKit>();
                    AssetDatabase.CreateAsset(kit, path);
                }

                kit.facilityName = k.name;
                kit.clearRadius = k.clear;
                kit.pickupOffset = k.pickup;
                var list = new System.Collections.Generic.List<FacilityPiece>();
                foreach (Def d in k.pieces)
                {
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Sprites/Facilities/{d.file}.png");
                    if (tex == null)
                    {
                        throw new System.InvalidOperationException($"Facility sprite missing: {d.file}");
                    }

                    list.Add(new FacilityPiece
                    {
                        sprite = tex, offset = new Vector2(d.x, d.z), yaw = d.yaw, widthMeters = d.width, cellPixels = d.cell, opaquePixels = d.opaque,
                        solid = d.solid, colliders = d.colliders,
                        material = BuildPropLibrary.SpriteMaterial($"Assets/Materials/Facilities/{d.file}.mat", tex, Color.white),
                        fadeMaterial = BuildPropLibrary.FadeMaterial($"Assets/Materials/Facilities/{d.file}_fade.mat", tex),
                    });
                    pieces++;
                }

                kit.pieces = list.ToArray();
                EditorUtility.SetDirty(kit);
                kits.Add(kit);
            }

            lib.kits = kits.ToArray();
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            return $"{LibraryPath} ({kits.Count} kits, {pieces} pieces)";
        }
    }
}
