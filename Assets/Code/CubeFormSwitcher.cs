using UnityEngine;
using System.Collections;

//--------------------------------------------------------------------
//CubeFormSwitcher lets the player press a button to cycle the cube between
//a set of "Forms" (e.g. Light/Fast vs Heavy/Sticky). Each form carries its
//own color and movement stats, which get pushed onto the
//GroundedCharacterController whenever the form changes.
//
//Attach this to the same GameObject as GroundedCharacterController and
//PlayerInput. Requires a button input (default name "SwitchForm") to be
//added to PlayerInput's input list in the inspector.
//--------------------------------------------------------------------
[RequireComponent(typeof(PlayerInput))]
public class CubeFormSwitcher : MonoBehaviour
{
    [System.Serializable]
    public class CubeForm
    {
        public string m_FormName = "Form";
        public Color m_Color = Color.white;
        public float m_JumpVelocity = 32.0f;
        public float m_Gravity = 50.0f;
        public float m_FrictionConstant = 8.0f;
        [Range(0f, 1f)] public float m_WallStickFactor = 0.0f; //minor effect - see note below on WallSlidingModule
        [Tooltip("Pushed into WallSlidingModule.SetSlideGravity(). Fill in your current WallSlidingModule value for the Light form, then reduce it for Heavy so it falls slower.")]
        public float m_WallSlideGravity = 0.0f;
        [Tooltip("Pushed into WallSlidingModule.SetSlideFriction(). Fill in your current WallSlidingModule value for the Light form, then increase it for Heavy so it grips harder.")]
        public float m_WallSlideFriction = 0.0f;
        [Range(0.5f, 1.5f)] public float m_SquashStretchPunch = 1.0f; //visual juice multiplier on switch
    }

    [Tooltip("Must match a Button-type input name configured on this object's PlayerInput.")]
    [SerializeField] string m_SwitchInputName = "SwitchForm";
    [Tooltip("Unity input button name used if the SwitchForm input has to be auto-registered on PlayerInput.")]
    [SerializeField] string m_DefaultUnityButtonName = "Fire1";
    [Tooltip("Must match the Module Name field set on your WallJumpModule component.")]
    [SerializeField] string m_WallJumpModuleName = "WallJump";
    [Tooltip("Must match the Module Name field set on your WallSlidingModule component.")]
    [SerializeField] string m_WallSlideModuleName = "WallSlide";
    [Tooltip("Transform to squash/stretch on form switch. MUST be a child object holding the sprite, not the root - scaling the root would also scale the physics collider and cause wall-detection glitches. Leave empty to auto-use the SpriteRenderer's transform.")]
    [SerializeField] Transform m_VisualTransform;
    [SerializeField] CubeForm[] m_Forms;
    [SerializeField] int m_StartingFormIndex = 0;
    [SerializeField] float m_SwitchAnimDuration = 0.12f;

    GroundedCharacterController m_Controller;
    PlayerInput m_PlayerInput;
    ButtonInput m_SwitchInput;
    SpriteRenderer m_SpriteRenderer;
    Renderer m_GenericRenderer;
    MaterialPropertyBlock m_PropBlock;
    WallJumpModule m_WallJumpModule;
    WallSlidingModule m_WallSlideModule;

    int m_CurrentFormIndex = -1;
    Coroutine m_JuiceRoutine;
    Vector3 m_BaseScale;

    public delegate void OnFormChangedEvent(CubeForm a_NewForm, int a_NewIndex);
    public event OnFormChangedEvent OnFormChanged;

