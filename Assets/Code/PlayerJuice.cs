using UnityEngine;

//--------------------------------------------------------------------
//PlayerJuice is the single owner of the player's visual squash and stretch.
//It reads state straight off the controller and collider each frame, drives a
//spring-damped scale on the sprite child, and fires sound effects and particle
//bursts on state transitions.
//
//Nothing else should write to the visual transform's localScale. CubeFormSwitcher
//routes its form-swap punch through AddPunch() so the two never fight.
//
//Scaling the ROOT would also scale the physics capsule and break wall detection,
//so this only ever touches a child transform.
//--------------------------------------------------------------------
[RequireComponent(typeof(GroundedCharacterController))]
[RequireComponent(typeof(ControlledCapsuleCollider))]
public class PlayerJuice : MonoBehaviour
{
    public enum JuiceState
    {
        Grounded,
        Rising,
        Falling,
        WallSticking,
        WallSlipping
    }

    [Header("References")]
    [Tooltip("Child transform holding the sprite. Leave empty to auto-find the SpriteRenderer's transform.")]
    [SerializeField] Transform m_VisualTransform;
    [Tooltip("Optional. Leave empty and it will be found on this GameObject.")]
    [SerializeField] ProceduralSfx m_Sfx;

    [Header("Airborne stretch")]
    [Tooltip("How much vertical speed stretches the cube along Y. 0 disables speed-based stretch.")]
    [SerializeField] float m_StretchPerSpeed = 0.008f;
    [Tooltip("Hard cap on speed-based stretch so fast falls stay readable.")]
    [Range(0.0f, 0.8f)] [SerializeField] float m_MaxStretch = 0.35f;

    [Header("Wall stick")]
    [Tooltip("How far the cube compresses into the wall along X while stuck to it.")]
    [Range(0.0f, 0.6f)] [SerializeField] float m_WallCompress = 0.22f;
    [Tooltip("Sideways lean into the wall, in world units, applied to the visual only.")]
    [Range(0.0f, 0.3f)] [SerializeField] float m_WallLean = 0.06f;
    [Tooltip("Punch strength on the frame the cube first grabs a wall.")]
    [Range(0.0f, 1.0f)] [SerializeField] float m_WallStickPunch = 0.30f;
    [Tooltip("How far the cube leans into the wall while only slipping. Much weaker than a real grip, " +
        "so the two forms stay readable at a glance.")]
    [Range(0.0f, 0.6f)] [SerializeField] float m_SlipCompress = 0.08f;

    [Header("Impacts")]
    [Tooltip("Squash strength on landing, scaled by impact speed.")]
    [Range(0.0f, 1.0f)] [SerializeField] float m_LandPunch = 0.45f;
    [Tooltip("Stretch strength when leaving the ground.")]
    [Range(0.0f, 1.0f)] [SerializeField] float m_JumpPunch = 0.35f;
    [Tooltip("Fall speed that produces a full-strength landing. Slower landings scale down.")]
    [SerializeField] float m_MaxImpactSpeed = 34.0f;
    [Tooltip("Impacts below this speed are ignored entirely, so walking off a kerb stays quiet.")]
    [SerializeField] float m_MinImpactSpeed = 4.0f;

    [Header("Spring")]
    [Tooltip("Higher = snappier recovery.")]
    [SerializeField] float m_Stiffness = 260.0f;
    [Tooltip("Higher = less wobble. Around 2*sqrt(stiffness) is critically damped.")]
    [SerializeField] float m_Damping = 22.0f;
    [Tooltip("How fast the continuous (non-impulse) shape follows its target.")]
    [SerializeField] float m_ShapeFollow = 18.0f;

