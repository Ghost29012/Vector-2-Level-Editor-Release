using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// ================================================================
//  VectorierEditorUI.cs  —  DROP THIS ON SCRIPT MANAGER. DONE.
//  All features work in both Editor AND standalone build.
// ================================================================

public class VectorierEditorUI : MonoBehaviour
{
    // ── layout ───────────────────────────────────────────────────
    const float LEFT_W    = 260f;
    const float RIGHT_W   = 300f;
    const float TOOLBAR_H = 62f;
    const float ROW_H     = 22f;
    const float HIER_H    = 340f;  // taller to show more of the tree
    const float PREVIEW_H = 150f;
    const float INDENT    = 14f;

    // full sorting layer list from the project
    static readonly string[] SORTING_LAYERS = new string[] {
        "BgFurther","BgVeryVeryFar","BgVeryFar","BgFar","BgMiddle","BgClose",
        "BgVeryClose","Bg_0.5","Wall","CAperture","CApertureAdd","CPanels",
        "CPanelsAdd","CDecals","CDecalsAdd","CQuestDecals","CQuestDecalsAdd",
        "HoloWarningSigns","UnderFloorPanels","CutScene","Swarm","Shadows",
        "StuntsLinearDodge","Stunts","Black","Collision","BlackAdd","TrapsColor",
        "TrapsBlack","LaserLow1","LaserLow2","LaserMed","LaserHigh","LaserHigh2",
        "TrapsShadows","Sequences","Lights","LightsAdd","Model","Items",
        "Particles","Fg","FgAdd","0","Default","Debug"
    };

    // Tags available in this project (Untagged + TagManager.asset tags).
    static readonly string[] TAGS = new string[] {
        "Untagged",
        "Object","Image","Platform","Area","Trigger","Camera","Dynamic",
        "In","Out","Object Reference","Animation","Dynamic Trigger",
        "Trapezoid","Spawn","Unused"
    };

    // ── camera ───────────────────────────────────────────────────
    Camera  _cam;
    bool    _panning;
    Vector3 _panOrigin, _camOrigin;

    // ── scene selection + drag ───────────────────────────────────
    GameObject       _selectedGO;   // primary selection (inspector shows this one)
    List<GameObject> _selection   = new List<GameObject>(); // full multi-selection
    bool             _dragging;
    bool             _dragConfirmed  = false;  // true once mouse moves >5px after click
    Vector2          _dragStartScreen;          // screen pos where drag began
    Vector3          _dragOffset;
    List<Vector3>    _dragOffsets  = new List<Vector3>(); // per-object offsets for multi-drag
    int              _lastHierIdx  = -1; // for shift-range select in hierarchy

    // ── hierarchy drag-to-reparent ───────────────────────────────
    bool             _hierDragging       = false;  // dragging a GO in the hierarchy
    int              _hierDragSourceIdx  = -1;     // which visible row we grabbed
    GameObject       _hierDragGO         = null;   // the GO being dragged
    int              _hierDropTargetIdx  = -1;     // which row the drop indicator shows above
    bool             _hierDropAsChild    = false;  // drop onto = child, drop between = sibling
    float            _hierDragStartY     = 0f;     // scroll-space Y where drag started
    const float      HIER_DRAG_THRESH    = 6f;     // pixels to confirm drag

    // ── drag-select rectangle ────────────────────────────────────
    bool    _boxSelecting   = false;
    Vector3 _boxStartWorld;         // world-space start of drag-select
    Vector2 _boxStartScreen;        // screen-space start
    static readonly string[] OR_FILENAMES = {
        "triggers","traps_placeholder","traps","shadows","wall_props",
        "bonus","obstacles","obstacles_moving","doors","doors_service",
        "objects_items","underfloor"
    };

    // ── cached scene objects (refresh on demand only) ────────────
    SpriteRenderer[] _cachedSRs        = new SpriteRenderer[0];
    SpriteRenderer   _cachedSelectedSR = null;
    bool             _srDirty          = true;
    GameObject[]     _cachedIn         = new GameObject[0];
    GameObject[]     _cachedOut        = new GameObject[0];
    float            _markerCacheTime  = -1f;

    SpriteRenderer[] GetSpriteRenderers() {
        if (_srDirty) {
            _cachedSRs = FindObjectsOfType<SpriteRenderer>();
            _srDirty   = false;
        }
        return _cachedSRs;
    }

    void InvalidateSRCache() { _srDirty = true; }

    // ── resize handles ───────────────────────────────────────────
    // New approach: track drag corner + anchor corner in world space.
    // Scale is ALWAYS positive. Never flip sign.
    enum ResizeHandle { None, TL, TR, BL, BR }
    ResizeHandle _resizing         = ResizeHandle.None;
    Vector3      _resizeAnchorWorld;  // the corner that stays fixed (opposite to drag)
    Vector3      _resizeOriginScale;  // scale when drag started (for Z preservation)
    Vector3      _resizeOriginPos;    // unused but kept for compatibility
    const float  HANDLE_SCREEN_SIZE = 8f;

    // ── undo stack ───────────────────────────────────────────────
    struct UndoState { public GameObject go; public Vector3 position; public Vector3 scale; }
    List<UndoState> _undoStack = new List<UndoState>();
    const int MAX_UNDO = 20;
    void RecordUndo() {
        foreach (var go in _selection)
            if (go != null) _undoStack.Add(new UndoState { go=go, position=go.transform.position, scale=go.transform.localScale });
        while (_undoStack.Count > MAX_UNDO * 10) _undoStack.RemoveAt(0);
    }
    void PerformUndo() {
        if (_undoStack.Count == 0) return;
        int count = Mathf.Max(1, _selection.Count);
        int start = Mathf.Max(0, _undoStack.Count - count);
        for (int i = _undoStack.Count - 1; i >= start; i--) {
            var s = _undoStack[i];
            if (s.go != null) { s.go.transform.position = s.position; s.go.transform.localScale = s.scale; }
            _undoStack.RemoveAt(i);
        }
    }
    static bool IsProtected(GameObject go) =>
        go != null && (go.name == "ScriptManager" || go.name == "Camera" || go.name == "Main Camera");

    GameObject GetSceneSelectionTarget(GameObject go) {
        if (go == null) return null;

#if UNITY_EDITOR
        GameObject prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
        if (prefabRoot != null) return prefabRoot;
#endif

        Transform t = go.transform;
        while (t.parent != null && t.parent.parent != null) t = t.parent;
        return t.parent != null ? t.parent.gameObject : t.gameObject;
    }

    // ── copy/paste ───────────────────────────────────────────────
    List<GameObject> _clipboard = new List<GameObject>();

    // ── asset tree ───────────────────────────────────────────────
    class Node {
        public string     Path, Name;
        public bool       IsDir, Expanded;
        public int        Depth;
        public List<Node> Children = new List<Node>();
    }
    Node        _root;
    List<Node>  _flat        = new List<Node>();
    Node        _selectedAsset;
    Vector2     _treeScroll;
    string      _search      = "";
    Texture2D   _previewTex;
    string      _previewName = "", _previewMeta = "";

    // ── hierarchy ────────────────────────────────────────────────
    Vector2 _hierScroll;
    string  _hierSearch    = "";
    HashSet<int> _collapsed = new HashSet<int>(); // instance IDs of collapsed GOs

    // ── inspector ────────────────────────────────────────────────
    Vector2 _inspScroll;
    int     _inspTab = 0; // 0=Inspector  1=Add Component
    HashSet<string> _collapsedSections = new HashSet<string>(); // which InspSec headers are collapsed
    float   _inspContentH = 600f; // updated each frame after drawing inspector
    float   _lastBuildTime = -999f;
    bool    _wasFullScreen   = false; // track fullscreen state to fix black bars on exit
    float   _lastClickTime   = -1f;   // for double-click detection
    Vector2 _lastClickScreen;         // screen pos of last click
    bool    _childSelectMode  = false; // true after double-click: next drag moves child, not root

    // ── popup state ──────────────────────────────────────────────
    // Runtime-compatible dropdown state

    // Color picker state
    bool  _colorPickerOpen = false;
    Color _editingColor    = Color.white;

    // ObjectReference file picker popup
    bool            _orFilePopupOpen   = false;
    ObjectReference _orFilePopupTarget = null;
    Vector2         _orFilePopupScroll;
    bool            _popupEventEaten   = false; // flag to replay eaten events into popup
    bool            _popupClosedThisFrame = false; // true the frame a popup selection was made — blocks hierarchy

    // ── settings panel ───────────────────────────────────────────
    bool    _settingsOpen      = false;
    float   _scrollSpeed       = 3f;
    int     _targetFPS         = 60;
    int     _aaLevel           = 0; // 0=off,2,4,8
    bool    _vsync             = false;
    int     _theme             = 0; // 0=Deep Slate, 1=Warm Dark, 2=High Contrast
    int     _resolutionIdx     = 0;
    static readonly int[]    FPS_OPTIONS = { 30, 60, 120, 144, 0 }; // 0 = unlimited
    static readonly string[] FPS_LABELS  = { "30", "60", "120", "144", "Unlimited" };
    static readonly int[]    AA_OPTIONS  = { 0, 2, 4, 8 };
    static readonly string[] AA_LABELS   = { "Off", "2x", "4x", "8x" };

    // ── V2 Dimensions input state (persists between frames) ──────
    string _v2InputX = "", _v2InputY = "", _v2InputW = "", _v2InputH = "";
    GameObject _v2InputTarget = null; // which GO these inputs belong to
    string _roomsDirectory    = "";
    bool   _showRoomsDirEntry = false;
    bool   _showV2Dim = false; // deprecated feature, hidden by default

    // ── XML level loader ─────────────────────────────────────────────

    // ── grid & snap (Vec2 units: 1 Unity unit = 100 Vec2 units) ──
    // Minor line = 50 Vec2, Major line = 100 Vec2 (one native sprite unit)
    bool  _showGrid    = true;
    bool  _snapEnabled = true;
    bool  _showInOut   = true;   // toggle In/Out visual markers
    float _snapSize = 50f;
    float SnapV2(float v) => (_snapEnabled && _snapSize > 0f) ? Mathf.Round(v / (_snapSize/100f)) * (_snapSize/100f) : v;

    // ── texture cache (avoid creating Texture2D every frame) ──────
    static readonly Dictionary<Color, Texture2D> _texCache = new Dictionary<Color, Texture2D>();
    static Texture2D Tex(Color col) {
        if (_texCache.TryGetValue(col, out var t) && t != null) return t;
        t = new Texture2D(1,1); t.SetPixel(0,0,col); t.Apply();
        _texCache[col] = t;
        return t;
    }

    // ── hierarchy cache (rebuild only when scene changes) ────────
    List<GameObject> _cachedHierarchy = new List<GameObject>();
    int   _hierarchyVersion   = -1;
    float _hierarchyRebuildTime = -1f;

    // Cached visible list — only rebuilt when collapse/scene/search changes
    List<GameObject> _visibleGOs    = new List<GameObject>();
    List<int>        _visibleDepths = new List<int>();
    List<bool>       _hasKidsList   = new List<bool>();
    string           _lastHierSearch = null;
    int              _collapsedVersion = 0;   // bump when collapse state changes
    int              _lastCollapsedVersion = -1;
    float            _lastVisibleRebuild = -1f;
    // Cached protected style so we don't alloc per row
    GUIStyle _sProtected;

    // ── styles ───────────────────────────────────────────────────
    GUIStyle _sRow, _sRowSel, _sLabel, _sDim, _sSearch,
             _sHeader, _sBtn, _sInspValue, _sInspSection,
             _sHierRow, _sHierSel, _sTextField, _sPopupItem, _sPopupItemSel;
    bool _stylesReady;

    // ── colours — liquid glass ────────────────────────────────────
    static Color C(float r,float g,float b,float a=1f) => new Color(r,g,b,a);

    // Deep cool-black base with subtle teal undertone
    // Theme-sensitive colors — instance fields, reassigned by ApplyTheme()
    Color BG        = C(0.04f,0.05f,0.07f,0.97f);
    Color PANEL     = C(0.08f,0.10f,0.14f,0.88f);
    Color TOOLBAR   = C(0.05f,0.06f,0.09f,0.96f);

    // Rows — barely-there alternation
    Color ROW_EVEN  = C(0.09f,0.11f,0.15f,0.85f);
    Color ROW_ODD   = C(0.07f,0.08f,0.12f,0.85f);
    Color ROW_SEL   = C(0.18f,0.52f,0.92f,0.85f);
    Color ROW_HOV   = C(0.14f,0.18f,0.26f,0.70f);

    // Accent — electric cyan-blue, like frosted glass catching light
    Color ACCENT    = C(0.22f,0.72f,0.98f,1f);
    Color ACCENT2   = C(0.10f,0.55f,0.85f,1f);

    // Borders — iridescent hairlines
    Color BORDER    = C(0.30f,0.45f,0.65f,0.22f);
    Color BORDER_LT = C(0.60f,0.80f,1.00f,0.12f);
    Color BORDER_GL = C(0.70f,0.90f,1.00f,0.18f);

    // Text
    Color TEXT      = C(0.92f,0.95f,0.98f,1f);
    Color TEXT_SEC  = C(0.58f,0.68f,0.80f,1f);
    static readonly Color DIM       = C(0.38f,0.48f,0.62f,1f);

    // Asset badge colours — vivid, saturated
    static readonly Color COL_DIR   = C(1.00f,0.85f,0.20f,1f);
    static readonly Color COL_IMG   = C(0.20f,0.95f,0.65f,1f);
    static readonly Color COL_PRE   = C(0.40f,0.75f,1.00f,1f);
    static readonly Color COL_XML   = C(1.00f,0.55f,0.20f,1f);
    static readonly Color COL_SCN   = C(0.80f,0.45f,1.00f,1f);
    static readonly Color COL_OTH   = C(0.45f,0.55f,0.70f,1f);

    // Inspector — frosted glass cards
    Color INSP_SEC  = C(0.10f,0.13f,0.19f,0.90f);
    Color INSP_FIELD= C(0.06f,0.08f,0.13f,0.80f);
    static readonly Color INSP_HL   = C(0.22f,0.72f,0.98f,0.10f);

    // Popup — deep frosted
    Color POPUP_BG  = C(0.05f,0.07f,0.11f,0.97f);

    // ─────────────────────────────────────────────────────────────

    void Start()
    {
        _cam = Camera.main ?? FindObjectOfType<Camera>();
        if (_cam == null) {
            var cgo = new GameObject("EditorCamera");
            _cam = cgo.AddComponent<Camera>();
        }
        _cam.orthographic     = true;
        _cam.orthographicSize = 5f;
        _cam.backgroundColor  = C(0.03f,0.04f,0.06f);
        _cam.clearFlags       = CameraClearFlags.SolidColor;

        // Load and apply settings
        _scrollSpeed  = PlayerPrefs.GetFloat("VE_ScrollSpeed", 3f);
        _targetFPS    = PlayerPrefs.GetInt("VE_FPS", 60);
        _aaLevel      = PlayerPrefs.GetInt("VE_AA", 0);
        _vsync        = PlayerPrefs.GetInt("VE_VSync", 0) == 1;
        _theme        = PlayerPrefs.GetInt("VE_Theme", 0);
        ApplyTheme(); // sets _cam.backgroundColor from theme
        ApplySettings(); // applies vsync, fps cap, and AA from saved prefs

#if UNITY_EDITOR
        _roomsDirectory = EditorPrefs.GetString("VectorierSettings.RoomsDirectory", "");
#else
        _roomsDirectory = PlayerPrefs.GetString("VectorierSettings.RoomsDirectory", "");
#endif
        RefreshTree();
    }

    float _lastGuiTime;

    bool AnyPopupOpen => _orFilePopupOpen;

    // Save directory to prefs AND push to any live BuildMapVec2 component immediately
    void SaveRoomsDirectory(string dir) {
        // Strip surrounding single/double quotes, then trailing slashes
        dir = dir.Trim(new char[]{'"', '\''}).TrimEnd('/', '\\').Trim();
        _roomsDirectory = dir;
#if UNITY_EDITOR
        EditorPrefs.SetString("VectorierSettings.RoomsDirectory", dir);
#else
        PlayerPrefs.SetString("VectorierSettings.RoomsDirectory", dir);
        PlayerPrefs.Save();
#endif
        // Push to the live component so Build() uses new path immediately
        var bm = FindObjectOfType<BuildMapVec2>();
        if (bm != null) bm.vectorFilePath = dir;
    }

