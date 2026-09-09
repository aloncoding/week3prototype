using UnityEngine;

//--------------------------------------------------------------------
//FormChainAssist makes the intended wall-chaining combo possible:
//
//  1. approach a wall, swap to the sticking form to grab it
//  2. swap to the high-jump form just before kicking off
//  3. jump, and get the high-jump form's launch velocity off the wall
//
//Step 2 is the problem this solves. The high-jump form has wall jump locked,
//so swapping to it on a wall would normally kill the jump outright. Instead we
//open a short grace window whenever the player swaps AWAY from a sticking form
//while actually stuck, during which wall jump stays unlocked regardless of what
//the form says. Miss the window and the lock comes back.
//
//The window is what makes this a skill rather than a freebie: leaving wall jump
//permanently unlocked on the light form would let you chain walls forever
//without ever swapping, which removes the point of having two forms.
//
//Attach to the Player, alongside CubeFormSwitcher.
//--------------------------------------------------------------------
[RequireComponent(typeof(CubeFormSwitcher))]
[RequireComponent(typeof(GroundedCharacterController))]
[RequireComponent(typeof(ControlledCapsuleCollider))]
public class FormChainAssist : MonoBehaviour
{
    [Header("Chain window")]
    [Tooltip("Seconds after swapping off a wall-sticking form during which wall jump stays unlocked. " +
        "Raise it to make the combo more forgiving, lower it to demand tighter timing.")]
    [Range(0.0f, 0.6f)] [SerializeField] float m_ChainWindow = 0.20f;
    [Tooltip("How long after leaving the wall a swap can still open the window. Covers the case where the " +
        "player swaps a frame or two after the stick has already broken.")]
    [Range(0.0f, 0.4f)] [SerializeField] float m_StickMemory = 0.12f;
    [Tooltip("If true the window only opens when the player was genuinely stuck to a wall. " +
        "Turn off to let the light form wall jump freely - simpler, but removes the timing skill.")]
    [SerializeField] bool m_RequireRecentStick = true;

    [Header("Module names")]
    [Tooltip("Must match the Module Name on your WallJumpModule.")]
    [SerializeField] string m_WallJumpModuleName = "WallJump";
    [Tooltip("Must match the Module Name on your WallSlidingModule.")]
    [SerializeField] string m_WallSlideModuleName = "WallSlide";

    [Header("Feedback")]
    [Tooltip("Optional. Flashes this colour on the sprite while the chain window is open, so the timing is learnable.")]
    [SerializeField] bool m_TintDuringWindow = true;
    [SerializeField] Color m_WindowTint = new Color(1.0f, 1.0f, 1.0f, 1.0f);

    CubeFormSwitcher m_FormSwitcher;
    GroundedCharacterController m_Controller;
    ControlledCapsuleCollider m_Collider;
    WallJumpModule m_WallJumpModule;
    WallSlidingModule m_WallSlideModule;
    SpriteRenderer m_SpriteRenderer;

    float m_LastStuckTime = -99.0f;
    float m_WindowClosesAt = -99.0f;
    bool m_WindowOpen;
    bool m_FormWantsWallJumpLocked;
    bool m_FormGripsWall;
    Color m_FormColor = Color.white;

    public bool IsChainWindowOpen()
    {
        return m_WindowOpen;
    }

    //Fires when the window opens, so juice or UI can react
    public delegate void OnChainWindowEvent(bool a_IsOpen);
    public event OnChainWindowEvent OnChainWindowChanged;

