using UnityEngine;

//--------------------------------------------------------------------
//ProceduralSfx synthesises every sound effect at runtime, so the project needs
//no audio assets on disk. Each Voice is baked into an AudioClip once in Awake,
//then played through a small AudioSource pool. The wall slide is a seamless
//looping noise bed on its own dedicated source so it can fade in and out.
//
//Attach to the Player alongside PlayerJuice, which drives all of this.
//Every field is inspector-tweakable - retuning does not need a re-import.
//--------------------------------------------------------------------
public class ProceduralSfx : MonoBehaviour
{
    public enum Wave
    {
        Square,
        Triangle,
        Saw,
        Sine,
        Noise
    }

    [System.Serializable]
    public class Voice
    {
        public string m_Name = "Voice";
        public Wave m_Wave = Wave.Square;
        [Tooltip("Clip length in seconds. Keep these short - long clips read as musical rather than as feedback.")]
        [Range(0.02f, 1.5f)] public float m_Duration = 0.12f;
        [Tooltip("Pitch at the start of the clip, in Hz.")]
        public float m_StartFreq = 440.0f;
        [Tooltip("Pitch at the end of the clip. Sweep up for 'lift', down for 'weight'.")]
        public float m_EndFreq = 880.0f;
        [Range(0.0f, 1.0f)] public float m_Volume = 0.5f;
        [Tooltip("Fade-in time as a fraction of duration. Near 0 gives a hard percussive attack.")]
        [Range(0.0f, 0.5f)] public float m_Attack = 0.01f;
        [Tooltip("How sharply the tail decays. Higher = snappier.")]
        [Range(0.5f, 12.0f)] public float m_Decay = 5.0f;
        [Tooltip("Blends white noise over the tone. Good for impacts and scrapes.")]
        [Range(0.0f, 1.0f)] public float m_NoiseMix = 0.0f;
        [Tooltip("Quantises the waveform to this many steps for a crunchier, lo-fi timbre. 0 = off.")]
        [Range(0, 32)] public int m_BitCrush = 0;

        [HideInInspector] public AudioClip m_Clip;
    }

    const int k_SampleRate = 44100;

    [Header("Voices")]
    [SerializeField] Voice m_Jump = new Voice
    {
        m_Name = "Jump", m_Wave = Wave.Square, m_Duration = 0.12f,
        m_StartFreq = 320.0f, m_EndFreq = 720.0f, m_Volume = 0.32f,
        m_Attack = 0.01f, m_Decay = 6.0f, m_NoiseMix = 0.0f, m_BitCrush = 8
    };
    [SerializeField] Voice m_WallJump = new Voice
    {
        m_Name = "WallJump", m_Wave = Wave.Square, m_Duration = 0.14f,
        m_StartFreq = 260.0f, m_EndFreq = 620.0f, m_Volume = 0.34f,
        m_Attack = 0.01f, m_Decay = 5.0f, m_NoiseMix = 0.25f, m_BitCrush = 8
    };
    [SerializeField] Voice m_Land = new Voice
    {
        m_Name = "Land", m_Wave = Wave.Triangle, m_Duration = 0.16f,
        m_StartFreq = 180.0f, m_EndFreq = 60.0f, m_Volume = 0.38f,
        m_Attack = 0.0f, m_Decay = 7.0f, m_NoiseMix = 0.45f, m_BitCrush = 0
    };
    [Tooltip("Played when swapping INTO the light/high-jump form. Sweeps upward.")]
    [SerializeField] Voice m_SwapUp = new Voice
    {
        m_Name = "SwapUp", m_Wave = Wave.Triangle, m_Duration = 0.16f,
        m_StartFreq = 480.0f, m_EndFreq = 1040.0f, m_Volume = 0.30f,
        m_Attack = 0.01f, m_Decay = 4.0f, m_NoiseMix = 0.0f, m_BitCrush = 6
    };
    [Tooltip("Played when swapping INTO the heavy/wall-sticking form. Sweeps downward.")]
    [SerializeField] Voice m_SwapDown = new Voice
    {
        m_Name = "SwapDown", m_Wave = Wave.Square, m_Duration = 0.16f,
        m_StartFreq = 520.0f, m_EndFreq = 200.0f, m_Volume = 0.30f,
        m_Attack = 0.01f, m_Decay = 4.0f, m_NoiseMix = 0.0f, m_BitCrush = 6
    };
    [Tooltip("The moment of contact when the heavy form grabs a wall.")]
    [SerializeField] Voice m_WallStick = new Voice
    {
        m_Name = "WallStick", m_Wave = Wave.Noise, m_Duration = 0.09f,
        m_StartFreq = 900.0f, m_EndFreq = 300.0f, m_Volume = 0.30f,
        m_Attack = 0.0f, m_Decay = 9.0f, m_NoiseMix = 1.0f, m_BitCrush = 0
    };

