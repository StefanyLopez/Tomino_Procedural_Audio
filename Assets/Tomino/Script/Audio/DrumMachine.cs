using System.Collections;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
// DrumMachine — Batería procedimental por síntesis pura.
//
// CORRECCIÓN: usa System.Random en lugar de UnityEngine.Random.value,
// ya que OnAudioFilterRead corre en el hilo de audio (no el hilo principal).
// UnityEngine.Random solo puede llamarse desde el main thread.
// ═══════════════════════════════════════════════════════════════════════════════

[RequireComponent(typeof(AudioSource))]
public class DrumMachine : MonoBehaviour
{
    [Range(0f, 1f)] public float masterVolume = 0.15f;

    [Header("Kick")]
    public float kickStartFreq  = 80f;
    public float kickEndFreq    = 40f;
    public float kickDecay      = 0.2f;
    public float kickPitchDecay = 0.08f;

    [Header("Snare")]
    public float snareDecay    = 0.18f;
    public float snareToneFreq = 200f;
    public float snareToneMix  = 0.3f;

    [Header("HiHat")]
    public float hihatDecay  = 0.05f;
    public float hihatCutoff = 0.6f;

    // ── Estado interno ────────────────────────────────────────────────────────

    private float sampleRate;

    private float[] kickBuffer;
    private float[] snareBuffer;
    private float[] hihatBuffer;
    private int kickPos, snarePos, hihatPos;

    // Thread-safe: System.Random funciona desde cualquier hilo
    private System.Random rng = new System.Random();

    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        sampleRate = AudioSettings.outputSampleRate;

        var aud = GetComponent<AudioSource>();
        aud.clip = AudioClip.Create("drum_silence", (int)sampleRate, 1, (int)sampleRate, false);
        aud.loop        = true;
        aud.playOnAwake = false;
        aud.volume      = 1f;
        aud.Play();
    }

    // ── API pública ───────────────────────────────────────────────────────────

    public void Kick()
    {
        int len = Mathf.RoundToInt(kickDecay * sampleRate);
        kickBuffer = GenerateKick(len);
        kickPos    = 0;
    }

    public void Snare()
    {
        int len = Mathf.RoundToInt(snareDecay * sampleRate);
        snareBuffer = GenerateSnare(len);
        snarePos    = 0;
    }

    public void HiHat()
    {
        int len = Mathf.RoundToInt(hihatDecay * sampleRate);
        hihatBuffer = GenerateHiHat(len);
        hihatPos    = 0;
    }

    // ── Generadores (se llaman desde el main thread, antes de OnAudioFilterRead)

    private float[] GenerateKick(int len)
    {
        float[] buf = new float[len];
        for (int i = 0; i < len; i++)
        {
            float t   = (float)i / sampleRate;
            float env = Mathf.Exp(-t / kickDecay);
            float freq = kickEndFreq + (kickStartFreq - kickEndFreq) * Mathf.Exp(-t / kickPitchDecay);
            float sample = Mathf.Sin(2f * Mathf.PI * freq * t) * env;

            // Transiente de click (~1ms) para dar punch — usa System.Random
            if (i < sampleRate * 0.001f)
                sample += (float)(rng.NextDouble() * 2.0 - 1.0) * 0.5f * env;

            buf[i] = sample;
        }
        return buf;
    }

    private float[] GenerateSnare(int len)
    {
        float[] buf = new float[len];
        for (int i = 0; i < len; i++)
        {
            float t    = (float)i / sampleRate;
            float env  = Mathf.Exp(-t * (1f / snareDecay));
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float tone  = Mathf.Sin(2f * Mathf.PI * snareToneFreq * t);
            buf[i] = env * Mathf.Lerp(noise, tone, snareToneMix);
        }
        return buf;
    }

    private float[] GenerateHiHat(int len)
    {
        float[] buf  = new float[len];
        float   prev = 0f;
        float   alpha = 1f - hihatCutoff;

        for (int i = 0; i < len; i++)
        {
            float t     = (float)i / sampleRate;
            float env   = Mathf.Exp(-t * (1f / hihatDecay));
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float hp    = noise - prev + alpha * prev;
            prev = noise;
            buf[i] = env * hp;
        }
        return buf;
    }

    // ── Mezcla en tiempo real (hilo de audio) ─────────────────────────────────
    // NO llamar a Random.value ni a ninguna API de Unity aquí

    void OnAudioFilterRead(float[] data, int channels)
    {
        for (int i = 0; i < data.Length; i += channels)
        {
            float sample = 0f;

            if (kickBuffer  != null && kickPos  < kickBuffer.Length)  sample += kickBuffer[kickPos++]   * 0.9f;
            if (snareBuffer != null && snarePos < snareBuffer.Length) sample += snareBuffer[snarePos++] * 0.7f;
            if (hihatBuffer != null && hihatPos < hihatBuffer.Length) sample += hihatBuffer[hihatPos++] * 0.5f;

            sample *= masterVolume;
            data[i] = sample;
            if (channels == 2) data[i + 1] = sample;
        }
    }

    // ── Patrones predefinidos ─────────────────────────────────────────────────

    /// <summary>Patrón 4/4: Kick en 1 y 3, Snare en 2 y 4, HiHat en todos.</summary>
    public IEnumerator PlayPattern_Basic(float bpm, int bars = 1)
    {
        float beat = 60f / bpm;
        for (int b = 0; b < bars * 4; b++)
        {
            HiHat();
            if (b % 4 == 0 || b % 4 == 2) Kick();
            if (b % 4 == 1 || b % 4 == 3) Snare();
            yield return new WaitForSeconds(beat);
        }
    }

    /// <summary>Patrón vals 3/4: Kick en el 1, HiHat en 2 y 3.</summary>
    public IEnumerator PlayPattern_Waltz(float bpm, int bars = 1)
    {
        float beat = 60f / bpm;
        for (int b = 0; b < bars * 3; b++)
        {
            if (b % 3 == 0) Kick();
            else            HiHat();
            yield return new WaitForSeconds(beat);
        }
    }

    /// <summary>Patrón shuffle (swing): HiHat en corcheas de trío.</summary>
    public IEnumerator PlayPattern_Shuffle(float bpm, int bars = 1)
    {
        float beat    = 60f / bpm;
        float triplet = beat / 3f;

        for (int b = 0; b < bars * 4; b++)
        {
            HiHat();
            if (b % 4 == 0 || b % 4 == 2) Kick();
            if (b % 4 == 1 || b % 4 == 3) Snare();
            yield return new WaitForSeconds(triplet * 2f);
            HiHat();
            yield return new WaitForSeconds(triplet);
        }
    }
}