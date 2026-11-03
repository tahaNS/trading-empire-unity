using System;
using System.Collections.Generic;
using System.Linq;
using TradingEmpire.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TradingEmpire.Terminal
{
    /// <summary>
    /// The 3D city: ground, streets, buildings as placeholder boxes (like the web version), a placement preview and an
    /// isometric camera with pan and zoom. It only reads <see cref="CityGame"/> state; clicks go through its methods.
    /// </summary>
    public sealed class CityView : MonoBehaviour
    {
        public CityGame City { get; private set; }
        public Camera Camera { get; private set; }

        Transform _root, _groundRoot, _buildingRoot, _ghostRoot;
        readonly Dictionary<string, GameObject> _views = new Dictionary<string, GameObject>();
        readonly Dictionary<string, string> _signature = new Dictionary<string, string>();
        readonly Dictionary<int, Material> _materials = new Dictionary<int, Material>();
        Material _baseMaterial;
        GameObject _ownedPlane;
        int _landTierDrawn = -1;
        bool _dirty = true;
        float _nextSiteRefresh;
        Vector3 _focus = Vector3.zero;
        float _zoom = 13f;
        Vector3 _dragLast;
        public bool Interactive = true;

        public static CityView Create(CityGame city, Camera camera)
        {
            var go = new GameObject("City");
            var view = go.AddComponent<CityView>();
            view.City = city;
            view.Camera = camera;
            view.Setup();
            city.Changed += view.MarkDirty;
            return view;
        }

        void MarkDirty() { _dirty = true; }
        void OnDestroy() { if (City != null) City.Changed -= MarkDirty; }

        void Setup()
        {
            _root = transform;
            _groundRoot = new GameObject("Ground").transform; _groundRoot.SetParent(_root, false);
            _buildingRoot = new GameObject("Buildings").transform; _buildingRoot.SetParent(_root, false);
            _ghostRoot = new GameObject("Ghost").transform; _ghostRoot.SetParent(_root, false);

            // the default material of a primitive is a lit, always-available shader: clone it for every colour
            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _baseMaterial = probe.GetComponent<Renderer>().sharedMaterial;
            DestroyImmediate(probe);

            BuildGround();
            SetupCamera();
            SetupLight();
        }

        void SetupCamera()
        {
            Camera.orthographic = true;
            Camera.orthographicSize = _zoom;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(0.55f, 0.74f, 0.88f);
            Camera.transform.rotation = Quaternion.Euler(33f, 45f, 0f);
            ApplyCamera();
        }

        void ApplyCamera()
        {
            Camera.transform.position = _focus - Camera.transform.forward * 60f;
            Camera.orthographicSize = _zoom;
        }

        void SetupLight()
        {
            var light = UnityEngine.Object.FindFirstObjectByType<Light>();
            if (light == null)
            {
                light = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
                light.type = LightType.Directional;
            }
            light.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.96f, 0.88f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.66f);
        }

        // ---------------------------------------------------------------- materials / primitives

        Material MaterialFor(Color c)
        {
            int key = ((int)(c.r * 255) << 24) ^ ((int)(c.g * 255) << 16) ^ ((int)(c.b * 255) << 8) ^ (int)(c.a * 255);
            if (!_materials.TryGetValue(key, out var m))
            {
                m = new Material(_baseMaterial) { color = c };
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.05f);
                _materials[key] = m;
            }
            return m;
        }

        GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = MaterialFor(color);
            return go;
        }

        GameObject Ball(Transform parent, Vector3 center, float diameter, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = Vector3.one * diameter;
            go.GetComponent<Renderer>().sharedMaterial = MaterialFor(color);
            return go;
        }

        // ---------------------------------------------------------------- ground and streets

        void BuildGround()
        {
            int w = City.Grid.Width, h = City.Grid.Height;
            Box(_groundRoot, "Base", new Vector3(0, -0.6f, 0), new Vector3(w + 4, 1f, h + 4), new Color(0.30f, 0.42f, 0.30f));
            // a soft chequer on the whole map so the grid reads, darker outside your land
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = new Cell(x, y);
                    var (wx, wz) = City.Grid.CellToWorld(c);
                    if (Roads.IsRoadCell(c))
                    {
                        Box(_groundRoot, "Road", new Vector3(wx, 0.01f, wz), new Vector3(1f, 0.04f, 1f), new Color(0.22f, 0.23f, 0.26f));
                        var kind = Roads.KindOf(c);
                        if (kind != Roads.Kind.Cross)
                        {
                            bool ns = kind == Roads.Kind.NorthSouth;
                            Box(_groundRoot, "Lane", new Vector3(wx, 0.04f, wz), new Vector3(ns ? 0.06f : 0.4f, 0.01f, ns ? 0.4f : 0.06f), new Color(0.85f, 0.82f, 0.5f));
                        }
                    }
                }
            RebuildLand();
        }

        void RebuildLand()
        {
            if (_ownedPlane != null) Ui.Kill(_ownedPlane);
            var a = City.Grid.Buildable;
            var (x0, z0) = City.Grid.CellToWorld(new Cell(a.X, a.Y));
            float cx = x0 + (a.W - 1) / 2f, cz = z0 + (a.H - 1) / 2f;
            // owned land: a lighter lawn under the streets
            _ownedPlane = new GameObject("Owned land");
            _ownedPlane.transform.SetParent(_groundRoot, false);
            for (int y = a.Y; y < a.Y + a.H; y++)
                for (int x = a.X; x < a.X + a.W; x++)
                {
                    var c = new Cell(x, y);
                    if (Roads.IsRoadCell(c)) continue;
                    var (wx, wz) = City.Grid.CellToWorld(c);
                    bool alt = (x + y) % 2 == 0;
                    Box(_ownedPlane.transform, "Lawn", new Vector3(wx, 0.0f, wz), new Vector3(1f, 0.03f, 1f), alt ? new Color(0.50f, 0.74f, 0.42f) : new Color(0.46f, 0.70f, 0.39f));
                }
            _landTierDrawn = City.State.LandTier;
        }

        // ---------------------------------------------------------------- buildings

        static string SignatureOf(BuildingState b, TradingRecord record, bool onFire)
        {
            int level = Landmarks.EffectiveLevel(b.Level, Buildings.Get(b.DefId), record);
            return b.Status + ":" + level + ":" + onFire + ":" + (b.Status == BuildStatus.Building ? 0 : 1);
        }

        void SyncBuildings()
        {
            var state = City.State;
            var live = new HashSet<string>(state.Buildings.Select(b => b.Id));
            foreach (var id in _views.Keys.Where(id => !live.Contains(id)).ToList()) { Ui.Kill(_views[id]); _views.Remove(id); _signature.Remove(id); }
            foreach (var b in state.Buildings)
            {
                bool fire = state.Events.Any(e => e.Kind == EventKind.Fire && e.BuildingId == b.Id);
                // buildings under construction grow with time, so they are refreshed every sync
                string sig = SignatureOf(b, state.Record, fire);
                if (b.Status == BuildStatus.Building || !_signature.TryGetValue(b.Id, out var old) || old != sig)
                {
                    if (_views.TryGetValue(b.Id, out var prev)) Ui.Kill(prev);
                    _views[b.Id] = BuildBuilding(b, fire, state);
                    _signature[b.Id] = sig;
                }
            }
        }

        GameObject BuildBuilding(BuildingState b, bool onFire, CityState state)
        {
            var def = Buildings.Get(b.DefId);
            var root = new GameObject("Building " + b.Id).transform;
            root.SetParent(_buildingRoot, false);
            var fp = CityGrid.Rotated(def.Footprint, b.Rotation);
            var (cx, cz) = City.Grid.FootprintCenter(b.Origin, def.Footprint, b.Rotation);
            root.localPosition = new Vector3(cx, 0, cz);
            int level = Landmarks.EffectiveLevel(b.Level, def, state.Record);
            Color baseColor = ColorOf(def.Color);
            float height = def.Height * (1f + 0.22f * (level - 1));
            float w = fp.W - 0.16f, d = fp.H - 0.16f;

            if (b.Status == BuildStatus.Building)
            {
                double progress = Math.Max(0.05, Math.Min(1, (City.Clock.Time - b.BuiltAt) / (double)Math.Max(1, b.CompletesAt - b.BuiltAt)));
                Box(root, "Site", new Vector3(0, 0.05f, 0), new Vector3(fp.W - 0.05f, 0.1f, fp.H - 0.05f), new Color(0.62f, 0.55f, 0.42f));
                float hh = (float)(height * progress);
                Box(root, "Frame", new Vector3(0, hh / 2, 0), new Vector3(w, hh, d), Color.Lerp(baseColor, new Color(0.7f, 0.7f, 0.7f), 0.55f));
                Box(root, "Crane", new Vector3(w / 2 - 0.1f, height * 0.5f + 0.4f, d / 2 - 0.1f), new Vector3(0.08f, height + 0.8f, 0.08f), new Color(0.95f, 0.75f, 0.2f));
                return root.gameObject;
            }

            if (onFire) baseColor = Color.Lerp(baseColor, new Color(0.2f, 0.12f, 0.1f), 0.7f);

            if (def.Category == BuildingCategory.Recreation)
            {
                Box(root, "Green", new Vector3(0, 0.08f, 0), new Vector3(fp.W - 0.1f, 0.16f, fp.H - 0.1f), baseColor);
                if (def.Id == "plaza") Box(root, "Fountain", new Vector3(0, 0.3f, 0), new Vector3(1.1f, 0.5f, 1.1f), new Color(0.55f, 0.75f, 0.9f));
                int trees = 2 + level;
                var rnd = new System.Random(b.Id.GetHashCode());
                for (int i = 0; i < trees; i++)
                {
                    float tx = ((float)rnd.NextDouble() - 0.5f) * (fp.W - 0.6f), tz = ((float)rnd.NextDouble() - 0.5f) * (fp.H - 0.6f);
                    Box(root, "Trunk", new Vector3(tx, 0.3f, tz), new Vector3(0.1f, 0.6f, 0.1f), new Color(0.4f, 0.28f, 0.18f));
                    Ball(root, new Vector3(tx, 0.8f, tz), 0.7f, new Color(0.25f, 0.6f, 0.28f));
                }
                return root.gameObject;
            }

            Box(root, "Body", new Vector3(0, height / 2, 0), new Vector3(w, height, d), baseColor);
            // a darker roof slab and a few lit windows so the boxes read as buildings
            Color roof = Color.Lerp(baseColor, Color.black, 0.35f);
            Box(root, "Roof", new Vector3(0, height + 0.06f, 0), new Vector3(w + 0.08f, 0.12f, d + 0.08f), roof);
            if (level >= 2) Box(root, "Penthouse", new Vector3(0, height + 0.3f, 0), new Vector3(w * 0.5f, 0.35f, d * 0.5f), roof);
            if (level >= 3) Box(root, "Spire", new Vector3(0, height + 0.8f, 0), new Vector3(0.12f, 0.9f, 0.12f), new Color(0.85f, 0.85f, 0.9f));
            var win = new Color(1f, 0.93f, 0.6f);
            int rows = Mathf.Max(1, (int)(height / 1.1f));
            for (int r = 0; r < rows; r++)
            {
                float y = 0.5f + r * 1.1f;
                if (y > height - 0.3f) break;
                for (int i = 0; i < fp.W; i++)
                {
                    float x = -fp.W / 2f + 0.5f + i;
                    Box(root, "Win", new Vector3(x, y, -d / 2 - 0.005f), new Vector3(0.4f, 0.4f, 0.02f), win);
                    Box(root, "Win", new Vector3(w / 2 + 0.005f, y, -fp.H / 2f + 0.5f + i), new Vector3(0.02f, 0.4f, 0.4f), win);
                }
            }
            if (onFire) Ball(root, new Vector3(0, height + 0.5f, 0), 0.9f, new Color(1f, 0.4f, 0.1f));
            return root.gameObject;
        }

        static Color ColorOf(int rgb) { return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f); }

        // ---------------------------------------------------------------- placement preview, selection

        void SyncGhost()
        {
            for (int i = _ghostRoot.childCount - 1; i >= 0; i--) Ui.Kill(_ghostRoot.GetChild(i).gameObject);
            var p = City.State.Placement;
            var hover = City.State.Hover;
            var sel = City.State.SelectedId;
            if (p != null && hover != null)
            {
                var def = Buildings.Get(p.DefId);
                var fp = CityGrid.Rotated(def.Footprint, p.Rotation);
                var (cx, cz) = City.Grid.FootprintCenter(hover.Origin, def.Footprint, p.Rotation);
                Color col = hover.Check.Ok ? new Color(0.3f, 0.9f, 0.45f) : new Color(0.95f, 0.3f, 0.3f);
                Box(_ghostRoot, "Ghost tile", new Vector3(cx, 0.1f, cz), new Vector3(fp.W, 0.06f, fp.H), col);
                Box(_ghostRoot, "Ghost", new Vector3(cx, def.Height * 0.5f + 0.1f, cz), new Vector3(fp.W - 0.3f, def.Height, fp.H - 0.3f), Color.Lerp(ColorOf(def.Color), col, 0.55f));
            }
            if (sel != null)
            {
                var b = City.State.Buildings.FirstOrDefault(x => x.Id == sel);
                if (b != null)
                {
                    var def = Buildings.Get(b.DefId);
                    var fp = CityGrid.Rotated(def.Footprint, b.Rotation);
                    var (cx, cz) = City.Grid.FootprintCenter(b.Origin, def.Footprint, b.Rotation);
                    Box(_ghostRoot, "Selection", new Vector3(cx, 0.08f, cz), new Vector3(fp.W + 0.3f, 0.05f, fp.H + 0.3f), new Color(1f, 0.85f, 0.3f));
                }
            }
        }

        // ---------------------------------------------------------------- per frame

        void Update()
        {
            if (City.State.LandTier != _landTierDrawn) RebuildLand();
            if (!Interactive) { if (_dirty) Sync(); return; }
            HandleCamera();
            HandleMouse();
            if (_dirty) Sync();
            else if (Time.unscaledTime > _nextSiteRefresh && City.State.Buildings.Any(b => b.Status == BuildStatus.Building))
            {
                // buildings under construction grow smoothly, a few times a second
                _nextSiteRefresh = Time.unscaledTime + 0.25f;
                SyncBuildings();
            }
        }

        void Sync()
        {
            _dirty = false;
            SyncBuildings();
            SyncGhost();
        }

        /// <summary>Redraw now (used by the screenshot tool, where Update does not run).</summary>
        public void SyncNow() { if (City.State.LandTier != _landTierDrawn) RebuildLand(); Sync(); ApplyCamera(); }

        public void SetCamera(Vector3 focus, float zoom) { _focus = focus; _zoom = zoom; ApplyCamera(); }

        void HandleCamera()
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f && !PointerOverUi()) { _zoom = Mathf.Clamp(_zoom * (scroll > 0 ? 0.9f : 1.1f), 5f, 24f); }
            var right = Camera.transform.right; right.y = 0; right.Normalize();
            var fwd = Camera.transform.forward; fwd.y = 0; fwd.Normalize();
            Vector3 move = Vector3.zero;
            bool typing = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null && EventSystem.current.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>() != null;
            if (!typing)
            {
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) move += fwd;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) move -= fwd;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move += right;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move -= right;
            }
            _focus += move * (_zoom * 0.9f * Time.unscaledDeltaTime);
            if (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) _dragLast = Input.mousePosition;
            if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
            {
                var delta = Input.mousePosition - _dragLast;
                _dragLast = Input.mousePosition;
                float k = _zoom * 2f / Screen.height;
                _focus -= (right * delta.x + fwd * delta.y / Mathf.Cos(33f * Mathf.Deg2Rad)) * k;
            }
            float limit = City.Grid.Width * 0.6f;
            _focus = new Vector3(Mathf.Clamp(_focus.x, -limit, limit), 0, Mathf.Clamp(_focus.z, -limit, limit));
            ApplyCamera();
        }

        static bool PointerOverUi() { return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(); }

        bool CellUnderMouse(out Cell cell)
        {
            cell = default;
            var ray = Camera.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (!plane.Raycast(ray, out float t)) return false;
            var hit = ray.GetPoint(t);
            cell = City.Grid.WorldToCell(hit.x, hit.z);
            return City.Grid.IsInBounds(cell);
        }

        void HandleMouse()
        {
            var placement = City.State.Placement;
            if (PointerOverUi()) { if (placement != null) City.SetHover(null); return; }
            bool over = CellUnderMouse(out var cell);
            if (placement != null)
            {
                var def = Buildings.Get(placement.DefId);
                // the cursor cell is the footprint's centre cell, so a big building follows the mouse naturally
                var fp = CityGrid.Rotated(def.Footprint, placement.Rotation);
                var origin = new Cell(cell.X - fp.W / 2, cell.Y - fp.H / 2);
                City.SetHover(over ? origin : (Cell?)null);
                if (Input.GetKeyDown(KeyCode.R)) City.Rotate();
                if (Input.GetKeyDown(KeyCode.Escape)) City.Escape();
                if (Input.GetMouseButtonDown(0) && over) City.TryPlace(origin);
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.Escape)) City.Escape();
                if (Input.GetMouseButtonDown(0) && over) City.SelectAt(cell);
            }
        }
    }
}