    [Header("Quality jump")]
    [Tooltip("Tone for a perfectly timed chain jump. Kept as a clean sine with no noise or crush " +
        "so it rings clear against every other sound in the mix.")]
    [SerializeField] Voice m_QualityJumpPure = new Voice
    {
        m_Name = "QualityPure", m_Wave = Wave.Sine, m_Duration = 0.30f,
        m_StartFreq = 520.0f, m_EndFreq = 1180.0f, m_Volume = 0.34f,
        m_Attack = 0.005f, m_Decay = 3.2f, m_NoiseMix = 0.0f, m_BitCrush = 0
    };
    [Tooltip("Tone for a barely-made chain jump. Grittier and duller, but never unpleasant.")]
    [SerializeField] Voice m_QualityJumpRough = new Voice
    {
        m_Name = "QualityRough", m_Wave = Wave.Square, m_Duration = 0.15f,
        m_StartFreq = 300.0f, m_EndFreq = 640.0f, m_Volume = 0.30f,
        m_Attack = 0.01f, m_Decay = 6.0f, m_NoiseMix = 0.22f, m_BitCrush = 7
    };
    [Tooltip("How many steps the purity ladder is baked into. Clips are baked once at startup " +
        "rather than synthesised per jump, so a jump never allocates.")]
    [Range(2, 12)] [SerializeField] int m_QualityTiers = 6;

    [Header("Wall slide loop")]
    [Tooltip("Seamless noise bed faded in while sliding down a wall.")]
    [SerializeField] Voice m_WallSlide = new Voice
    {
        m_Name = "WallSlide", m_Wave = Wave.Noise, m_Duration = 0.5f,
        m_StartFreq = 240.0f, m_EndFreq = 240.0f, m_Volume = 0.22f,
        m_Attack = 0.0f, m_Decay = 0.0f, m_NoiseMix = 1.0f, m_BitCrush = 0
    };
    [Tooltip("Thinner, airier bed for the light form losing its grip and slipping down a wall. " +
        "Deliberately higher and softer than the slide so the two forms are audibly different.")]
    [SerializeField] Voice m_WallSlip = new Voice
    {
        m_Name = "WallSlip", m_Wave = Wave.Noise, m_Duration = 0.5f,
        m_StartFreq = 620.0f, m_EndFreq = 620.0f, m_Volume = 0.15f,
        m_Attack = 0.0f, m_Decay = 0.0f, m_NoiseMix = 1.0f, m_BitCrush = 0
    };
    [Tooltip("How quickly the loops fade in and out, in volume units per second.")]
    [SerializeField] float m_SlideFadeSpeed = 6.0f;

    [Header("Output")]
    [SerializeField] int m_VoiceCount = 6;
    [Range(0.0f, 1.0f)] [SerializeField] float m_MasterVolume = 1.0f;

    AudioSource[] m_Sources;
    int m_NextSource;
    Voice[] m_QualityLadder;
    AudioSource m_SlideSource;
    float m_SlideTarget;
    float m_SlideLevel;
    AudioSource m_SlipSource;
    float m_SlipTarget;
    float m_SlipLevel;

    void Awake()
    {
        BakeVoice(m_Jump);
        BakeVoice(m_WallJump);
        BakeVoice(m_Land);
        BakeVoice(m_SwapUp);
        BakeVoice(m_SwapDown);
        BakeVoice(m_WallStick);
        BakeLoop(m_WallSlide);
        BakeLoop(m_WallSlip);
        BakeQualityLadder();

        m_Sources = new AudioSource[Mathf.Max(1, m_VoiceCount)];
        for (int i = 0; i < m_Sources.Length; i++)
        {
            m_Sources[i] = gameObject.AddComponent<AudioSource>();
            m_Sources[i].playOnAwake = false;
            m_Sources[i].spatialBlend = 0.0f;
        }

        m_SlideSource = MakeLoopSource(m_WallSlide);
        m_SlipSource = MakeLoopSource(m_WallSlip);
    }

    void Update()
    {
        //Fade the beds toward their targets so starting/stopping never clicks
        m_SlideLevel = DriveLoop(m_SlideSource, m_WallSlide, m_SlideLevel, m_SlideTarget);
        m_SlipLevel = DriveLoop(m_SlipSource, m_WallSlip, m_SlipLevel, m_SlipTarget);
    }