    [Header("Particles (all optional)")]
    [Tooltip("One-shot burst when the cube grabs a wall.")]
    [SerializeField] ParticleSystem m_WallStickBurst;
    [Tooltip("One-shot puff on landing.")]
    [SerializeField] ParticleSystem m_LandBurst;
    [Tooltip("One-shot burst on form swap, tinted to the new form's colour.")]
    [SerializeField] ParticleSystem m_SwapBurst;
    [Tooltip("Continuous scrape dust while sliding down a wall. Emitted at the contact point.")]
    [SerializeField] ParticleSystem m_WallSlideDust;
    [Tooltip("Particles emitted per fixed step while scraping.")]
    [SerializeField] int m_WallSlideParticlesPerStep = 2;
    [Tooltip("Minimum slide speed before scrape dust and scrape audio kick in.")]
    [SerializeField] float m_MinSlideSpeed = 2.0f;
    [SerializeField] int m_WallStickParticles = 10;
    [SerializeField] int m_LandParticles = 12;
    [SerializeField] int m_SwapParticles = 16;

    GroundedCharacterController m_Controller;
    ControlledCapsuleCollider m_Collider;
    CubeFormSwitcher m_FormSwitcher;

    Vector3 m_BaseScale;
    Vector3 m_BaseLocalPos;
    //Spring state, expressed as an offset from the resting shape
    float m_Punch;
    float m_PunchVel;
    Vector2 m_Shape = Vector2.one;

    JuiceState m_State = JuiceState.Grounded;
    bool m_CanWallSlideInThisForm;
    bool m_FormGripsWall;
    float m_WallSide;
    float m_LastVerticalSpeed;

    public JuiceState GetState()
    {
        return m_State;
    }

    void Awake()
    {
        m_Controller = GetComponent<GroundedCharacterController>();
        m_Collider = GetComponent<ControlledCapsuleCollider>();
        m_FormSwitcher = GetComponent<CubeFormSwitcher>();
        if (m_Sfx == null)
        {
            m_Sfx = GetComponent<ProceduralSfx>();
        }

        if (m_VisualTransform == null)
        {
            SpriteRenderer sprite = GetComponentInChildren<SpriteRenderer>();
            if (sprite != null)
            {
                m_VisualTransform = sprite.transform;
            }
        }
        if (m_VisualTransform == transform)
        {
            //Scaling the root would scale the capsule and corrupt wall casts
            m_VisualTransform = null;
        }
        if (m_VisualTransform == null)
        {
            Debug.LogWarning("PlayerJuice: no child visual transform found - squash/stretch is disabled. " +
                "Put the sprite on a child object and assign it to Visual Transform.");
        }
        else
        {
            m_BaseScale = m_VisualTransform.localScale;
            m_BaseLocalPos = m_VisualTransform.localPosition;
        }
    }

    void OnEnable()
    {
        if (m_Controller != null)
        {
            m_Controller.OnJump += HandleJump;
        }
        if (m_FormSwitcher != null)
        {
            m_FormSwitcher.OnFormChanged += HandleFormChanged;
        }
    }

    void OnDisable()
    {
        if (m_Controller != null)
        {
            m_Controller.OnJump -= HandleJump;
        }
        if (m_FormSwitcher != null)
        {
            m_FormSwitcher.OnFormChanged -= HandleFormChanged;
        }
    }

    void Start()
    {
        //CubeFormSwitcher applies the starting form in its own Start, which may run
        //before ours, so seed from the current form rather than waiting for an event
        if (m_FormSwitcher != null)
        {
            CubeFormSwitcher.CubeForm form = m_FormSwitcher.GetCurrentForm();
            if (form != null)
            {
                m_CanWallSlideInThisForm = form.m_CanWallSlide;
                m_FormGripsWall = form.m_GripsWall;
            }
        }
    }

    void Update()
    {
        JuiceState previous = m_State;
        m_State = EvaluateState();

        if (m_State != previous)
        {
            HandleTransition(previous, m_State);
        }

        DriveWallSlideLoop();
        m_LastVerticalSpeed = m_Collider.GetVelocity().y;
    }

