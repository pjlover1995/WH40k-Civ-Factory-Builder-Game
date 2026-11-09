using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WH30K.Gameplay.Construction
{
    /// <summary>Shared, batched gothic-industrial architecture for the core, construction and its preview.</summary>
    public static partial class BuildingVisuals
    {
        public const float WorldScale = 0.2f;
        public static Vector3 ShipDimensions => new Vector3(WH30K.Sim.ShipRules.Beam, WH30K.Sim.ShipRules.Height, WH30K.Sim.ShipRules.Length);
        public static Vector3 LegacyShipDimensions => new Vector3(WH30K.Sim.ShipRules.LegacyBeam, WH30K.Sim.ShipRules.LegacyHeight, WH30K.Sim.ShipRules.LegacyLength);
        private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
        private static Material[] materials;
        private static Material[] shipMaterials;
        private static Material[] tenderMaterials;
        private const int Stone = 0, Iron = 1, Roof = 2, Brass = 3, Amber = 4, Cyan = 5, Dark = 6;
        public static Material[] ServiceRoadMaterials => new[] { GetMaterials()[Iron], GetMaterials()[Brass] };
        public static Material[] ArchitectureMaterials => GetMaterials();

        public static GameObject Create(string id, string name, Transform parent, Vector3 dimensions,
            Material preview = null)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localScale = dimensions;
            if (!Meshes.TryGetValue(id, out var mesh) || mesh == null)
            {
                if (id == "LandingVessel")
                {
                    var authoredTender = Resources.Load<GameObject>("Architecture/TenderHull");
                    mesh = authoredTender != null ? authoredTender.GetComponentInChildren<MeshFilter>()?.sharedMesh : null;
                }
                else if(id=="Farm")mesh=FarmMesh(0);
                else if(id=="HabBlock")mesh=ResidentialVisual.PreviewCoreMesh;
                else
                {
                var builder = new Architecture();
                if (id != "ColonyShip" && id != "LandingVessel") Foundation(builder);
                switch (id)
                {
                    case "ColonyShip": ExploratorShip(builder); break;
                    case "LandingVessel": LandingShip(builder); break;
                    case "ColonyShipLegacy": LandingShip(builder); break;
                    case "HabBlock": Habitation(builder); break;
                    case "Factory": Manufactorum(builder); break;
                    case "Utility": Scrubber(builder); break;
                    case "Mine": ExtractionRig(builder, false); break;
                    case "Quarry": ExtractionRig(builder, true); break;
                    case "Harvester": HarvestStation(builder); break;
                    case "Smelter": SmeltingWorks(builder); break;
                    case "Works": FabricationWorks(builder); break;
                    case "PromethiumExtractor": ExtractionRig(builder, false); bFuelPump(builder); break;
                    case "PromethiumGenerator": PowerGenerator(builder); break;
                    case "Refinery": Refinery(builder); break;
                    case "Manufactorum": Manufactorum(builder); break;
                    case "StorageDepot": StorageDepot(builder); break;
                    case "GroundwaterPump": GroundwaterStation(builder); break;
                    case "DesalinationPump": DesalinationPlant(builder); break;
                    case "Cognitorium": Cognitorium(builder); break;
                    default: Outpost(builder); break;
                }
                mesh = builder.Build(id);
                }
                if (mesh == null)
                {
                    var fallback = new Architecture(); LandingShip(fallback); mesh = fallback.Build(id);
                }
                Meshes[id] = mesh;
            }
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            if (preview != null)
            {
                var overrides = new Material[7];
                for (var i = 0; i < overrides.Length; i++) overrides[i] = preview;
                renderer.sharedMaterials = overrides;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            else
            {
                renderer.sharedMaterials = id == "Farm" ? FarmMaterials
                    : id == "LandingVessel" ? GetTenderMaterials()
                    : id == "ColonyShip" || id == "ColonyShipLegacy" ? GetShipMaterials()
                    : GetMaterials();
                if (WH30K.Sim.IndustryRules.Facility(id)) go.AddComponent<FacilityAnimation>().Configure(id);
            }
            if (id == "ColonyShip") AddDistantShip(go, renderer);
            return go;
        }

        private static void LandingShip(Architecture b)
        {
            // Distinct from the orbiting Explorator: a low, armoured 500 m landing craft
            // with a blunt heat-shielded ram, broad lift nacelles and a stepped command deck.
            b.Box(Dark, new Vector3(0, 25, 0), new Vector3(62, 23, 100));
            b.Box(Iron, new Vector3(0, 38, 4), new Vector3(57, 23, 88));
            b.Box(Brass, new Vector3(0, 50, 16), new Vector3(49, 3, 55));
            b.Gable(Iron, new Vector3(0, 44, -25), 54, 18, 42);
            b.Box(Brass, new Vector3(0, 23, -49), new Vector3(46, 23, 3));
            b.Box(Amber, new Vector3(0, 25, -51), new Vector3(34, 7, 2));
            for (var deck = 0; deck < 4; deck++)
            {
                var z = -20 + deck * 15;
                b.Box(Brass, new Vector3(0, 51 + deck % 2 * 4, z), new Vector3(45 - deck * 2, 1.3f, 9));
                for (var side = -1; side <= 1; side += 2)
                {
                    b.Box(Dark, new Vector3(side * 26, 39, z), new Vector3(2, 10, 8));
                    b.Box(Amber, new Vector3(side * 27.2f, 40, z), new Vector3(.7f, 5.5f, 3.5f));
                    b.Beam(Brass, new Vector3(side * 25, 32, z - 5), new Vector3(side * 31, 24, z - 6), 1.6f);
                }
            }
            for (var side = -1; side <= 1; side += 2)
            {
                var x = side * 40;
                b.Box(Iron, new Vector3(x, 28, 8), new Vector3(17, 30, 68));
                b.Box(Dark, new Vector3(x, 14, 4), new Vector3(13, 4, 84));
                b.Box(Brass, new Vector3(x, 45, 14), new Vector3(18, 2, 42));
                for (var engine = -1; engine <= 1; engine++)
                {
                    var z = 31 + engine * 17;
                    b.Cylinder(Iron, new Vector3(x, 25, z), 8, 6.5f, 19, 24);
                    b.Cylinder(Dark, new Vector3(x, 25, z + 10), 7, 4, 6, 24);
                    b.Cylinder(Cyan, new Vector3(x, 25, z + 15), 3.7f, 2.8f, 1, 24);
                    b.Cylinder(Brass, new Vector3(x, 25, z - 5), 9, 9, 1.5f, 24);
                }
                for (var leg = -1; leg <= 1; leg += 2)
                {
                    var z = leg * 36;
                    b.Beam(Iron, new Vector3(x * .72f, 23, z), new Vector3(x, 3, z), 4);
                    b.Box(Brass, new Vector3(x, 2, z), new Vector3(18, 3, 12));
                }
            }
            b.Box(Roof, new Vector3(0, 59, 22), new Vector3(29, 8, 24));
            b.Box(Iron, new Vector3(0, 68, 24), new Vector3(20, 12, 20));
            b.Box(Cyan, new Vector3(0, 69, 13), new Vector3(16, 6, 2));
            b.Cylinder(Brass, new Vector3(0, 76, 24), 9, 7, 5, 12);
            for (var side = -1; side <= 1; side += 2)
                for (var z = -38; z <= 35; z += 9)
                {
                    b.Box(Dark, new Vector3(side * 29.2f, 36, z), new Vector3(1.5f, 3, 4));
                    b.Box(Amber, new Vector3(side * 30.1f, 36, z), new Vector3(.6f, 1.2f, 2));
                }
            Crest(b, new Vector3(0, 52, -28));
        }

        private static void ExtractionRig(Architecture b, bool quarry)
        {
            b.Box(Stone,new Vector3(-24,30,15),new Vector3(35,40,47));
            b.Gable(Roof,new Vector3(-24,50,15),38,16,49);
            for(var side=-1;side<=1;side+=2)
            {
                b.Beam(Iron,new Vector3(15+side*18,12,-19),new Vector3(15+side*8,108,-19),6);
                b.Beam(Brass,new Vector3(15+side*18,12,-19),new Vector3(15-side*8,88,-19),3);
                Window(b,-24,31,side<0?-10:40,13,20);
            }
            b.Box(Brass,new Vector3(15,109,-19),new Vector3(35,6,22));
            b.Cylinder(Iron,new Vector3(15,14,-19),10,quarry?4:1,70,12);
            b.Box(Dark,new Vector3(15,15,-19),new Vector3(29,4,29));
            for(var i=0;i<8;i++) b.Box(Brass,new Vector3(17,18+i*7,-31),new Vector3(22,2,3));
            if(quarry) for(var i=0;i<4;i++) b.Box(Stone,new Vector3(24,12+i*5,19),new Vector3(40-i*6,5,34-i*5));
            Crest(b,new Vector3(-24,35,-11));
        }
        private static void HarvestStation(Architecture b)
        {
            b.Box(Stone,new Vector3(0,28,0),new Vector3(72,32,64));b.Gable(Roof,new Vector3(0,44,0),76,17,68);
            for(var x=-1;x<=1;x++) { Window(b,x*22,27,-33,10,18,Cyan);Window(b,x*22,27,33,10,18,Cyan); }
            for(var side=-1;side<=1;side+=2)
            {
                b.Cylinder(Iron,new Vector3(side*32,14,-12),11,11,49,12);
                b.Cylinder(Brass,new Vector3(side*32,62,-12),12,3,10,12);
                b.Box(Cyan,new Vector3(side*32,39,-24),new Vector3(3,31,2));
            }
            b.Beam(Iron,new Vector3(0,60,0),new Vector3(0,95,0),3);Crest(b,new Vector3(0,38,-34));
        }
        private static void SmeltingWorks(Architecture b)
        {
            b.Box(Stone,new Vector3(0,35,0),new Vector3(84,46,67));b.Gable(Roof,new Vector3(0,58,0),87,20,70);
            for(var x=-1;x<=1;x++)
            {
                b.Cylinder(Iron,new Vector3(x*27,17,8),11,8,99+x*8,12);
                b.Cylinder(Brass,new Vector3(x*27,115+x*8,8),10,10,4,12);
                b.Arch(Brass,new Vector3(x*24,13,-35),20,28,3);b.Arch(Amber,new Vector3(x*24,14,-37),14,24,1);
                for(var rib=0;rib<5;rib++)b.Box(Iron,new Vector3(x*27,38+rib*13,-3),new Vector3(25,3,4));
            }
        }
        private static void FabricationWorks(Architecture b)
        {
            Manufactorum(b);
            b.Box(Brass,new Vector3(0,23,-43),new Vector3(75,5,8));
            for(var x=-2;x<=2;x++)b.Box(Cyan,new Vector3(x*14,30,-43),new Vector3(7,5,5));
        }

        private static void bFuelPump(Architecture b)
        {
            for(var side=-1;side<=1;side+=2)
            {
                b.Cylinder(Brass,new Vector3(side*37,12,36),12,12,38,12);
                b.Cylinder(Dark,new Vector3(side*37,50,36),13,10,4,12);
                b.Box(Amber,new Vector3(side*37,31,25),new Vector3(4,8,2));
            }
        }
        private static void PowerGenerator(Architecture b)
        {
            b.Box(Stone,new Vector3(0,31,0),new Vector3(84,38,70));
            b.Gable(Roof,new Vector3(0,51,0),88,18,74);
            for(var side=-1;side<=1;side+=2)
            {
                b.Cylinder(Iron,new Vector3(side*29,18,0),14,14,94,16);
                b.Cylinder(Amber,new Vector3(side*29,67,0),15,15,4,16);
                b.Box(Cyan,new Vector3(side*29,43,-12),new Vector3(4,20,3));
            }
            b.Cylinder(Brass,new Vector3(0,17,-28),18,18,62,14);
            b.Cylinder(Dark,new Vector3(0,51,-28),20,20,5,14);
            Crest(b,new Vector3(0,41,-37));
        }
        private static void Refinery(Architecture b)
        {
            b.Box(Stone,new Vector3(0,31,0),new Vector3(96,42,80));
            b.Gable(Roof,new Vector3(0,53,0),100,20,84);
            for(var x=-1;x<=1;x++)
            {
                b.Cylinder(Iron,new Vector3(x*32,17,14),14,14,78,14);
                b.Cylinder(Brass,new Vector3(x*32,58,14),16,16,5,14);
                b.Box(Amber,new Vector3(x*32,35,-29),new Vector3(14,12,3));
            }
            b.Box(Cyan,new Vector3(0,20,43),new Vector3(60,4,3));
            Crest(b,new Vector3(0,40,-40));
        }
        private static void StorageDepot(Architecture b)
        {
            b.Box(Stone,new Vector3(0,27,0),new Vector3(94,34,82));
            b.Gable(Roof,new Vector3(0,48,0),98,21,86);
            for(var x=-1;x<=1;x++)for(var z=-1;z<=1;z++)
            {
                b.Box(Iron,new Vector3(x*30,22,z*22),new Vector3(22,28,20));
                b.Box(Brass,new Vector3(x*30,37,z*22),new Vector3(23,3,21));
                b.Box(Cyan,new Vector3(x*30,24,z*22-10),new Vector3(12,2,1));
            }
            Crest(b,new Vector3(0,44,-42));
        }

        private static void GroundwaterStation(Architecture b)
        {
            b.Box(Stone,new Vector3(-20,23,0),new Vector3(58,30,60));
            b.Gable(Roof,new Vector3(-20,39,0),58,15,64);
            for(var side=-1;side<=1;side+=2)
            {
                b.Cylinder(Iron,new Vector3(side*24,15,18),12,12,44,12);
                b.Cylinder(Cyan,new Vector3(side*24,38,18),13,10,4,12);
                b.Box(Brass,new Vector3(side*24,28,0),new Vector3(4,6,35));
            }
            b.Beam(Brass,new Vector3(20,22,-18),new Vector3(20,76,-18),4);
            b.Beam(Iron,new Vector3(20,76,-18),new Vector3(36,24,-18),3);
            b.Cylinder(Dark,new Vector3(20,21,-18),6,6,48,8);Crest(b,new Vector3(-20,33,-33));
        }

        private static void DesalinationPlant(Architecture b)
        {
            b.Box(Stone,new Vector3(0,22,0),new Vector3(82,30,68));
            b.Gable(Roof,new Vector3(0,38,0),86,16,72);
            for(var side=-1;side<=1;side+=2)
            {
                b.Cylinder(Iron,new Vector3(side*27,15,15),13,13,54,14);
                b.Cylinder(Cyan,new Vector3(side*27,43,15),14,11,5,14);
                for(var rib=0;rib<4;rib++)b.Box(Brass,new Vector3(side*27,26+rib*6,-3),new Vector3(28,2,4));
            }
            b.Box(Cyan,new Vector3(0,12,47),new Vector3(94,5,5));
            b.Cylinder(Brass,new Vector3(0,13,41),8,8,24,10);Crest(b,new Vector3(0,35,-37));
        }

        private static void Foundation(Architecture b)
        {
            b.Box(Stone, new Vector3(0, 1, 0), new Vector3(100, 14, 100));
            b.Box(Iron, new Vector3(0, 9, 0), new Vector3(96, 3, 96));
            for (var x = -1; x <= 1; x += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    b.Box(Brass, new Vector3(x * 44, 12, z * 44), new Vector3(6, 4, 6));
                    b.Box(Amber, new Vector3(x * 44, 15, z * 44), new Vector3(2, 3, 2));
                }
            for (var i = 0; i < 5; i++)
                b.Box(Stone, new Vector3(0, 2 + i * 1.6f, -48 + i * 2), new Vector3(24, 3, 4));
        }

        private static void Habitation(Architecture b)
        {
            b.Box(Stone, new Vector3(0, 58, 0), new Vector3(65, 96, 68));
            b.Box(Iron, new Vector3(0, 106, 0), new Vector3(70, 5, 74));
            b.Gable(Roof, new Vector3(0, 109, 0), 71, 24, 76);
            for (var floor = 0; floor < 4; floor++)
            {
                var y = 29 + floor * 20;
                b.Box(Iron, new Vector3(0, y - 7, 0), new Vector3(68, 3, 71));
                for (var x = -1; x <= 1; x++)
                {
                    Window(b, x * 19, y, -34.8f, 8, 12);
                    Window(b, x * 19, y, 34.8f, 8, 12);
                }
                for (var side = -1; side <= 1; side += 2)
                    for (var z = -1; z <= 1; z++)
                    {
                        b.Box(Dark, new Vector3(side * 33, y + 5, z * 21), new Vector3(2, 14, 9));
                        b.Box(Amber, new Vector3(side * 34.1f, y + 6, z * 21), new Vector3(1, 10, 3));
                    }
            }
            for (var side = -1; side <= 1; side += 2)
                for (var z = -1; z <= 1; z++)
                {
                    b.Box(Stone, new Vector3(side * 39, 43, z * 27), new Vector3(10, 67, 8));
                    b.Beam(Brass, new Vector3(side * 43, 17, z * 27), new Vector3(side * 32, 100, z * 27), 3);
                    b.Cylinder(Roof, new Vector3(side * 39, 79, z * 27), 6, 0, 15, 4);
                }
            Tower(b, new Vector3(0, 108, -24), 16, 31);
            b.Arch(Dark, new Vector3(0, 10, -36), 20, 26, 4);
            b.Arch(Brass, new Vector3(0, 11, -38.3f), 13, 22, 1);
            b.Box(Dark, new Vector3(0, 20, -39), new Vector3(9, 18, 1));
            Crest(b, new Vector3(0, 93, -37));
        }

        private static void Manufactorum(Architecture b)
        {
            b.Box(Stone, new Vector3(0, 36, 4), new Vector3(84, 51, 70));
            b.Box(Iron, new Vector3(0, 65, 4), new Vector3(88, 7, 74));
            for (var i = 0; i < 4; i++)
            {
                var z = -25 + i * 18;
                b.Gable(Roof, new Vector3(0, 69, z), 86, 17, 17);
                b.Beam(Brass, new Vector3(-43, 70, z - 7), new Vector3(0, 87, z - 7), 2);
                b.Beam(Brass, new Vector3(0, 87, z - 7), new Vector3(43, 70, z - 7), 2);
                for (var side = -1; side <= 1; side += 2)
                {
                    b.Box(Iron, new Vector3(side * 44, 37, z), new Vector3(5, 52, 5));
                    b.Box(Dark, new Vector3(side * 43, 41, z), new Vector3(2, 19, 10));
                    b.Box(Amber, new Vector3(side * 44.3f, 40, z), new Vector3(1, 12, 7));
                }
            }
            for (var i = 0; i < 3; i++)
            {
                var x = -26 + i * 26;
                var h = i == 1 ? 81 : 65;
                b.Cylinder(Stone, new Vector3(x, 64, 28), 11, 9, 26);
                b.Cylinder(Iron, new Vector3(x, 80, 28), 6, 5, h);
                for (var ring = 0; ring < 3; ring++)
                    b.Cylinder(Brass, new Vector3(x, 88 + ring * 22, 28), 7, 7, 3);
                b.Cylinder(Iron, new Vector3(x, 80 + h, 28), 8, 8, 5);
                b.Cylinder(Amber, new Vector3(x, 85 + h, 28), 5.2f, 5.2f, 0.7f);
            }
            b.Arch(Brass, new Vector3(0, 11, -32), 32, 37, 5);
            b.Arch(Dark, new Vector3(0, 11, -35), 25, 31, 2);
            for (var i = 0; i < 5; i++)
                b.Box(Iron, new Vector3(0, 15 + i * 5, -36.5f), new Vector3(24, 1.3f, 1));
            for (var side = -1; side <= 1; side += 2)
            {
                b.Cylinder(Iron, new Vector3(side * 33, 13, -38), 7, 7, 27);
                b.Cylinder(Brass, new Vector3(side * 33, 39, -38), 8, 8, 3);
                b.Beam(Iron, new Vector3(side * 33, 40, -38), new Vector3(side * 33, 40, -15), 5);
                Window(b, side * 27, 49, -32.5f, 9, 10);
            }
            Crest(b, new Vector3(0, 59, -36));
        }

        private static void Scrubber(Architecture b)
        {
            b.Box(Stone, new Vector3(0, 30, -20), new Vector3(76, 40, 40));
            b.Gable(Roof, new Vector3(0, 50, -20), 79, 15, 44);
            for (var side = -1; side <= 1; side += 2)
            {
                var x = side * 25;
                b.Cylinder(Iron, new Vector3(x, 12, 20), 19, 17, 91, 20);
                b.Cylinder(Stone, new Vector3(x, 10, 20), 21, 21, 13, 20);
                for (var ring = 0; ring < 5; ring++)
                {
                    b.Cylinder(Brass, new Vector3(x, 29 + ring * 17, 20), 19, 19, 3, 20);
                    for (var vane = 0; vane < 8; vane++)
                    {
                        var a = vane * Mathf.PI / 4;
                        b.Beam(Iron, new Vector3(x + Mathf.Cos(a) * 18, 31 + ring * 17, 20 + Mathf.Sin(a) * 18),
                            new Vector3(x + Mathf.Cos(a) * 18, 43 + ring * 17, 20 + Mathf.Sin(a) * 18), 2);
                    }
                }
                b.Cylinder(Dark, new Vector3(x, 104, 20), 16, 16, 1, 20);
                b.Cylinder(Cyan, new Vector3(x, 105, 20), 12, 12, 1, 20);
                for (var blade = 0; blade < 6; blade++)
                {
                    var a = blade * Mathf.PI / 3;
                    b.Beam(Iron, new Vector3(x, 107, 20),
                        new Vector3(x + Mathf.Cos(a) * 15, 107, 20 + Mathf.Sin(a) * 15), 3);
                }
                b.Cylinder(Iron, new Vector3(x, 107, 20), 4, 4, 4);
                b.Beam(Iron, new Vector3(x, 24, -4), new Vector3(x, 24, 20), 10);
                Window(b, x, 29, -40.8f, 9, 15, Cyan);
            }
            b.Box(Iron, new Vector3(0, 85, -12), new Vector3(18, 56, 20));
            b.Cylinder(Brass, new Vector3(0, 112, -12), 11, 0, 31, 4);
            b.Beam(Iron, new Vector3(0, 130, -12), new Vector3(0, 157, -12), 1.4f);
            b.Arch(Dark, new Vector3(0, 11, -42), 18, 24, 3);
            Crest(b, new Vector3(0, 44, -43));
        }

        private static void Cognitorium(Architecture b)
        {
            b.Box(Stone, new Vector3(0, 25, 0), new Vector3(98, 42, 94));
            b.Box(Iron, new Vector3(0, 49, 0), new Vector3(88, 7, 84));
            b.Gable(Roof, new Vector3(0, 54, 0), 78, 29, 76);
            for(var side=-1;side<=1;side+=2)
            {
                b.Box(Stone,new Vector3(side*43,48,0),new Vector3(12,48,86));
                for(var z=-1;z<=1;z+=2)
                {
                    b.Beam(Brass,new Vector3(side*41,12,z*36),new Vector3(side*46,64,z*40),3.4f);
                    b.Box(Cyan,new Vector3(side*49,42,z*22),new Vector3(2,17,11));
                }
            }
            b.Cylinder(Brass,new Vector3(0,81,0),25,4,38,8);
            b.Cylinder(Iron,new Vector3(0,96,0),17,2,47,8);
            b.Cylinder(Cyan,new Vector3(0,119,0),8,0,45,8);
            for(var i=0;i<8;i++)
            {
                var angle=i*Mathf.PI/4;
                var foot=new Vector3(Mathf.Cos(angle)*36,60,Mathf.Sin(angle)*36);
                var tip=new Vector3(Mathf.Cos(angle)*46,132,Mathf.Sin(angle)*46);
                b.Beam(Brass,foot,tip,2.7f);
                b.Cylinder(Amber,tip,3,0,9,5);
            }
            b.Beam(Iron,new Vector3(0,143,-6),new Vector3(0,166,-6),1.7f);
            b.Box(Brass,new Vector3(0,148,-6),new Vector3(20,3,8));
            b.Arch(Dark,new Vector3(0,10,-46),19,25,3);
            Crest(b,new Vector3(0,43,-47));
        }

        private static void Outpost(Architecture b)
        {
            b.Box(Stone, new Vector3(0, 32, 0), new Vector3(81, 43, 73));
            b.Box(Iron, new Vector3(0, 54, 0), new Vector3(86, 5, 78));
            b.Box(Stone, new Vector3(0, 77, 7), new Vector3(44, 44, 41));
            b.Gable(Roof, new Vector3(0, 99, 7), 49, 25, 47);
            for (var side = -1; side <= 1; side += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var center = new Vector3(side * 34, 11, z * 31);
                    b.Box(Stone, center + Vector3.up * 31, new Vector3(18, 62, 18));
                    b.Box(Brass, center + Vector3.up * 63, new Vector3(21, 4, 21));
                    b.Cylinder(Roof, center + Vector3.up * 65, 14, 0, 24, 4);
                    b.Box(Amber, center + new Vector3(0, 41, z * 10), new Vector3(4, 14, 1));
                }
            for (var i = 0; i < 7; i++)
            {
                b.Box(Stone, new Vector3(-36 + i * 12, 62, -37), new Vector3(7, 12, 6));
                b.Box(Stone, new Vector3(-36 + i * 12, 62, 37), new Vector3(7, 12, 6));
            }
            Tower(b, new Vector3(0, 123, 7), 12, 19);
            b.Beam(Iron, new Vector3(0, 148, 7), new Vector3(0, 160, 7), 1.2f);
            b.Arch(Brass, new Vector3(0, 11, -38), 28, 34, 3);
            b.Arch(Dark, new Vector3(0, 11, -40), 21, 28, 2);
            Window(b, -13, 76, -14, 8, 17);
            Window(b, 13, 76, -14, 8, 17);
            Crest(b, new Vector3(0, 47, -40));
        }

        private static void Window(Architecture b, float x, float y, float z, float w, float h, int light = Amber)
        {
            var sign = z < 0 ? -1f : 1f;
            b.Arch(Brass, new Vector3(x, y, z), w + 3, h + 3, 1.5f);
            b.Arch(Dark, new Vector3(x, y + 1, z + sign), w, h, 1.2f);
            b.Arch(light, new Vector3(x, y + 3, z + sign * 1.8f), w * 0.55f, h - 4, 0.7f);
            b.Box(Iron, new Vector3(x, y + h * 0.47f, z + sign * 2.3f), new Vector3(1.1f, h - 2, 0.8f));
        }

        private static void Tower(Architecture b, Vector3 p, float width, float height)
        {
            b.Box(Stone, p + Vector3.up * height * 0.5f, new Vector3(width, height, width));
            b.Box(Brass, p + Vector3.up * height, new Vector3(width + 3, 2, width + 3));
            b.Cylinder(Roof, p + Vector3.up * (height + 1), width * 0.8f, 0, 20, 4);
        }

        private static void Crest(Architecture b, Vector3 p)
        {
            // A winged cog insignia, formed from relief geometry rather than a flat decal.
            b.Box(Brass, p, new Vector3(6, 9, 2));
            for (var side = -1; side <= 1; side += 2)
                for (var i = 0; i < 3; i++)
                    b.Beam(Brass, p + new Vector3(side * 3, 2 - i * 2, 0),
                        p + new Vector3(side * (15 - i * 3), 5 - i * 3, 0), 1.7f);
            b.Box(Dark, p + new Vector3(0, 1, -1.3f), new Vector3(2, 3, 0.5f));
        }

        private static Material[] GetMaterials()
        {
            if (materials != null && materials[0] != null) return materials;
            materials = new[]
            {
                Surface("Weathered limestone", Color.white, 0f, 0.22f, new Vector2(0, .5f), .16f, .012f),
                Surface("Forged iron", Color.white, 0.82f, 0.38f, new Vector2(.5f, .5f), .24f, .003f),
                Surface("Oxide roof", Color.white, 0.48f, 0.28f, Vector2.zero, .16f, .006f),
                Surface("Aged brass", Color.white, 0.78f, 0.43f, new Vector2(.5f, 0), .32f, .002f),
                Emissive("Amber lumen", new Color(1f, 0.52f, 0.14f)),
                Emissive("Filter status lumen", new Color(0.15f, 0.78f, 0.74f)),
                Surface("Recesses", new Color(.32f, .36f, .38f), 0.3f, 0.18f, new Vector2(.5f, .5f), .24f, .002f)
            };
            return materials;
        }

        private static Material[] GetShipMaterials()
        {
            if (shipMaterials != null && shipMaterials[0] != null) return shipMaterials;
            var atlas = UnityEngine.Resources.Load<Texture2D>("Architecture/ExploratorSurfaceAtlas");
            var template = UnityEngine.Resources.Load<Material>("Architecture/BuildingSurface");
            if (atlas == null || template == null)
                throw new System.InvalidOperationException("Missing Explorator photographic surface atlas or architecture shader material.");
            shipMaterials = new[]
            {
                ShipSurface(template, atlas, "Voidship armour", new Vector2(0, .5f), .006f, .003f, .72f, .24f),
                ShipSurface(template, atlas, "Scorched voidship iron", new Vector2(.5f, .5f), .008f, .002f, .82f, .28f),
                ShipSurface(template, atlas, "Oxblood cathedral plating", Vector2.zero, .008f, .003f, .42f, .24f),
                ShipSurface(template, atlas, "Tarnished voidship brass", new Vector2(.5f, 0), .012f, .001f, .76f, .32f),
                GetMaterials()[Amber], GetMaterials()[Cyan],
                ShipSurface(template, atlas, "Deep recesses", new Vector2(.5f, .5f), .008f, .002f, .6f, .2f)
            };
            return shipMaterials;
        }

        private static Material[] GetTenderMaterials()
        {
            if (tenderMaterials != null && tenderMaterials[0] != null) return tenderMaterials;
            var atlas = UnityEngine.Resources.Load<Texture2D>("Architecture/TenderSurfaceAtlas");
            var template = UnityEngine.Resources.Load<Material>("Architecture/BuildingSurface");
            var authoredTender = UnityEngine.Resources.Load<GameObject>("Architecture/TenderHull");
            var authoredSlots = authoredTender != null
                ? authoredTender.GetComponentInChildren<MeshRenderer>()?.sharedMaterials
                : null;
            if (atlas == null || template == null)
                throw new System.InvalidOperationException("Missing tender surface atlas or architecture shader material.");

            // Unity's FBX importer groups slots by its own material ordering. Build logical
            // photographic materials first, then align each submesh slot by its authored name.
            var logical = new[]
            {
                ShipSurface(template, atlas, "Tender primary armour", new Vector2(0, .5f), .018f, .006f, .25f, .42f),
                ShipSurface(template, atlas, "Tender engine machinery", new Vector2(.5f, .5f), .022f, .006f, .42f, .39f),
                ShipSurface(template, atlas, "Tender oxblood plating", Vector2.zero, .020f, .006f, .22f, .41f),
                ShipSurface(template, atlas, "Tender aged brass", new Vector2(.5f, 0), .028f, .005f, .38f, .40f),
                Emissive("Tender amber signal", new Color(.85f, .33f, .06f)),
                Emissive("Tender cyan exhaust", new Color(.01f, .63f, .72f)),
                ShipSurface(template, atlas, "Tender recessed iron", new Vector2(.5f, .5f), .024f, .003f, .28f, .37f)
            };
            if (authoredSlots == null) return logical; // Keep the procedural tender fallback usable without an FBX import.
            if (authoredSlots.Length != logical.Length)
                throw new System.InvalidOperationException("Tender FBX must retain all seven authored material slots.");
            tenderMaterials = new Material[authoredSlots.Length];
            for (var slot = 0; slot < authoredSlots.Length; slot++)
            {
                var logicalIndex = TenderMaterialIndex(authoredSlots[slot] != null ? authoredSlots[slot].name : string.Empty);
                if (logicalIndex < 0)
                    throw new System.InvalidOperationException("Unknown landing tender FBX material slot: "
                        + (authoredSlots[slot] != null ? authoredSlots[slot].name : "<null>"));
                tenderMaterials[slot] = logical[logicalIndex];
            }
            return tenderMaterials;
        }

        private static int TenderMaterialIndex(string name)
        {
            switch (name)
            {
                case "Tender primary armour": return 0;
                case "Tender engine machinery": return 1;
                case "Tender oxblood plating": return 2;
                case "Tender aged brass": return 3;
                case "Tender amber signal": return 4;
                case "Tender cyan exhaust": return 5;
                case "Tender recessed iron": return 6;
                default: return -1;
            }
        }
        private static Material ShipSurface(Material template, Texture2D atlas, string name, Vector2 quadrant,
            float scale, float relief, float metal, float smoothness)
        {
            var material = new Material(template) { name = name, enableInstancing = true };
            material.SetTexture("_MainTex", atlas);
            material.SetVector("_AtlasOffset", new Vector4(quadrant.x, quadrant.y, 0, 0));
            material.SetFloat("_SurfaceScale", scale);
            material.SetFloat("_Relief", relief);
            material.SetFloat("_Metallic", metal);
            material.SetFloat("_Glossiness", smoothness);
            material.SetFloat("_TintStrength", 0);
            return material;
        }

        private static Material Surface(string name, Color color, float metal, float gloss,
            Vector2 quadrant, float repeatsPerMetre, float relief)
        {
            // An asset reference under Resources retains the custom shader and atlas in players.
            var template = UnityEngine.Resources.Load<Material>("Architecture/BuildingSurface");
            if (template == null)
                throw new System.InvalidOperationException("Missing Resources/Architecture/BuildingSurface material.");
            var material = new Material(template) { name = name, color = color, enableInstancing = true };
            material.SetFloat("_Metallic", metal);
            material.SetFloat("_Glossiness", gloss);
            material.SetVector("_AtlasOffset", new Vector4(quadrant.x, quadrant.y, 0, 0));
            material.SetFloat("_SurfaceScale", repeatsPerMetre);
            material.SetFloat("_Relief", relief);
            material.SetFloat("_TintStrength", color == Color.white ? 0 : 1);
            return material;
        }

        private static Material Emissive(string name, Color color)
        {
            var material = new Material(Shader.Find("Standard")) { name = name, color = color * 0.24f, enableInstancing = true };
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 0.65f);
            material.SetFloat("_Glossiness", 0.78f);
            return material;
        }

        private sealed class Architecture
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<int>[] triangles = new List<int>[7];
            public Architecture() { for (var i = 0; i < 7; i++) triangles[i] = new List<int>(); }
            private static Vector3 Unit(Vector3 p) => Vector3.Scale(p, new Vector3(0.01f, 1f / 166f, 0.01f));

            public void Triangle(int material, Vector3 a, Vector3 b, Vector3 c)
            {
                a=Unit(a); b=Unit(b); c=Unit(c);
                var start=vertices.Count; var normal=Vector3.Cross(b-a,c-a).normalized;
                vertices.AddRange(new[] { a,b,c }); normals.AddRange(new[] { normal,normal,normal });
                uvs.AddRange(new[] { Vector2.zero,Vector2.up,Vector2.one });
                triangles[material].AddRange(new[] { start,start+1,start+2 });
            }

            public void Quad(int material, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                var start = vertices.Count;
                a = Unit(a); b = Unit(b); c = Unit(c); d = Unit(d);
                var cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude < 1e-12f) cross = Vector3.Cross(b - a, d - a);
                var normal = cross.sqrMagnitude > 1e-12f ? cross.normalized : Vector3.up;
                vertices.AddRange(new[] { a, b, c, d });
                for (var i = 0; i < 4; i++) normals.Add(normal);
                uvs.AddRange(new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right });
                triangles[material].AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }

            public void Box(int material, Vector3 center, Vector3 size) => OrientedBox(material, center, size, Quaternion.identity);
            public void Beam(int material, Vector3 a, Vector3 b, float width)
                => OrientedBox(material, (a + b) * 0.5f, new Vector3(width, (b - a).magnitude, width), Quaternion.FromToRotation(Vector3.up, b - a));
            private void OrientedBox(int material, Vector3 center, Vector3 size, Quaternion rotation)
            {
                if (material <= Iron && Mathf.Min(size.x, Mathf.Min(size.y, size.z)) > 14)
                {
                    ArchitectureGeometry.BevelBox(material, center, size, rotation, 0.7f, Quad);
                    return;
                }
                var p = new Vector3[8];
                for (var i = 0; i < 8; i++)
                    p[i] = center + rotation * Vector3.Scale(size * 0.5f,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Quad(material, p[0], p[2], p[3], p[1]); Quad(material, p[5], p[7], p[6], p[4]);
                Quad(material, p[4], p[6], p[2], p[0]); Quad(material, p[1], p[3], p[7], p[5]);
                Quad(material, p[2], p[6], p[7], p[3]); Quad(material, p[4], p[0], p[1], p[5]);
            }

            public void Gable(int material, Vector3 p, float width, float height, float depth)
                => Prism(material, p, new[] { new Vector2(-width / 2, 0), new Vector2(width / 2, 0), new Vector2(0, height) }, depth);
            public void Arch(int material, Vector3 p, float width, float height, float depth)
                => Prism(material, p, new[] { new Vector2(-width / 2, 0), new Vector2(width / 2, 0),
                    new Vector2(width / 2, height * 0.68f), new Vector2(0, height), new Vector2(-width / 2, height * 0.68f) }, depth);
            private void Prism(int material, Vector3 p, Vector2[] polygon, float depth)
            {
                var front = new Vector3[polygon.Length]; var back = new Vector3[polygon.Length];
                for (var i = 0; i < polygon.Length; i++)
                {
                    front[i] = p + new Vector3(polygon[i].x, polygon[i].y, -depth / 2);
                    back[i] = front[i] + Vector3.forward * depth;
                }
                for (var i = 1; i < polygon.Length - 1; i++)
                {
                    Quad(material, front[0], front[i + 1], front[i], front[i]);
                    Quad(material, back[0], back[i], back[i + 1], back[i + 1]);
                }
                for (var i = 0; i < polygon.Length; i++)
                {
                    var next = (i + 1) % polygon.Length;
                    Quad(material, front[i], front[next], back[next], back[i]);
                }
            }

            public void Cylinder(int material, Vector3 p, float bottom, float top, float height, int sides = 16)
            {
                // Round tanks and stacks retain their silhouette at close colony zoom.
                // Keep intentionally angular stone towers and pyramidal roof spires.
                var round = sides >= 8 && material != Stone && material != Roof;
                if (round) sides = Mathf.Max(sides, 24);
                for (var i = 0; i < sides; i++)
                {
                    var a = i * Mathf.PI * 2 / sides; var c = (i + 1) * Mathf.PI * 2 / sides;
                    var v = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); var w = new Vector3(Mathf.Cos(c), 0, Mathf.Sin(c));
                    Quad(material, p + v * bottom, p + v * top + Vector3.up * height,
                        p + w * top + Vector3.up * height, p + w * bottom);
                    if (round)
                    {
                        var slope = (bottom - top) / Mathf.Max(height, .001f);
                        var n0 = new Vector3(v.x * 100, slope * 166, v.z * 100).normalized;
                        var n1 = new Vector3(w.x * 100, slope * 166, w.z * 100).normalized;
                        var start = normals.Count - 4;
                        normals[start] = normals[start + 1] = n0;
                        normals[start + 2] = normals[start + 3] = n1;
                    }
                    Quad(material, p, p + v * bottom, p + w * bottom, p + w * bottom);
                    var peak = p + Vector3.up * height;
                    Quad(material, peak, peak + w * top, peak + v * top, peak + v * top);
                }
            }

            public Mesh Build(string id)
            {
                if (id == "LandingVessel")
                {
                    var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                    var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                    foreach (var vertex in vertices) { minimum = Vector3.Min(minimum, vertex); maximum = Vector3.Max(maximum, vertex); }
                    var extent = maximum - minimum;
                    var middle = (maximum + minimum) * .5f;
                    for (var i = 0; i < vertices.Count; i++)
                    {
                        var vertex = vertices[i];
                        vertices[i] = new Vector3((vertex.x - middle.x) / extent.x, (vertex.y - middle.y) / extent.y,
                            (vertex.z - middle.z) / extent.z);
                        var normal = normals[i];
                        normal = new Vector3(normal.x / extent.x, normal.y / extent.y, normal.z / extent.z);
                        normals[i] = normal.sqrMagnitude > .000001f ? normal.normalized : Vector3.up;
                    }
                }
                if (id == "ColonyShip")
                {
                    // Bake the asymmetric crash into the mesh so the game's tangent-aligned
                    // placement anchor, footprint and colony services remain stable.
                    var dimensions = LegacyShipDimensions;
                    var impactPose = Quaternion.Euler(.65f, 0, -5.5f);
                    var sink = Vector3.down * 65f;
                    for (var i = 0; i < vertices.Count; i++)
                    {
                        var metres = Vector3.Scale(vertices[i], dimensions);
                        metres = impactPose * metres + sink;
                        vertices[i] = new Vector3(metres.x / dimensions.x, metres.y / dimensions.y,
                            metres.z / dimensions.z);
                        // Vertices use normalised construction axes with a non-uniform
                        // metre scale. Apply the matching inverse-transpose to authored
                        // normals, retaining the rounded engine/pipe shading.
                        var n = normals[i];
                        n = impactPose * new Vector3(n.x / dimensions.x, n.y / dimensions.y, n.z / dimensions.z);
                        n = Vector3.Scale(n, dimensions);
                        normals[i] = n.sqrMagnitude > .000001f ? n.normalized : Vector3.up;
                    }
                    // Fit the posed longitudinal silhouette to the agreed 3 km length. This
                    // includes engine lips and bow details, and keeps preview/clearance aligned.
                    var minZ = float.PositiveInfinity; var maxZ = float.NegativeInfinity;
                    foreach (var vertex in vertices) { minZ = Mathf.Min(minZ, vertex.z); maxZ = Mathf.Max(maxZ, vertex.z); }
                    var extent = maxZ - minZ; var middle = (maxZ + minZ) * .5f;
                    for (var i = 0; i < vertices.Count; i++)
                    {
                        var vertex = vertices[i]; vertex.z = (vertex.z - middle) / extent; vertices[i] = vertex;
                        var normal = normals[i]; normal.z *= extent; normals[i] = normal.normalized;
                    }
                }
                var mesh = new Mesh { name = id + " gothic architecture", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs); mesh.subMeshCount = 7;
                for (var i = 0; i < 7; i++) mesh.SetTriangles(triangles[i], i);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
