using UnityEngine;

//--------------------------------------------------------------------
//A deadly floor that climbs the shaft behind you, slowly at first and then
//faster, so a run cannot be stalled indefinitely.
//
//It stays dormant for a grace period, then accelerates continuously. A trailing
//clamp stops it from being left so far behind that it stops mattering: however
//well you climb, it is always somewhere below you and always closing.
//--------------------------------------------------------------------
public class RisingHazard : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Transform m_Player;
    [SerializeField] EndlessRunManager m_RunManager;
    [SerializeField] Camera m_Camera;

    [Header("Timing")]
    [Tooltip("Seconds before the hazard starts moving at all. Lets the player settle in first.")]
    [SerializeField] float m_GraceTime = 8.0f;
    [Tooltip("Speed the moment the grace period ends, in world units per second.")]
    [SerializeField] float m_StartSpeed = 1.6f;
    [Tooltip("Extra speed gained per second of run time.")]
    [SerializeField] float m_Acceleration = 0.055f;
    [Tooltip("Hard ceiling on rise speed, so late game stays survivable.")]
    [SerializeField] float m_MaxSpeed = 14.0f;

    [Header("Placement")]
    [Tooltip("How far below the player the hazard starts.")]
    [SerializeField] float m_StartOffsetBelow = 10.0f;
    [Tooltip("Never allowed to trail further than this behind the player. Without it, a strong " +
        "climber outruns the hazard permanently and the pressure disappears for the rest of the run.")]
    [SerializeField] float m_MaxTrailDistance = 45.0f;

    [Header("Look")]
    [SerializeField] float m_Width = 12.0f;
    [Tooltip("How far down the slab is drawn below its deadly surface.")]
    [SerializeField] float m_VisualDepth = 40.0f;
    [SerializeField] Color m_Color = new Color(0.85f, 0.20f, 0.25f);
    [Tooltip("Pulsing brightness so it reads as dangerous rather than as scenery.")]
    [SerializeField] Color m_PulseColor = new Color(1.0f, 0.55f, 0.25f);
    [SerializeField] float m_PulseSpeed = 3.0f;
    [SerializeField] float m_SurfaceZ = 0.0f;

    float m_SurfaceY;
    float m_Speed;
    float m_Elapsed;
    bool m_Active;
    Transform m_Visual;
    Material m_Material;

    public float GetSurfaceY()
    {
        return m_SurfaceY;
    }

    public bool IsRising()
    {
        return m_Active;
    }

    public float GetSpeed()
    {
        return m_Speed;
    }

    void Start()
    {
        if (m_Player == null || m_RunManager == null)
        {
            Debug.LogError("RisingHazard: Player and RunManager must both be assigned.");
            enabled = false;
            return;
        }
        if (m_Camera == null)
        {
            m_Camera = Camera.main;
        }

        m_SurfaceY = m_Player.position.y - m_StartOffsetBelow;
        BuildVisual();
        UpdateVisual();
    }

    void Update()
    {
        if (m_RunManager.IsRunOver())
        {
            return;
        }

        m_Elapsed += Time.deltaTime;
        if (!m_Active)
        {
            if (m_Elapsed < m_GraceTime)
            {
                UpdateVisual();
                return;
            }
            m_Active = true;
        }

        float sinceStart = m_Elapsed - m_GraceTime;
        m_Speed = Mathf.Min(m_MaxSpeed, m_StartSpeed + (m_Acceleration * sinceStart));
        m_SurfaceY += m_Speed * Time.deltaTime;

        //Never let it drop out of relevance behind a fast climber
        float floor = m_Player.position.y - m_MaxTrailDistance;
        if (m_SurfaceY < floor)
        {
            m_SurfaceY = floor;
        }

        UpdateVisual();

        if (m_Player.position.y <= m_SurfaceY)
        {
            m_RunManager.EndRun();
        }
    }

    void BuildVisual()
    {
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Collider collider = temp.GetComponent<Collider>();
        if (collider != null)
        {
            //Purely decorative - the kill test is the height comparison in Update,
            //so a physical collider here would fight the character controller
            Destroy(collider);
        }
        temp.name = "HazardSurface";
        temp.transform.SetParent(transform, false);

        m_Material = new Material(Shader.Find("Unlit/Color"));
        m_Material.SetColor("_Color", m_Color);

        MeshRenderer renderer = temp.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = m_Material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        m_Visual = temp.transform;
        m_Visual.localScale = new Vector3(m_Width, m_VisualDepth, 0.4f);
    }

    void UpdateVisual()
    {
        if (m_Visual == null)
        {
            return;
        }
        //Position by its TOP face, which is the surface that kills
        m_Visual.position = new Vector3(0.0f, m_SurfaceY - (m_VisualDepth * 0.5f), m_SurfaceZ);

        if (m_Material == null)
        {
            return;
        }
        float pulse = m_Active ? (Mathf.Sin(Time.time * m_PulseSpeed) * 0.5f) + 0.5f : 0.15f;
        m_Material.SetColor("_Color", Color.Lerp(m_Color, m_PulseColor, pulse));
    }
}