    //LateUpdate so the visual settles after any gameplay movement this frame
    void LateUpdate()
    {
        if (m_VisualTransform == null)
        {
            return;
        }

        Vector2 target = ResolveTargetShape();
        m_Shape = Vector2.Lerp(m_Shape, target, 1.0f - Mathf.Exp(-m_ShapeFollow * Time.deltaTime));

        //Critically-ish damped spring pulling the punch back to zero
        float accel = (-m_Stiffness * m_Punch) - (m_Damping * m_PunchVel);
        m_PunchVel += accel * Time.deltaTime;
        m_Punch += m_PunchVel * Time.deltaTime;

        //A positive punch squashes (wide and short), negative stretches (tall and thin)
        float x = m_Shape.x * (1.0f + m_Punch);
        float y = m_Shape.y * (1.0f - m_Punch);

        m_VisualTransform.localScale = new Vector3(
            m_BaseScale.x * Mathf.Max(0.05f, x),
            m_BaseScale.y * Mathf.Max(0.05f, y),
            m_BaseScale.z);

        float leanScale = 0.0f;
        if (m_State == JuiceState.WallSticking) leanScale = 1.0f;
        else if (m_State == JuiceState.WallSlipping) leanScale = 0.35f;
        float lean = -m_WallSide * m_WallLean * leanScale;
        Vector3 pos = m_BaseLocalPos;
        pos.x += lean;
        m_VisualTransform.localPosition = Vector3.Lerp(m_VisualTransform.localPosition, pos,
            1.0f - Mathf.Exp(-m_ShapeFollow * Time.deltaTime));
    }

    //----------------------------------------------------------------
    //State
    //----------------------------------------------------------------
    JuiceState EvaluateState()
    {
        if (m_Collider.IsGrounded())
        {
            return JuiceState.Grounded;
        }

        CSideCastInfo sideCast = m_Collider.GetSideCastInfo();
        if (m_CanWallSlideInThisForm && sideCast != null && sideCast.m_WallCastCount >= 2)
        {
            //Normal points away from the wall, so negating it gives the side the wall is on
            m_WallSide = -Mathf.Sign(sideCast.GetSideNormal().x);
            return m_FormGripsWall ? JuiceState.WallSticking : JuiceState.WallSlipping;
        }

        return m_Collider.GetVelocity().y > 0.0f ? JuiceState.Rising : JuiceState.Falling;
    }

    Vector2 ResolveTargetShape()
    {
        if (m_State == JuiceState.WallSticking)
        {
            //Squeeze into the wall along X, and let the cube elongate vertically
            return new Vector2(1.0f - m_WallCompress, 1.0f + (m_WallCompress * 0.6f));
        }

        if (m_State == JuiceState.WallSlipping)
        {
            //Barely touching - a hint of contact rather than a grip
            return new Vector2(1.0f - m_SlipCompress, 1.0f + (m_SlipCompress * 0.6f));
        }

        if (m_State == JuiceState.Rising || m_State == JuiceState.Falling)
        {
            float stretch = Mathf.Clamp(Mathf.Abs(m_Collider.GetVelocity().y) * m_StretchPerSpeed,
                0.0f, m_MaxStretch);
            //Preserve apparent volume: taller means thinner
            return new Vector2(1.0f - (stretch * 0.5f), 1.0f + stretch);
        }

        return Vector2.one;
    }

    void HandleTransition(JuiceState a_From, JuiceState a_To)
    {
        if (a_To == JuiceState.Grounded && a_From != JuiceState.Grounded)
        {
            float impact = Mathf.Abs(m_LastVerticalSpeed);
            if (impact >= m_MinImpactSpeed)
            {
                float strength = Mathf.InverseLerp(m_MinImpactSpeed, m_MaxImpactSpeed, impact);
                AddPunch(m_LandPunch * strength);
                if (m_Sfx != null)
                {
                    m_Sfx.PlayLand(strength);
                }
                Burst(m_LandBurst, Mathf.RoundToInt(m_LandParticles * Mathf.Max(0.35f, strength)));
            }
        }

        if (a_To == JuiceState.WallSticking)
        {
            AddPunch(m_WallStickPunch);
            if (m_Sfx != null)
            {
                m_Sfx.PlayWallStick();
            }
            Burst(m_WallStickBurst, m_WallStickParticles);
        }
    }

