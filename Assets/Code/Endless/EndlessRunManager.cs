using UnityEngine;
using UnityEngine.SceneManagement;

//--------------------------------------------------------------------
//Owns the endless run: scoring by height climbed, milestone events, and ending
//the run when the player drops out of view.
//
//Score is based on the HIGHEST point reached, not the current one, so dropping
//back down a wall never takes points away - only falling out of frame ends things.
//--------------------------------------------------------------------
public class EndlessRunManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Transform m_Player;
    [SerializeField] EndlessClimbCamera m_Camera;
    [Tooltip("Optional. Used to avoid ending the run before the shaft has finished building at startup.")]
    [SerializeField] EndlessShaftGenerator m_Generator;

    [Header("Scoring")]
    [Tooltip("Points awarded per world unit climbed above the starting height.")]
    [SerializeField] float m_PointsPerUnit = 10.0f;
    [Tooltip("A milestone fires every time the score crosses a multiple of this.")]
    [SerializeField] int m_MilestoneInterval = 1000;

    [Header("Falling")]
    [Tooltip("How far below the bottom of the view the player must fall before the run ends. " +
        "A little slack stops a near-miss at the screen edge from being fatal.")]
    [SerializeField] float m_FallMargin = 2.0f;
    [Tooltip("Pause before the scene reloads, so the death sound and final score are readable.")]
    [SerializeField] float m_RestartDelay = 0.75f;
    [Tooltip("Turn off to practise without ever dying.")]
    [SerializeField] bool m_FallEndsRun = true;

    float m_StartY;
    float m_HighestY;
    int m_Score;
    int m_LastMilestone;
    bool m_RunOver;
    float m_RestartAt;

    public delegate void ScoreEvent(int a_Score);
    public delegate void MilestoneEvent(int a_MilestoneValue, int a_MilestoneIndex);

    public event ScoreEvent OnScoreChanged;
    public event MilestoneEvent OnMilestone;
    public event ScoreEvent OnRunEnded;

    public int GetScore()
    {
        return m_Score;
    }

    public bool IsRunOver()
    {
        return m_RunOver;
    }

    void Start()
    {
        if (m_Player == null || m_Camera == null)
        {
            Debug.LogError("EndlessRunManager: Player and Camera must both be assigned.");
            enabled = false;
            return;
        }
        m_StartY = m_Player.position.y;
        m_HighestY = m_StartY;
    }

    void Update()
    {
        if (m_RunOver)
        {
            if (Time.time >= m_RestartAt)
            {
                Restart();
            }
            return;
        }

        UpdateScore();
        CheckFall();
    }

    void UpdateScore()
    {
        if (m_Player.position.y <= m_HighestY)
        {
            return;
        }
        m_HighestY = m_Player.position.y;

        int newScore = Mathf.Max(0, Mathf.FloorToInt((m_HighestY - m_StartY) * m_PointsPerUnit));
        if (newScore == m_Score)
        {
            return;
        }
        m_Score = newScore;

        if (OnScoreChanged != null)
        {
            OnScoreChanged(m_Score);
        }

        if (m_MilestoneInterval <= 0)
        {
            return;
        }
        //A single frame can cross more than one milestone at high speed, so fire each in turn
        int reached = m_Score / m_MilestoneInterval;
        while (m_LastMilestone < reached)
        {
            m_LastMilestone++;
            if (OnMilestone != null)
            {
                OnMilestone(m_LastMilestone * m_MilestoneInterval, m_LastMilestone);
            }
        }
    }

    void CheckFall()
    {
        if (!m_FallEndsRun)
        {
            return;
        }
        float killY = m_Camera.GetBottomEdgeY() - m_FallMargin;
        if (m_Player.position.y >= killY)
        {
            return;
        }
        EndRun();
    }

    //Public so hazards can end the run too, not just falling out of view
    public void EndRun()
    {
        if (m_RunOver)
        {
            return;
        }
        m_RunOver = true;
        m_RestartAt = Time.time + m_RestartDelay;

        if (OnRunEnded != null)
        {
            OnRunEnded(m_Score);
        }
    }

    void Restart()
    {
        Scene scene = gameObject.scene;
        //Load by name rather than build index: this scene may not be in the build
        //settings, and LoadScene(-1) would throw
        SceneManager.LoadScene(scene.name);
    }
}
