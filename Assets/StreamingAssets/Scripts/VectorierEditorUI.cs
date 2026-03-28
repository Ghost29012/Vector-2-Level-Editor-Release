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
//
//  FIXES IN THIS VERSION:
//  1. BuildMapVec2 fields (Map To Override) editable in inspector
//  2. Rooms Directory editable directly in the UI (no project settings needed)
//  3. Inspector layout fixed — no more overlapping
//  4. Sorting layers hardcoded from actual project layer list
// ================================================================

public class VectorierEditorUI : MonoBehaviour
{
    // ── layout ───────────────────────────────────────────────────
    const float LEFT_W    = 260f;
    const float RIGHT_W   = 300f;
    const float TOOLBAR_H = 36f;
    const float ROW_H     = 22f;
    const float HIER_H    = 220f;
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

    // ── camera ───────────────────────────────────────────────────
    Camera  _cam;
    bool    _panning;
    Vector3 _panOrigin, _camOrigin;

    // ── scene selection + drag ───────────────────────────────────
    GameObject _selectedGO;
    bool       _dragging;
    Vector3    _dragOffset;

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

    // ── inspector ────────────────────────────────────────────────
    Vector2 _inspScroll;

    // ── vectorier settings (rooms directory) ─────────────────────
    string _roomsDirectory = "";

    // ── styles ───────────────────────────────────────────────────
    GUIStyle _sRow, _sRowSel, _sLabel, _sDim, _sSearch,
             _sHeader, _sBtn, _sInspValue, _sInspSection,
             _sHierRow, _sHierSel, _sTextField;
    bool _stylesReady;

