using UnityEngine;

//--------------------------------------------------------------------
//Camera for the endless climb. Follows the player horizontally, but only ever
//RISES vertically - once the view has moved up it never comes back down. That
//ratchet is what turns a fall into a lost run: drop below the bottom edge and
//there is no way back into frame.
//
//Replaces BasicCameraTracker on this scene's camera. BasicCameraTracker itself is
//left untouched because the other demo scenes still use it.
//--------------------------------------------------------------------
public class EndlessClimbCamera : MonoBehaviour
{
    [SerializeField] Transform m_Target;
    [Tooltip("How quickly the camera catches up horizontally.")]
    [SerializeField] float m_HorizontalFollow = 4.0f;
    [Tooltip("How quickly the camera rises to follow the player up.")]
    [SerializeField] float m_RiseFollow = 3.0f;
    [Tooltip("Distance kept on Z. The gameplay plane is at z = 0.")]
    [SerializeField] float m_ZDistance = 10.0f;
    [Tooltip("Where the player sits vertically on screen, as a viewport fraction. " +
        "0.5 centres them; HIGHER values push them up the screen, which is what opens up " +
        "room underneath to see the rising hazard closing in.")]
    [Range(0.15f, 0.85f)] [SerializeField] float m_PlayerViewportY = 0.66f;
    [Tooltip("Gameplay plane depth. Used to work out how much world height one screen covers.")]
    [SerializeField] float m_PlanePositionZ = 0.0f;
    [Tooltip("Once the view has risen it never comes back down. This is what makes a fall fatal.")]
    [SerializeField] bool m_NeverDescend = true;

    Camera m_Camera;
    float m_HighestY;

    void Awake()
    {
        m_Camera = GetComponent<Camera>();
        m_HighestY = transform.position.y;
    }

    void Start()
    {
        //Snap to the correct framing before the first frame is drawn. Without this the
        //never-descend ratchet is stuck at whatever height the camera was authored at,
        //so the requested viewport line cannot be reached until the player climbs past
        //it - which is exactly the room under the player we are trying to open up.
        SnapToTarget();
    }

    public void SnapToTarget()
    {
        if (m_Target == null)
        {
            return;
        }
        float desiredY = m_Target.position.y - ((m_PlayerViewportY - 0.5f) * GetViewHeight());
        Vector3 pos = transform.position;
        pos.x = m_Target.position.x;
        pos.y = desiredY;
        pos.z = -m_ZDistance;
        transform.position = pos;
        m_HighestY = desiredY;
    }

    void LateUpdate()
    {
        if (m_Target == null)
        {
            return;
        }

        Vector3 pos = transform.position;

        pos.x = Mathf.Lerp(pos.x, m_Target.position.x,
            1.0f - Mathf.Exp(-m_HorizontalFollow * Time.deltaTime));

        //Place the camera so the player lands on the requested viewport line. Framing
        //this as a fraction of the screen keeps it correct at any aspect or field of view,
        //unlike a fixed world-space offset.
        float viewHeight = GetViewHeight();
        float desiredY = m_Target.position.y - ((m_PlayerViewportY - 0.5f) * viewHeight);
        if (m_NeverDescend)
        {
            //Ratchet: only ever take the higher of where we are and where we want to be
            desiredY = Mathf.Max(desiredY, m_HighestY);
        }
        pos.y = Mathf.Lerp(pos.y, desiredY, 1.0f - Mathf.Exp(-m_RiseFollow * Time.deltaTime));

        if (m_NeverDescend)
        {
            pos.y = Mathf.Max(pos.y, m_HighestY);
            m_HighestY = pos.y;
        }

        pos.z = -m_ZDistance;
        transform.position = pos;
    }

    //World-space Y of the bottom of the view, measured on the gameplay plane (z = 0).
    //Works for both perspective and orthographic cameras.
    public float GetBottomEdgeY()
    {
        return EdgeY(0.0f);
    }

    public float GetTopEdgeY()
    {
        return EdgeY(1.0f);
    }

    //World height covered by one screen at the gameplay plane
    public float GetViewHeight()
    {
        if (m_Camera == null)
        {
            m_Camera = GetComponent<Camera>();
        }
        if (m_Camera.orthographic)
        {
            return m_Camera.orthographicSize * 2.0f;
        }
        float distance = Mathf.Abs(transform.position.z - m_PlanePositionZ);
        return 2.0f * distance * Mathf.Tan(m_Camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
    }

    float EdgeY(float a_ViewportY)
    {
        if (m_Camera == null)
        {
            m_Camera = GetComponent<Camera>();
        }
        float planeDistance = Mathf.Abs(transform.position.z);
        return m_Camera.ViewportToWorldPoint(new Vector3(0.0f, a_ViewportY, planeDistance)).y;
    }

    public void SetTarget(Transform a_Target)
    {
        m_Target = a_Target;
    }
}