    void Update()
    {
        HandleCam();
        if (!AnyPopupOpen && !_popupClosedThisFrame) HandleSceneInteraction();

        // Continuous repaint during hierarchy drag is handled by OnGUI being called each frame
        // Fix black bars after exiting fullscreen — Unity sometimes leaves letterboxing
        if (!Screen.fullScreen && _wasFullScreen) {
            // Re-apply the windowed resolution to clear letterbox bars
            Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, false);
        }
        _wasFullScreen = Screen.fullScreen;
    }

    // ── camera ───────────────────────────────────────────────────

    void HandleCam()
    {
        if (_cam == null) return;
        float mx = Input.mousePosition.x;
        bool overUI = mx < LEFT_W || mx > Screen.width - RIGHT_W;
        if (overUI) { _panning = false; return; }

        if (Input.GetMouseButtonDown(2)) {
            _panning   = true;
            _panOrigin = Input.mousePosition;
            _camOrigin = _cam.transform.position;
        }
        if (Input.GetMouseButtonUp(2)) _panning = false;
        if (_panning) {
            Vector3 d = Input.mousePosition - _panOrigin;
            float s   = _cam.orthographicSize / (Screen.height * 0.5f);
            _cam.transform.position = _camOrigin + new Vector3(-d.x*s,-d.y*s,0f);
        }
        float mx2 = Input.mousePosition.x;
        float my2 = Screen.height - Input.mousePosition.y;
        bool overViewport = mx2 > LEFT_W && mx2 < Screen.width - RIGHT_W && my2 > TOOLBAR_H;
        if (overViewport) {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.001f)
                _cam.orthographicSize = Mathf.Clamp(
                    _cam.orthographicSize - scroll * _scrollSpeed, 0.5f, 50f);
        }
    }

    // ── scene interaction ─────────────────────────────────────────

    // Returns the 4 world-space corners of a GameObject's SpriteRenderer bounds
    // order: TL, TR, BL, BR
    bool GetCorners(GameObject go, out Vector3 tl, out Vector3 tr, out Vector3 bl, out Vector3 br)
    {
        tl = tr = bl = br = Vector3.zero;
        var sr = go?.GetComponent<SpriteRenderer>();
        if (sr == null) return false;
        Bounds b = sr.bounds;
        tl = new Vector3(b.min.x, b.max.y, 0f);
        tr = new Vector3(b.max.x, b.max.y, 0f);
        bl = new Vector3(b.min.x, b.min.y, 0f);
        br = new Vector3(b.max.x, b.min.y, 0f);
        return true;
    }

    // Check if a screen-space mouse position is near a world-space point
    bool NearHandle(Vector3 worldPt, Vector2 mouseScreen)
    {
        if (_cam == null) return false;
        Vector3 sp = _cam.WorldToScreenPoint(worldPt);
        Vector2 ss = new Vector2(sp.x, sp.y); // keep Y in screen space (not flipped)
        return Vector2.Distance(ss, new Vector2(mouseScreen.x, mouseScreen.y)) < HANDLE_SCREEN_SIZE + 2f;
    }

    void HandleSceneInteraction()
    {
        if (_cam == null) return;
        float mx = Input.mousePosition.x;
        float my = Screen.height - Input.mousePosition.y;
        bool overUI = mx < LEFT_W || mx > Screen.width - RIGHT_W || my < TOOLBAR_H;
        if (overUI) { _resizing = ResizeHandle.None; _boxSelecting = false; return; }

        Vector3 wp = _cam.ScreenToWorldPoint(new Vector3(
            Input.mousePosition.x, Input.mousePosition.y,
            Mathf.Abs(_cam.transform.position.z)));
        wp.z = 0f;

        Vector2 mouseScreen = new Vector2(Input.mousePosition.x, Input.mousePosition.y);

        // ── active resize drag ────────────────────────────────────
        // Purely world-space: measure distance from fixed anchor to mouse,
        // divide by native sprite size to get scale. Always positive.
        if (_resizing != ResizeHandle.None && Input.GetMouseButton(0) && _selectedGO != null) {
            var sr = _selectedGO.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null) {
                float nativeW = sr.sprite.rect.width  / sr.sprite.pixelsPerUnit;
                float nativeH = sr.sprite.rect.height / sr.sprite.pixelsPerUnit;

                float distX = Mathf.Abs(wp.x - _resizeAnchorWorld.x);
                float distY = Mathf.Abs(wp.y - _resizeAnchorWorld.y);

                float newSx = Mathf.Max(distX / nativeW, 0.01f);
                float newSy = Mathf.Max(distY / nativeH, 0.01f);

                // Only update scale — never touch position.
                // Vec2 export reads transform.position directly; moving it here breaks export.
                _selectedGO.transform.localScale = new Vector3(newSx, newSy, _resizeOriginScale.z);
            }
        }

        // ── box-select drag (Shift held) ──────────────────────────
        if (_boxSelecting && Input.GetMouseButton(0)) {
            // live preview drawn in DrawSelectedGizmo
        }

        if (Input.GetMouseButtonUp(0)) {
            if (_boxSelecting) {
                // Finish box select — find all sprites whose bounds overlap the drag rect
                float minX = Mathf.Min(_boxStartWorld.x, wp.x);
                float maxX = Mathf.Max(_boxStartWorld.x, wp.x);
                float minY = Mathf.Min(_boxStartWorld.y, wp.y);
                float maxY = Mathf.Max(_boxStartWorld.y, wp.y);
                Bounds box = new Bounds(
                    new Vector3((minX+maxX)*0.5f,(minY+maxY)*0.5f,0),
                    new Vector3(maxX-minX, maxY-minY, 1));
                bool ctrl = Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl)
                         || Input.GetKey(KeyCode.LeftCommand) ||Input.GetKey(KeyCode.RightCommand);
                if (!ctrl) _selection.Clear();
                foreach (var sr in GetSpriteRenderers()) {
                    if (!sr.bounds.Intersects(box)) continue;
                    GameObject target = GetSceneSelectionTarget(sr.gameObject);
                    if (target != null && !_selection.Contains(target))
                        _selection.Add(target);
                }
                _selectedGO = _selection.Count > 0 ? _selection[_selection.Count-1] : null;
#if UNITY_EDITOR
                if (_selectedGO) Selection.activeGameObject = _selectedGO;
#endif
                _boxSelecting = false;
            }
            _resizing      = ResizeHandle.None;
            _dragging      = false;
            _dragConfirmed = false;
        }

        if (Input.GetMouseButtonDown(0)) {
            _colorPickerOpen = false;

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl  = Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl)
                      || Input.GetKey(KeyCode.LeftCommand) ||Input.GetKey(KeyCode.RightCommand);

            // ── check resize handles on primary selection first ───
            if (!shift && _selectedGO != null && _selection.Count == 1) {
                Vector3 tl, tr, bl, br;
                if (GetCorners(_selectedGO, out tl, out tr, out bl, out br)) {
                    if (NearHandle(tl, mouseScreen)) { StartResize(ResizeHandle.TL, br); return; }
                    if (NearHandle(tr, mouseScreen)) { StartResize(ResizeHandle.TR, bl); return; }
                    if (NearHandle(bl, mouseScreen)) { StartResize(ResizeHandle.BL, tr); return; }
                    if (NearHandle(br, mouseScreen)) { StartResize(ResizeHandle.BR, tl); return; }
                }
            }

            // ── Shift+click on empty space = start box select ─────
            var hit = Physics2D.OverlapPoint(wp);
            GameObject clicked = hit != null ? hit.gameObject : null;

            // Detect double-click (< 0.3s between clicks on same spot)
            bool isDouble = (Time.realtimeSinceStartup - _lastClickTime) < 0.3f &&
                            Vector2.Distance(mouseScreen, _lastClickScreen) < 8f;
            _lastClickTime   = Time.realtimeSinceStartup;
            _lastClickScreen = mouseScreen;

            if (isDouble) {
                // Double-click: select the deepest child object under cursor
                GameObject childHit = null;
                int dblBestDepth = -1;
                foreach (var sr in GetSpriteRenderers()) {
                    if (!sr.bounds.Contains(wp)) continue;
                    int d = 0; var t3 = sr.transform;
                    while (t3.parent != null) { d++; t3 = t3.parent; }
                    if (d > dblBestDepth) { dblBestDepth = d; childHit = sr.gameObject; }
                }
                if (childHit != null) {
                    _selection.Clear();
                    _selection.Add(childHit);
                    _selectedGO = childHit;
                    _childSelectMode = true; // stay in child mode until next click on empty space
                    _dragging = true;
                    _dragStartScreen = mouseScreen;
                    _dragConfirmed   = false;
                    RecordUndo();
                    _dragOffsets.Clear();
                    _dragOffsets.Add(childHit.transform.position - wp);
#if UNITY_EDITOR
                    Selection.activeGameObject = _selectedGO;
#endif
                }
                return;
            }

            // Single-click: if in child-select mode, keep selecting children; otherwise walk to root
            if (_childSelectMode) {
                // Child mode: pick the deepest (most nested) SR that contains the point
                int bestDepth = -1;
                foreach (var sr in GetSpriteRenderers()) {
                    if (!sr.bounds.Contains(wp)) continue;
                    int d = 0; var t2 = sr.transform;
                    while (t2.parent != null) { d++; t2 = t2.parent; }
                    if (d > bestDepth) { bestDepth = d; clicked = sr.gameObject; }
                }
                // If nothing hit, exit child mode
                if (clicked == null) _childSelectMode = false;
            } else {
                // Normal: walk up to scene root (images keep old behavior, prefabs handled below)
                if (clicked != null) {
                    Transform t = clicked.transform;
                    while (t.parent != null && t.parent.parent != null) t = t.parent;
                    if (t.parent != null) clicked = t.parent.gameObject;
                }
                if (clicked == null) {
                    foreach (var sr in GetSpriteRenderers()) {
                        if (!sr.bounds.Contains(wp)) continue;
                        
#if UNITY_EDITOR
                        // Check if this sprite belongs to a prefab instance
                        GameObject prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(sr.gameObject);
                        if (prefabRoot != null) {
                            // It's a prefab - select the prefab root directly without root-walking
                            clicked = prefabRoot;
                        } else {
                            // Not a prefab (it's an image) - use root-walking
                            Transform t = sr.transform;
                            while (t.parent != null && t.parent.parent != null) t = t.parent;
                            clicked = t.parent != null ? t.parent.gameObject : t.gameObject;
                        }
#else
                        // Runtime: use root-walking (no prefab detection available)
                        Transform t = sr.transform;
                        while (t.parent != null && t.parent.parent != null) t = t.parent;
                        clicked = t.parent != null ? t.parent.gameObject : t.gameObject;
#endif
                        break;
                    }
                }
            }

            if (shift && clicked == null) {
                _boxSelecting  = true;
                _boxStartWorld = wp;
                _boxStartScreen = mouseScreen;
                return;
            }

            // ── normal click / select ─────────────────────────────
            if (clicked != null) {
                if (ctrl) {
                    // Ctrl+click: toggle in/out of selection but always drag everything selected
                    if (_selection.Contains(clicked)) {
                        _selection.Remove(clicked);
                        _selectedGO = _selection.Count > 0 ? _selection[_selection.Count-1] : null;
                    } else {
                        _selection.Add(clicked);
                        _selectedGO = clicked;
                    }
                } else if (!_selection.Contains(clicked)) {
                    // Plain click on something not selected: select only it
                    _selection.Clear();
                    _selection.Add(clicked);
                    _selectedGO = clicked;
                } else {
                    // Plain click on already-selected object: keep selection, just update primary
                    _selectedGO = clicked;
                }
            } else if (!ctrl) {
                _selection.Clear();
                _selectedGO = null;
                _childSelectMode = false;
            }

            _dragging = _selection.Count > 0;
            _dragStartScreen = mouseScreen;
            _dragConfirmed   = false;
            RecordUndo();  // snapshot positions before drag
            _dragOffsets.Clear();
            foreach (var go in _selection)
                _dragOffsets.Add(go.transform.position - wp);

#if UNITY_EDITOR
            Selection.activeGameObject = _selectedGO;