    // ── colours ──────────────────────────────────────────────────
    static Color C(float r,float g,float b,float a=1f) => new Color(r,g,b,a);
    static readonly Color BG        = C(0.08f,0.08f,0.09f,0.97f);
    static readonly Color PANEL     = C(0.11f,0.11f,0.13f,0.95f);
    static readonly Color TOOLBAR   = C(0.09f,0.09f,0.11f,1.00f);
    static readonly Color ROW_EVEN  = C(0.12f,0.12f,0.14f);
    static readonly Color ROW_ODD   = C(0.10f,0.10f,0.12f);
    static readonly Color ROW_SEL   = C(0.18f,0.38f,0.72f);
    static readonly Color ROW_HOV   = C(0.17f,0.17f,0.20f);
    static readonly Color ACCENT    = C(0.22f,0.55f,1.00f);
    static readonly Color BORDER    = C(0.22f,0.22f,0.26f);
    static readonly Color TEXT      = C(0.90f,0.90f,0.92f);
    static readonly Color DIM       = C(0.50f,0.50f,0.55f);
    static readonly Color COL_DIR   = C(0.95f,0.80f,0.35f);
    static readonly Color COL_IMG   = C(0.40f,0.85f,0.60f);
    static readonly Color COL_PRE   = C(0.45f,0.70f,1.00f);
    static readonly Color COL_XML   = C(0.90f,0.55f,0.30f);
    static readonly Color COL_SCN   = C(0.80f,0.50f,0.90f);
    static readonly Color COL_OTH   = C(0.60f,0.60f,0.65f);
    static readonly Color INSP_SEC  = C(0.14f,0.14f,0.17f);
    static readonly Color INSP_FIELD= C(0.13f,0.13f,0.16f);

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
        _cam.backgroundColor  = C(0.15f,0.18f,0.22f);
        _cam.clearFlags       = CameraClearFlags.SolidColor;

#if UNITY_EDITOR
        // load saved rooms directory from EditorPrefs
        _roomsDirectory = EditorPrefs.GetString(
            "VectorierSettings.RoomsDirectory", "");
#endif
        RefreshTree();
    }

    void Update()
    {
        HandleCam();
        HandleSceneInteraction();
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
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
            _cam.orthographicSize = Mathf.Clamp(
                _cam.orthographicSize - scroll * 3f, 0.5f, 50f);
    }

    // ── scene interaction ─────────────────────────────────────────

    void HandleSceneInteraction()
    {
        if (_cam == null) return;
        float mx = Input.mousePosition.x;
        bool overUI = mx < LEFT_W || mx > Screen.width - RIGHT_W;
        if (overUI) return;

        Vector3 wp = _cam.ScreenToWorldPoint(new Vector3(
            Input.mousePosition.x, Input.mousePosition.y,
            Mathf.Abs(_cam.transform.position.z)));
        wp.z = 0f;

        if (Input.GetMouseButtonDown(0)) {
            var hit = Physics2D.OverlapPoint(wp);
            GameObject clicked = hit != null ? hit.gameObject : null;
            if (clicked == null)
                foreach (var sr in FindObjectsOfType<SpriteRenderer>())
                    if (sr.bounds.Contains(wp)) { clicked = sr.gameObject; break; }

            _selectedGO = clicked;
            _dragging   = clicked != null;
            if (clicked != null) _dragOffset = clicked.transform.position - wp;
#if UNITY_EDITOR
            Selection.activeGameObject = clicked;
#endif
        }
        if (Input.GetMouseButton(0) && _dragging && _selectedGO != null)
            _selectedGO.transform.position = wp + _dragOffset;
        if (Input.GetMouseButtonUp(0)) _dragging = false;
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
        DrawLeftPanel(sh);
        DrawRightPanel(sw, sh);
        DrawSelectedGizmo();
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
        if (Btn(LEFT_W-60,by,54,bh,"Refresh",PANEL)) {
            RefreshTree(); _selectedAsset=null; _previewTex=null;
        }

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

        for (int i=0;i<_flat.Count;i++) {
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
#if UNITY_EDITOR
            if (!_selectedAsset.IsDir &&
                _selectedAsset.Name.EndsWith(".prefab",StringComparison.OrdinalIgnoreCase))
                if (Btn(6,py+PREVIEW_H-30,LEFT_W-12,24,"Place in Scene",ACCENT))
                    PlacePrefab(_selectedAsset.Path);
#endif
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
        Box(rx,ry,RIGHT_W,22,C(0.09f,0.09f,0.11f));
        GUI.Label(new Rect(rx+8,ry+3,RIGHT_W-16,16),"HIERARCHY",_sHeader);
        ry+=22;
        Box(rx,ry,RIGHT_W,HIER_H,PANEL);

        var roots = UnityEngine.SceneManagement.SceneManager
            .GetActiveScene().GetRootGameObjects();
        var allGOs = new List<GameObject>();
        foreach (var go in roots) CollectHierarchy(go,allGOs);

        float hierCH = Mathf.Max(allGOs.Count*ROW_H, HIER_H);
        _hierScroll = GUI.BeginScrollView(
            new Rect(rx,ry,RIGHT_W,HIER_H),_hierScroll,
            new Rect(0,0,RIGHT_W-14,hierCH));

        for (int i=0;i<allGOs.Count;i++) {
            var go   = allGOs[i];
            int dep  = GetDepth(go);
            Rect rr  = new Rect(0,i*ROW_H,RIGHT_W-14,ROW_H);
            bool sel = go==_selectedGO;
            DrawRect(rr,sel?ROW_SEL:(i%2==0?ROW_EVEN:ROW_ODD));
            if (!sel && rr.Contains(Event.current.mousePosition)) DrawRect(rr,ROW_HOV);
            float tx=dep*INDENT+4;
            bool active=GUI.Toggle(new Rect(tx,i*ROW_H+4,14,14),go.activeSelf,"");
            if (active!=go.activeSelf) go.SetActive(active);
            tx+=18;
            GUI.Label(new Rect(tx,i*ROW_H+2,RIGHT_W-tx-8,ROW_H-2),
                go.name,sel?_sHierSel:_sHierRow);
            if (Event.current.type==EventType.MouseDown &&
                rr.Contains(Event.current.mousePosition)) {
                _selectedGO=go;
#if UNITY_EDITOR
                Selection.activeGameObject=go;
#endif
                Event.current.Use();
            }
        }
        GUI.EndScrollView();
        ry+=HIER_H;

        // ── INSPECTOR ─────────────────────────────────────────────
        Line(rx,ry,RIGHT_W,1,BORDER);
        Box(rx,ry,RIGHT_W,22,C(0.09f,0.09f,0.11f));
        GUI.Label(new Rect(rx+8,ry+3,RIGHT_W-16,16),"INSPECTOR",_sHeader);
        ry+=22;

        float inspH = sh - ry;
        Box(rx,ry,RIGHT_W,inspH,PANEL);

        if (_selectedGO==null) {
            GUI.Label(new Rect(rx+8,ry+20,RIGHT_W-16,20),"Nothing selected",_sDim);
            return;
        }

        // scroll view — tall content rect so fields don't get clipped
        _inspScroll = GUI.BeginScrollView(
            new Rect(rx,ry,RIGHT_W,inspH),_inspScroll,
            new Rect(0,0,RIGHT_W-14,3000));

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

        // ── Tag + Layer ───────────────────────────────────────────
        Box(lx,iy,iw,22,INSP_FIELD);
        GUI.Label(new Rect(lx+4,iy+3,26,16),"Tag",_sDim);
#if UNITY_EDITOR
        string[] tags=UnityEditorInternal.InternalEditorUtility.tags;
        int ti=Mathf.Max(0,Array.IndexOf(tags,_selectedGO.tag));
        int nti=EditorGUI.Popup(new Rect(lx+32,iy+2,iw/2-36,18),ti,tags);
        if (nti!=ti) _selectedGO.tag=tags[nti];
        GUI.Label(new Rect(lx+iw/2+4,iy+3,34,16),"Layer",_sDim);
        // use our hardcoded sorting layer list isn't the same as physics layer
        // physics layer uses LayerMask
        string[] physLayers=UnityEditorInternal.InternalEditorUtility.layers;
        int li=Mathf.Max(0,Array.IndexOf(physLayers,
            LayerMask.LayerToName(_selectedGO.layer)));
        int nli=EditorGUI.Popup(new Rect(lx+iw/2+42,iy+2,iw/2-46,18),li,physLayers);
        if (nli!=li) _selectedGO.layer=LayerMask.NameToLayer(physLayers[nli]);
#endif
        iy+=26;

        // ── Transform ────────────────────────────────────────────
        iy=InspSec("Transform",lx,iy,iw);
        var tf=_selectedGO.transform;
        iy=Vec3Row("Position",lx,iy,iw,tf.position,       v=>tf.position=v);
        iy=Vec3Row("Rotation",lx,iy,iw,tf.eulerAngles,    v=>tf.eulerAngles=v);
        iy=Vec3Row("Scale",   lx,iy,iw,tf.localScale,     v=>tf.localScale=v);
        iy+=4;

        // ── Sprite Renderer ───────────────────────────────────────
        var sr=_selectedGO.GetComponent<SpriteRenderer>();
        if (sr!=null) {
            iy=InspSec("Sprite Renderer",lx,iy,iw);
            iy=LabelRow("Sprite",lx,iy,iw,sr.sprite!=null?sr.sprite.name:"None");
#if UNITY_EDITOR
            // Color
            Box(lx,iy,iw,22,INSP_FIELD);
            GUI.Label(new Rect(lx+4,iy+3,80,16),"Color",_sDim);
            sr.color=EditorGUI.ColorField(new Rect(lx+88,iy+3,iw-92,16),sr.color);
            iy+=24;
#endif
            // Sorting Layer — use our hardcoded list
            Box(lx,iy,iw,22,INSP_FIELD);
            GUI.Label(new Rect(lx+4,iy+3,80,16),"Sorting Layer",_sDim);
#if UNITY_EDITOR
            int slIdx=Mathf.Max(0,Array.IndexOf(SORTING_LAYERS,sr.sortingLayerName));
            int nslIdx=EditorGUI.Popup(new Rect(lx+88,iy+2,iw-92,18),slIdx,SORTING_LAYERS);
            if (nslIdx!=slIdx) sr.sortingLayerName=SORTING_LAYERS[nslIdx];
#else
            GUI.Label(new Rect(lx+88,iy+3,iw-92,16),sr.sortingLayerName,_sInspValue);
#endif
            iy+=24;

            // Order in Layer
            Box(lx,iy,iw,22,INSP_FIELD);
            GUI.Label(new Rect(lx+4,iy+3,80,16),"Order in Layer",_sDim);
#if UNITY_EDITOR
            sr.sortingOrder=EditorGUI.IntField(
                new Rect(lx+88,iy+3,iw-92,16),sr.sortingOrder);
#else
            GUI.Label(new Rect(lx+88,iy+3,iw-92,16),sr.sortingOrder.ToString(),_sInspValue);
#endif
            iy+=24;
            iy+=4;
        }

        // ── BuildMapVec2 ──────────────────────────────────────────
        var bm=_selectedGO.GetComponent<BuildMapVec2>();
        if (bm!=null) {
            iy=InspSec("Build Map Vec2",lx,iy,iw);

            // Map To Override
            Box(lx,iy,iw,22,INSP_FIELD);
            GUI.Label(new Rect(lx+4,iy+3,100,16),"Map To Override",_sDim);
            bm.mapToOverride=GUI.TextField(
                new Rect(lx+108,iy+3,iw-112,16),bm.mapToOverride,_sTextField);
            iy+=24;

            // Debug Object Writing
            Box(lx,iy,iw,22,INSP_FIELD);
            GUI.Label(new Rect(lx+4,iy+3,130,16),"Debug Object Writing",_sDim);
            bm.debugObjectWriting=GUI.Toggle(
                new Rect(lx+iw-20,iy+4,16,16),bm.debugObjectWriting,"");
            iy+=24;

            // Build button
            iy+=4;
            if (Btn(lx,iy,iw,26,"Build Map (Vec2)",ACCENT)) {
#if UNITY_EDITOR
                BuildMapVec2.BuildXml();
#endif
            }
            iy+=30;
            iy+=4;
        }

        // ── Vectorier Rooms Directory ─────────────────────────────
        iy=InspSec("Vectorier Settings",lx,iy,iw);
        Box(lx,iy,iw,22,INSP_FIELD);
        GUI.Label(new Rect(lx+4,iy+3,100,16),"Rooms Directory",_sDim);
        string newDir=GUI.TextField(
            new Rect(lx+108,iy+3,iw-138,16),_roomsDirectory,_sTextField);
        if (newDir!=_roomsDirectory) {
            _roomsDirectory=newDir;
#if UNITY_EDITOR
            EditorPrefs.SetString("VectorierSettings.RoomsDirectory",_roomsDirectory);
#endif
        }
        // browse button
        if (Btn(lx+iw-28,iy+2,26,18,"...",PANEL)) {
#if UNITY_EDITOR
            string picked=EditorUtility.OpenFolderPanel(
                "Select Rooms Directory","","");
            if (!string.IsNullOrEmpty(picked)) {
                _roomsDirectory=picked;
                EditorPrefs.SetString("VectorierSettings.RoomsDirectory",picked);
            }
#endif
        }
        iy+=24;
        iy+=4;

        // ── All other Vectorier MonoBehaviours ────────────────────
        var comps=_selectedGO.GetComponents<MonoBehaviour>();
        foreach (var comp in comps) {
            if (comp==null) continue;
            string cn=comp.GetType().Name;
            if (cn=="VectorierEditorUI"||cn=="BuildMapVec2") continue;
#if UNITY_EDITOR
            iy=InspSec(cn,lx,iy,iw);
            var so=new SerializedObject(comp);
            so.Update();
            var prop=so.GetIterator();
            prop.NextVisible(true); // skip m_Script field
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
#endif
        }

        GUI.EndScrollView();
    }

    // ── gizmo outline ─────────────────────────────────────────────

    void DrawSelectedGizmo()
    {
        if (_selectedGO==null||_cam==null) return;
        var sr=_selectedGO.GetComponent<SpriteRenderer>();
        if (sr==null) return;
        Bounds b=sr.bounds;
        Vector3[] corners={ new Vector3(b.min.x,b.min.y,0),
                             new Vector3(b.max.x,b.min.y,0),
                             new Vector3(b.max.x,b.max.y,0),
                             new Vector3(b.min.x,b.max.y,0) };
        Vector2[] sc=new Vector2[4];
        for (int i=0;i<4;i++) {
            Vector3 sp=_cam.WorldToScreenPoint(corners[i]);
            sc[i]=new Vector2(sp.x,Screen.height-sp.y);
        }
        Color prev=GUI.color; GUI.color=ACCENT;
        float t=1.5f;
        for (int i=0;i<4;i++) {
            Vector2 a=sc[i], bb=sc[(i+1)%4];
            float mnX=Mathf.Min(a.x,bb.x),mnY=Mathf.Min(a.y,bb.y);
            float mxX=Mathf.Max(a.x,bb.x),mxY=Mathf.Max(a.y,bb.y);
            if (Mathf.Abs(a.x-bb.x)<0.5f)
                GUI.DrawTexture(new Rect(mnX-t/2,mnY,t,mxY-mnY),Texture2D.whiteTexture);
            else
                GUI.DrawTexture(new Rect(mnX,mnY-t/2,mxX-mnX,t),Texture2D.whiteTexture);
        }
        GUI.color=prev;
    }

    // ── inspector draw helpers ────────────────────────────────────

    float InspSec(string title,float lx,float iy,float iw) {
        Box(lx,iy,iw,22,INSP_SEC);
        GUI.Label(new Rect(lx+6,iy+3,iw-10,16),title,_sInspSection);
        return iy+24;
    }
    float Vec3Row(string label,float lx,float iy,float iw,
                  Vector3 v,Action<Vector3> set)
    {
        Box(lx,iy,iw,22,INSP_FIELD);
        GUI.Label(new Rect(lx+4,iy+3,72,16),label,_sDim);
        float fw=(iw-80)/3f-2f; float fx=lx+78;
#if UNITY_EDITOR
        float nx=EditorGUI.FloatField(new Rect(fx,    iy+3,fw,16),v.x);
        float ny=EditorGUI.FloatField(new Rect(fx+fw+2,iy+3,fw,16),v.y);
        float nz=EditorGUI.FloatField(new Rect(fx+fw*2+4,iy+3,fw,16),v.z);
        if (nx!=v.x||ny!=v.y||nz!=v.z) set(new Vector3(nx,ny,nz));
#else
        GUI.Label(new Rect(fx,iy+3,iw-82,16),
            $"X{v.x:F2} Y{v.y:F2} Z{v.z:F2}",_sInspValue);
#endif
        return iy+24;
    }
    float LabelRow(string label,float lx,float iy,float iw,string val) {
        Box(lx,iy,iw,22,INSP_FIELD);
        GUI.Label(new Rect(lx+4,iy+3,84,16),label,_sDim);
        GUI.Label(new Rect(lx+90,iy+3,iw-94,16),val,_sInspValue);
        return iy+24;
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
    void PlacePrefab(string fullPath) {
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
            _selectedGO=go;
            Selection.activeGameObject=go;
        } else Debug.LogWarning("[Vectorier] Prefab not found: "+fullPath);
    }
#endif

    // ── styles ────────────────────────────────────────────────────

    void MakeStyles() {
        if (_stylesReady) return;
        _stylesReady=true;
        _sRow=new GUIStyle(GUI.skin.label){
            normal={textColor=TEXT},fontSize=11,
            alignment=TextAnchor.MiddleLeft,padding=new RectOffset(2,2,0,0)};
        _sRowSel=new GUIStyle(_sRow){
            normal={textColor=Color.white},fontStyle=FontStyle.Bold};
        _sHierRow=new GUIStyle(_sRow);
        _sHierSel=new GUIStyle(_sRowSel);
        _sLabel=new GUIStyle(GUI.skin.label){
            normal={textColor=TEXT},fontSize=11,alignment=TextAnchor.MiddleLeft};
        _sDim=new GUIStyle(GUI.skin.label){
            normal={textColor=DIM},fontSize=10,
            alignment=TextAnchor.MiddleLeft,wordWrap=false};
        _sSearch=new GUIStyle(GUI.skin.textField){
            normal={background=Tex(C(0.15f,0.15f,0.18f)),textColor=TEXT},
            focused={background=Tex(C(0.15f,0.15f,0.18f)),textColor=Color.white},
            fontSize=11,alignment=TextAnchor.MiddleLeft,
            padding=new RectOffset(6,6,2,2)};
        _sTextField=new GUIStyle(GUI.skin.textField){
            normal={background=Tex(C(0.13f,0.13f,0.17f)),textColor=TEXT},
            focused={background=Tex(C(0.13f,0.13f,0.17f)),textColor=Color.white},
            fontSize=10,alignment=TextAnchor.MiddleLeft,
            padding=new RectOffset(4,4,2,2)};
        _sHeader=new GUIStyle(GUI.skin.label){
            normal={textColor=DIM},fontSize=9,
            fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleLeft};
        _sBtn=new GUIStyle(GUI.skin.button){
            normal={background=Tex(PANEL),textColor=TEXT},
            hover={background=Tex(C(0.20f,0.20f,0.24f)),textColor=Color.white},
            active={background=Tex(C(0.15f,0.35f,0.65f)),textColor=Color.white},
            fontSize=11,alignment=TextAnchor.MiddleCenter,
            border=new RectOffset(3,3,3,3),padding=new RectOffset(4,4,3,3)};
        _sInspValue=new GUIStyle(GUI.skin.label){
            normal={textColor=TEXT},fontSize=10,alignment=TextAnchor.MiddleLeft};
        _sInspSection=new GUIStyle(GUI.skin.label){
            normal={textColor=TEXT},fontSize=11,
            fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleLeft};
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
    static Texture2D Tex(Color col){
        var t=new Texture2D(1,1); t.SetPixel(0,0,col); t.Apply(); return t;
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