    void DriveWallSlideLoop()
    {
        if (m_Sfx == null)
        {
            return;
        }
        bool onWall = (m_State == JuiceState.WallSticking || m_State == JuiceState.WallSlipping);
        //Only make noise while actually moving down the wall
        float speed = Mathf.Abs(m_Collider.GetVelocity().y);
        if (!onWall || speed < m_MinSlideSpeed)
        {
            m_Sfx.SetWallSlide(0.0f);
            m_Sfx.SetWallSlip(0.0f);
            return;
        }

        float intensity = Mathf.Clamp01(speed / 12.0f);
        if (m_State == JuiceState.WallSticking)
        {
            m_Sfx.SetWallSlide(intensity);
            m_Sfx.SetWallSlip(0.0f);
        }
        else
        {
            m_Sfx.SetWallSlide(0.0f);
            m_Sfx.SetWallSlip(intensity);
        }
    }

    //Emitted on the physics step so the contact point matches the collider's own cast data
    void FixedUpdate()
    {
        if (m_WallSlideDust == null)
        {
            return;
        }
        if (m_State != JuiceState.WallSticking && m_State != JuiceState.WallSlipping)
        {
            return;
        }
        if (Mathf.Abs(m_Collider.GetVelocity().y) < m_MinSlideSpeed)
        {
            return;
        }

        CSideCastInfo sideCast = m_Collider.GetSideCastInfo();
        if (sideCast == null || sideCast.m_WallCastCount < 2)
        {
            return;
        }

        Vector2 normal = sideCast.GetSideNormal();
        m_WallSlideDust.transform.position = sideCast.GetSidePoint();
        //Aim the emitter out along the wall normal so dust sprays off the surface
        m_WallSlideDust.transform.LookAt(
            sideCast.GetSidePoint() + new Vector3(normal.x, normal.y, 0.0f), Vector3.back);
        int count = (m_State == JuiceState.WallSticking)
            ? m_WallSlideParticlesPerStep
            : Mathf.Max(1, m_WallSlideParticlesPerStep / 2);
        m_WallSlideDust.Emit(count);
    }

    //----------------------------------------------------------------
    //Events
    //----------------------------------------------------------------
    void HandleJump()
    {
        //Negative punch = stretch tall on the way up
        AddPunch(-m_JumpPunch);
        if (m_Sfx != null)
        {
            if (m_State == JuiceState.WallSticking)
            {
                m_Sfx.PlayWallJump();
            }
            else
            {
                m_Sfx.PlayJump();
            }
        }
    }

    void HandleFormChanged(CubeFormSwitcher.CubeForm a_Form, int a_Index)
    {
        m_CanWallSlideInThisForm = a_Form.m_CanWallSlide;
        m_FormGripsWall = a_Form.m_GripsWall;

        //m_SquashStretchPunch is authored around 1.0, where >1 stretches and <1 squashes
        AddPunch(1.0f - a_Form.m_SquashStretchPunch);
        if (m_Sfx != null)
        {
            m_Sfx.PlaySwap(!a_Form.m_GripsWall);
        }

        if (m_SwapBurst != null)
        {
            ParticleSystem.MainModule main = m_SwapBurst.main;
            main.startColor = a_Form.m_Color;
            Burst(m_SwapBurst, m_SwapParticles);
        }
    }

    //----------------------------------------------------------------
    //Public API
    //----------------------------------------------------------------
    //Positive squashes, negative stretches. Other scripts route their juice through here
    //so there is only ever one writer to the visual transform.
    public void AddPunch(float a_Amount)
    {
        m_PunchVel += a_Amount * m_Stiffness * 0.1f;
    }

    void Burst(ParticleSystem a_System, int a_Count)
    {
        if (a_System == null || a_Count <= 0)
        {
            return;
        }
        a_System.Emit(a_Count);
    }
}