    //Provides sensible defaults so you can drop this on a cube and press Play
    void Reset()
    {
        m_Forms = new CubeForm[2];
        //Heavy == your tuned baseline, untouched. This is the "normal" feel of the game.
        m_Forms[0] = new CubeForm
        {
            m_FormName = "Heavy",
            m_Color = new Color(0.16f, 0.17f, 0.22f), //dark charcoal - reads as "heavy"
            m_JumpVelocity = 32.0f,
            m_Gravity = 50.0f,
            m_FrictionConstant = 8.0f,
            m_WallStickFactor = 0.0f, //deprecated, WallSlidingModule handles stickiness now - see notes
            m_WallSlideGravity = 40.0f,
            m_WallSlideFriction = 27.0f,
            m_SquashStretchPunch = 0.85f
        };
        //Light == a clear deviation above that baseline: jumps a bit higher, slides down walls
        //faster (higher slide gravity, lower slide friction = weaker grip).
        m_Forms[1] = new CubeForm
        {
            m_FormName = "Light",
            m_Color = new Color(1f, 0.92f, 0.35f), //bright pale yellow - reads as "light"
            m_JumpVelocity = 38.0f,
            m_Gravity = 50.0f,
            m_FrictionConstant = 5.0f,
            m_WallStickFactor = 0.0f, //deprecated, WallSlidingModule handles stickiness now - see notes
            m_WallSlideGravity = 65.0f,
            m_WallSlideFriction = 10.0f,
            m_SquashStretchPunch = 1.2f
        };
    }

    void Awake()
    {
        m_Controller = GetComponent<GroundedCharacterController>();
        m_PlayerInput = GetComponent<PlayerInput>();
        m_SpriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (m_SpriteRenderer == null)
        {
            m_GenericRenderer = GetComponentInChildren<Renderer>();
            m_PropBlock = new MaterialPropertyBlock();
        }
        m_BaseScale = transform.localScale;

        if (m_VisualTransform == null && m_SpriteRenderer != null)
        {
            m_VisualTransform = m_SpriteRenderer.transform;
        }
        if (m_VisualTransform == null || m_VisualTransform == transform)
        {
            m_VisualTransform = null;
            Debug.LogWarning("CubeFormSwitcher: no separate visual child transform found - squash/stretch juice is " +
                "DISABLED to avoid scaling the collider's own root transform. Put the sprite on a child object and " +
                "assign it to Visual Transform to re-enable the juice.");
        }
        else
        {
            m_BaseScale = m_VisualTransform.localScale;
        }

        if (m_Controller == null)
        {
            Debug.LogError("CubeFormSwitcher requires a GroundedCharacterController on " + name);
        }
    }

    void Start()
    {
        if (m_PlayerInput != null && !m_PlayerInput.DoesInputExist(m_SwitchInputName))
        {
            //Not configured in the inspector yet - register it at runtime so this works out of the box
            m_PlayerInput.EnsureSwitchFormInputIsSet(m_SwitchInputName, m_DefaultUnityButtonName);
        }

        if (m_PlayerInput != null && m_PlayerInput.DoesInputExist(m_SwitchInputName))
        {
            m_SwitchInput = m_PlayerInput.GetButton(m_SwitchInputName);
            Debug.Log("CubeFormSwitcher: found \"" + m_SwitchInputName + "\" input, ButtonInput is " +
                (m_SwitchInput == null ? "NULL" : "OK"));
        }
        else
        {
            Debug.LogError("CubeFormSwitcher: PlayerInput has no button input named \"" + m_SwitchInputName +
                "\" and it could not be auto-registered. Check PlayerInput.EnsureSwitchFormInputIsSet exists.");
        }

        if (m_Forms != null && m_Forms.Length > 0)
        {
            AbilityModuleManager abilityManager = m_Controller != null ? m_Controller.GetAbilityModuleManager() : null;
            if (abilityManager != null)
            {
                AbilityModule wallJump = abilityManager.GetModuleWithName(m_WallJumpModuleName);
                m_WallJumpModule = wallJump as WallJumpModule;
                if (m_WallJumpModule == null)
                {
                    Debug.LogWarning("CubeFormSwitcher: no WallJumpModule found named \"" + m_WallJumpModuleName +
                        "\" - wall jump height won't change between forms.");
                }

                AbilityModule wallSlide = abilityManager.GetModuleWithName(m_WallSlideModuleName);
                m_WallSlideModule = wallSlide as WallSlidingModule;
                if (m_WallSlideModule == null)
                {
                    Debug.LogWarning("CubeFormSwitcher: no WallSlidingModule found named \"" + m_WallSlideModuleName +
                        "\" - wall stickiness won't change between forms.");
                }
            }

            ApplyForm(Mathf.Clamp(m_StartingFormIndex, 0, m_Forms.Length - 1), false);
        }
    }