    void Awake()
    {
        m_FormSwitcher = GetComponent<CubeFormSwitcher>();
        m_Controller = GetComponent<GroundedCharacterController>();
        m_Collider = GetComponent<ControlledCapsuleCollider>();
        m_SpriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    void OnEnable()
    {
        if (m_FormSwitcher != null)
        {
            m_FormSwitcher.OnFormChanged += HandleFormChanged;
        }
    }

    void OnDisable()
    {
        if (m_FormSwitcher != null)
        {
            m_FormSwitcher.OnFormChanged -= HandleFormChanged;
        }
    }

    void Start()
    {
        AbilityModuleManager manager = m_Controller != null ? m_Controller.GetAbilityModuleManager() : null;
        if (manager == null)
        {
            Debug.LogError("FormChainAssist: no AbilityModuleManager on " + name);
            enabled = false;
            return;
        }

        m_WallJumpModule = manager.GetModuleWithName(m_WallJumpModuleName) as WallJumpModule;
        m_WallSlideModule = manager.GetModuleWithName(m_WallSlideModuleName) as WallSlidingModule;

        if (m_WallJumpModule == null)
        {
            Debug.LogWarning("FormChainAssist: no WallJumpModule named \"" + m_WallJumpModuleName +
                "\" - wall chaining is disabled.");
            enabled = false;
            return;
        }

        CubeFormSwitcher.CubeForm form = m_FormSwitcher.GetCurrentForm();
        if (form != null)
        {
            m_FormWantsWallJumpLocked = !form.m_CanWallJump;
            m_FormGripsWall = form.m_GripsWall;
            m_FormColor = form.m_Color;
        }
    }

    void Update()
    {
        TrackWallStick();

        if (!m_WindowOpen)
        {
            return;
        }

        //The window also closes the moment the jump is spent, so a successful
        //chain does not leave a lingering free wall jump behind it
        bool expired = Time.time >= m_WindowClosesAt;
        bool spent = m_Controller.DidJustJump();
        bool landed = m_Collider.IsGrounded();

        if (expired || spent || landed)
        {
            CloseWindow();
        }
    }

    //Remember the last moment the player was genuinely stuck to a wall
    void TrackWallStick()
    {
        //Both forms may have the slide module unlocked now, so the module's lock state
        //no longer tells us whether the cube is actually gripping. Only a gripping form counts.
        if (m_WallSlideModule == null || m_WallSlideModule.IsLocked() || !m_FormGripsWall)
        {
            return;
        }
        if (m_Collider.IsGrounded())
        {
            return;
        }
        CSideCastInfo sideCast = m_Collider.GetSideCastInfo();
        if (sideCast != null && sideCast.m_WallCastCount >= 2)
        {
            m_LastStuckTime = Time.time;
        }
    }

    void HandleFormChanged(CubeFormSwitcher.CubeForm a_Form, int a_Index)
    {
        m_FormWantsWallJumpLocked = !a_Form.m_CanWallJump;
        m_FormColor = a_Form.m_Color;
        //Read the OLD grip state before overwriting it - the swap we are handling is
        //the one that ends the stick, so whether it counts as a chain depends on the
        //form we are leaving, not the one we are entering
        bool wasGripping = m_FormGripsWall;
        m_FormGripsWall = a_Form.m_GripsWall;

        //Swapping into a form that allows wall jump anyway needs no assistance
        if (!m_FormWantsWallJumpLocked)
        {
            if (m_WindowOpen)
            {
                CloseWindow();
            }
            return;
        }

        bool stuckRecently = wasGripping && (Time.time - m_LastStuckTime) <= m_StickMemory;
        if (m_RequireRecentStick && !stuckRecently)
        {
            //Not a chain, just a normal swap - honour the form's lock
            ApplyLock(true);
            return;
        }

        OpenWindow();
    }

    void OpenWindow()
    {
        m_WindowOpen = true;
        m_WindowClosesAt = Time.time + m_ChainWindow;
        ApplyLock(false);
        Tint(m_WindowTint);

        if (OnChainWindowChanged != null)
        {
            OnChainWindowChanged(true);
        }
    }

    void CloseWindow()
    {
        m_WindowOpen = false;
        ApplyLock(m_FormWantsWallJumpLocked);
        Tint(m_FormColor);

        if (OnChainWindowChanged != null)
        {
            OnChainWindowChanged(false);
        }
    }

    void ApplyLock(bool a_Locked)
    {
        if (m_WallJumpModule != null)
        {
            m_WallJumpModule.SetLocked(a_Locked);
        }
    }

    void Tint(Color a_Color)
    {
        if (!m_TintDuringWindow || m_SpriteRenderer == null)
        {
            return;
        }
        m_SpriteRenderer.color = a_Color;
    }
}
