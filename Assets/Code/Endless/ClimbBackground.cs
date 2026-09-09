using UnityEngine;
using System.Collections.Generic;

//--------------------------------------------------------------------
//Procedural backdrop for the endless climb. Three layers, all pooled and
//recycled so they cost the same at 10 metres as at 10,000:
//
//  1. sky      - camera clear colour driven by altitude
//  2. stars    - parallax dot layers at increasing depth
//  3. grid     - the ladder/rung backdrop, rebuilt cleanly
//
//The grid replaces the hand-placed Net groups. Those had three separate faults
//that showed up as clipped, mismatched edges: they were spaced 9.5 apart while
//being 10 tall (so they overlapped AND broke the 1.0 rung pitch), each group sat
//at a different z (0.7 to 1.3), which under a perspective camera renders each at
//a different scale so they can never align, and their horizontal coverage was
//lopsided. Here every tile is an exact multiple of the rung pitch, at one fixed
//z, symmetric about the centre line.
//--------------------------------------------------------------------
public class ClimbBackground : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Camera m_Camera;
    [Tooltip("Material used for the grid. Point this at the existing 'Stairs' material to keep the current look.")]
    [SerializeField] Material m_GridMaterial;

    [Header("Sky")]
    [SerializeField] bool m_DriveSkyColour = true;
    [Tooltip("Altitude at which the gradient reaches its far end.")]
    [SerializeField] float m_SkyTopAltitude = 1200.0f;
    [SerializeField] Gradient m_SkyGradient = new Gradient();

    [Header("Grid")]
    [SerializeField] bool m_BuildGrid = true;
    [Tooltip("Single depth for the whole grid. Mixing depths is what made the old backdrop misalign.")]
    [SerializeField] float m_GridZ = 1.1f;
    [Tooltip("Vertical distance between rungs. Tile height must stay an exact multiple of this.")]
    [SerializeField] float m_RungPitch = 1.0f;
    [Tooltip("Horizontal distance between vertical rails.")]
    [SerializeField] float m_RailPitch = 2.0f;
    [Tooltip("Grid is built symmetrically from -this to +this on X.")]
    [SerializeField] float m_GridHalfWidth = 6.0f;
    [Tooltip("Height of one recycled grid tile. Must divide evenly by Rung Pitch.")]
    [SerializeField] float m_GridTileHeight = 10.0f;
    [SerializeField] float m_RungThickness = 0.5f;
    [SerializeField] float m_RailThickness = 0.6f;
    [SerializeField] float m_GridDepth = 0.2f;
    [SerializeField] Color m_GridColor = new Color(0.30f, 0.34f, 0.46f);

    [Header("Bottom limit")]
    [Tooltip("Nothing in the backdrop is drawn below this world Y. Set it to the top of the starting " +
        "floor so the grid and stars stop cleanly at the white bar instead of bleeding underneath it.")]
    [SerializeField] bool m_ClipBelowFloor = true;
    [SerializeField] float m_BottomLimitY = -1.26f;

    [Header("Stars")]
    [SerializeField] bool m_BuildStars = true;
    [Tooltip("One entry per parallax layer. Depth is distance behind the gameplay plane.")]
    [SerializeField] float[] m_StarLayerDepths = new float[] { 18.0f, 40.0f, 80.0f };
    [SerializeField] int m_StarsPerLayer = 26;
    [SerializeField] float m_StarSize = 0.16f;
    [SerializeField] Color m_StarColor = new Color(1.0f, 0.98f, 0.90f, 1.0f);

    class Tile
    {
        public Transform m_Root;
        public float m_BottomY;
        public List<Transform> m_Rails = new List<Transform>();
        public List<Transform> m_Rungs = new List<Transform>();
    }

    readonly List<Tile> m_Tiles = new List<Tile>();
    readonly List<Transform> m_Stars = new List<Transform>();
    readonly List<float> m_StarBandHeights = new List<float>();
    readonly List<float> m_StarDepths = new List<float>();

    Material m_RuntimeGridMaterial;
    Material m_StarMaterial;
    Mesh m_CubeMesh;
    float m_TileSpan;

    void Reset()
    {
        //A default sky that reads as ground haze climbing into deep space
        GradientColorKey[] colors = new GradientColorKey[4];
        colors[0] = new GradientColorKey(new Color(0.16f, 0.15f, 0.22f), 0.0f);
        colors[1] = new GradientColorKey(new Color(0.13f, 0.18f, 0.34f), 0.35f);
        colors[2] = new GradientColorKey(new Color(0.09f, 0.10f, 0.26f), 0.70f);
        colors[3] = new GradientColorKey(new Color(0.02f, 0.02f, 0.07f), 1.0f);
        GradientAlphaKey[] alphas = new GradientAlphaKey[2];
        alphas[0] = new GradientAlphaKey(1.0f, 0.0f);
        alphas[1] = new GradientAlphaKey(1.0f, 1.0f);
        m_SkyGradient = new Gradient();
        m_SkyGradient.SetKeys(colors, alphas);
    }

    void Awake()
    {
        if (m_Camera == null)
        {
            m_Camera = Camera.main;
        }
        if (m_SkyGradient == null || m_SkyGradient.colorKeys.Length == 0)
        {
            Reset();
        }
    }

    void Start()
    {
        if (m_Camera == null)
        {
            Debug.LogError("ClimbBackground: no camera assigned and no MainCamera found.");
            enabled = false;
            return;
        }

        if (m_DriveSkyColour)
        {
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
        }

        m_CubeMesh = BorrowCubeMesh();
        if (m_CubeMesh == null)
        {
            Debug.LogWarning("ClimbBackground: could not obtain a cube mesh, grid and stars disabled.");
            return;
        }

        if (m_BuildGrid)
        {
            BuildGrid();
        }
        if (m_BuildStars)
        {
            BuildStars();
        }
    }

    void LateUpdate()
    {
        if (m_Camera == null)
        {
            return;
        }
        float camY = m_Camera.transform.position.y;

        if (m_DriveSkyColour)
        {
            float t = Mathf.Clamp01(camY / Mathf.Max(1.0f, m_SkyTopAltitude));
            m_Camera.backgroundColor = m_SkyGradient.Evaluate(t);
        }

        RecycleGrid(camY);
        RecycleStars(camY);
    }

    //----------------------------------------------------------------
    //Grid
    //----------------------------------------------------------------
    void BuildGrid()
    {
        //Snap the tile height to a whole number of rungs. If it is not an exact
        //multiple, every recycle shifts the pattern and the seam becomes visible.
        int rungsPerTile = Mathf.Max(1, Mathf.RoundToInt(m_GridTileHeight / m_RungPitch));
        m_TileSpan = rungsPerTile * m_RungPitch;

        m_RuntimeGridMaterial = MakeMaterial(m_GridMaterial, m_GridColor);

        float viewHeight = ViewHeightAt(m_GridZ);
        int tileCount = Mathf.CeilToInt(viewHeight / m_TileSpan) + 3;

        float startBottom = Mathf.Floor((m_Camera.transform.position.y - viewHeight) / m_TileSpan) * m_TileSpan;
        for (int i = 0; i < tileCount; i++)
        {
            Tile tile = BuildGridTile(i, rungsPerTile);
            PlaceTile(tile, startBottom + (i * m_TileSpan));
            m_Tiles.Add(tile);
        }
    }

    Tile BuildGridTile(int a_Index, int a_Rungs)
    {
        GameObject root = new GameObject("GridTile_" + a_Index);
        root.transform.SetParent(transform, false);
        List<Transform> rails = new List<Transform>();
        List<Transform> rungs = new List<Transform>();

        //Rails: symmetric about zero so the two halves always match
        int railsEachSide = Mathf.FloorToInt(m_GridHalfWidth / m_RailPitch);
        for (int i = -railsEachSide; i <= railsEachSide; i++)
        {
            float x = i * m_RailPitch;
            rails.Add(MakeBar(root.transform, "Rail_" + i,
                new Vector3(x, m_TileSpan * 0.5f, 0.0f),
                new Vector3(m_RailThickness, m_TileSpan, m_GridDepth)));
        }

        //Rungs: one bar spanning the full width per pitch step, so there are no
        //vertical seams partway across like the authored version had
        for (int i = 0; i < a_Rungs; i++)
        {
            float y = (i + 0.5f) * m_RungPitch;
            rungs.Add(MakeBar(root.transform, "Rung_" + i,
                new Vector3(0.0f, y, 0.0f),
                new Vector3(m_GridHalfWidth * 2.0f, m_RungThickness, m_GridDepth)));
        }

        Tile tile = new Tile();
        tile.m_Root = root.transform;
        tile.m_Rails = rails;
        tile.m_Rungs = rungs;
        return tile;
    }

    Transform MakeBar(Transform a_Parent, string a_Name, Vector3 a_LocalPos, Vector3 a_Scale)
    {
        GameObject bar = new GameObject(a_Name);
        bar.transform.SetParent(a_Parent, false);
        bar.transform.localPosition = a_LocalPos;
        bar.transform.localScale = a_Scale;

        MeshFilter filter = bar.AddComponent<MeshFilter>();
        filter.sharedMesh = m_CubeMesh;
        MeshRenderer renderer = bar.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = m_RuntimeGridMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return bar.transform;
    }

    void PlaceTile(Tile a_Tile, float a_BottomY)
    {
        a_Tile.m_BottomY = a_BottomY;
        a_Tile.m_Root.position = new Vector3(0.0f, a_BottomY, m_GridZ);
        ApplyBottomLimit(a_Tile);
    }

    //Trim the tile against the floor line. Rungs are whole bars so they just switch
    //off; rails are cut down to start exactly at the limit, giving a clean edge along
    //the white bar rather than geometry poking out below it.
    void ApplyBottomLimit(Tile a_Tile)
    {
        if (!m_ClipBelowFloor)
        {
            return;
        }
        float limit = m_BottomLimitY;
        float bottom = a_Tile.m_BottomY;
        float top = bottom + m_TileSpan;

        for (int i = 0; i < a_Tile.m_Rungs.Count; i++)
        {
            Transform rung = a_Tile.m_Rungs[i];
            bool visible = (bottom + rung.localPosition.y) >= limit;
            if (rung.gameObject.activeSelf != visible)
            {
                rung.gameObject.SetActive(visible);
            }
        }

        for (int i = 0; i < a_Tile.m_Rails.Count; i++)
        {
            Transform rail = a_Tile.m_Rails[i];
            if (top <= limit)
            {
                if (rail.gameObject.activeSelf)
                {
                    rail.gameObject.SetActive(false);
                }
                continue;
            }
            if (!rail.gameObject.activeSelf)
            {
                rail.gameObject.SetActive(true);
            }

            float visibleBottom = Mathf.Max(bottom, limit);
            float height = top - visibleBottom;
            Vector3 scale = rail.localScale;
            scale.y = height;
            rail.localScale = scale;

            Vector3 pos = rail.localPosition;
            pos.y = (visibleBottom - bottom) + (height * 0.5f);
            rail.localPosition = pos;
        }
    }

    void RecycleGrid(float a_CamY)
    {
        if (m_Tiles.Count == 0)
        {
            return;
        }
        float viewHeight = ViewHeightAt(m_GridZ);
        float cullBelow = a_CamY - viewHeight;
        float coverTo = a_CamY + viewHeight;

        int guard = 0;
        bool moved = true;
        while (moved && guard < 256)
        {
            moved = false;
            guard++;

            Tile lowest = m_Tiles[0];
            float highestTop = float.MinValue;
            for (int i = 0; i < m_Tiles.Count; i++)
            {
                if (m_Tiles[i].m_BottomY < lowest.m_BottomY)
                {
                    lowest = m_Tiles[i];
                }
                highestTop = Mathf.Max(highestTop, m_Tiles[i].m_BottomY + m_TileSpan);
            }

            if ((lowest.m_BottomY + m_TileSpan) < cullBelow && highestTop < coverTo)
            {
                //Move by a whole tile span so rung alignment is preserved exactly
                PlaceTile(lowest, highestTop);
                moved = true;
            }
        }
    }

    //----------------------------------------------------------------
    //Stars
    //----------------------------------------------------------------
    void BuildStars()
    {
        m_StarMaterial = MakeMaterial(null, m_StarColor);

        for (int layer = 0; layer < m_StarLayerDepths.Length; layer++)
        {
            float depth = m_StarLayerDepths[layer];
            float viewHeight = ViewHeightAt(depth);
            float viewWidth = viewHeight * m_Camera.aspect;
            float band = viewHeight * 2.0f;

            //Further layers get smaller, dimmer dots so depth reads without a fog pass
            float sizeScale = Mathf.Lerp(1.0f, 0.55f, (float)layer / Mathf.Max(1, m_StarLayerDepths.Length - 1));
            float bright = Mathf.Lerp(1.0f, 0.45f, (float)layer / Mathf.Max(1, m_StarLayerDepths.Length - 1));

            Material mat = MakeMaterial(null, m_StarColor * bright);

            for (int i = 0; i < m_StarsPerLayer; i++)
            {
                GameObject star = new GameObject("Star_" + layer + "_" + i);
                star.transform.SetParent(transform, false);

                float x = Random.Range(-viewWidth, viewWidth);
                float y = m_Camera.transform.position.y + Random.Range(-viewHeight, viewHeight);
                star.transform.position = new Vector3(x, y, depth);

                //Depth scaling keeps distant dots from vanishing entirely under perspective
                float size = m_StarSize * sizeScale * (1.0f + (depth * 0.05f));
                star.transform.localScale = Vector3.one * size;

                MeshFilter filter = star.AddComponent<MeshFilter>();
                filter.sharedMesh = m_CubeMesh;
                MeshRenderer renderer = star.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                m_Stars.Add(star.transform);
                m_StarBandHeights.Add(band);
                m_StarDepths.Add(depth);
            }
        }
    }

    //Stars live in world space, so perspective gives real parallax for free -
    //a star at depth 80 slides far less than one at 18. All we do is wrap them.
    void RecycleStars(float a_CamY)
    {
        for (int i = 0; i < m_Stars.Count; i++)
        {
            Transform star = m_Stars[i];
            float band = m_StarBandHeights[i];
            float half = band * 0.5f;
            Vector3 pos = star.position;

            if (pos.y < a_CamY - half)
            {
                pos.y += band;
                pos.x = Random.Range(-half * m_Camera.aspect, half * m_Camera.aspect);
                star.position = pos;
            }
            else if (pos.y > a_CamY + half)
            {
                pos.y -= band;
                star.position = pos;
            }

            //Keep the sky above the floor line only
            bool visible = !m_ClipBelowFloor || pos.y >= m_BottomLimitY;
            if (star.gameObject.activeSelf != visible)
            {
                star.gameObject.SetActive(visible);
            }
        }
    }

    //----------------------------------------------------------------
    //Helpers
    //----------------------------------------------------------------
    //Visible world height at a given world z, for either projection
    float ViewHeightAt(float a_WorldZ)
    {
        float distance = Mathf.Abs(a_WorldZ - m_Camera.transform.position.z);
        if (m_Camera.orthographic)
        {
            return m_Camera.orthographicSize * 2.0f;
        }
        return 2.0f * distance * Mathf.Tan(m_Camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
    }

    Material MakeMaterial(Material a_Source, Color a_Color)
    {
        Material mat = a_Source != null
            ? new Material(a_Source)
            : new Material(Shader.Find("Unlit/Color"));
        if (mat.HasProperty("_Color"))
        {
            mat.SetColor("_Color", a_Color);
        }
        return mat;
    }

    //Grab a cube mesh without needing an asset reference
    Mesh BorrowCubeMesh()
    {
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        MeshFilter filter = temp.GetComponent<MeshFilter>();
        Mesh mesh = filter != null ? filter.sharedMesh : null;
        Destroy(temp);
        return mesh;
    }
}
