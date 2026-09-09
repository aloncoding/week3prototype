using UnityEngine;
using System.Collections.Generic;

//--------------------------------------------------------------------
//Builds an endless vertical shaft out of a fixed pool of segments.
//
//Segments are never created or destroyed after startup - when the lowest one
//falls far enough behind the player it is lifted to the top of the stack and
//reused. A fixed pool means an hour-long run allocates exactly as much as the
//first second of one.
//
//Wall look and collision are copied from a template renderer in the scene (the
//existing hand-placed wall), so generated walls match the authored ones without
//needing a prefab asset. The generated walls are built with clean positive scale
//rather than cloning the template's transform, because the authored walls use a
//negative scale that makes Unity fall back to a positive box and warn about it.
//--------------------------------------------------------------------
public class EndlessShaftGenerator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Transform m_Player;
    [Tooltip("An existing wall in the scene. Its sprite, material, layer and colour are copied onto every generated wall.")]
    [SerializeField] SpriteRenderer m_WallTemplate;
    [Tooltip("Optional decorative backdrop (the Net group). Cloned into every segment if set.")]
    [SerializeField] Transform m_BackdropTemplate;

    [Header("Shaft shape")]
    [Tooltip("Distance from the centre line to the INNER face of each wall. The playable gap is twice this.")]
    [SerializeField] float m_ShaftHalfWidth = 4.8f;
    [SerializeField] float m_WallThickness = 1.0f;
    [Tooltip("Height of one segment. Also the vertical spacing of the stack.")]
    [SerializeField] float m_SegmentHeight = 20.0f;
    [Tooltip("Y to start generating from. Set this at or BELOW the top of the LOWEST hand-built wall. " +
        "The two authored walls are not the same height, so starting above the shorter one leaves a hole in it.")]
    [SerializeField] float m_StartY = 19.5f;
    [Tooltip("Extra height added to every wall piece, half above and half below its segment. Guarantees the " +
        "seams between recycled segments - and the join onto the authored walls - can never show a gap, " +
        "even with floating point drift over a long climb.")]
    [SerializeField] float m_VerticalOverlap = 0.5f;

    [Header("Streaming")]
    [Tooltip("How far above the player the shaft is kept built.")]
    [SerializeField] float m_BuildAhead = 60.0f;
    [Tooltip("How far below the player a segment may sit before it is recycled to the top.")]
    [SerializeField] float m_RecycleBehind = 40.0f;

    [Header("Backdrop")]
    [Tooltip("Vertical spacing of backdrop copies within a segment.")]
    [SerializeField] float m_BackdropSpacing = 9.5f;

    class Segment
    {
        public Transform m_Root;
        public float m_BottomY;
    }

    readonly List<Segment> m_Segments = new List<Segment>();
    float m_NextSegmentY;

    void Start()
    {
        if (m_Player == null || m_WallTemplate == null)
        {
            Debug.LogError("EndlessShaftGenerator: Player and Wall Template must both be assigned.");
            enabled = false;
            return;
        }

        m_NextSegmentY = m_StartY;

        //Enough segments to cover the streamed window at all times, plus one either side
        int needed = Mathf.CeilToInt((m_BuildAhead + m_RecycleBehind) / m_SegmentHeight) + 2;
        for (int i = 0; i < needed; i++)
        {
            m_Segments.Add(BuildSegment(i));
            PlaceSegment(m_Segments[m_Segments.Count - 1], m_NextSegmentY);
            m_NextSegmentY += m_SegmentHeight;
        }
    }

    void Update()
    {
        if (m_Player == null)
        {
            return;
        }

        float recycleBelow = m_Player.position.y - m_RecycleBehind;

        //Lift any segment that has fallen behind up to the top of the stack.
        //Guarded by a loop cap so a teleport cannot spin here forever.
        int guard = 0;
        bool moved = true;
        while (moved && guard < 64)
        {
            moved = false;
            guard++;

            Segment lowest = null;
            for (int i = 0; i < m_Segments.Count; i++)
            {
                if (lowest == null || m_Segments[i].m_BottomY < lowest.m_BottomY)
                {
                    lowest = m_Segments[i];
                }
            }
            if (lowest == null)
            {
                return;
            }

            bool behind = (lowest.m_BottomY + m_SegmentHeight) < recycleBelow;
            bool needMoreAbove = m_NextSegmentY < (m_Player.position.y + m_BuildAhead);
            if (behind && needMoreAbove)
            {
                PlaceSegment(lowest, m_NextSegmentY);
                m_NextSegmentY += m_SegmentHeight;
                moved = true;
            }
        }
    }

    Segment BuildSegment(int a_Index)
    {
        GameObject root = new GameObject("ShaftSegment_" + a_Index);
        root.transform.SetParent(transform, false);

        MakeWall(root.transform, "WallLeft", -(m_ShaftHalfWidth + (m_WallThickness * 0.5f)));
        MakeWall(root.transform, "WallRight", m_ShaftHalfWidth + (m_WallThickness * 0.5f));

        if (m_BackdropTemplate != null && m_BackdropSpacing > 0.01f)
        {
            int copies = Mathf.Max(1, Mathf.RoundToInt(m_SegmentHeight / m_BackdropSpacing));
            for (int i = 0; i < copies; i++)
            {
                GameObject backdrop = Instantiate(m_BackdropTemplate.gameObject, root.transform);
                backdrop.name = "Backdrop_" + i;
                //Template sits at its own authored world position, so rebuild the local offset
                Vector3 local = backdrop.transform.localPosition;
                local.y = (i + 0.5f) * (m_SegmentHeight / copies);
                backdrop.transform.localPosition = local;
            }
        }

        Segment segment = new Segment();
        segment.m_Root = root.transform;
        return segment;
    }

    void MakeWall(Transform a_Parent, string a_Name, float a_CentreX)
    {
        GameObject wall = new GameObject(a_Name);
        wall.transform.SetParent(a_Parent, false);
        wall.layer = m_WallTemplate.gameObject.layer;

        //Positive scale only - the authored walls use a negative one, which makes
        //BoxCollider force the size positive and log a warning every run
        //Overgrow each piece slightly so neighbouring segments always overlap instead of merely touching
        wall.transform.localPosition = new Vector3(a_CentreX, m_SegmentHeight * 0.5f, 0.0f);
        wall.transform.localScale = new Vector3(m_WallThickness, m_SegmentHeight + m_VerticalOverlap, 1.0f);

        SpriteRenderer renderer = wall.AddComponent<SpriteRenderer>();
        renderer.sprite = m_WallTemplate.sprite;
        renderer.sharedMaterial = m_WallTemplate.sharedMaterial;
        renderer.color = m_WallTemplate.color;
        renderer.sortingLayerID = m_WallTemplate.sortingLayerID;
        renderer.sortingOrder = m_WallTemplate.sortingOrder;
        renderer.drawMode = SpriteDrawMode.Simple;

        BoxCollider box = wall.AddComponent<BoxCollider>();
        box.size = Vector3.one;
        box.center = Vector3.zero;
    }

    void PlaceSegment(Segment a_Segment, float a_BottomY)
    {
        a_Segment.m_BottomY = a_BottomY;
        a_Segment.m_Root.position = new Vector3(0.0f, a_BottomY, 0.0f);
    }

    //Lowest point that still has shaft built around it - the run manager uses this
    //to avoid killing the player before anything exists to stand on
    public float GetLowestBuiltY()
    {
        float lowest = float.MaxValue;
        for (int i = 0; i < m_Segments.Count; i++)
        {
            lowest = Mathf.Min(lowest, m_Segments[i].m_BottomY);
        }
        return lowest;
    }
}