    AudioSource MakeLoopSource(Voice a_Voice)
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0.0f;
        source.loop = true;
        source.clip = a_Voice.m_Clip;
        source.volume = 0.0f;
        return source;
    }

    float DriveLoop(AudioSource a_Source, Voice a_Voice, float a_Level, float a_Target)
    {
        a_Level = Mathf.MoveTowards(a_Level, a_Target, m_SlideFadeSpeed * Time.deltaTime);
        if (a_Level > 0.001f)
        {
            if (!a_Source.isPlaying)
            {
                a_Source.Play();
            }
            a_Source.volume = a_Level * a_Voice.m_Volume * m_MasterVolume;
        }
        else if (a_Source.isPlaying)
        {
            a_Source.Stop();
        }
        return a_Level;
    }

    //----------------------------------------------------------------
    //Public API - PlayerJuice calls into these
    //----------------------------------------------------------------
    public void PlayJump()
    {
        Play(m_Jump, 1.0f);
    }

    public void PlayWallJump()
    {
        Play(m_WallJump, 1.0f);
    }

    //a_Strength (0..1) scales volume and pitch so a light tap reads differently to a hard slam
    public void PlayLand(float a_Strength)
    {
        Play(m_Land, Mathf.Clamp01(a_Strength), Mathf.Lerp(1.15f, 0.85f, Mathf.Clamp01(a_Strength)));
    }

    public void PlaySwap(bool a_ToLightForm)
    {
        Play(a_ToLightForm ? m_SwapUp : m_SwapDown, 1.0f);
    }

    public void PlayWallStick()
    {
        Play(m_WallStick, 1.0f);
    }

    //a_Quality 0..1, where 1 is a perfectly timed chain jump. Picks the nearest rung of
    //the pre-baked purity ladder and nudges pitch up slightly for the cleanest ones.
    public void PlayQualityJump(float a_Quality)
    {
        if (m_QualityLadder == null || m_QualityLadder.Length == 0)
        {
            PlayWallJump();
            return;
        }
        float q = Mathf.Clamp01(a_Quality);
        int index = Mathf.Clamp(Mathf.RoundToInt(q * (m_QualityLadder.Length - 1)),
            0, m_QualityLadder.Length - 1);
        Play(m_QualityLadder[index], 1.0f, Mathf.Lerp(0.97f, 1.06f, q));
    }

    //a_Intensity 0 stops the loop, 1 is full volume
    public void SetWallSlide(float a_Intensity)
    {
        m_SlideTarget = Mathf.Clamp01(a_Intensity);
    }

    //The light form losing its grip. Kept separate from the slide bed so both
    //can never sound at once and each form stays recognisable by ear.
    public void SetWallSlip(float a_Intensity)
    {
        m_SlipTarget = Mathf.Clamp01(a_Intensity);
    }

    //----------------------------------------------------------------
    //Playback
    //----------------------------------------------------------------
    void Play(Voice a_Voice, float a_Scale, float a_Pitch = 1.0f)
    {
        if (a_Voice == null || a_Voice.m_Clip == null || m_Sources == null || a_Scale <= 0.001f)
        {
            return;
        }
        AudioSource source = m_Sources[m_NextSource];
        m_NextSource = (m_NextSource + 1) % m_Sources.Length;
        source.Stop();
        source.clip = a_Voice.m_Clip;
        source.pitch = a_Pitch;
        source.volume = a_Voice.m_Volume * a_Scale * m_MasterVolume;
        source.Play();
    }

    //----------------------------------------------------------------
    //Synthesis
    //----------------------------------------------------------------
    //Bake the whole gritty-to-pure ladder up front. Synthesising a clip at the moment
    //of the jump would allocate and hitch on exactly the frame that must feel tightest.
    void BakeQualityLadder()
    {
        int tiers = Mathf.Max(2, m_QualityTiers);
        m_QualityLadder = new Voice[tiers];

        for (int i = 0; i < tiers; i++)
        {
            float t = (float)i / (tiers - 1); //0 = rough, 1 = pure
            Voice v = new Voice();
            v.m_Name = "QualityJump_" + i;
            //Waveform itself gets purer, not just the parameters
            v.m_Wave = (t > 0.66f) ? Wave.Sine : ((t > 0.33f) ? Wave.Triangle : Wave.Square);
            v.m_Duration = Mathf.Lerp(m_QualityJumpRough.m_Duration, m_QualityJumpPure.m_Duration, t);
            v.m_StartFreq = Mathf.Lerp(m_QualityJumpRough.m_StartFreq, m_QualityJumpPure.m_StartFreq, t);
            v.m_EndFreq = Mathf.Lerp(m_QualityJumpRough.m_EndFreq, m_QualityJumpPure.m_EndFreq, t);
            v.m_Volume = Mathf.Lerp(m_QualityJumpRough.m_Volume, m_QualityJumpPure.m_Volume, t);
            v.m_Attack = Mathf.Lerp(m_QualityJumpRough.m_Attack, m_QualityJumpPure.m_Attack, t);
            v.m_Decay = Mathf.Lerp(m_QualityJumpRough.m_Decay, m_QualityJumpPure.m_Decay, t);
            v.m_NoiseMix = Mathf.Lerp(m_QualityJumpRough.m_NoiseMix, m_QualityJumpPure.m_NoiseMix, t);
            v.m_BitCrush = Mathf.RoundToInt(Mathf.Lerp(m_QualityJumpRough.m_BitCrush, m_QualityJumpPure.m_BitCrush, t));
            BakeVoice(v);
            m_QualityLadder[i] = v;
        }
    }

    void BakeVoice(Voice a_Voice)
    {
        int sampleCount = Mathf.Max(1, Mathf.RoundToInt(a_Voice.m_Duration * k_SampleRate));
        float[] data = new float[sampleCount];
        float phase = 0.0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleCount;
            float freq = Mathf.Lerp(a_Voice.m_StartFreq, a_Voice.m_EndFreq, t);
            phase += freq / k_SampleRate;
            phase -= Mathf.Floor(phase);

            float sample = Sample(a_Voice.m_Wave, phase);
            if (a_Voice.m_NoiseMix > 0.0f)
            {
                sample = Mathf.Lerp(sample, Random.Range(-1.0f, 1.0f), a_Voice.m_NoiseMix);
            }
            sample *= Envelope(t, a_Voice.m_Attack, a_Voice.m_Decay);
            data[i] = Crush(sample, a_Voice.m_BitCrush);
        }

        a_Voice.m_Clip = AudioClip.Create(a_Voice.m_Name, sampleCount, 1, k_SampleRate, false);
        a_Voice.m_Clip.SetData(data, 0);
    }

    //Loop variant: no envelope, and the tail is cross-faded into the head so it tiles seamlessly
    void BakeLoop(Voice a_Voice)
    {
        int sampleCount = Mathf.Max(1, Mathf.RoundToInt(a_Voice.m_Duration * k_SampleRate));
        float[] data = new float[sampleCount];
        float phase = 0.0f;

        for (int i = 0; i < sampleCount; i++)
        {
            phase += a_Voice.m_StartFreq / k_SampleRate;
            phase -= Mathf.Floor(phase);
            float sample = Sample(a_Voice.m_Wave, phase);
            if (a_Voice.m_NoiseMix > 0.0f)
            {
                sample = Mathf.Lerp(sample, Random.Range(-1.0f, 1.0f), a_Voice.m_NoiseMix);
            }
            data[i] = Crush(sample, a_Voice.m_BitCrush);
        }

        int fade = Mathf.Min(sampleCount / 8, 2048);
        for (int i = 0; i < fade; i++)
        {
            float blend = (float)i / fade;
            data[i] = Mathf.Lerp(data[sampleCount - fade + i], data[i], blend);
        }

        a_Voice.m_Clip = AudioClip.Create(a_Voice.m_Name, sampleCount, 1, k_SampleRate, false);
        a_Voice.m_Clip.SetData(data, 0);
    }

    static float Sample(Wave a_Wave, float a_Phase)
    {
        switch (a_Wave)
        {
            case Wave.Square:
                return a_Phase < 0.5f ? 1.0f : -1.0f;
            case Wave.Triangle:
                return 4.0f * Mathf.Abs(a_Phase - 0.5f) - 1.0f;
            case Wave.Saw:
                return 2.0f * a_Phase - 1.0f;
            case Wave.Sine:
                return Mathf.Sin(a_Phase * Mathf.PI * 2.0f);
            case Wave.Noise:
                return Random.Range(-1.0f, 1.0f);
        }
        return 0.0f;
    }

    static float Envelope(float a_T, float a_Attack, float a_Decay)
    {
        if (a_Attack > 0.0f && a_T < a_Attack)
        {
            return a_T / a_Attack;
        }
        return Mathf.Exp(-a_Decay * a_T);
    }

    static float Crush(float a_Sample, int a_Steps)
    {
        if (a_Steps <= 0)
        {
            return a_Sample;
        }
        return Mathf.Round(a_Sample * a_Steps) / a_Steps;
    }
}