#endif
        }

        // ── drag all selected objects (with threshold) ────────────
        if (_resizing == ResizeHandle.None && !_boxSelecting &&
            Input.GetMouseButton(0) && _dragging && _selection.Count > 0) {
            // Only start actually moving after mouse travels >5px — prevents accidental moves
            if (!_dragConfirmed &&
                Vector2.Distance(mouseScreen, _dragStartScreen) > 5f)
                _dragConfirmed = true;
            if (_dragConfirmed) {
                for (int i = 0; i < _selection.Count; i++)
                    if (_selection[i] != null && i < _dragOffsets.Count) {
                        Vector3 raw = wp + _dragOffsets[i];
                        raw.x = SnapV2(raw.x);
                        raw.y = SnapV2(raw.y);
                        _selection[i].transform.position = raw;
                    }
            }
        }
    }

    void StartResize(ResizeHandle handle, Vector3 anchorWorld)
    {
        _resizing          = handle;
        _resizeAnchorWorld = anchorWorld;
        _resizeOriginScale = _selectedGO.transform.localScale;
        _resizeOriginPos   = _selectedGO.transform.position;
        _dragging          = false;

        // Normalise: make sure scale is positive before we start.
        // Negative scale is what causes Vector 2 to break.
        var s = _selectedGO.transform.localScale;
        _selectedGO.transform.localScale = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), s.z);
        _resizeOriginScale = _selectedGO.transform.localScale;
    }

    // ── asset tree ───────────────────────────────────────────────

    void RefreshTree()
    {
        string root = Application.streamingAssetsPath;
        if (!Directory.Exists(root)) {
            _root = new Node { Name="StreamingAssets (missing)", IsDir=true };
            RebuildFlat(); return;
        }
        _root = Build(root, 0);
        _root.Expanded = true;
        RebuildFlat();
    }

    Node Build(string path, int depth)
    {
        bool isDir = Directory.Exists(path);
        var n = new Node {
            Path=path,
            Name=depth==0?"Assets":Path.GetFileName(path),
            IsDir=isDir, Depth=depth
        };
        if (!isDir) return n;
        foreach (string d in Directory.GetDirectories(path)) n.Children.Add(Build(d,depth+1));
        foreach (string f in Directory.GetFiles(path)) {
            if (f.EndsWith(".meta")) continue;
            n.Children.Add(Build(f,depth+1));
        }
        return n;
    }

    void RebuildFlat() {
        _flat.Clear();
        if (_root==null) return;
        if (!string.IsNullOrEmpty(_search)) SearchFlat(_root,_search.ToLower());
        else Flatten(_root);
    }
    void Flatten(Node n) {
        _flat.Add(n);
        if (n.IsDir && n.Expanded) foreach (var c in n.Children) Flatten(c);
    }
    void SearchFlat(Node n,string q) {
        if (!n.IsDir && n.Name.ToLower().Contains(q)) _flat.Add(n);
        foreach (var c in n.Children) SearchFlat(c,q);
    }

    // ═════════════════════════════════════════════════════════════
    //  GUI
    // ═════════════════════════════════════════════════════════════

    void OnGUI()
    {
        MakeStyles();
        float sw = Screen.width, sh = Screen.height;
        _popupClosedThisFrame = false; // reset each frame

        // ── Early event blocker: if any popup is open, eat mouse events on the right
        // panel EXCEPT for scroll events (so popup scrollviews work) and events that
        // land inside an open popup rect (so popup items are clickable).
        if (AnyPopupOpen && Event.current.isMouse &&
            Event.current.type != EventType.ScrollWheel) {
            float rx = sw - RIGHT_W;
            if (Event.current.mousePosition.x >= rx)
                Event.current.Use();
        }

        DrawLeftPanel(sh);
        DrawRightPanel(sw, sh);
        if (_showGrid) DrawGrid(sw, sh);
        DrawWorldOverlay(sw, sh);
        DrawSelectedGizmo();
        DrawSettingsPanel(sw, sh);

        // Popups MUST be drawn last. GUI.depth=-100 makes them render on top.
        // Event interception is handled entirely inside DrawPopupsOnTop using
        // Input.GetMouseButtonDown so it bypasses the IMGUI event-consumed-already problem.
        GUI.depth = -100;
        DrawPopupsOnTop(sw, sh);
        GUI.depth = 0;
    }

    // ── LEFT: asset browser ───────────────────────────────────────

    void DrawLeftPanel(float sh)
    {
        Box(0,0,LEFT_W,sh,BG);
        Box(0,0,LEFT_W,TOOLBAR_H,TOOLBAR);
        Line(0,TOOLBAR_H-1,LEFT_W,BORDER);

        float bx=6,by=6,bh=24;
#if UNITY_EDITOR
        if (Btn(bx,by,72,bh,"New Scene",ACCENT))  NewScene();  bx+=76;
        if (Btn(bx,by,52,bh,"Open",     PANEL))   OpenScene(); bx+=56;
        if (Btn(bx,by,46,bh,"Save",     PANEL))   SaveScene();
#else
        GUI.Label(new Rect(6,by,200,bh),"Scene ops: Editor only",_sDim);
#endif
        // Grid + Snap + In/Out controls on second row
        float gby = by + bh + 4f;
        float gbx = 6f;
        if (Btn(gbx, gby, 40, 20, "Grid",   _showGrid    ? ACCENT : PANEL)) _showGrid    = !_showGrid;    gbx += 43f;
        if (Btn(gbx, gby, 40, 20, "Snap",   _snapEnabled ? ACCENT : PANEL)) _snapEnabled = !_snapEnabled; gbx += 43f;
        if (Btn(gbx, gby, 52, 20, "In/Out", _showInOut   ? ACCENT : PANEL)) _showInOut   = !_showInOut;
        if (Btn(LEFT_W-58, by, 52, bh, "Refresh", PANEL)) {
            RefreshTree(); _selectedAsset=null; _previewTex=null;
        }
        if (Btn(LEFT_W-116, by, 54, bh, "Settings", _settingsOpen ? ACCENT : PANEL))
            _settingsOpen = !_settingsOpen;

        float hy=TOOLBAR_H;
        Box(0,hy,LEFT_W,22,C(0.09f,0.09f,0.11f));
        GUI.Label(new Rect(8,hy+3,LEFT_W-16,16),"ASSETS",_sHeader);

        float sy=hy+22;
        Box(0,sy,LEFT_W,27,TOOLBAR);
        Line(0,sy+26,LEFT_W,BORDER);
        string ns=GUI.TextField(new Rect(8,sy+5,LEFT_W-16,17),_search,_sSearch);
        if (string.IsNullOrEmpty(_search))
            GUI.Label(new Rect(10,sy+5,LEFT_W-16,17),"  Search...",_sDim);
        if (ns!=_search) { _search=ns; RebuildFlat(); }

        float tt=sy+27, th=sh-PREVIEW_H-tt-1f;
        float ch=Mathf.Max(_flat.Count*ROW_H,th);
        Box(0,tt,LEFT_W,th,PANEL);

        _treeScroll=GUI.BeginScrollView(
            new Rect(0,tt,LEFT_W,th),_treeScroll,
            new Rect(0,0,LEFT_W-14,ch));

        // Virtual scrolling — only draw rows visible in the scroll window
        int assetFirst = Mathf.Max(0, Mathf.FloorToInt(_treeScroll.y / ROW_H));
        int assetLast  = Mathf.Min(_flat.Count-1, Mathf.CeilToInt((_treeScroll.y+th) / ROW_H));
        if (assetFirst > 0) GUILayout.Space(assetFirst * ROW_H);
        for (int i=assetFirst;i<=assetLast;i++) {
            Node node=_flat[i];
            Rect rr=new Rect(0,i*ROW_H,LEFT_W-14,ROW_H);
            bool sel=node==_selectedAsset;
            DrawRect(rr,sel?ROW_SEL:(i%2==0?ROW_EVEN:ROW_ODD));
            if (!sel && rr.Contains(Event.current.mousePosition)) DrawRect(rr,ROW_HOV);
            float tx=node.Depth*INDENT+4;
            if (node.IsDir) {
                if (GUI.Button(new Rect(tx,i*ROW_H+3,14,ROW_H-4),
                    node.Expanded?"v":">",_sLabel)) {
                    node.Expanded=!node.Expanded; RebuildFlat(); break;
                }
                tx+=16;
            }
            GUI.contentColor=BadgeCol(node);
            GUI.Label(new Rect(tx,i*ROW_H+3,30,ROW_H-4),Badge(node),_sLabel);
            GUI.contentColor=Color.white;
            tx+=32;
            GUI.Label(new Rect(tx,i*ROW_H+2,LEFT_W-tx-16,ROW_H-2),
                node.Name,sel?_sRowSel:_sRow);
            if (Event.current.type==EventType.MouseDown &&
                rr.Contains(Event.current.mousePosition)) {
                if (node.IsDir) { node.Expanded=!node.Expanded; RebuildFlat(); }
                SelectAsset(node); Event.current.Use(); break;
            }
        }
        GUI.EndScrollView();

        // preview
        float py=sh-PREVIEW_H;
        Line(0,py,LEFT_W,BORDER);
        Box(0,py,LEFT_W,PREVIEW_H,TOOLBAR);
        if (_selectedAsset!=null) {
            float px=6, ppy=py+6;
            if (_previewTex!=null) {
                float asp=(float)_previewTex.width/_previewTex.height;
                float pw=Mathf.Min(LEFT_W-12,70f*asp);
                Box(px,ppy,pw,70,C(0.06f,0.06f,0.06f));
                GUI.DrawTexture(new Rect(px,ppy,pw,70),_previewTex,ScaleMode.ScaleToFit);
                ppy+=74;
            }
            GUI.Label(new Rect(6,ppy,LEFT_W-12,18),_previewName,_sLabel);
            GUI.Label(new Rect(6,ppy+18,LEFT_W-12,50),_previewMeta,_sDim);
            if (!_selectedAsset.IsDir) {
                string ext = Path.GetExtension(_selectedAsset.Name).ToLower();
                if (ext == ".prefab")
                    if (Btn(6,py+PREVIEW_H-30,LEFT_W-12,24,"Place in Scene",ACCENT))
                        PlacePrefab(_selectedAsset.Path);
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                    if (Btn(6,py+PREVIEW_H-30,LEFT_W-12,24,"Place as Sprite",ACCENT))
                        PlacePng(_selectedAsset.Path);
            }
        } else {
            GUI.Label(new Rect(6,py+PREVIEW_H*0.45f,LEFT_W-12,20),
                "Select a file to preview",_sDim);
        }
    }

    // ── RIGHT: hierarchy + inspector ──────────────────────────────

    void DrawRightPanel(float sw, float sh)
    {
        float rx = sw - RIGHT_W;
        Box(rx,0,RIGHT_W,sh,BG);
        Line(rx,0,1,sh,BORDER);

        // ── HIERARCHY ─────────────────────────────────────────────
        float ry=0;

        // Header
        Box(rx,ry,RIGHT_W,22,C(0.07f,0.09f,0.13f));
        Box(rx,ry,3,22,ACCENT);
        Box(rx,ry,RIGHT_W,1,BORDER_GL);
        GUI.Label(new Rect(rx+10,ry+3,80,16),"HIERARCHY",_sHeader);
        ry+=22;

        // Search bar
        Box(rx,ry,RIGHT_W,26,C(0.06f,0.08f,0.12f));
        GUI.Label(new Rect(rx+6,ry+5,16,16),"⌕",_sDim);
        _hierSearch = GUI.TextField(new Rect(rx+22,ry+5,RIGHT_W-28,16), _hierSearch, _sSearch);
        ry+=26;
        Box(rx,ry,RIGHT_W,1,BORDER);
        ry+=1;

        // Rebuild scene cache every 0.25s
        float now = Time.realtimeSinceStartup;
        if (now - _hierarchyRebuildTime > 0.25f) {
            _cachedHierarchy.Clear();
            var roots2 = SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var go2 in roots2) CollectHierarchy(go2, _cachedHierarchy);
            _hierarchyRebuildTime = now;
            _lastVisibleRebuild = -1f; // force visible rebuild
        }

        // Rebuild visible list only when something relevant changed
        bool searching = !string.IsNullOrEmpty(_hierSearch);
        bool needRebuild = _lastVisibleRebuild < 0 ||
                           _hierSearch != _lastHierSearch ||
                           _collapsedVersion != _lastCollapsedVersion;

        if (needRebuild) {
            _visibleGOs.Clear(); _visibleDepths.Clear(); _hasKidsList.Clear();
            if (searching) {
                string q = _hierSearch.ToLower();
                foreach (var go2 in _cachedHierarchy)
                    if (go2 != null && go2.name.ToLower().Contains(q)) {
                        _visibleGOs.Add(go2); _visibleDepths.Add(0); _hasKidsList.Add(false);
                    }
            } else {
                System.Action<GameObject,int> addNode = null;
                addNode = (go2, depth) => {
                    _visibleGOs.Add(go2); _visibleDepths.Add(depth);
                    _hasKidsList.Add(go2.transform.childCount > 0);
                    if (!_collapsed.Contains(go2.GetInstanceID()))
                        foreach (Transform child in go2.transform)
                            if (child != null) addNode(child.gameObject, depth+1);
                };
                foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                    if (root != null) addNode(root, 0);
            }
            _lastHierSearch = _hierSearch;
            _lastCollapsedVersion = _collapsedVersion;
            _lastVisibleRebuild = now;
        }

        float hierPanelH = Mathf.RoundToInt(sh * 0.48f) - ry;
        float hierCH = Mathf.Max(_visibleGOs.Count * ROW_H + 4, hierPanelH);
        Box(rx, ry, RIGHT_W, hierPanelH, PANEL);

        _hierScroll = GUI.BeginScrollView(
            new Rect(rx, ry, RIGHT_W, hierPanelH), _hierScroll,
            new Rect(0, 0, RIGHT_W-14, hierCH));

        // Only draw rows that are visible in the scroll window — skip offscreen rows
        float scrollTop    = _hierScroll.y;
        float scrollBottom = scrollTop + hierPanelH;
        int firstRow = Mathf.Max(0, Mathf.FloorToInt(scrollTop / ROW_H) - 1);
        int lastRow  = Mathf.Min(_visibleGOs.Count - 1, Mathf.CeilToInt(scrollBottom / ROW_H) + 1);

        // Spacer for rows above visible range
        if (firstRow > 0)
            GUILayout.Space(firstRow * ROW_H);

        const float IND = 14f;
        bool anyPopup = AnyPopupOpen;
        for (int i = firstRow; i <= lastRow; i++) {
            var go  = _visibleGOs[i];
            if (go == null) continue;
            int dep      = _visibleDepths[i];
            bool hasKids = _hasKidsList[i];
            bool sel     = _selection.Contains(go);
            Rect rr      = new Rect(0, i*ROW_H, RIGHT_W-14, ROW_H);

            DrawRect(rr, sel ? ROW_SEL : (i%2==0 ? ROW_EVEN : ROW_ODD));
            if (!sel && rr.Contains(Event.current.mousePosition)) DrawRect(rr, ROW_HOV);
            if (sel) DrawRect(new Rect(0, i*ROW_H, 2, ROW_H), ACCENT);

            // Depth guide lines — faint vertical lines showing nesting level
            for (int d = 1; d <= dep; d++) {
                float lineX = d * IND;
                DrawRect(new Rect(lineX, i*ROW_H, 1, ROW_H), C(ACCENT.r, ACCENT.g, ACCENT.b, 0.12f));
            }

            float tx = dep * IND + 4;

            // Expand/collapse arrow — reuse cached style
            if (hasKids && !searching) {
                bool coll  = _collapsed.Contains(go.GetInstanceID());
                Color ac   = coll ? C(0.45f,0.65f,0.88f) : C(0.30f,0.55f,0.75f);
                string arr = coll ? "▶" : "▼";
                var arrowSt = new GUIStyle(GUI.skin.label){
                    normal={textColor=ac}, fontSize=8, alignment=TextAnchor.MiddleCenter};
                if (GUI.Button(new Rect(tx, i*ROW_H+3, 12, 14), arr, arrowSt)) {
                    if (coll) _collapsed.Remove(go.GetInstanceID());
                    else      _collapsed.Add(go.GetInstanceID());
                    _collapsedVersion++;
                }
                tx += 14;
            } else { tx += searching ? 0 : 14; }

            // Active toggle — protected objects (ScriptManager, Camera) cannot be hidden
            if (!IsProtected(go)) {
                bool active = GUI.Toggle(new Rect(tx, i*ROW_H+4, 14, 14), go.activeSelf, "");
                if (active != go.activeSelf) go.SetActive(active);
            } else {
                // Draw a locked indicator instead of a toggle
                GUI.Label(new Rect(tx, i*ROW_H+3, 14, 14), "🔒", new GUIStyle(_sDim){fontSize=8, alignment=TextAnchor.MiddleCenter});
            }
            tx += 18;

            // Name
            GUIStyle ns = sel ? _sHierSel : _sHierRow;
            if (IsProtected(go)) {
                if (_sProtected == null)
                    _sProtected = new GUIStyle(_sHierRow){normal={textColor=C(0.50f,0.65f,0.80f)}};
                ns = _sProtected;
            }
            GUI.Label(new Rect(tx, i*ROW_H+3, Mathf.Max(40, RIGHT_W - 14 - tx - 4), ROW_H-4), go.name, ns);

            // ── Hierarchy drag-to-reparent: detect drag start ─────────
            if (Event.current.type == EventType.MouseDown && rr.Contains(Event.current.mousePosition) && Event.current.button == 0) {
                if (anyPopup || _popupClosedThisFrame) {
                    _orFilePopupOpen = false;
                    Event.current.Use();
                } else {
                    bool ctrl  = Event.current.control || Event.current.command;
                    bool shift = Event.current.shift;
                    if (shift && _lastHierIdx >= 0) {
                        int lo = Mathf.Min(_lastHierIdx, i), hi = Mathf.Max(_lastHierIdx, i);
                        if (!ctrl) _selection.Clear();
                        for (int j=lo; j<=hi; j++) if (!_selection.Contains(_visibleGOs[j])) _selection.Add(_visibleGOs[j]);
                        _selectedGO = go;
                    } else if (ctrl) {
                        if (_selection.Contains(go)) { _selection.Remove(go); _selectedGO=_selection.Count>0?_selection[_selection.Count-1]:null; }
                        else { _selection.Add(go); _selectedGO=go; }
                        _lastHierIdx=i;
                    } else {
                        _selection.Clear(); _selection.Add(go); _selectedGO=go; _lastHierIdx=i;
                    }
#if UNITY_EDITOR
                    Selection.activeGameObject = _selectedGO;
#endif
                    // Begin potential hierarchy drag
                    _hierDragging      = false;
                    _hierDragSourceIdx = i;
                    _hierDragGO        = go;
                    _hierDragStartY    = Event.current.mousePosition.y;
                    _hierDropTargetIdx = -1;
                    Event.current.Use();
                }
            }

            // ── Highlight drop target while dragging ────────────────
            if (_hierDragging && _hierDragGO != null) {
                float midY = i * ROW_H + ROW_H * 0.5f;
                float mouseY = Event.current.mousePosition.y;
                // "onto" zone = middle 50% of row; "between" = top/bottom 25%
                bool inRow = mouseY >= i*ROW_H && mouseY < (i+1)*ROW_H;
                if (inRow) {
                    _hierDropTargetIdx = i;
                    _hierDropAsChild   = (mouseY > i*ROW_H + ROW_H*0.25f && mouseY < i*ROW_H + ROW_H*0.75f);
                }
            }
        }
        // ── Hierarchy drag: track movement and draw drop indicator ──
        if (_hierDragGO != null && Event.current.type == EventType.MouseDrag) {
            float moved = Mathf.Abs(Event.current.mousePosition.y - _hierDragStartY);
            if (!_hierDragging && moved > HIER_DRAG_THRESH)
                _hierDragging = true;
        }

        // Drop indicator line / highlight
        if (_hierDragging && _hierDropTargetIdx >= 0) {
            var tgt = _visibleGOs[_hierDropTargetIdx];
            if (tgt != null && tgt != _hierDragGO) {
                if (_hierDropAsChild) {
                    // Highlight the whole row green = "will become child of this"
                    DrawRect(new Rect(0, _hierDropTargetIdx*ROW_H, RIGHT_W-14, ROW_H),
                             C(0.15f, 0.6f, 0.25f, 0.28f));
                    DrawRect(new Rect(0, _hierDropTargetIdx*ROW_H, 3, ROW_H), C(0.2f,0.9f,0.35f,0.9f));
                } else {
                    // Blue line between rows = "will be placed as sibling before this"
                    float lineY = _hierDropTargetIdx * ROW_H;
                    DrawRect(new Rect(0, lineY-1, RIGHT_W-14, 2), ACCENT);
                    DrawRect(new Rect(0, lineY-3, 6, 6), ACCENT);
                }
            }
        }

        // Dragged item ghost label
        if (_hierDragging && _hierDragGO != null) {
            float ghostY = Event.current.mousePosition.y - ROW_H*0.5f;
            DrawRect(new Rect(2, ghostY, RIGHT_W-18, ROW_H), C(ACCENT.r,ACCENT.g,ACCENT.b,0.18f));
            DrawRect(new Rect(2, ghostY, 2, ROW_H), ACCENT);
            GUI.Label(new Rect(10, ghostY+3, RIGHT_W-24, ROW_H-4), _hierDragGO.name, _sRow);
        }

        GUI.EndScrollView();

        // ── Hierarchy drag: MouseUp = execute reparent ────────────
        if (Event.current.type == EventType.MouseUp && _hierDragGO != null) {
            if (_hierDragging && _hierDropTargetIdx >= 0 && _hierDropTargetIdx != _hierDragSourceIdx) {
                var dragGO = _hierDragGO;
                var tgtGO  = _visibleGOs[_hierDropTargetIdx];
                // Guard: can't drop onto self or a descendant of self
                bool isDescendant = false;
                var t2 = tgtGO != null ? tgtGO.transform : null;
                while (t2 != null) { if (t2.gameObject == dragGO){isDescendant=true;break;} t2=t2.parent; }

                if (tgtGO != null && tgtGO != dragGO && !isDescendant && !IsProtected(dragGO)) {
#if UNITY_EDITOR
                    Undo.SetTransformParent(dragGO.transform,
                        _hierDropAsChild ? tgtGO.transform : tgtGO.transform.parent,
                        "Reparent");
#else
                    dragGO.transform.SetParent(
                        _hierDropAsChild ? tgtGO.transform : tgtGO.transform.parent, true);
#endif
                    if (!_hierDropAsChild) {
                        // Place just before the target in sibling order
                        int sibIdx = tgtGO.transform.GetSiblingIndex();
                        dragGO.transform.SetSiblingIndex(sibIdx);
                    }
                    _hierarchyRebuildTime = -1f; // force rebuild
                    _lastVisibleRebuild   = -1f;
                }
            }
            // Always reset drag state on mouse-up
            _hierDragging      = false;
            _hierDragGO        = null;
            _hierDragSourceIdx = -1;
            _hierDropTargetIdx = -1;
            if (Event.current.type == EventType.MouseUp) Event.current.Use();
        }

        // ── Underscore fix — manually inject _ into the focused TextEditor ──
        if (Event.current.type == EventType.KeyDown &&
            GUIUtility.keyboardControl != 0 &&
            Event.current.keyCode == KeyCode.Minus && Event.current.shift) {
            TextEditor te = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
            if (te != null) {
                te.Insert('_');
                Event.current.Use();
            }
        }

        // ── Delete selected objects via keyboard ──────────────────
        // Only fire if no text field is focused (GUIUtility.keyboardControl == 0)
        if (Event.current.type == EventType.KeyDown &&
            GUIUtility.keyboardControl == 0 &&
            (Event.current.keyCode == KeyCode.Delete || Event.current.keyCode == KeyCode.Backspace) &&
            _selection.Count > 0) {
            RecordUndo();
            foreach (var go in _selection) {
                if (go != null && !IsProtected(go)) {
#if UNITY_EDITOR
                    Undo.DestroyObjectImmediate(go);
#else
                    Destroy(go);
#endif
                }
            }
            _selection.Clear();
            _selectedGO = null;
            _lastHierIdx = -1;
            InvalidateSRCache();
            _hierarchyRebuildTime = -1f;
            Event.current.Use();
        }

        // ── Cancel hierarchy drag on Escape ──────────────────────
        if (_hierDragging && Event.current.type == EventType.KeyDown &&
            Event.current.keyCode == KeyCode.Escape) {
            _hierDragging = false; _hierDragGO = null;
            _hierDragSourceIdx = _hierDropTargetIdx = -1;
            Event.current.Use();
        }

        // ── Undo (Ctrl/Cmd+Z) — only when not typing ──────────────
        bool ctrlKey = Event.current.control || Event.current.command;
        if (Event.current.type == EventType.KeyDown && ctrlKey &&
            GUIUtility.keyboardControl == 0 &&
            Event.current.keyCode == KeyCode.Z) {
            PerformUndo();
            Event.current.Use();
        }

        // ── Copy (Ctrl/Cmd+C) — only when not typing ──────────────
        if (Event.current.type == EventType.KeyDown && ctrlKey &&
            GUIUtility.keyboardControl == 0 &&
            Event.current.keyCode == KeyCode.C && _selection.Count > 0) {
            _clipboard.Clear();
            foreach (var sel in _selection)
                if (!IsProtected(sel)) _clipboard.Add(sel);
            Event.current.Use();
        }

        // ── Paste (Ctrl/Cmd+V) — only when not typing ─────────────
        if (Event.current.type == EventType.KeyDown && ctrlKey &&
            GUIUtility.keyboardControl == 0 &&
            Event.current.keyCode == KeyCode.V && _clipboard.Count > 0) {
            _selection.Clear();
            foreach (var src in _clipboard) {
                if (src == null) continue;
                var copy = Instantiate(src);
                copy.name = src.name;
                copy.transform.position = src.transform.position + new Vector3(0.5f, -0.5f, 0f);
                copy.transform.localScale = src.transform.localScale;
                var srcSR = src.GetComponent<SpriteRenderer>();
                var copySR = copy.GetComponent<SpriteRenderer>();
                if (srcSR != null && copySR != null) {
                    copySR.sortingLayerName = srcSR.sortingLayerName;
                    copySR.sortingOrder = srcSR.sortingOrder;
                    copySR.color = srcSR.color;
                }
                try { copy.tag = src.tag; } catch {}
                _selection.Add(copy);
                _selectedGO = copy;
            }
            InvalidateSRCache();
            _hierarchyRebuildTime = -1f;
            Event.current.Use();
        }

        ry = Mathf.RoundToInt(sh * 0.48f); // inspector starts halfway down

        // ── INSPECTOR + TABS ──────────────────────────────────────
        Line(rx,ry,RIGHT_W,1,BORDER_GL);
        Box(rx,ry,RIGHT_W,22,C(0.07f,0.09f,0.13f));
        Box(rx,ry,3,22,ACCENT);
        GUI.Label(new Rect(rx+10,ry+3,80,16),"INSPECTOR",_sHeader);
        // Tab buttons
        float tabW = (RIGHT_W-100f)*0.5f;
        string[] tabLabels = {"Properties","Add Component"};
        for (int ti=0;ti<2;ti++) {
            bool active = _inspTab == ti;
            Color tabC = active ? ACCENT : C(0.06f,0.08f,0.12f);
            if (Btn(rx+96+ti*tabW, ry+1, tabW-1, 20, tabLabels[ti], tabC)) {
                _inspTab = ti;
                _inspScroll = Vector2.zero;
            }
        }
        ry+=22;

        float inspH = sh - ry;
        Box(rx,ry,RIGHT_W,inspH,PANEL);

        if (_selectedGO==null) {
            GUI.Label(new Rect(rx+8,ry+20,RIGHT_W-16,20),"Nothing selected",_sDim);
            return;
        }

        // ── ADD COMPONENT TAB ─────────────────────────────────────
        if (_inspTab == 1) {
            DrawAddComponentTab(rx, ry, inspH);
            return;
        }

        _inspScroll = GUI.BeginScrollView(
            new Rect(rx,ry,RIGHT_W,inspH),_inspScroll,
            new Rect(0,0,RIGHT_W-14,Mathf.Max(_inspContentH, inspH)));

        float iy=4;
        float iw=RIGHT_W-16;
        float lx=4;

        // ── active + name ─────────────────────────────────────────
        Box(lx,iy,iw,26,INSP_SEC);
        bool isActive=GUI.Toggle(new Rect(lx+4,iy+6,16,16),_selectedGO.activeSelf,"");
        if (isActive!=_selectedGO.activeSelf) _selectedGO.SetActive(isActive);
        string newName=GUI.TextField(new Rect(lx+24,iy+4,iw-90,18),
            _selectedGO.name,_sTextField);
        if (newName!=_selectedGO.name) _selectedGO.name=newName;
        GUI.Label(new Rect(lx+iw-64,iy+6,44,14),"Static",_sDim);
