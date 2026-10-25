using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace TweaksAndFixes.Modified
{
    internal class MainMenuM
    {
        public class VisualShip
        {
            public GameObject root;
            public ShipType shipType;
            public PartData hullData;
            public PlayerData nation;
            public List<List<MeshRenderer>> LODs = new();
            public float rollMax = 0;
            public float rollPeriod = -1;
            public float t = 0;
            public Vector2 size;

            public VisualShip(GameObject root, ShipType shipType, PartData hullData, PlayerData nation)
            {
                this.root = root;
                this.shipType = shipType;
                this.hullData = hullData;
                this.nation = nation;
            }

            public void Update()
            {
                var dt = Time.deltaTime;
                t = (t + dt) % rollPeriod;

                float rot = Mathf.Sin(t * 2 * Mathf.PI * (1.0f / rollPeriod)) * rollMax;

                root.transform.eulerAngles = new(
                    root.transform.eulerAngles.x,
                    root.transform.eulerAngles.y,
                    rot
                );
            }
        }

        public class CamLocation
        {
            public Vector3 pos;
            public Vector2 angle;
            public float distance;
            public PortLocation? require = null;

            public CamLocation(Vector3 pos, Vector2 angle, float distance, PortLocation? require)
            {
                this.pos = pos;
                this.angle = angle;
                this.distance = distance;
                this.require = require;
            }
        }

        public class PortLocation
        {
            public enum LocType
            {
                Tiny, Small, Medium, Large,
                LoadingDock, Anchorage,
                Path
            }

            public LocType type;
            public Vector3 pos1;
            public Vector3 pos2;
            public Vector2 maxSize;
            public float rot;
            public float skipChance = 0.1f;

            public PortLocation(LocType type, Vector3 pos, float rot, Vector2 maxSize, float skipChanmce = 0.1f)
            {
                this.type = type;
                this.pos1 = pos;
                this.rot = rot;
                this.maxSize = maxSize;
                this.skipChance = skipChanmce;
            }

            public PortLocation(LocType type, Vector3 pos1, Vector3 pos2, Vector2 maxSize, float skipChance = 0.1f)
            {
                this.type = type;
                this.pos1 = pos1;
                this.pos2 = pos2;
                this.maxSize = maxSize;
                this.skipChance = skipChance;
            }

            public bool Fits(VisualShip ship)
            {
                // Melon<TweaksAndFixes>.Logger.Msg($"Fits: {ship.root.name} | {maxSize} / {ship.size}");

                switch (type)
                {
                    case LocType.Tiny:
                    case LocType.Small:
                    case LocType.Medium:
                    case LocType.Large:
                    case LocType.LoadingDock:
                        if ((maxSize.x == -1 || maxSize.x >= ship.size.x)
                            && (maxSize.y == -1 || maxSize.y >= ship.size.y))
                            return true;
                        break;
                    default:
                        break;
                }

                return false;
            }

            public void Fill(VisualShip ship)
            {
                // Fixed pos
                if (type == LocType.Tiny
                    || type == LocType.Small
                    || type == LocType.Medium
                    || type == LocType.Large)
                {
                    // var c = ship.Clone(pos1, rot);
                    ship.root.transform.localPosition = pos1;
                    return;
                }

                ship.root.transform.localPosition = (pos1 + pos2) / 2;

                return;
            }
        }

        public static GameObject shipsContainer = new();
        public static List<VisualShip> ships = new();
        public static List<PortLocation> locations = new() {
            // "Wet" docks
            new(PortLocation.LocType.Tiny,      new Vector3(-250, 0, -27.5f),   0, new Vector2(17.5f, 165)),
            new(PortLocation.LocType.Small,     new Vector3(   0, 0, -10.0f),   0, new Vector2(35.0f, 180)),
            new(PortLocation.LocType.Medium,    new Vector3( 250, 0,  30.0f),   0, new Vector2(50.0f, 270)),
            new(PortLocation.LocType.Large,     new Vector3( 500, 0,  70.0f),   0, new Vector2(65.0f, 450)),

            // Loading Docks
            new(PortLocation.LocType.LoadingDock,   new Vector3(700, 0, 390), new Vector3(700, 0, -110), new Vector2(100, -1)),
        };
        public static List<CamLocation> cams = new() {
            new(new(-250, 0, -27.5f), new(20, 225), 180, locations[0]),
            new(new(   0, 0, -15.0f), new(20, 225), 200, locations[1]),
            new(new( 250, 0,  30.0f), new(20, 225), 220, locations[2]),
            new(new( 500, 0,  70.0f), new(20, 225), 250, locations[3]),
            new(new( 700, 0,  390  ), new(20, 225), 300, locations[4]),
        };

        // ★ [SMS.1.MVS] 2026-10-26 按 3.22（main 51d0518）重写 MakeVisualShip。
        //   旧实现（3.21.1）手工装配 hull/parts/sections/decor，约 430 行；
        //   3.22 改为"用游戏自己的装载流程"：Ship.Create → FromStore → LoadUnloadModel → Instantiate。
        //   这样封面船与游戏内实际船体完全一致（含分段/装饰/材质），也省掉大量重复代码。
        private static VisualShip? MakeVisualShip(Ship.Store design, bool roll = true)
        {
            if (!G.GameData.parts.ContainsKey(design.hullName)
                || !G.GameData.shipTypes.ContainsKey(design.shipType)
                || !G.GameData.players.ContainsKey(design.playerName))
            {
                return null;
            }

            PlayerController.Init(G.GameData.players[design.playerName]);

            var cc = CampaignController.Instance;
            cc.StartYear = design.YearCreated;

            foreach (var t in design.techs)
            {
                Technology tech = new();
                tech.data = G.GameData.technologies[t];
                tech.progress = 100;
                PlayerController.Instance.technologies.Add(tech);
            }

            var s = Ship.Create(null, null);
            s.FromStore(design, new Il2CppSystem.Nullable<Il2CppSystem.Guid>(s.id), null, PlayerController.Instance, true);
            s.LoadUnloadModel(true);
            var c = GameObject.Instantiate(s);
            c.transform.SetParent(shipsContainer, false);
            c.gameObject.active = true;

            VisualShip container = new(
                c.gameObject, G.GameData.shipTypes[design.shipType], G.GameData.parts[design.hullName], G.GameData.players[design.playerName]
            );
            container.size = new Vector2(s.hullSize.size.x, s.hullSize.size.z);

            return container;
        }

        private static bool inited = false;

        public static void ClearScene()
        {
            if (ships.Count == 1)
            {
                ships[0].root.TryDestroy(true);
                ships.Clear();
            }
        }

        public static void InitRandomScene()
        {
            /*
            Random Nation
            Filter positions by camera pos (optional)
            Grab 3-4 tiny, 2-3 small, 2 med, 1-2 large, 1-2 huge
            Loop over positions
             - Chance to skip
             - Randomly choose a ship that matches the size
             - Fill according to placement
            Update
             - Non-static: LODs
             - Static: Roll
             - Scene Elements:
               - Cranes
               - Lights?
               - Vehicles?
            */

            // Hide container in Ship.UnloadModel or EnterContstructor

            G.cam.enabled = false;
            G.cam.lookingAt = new();
            G.cam.distance = 200;
            G.cam.distanceDesired = 200;
            G.cam.rotationX = 20;
            G.cam.rotationY = 225;
            UiM.SettupMainMenuCam(G.cam);

            if (!inited)
            {
                shipsContainer.name = "DecorativeShips";
                shipsContainer.SetParent(
                    ModUtils.GetChildAtPath("Dock", Patch_SceneManager.LevelConstructor)
                );
                shipsContainer.transform.localPosition = Vector3.zero;
            }

            inited = true;

            if (ships.Count == 1)
            {
                ships[0].root.TryDestroy(true);
                ships.Clear();
            }

            string path = $"{Config._BasePath}/DecorativeShips";

            if (!Directory.Exists(path))
                return;

            var designs = Directory.GetFiles(path);

            if (designs.Length == 0)
                return;

            var dockyard = Patch_SceneManager.LevelConstructor.GetChild("Dock");
            var dockyardPos = dockyard.transform.localPosition;

            dockyard.transform.localPosition = Vector3.zero;

            VisualShip? s = null;
            int maxDepth = 10;

            do
            {
                if (maxDepth-- == 0)
                {
                    Melon<TweaksAndFixes>.Logger.Msg($"Failed to find valid design after 10 attempts!");
                    return;
                }

                var file = designs[UnityEngine.Random.Range(0, designs.Length)];

                var design = Util.DeserializeObjectByte<Ship.Store>(File.ReadAllBytes(file));
                Melon<TweaksAndFixes>.Logger.Msg($"Attempting to display {design.vesselName}");

                s = MakeVisualShip(design);
            }
            while (s == null);

            ships.Add(s);

            // s.root.transform.localPosition += new Vector3(0, 0, 10);

            foreach (var loc in locations)
            {
                if (!loc.Fits(s))
                    continue;

                loc.Fill(s);

                break;
            }

            // Melon<TweaksAndFixes>.Logger.Msg($"{s.size.y}");

            dockyard.transform.localPosition = dockyardPos;

            G.cam.enabled = false;
            G.cam.lookingAt = s.root.transform.position;
            G.cam.distance = s.size.y;
            G.cam.distanceDesired = s.size.y;
            G.cam.rotationX = 25;
            G.cam.rotationY = 225;
            UiM.SettupMainMenuCam(G.cam);

            // var design1 = G.GameData.sharedDesignsPerNation["usa"][0].Item1;
            // var design2 = G.GameData.sharedDesignsPerNation["usa"][1].Item1;
            // 
            // MakeVisualShip(design1, new(0,0,0), 0);
            // MakeVisualShip(design2, new(-50,0,-150), 90);
        }
    }
}
