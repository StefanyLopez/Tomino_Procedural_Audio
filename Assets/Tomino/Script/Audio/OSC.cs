using UnityEngine;

public class OSC : MonoBehaviour
{
    public float FM;
    public float f = 440;

    public AudioSource Aud;
    public int waveType = 0;

    float X = 0f;
    public int TimeIndex = 0;

    [Header("Additive Synthesis")]
    public int numberOfHarmonics = 5;
    public float[] harmonicAmplitudes = new float[10];

    [Header("ADSR")]
    public float attack  = 0.1f;
    public float decay   = 0.25f;
    public float sustain = 0.0f;
    public float release = 0.2f;

    public float envelopeValue = 0f;
    public bool  isNoteOn      = false;
    public int   noteOnSample  = 0;
    public int   noteOffSample = 0;

    [Header("Wavetable")]
    public bool useWavetable = false;
    private float[] wavetable;
    private const int wavetableSize = 2048;

    [Header("FM Synthesis")]
    public float fmModFrequency = 220f;
    public float fmModIndex     = 1f;

    void Awake()
    {
        if (Aud == null)
            Aud = GetComponent<AudioSource>();
    }

    void Start()
    {
        Debug.Log("Sample rate: " + AudioSettings.outputSampleRate);

        FM = AudioSettings.outputSampleRate;
        for (int i = 0; i < harmonicAmplitudes.Length; i++)
            harmonicAmplitudes[i] = 1.0f;

        Aud.loop        = true;
        Aud.playOnAwake = false;
        Aud.volume      = 1f;

        int bufferSize = (int)FM;
        Aud.clip = AudioClip.Create("osc", bufferSize, 1, (int)FM, false);

        GenerateWavetable();
    }

    // ── Wavetable ─────────────────────────────────────────────────────────────

    public void GenerateWavetable()
    {
        wavetable = new float[wavetableSize];
        for (int i = 0; i < wavetableSize; i++)
        {
            float fNorm = FM / wavetableSize;
            switch (waveType)
            {
                case 0: wavetable[i] = SineWave(fNorm, i);          break;
                case 1: wavetable[i] = SquareWave(fNorm, i);        break;
                case 2: wavetable[i] = TriangleWave(fNorm, i);      break;
                case 3: wavetable[i] = SawWave(fNorm, i);           break;
                case 4: wavetable[i] = AdditiveSynthesis(fNorm, i); break;
                case 5: wavetable[i] = FMWave(fNorm, i); break;
            }
        }
    }

    private float GetWavetableSample(int index, float frequency)
    {
        int wavetableIndex = Mathf.RoundToInt((index * frequency / FM) * wavetableSize) % wavetableSize;
        return wavetable[wavetableIndex];
    }

    public void ToggleWavetable(bool isOn)
    {
        useWavetable = isOn;
        GenerateWavetable();
    }

    // ── Formas de onda ────────────────────────────────────────────────────────

    public float SineWave(float freq, int t)
        => Mathf.Sin(2 * Mathf.PI * freq * t / FM);

    public float SquareWave(float freq, int t)
        => Mathf.Sign(Mathf.Sin(2 * Mathf.PI * freq * t / FM));

    public float TriangleWave(float freq, int t)
    {
        float T  = FM / freq;
        float s  = t % T;
        float t2 = T / 4f, t3 = 3f * T / 4f;
        if      (s < t2) return Mathf.Lerp( 0f,  1f, s / t2);
        else if (s < t3) return Mathf.Lerp( 1f, -1f, (s - t2) / (t3 - t2));
        else             return Mathf.Lerp(-1f,  0f, (s - t3) / (T  - t3));
    }

    public float SawWave(float freq, int t)
    {
        float T = FM / freq;
        return Mathf.Lerp(1f, -1f, (t % T) / T);
    }

    public float AdditiveSynthesis(float baseFreq, int t)
    {
        float sample = 0f, totalAmplitude = 0f;
        for (int i = 0; i < numberOfHarmonics; i++)
        {
            float hFreq = baseFreq * (i + 1);
            if (hFreq >= FM * 0.5f) break;
            sample         += harmonicAmplitudes[i] * Mathf.Sin(2 * Mathf.PI * hFreq * t / FM);
            totalAmplitude += harmonicAmplitudes[i];
        }
        if (totalAmplitude > 0f) sample /= totalAmplitude;
        return sample;
    }

    public float FMWave(float carrierFrequency, int t)
    {
        if (carrierFrequency <= 0f) return 0f;
        float carrierPhase   = 2f * Mathf.PI * carrierFrequency * t / FM;
        float modulatorPhase = 2f * Mathf.PI * fmModFrequency   * t / FM;
        return Mathf.Sin(carrierPhase + fmModIndex * Mathf.Sin(modulatorPhase));
    }

    // ── ADSR ─────────────────────────────────────────────────────────────────

    float ADSR()
    {
        float attackSamples  = attack  * FM;
        float decaySamples   = decay   * FM;
        float releaseSamples = release * FM;
        float envelope       = 0f;

        if (isNoteOn)
        {
            int elapsed = TimeIndex - noteOnSample;
            if (elapsed < attackSamples)
                envelope = elapsed / attackSamples;
            else if (elapsed < attackSamples + decaySamples)
                envelope = Mathf.Lerp(1f, sustain, (elapsed - attackSamples) / decaySamples);
            else
                envelope = sustain;
        }
        else
        {
            int elapsed = TimeIndex - noteOffSample;
            envelope = elapsed < releaseSamples
                     ? Mathf.Lerp(envelopeValue, 0f, elapsed / releaseSamples)
                     : 0f;
        }

        envelopeValue = envelope;
        return envelope;
    }

    // ── Audio loop ────────────────────────────────────────────────────────────
    // IMPORTANTE: Sin Debug.Log aquí — se llamaría miles de veces por segundo

    void OnAudioFilterRead(float[] data, int channels)
    {
        for (int i = 0; i < data.Length; i += channels)
        {
            if (useWavetable)
                X = GetWavetableSample(TimeIndex, f);
            else
            {
                switch (waveType)
                {
                    case 0: X = SineWave         (f, TimeIndex); break;
                    case 1: X = SquareWave       (f, TimeIndex); break;
                    case 2: X = TriangleWave     (f, TimeIndex); break;
                    case 3: X = SawWave          (f, TimeIndex); break;
                    case 4: X = AdditiveSynthesis(f, TimeIndex); break;
                    case 5: X = FMWave(f, TimeIndex); break;          
                }
            }

            float sample = X * ADSR() * 0.3f;
            data[i] = sample;
            if (channels == 2) data[i + 1] = sample;
            TimeIndex++;
        }
    }
}