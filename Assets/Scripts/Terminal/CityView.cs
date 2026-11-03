using System;
using System.Collections.Generic;
using System.Linq;
using TradingEmpire.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TradingEmpire.Terminal
{
    /// <summary>
    /// The 3D city, ported from the web version's look: detailed low-poly buildings (lit windows, awnings, rooftop units, fountains), paved lots in
    /// district colours, streets with sidewalks, lamps and zebra crossings, trees, cars and pedestrians, a sun with soft shadows, construction
    /// sites that grow with scaffolding, and an isometric camera with pan and zoom. It only reads <see cref="CityGame"/>; clicks go through its methods.
    /// Everything is built in the web version's coordinates under a root that is mirrored on z, so the picture matches the browser game.
    /// </summary>
    public sealed class CityView : MonoBehaviour
    {
        const float SiteMinScale = 0.15f, BuildAnimSeconds = 0.3f, PopSeconds = 0.7f;
        static readonly Color Sky = new Color(0.749f, 0.89f, 0.961f);

        public CityGame City { get; private set; }
        public Camera Camera { get; private set; }
        public bool Interactive = true;

        sealed class BuildingView
        {
            public GameObject Model, Lot, Scaffold;
            public int Level;
            public bool Built;
            public float SpawnAt = -1, PopAt = -1;
            public Mesh ModelMesh, LotMesh, ScaffoldMesh;
        }

        Transform _root, _static, _buildings, _ghostRoot;
        Material _mat;
        GameObject _ground, _plot, _props;
        TrafficSystem _traffic;
        readonly Dictionary<string, BuildingView> _views = new Dictionary<string, BuildingView>();
        readonly Dictionary<string, Mesh> _modelCache = new Dictionary<string, Mesh>();
        MaterialPropertyBlock _tintBlock;
        int _landTierDrawn = -1;
        bool _dirty = true, _synced;
        float _nextSiteRefresh;
        Vector3 _focus = Vector3.zero;
        float _zoom = 9f;
        Vector3 _dragLast;
        double _moveTime;

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

        // ---------------------------------------------------------------- setup

        void Setup()
        {
            // everything below is in the web version's coordinates; mirroring z here makes the picture match the browser game
            _tintBlock = new MaterialPropertyBlock();
            _root = new GameObject("World").transform;
            _root.SetParent(transform, false);
            _root.localScale = new Vector3(1, 1, -1);
            _static = new GameObject("Static").transform; _static.SetParent(_root, false);
            _buildings = new GameObject("Buildings").transform; _buildings.SetParent(_root, false);
            _ghostRoot = new GameObject("Ghost").transform; _ghostRoot.SetParent(_root, false);

            var shader = Resources.Load<Shader>("CityLit");
            _mat = shader != null && shader.isSupported ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));

            _ground = Mesh("Ground", CityScenery.Ground(City.Grid), true, false); // the ground only receives shadows (casting them onto itself causes streaks)
            RebuildLand();
            SetupCamera();
            SetupLight();
        }

        GameObject Mesh(string name, Mesh mesh, bool receive, bool cast, Transform parent = null)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent ?? _static, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.receiveShadows = receive;
            r.shadowCastingMode = cast ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        void RebuildLand()
        {
            if (_plot != null) Ui.Kill(_plot);
            if (_props != null) Ui.Kill(_props);
            if (_traffic != null) _traffic.Destroy();
            _plot = Mesh("Plot", CityScenery.Plot(City.Grid), true, false);
            _props = Mesh("Props", CityScenery.Props(City.Grid), true, true);
            _traffic = new TrafficSystem(City.Grid, _static, _mat, City.Grid.Buildable);
            _landTierDrawn = City.State.LandTier;
        }

        void SetupCamera()
        {
            Camera.orthographic = true;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = Sky;
            Camera.nearClipPlane = 0.1f;
            Camera.farClipPlane = 300f;
            // looking at the city from the web version's (1,1,1) corner; z is mirrored, so in the world that is (1,1,-1)
            Camera.transform.rotation = Quaternion.Euler(35.264f, 315f, 0f);
            ApplyCamera();
        }

        void ApplyCamera()
        {
            Camera.transform.position = _focus - Camera.transform.forward * 80f;
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
            // the web sun stands at (15, 25, 10); with z mirrored the light travels towards (-15, -25, +10)
            light.transform.rotation = Quaternion.LookRotation(new Vector3(-15f, -25f, 10f).normalized);
            light.color = new Color(1f, 0.94f, 0.84f);
            light.intensity = 0.95f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.6f;
            light.shadowBias = 0.06f;
            light.shadowNormalBias = 0.8f;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowDistance = 100f;
            QualitySettings.shadowCascades = 1;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;
            // the web hemisphere light: sky 0xdff1ff over ground 0x6f8d52
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.875f, 0.945f, 1f) * 0.62f;
            RenderSettings.ambientEquatorColor = new Color(0.66f, 0.76f, 0.7f) * 0.6f;
            RenderSettings.ambientGroundColor = new Color(0.435f, 0.553f, 0.322f) * 0.62f;
        }

        // ---------------------------------------------------------------- buildings

        Mesh ModelFor(BuildingDefinition def, int level, int seed)
        {
            // the colour of a building varies with its seed, so models are cached per (type, level, seed)
            string key = def.Id + ":" + level + ":" + seed;
            if (!_modelCache.TryGetValue(key, out var mesh))
            {
                mesh = BuildingModels.Build(def, level, seed).ToMesh(def.Id + " L" + level);
                _modelCache[key] = mesh;
            }
            return mesh;
        }

        void SyncBuildings(bool animate)
        {
            var state = City.State;
            var live = new HashSet<string>(state.Buildings.Select(b => b.Id));
            foreach (var id in _views.Keys.Where(id => !live.Contains(id)).ToList()) { DestroyView(_views[id]); _views.Remove(id); }
            foreach (var b in state.Buildings)
            {
                var def = Buildings.Get(b.DefId);
                int level = Landmarks.EffectiveLevel(b.Level, def, state.Record);
                bool built = b.Status == BuildStatus.Complete;
                bool onFire = state.Events.Any(e => e.Kind == EventKind.Fire && e.BuildingId == b.Id);
                _views.TryGetValue(b.Id, out var v);
                // an upgrade (or a landmark's new trading level) replaces the model
                if (v != null && v.Level != level) { Ui.Kill(v.Model); v.Model = null; }
                if (v == null)
                {
                    v = new BuildingView { Level = level, Built = built };
                    _views[b.Id] = v;
                    var (cx, cz) = City.Grid.FootprintCenter(b.Origin, def.Footprint, b.Rotation);
                    var fp = CityGrid.Rotated(def.Footprint, b.Rotation);
                    v.LotMesh = CityScenery.Lot(def.District, fp.W, fp.H, built);
                    v.Lot = Mesh("Lot " + b.Id, v.LotMesh, true, false, _buildings);
                    v.Lot.transform.localPosition = new Vector3(cx, 0.01f, cz);
                    if (animate && _synced) v.SpawnAt = Time.unscaledTime;
                }
                if (v.Model == null)
                {
                    var (cx, cz) = City.Grid.FootprintCenter(b.Origin, def.Footprint, b.Rotation);
                    v.Level = level;
                    v.ModelMesh = ModelFor(def, level, BuildingModels.SeedOf(b.Id));
                    v.Model = Mesh("Building " + b.Id, v.ModelMesh, true, true, _buildings);
                    v.Model.transform.localPosition = new Vector3(cx, 0f, cz);
                    v.Model.transform.localRotation = Quaternion.Euler(0f, -b.Rotation * 90f, 0f);
                    if (!animate || !_synced) v.SpawnAt = -1;
                }
                // a finished site: bare earth becomes the district's paving, with a little bounce
                if (built && !v.Built)
                {
                    v.Built = true;
                    var (cx, cz) = City.Grid.FootprintCenter(b.Origin, def.Footprint, b.Rotation);
                    var fp = CityGrid.Rotated(def.Footprint, b.Rotation);
                    v.LotMesh = CityScenery.Lot(def.District, fp.W, fp.H, true);
                    v.Lot.GetComponent<MeshFilter>().sharedMesh = v.LotMesh;
                    if (animate && _synced) v.PopAt = Time.unscaledTime;
                    if (v.Scaffold != null) { Ui.Kill(v.Scaffold); v.Scaffold = null; }
                }
                if (!built && v.Scaffold == null)
                {
                    var fp = CityGrid.Rotated(def.Footprint, b.Rotation);
                    v.ScaffoldMesh = CityScenery.Scaffold(fp.W * 0.9f, fp.H * 0.9f, def.Height * 1.1f);
                    v.Scaffold = Mesh("Scaffold " + b.Id, v.ScaffoldMesh, false, false, _buildings);
                    var (cx, cz) = City.Grid.FootprintCenter(b.Origin, def.Footprint, b.Rotation);
                    v.Scaffold.transform.localPosition = new Vector3(cx, 0f, cz);
                }
                // a fire darkens the building
                var r = v.Model.GetComponent<MeshRenderer>();
                if (onFire) { _tintBlock.SetColor("_Tint", new Color(0.2f, 0.12f, 0.1f)); _tintBlock.SetFloat("_TintAmt", 0.7f); r.SetPropertyBlock(_tintBlock); }
                else r.SetPropertyBlock(null);
            }
            _synced = true;
            ApplyScales(animate);
        }

        void DestroyView(BuildingView v)
        {
            if (v.Model != null) Ui.Kill(v.Model);
            if (v.Lot != null) Ui.Kill(v.Lot);
            if (v.Scaffold != null) Ui.Kill(v.Scaffold);
        }

        /// <summary>The height of each model: growing with construction progress, the pop-in when a site is created, and the finished bounce.</summary>
        void ApplyScales(bool animate)
        {
            float now = Time.unscaledTime;
            double simNow = City.Clock.Time;
            foreach (var b in City.State.Buildings)
            {
                if (!_views.TryGetValue(b.Id, out var v) || v.Model == null) continue;
                float target = 1f;
                if (b.Status == BuildStatus.Building)
                {
                    double k = (simNow - b.BuiltAt) / Math.Max(b.CompletesAt - b.BuiltAt, 1.0);
                    target = SiteMinScale + (1f - SiteMinScale) * (float)Math.Min(Math.Max(k, 0), 1);
                }
                float y = target;
                if (animate && v.SpawnAt >= 0)
                {
                    float k = Mathf.Min((now - v.SpawnAt) / BuildAnimSeconds, 1f);
                    y = target * (0.05f + 0.95f * (1f - Mathf.Pow(1f - k, 3f)));
                    if (k >= 1f) v.SpawnAt = -1;
                }
                float squash = 0f;
                if (animate && v.PopAt >= 0)
                {
                    float k = (now - v.PopAt) / PopSeconds;
                    if (k >= 1f) v.PopAt = -1; else squash = Mathf.Sin(k * Mathf.PI * 3f) * (1f - k) * 0.14f;
                }
                v.Model.transform.localScale = new Vector3(1f - squash * 0.5f, y * (1f + squash), 1f - squash * 0.5f);
                if (v.Scaffold != null) v.Scaffold.transform.localScale = new Vector3(1f, Mathf.Max(target, 0.2f), 1f);
            }
        }

        /// <summary>The world position of a building's top (for floating text).</summary>
        public Vector3 TopOf(string buildingId)
        {
            var b = City.State.Buildings.FirstOrDefault(x => x.Id == buildingId);
            if (b == null) return Vector3.zero;
            var def = Buildings.Get(b.DefId);
            var (cx, cz) = City.Grid.FootprintCenter(b.Origin, def.Footprint, b.Rotation);
            return _root.TransformPoint(new Vector3(cx, def.Height + 0.6f, cz));
        }

        // ---------------------------------------------------------------- placement preview, selection

        void SyncGhost()
        {
            for (int i = _ghostRoot.childCount - 1; i >= 0; i--) Ui.Kill(_ghostRoot.GetChild(i).gameObject);
            var p = City.State.Placement;
            var hover = City.State.Hover;
            if (p != null && hover != null)
            {
                var def = Buildings.Get(p.DefId);
                var fp = CityGrid.Rotated(def.Footprint, p.Rotation);
                var (cx, cz) = City.Grid.FootprintCenter(hover.Origin, def.Footprint, p.Rotation);
                var col = hover.Check.Ok ? new Color(0.24f, 0.86f, 0.52f) : new Color(0.9f, 0.28f, 0.31f);
                var tile = new MeshBuilder();
                tile.Box(fp.W, 0.05f, fp.H, 0, 0, 0, col, 0, null, 0, 0, 0, 1, 1f);
                var tileGo = Mesh("Ghost tile", tile.ToMesh("tile"), false, false, _ghostRoot);
                tileGo.transform.localPosition = new Vector3(cx, 0.08f, cz);
                var model = Mesh("Ghost", ModelFor(def, 1, 0), false, false, _ghostRoot);
                model.transform.localPosition = new Vector3(cx, 0.06f, cz);
                model.transform.localRotation = Quaternion.Euler(0f, -p.Rotation * 90f, 0f);
                _tintBlock.SetColor("_Tint", col); _tintBlock.SetFloat("_TintAmt", 0.5f);
                model.GetComponent<MeshRenderer>().SetPropertyBlock(_tintBlock);
            }
            var sel = City.State.SelectedId;
            if (sel != null)
            {
                var b = City.State.Buildings.FirstOrDefault(x => x.Id == sel);
                if (b != null)
                {
                    var def = Buildings.Get(b.DefId);
                    var fp = CityGrid.Rotated(def.Footprint, b.Rotation);
                    var (cx, cz) = City.Grid.FootprintCenter(b.Origin, def.Footprint, b.Rotation);
                    var ring = new MeshBuilder();
                    var gold = new Color(1f, 0.85f, 0.3f);
                    float w = fp.W + 0.25f, d = fp.H + 0.25f, t = 0.07f;
                    ring.Box(w, 0.05f, t, 0, 0, d / 2, gold, 0, null, 0, 0, 0, 1, 1f); ring.Box(w, 0.05f, t, 0, 0, -d / 2, gold, 0, null, 0, 0, 0, 1, 1f);
                    ring.Box(t, 0.05f, d, w / 2, 0, 0, gold, 0, null, 0, 0, 0, 1, 1f); ring.Box(t, 0.05f, d, -w / 2, 0, 0, gold, 0, null, 0, 0, 0, 1, 1f);
                    var go = Mesh("Selection", ring.ToMesh("selection"), false, false, _ghostRoot);
                    go.transform.localPosition = new Vector3(cx, 0.07f, cz);
                }
            }
        }

        // ---------------------------------------------------------------- per frame

        void Update()
        {
            if (City.State.LandTier != _landTierDrawn) RebuildLand();
            if (_traffic != null) _traffic.Animate(Time.unscaledDeltaTime, City.Clock.Speed > 0 && Interactive);
            if (Interactive) { HandleCamera(); HandleMouse(); }
            if (_dirty) Sync();
            else if (_views.Count > 0) ApplyScales(true);
        }

        void Sync()
        {
            _dirty = false;
            SyncBuildings(true);
            SyncGhost();
        }

        /// <summary>Redraw now with no animation (used by the screenshot tool, where Update does not run).</summary>
        public void SyncNow()
        {
            if (City.State.LandTier != _landTierDrawn) RebuildLand();
            _dirty = false;
            SyncBuildings(false);
            SyncGhost();
            ApplyCamera();
        }

        public void SetCamera(Vector3 focus, float zoom) { _focus = focus; _zoom = zoom; ApplyCamera(); }

        // ---------------------------------------------------------------- camera and mouse

        void HandleCamera()
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f && !PointerOverUi()) _zoom = Mathf.Clamp(_zoom * (scroll > 0 ? 0.9f : 1.1f), 4f, 28f);
            var right = Camera.transform.right; right.y = 0; right.Normalize();
            var fwd = Camera.transform.forward; fwd.y = 0; fwd.Normalize();
            Vector3 move = Vector3.zero;
            var es = EventSystem.current;
            bool typing = es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>() != null;
            if (!typing)
            {
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) move += fwd;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) move -= fwd;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move += right;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move -= right;
            }
            _focus += move * (_zoom * 1.2f * Time.unscaledDeltaTime);
            if (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) _dragLast = Input.mousePosition;
            if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
            {
                var delta = Input.mousePosition - _dragLast;
                _dragLast = Input.mousePosition;
                float k = _zoom * 2f / Screen.height;
                _focus -= (right * delta.x + fwd * delta.y / Mathf.Sin(35.264f * Mathf.Deg2Rad)) * k;
            }
            float limit = City.Grid.Width * 0.5f - 3f;
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
            var local = _root.InverseTransformPoint(ray.GetPoint(t));
            cell = City.Grid.WorldToCell(local.x, local.z);
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