    void Update()
    {
        if (m_SwitchInput == null || m_Forms == null || m_Forms.Length == 0)
        {
            return;
        }

        if (m_SwitchInput.m_WasJustPressed)
        {
            m_SwitchInput.m_WasJustPressed = false;
            int nextIndex = (m_CurrentFormIndex + 1) % m_Forms.Length;
            Debug.Log("CubeFormSwitcher: switch pressed, moving to form " + nextIndex);
            ApplyForm(nextIndex, true);
        }
    }

    void ApplyForm(int a_Index, bool a_PlayJuice)
    {
        if (m_Forms == null || a_Index < 0 || a_Index >= m_Forms.Length)
        {
            return;
        }
        m_CurrentFormIndex = a_Index;
        CubeForm form = m_Forms[a_Index];

        if (m_Controller != null)
        {
            m_Controller.SetJumpVelocity(form.m_JumpVelocity);
            m_Controller.SetGravity(form.m_Gravity);
            m_Controller.SetFrictionConstant(form.m_FrictionConstant);
            m_Controller.SetWallStickFactor(form.m_WallStickFactor);
        }

        if (m_WallJumpModule != null)
        {
            m_WallJumpModule.SetJumpVelocity(form.m_JumpVelocity);
        }

        if (m_WallSlideModule != null)
        {
            m_WallSlideModule.SetSlideGravity(form.m_WallSlideGravity);
            m_WallSlideModule.SetSlideFriction(form.m_WallSlideFriction);
        }

        SetColor(form.m_Color);

        if (a_PlayJuice && m_VisualTransform != null)
        {
            if (m_JuiceRoutine != null)
            {
                StopCoroutine(m_JuiceRoutine);
            }
            m_JuiceRoutine = StartCoroutine(SquashStretchPunch(form.m_SquashStretchPunch));
        }

        if (OnFormChanged != null)
        {
            OnFormChanged(form, a_Index);
        }
    }

    void SetColor(Color a_Color)
    {
        if (m_SpriteRenderer != null)
        {
            m_SpriteRenderer.color = a_Color;
        }
        else if (m_GenericRenderer != null)
        {
            m_GenericRenderer.GetPropertyBlock(m_PropBlock);
            m_PropBlock.SetColor("_Color", a_Color);
            m_GenericRenderer.SetPropertyBlock(m_PropBlock);
        }
    }

    //Quick squash-stretch juice pass so the switch actually feels like something happened
    IEnumerator SquashStretchPunch(float a_Punch)
    {
        float t = 0f;
        Vector3 squashed = new Vector3(m_BaseScale.x * (2f - a_Punch), m_BaseScale.y * a_Punch, m_BaseScale.z);
        while (t < m_SwitchAnimDuration)
        {
            t += Time.deltaTime;
            float lerpT = t / m_SwitchAnimDuration;
            m_VisualTransform.localScale = Vector3.Lerp(squashed, m_BaseScale, lerpT);
            yield return null;
        }
        m_VisualTransform.localScale = m_BaseScale;
    }

    public CubeForm GetCurrentForm()
    {
        if (m_Forms == null || m_CurrentFormIndex < 0 || m_CurrentFormIndex >= m_Forms.Length)
        {
            return null;
        }
        return m_Forms[m_CurrentFormIndex];
    }

    public int GetCurrentFormIndex()
    {
        return m_CurrentFormIndex;
    }
}