#if UNITY_EDITOR
        bool isSt=GUI.Toggle(new Rect(lx+iw-18,iy+6,16,16),_selectedGO.isStatic,"");
        if (isSt!=_selectedGO.isStatic) _selectedGO.isStatic=isSt;
#endif
        iy+=30;

        // ── Tag (inline list, collapsible) ───────────────────────
        { bool tagOpen;
          iy = InspSec("Tag  [" + _selectedGO.tag + "]", lx, iy, iw, out tagOpen);
          if (tagOpen) {
            string currentTag = _selectedGO.tag;
            for (int ti = 0; ti < TAGS.Length; ti++) {
                bool isCur = TAGS[ti] == currentTag;
                Color rowC = isCur ? ROW_SEL : (ti%2==0 ? ROW_EVEN : ROW_ODD);
                Box(lx, iy, iw, 20, rowC);
                if (isCur) DrawRect(new Rect(lx, iy, 3, 20), ACCENT);
                GUI.Label(new Rect(lx+8, iy+2, iw-12, 16), TAGS[ti], isCur ? _sRowSel : _sRow);
                if (Event.current.type == EventType.MouseDown &&
                    new Rect(lx, iy, iw, 20).Contains(Event.current.mousePosition)) {
                    RecordUndo();
                    try {
                        _selectedGO.tag = TAGS[ti];
                    } catch (UnityException ex) {
                        Debug.LogWarning($"[Vectorier] Cannot set tag '{TAGS[ti]}': {ex.Message}");
                    }
                    // Remove old key — do NOT add new key, so the section stays open
                    _collapsedSections.Remove("Tag  [" + currentTag + "]");
                    Event.current.Use();
                }
                iy += 21;
            }
            iy += 4;
          }
        }

        // ── Physics Layer (inline list, collapsible) ────────────
        { var layerNames2 = new System.Collections.Generic.List<string>();
          for (int k=0;k<32;k++){string s=LayerMask.LayerToName(k);if(!string.IsNullOrEmpty(s))layerNames2.Add(s);}
          string currentLayerName = LayerMask.LayerToName(_selectedGO.layer);
          bool layOpen;
          iy = InspSec("Physics Layer  [" + currentLayerName + "]", lx, iy, iw, out layOpen);
          if (layOpen) {
            for (int li = 0; li < layerNames2.Count; li++) {
                bool isCur = layerNames2[li] == currentLayerName;
                Color rowC = isCur ? ROW_SEL : (li%2==0 ? ROW_EVEN : ROW_ODD);
                Box(lx, iy, iw, 20, rowC);
                if (isCur) DrawRect(new Rect(lx, iy, 3, 20), ACCENT);
                GUI.Label(new Rect(lx+8, iy+2, iw-12, 16), layerNames2[li], isCur ? _sRowSel : _sRow);
                if (Event.current.type == EventType.MouseDown &&
                    new Rect(lx, iy, iw, 20).Contains(Event.current.mousePosition)) {
                    RecordUndo();
                    _selectedGO.layer = LayerMask.NameToLayer(layerNames2[li]);
                    Event.current.Use();
                }
                iy += 21;
            }
            iy += 4;
          }
        }

        // ── Transform ────────────────────────────────────────────
        { bool tfOpen;
          iy=InspSec("Transform",lx,iy,iw, out tfOpen);
          var tf=_selectedGO.transform;
          if (tfOpen) {
            iy=Vec3Row("Position",lx,iy,iw,tf.position,    v=>tf.position=v);
            iy=Vec3Row("Rotation",lx,iy,iw,tf.eulerAngles, v=>tf.eulerAngles=v);
            iy=Vec3Row("Scale",   lx,iy,iw,tf.localScale,  v=>tf.localScale=v);
            iy+=4;
          }
        }

        // ── Vector 2 Dimensions (deprecated — toggle in Settings) ───
        if (_showV2Dim) {
            var srV2 = _selectedGO.GetComponent<SpriteRenderer>();
            if (srV2 != null && srV2.sprite != null &&
                (_selectedGO.tag == "Image" || _selectedGO.tag == "Untagged")) {

                iy = InspSec("V2 Dimensions", lx, iy, iw);

                float nativeW = srV2.sprite.bounds.size.x * 100f;
                float nativeH = srV2.sprite.bounds.size.y * 100f;

                if (_v2InputTarget != _selectedGO) {
                    _v2InputTarget = _selectedGO;
                    _v2InputX = Mathf.RoundToInt(_selectedGO.transform.position.x * 100f).ToString();
                    _v2InputY = Mathf.RoundToInt(-_selectedGO.transform.position.y * 100f).ToString();
                    _v2InputW = Mathf.Abs(nativeW * _selectedGO.transform.localScale.x).ToString("F2");
                    _v2InputH = Mathf.Abs(nativeH * _selectedGO.transform.localScale.y).ToString("F2");
                }

                Box(lx, iy, iw, 10, INSP_FIELD);
                float hw = (iw - 8) * 0.5f;
                GUI.Label(new Rect(lx+4,    iy+1, hw-4, 10), "X", _sHeader);
                GUI.Label(new Rect(lx+hw+8, iy+1, hw-4, 10), "Y", _sHeader);
                iy += 10;
                Box(lx, iy, iw, 18, INSP_FIELD);
                _v2InputX = GUI.TextField(new Rect(lx+4,    iy+2, hw-4, 14), _v2InputX, _sTextField);
                _v2InputY = GUI.TextField(new Rect(lx+hw+8, iy+2, hw-4, 14), _v2InputY, _sTextField);
                iy += 20;

                Box(lx, iy, iw, 10, INSP_FIELD);
                GUI.Label(new Rect(lx+4,    iy+1, hw-4, 10), "W", _sHeader);
                GUI.Label(new Rect(lx+hw+8, iy+1, hw-4, 10), "H", _sHeader);
                iy += 10;
                Box(lx, iy, iw, 18, INSP_FIELD);
                GUI.Label(new Rect(lx+4,    iy+3, hw-4, 14), _v2InputW, _sDim);
                GUI.Label(new Rect(lx+hw+8, iy+3, hw-4, 14), _v2InputH, _sDim);
                iy += 26;
            }
        }

        // ── Sprite Renderer ───────────────────────────────────────
        // Fall back to child SR so prefab roots (which have SR on a child) still show sorting layer
        var sr=_selectedGO.GetComponent<SpriteRenderer>()
             ?? _selectedGO.GetComponentInChildren<SpriteRenderer>();
        if (sr!=null) {
            bool srOpen;
            iy=InspSec("Sprite Renderer",lx,iy,iw, out srOpen);
            if (srOpen) {
            iy=LabelRow("Sprite",lx,iy,iw,sr.sprite!=null?sr.sprite.name:"None");



            // Sorting Layer — inline list, collapsible
            { bool sortOpen;
              iy = InspSec("Sorting Layer  [" + sr.sortingLayerName + "]", lx, iy, iw, out sortOpen);
              if (sortOpen) {
                for (int si = 0; si < SORTING_LAYERS.Length; si++) {
                    bool isCur = SORTING_LAYERS[si] == sr.sortingLayerName;
                    Color rowC = isCur ? ROW_SEL : (si%2==0 ? ROW_EVEN : ROW_ODD);
                    Box(lx, iy, iw, 20, rowC);
                    if (isCur) DrawRect(new Rect(lx, iy, 3, 20), ACCENT);
                    GUI.Label(new Rect(lx+8, iy+2, iw-12, 16), SORTING_LAYERS[si], isCur ? _sRowSel : _sRow);
                    if (Event.current.type == EventType.MouseDown &&
                        new Rect(lx, iy, iw, 20).Contains(Event.current.mousePosition)) {
                        RecordUndo();
                        sr.sortingLayerName = SORTING_LAYERS[si];
                        Event.current.Use();
                    }
                    iy += 21;
                }
                iy += 4;
              }
            }

            // Order in Layer
            Box(lx,iy,iw,22,INSP_FIELD);
            GUI.Label(new Rect(lx+4,iy+3,80,16),"Order in Layer",_sDim);
            string orderStr = GUI.TextField(
                new Rect(lx+88,iy+3,iw-92,16),sr.sortingOrder.ToString(),_sTextField);
            if (int.TryParse(orderStr, out int newOrder)) sr.sortingOrder = newOrder;
            iy+=24;
            iy+=4;
            } // end srOpen
        }

        // ── BuildMapVec2 ──────────────────────────────────────────
        var bm=_selectedGO.GetComponent<BuildMapVec2>();
        if (bm!=null) {
            bool bmOpen;
            iy=InspSec("Build Map Vec2",lx,iy,iw, out bmOpen);
            if (bmOpen) {

            // One-time restore from prefs (only if the field still has the default value)
            if (bm.mapToOverride == "escape_room" && PlayerPrefs.HasKey("VE_MapOverride"))
                bm.mapToOverride = PlayerPrefs.GetString("VE_MapOverride", bm.mapToOverride);

            Box(lx,iy,iw,22,INSP_FIELD);
            GUI.Label(new Rect(lx+4,iy+3,100,16),"Map To Override",_sDim);
            // Strip .xml suffix if user typed it — BuildMapVec2.MoveXML appends .xml itself
            string displayMap = bm.mapToOverride.Replace(".xml","").Replace(".XML","");
            string newMap = GUI.TextField(
                new Rect(lx+108,iy+3,iw-112,16), displayMap, _sTextField);
            if (newMap != displayMap) {
                bm.mapToOverride = newMap.Replace(".xml","").Replace(".XML","");
                PlayerPrefs.SetString("VE_MapOverride", bm.mapToOverride);
                PlayerPrefs.Save();
            }
            iy+=24;

            Box(lx,iy,iw,22,INSP_FIELD);
            GUI.Label(new Rect(lx+4,iy+3,130,16),"Debug Object Writing",_sDim);
            bm.debugObjectWriting=GUI.Toggle(
                new Rect(lx+iw-20,iy+4,16,16),bm.debugObjectWriting,"");
            iy+=24;

            iy+=4;
            bool buildReady = (Time.realtimeSinceStartup - _lastBuildTime) > 0.5f;
            if (buildReady && Btn(lx,iy,iw,26,"Build Map (Vec2)",ACCENT)) {
                _lastBuildTime = Time.realtimeSinceStartup;
#if UNITY_EDITOR
                BuildMapVec2.BuildXml();
#else
                BuildMapVec2.Build(false, false, true);
#endif
            } else if (!buildReady) {
                Box(lx,iy,iw,26,C(0.12f,0.14f,0.18f));
                GUI.Label(new Rect(lx,iy,iw,26),"Building...",new GUIStyle(_sDim){alignment=TextAnchor.MiddleCenter});
            }
            iy+=30;
            iy+=4;
            } // end bmOpen
        }

        // ── Vectorier Rooms Directory ─────────────────────────────
        { bool vsOpen;
          iy=InspSec("Vectorier Settings",lx,iy,iw, out vsOpen);
          if (vsOpen) {
            Box(lx,iy,iw,22,INSP_FIELD);
            GUI.Label(new Rect(lx+4,iy+3,100,16),"Rooms Directory",_sDim);
            string newDir=GUI.TextField(
                new Rect(lx+108,iy+3,iw-138,16),_roomsDirectory,_sTextField);
            if (newDir!=_roomsDirectory) {
                _roomsDirectory=newDir;
                SaveRoomsDirectory(_roomsDirectory);
            }
            if (Btn(lx+iw-28,iy+2,26,18,"...",PANEL)) {
#if UNITY_EDITOR
                string picked=EditorUtility.OpenFolderPanel("Select Rooms Directory","","");
                if (!string.IsNullOrEmpty(picked)) {
                    _roomsDirectory=picked;
                    SaveRoomsDirectory(_roomsDirectory);
                }
#else
                _showRoomsDirEntry = !_showRoomsDirEntry;
#endif
            }
            iy+=24;
#if !UNITY_EDITOR
            if (_showRoomsDirEntry) {
                Box(lx,iy,iw,22,INSP_FIELD);
                GUI.Label(new Rect(lx+4,iy+3,60,16),"Path",_sDim);
                _roomsDirectory=GUI.TextField(new Rect(lx+48,iy+3,iw-72,16),_roomsDirectory,_sTextField);
                if (Btn(lx+iw-22,iy+2,20,18,"✓",ACCENT)) {
                    SaveRoomsDirectory(_roomsDirectory);
                    _showRoomsDirEntry=false;
                }
                iy+=26;
            }
#endif
            iy+=4;
          }
        }

        // ── ObjectReference ───────────────────────────────────────
        var objRef = _selectedGO.GetComponent<ObjectReference>();
        if (objRef != null) {
            bool orOpen;
            iy = InspSec("Object Reference", lx, iy, iw, out orOpen);
            if (orOpen) {

            // FileName dropdown
            Box(lx, iy, iw, 22, INSP_FIELD);
            GUI.Label(new Rect(lx+4, iy+3, 80, 16), "File Name", _sDim);
            int curIdx = System.Array.IndexOf(OR_FILENAMES, objRef.FileName.ToString());
            if (curIdx < 0) curIdx = 0;
            if (Btn(lx+88, iy+2, iw-92, 18, OR_FILENAMES[curIdx], INSP_FIELD)) {
                _orFilePopupOpen = !_orFilePopupOpen;
                _orFilePopupTarget = objRef;
            }
            iy += 24;

            // useCustomVariables toggle
            Box(lx, iy, iw, 22, INSP_FIELD);
            GUI.Label(new Rect(lx+4, iy+3, 120, 16), "Use Custom Variables", _sDim);
            objRef.useCustomVariables = GUI.Toggle(new Rect(lx+iw-20, iy+4, 16, 16), objRef.useCustomVariables, "");
            iy += 24;

            if (objRef.useCustomVariables) {
                bool isTriggers = OR_FILENAMES[curIdx].ToLower() == "triggers";
                iy = isTriggers
                    ? DrawTriggersEditor(objRef, lx, iy, iw)
                    : DrawVariableEditor(objRef, lx, iy, iw);

                // Raw XML collapsible
                Box(lx, iy, iw, 20, INSP_SEC);
                Box(lx, iy, 3, 20, C(0.4f,0.4f,0.5f));
                GUI.Label(new Rect(lx+10, iy+2, iw-60, 16), "Raw XML", _sDim);
                _orRawXmlOpen = GUI.Toggle(new Rect(lx+iw-22, iy+3, 16, 14), _orRawXmlOpen, "");
                iy += 20;
                if (_orRawXmlOpen) {
                    float taH = 120f;
                    var taStyle = new GUIStyle(GUI.skin.textArea) {
                        normal  = { background = Tex(C(0.07f,0.07f,0.10f)), textColor = TEXT },
                        focused = { background = Tex(C(0.08f,0.09f,0.13f)), textColor = Color.white },
                        fontSize = 12, wordWrap = true, padding = new RectOffset(6,6,6,6)
                    };
                    string newCV = GUI.TextArea(new Rect(lx, iy, iw, taH), objRef.CustomVariables ?? "", taStyle);
                    if (newCV != objRef.CustomVariables) objRef.CustomVariables = newCV;
                    iy += taH + 4;
                }
            }
            iy += 4;
            } // end orOpen
        }

        // ── All other Vectorier MonoBehaviours ────────────────────
        var comps=_selectedGO.GetComponents<MonoBehaviour>();
        foreach (var comp in comps) {
            if (comp==null) continue;
            string cn=comp.GetType().Name;
            if (cn=="VectorierEditorUI"||cn=="BuildMapVec2"||cn=="ObjectReference") continue;
#if UNITY_EDITOR
            bool monoOpen;
            iy=InspSec(cn,lx,iy,iw, out monoOpen);
            if (!monoOpen) continue;
            var so=new SerializedObject(comp);
            so.Update();
            var prop=so.GetIterator();
            prop.NextVisible(true);
            while (prop.NextVisible(false)) {
                float fh = prop.propertyType==SerializedPropertyType.String
                           && prop.stringValue.Contains("\n") ? 60f : 22f;
                Box(lx,iy,iw,fh,INSP_FIELD);
                GUI.Label(new Rect(lx+4,iy+3,100,fh-4),prop.displayName,_sDim);
                EditorGUI.PropertyField(
                    new Rect(lx+108,iy+2,iw-112,fh-4),prop,GUIContent.none);
                iy+=fh+2;
            }
            if (so.hasModifiedProperties) so.ApplyModifiedProperties();
            iy+=4;
#else
            // Runtime reflection-based field display
            bool monoOpen2;
            iy=InspSec(cn,lx,iy,iw, out monoOpen2);
            if (!monoOpen2) continue;
            var fields=comp.GetType().GetFields(
                System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance);

            // Styles built once per section
            var fLabelSt = new GUIStyle(GUI.skin.label) {
                normal    = { textColor = C(0.85f, 0.90f, 1.00f) },  // bright, always readable
                fontSize  = 11,
                alignment = TextAnchor.MiddleLeft,
                wordWrap  = false,
                clipping  = TextClipping.Clip
            };
            var fValSt = new GUIStyle(GUI.skin.label) {
                normal    = { textColor = Color.white },
                fontSize  = 11,
                alignment = TextAnchor.MiddleLeft,
                wordWrap  = false
            };

            foreach (var field in fields) {
                object val   = field.GetValue(comp);
                string valStr = val != null ? val.ToString() : "";

                bool isBool   = field.FieldType == typeof(bool);
                bool isString = field.FieldType == typeof(string);
                bool isFloat  = field.FieldType == typeof(float);
                bool isInt    = field.FieldType == typeof(int);
                bool isOther  = !isBool && !isString && !isFloat && !isInt;

                // Two-row layout for long strings, single-row for everything else
                bool twoRow = isString && valStr.Contains("\n");
                float fh = twoRow ? 60f : 22f;

                // Row background — alternate for readability
                Box(lx, iy, iw, fh, INSP_FIELD);
                // Thin left rule per field
                DrawRect(new Rect(lx, iy, 2, fh), C(0.25f,0.40f,0.65f,0.5f));

                // Label column: left half minus a bit
                float labelW = iw * 0.50f - 6f;
                float valueX = lx + labelW + 8f;
                float valueW = iw - labelW - 12f;

                GUI.Label(new Rect(lx+6, iy, labelW, fh), field.Name, fLabelSt);

                if (isBool) {
                    bool nv = GUI.Toggle(new Rect(lx+iw-22, iy+3, 16, 16), (bool)val, "");
                    if (nv != (bool)val) field.SetValue(comp, nv);
                } else if (isString) {
                    string cur = (string)val ?? "";
                    string nv  = twoRow
                        ? GUI.TextArea (new Rect(valueX, iy+2, valueW, fh-4), cur, _sTextField)
                        : GUI.TextField(new Rect(valueX, iy+3, valueW, 16),   cur, _sTextField);
                    if (nv != cur) field.SetValue(comp, nv);
                } else if (isFloat) {
                    string nv = GUI.TextField(new Rect(valueX, iy+3, valueW, 16),
                        ((float)val).ToString("F3"), _sTextField);
                    if (float.TryParse(nv, out float fv)) field.SetValue(comp, fv);
                } else if (isInt) {
                    string nv = GUI.TextField(new Rect(valueX, iy+3, valueW, 16),
                        val.ToString(), _sTextField);
                    if (int.TryParse(nv, out int iv)) field.SetValue(comp, iv);
                } else {
                    // Object ref or enum — show name
                    string display = val is UnityEngine.Object uo && uo != null ? uo.name : valStr;
                    GUI.Label(new Rect(valueX, iy+3, valueW, 16), display, fValSt);
                }
                iy += fh + 2f;
            }
            iy += 4;
#endif
        }

        // Track actual content height so scroll area isn't over-allocated
        if (Event.current.type == EventType.Repaint) _inspContentH = iy + 20;
        GUI.EndScrollView();
    }

    // ── ADD COMPONENT TAB ────────────────────────────────────────
    void DrawAddComponentTab(float rx, float ry, float inspH)
    {
        if (_selectedGO == null) return;
        float iw   = RIGHT_W - 16;
        float lx   = rx + 4;
        float iy   = ry + 6;

        // Helper: draw a section header
        void Sec(string title) {
            Box(lx, iy, iw, 20, INSP_SEC);
            Box(lx, iy, 3, 20, ACCENT);
            GUI.Label(new Rect(lx+8, iy+3, iw-10, 14), title, _sInspSection);
            iy += 22;
        }

        // Helper: add/remove toggle button for a component type
        void CompRow<T>(string label, string tooltip = "") where T : MonoBehaviour {
            bool has = _selectedGO.GetComponent<T>() != null;
            Box(lx, iy, iw, 22, INSP_FIELD);
            DrawRect(new Rect(lx, iy, 2, 22), has ? C(0.2f,0.85f,0.3f,0.7f) : C(0.4f,0.4f,0.5f,0.4f));
            GUI.Label(new Rect(lx+8, iy+3, iw-56, 16), label, _sDim);
            if (!string.IsNullOrEmpty(tooltip))
                GUI.Label(new Rect(lx+8, iy+3, iw-56, 16), "", _sDim); // placeholder for hover
            Color btnColor = has ? C(0.65f,0.18f,0.18f) : ACCENT;
            string btnText = has ? "Remove" : "Add";
            if (Btn(lx+iw-50, iy+2, 48, 18, btnText, btnColor)) {
#if UNITY_EDITOR
                if (has) UnityEditor.Undo.DestroyObjectImmediate(_selectedGO.GetComponent<T>());
                else     UnityEditor.Undo.AddComponent<T>(_selectedGO);
#else
                if (has) Destroy(_selectedGO.GetComponent<T>());
                else     _selectedGO.AddComponent<T>();
#endif
                InvalidateSRCache();
            }
            iy += 24;
        }

        // ── Vectorier game components ─────────────────────────────
        Sec("Vectorier");
        CompRow<ObjectReference>("Object Reference",  "Attach a game object reference (triggers, traps, etc.)");
        CompRow<DynamicTrigger> ("Dynamic Trigger",   "A trigger that runs transformations");
        CompRow<TriggerSettings>("Trigger Settings",  "Raw XML trigger content override");
        CompRow<Spawn>          ("Spawn",             "Player spawn point");
        CompRow<Respawn>        ("Respawn",           "Respawn zone");
        CompRow<BlackBall>      ("Black Ball",        "BlackBall obstacle behaviour");
        CompRow<Laser>          ("Laser",             "Laser obstacle");
        CompRow<Dynamic>        ("Dynamic",           "Dynamic object group");
        CompRow<DynamicColor>   ("Dynamic Color",     "Color animation for dynamic objects");
        CompRow<DynamicPreview> ("Dynamic Preview",   "Preview helper for dynamic objects");
        CompRow<Panels>         ("Panels",            "Panel group controller");
        CompRow<NameVariation>  ("Name Variation",    "Randomise object name suffix");
        CompRow<AnimationProperties>("Animation Properties", "Sprite animation configuration");
        // ── Unity built-in components ─────────────────────────────
        iy += 4;
        Sec("Unity");
        // SpriteRenderer
        {
            bool has = _selectedGO.GetComponent<SpriteRenderer>() != null;
            Box(lx, iy, iw, 22, INSP_FIELD);
            DrawRect(new Rect(lx, iy, 2, 22), has ? C(0.2f,0.85f,0.3f,0.7f) : C(0.4f,0.4f,0.5f,0.4f));
            GUI.Label(new Rect(lx+8, iy+3, iw-56, 16), "Sprite Renderer", _sDim);
            Color btnColor = has ? C(0.65f,0.18f,0.18f) : ACCENT;
            if (Btn(lx+iw-50, iy+2, 48, 18, has?"Remove":"Add", btnColor)) {
#if UNITY_EDITOR
                if (has) UnityEditor.Undo.DestroyObjectImmediate(_selectedGO.GetComponent<SpriteRenderer>());
                else     UnityEditor.Undo.AddComponent<SpriteRenderer>(_selectedGO);
#else
                if (has) Destroy(_selectedGO.GetComponent<SpriteRenderer>());
                else     _selectedGO.AddComponent<SpriteRenderer>();
#endif
                InvalidateSRCache();
            }
            iy += 24;
        }
        // Rigidbody2D
        {
            bool has = _selectedGO.GetComponent<Rigidbody2D>() != null;
            Box(lx, iy, iw, 22, INSP_FIELD);
            DrawRect(new Rect(lx, iy, 2, 22), has ? C(0.2f,0.85f,0.3f,0.7f) : C(0.4f,0.4f,0.5f,0.4f));
            GUI.Label(new Rect(lx+8, iy+3, iw-56, 16), "Rigidbody 2D", _sDim);
            Color btnColor2 = has ? C(0.65f,0.18f,0.18f) : ACCENT;
            if (Btn(lx+iw-50, iy+2, 48, 18, has?"Remove":"Add", btnColor2)) {
#if UNITY_EDITOR
                if (has) UnityEditor.Undo.DestroyObjectImmediate(_selectedGO.GetComponent<Rigidbody2D>());
                else     UnityEditor.Undo.AddComponent<Rigidbody2D>(_selectedGO);
#else
                if (has) Destroy(_selectedGO.GetComponent<Rigidbody2D>());
                else     _selectedGO.AddComponent<Rigidbody2D>();
#endif
            }
            iy += 24;
        }
        // BoxCollider2D
        {
            bool has = _selectedGO.GetComponent<BoxCollider2D>() != null;
            Box(lx, iy, iw, 22, INSP_FIELD);
            DrawRect(new Rect(lx, iy, 2, 22), has ? C(0.2f,0.85f,0.3f,0.7f) : C(0.4f,0.4f,0.5f,0.4f));
            GUI.Label(new Rect(lx+8, iy+3, iw-56, 16), "Box Collider 2D", _sDim);
            Color btnColor3 = has ? C(0.65f,0.18f,0.18f) : ACCENT;
            if (Btn(lx+iw-50, iy+2, 48, 18, has?"Remove":"Add", btnColor3)) {
#if UNITY_EDITOR
                if (has) UnityEditor.Undo.DestroyObjectImmediate(_selectedGO.GetComponent<BoxCollider2D>());
                else     UnityEditor.Undo.AddComponent<BoxCollider2D>(_selectedGO);
#else
                if (has) Destroy(_selectedGO.GetComponent<BoxCollider2D>());
                else     _selectedGO.AddComponent<BoxCollider2D>();
#endif
            }
            iy += 24;
        }
        // PolygonCollider2D
        {
            bool has = _selectedGO.GetComponent<PolygonCollider2D>() != null;
            Box(lx, iy, iw, 22, INSP_FIELD);
            DrawRect(new Rect(lx, iy, 2, 22), has ? C(0.2f,0.85f,0.3f,0.7f) : C(0.4f,0.4f,0.5f,0.4f));
            GUI.Label(new Rect(lx+8, iy+3, iw-56, 16), "Polygon Collider 2D", _sDim);
            Color btnColor4 = has ? C(0.65f,0.18f,0.18f) : ACCENT;
            if (Btn(lx+iw-50, iy+2, 48, 18, has?"Remove":"Add", btnColor4)) {
#if UNITY_EDITOR
                if (has) UnityEditor.Undo.DestroyObjectImmediate(_selectedGO.GetComponent<PolygonCollider2D>());
                else     UnityEditor.Undo.AddComponent<PolygonCollider2D>(_selectedGO);
#else
                if (has) Destroy(_selectedGO.GetComponent<PolygonCollider2D>());
                else     _selectedGO.AddComponent<PolygonCollider2D>();
#endif
            }
            iy += 24;
        }
    }

    void DrawPopupsOnTop(float sw, float sh)
    {
        float rx = sw - RIGHT_W;
        float iw = RIGHT_W - 16;
        float lx = rx + 4;

        if (_selectedGO == null || _selection.Count == 0) return;
        if (!_orFilePopupOpen) return;

        bool clicked = Input.GetMouseButtonDown(0);
        Vector2 mp = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        float scroll = Input.GetAxis("Mouse ScrollWheel");

        // Shared popup helper — manual rendering, no BeginScrollView
        bool DrawPopup(float popX, float popY, float popW, float popH,
                       string[] items, string currentItem,
                       ref Vector2 scrollPos, out string selectedItem)
        {
            selectedItem = null;
            DrawRect(new Rect(popX+3, popY+3, popW, popH), C(0,0,0,0.4f));
            DrawRect(new Rect(popX, popY, popW, popH), POPUP_BG);
            DrawRect(new Rect(popX, popY, popW, 2), ACCENT);
            DrawRect(new Rect(popX, popY+popH-1, popW, 1), BORDER);
            DrawRect(new Rect(popX, popY, 1, popH), BORDER);
            DrawRect(new Rect(popX+popW-1, popY, 1, popH), BORDER);

            float itemH = 22f;
            float totalH = items.Length * itemH;
            float maxScroll = Mathf.Max(0, totalH - popH + 4);

            bool mouseOver = new Rect(popX, popY, popW, popH).Contains(mp);
            if (mouseOver && Mathf.Abs(scroll) > 0.001f)
                scrollPos.y = Mathf.Clamp(scrollPos.y - scroll * 120f, 0, maxScroll);

            if (maxScroll > 0) {
                float trackH = popH - 4f;
                float thumbH = Mathf.Max(20f, trackH * (popH / totalH));
                float thumbY = popY + 2f + (scrollPos.y / maxScroll) * (trackH - thumbH);
                DrawRect(new Rect(popX+popW-6, popY+2, 4, trackH), C(0,0,0,0.3f));
                DrawRect(new Rect(popX+popW-6, thumbY, 4, thumbH), C(ACCENT.r,ACCENT.g,ACCENT.b,0.5f));
            }

            int first = Mathf.Max(0, Mathf.FloorToInt(scrollPos.y / itemH));
            int last  = Mathf.Min(items.Length - 1, Mathf.CeilToInt((scrollPos.y + popH) / itemH));
            for (int i = first; i <= last; i++) {
                float ry2 = popY + 2 + i * itemH - scrollPos.y;
                if (ry2 + itemH < popY || ry2 > popY + popH) continue;
                Rect r = new Rect(popX+1, ry2, popW - (maxScroll > 0 ? 8f : 2f), itemH);
                bool isCur = items[i] == currentItem;
                bool isHov = r.Contains(mp);
                DrawRect(r, isCur ? ROW_SEL : (isHov ? ROW_HOV : (i%2==0 ? ROW_EVEN : ROW_ODD)));
                GUI.Label(new Rect(popX+10, ry2+3, popW-(maxScroll>0?20f:12f), itemH-4),
                          items[i], isCur ? _sRowSel : _sRow);
                if (clicked && r.Contains(mp)) { selectedItem = items[i]; return true; }
            }
            return false;
        }

        Rect orRect = new Rect(lx+88f, sh*0.25f, iw-92f, Mathf.Min(OR_FILENAMES.Length*22f+6f, 260f));
        if (clicked && !orRect.Contains(mp)) {
            _orFilePopupOpen = false;
            _popupClosedThisFrame = true;
            return;
        }
        if (Event.current.isMouse && Event.current.type != EventType.ScrollWheel
            && orRect.Contains(Event.current.mousePosition))
            Event.current.Use();

        // ── OBJECT REFERENCE FILENAME POPUP ──────────────────────
        if (_orFilePopupOpen && _orFilePopupTarget != null) {
            float popH = Mathf.Min(OR_FILENAMES.Length*22f+6f, 260f);
            string curName = _orFilePopupTarget.FileName.ToString();
            string sel2;
            if (DrawPopup(lx+88f, sh*0.25f, iw-92f, popH, OR_FILENAMES, curName, ref _orFilePopupScroll, out sel2)) {
                if (System.Enum.TryParse(sel2, out ObjectReference.Filename fn))
                    _orFilePopupTarget.FileName = fn;
                _orFilePopupOpen = false;
                _popupClosedThisFrame = true;
            }
        }
    }

    void PlacePng(string fullPath)
    {
        if (!RuntimeStreamingSpriteCache.TryGetOrCreateSprite(fullPath, out var sprite, out var error)) {
            Debug.LogError("[Vectorier] " + error);
            return;
        }

        string objName = Path.GetFileNameWithoutExtension(fullPath);
        var go = new GameObject(objName);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;

        // Auto-assign tag based on sprite name (mirrors AutomaticTagApplier)
        string spriteName = Path.GetFileNameWithoutExtension(fullPath).ToLower();
        string autoTag = "Image";
        if (spriteName.StartsWith("collision"))      autoTag = "Platform";
        else if (spriteName.StartsWith("trapezoid_"))autoTag = "Trapezoid";
        else if (spriteName.StartsWith("trigger"))   autoTag = "Trigger";
        else if (spriteName.StartsWith("trick"))     autoTag = "Area";
        try { go.tag = autoTag; } catch {}

        // Auto-assign sorting layer based on sprite name
        string autoLayer = "Default";
        if      (spriteName.StartsWith("black"))      autoLayer = "Black";
        else if (spriteName.Contains("gradient"))     autoLayer = "Shadows";
        else if (spriteName.StartsWith("walls"))      autoLayer = "Wall";
        else if (spriteName.StartsWith("collision"))  autoLayer = "Collision";
        sr.sortingLayerName = autoLayer;

        // Place at camera centre
        if (_cam != null)
            go.transform.position = new Vector3(
                _cam.transform.position.x, _cam.transform.position.y, 0f);
        go.transform.localScale = Vector3.one;

#if UNITY_EDITOR
        Undo.RegisterCreatedObjectUndo(go, "Place PNG");
        Selection.activeGameObject = go;
#endif

        _selection.Clear();
        _selection.Add(go);
        _selectedGO = go;
        _dragging = false;
        _dragOffsets.Clear();
        InvalidateSRCache();
    }

    // ── prefab placement ──────────────────────────────────────────

    void PlacePrefab(string fullPath)
    {
#if UNITY_EDITOR
        string rel="Assets/StreamingAssets/"+
            fullPath.Replace("\\","/")
                    .Replace(Application.streamingAssetsPath.Replace("\\","/"),"")
                    .TrimStart('/');
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(rel);
        if (prefab==null) {
            string nm=Path.GetFileNameWithoutExtension(fullPath);
            string[] found=AssetDatabase.FindAssets(nm+" t:Prefab");
            if (found.Length>0)
                prefab=AssetDatabase.LoadAssetAtPath<GameObject>(
                    AssetDatabase.GUIDToAssetPath(found[0]));
        }
        if (prefab!=null) {
            var go=PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (go!=null&&_cam!=null)
                go.transform.position=new Vector3(
                    _cam.transform.position.x,_cam.transform.position.y,0f);
            Undo.RegisterCreatedObjectUndo(go,"Place Prefab");
            InvalidateSRCache(); // Ensure newly placed prefab is in hit detection cache
            _selection.Clear();
            _selection.Add(go);
            _selectedGO=go;
            Selection.activeGameObject=go;
            _dragging=false;
            _dragOffsets.Clear();
        } else Debug.LogWarning("[Vectorier] Prefab not found: "+fullPath);
#else
        // Runtime: search all prefabs loaded from Resources folder
        string nm = Path.GetFileNameWithoutExtension(fullPath);
        GameObject prefab = null;
        // Load ALL prefabs from Resources and find by name — works with any subfolder structure
        var allPrefabs = Resources.LoadAll<GameObject>("");
        foreach (var p in allPrefabs) {
            if (p.name.Equals(nm, StringComparison.OrdinalIgnoreCase)) {
                prefab = p; break;
            }
        }
        if (prefab != null) {
            var go = Instantiate(prefab);
            if (go != null) {
                go.name = prefab.name;
                if (_cam != null)
                    go.transform.position = new Vector3(
                        _cam.transform.position.x, _cam.transform.position.y, 0f);
            }
            InvalidateSRCache(); // Ensure newly placed prefab is in hit detection cache
            _selection.Clear();
            _selection.Add(go);
            _selectedGO = go;
            _dragging = false;
            _dragOffsets.Clear();
        } else {
            Debug.LogWarning("[Vectorier] Runtime: Prefab '" + nm +
                "' not found in Resources. Make sure it's inside a Resources folder.");
        }
#endif
    }

    // ── grid overlay (Vec2 scale) ─────────────────────────────────
    // Vec2 units: 1 Unity unit = 100 Vec2.  Minor=50, Major=100.
    void DrawGrid(float sw, float sh)
    {
        if (_cam == null) return;
        const float MINOR = 50f;   // Vec2 units per minor line
        const float MAJOR = 100f;  // Vec2 units per major line

        float viewL = LEFT_W, viewR = sw - RIGHT_W;
        float viewT = TOOLBAR_H + 48f; // leave room for toolbar + grid buttons
        float viewB = sh;

        // World extents visible on screen
        Vector3 wtl = _cam.ScreenToWorldPoint(new Vector3(viewL, sh - viewT, 0f));
        Vector3 wbr = _cam.ScreenToWorldPoint(new Vector3(viewR, sh - viewB, 0f));

        // Convert to Vec2 space (Y flips)
        float v2L =  wtl.x * 100f, v2R =  wbr.x * 100f;
        float v2T = -wtl.y * 100f, v2B = -wbr.y * 100f;
        if (v2T > v2B) { float tmp = v2T; v2T = v2B; v2B = tmp; }

        // Only draw minor lines if they're at least 4px apart
        Vector3 pa = _cam.WorldToScreenPoint(Vector3.zero);
        Vector3 pb = _cam.WorldToScreenPoint(new Vector3(MINOR / 100f, 0f, 0f));
        float pxPerMinor = Mathf.Abs(pb.x - pa.x);
        float step = pxPerMinor >= 4f ? MINOR : MAJOR;

        Color minorCol  = new Color(0.20f, 0.20f, 0.25f, 0.55f);
        Color majorCol  = new Color(0.30f, 0.30f, 0.40f, 0.80f);
        Color originCol = new Color(0.38f, 0.55f, 0.90f, 0.90f);

        // Vertical lines
        float startV = Mathf.Floor(v2L / MAJOR) * MAJOR;
        for (float v = startV; v <= v2R + step; v += step) {
            float sv = Mathf.Round(v / step) * step;
            bool  isMaj = Mathf.Abs(sv % MAJOR) < 0.01f;
            bool  isOri = Mathf.Abs(sv) < 0.01f;
            Color col   = isOri ? originCol : isMaj ? majorCol : minorCol;
            float lw    = isOri ? 2f : 1f;
            float ux    = sv / 100f;
            Vector3 sp  = _cam.WorldToScreenPoint(new Vector3(ux, 0f, 0f));
            float sx     = sp.x;
            if (sx < viewL || sx > viewR) continue;
            DrawRect(new Rect(sx - lw * 0.5f, viewT, lw, viewB - viewT), col);
            if (isMaj && pxPerMinor >= 16f)
                GUI.Label(new Rect(sx + 2f, viewT + 2f, 60f, 14f), sv.ToString("0"),
                    new GUIStyle(_sDim){fontSize=9});
        }

        // Horizontal lines
        float startH = Mathf.Floor(v2T / MAJOR) * MAJOR;
        for (float v = startH; v <= v2B + step; v += step) {
            float sv  = Mathf.Round(v / step) * step;
            bool  isMaj = Mathf.Abs(sv % MAJOR) < 0.01f;
            bool  isOri = Mathf.Abs(sv) < 0.01f;
            Color col   = isOri ? originCol : isMaj ? majorCol : minorCol;
            float lh    = isOri ? 2f : 1f;
            float uy    = -sv / 100f;
            Vector3 sp  = _cam.WorldToScreenPoint(new Vector3(0f, uy, 0f));
            float sy    = sh - sp.y;
            if (sy < viewT || sy > viewB) continue;
            DrawRect(new Rect(viewL, sy - lh * 0.5f, viewR - viewL, lh), col);
            if (isMaj && pxPerMinor >= 16f)
                GUI.Label(new Rect(viewL + 2f, sy + 2f, 60f, 14f), sv.ToString("0"),
                    new GUIStyle(_sDim){fontSize=9});
        }

        // Snap info in bottom-left of viewport
        string info = _snapEnabled
            ? $"Grid 50/100  Snap {_snapSize} Vec2"
            : "Grid 50/100  Snap OFF";
        DrawRect(new Rect(viewL + 4f, viewB - 20f, 200f, 16f), new Color(0,0,0,0.4f));
        GUI.Label(new Rect(viewL + 6f, viewB - 20f, 200f, 16f), info,
            new GUIStyle(_sDim){fontSize=9});
    }

    // ── world overlay: origin crosshair + coordinate HUD ─────────

    // ── inspector helpers ─────────────────────────────────────────

    // Returns new iy. isOpen = true if section is expanded.
    float InspSec(string title, float lx, float iy, float iw) {
        bool wasOpen; InspSec(title, lx, ref iy, iw, out wasOpen); return iy;
    }
    float InspSec(string title, float lx, float iy, float iw, out bool isOpen) {
        InspSec(title, lx, ref iy, iw, out isOpen); return iy;
    }
    void InspSec(string title, float lx, ref float iy, float iw, out bool isOpen) {
        bool collapsed = _collapsedSections.Contains(title);
        isOpen = !collapsed;
        Box(lx, iy, iw, 24, INSP_SEC);
        Box(lx, iy, 3, 24, ACCENT);
        Box(lx, iy, iw, 1, BORDER_LT);
        // Arrow indicator
        string arrow = collapsed ? "▶" : "▼";
        GUI.Label(new Rect(lx+8, iy+4, 14, 16), arrow, _sDim);
        GUI.Label(new Rect(lx+22, iy+4, iw-26, 16), title, _sInspSection);
        if (Event.current.type == EventType.MouseDown &&
            new Rect(lx, iy, iw, 24).Contains(Event.current.mousePosition)) {
            if (collapsed) _collapsedSections.Remove(title);
            else           _collapsedSections.Add(title);
            Event.current.Use();
        }
        iy += 26;
    }

    float LabelRow(string label, float lx, float iy, float iw, string val) {
        Box(lx, iy, iw, 22, INSP_FIELD);
        DrawRect(new Rect(lx, iy, 2, 22), ACCENT);
        GUI.Label(new Rect(lx+6,  iy+3, 84,      16), label, _sInspSection);
        GUI.Label(new Rect(lx+90, iy+3, iw-94,   16), val,   _sInspValue);
        return iy + 24;
    }

    const float NUDGE_SM = 0.001f;
    const float NUDGE_LG = 0.01f;

    float Vec3Row(string label, float lx, float iy, float iw,
                  Vector3 v, Action<Vector3> set)
    {
        float aw  = 16f;
        float gap = 2f;
        float fw  = (iw - 80 - (aw * 2 + gap) * 3) / 3f - 2f;
        float fx  = lx + 78;

        // label + X Y Z header row — taller so text is readable
        Box(lx, iy, iw, 16, INSP_FIELD);
        DrawRect(new Rect(lx, iy, 2, 16), ACCENT);
        GUI.Label(new Rect(lx+6, iy+2, 72, 14), label, _sInspSection);
        float colW = aw * 2 + gap + fw + 2f;
        string[] axes = { "X", "Y", "Z" };
        for (int a = 0; a < 3; a++) {
            float cx = fx + a * colW;
            GUI.Label(new Rect(cx + aw + gap * 0.5f, iy+2, fw, 13), axes[a], _sHeader);
        }
        iy += 16;

        // arrow buttons + text fields row
        Box(lx, iy, iw, 20, INSP_FIELD);
        float[] vals  = { v.x, v.y, v.z };
        bool changed  = false;
        float step    = Event.current.shift ? NUDGE_LG : NUDGE_SM;

        for (int a = 0; a < 3; a++) {
            float cx = fx + a * colW;
            if (Btn(cx, iy+1, aw, 16, "\u25c0", PANEL)) { vals[a] -= step; changed = true; }
            string s = GUI.TextField(new Rect(cx + aw + gap * 0.5f, iy+2, fw, 14),
                                     vals[a].ToString("F3"), _sTextField);
            if (float.TryParse(s, out float parsed) && parsed != vals[a]) {
                vals[a] = parsed; changed = true;
            }
            if (Btn(cx + aw + gap * 0.5f + fw + gap * 0.5f, iy+1, aw, 16, "\u25b6", PANEL)) {
                vals[a] += step; changed = true;
            }
        }
        if (changed) set(new Vector3(vals[0], vals[1], vals[2]));
        return iy + 24;
    }

    void DrawWorldOverlay(float sw, float sh)
    {
        if (_cam == null) return;

        // ── Origin crosshair at world (0,0) ───────────────────────
        Vector3 originScreen = _cam.WorldToScreenPoint(Vector3.zero);
        float ox = originScreen.x;
        float oy = sh - originScreen.y;

        bool originVisible = ox > LEFT_W && ox < sw - RIGHT_W && oy > TOOLBAR_H && oy < sh;
        if (originVisible) {
            float len = 12f;
            DrawRect(new Rect(ox - len, oy - 0.75f, len * 2f, 1.5f), C(1f,0.35f,0.35f,0.85f));
            DrawRect(new Rect(ox - 0.75f, oy - len, 1.5f, len * 2f), C(0.35f,1f,0.35f,0.85f));
            DrawRect(new Rect(ox - 2.5f, oy - 2.5f, 5f, 5f), Color.white);
            GUI.Label(new Rect(ox + 5f, oy - 14f, 30f, 12f), "0,0", new GUIStyle(_sDim){fontSize=9});
        }

        // ── In/Out markers (toggleable) ───────────────────────────
        if (_showInOut) {
            if (Time.time - _markerCacheTime > 2f) {
                _cachedIn  = GameObject.FindGameObjectsWithTag("In");
                _cachedOut = GameObject.FindGameObjectsWithTag("Out");
                _markerCacheTime = Time.time;
            }
            if (_cachedIn  != null) foreach (var go in _cachedIn)  if (go != null) DrawWorldMarker(go.transform.position, "IN",  C(0.3f,1f,0.3f,0.9f), sw, sh);
            if (_cachedOut != null) foreach (var go in _cachedOut) if (go != null) DrawWorldMarker(go.transform.position, "OUT", C(1f,0.4f,0.4f,0.9f), sw, sh);
        }

        // ── Mouse coord / resize HUD at bottom of viewport ───────
        float mx = Input.mousePosition.x;
        bool overViewport = mx > LEFT_W && mx < sw - RIGHT_W;
        if (overViewport) {
            Vector3 mouseWorld = _cam.ScreenToWorldPoint(new Vector3(
                Input.mousePosition.x, Input.mousePosition.y,
                Mathf.Abs(_cam.transform.position.z)));

            string coordText;
            // During resize show live Width/Height in Vector 2 units
            if (_resizing != ResizeHandle.None && _selectedGO != null) {
                var sr = _selectedGO.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null) {
                    float w = sr.sprite.bounds.size.x * Mathf.Abs(_selectedGO.transform.localScale.x) * 100f;
                    float h = sr.sprite.bounds.size.y * Mathf.Abs(_selectedGO.transform.localScale.y) * 100f;
                    coordText = $"W: {Mathf.RoundToInt(w)}  H: {Mathf.RoundToInt(h)}";
                } else {
                    coordText = $"X: {Mathf.RoundToInt(mouseWorld.x*100f)}  Y: {Mathf.RoundToInt(-mouseWorld.y*100f)}";
                }
            } else {
                coordText = $"X: {Mathf.RoundToInt(mouseWorld.x*100f)}  Y: {Mathf.RoundToInt(-mouseWorld.y*100f)}";
            }

            float tw = 160f, th = 18f;
            float tx = LEFT_W + (sw - LEFT_W - RIGHT_W) * 0.5f - tw * 0.5f;
            float ty = sh - th - 6f;
            DrawRect(new Rect(tx-4, ty-2, tw+8, th+4), C(0f,0f,0f,0.55f));
            DrawRect(new Rect(tx-4, ty-2, tw+8, 1), BORDER);
            GUI.Label(new Rect(tx, ty, tw, th), coordText,
                new GUIStyle(_sDim){fontSize=11, alignment=TextAnchor.MiddleCenter});
        }
    }

    void DrawWorldMarker(Vector3 worldPos, string label, Color col, float sw, float sh)
    {
        Vector3 sp = _cam.WorldToScreenPoint(worldPos);
        float sx = sp.x, sy = sh - sp.y;
        if (sx < LEFT_W || sx > sw - RIGHT_W || sy < TOOLBAR_H || sy > sh) return;
        float s = 8f;
        DrawRect(new Rect(sx-s/2, sy-s/2, s, s), col);
        var mkSt = new GUIStyle(_sDim){ fontSize=9 };
        mkSt.normal.textColor = col;
        GUI.Label(new Rect(sx+6, sy-8, 36, 16), label, mkSt);
    }

    void DrawSelectedGizmo()
    {
        if (_cam == null) return;
        // Update SR cache when selection changes
        if (_selectedGO != null && (_cachedSelectedSR == null || _cachedSelectedSR.gameObject != _selectedGO))
            _cachedSelectedSR = _selectedGO.GetComponent<SpriteRenderer>();
        else if (_selectedGO == null)
            _cachedSelectedSR = null;

        Color prev = GUI.color;
        float t = 1.5f;

        for (int s = 0; s < _selection.Count; s++) {
            var go = _selection[s];
            if (go == null) continue;
            SpriteRenderer sr = (go == _selectedGO) ? _cachedSelectedSR : go.GetComponent<SpriteRenderer>();
            if (sr == null) continue;

            bool isPrimary = go == _selectedGO;
            GUI.color = isPrimary ? ACCENT : C(0.22f,0.55f,1.00f,0.45f);

            Bounds b = sr.bounds;
            Vector3[] corners = { new Vector3(b.min.x,b.min.y,0),
                                   new Vector3(b.max.x,b.min.y,0),
                                   new Vector3(b.max.x,b.max.y,0),
                                   new Vector3(b.min.x,b.max.y,0) };
            Vector2[] sc = new Vector2[4];
            for (int i = 0; i < 4; i++) {
                Vector3 sp = _cam.WorldToScreenPoint(corners[i]);
                sc[i] = new Vector2(sp.x, Screen.height - sp.y);
            }
            // outline edges
            for (int i = 0; i < 4; i++) {
                Vector2 a = sc[i], bb = sc[(i+1)%4];
                float mnX=Mathf.Min(a.x,bb.x), mnY=Mathf.Min(a.y,bb.y);
                float mxX=Mathf.Max(a.x,bb.x), mxY=Mathf.Max(a.y,bb.y);
                if (Mathf.Abs(a.x-bb.x) < 0.5f)
                    GUI.DrawTexture(new Rect(mnX-t/2,mnY,t,mxY-mnY),Texture2D.whiteTexture);
                else
                    GUI.DrawTexture(new Rect(mnX,mnY-t/2,mxX-mnX,t),Texture2D.whiteTexture);
            }

            // ── resize handle squares on primary selection only ───
            if (isPrimary && _selection.Count == 1) {
                float h = HANDLE_SCREEN_SIZE;
                GUI.color = Color.white;
                foreach (Vector2 c in sc) {
                    // filled white square
                    GUI.DrawTexture(new Rect(c.x-h/2, c.y-h/2, h, h), Texture2D.whiteTexture);
                    // dark border inside
                    GUI.color = C(0.1f,0.1f,0.1f);
                    GUI.DrawTexture(new Rect(c.x-h/2+1, c.y-h/2+1, h-2, h-2), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }
            }
        }
        GUI.color = prev;

        // ── box-select rectangle ──────────────────────────────────
        if (_boxSelecting && _cam != null) {
            Vector3 curWP = _cam.ScreenToWorldPoint(new Vector3(
                Input.mousePosition.x, Input.mousePosition.y,
                Mathf.Abs(_cam.transform.position.z)));
            curWP.z = 0f;

            // Convert both world corners to screen/GUI space
            Vector3 a = _cam.WorldToScreenPoint(_boxStartWorld);
            Vector3 b2 = _cam.WorldToScreenPoint(curWP);
            float x1 = Mathf.Min(a.x, b2.x), x2 = Mathf.Max(a.x, b2.x);
            float y1 = Mathf.Min(a.y, b2.y), y2 = Mathf.Max(a.y, b2.y);
            // Flip Y for GUI space
            float gy1 = Screen.height - y2;
            float gy2 = Screen.height - y1;
            float rw = x2 - x1, rh = gy2 - gy1;

            // Fill
            GUI.color = C(0.20f,0.52f,0.98f,0.12f);
            GUI.DrawTexture(new Rect(x1,gy1,rw,rh), Texture2D.whiteTexture);
            // Border
            GUI.color = C(0.20f,0.52f,0.98f,0.80f);
            float bt = 1.5f;
            GUI.DrawTexture(new Rect(x1,      gy1,      rw, bt), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x1,      gy2-bt,   rw, bt), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x1,      gy1,      bt, rh), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x2-bt,   gy1,      bt, rh), Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }

    void ApplyTheme()
    {
        // Each theme: (bg, panel, accent, text, textSec)
        switch (_theme) {
            case 1: // Pure Dark
                BG=C(0.04f,0.04f,0.04f); PANEL=C(0.08f,0.08f,0.08f,0.95f); TOOLBAR=C(0.03f,0.03f,0.03f,0.98f);
                ACCENT=C(0.55f,0.55f,0.60f); ACCENT2=C(0.35f,0.35f,0.40f);
                TEXT=C(0.92f,0.92f,0.92f); TEXT_SEC=C(0.60f,0.60f,0.62f);
                ROW_EVEN=C(0.08f,0.08f,0.08f,0.85f); ROW_ODD=C(0.06f,0.06f,0.06f,0.85f);
                ROW_SEL=C(0.30f,0.30f,0.35f,0.90f); ROW_HOV=C(0.15f,0.15f,0.17f,0.70f);
                INSP_SEC=C(0.10f,0.10f,0.10f,0.90f); INSP_FIELD=C(0.07f,0.07f,0.07f,0.80f);
                POPUP_BG=C(0.06f,0.06f,0.06f,0.97f);
                BORDER=C(0.25f,0.25f,0.28f,0.30f); BORDER_LT=C(0.40f,0.40f,0.45f,0.15f); BORDER_GL=C(0.50f,0.50f,0.55f,0.20f);
                break;
            case 2: // Blue
                BG=C(0.03f,0.05f,0.12f); PANEL=C(0.06f,0.09f,0.20f,0.92f); TOOLBAR=C(0.03f,0.05f,0.10f,0.98f);
                ACCENT=C(0.25f,0.60f,1.00f); ACCENT2=C(0.15f,0.45f,0.85f);
                TEXT=C(0.88f,0.93f,1.00f); TEXT_SEC=C(0.50f,0.65f,0.90f);
                ROW_EVEN=C(0.07f,0.10f,0.20f,0.85f); ROW_ODD=C(0.05f,0.08f,0.16f,0.85f);
                ROW_SEL=C(0.18f,0.48f,0.92f,0.90f); ROW_HOV=C(0.10f,0.18f,0.35f,0.70f);
                INSP_SEC=C(0.07f,0.10f,0.22f,0.90f); INSP_FIELD=C(0.05f,0.08f,0.18f,0.80f);
                POPUP_BG=C(0.04f,0.06f,0.15f,0.97f);
                BORDER=C(0.20f,0.40f,0.75f,0.25f); BORDER_LT=C(0.40f,0.65f,1.00f,0.15f); BORDER_GL=C(0.50f,0.75f,1.00f,0.20f);
                break;
            case 3: // Red
                BG=C(0.08f,0.04f,0.04f); PANEL=C(0.12f,0.06f,0.06f,0.92f); TOOLBAR=C(0.07f,0.03f,0.03f,0.98f);
                ACCENT=C(0.88f,0.18f,0.18f); ACCENT2=C(0.65f,0.10f,0.10f);
                TEXT=C(0.97f,0.90f,0.90f); TEXT_SEC=C(0.78f,0.55f,0.55f);
                ROW_EVEN=C(0.12f,0.06f,0.06f,0.85f); ROW_ODD=C(0.09f,0.04f,0.04f,0.85f);
                ROW_SEL=C(0.75f,0.15f,0.15f,0.90f); ROW_HOV=C(0.22f,0.08f,0.08f,0.70f);
                INSP_SEC=C(0.14f,0.07f,0.07f,0.90f); INSP_FIELD=C(0.10f,0.05f,0.05f,0.80f);
                POPUP_BG=C(0.09f,0.04f,0.04f,0.97f);
                BORDER=C(0.60f,0.25f,0.25f,0.25f); BORDER_LT=C(0.85f,0.40f,0.40f,0.15f); BORDER_GL=C(0.90f,0.50f,0.50f,0.20f);
                break;
            case 4: // Lime
                BG=C(0.04f,0.07f,0.03f); PANEL=C(0.07f,0.11f,0.05f,0.92f); TOOLBAR=C(0.03f,0.06f,0.02f,0.98f);
                ACCENT=C(0.48f,0.82f,0.12f); ACCENT2=C(0.32f,0.62f,0.08f);
                TEXT=C(0.88f,0.96f,0.82f); TEXT_SEC=C(0.55f,0.75f,0.40f);
                ROW_EVEN=C(0.07f,0.11f,0.05f,0.85f); ROW_ODD=C(0.05f,0.09f,0.03f,0.85f);
                ROW_SEL=C(0.38f,0.72f,0.10f,0.90f); ROW_HOV=C(0.10f,0.17f,0.06f,0.70f);
                INSP_SEC=C(0.08f,0.13f,0.05f,0.90f); INSP_FIELD=C(0.06f,0.10f,0.04f,0.80f);
                POPUP_BG=C(0.05f,0.08f,0.03f,0.97f);
                BORDER=C(0.30f,0.55f,0.15f,0.25f); BORDER_LT=C(0.50f,0.80f,0.25f,0.15f); BORDER_GL=C(0.60f,0.88f,0.30f,0.20f);
                break;
            case 5: // High Contrast
                BG=C(0.02f,0.02f,0.02f); PANEL=C(0.06f,0.06f,0.06f,0.98f); TOOLBAR=C(0.01f,0.01f,0.01f,1.00f);
                ACCENT=C(0.00f,0.88f,0.88f); ACCENT2=C(0.00f,0.62f,0.62f);
                TEXT=Color.white; TEXT_SEC=C(0.65f,0.85f,0.85f);
                ROW_EVEN=C(0.07f,0.07f,0.07f,0.90f); ROW_ODD=C(0.04f,0.04f,0.04f,0.90f);
                ROW_SEL=C(0.00f,0.60f,0.60f,0.95f); ROW_HOV=C(0.10f,0.20f,0.20f,0.70f);
                INSP_SEC=C(0.08f,0.08f,0.08f,0.95f); INSP_FIELD=C(0.05f,0.05f,0.05f,0.85f);
                POPUP_BG=C(0.04f,0.04f,0.04f,0.99f);
                BORDER=C(0.00f,0.60f,0.60f,0.35f); BORDER_LT=C(0.00f,0.80f,0.80f,0.20f); BORDER_GL=C(0.00f,0.90f,0.90f,0.25f);
                break;
            default: // Deep Slate
                BG=C(0.04f,0.05f,0.07f,0.97f); PANEL=C(0.08f,0.10f,0.14f,0.88f); TOOLBAR=C(0.05f,0.06f,0.09f,0.96f);
                ACCENT=C(0.22f,0.72f,0.98f,1f); ACCENT2=C(0.10f,0.55f,0.85f,1f);
                TEXT=C(0.92f,0.95f,0.98f,1f); TEXT_SEC=C(0.58f,0.68f,0.80f,1f);
                ROW_EVEN=C(0.09f,0.11f,0.15f,0.85f); ROW_ODD=C(0.07f,0.08f,0.12f,0.85f);
                ROW_SEL=C(0.18f,0.52f,0.92f,0.85f); ROW_HOV=C(0.14f,0.18f,0.26f,0.70f);
                INSP_SEC=C(0.10f,0.13f,0.19f,0.90f); INSP_FIELD=C(0.06f,0.08f,0.13f,0.80f);
                POPUP_BG=C(0.05f,0.07f,0.11f,0.97f);
                BORDER=C(0.30f,0.45f,0.65f,0.22f); BORDER_LT=C(0.60f,0.80f,1.00f,0.12f); BORDER_GL=C(0.70f,0.90f,1.00f,0.18f);
                break;
        }
        _stylesReady = false; // force MakeStyles to rebuild with new text style colors
        // Update camera background to match theme's BG color
        if (_cam != null) _cam.backgroundColor = new Color(BG.r * 0.6f, BG.g * 0.6f, BG.b * 0.6f, 1f);
    }

    void ApplySettings()
    {
        if (_vsync) {
            // VSync active — let the display driver control frame rate
            QualitySettings.vSyncCount  = 1;
            Application.targetFrameRate = -1;
        } else {
            // VSync off — use chosen FPS cap
            QualitySettings.vSyncCount  = 0;
            Application.targetFrameRate = (_targetFPS == 0) ? 60 : _targetFPS;
        }
        QualitySettings.antiAliasing = _aaLevel;
        PlayerPrefs.SetFloat("VE_ScrollSpeed", _scrollSpeed);
        PlayerPrefs.SetInt("VE_FPS",   _targetFPS);
        PlayerPrefs.SetInt("VE_AA",    _aaLevel);
        PlayerPrefs.SetInt("VE_VSync", _vsync ? 1 : 0);
        PlayerPrefs.SetInt("VE_Theme", _theme);
        PlayerPrefs.Save();
    }

    void DrawSettingsPanel(float sw, float sh)
    {
        if (!_settingsOpen) return;

        const float PW = 300f;
        const float PH = 310f;
        float px = (sw - PW) * 0.5f;
        float py = (sh - PH) * 0.5f;

        // Shadow + glass panel
        DrawRect(new Rect(px+4, py+4, PW, PH), C(0,0,0,0.6f));
        DrawRect(new Rect(px, py, PW, PH), C(0.06f,0.08f,0.13f,0.97f));
        DrawRect(new Rect(px, py, PW, 1),  BORDER_GL);
        DrawRect(new Rect(px, py+PH-1, PW, 1), BORDER);
        DrawRect(new Rect(px, py, 1, PH),  BORDER);
        DrawRect(new Rect(px+PW-1, py, 1, PH), BORDER);
        DrawRect(new Rect(px, py, 3, PH),  ACCENT);

        // Title bar
        DrawRect(new Rect(px+3, py, PW-3, 30), C(0.07f,0.09f,0.14f,0.95f));
        DrawRect(new Rect(px, py+30, PW, 1), BORDER);
        GUI.Label(new Rect(px+14, py+7, PW-60, 18), "Settings", _sInspSection);
        if (Btn(px+PW-32, py+5, 24, 20, "✕", C(0.5f,0.1f,0.1f,0.8f))) _settingsOpen = false;

        float iy = py + 38f;
        float lx = px + 10f;
        float fw = PW - 20f;

        // Mouse Scroll Speed
        DrawRect(new Rect(lx, iy, fw, 22), INSP_FIELD);
        GUI.Label(new Rect(lx+4, iy+3, 110, 16), "Scroll Speed", _sDim);
        float newSpd = GUI.HorizontalSlider(new Rect(lx+116, iy+6, fw-160, 12), _scrollSpeed, 1f, 10f);
        GUI.Label(new Rect(lx+fw-38, iy+3, 36, 16), _scrollSpeed.ToString("F1"), _sDim);
        if (Mathf.Abs(newSpd - _scrollSpeed) > 0.01f) { _scrollSpeed = newSpd; ApplySettings(); }
        iy += 26f;

        // FPS Cap — give each button equal space that fits the labels
        DrawRect(new Rect(lx, iy, fw, 22), INSP_FIELD);
        GUI.Label(new Rect(lx+4, iy+3, 60, 16), "FPS Cap", _sDim);
        float fpsAreaW = fw - 68f;
        float bw = fpsAreaW / FPS_LABELS.Length;
        for (int i = 0; i < FPS_LABELS.Length; i++) {
            bool active = _targetFPS == FPS_OPTIONS[i];
            if (Btn(lx+66+i*bw, iy+2, bw-2, 18, FPS_LABELS[i], active ? ACCENT : PANEL)) {
                _targetFPS = FPS_OPTIONS[i]; ApplySettings();
            }
        }
        iy += 26f;

        // VSync
        DrawRect(new Rect(lx, iy, fw, 22), INSP_FIELD);
        GUI.Label(new Rect(lx+4, iy+3, 100, 16), "VSync", _sDim);
        bool vsNew = GUI.Toggle(new Rect(lx+fw-20, iy+4, 16, 16), _vsync, "");
        if (vsNew != _vsync) { _vsync = vsNew; ApplySettings(); }
        iy += 26f;

        // Fullscreen
        DrawRect(new Rect(lx, iy, fw, 22), INSP_FIELD);
        GUI.Label(new Rect(lx+4, iy+3, 100, 16), "Fullscreen", _sDim);
        bool fsNew = GUI.Toggle(new Rect(lx+fw-20, iy+4, 16, 16), Screen.fullScreen, "");
        if (fsNew != Screen.fullScreen) Screen.fullScreen = fsNew;
        iy += 26f;

        // In/Out Markers
        DrawRect(new Rect(lx, iy, fw, 22), INSP_FIELD);
        GUI.Label(new Rect(lx+4, iy+3, 120, 16), "In/Out Markers", _sDim);
        _showInOut = GUI.Toggle(new Rect(lx+fw-20, iy+4, 16, 16), _showInOut, "");
        iy += 26f;

        // V2 Dimensions (deprecated)
        DrawRect(new Rect(lx, iy, fw, 22), INSP_FIELD);
        GUI.Label(new Rect(lx+4, iy+3, fw-30, 16), "V2 Dimensions (deprecated)", _sDim);
        bool v2New = GUI.Toggle(new Rect(lx+fw-20, iy+4, 16, 16), _showV2Dim, "");
        if (v2New != _showV2Dim) _showV2Dim = v2New;
        iy += 26f;

        // Theme — label on its own row, then two rows of 3 buttons each
        GUI.Label(new Rect(lx+4, iy+3, 50, 16), "Theme", _sDim);
        iy += 20f;
        string[] themeNames = { "Slate", "Dark", "Blue", "Red", "Lime", "Contrast" };
        float tbw = fw / 3f;
        for (int ti = 0; ti < 6; ti++) {
            int col = ti % 3; int row = ti / 3;
            bool isActive = _theme == ti;
            if (Btn(lx + col * tbw, iy + row * 24f, tbw - 2f, 22f, themeNames[ti], isActive ? ACCENT : PANEL)) {
                _theme = ti; ApplyTheme(); _stylesReady = false; ApplySettings();
            }
        }

        // Eat mouse click events on panel but NOT scroll (so inspector behind can still scroll)
        if (Event.current.isMouse && Event.current.type != EventType.ScrollWheel &&
            new Rect(px, py, PW, PH).Contains(Event.current.mousePosition))
            Event.current.Use();
    }

    // ── hierarchy helpers ─────────────────────────────────────────

    void CollectHierarchy(GameObject go,List<GameObject> list) {
        list.Add(go);
        foreach (Transform c in go.transform) CollectHierarchy(c.gameObject,list);
    }
    int GetDepth(GameObject go) {
        int d=0; Transform t=go.transform;
        while (t.parent!=null){d++;t=t.parent;} return d;
    }

    // ── asset selection ───────────────────────────────────────────

    void SelectAsset(Node n) {
        _selectedAsset=n; _previewTex=null;
        _previewName=n.Name; _previewMeta="";
        if (n.IsDir) {
            _previewMeta=$"{n.Children.FindAll(c=>c.IsDir).Count} folders  "+
                         $"{n.Children.FindAll(c=>!c.IsDir).Count} files";
            return;
        }
        try {
            var fi=new FileInfo(n.Path);
            _previewMeta=KindLabel(n)+"  "+FileSz(fi.Length)+"\n"+
                n.Path.Replace(Application.streamingAssetsPath,"~");
        } catch {}
        string ext=Path.GetExtension(n.Name).ToLower();
        if (ext==".png"||ext==".jpg"||ext==".jpeg") {
            try { _previewTex=new Texture2D(2,2);
                  _previewTex.LoadImage(File.ReadAllBytes(n.Path)); }
            catch { _previewTex=null; }
        }
    }

    // ── scene ops ─────────────────────────────────────────────────

#if UNITY_EDITOR
    void NewScene() =>
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,NewSceneMode.Single);
    void OpenScene() {
        string p=EditorUtility.OpenFilePanel("Open Scene","Assets","unity");
        if (!string.IsNullOrEmpty(p))
            EditorSceneManager.OpenScene(p,OpenSceneMode.Single);
    }
    void SaveScene() {
        var scene=SceneManager.GetActiveScene();
        if (!string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene);
        else {
            string p=EditorUtility.SaveFilePanelInProject(
                "Save Scene","NewScene","unity","Save scene as");
            if (!string.IsNullOrEmpty(p)) EditorSceneManager.SaveScene(scene,p);
        }
    }
#endif

    // ── styles ────────────────────────────────────────────────────

    void MakeStyles() {
        if (_stylesReady) return;
        _stylesReady=true;

        // Apply theme — override the static color constants with theme values
        // Local aliases for static color fields used in style building
        Color T    = TEXT;
        Color Tsec = TEXT_SEC;
        Color Acc  = ACCENT;

        _sRow=new GUIStyle(GUI.skin.label){
            normal={textColor=T},fontSize=12,
            alignment=TextAnchor.MiddleLeft,
            wordWrap=false,clipping=TextClipping.Clip,
            padding=new RectOffset(4,4,0,0)};
        _sRowSel=new GUIStyle(_sRow){
            normal={textColor=Color.white},fontStyle=FontStyle.Bold,
            wordWrap=false,clipping=TextClipping.Clip};
        _sHierRow=new GUIStyle(_sRow){fontSize=12,wordWrap=false,clipping=TextClipping.Clip};
        _sHierSel=new GUIStyle(_sRowSel){fontSize=12,wordWrap=false,clipping=TextClipping.Clip};

        _sLabel=new GUIStyle(GUI.skin.label){
            normal={textColor=T},fontSize=12,alignment=TextAnchor.MiddleLeft};

        // Dim labels — was too faded (0.58), now readable
        _sDim=new GUIStyle(GUI.skin.label){
            normal={textColor=C(0.78f,0.86f,0.95f)},fontSize=12,
            alignment=TextAnchor.MiddleLeft,wordWrap=false};

        // Search
        _sSearch=new GUIStyle(GUI.skin.textField){
            normal  ={background=Tex(C(0.08f,0.11f,0.17f,0.85f)),textColor=T},
            focused ={background=Tex(C(0.10f,0.18f,0.28f,0.95f)),textColor=Color.white},
            fontSize=12,alignment=TextAnchor.MiddleLeft,
            padding=new RectOffset(10,8,3,3)};

        // Text field
        _sTextField=new GUIStyle(GUI.skin.textField){
            normal  ={background=Tex(C(0.06f,0.09f,0.15f,0.88f)),textColor=T},
            focused ={background=Tex(C(0.10f,0.18f,0.30f,0.95f)),textColor=Color.white},
            fontSize=12,alignment=TextAnchor.MiddleLeft,
            padding=new RectOffset(5,5,2,2)};

        // Section axis labels (X Y Z) — brighter, easier to read
        _sHeader=new GUIStyle(GUI.skin.label){
            normal={textColor=C(0.65f,0.85f,1.00f)},fontSize=10,
            fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter,
            padding=new RectOffset(2,2,0,0)};

        // Button
        _sBtn=new GUIStyle(GUI.skin.button){
            normal ={background=Tex(C(0.10f,0.14f,0.22f,0.80f)),textColor=T},
            hover  ={background=Tex(C(0.16f,0.30f,0.50f,0.90f)),textColor=Color.white},
            active ={background=Tex(C(0.10f,0.55f,0.85f,1.00f)),textColor=Color.white},
            fontSize=12,alignment=TextAnchor.MiddleCenter,
            border=new RectOffset(4,4,4,4),
            padding=new RectOffset(6,6,3,3)};

        // Inspector value text
        _sInspValue=new GUIStyle(GUI.skin.label){
            normal={textColor=T},fontSize=12,alignment=TextAnchor.MiddleLeft};

        // Inspector section headers — bright white, clearly legible
        _sInspSection=new GUIStyle(GUI.skin.label){
            normal={textColor=Color.white},fontSize=12,
            fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleLeft};

        _sPopupItem=new GUIStyle(GUI.skin.label){
            normal={textColor=T},fontSize=12,alignment=TextAnchor.MiddleLeft,
            padding=new RectOffset(10,8,0,0)};
        _sPopupItemSel=new GUIStyle(_sPopupItem){
            normal={textColor=Color.white},fontStyle=FontStyle.Bold};
    }

    // ── ObjectReference editor state ──────────────────────────────
    bool _orRawXmlOpen = false;

    // Known trigger variable names for structured editing
    static readonly string[] TRIGGER_VAR_NAMES = {
        "PreferredStunt", "Rarity", "ImageName", "TriggerName",
        "Active", "Node", "AI", "Flag1", "Type", "Direction"
    };
    static readonly string[] TRIGGER_TYPES = {
        "E_AreaTrick", "E_GroundTrick", "E_WallTrick", "E_AirTrick",
        "E_GrindTrick", "E_Stunt", "E_Jump", "E_Slide"
    };

    float DrawTriggersEditor(ObjectReference objRef, float lx, float iy, float iw) {
        // Parse existing variables from XML
        var vars = ParseVariables(objRef.CustomVariables);

        bool tvOpen;
        iy = InspSec("Trigger Variables", lx, iy, iw, out tvOpen);
        bool changed = false;
        if (tvOpen) {
        foreach (var key in TRIGGER_VAR_NAMES) {
            string val = vars.ContainsKey(key) ? vars[key] : "";
            Box(lx, iy, iw, 18, INSP_FIELD);
            GUI.Label(new Rect(lx+4, iy+2, iw-8, 14), key, _sHeader);
            iy += 20;
            Box(lx, iy, iw, 20, INSP_FIELD);

            // Type field gets a dropdown
            if (key == "Type" && vars.ContainsKey(key)) {
                int tIdx = System.Array.IndexOf(TRIGGER_TYPES, val);
                if (Btn(lx+4, iy+2, iw-8, 16, tIdx >= 0 ? TRIGGER_TYPES[tIdx] : val, INSP_FIELD)) {
                    tIdx = (tIdx + 1) % TRIGGER_TYPES.Length;
                    vars[key] = TRIGGER_TYPES[tIdx];
                    changed = true;
                }
            } else {
                string nv = GUI.TextField(new Rect(lx+4, iy+2, iw-8, 16), val, _sTextField);
                if (nv != val) { vars[key] = nv; changed = true; }
            }
            iy += 24;
        }

        } // end tvOpen
        if (changed) objRef.CustomVariables = BuildVariablesXml(vars);
        return iy;
    }

    float DrawVariableEditor(ObjectReference objRef, float lx, float iy, float iw) {
        var vars = ParseVariables(objRef.CustomVariables);

        bool vOpen;
        iy = InspSec("Variables", lx, iy, iw, out vOpen);
        bool changed = false;
        if (vOpen) {
        var keys = new List<string>(vars.Keys);
        string removeKey = null;
        foreach (var key in keys) {
            Box(lx, iy, iw, 20, INSP_FIELD);
            // Editable key
            string nk = GUI.TextField(new Rect(lx+4, iy+2, iw/2-24, 16), key, _sTextField);
            if (Btn(lx+iw-18, iy+2, 16, 16, "x", C(0.6f,0.2f,0.2f))) removeKey = key;
            iy += 22;
            Box(lx, iy, iw, 20, INSP_FIELD);
            string nv = GUI.TextField(new Rect(lx+4, iy+2, iw-8, 16), vars[key], _sTextField);
            if (nk != key || nv != vars[key]) {
                vars.Remove(key);
                vars[nk] = nv;
                changed = true;
            }
            iy += 24;
        }
        if (removeKey != null) { vars.Remove(removeKey); changed = true; }

        if (Btn(lx, iy, iw/2-2, 20, "+ Add Variable", PANEL)) {
            vars["NewVar"] = "";
            changed = true;
        }
        iy += 24;

        } // end vOpen
        if (changed) objRef.CustomVariables = BuildVariablesXml(vars);
        return iy;
    }

    // Parse <Variable Name="X" Value="Y"/> entries from CustomVariables XML
    static Dictionary<string,string> ParseVariables(string xml) {
        var result = new Dictionary<string,string>();
        if (string.IsNullOrEmpty(xml)) return result;
        try {
            var matches = System.Text.RegularExpressions.Regex.Matches(
                xml, @"<Variable\s+Name=""([^""]+)""\s+(?:Type=""[^""]*""\s+)?Value=""([^""]*)""\s*/>");
            foreach (System.Text.RegularExpressions.Match m in matches)
                result[m.Groups[1].Value] = m.Groups[2].Value;
        } catch {}
        return result;
    }

    // Rebuild CustomVariables XML from key/value dict, preserving <Static><OverrideVariable> wrapper
    static string BuildVariablesXml(Dictionary<string,string> vars) {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<Static>");
        sb.AppendLine("  <OverrideVariable>");
        foreach (var kv in vars)
            sb.AppendLine($"    <Variable Name=\"{kv.Key}\" Value=\"{kv.Value}\" />");
        sb.AppendLine("  </OverrideVariable>");
        sb.Append("</Static>");
        return sb.ToString();
    }

    // ── draw helpers ──────────────────────────────────────────────

    bool Btn(float x,float y,float w,float h,string lbl,Color col) {
        Color p=GUI.backgroundColor; GUI.backgroundColor=col;
        bool r=GUI.Button(new Rect(x,y,w,h),lbl,_sBtn);
        GUI.backgroundColor=p; return r;
    }
    static void Box(float x,float y,float w,float h,Color col)
        =>DrawRect(new Rect(x,y,w,h),col);
    static void Line(float x,float y,float w,float h,Color col)
        =>DrawRect(new Rect(x,y,w,h),col);
    static void Line(float x,float y,float w,Color col)
        =>DrawRect(new Rect(x,y,w,1),col);
    static void DrawRect(Rect r,Color col){
        Color p=GUI.color; GUI.color=col;
        GUI.DrawTexture(r,Texture2D.whiteTexture); GUI.color=p;
    }

    // ── badge helpers ─────────────────────────────────────────────

    static string Badge(Node n) {
        if (n.IsDir) return n.Expanded?"[-]":"[+]";
        switch (Path.GetExtension(n.Name).ToLower()) {
            case ".png":case ".jpg":case ".jpeg":case ".tga": return "IMG";
            case ".prefab": return "PRE";
            case ".xml":    return "XML";
            case ".dcl":    return "DCL";
            case ".wav":case ".mp3":case ".ogg": return "SND";
            case ".unity":  return "SCN";
            case ".cs":     return " CS";
            default:        return "---";
        }
    }
    static Color BadgeCol(Node n) {
        if (n.IsDir) return COL_DIR;
        switch (Path.GetExtension(n.Name).ToLower()) {
            case ".png":case ".jpg":case ".jpeg":case ".tga": return COL_IMG;
            case ".prefab": return COL_PRE;
            case ".xml":case ".dcl": return COL_XML;
            case ".unity":  return COL_SCN;
            default:        return COL_OTH;
        }
    }
    static string KindLabel(Node n) {
        switch (Path.GetExtension(n.Name).ToLower()) {
            case ".png":case ".jpg": return "Image";
            case ".prefab": return "Prefab";
            case ".xml":    return "XML";
            case ".unity":  return "Scene";
            default: return Path.GetExtension(n.Name).TrimStart('.');
        }
    }
    static string FileSz(long b) {
        if (b<1024)    return b+" B";
        if (b<1048576) return (b/1024f).ToString("F1")+" KB";
        return (b/1048576f).ToString("F1")+" MB";
    }
}
