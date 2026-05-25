using System.Collections;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
// SynthAmbient v6 — Korobeiniki reinterpretado, estilo Tetris clásico.
//
// ESTRUCTURA:
//   1. MELODÍA PRINCIPAL — línea monofónica de Korobeiniki, una nota a la vez,
//      articulada y clara. Usa SynthSFX con timbre de "piano de juguete"
//      (aditiva con armónico 2 prominente). Cada nota tiene decay corto
//      para que se escuche el ritmo, no un pad difuso.
//
//   2. ACOMPAÑAMIENTO — acordes en tiempos 1 y 3, SynthSFX con timbre suave.
//      Boom-chak que le da el feel de Tetris sin batería.
//
//   3. BAJO — seno puro, fundamental del acorde, pulso suave.
//
//   4. DRONE PAD — base armónica muy quieta en el fondo, apenas audible.
//      Solo para dar cuerpo, no para competir con la melodía.
//
// MELODÍA:
//   Korobeiniki en La menor (tonalidad original del Game Boy).
//   Las frecuencias están hardcodeadas nota por nota.
//   Dos secciones (A y B) que se alternan en loop.
//   A 160 BPM (tempo clásico del Tetris).
//
// REACTIVIDAD AL PELIGRO:
//   - cutoff del drone sube (más brillo, más tensión)
//   - melodía sube de volumen
//   - acompañamiento acelera levemente (BPM sube hasta 185)
// ═══════════════════════════════════════════════════════════════════════════════

namespace Tomino.Audio
{
    [RequireComponent(typeof(AudioSource))]
    public class SynthAmbient : MonoBehaviour
    {
        [Header("Pool SynthSFX — melodía (4) + acordes (4)")]
        public SynthSFX[] melPool;    // 4 instancias para melodía
        public SynthSFX[] chordPool;  // 4 instancias para acordes

        [Header("Tempo base (BPM)")]
        [Range(140f, 180f)] public float bpm = 160f;

        [Header("Volúmenes")]
        [Range(0f, 1f)] public float masterVolume = 0.55f;
        [Range(0f, 1f)] public float melVolume    = 0.52f;
        [Range(0f, 1f)] public float chordVolume  = 0.22f;
        [Range(0f, 1f)] public float bassVolume   = 0.20f;
        [Range(0f, 1f)] public float droneVolume  = 0.06f;

        [Header("Glide del drone")]
        [Range(0.5f, 4f)] public float glideTime = 2.0f;

        [Header("Capas")]
        public bool playMelody = true;
        public bool playChords = true;
        public bool playBass   = true;
        public bool playDrone  = true;

        // ── Peligro ───────────────────────────────────────────────────────────
        private float _danger       = 0f;
        private float _dangerSmooth = 0f;

        // =====================================================================
        // KOROBEINIKI — frecuencias de cada nota (La menor, tempo = 160 BPM)
        //
        // Duración en corcheas (1 corchea = 1 beat / 2):
        //   2 = negra, 1 = corchea, 3 = negra con puntillo, 4 = blanca
        //
        // Sección A (frase principal):
        //   E5 D5 | C5 D5 E5 | A4 - | A4 - |
        //   D5 - F5 | A5 - | G5 F5 | E5 - |
        //   C5 - E5 | G5 - | F5 E5 | D5 - |
        //   F5 A5 - | G5 - | E5 - | A4 - |
        //
        // Sección B (frase secundaria):
        //   E5 - C5 | A4 - | D5 - F5 | B4(#) - |
        //   E5 - C5 | A4 - | D5 B4 G4 | A4 - |
        // =====================================================================

        // Nota: frecuencias en Hz, duración en número de corcheas
        private struct Note { public float freq; public float dur; }

        // Silencio = freq 0
        private static Note N(float f, float d) => new Note { freq = f, dur = d };

        // Frecuencias estándar
        private const float
            G4  = 392.00f, A4  = 440.00f, B4  = 493.88f,
            C5  = 523.25f, D5  = 587.33f, E5  = 659.25f,
            F5  = 698.46f, G5  = 783.99f, A5  = 880.00f,
            Bb4 = 466.16f, Fs4 = 369.99f; // Fa#4 para sección B

        // Sección A — frase principal de Korobeiniki
        private readonly Note[] _secA = new Note[]
        {
            N(E5,2), N(D5,2),
            N(C5,2), N(D5,2), N(E5,2),
            N(A4,4),
            N(A4,4),
            N(D5,2), N(F5,2),
            N(A5,4),
            N(G5,2), N(F5,2),
            N(E5,4),
            N(C5,2), N(E5,2),
            N(G5,4),
            N(F5,2), N(E5,2),
            N(D5,4),
            N(F5,2), N(A5,4),
            N(G5,4),
            N(E5,4),
            N(A4,4),
        };

        // Sección B — frase secundaria
        private readonly Note[] _secB = new Note[]
        {
            N(E5,2), N(C5,2),
            N(A4,4),
            N(D5,2), N(F5,2),
            N(B4,4),
            N(E5,2), N(C5,2),
            N(A4,4),
            N(D5,2), N(B4,2), N(G4,2),
            N(A4,6),
        };

        // Progresión de acordes que acompaña cada sección
        // Acorde = array de frecuencias tocadas juntas (2-3 voces)
        // La menor clásica: Am - G - C - E
        private readonly float[][] _chordsA = new float[][]
        {
            new[] { 220.00f, 261.63f, 329.63f }, // Am
            new[] { 196.00f, 246.94f, 293.66f }, // G
            new[] { 261.63f, 329.63f, 392.00f }, // C
            new[] { 164.81f, 207.65f, 246.94f }, // E
        };
        private int _chordIdx = 0;

        // ── Drone — 4 phase accumulators ─────────────────────────────────────
        private const int ND = 4;
        private double[] _dPhase   = new double[ND];
        private double[] _dFreqCur = new double[ND];
        private double[] _dFreqTgt = new double[ND];

        // Voicing del drone: Am en registro medio-bajo
        private readonly float[] _droneBase = { 110.00f, 130.81f, 164.81f, 220.00f };

        // Harmónicos cálidos
        private readonly float[] _dHArms  = { 1f, 0.35f, 0.12f };

        // ── Filtro biquad (drone) ─────────────────────────────────────────────
        private float _bfx1, _bfx2, _bfy1, _bfy2;
        private float _cutoffCur = 400f;

        // ── LFO ──────────────────────────────────────────────────────────────
        private double _lfoPhase = 0.0;

        // ── Bajo ──────────────────────────────────────────────────────────────
        private double _bPhase   = 0.0;
        private double _bFreqCur = 110.0;
        private double _bFreqTgt = 110.0;
        private float  _bEnv     = 0f;

        // ── Volatile ──────────────────────────────────────────────────────────
        private volatile bool  _bassOn    = false;
        private volatile float _bassFreq  = 110f;

        // ── Estado general ────────────────────────────────────────────────────
        private float _sr;
        private bool  _running      = false;
        private float _globalAmp    = 0f;
        private float _globalAmpTgt = 0f;

        private int _melPoolIdx   = 0;
        private int _chordPoolIdx = 0;

        // ─────────────────────────────────────────────────────────────────────

        void Awake()
        {
            _sr = AudioSettings.outputSampleRate;

            for (int i = 0; i < ND; i++)
            {
                _dFreqCur[i] = _droneBase[i];
                _dFreqTgt[i] = _droneBase[i];
            }

            var aud          = GetComponent<AudioSource>();
            aud.clip         = AudioClip.Create("tomino_tetris", (int)_sr, 1, (int)_sr, false);
            aud.loop         = true;
            aud.playOnAwake  = false;
            aud.spatialBlend = 0f;
            aud.volume       = 1f;
            aud.Play();
        }

        void Start() => StartAmbient();

        // ── API pública ───────────────────────────────────────────────────────

        public void StartAmbient()
        {
            if (_running) return;
            _running      = true;
            _globalAmpTgt = 1f;
            if (playMelody) StartCoroutine(MelodyLoop());
            if (playBass)   StartCoroutine(BassLoop());
        }

        public void StopAmbient()
        {
            _running      = false;
            _globalAmpTgt = 0f;
            _bassOn       = false;
            StopAllCoroutines();
            if (melPool   != null) foreach (var s in melPool)   if (s) s.isActive = false;
            if (chordPool != null) foreach (var s in chordPool) if (s) s.isActive = false;
        }

        public void SetDangerLevel(float danger) => _danger = Mathf.Clamp01(danger);

        void OnDisable() => StopAmbient();

        // =====================================================================
        // MELODY LOOP — toca Korobeiniki sección A + B en loop
        // =====================================================================

        private IEnumerator MelodyLoop()
        {
            while (_running)
            {
                // Sección A x2
                yield return StartCoroutine(PlaySection(_secA));
                yield return StartCoroutine(PlaySection(_secA));
                // Sección B x1
                yield return StartCoroutine(PlaySection(_secB));
            }
        }

        private IEnumerator PlaySection(Note[] section)
        {
            for (int n = 0; n < section.Length; n++)
            {
                if (!_running) yield break;

                Note note = section[n];

                // BPM reactivo al peligro: sube hasta 185
                float curBpm    = bpm + _dangerSmooth * 25f;
                float corchea   = 60f / curBpm / 2f;   // duración de 1 corchea en segundos
                float noteDur   = note.dur * corchea;

                if (note.freq > 0f && playMelody && melPool != null && melPool.Length > 0)
                {
                    SynthSFX sfx = melPool[_melPoolIdx % melPool.Length];
                    _melPoolIdx++;

                    if (sfx != null)
                    {
                        // Timbre clásico de Tetris: aditiva con 2do armónico prominente
                        // Suena a "piano de chip" o "campanita de 8-bit suavizada"
                        sfx.numberOfHarmonics  = 4;
                        sfx.harmonicAmplitudes = new float[]
                            { 1f, 0.60f, 0.20f, 0.08f, 0f, 0f, 0f, 0f, 0f, 0f };

                        // Decay = 80% de la duración de la nota para articulación clara
                        // Release corto = se escucha el espacio entre notas (no es un pad)
                        float decTime = noteDur * 0.75f;
                        float relTime = 0.04f;

                        sfx.Play(note.freq, SynthSFX.SynthType.Additive,
                            atk: 0.005f,
                            dec: decTime,
                            sus: 0f,
                            rel: relTime,
                            vol: melVolume * (1f + _dangerSmooth * 0.25f));
                    }
                }

                // Disparar acorde en el primer tiempo de cada grupo de 4 corcheas
                if (playChords && n % 4 == 0)
                    StartCoroutine(PlayChord(curBpm));

                yield return new WaitForSeconds(noteDur);
            }
        }

        // ── Acompañamiento: acorde suave en tiempo 1 y 3 ─────────────────────

        private IEnumerator PlayChord(float curBpm)
        {
            float[] voices = _chordsA[_chordIdx % _chordsA.Length];
            _chordIdx++;

            if (chordPool == null || chordPool.Length == 0) yield break;

            for (int v = 0; v < voices.Length; v++)
            {
                SynthSFX sfx = chordPool[_chordPoolIdx % chordPool.Length];
                _chordPoolIdx++;
                if (sfx == null) continue;

                sfx.numberOfHarmonics  = 3;
                sfx.harmonicAmplitudes = new float[]
                    { 1f, 0.40f, 0.10f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };

                sfx.Play(voices[v], SynthSFX.SynthType.Additive,
                    atk: 0.015f,
                    dec: 0.60f,
                    sus: 0.10f,
                    rel: 0.30f,
                    vol: chordVolume * (0.85f + v * 0.05f));

                // Strum: 6ms entre voces
                if (v < voices.Length - 1)
                    yield return new WaitForSeconds(0.006f);
            }

            // Bajo junto al acorde
            _bassOn   = true;
            _bassFreq = voices[0] * 0.5f;   // fundamental una octava abajo
            _bFreqTgt = _bassFreq;
            yield return new WaitForSeconds(60f / curBpm * 0.25f);
            _bassOn = false;
        }

        // ── Bass Loop ─────────────────────────────────────────────────────────

        private IEnumerator BassLoop()
        {
            // El bajo lo dispara PlayChord, este coroutine solo mantiene el loop vivo
            while (_running) yield return null;
        }

        // =====================================================================
        // OnAudioFilterRead — Drone pad + Bajo
        // (Melodía y acordes van por SynthSFX, no aquí)
        // =====================================================================

        void OnAudioFilterRead(float[] data, int channels)
        {
            const double TWO_PI = 2.0 * System.Math.PI;

            float dSmA  = 1f - Mathf.Exp(-3f   / (float)_sr);
            float gA    = 1f - Mathf.Exp(-1.2f / (float)_sr);
            float bAtkA = 1f - Mathf.Exp(-1f   / (0.008f * (float)_sr));
            float bRelA = 1f - Mathf.Exp(-1f   / (0.35f  * (float)_sr));
            float cutA  = 1f - Mathf.Exp(-0.5f / (float)_sr);

            double dAlpha = 1.0 - System.Math.Pow(0.001, 1.0 / (_sr * glideTime));
            double bAlpha = 1.0 - System.Math.Pow(0.001, 1.0 / (_sr * glideTime * 0.3));

            bool  bOn   = _bassOn;
            float bFreq = _bassFreq;
            _bFreqTgt   = bFreq;

            for (int i = 0; i < data.Length; i += channels)
            {
                _dangerSmooth += (_danger - _dangerSmooth) * dSmA;
                float d = _dangerSmooth;

                _globalAmp += (_globalAmpTgt - _globalAmp) * gA;

                // Filtro del drone: cerrado en calma, abre con peligro
                float cutoffTgt = 380f + d * 1600f;
                _cutoffCur += (cutoffTgt - _cutoffCur) * cutA;

                float Q = 0.9f + d * 1.5f;

                double w0    = TWO_PI * _cutoffCur / _sr;
                double cosw0 = System.Math.Cos(w0);
                double sinw0 = System.Math.Sin(w0);
                double alpha = sinw0 / (2.0 * Q);
                double b0 = (1.0 - cosw0) / 2.0;
                double b1 =  1.0 - cosw0;
                double b2 = (1.0 - cosw0) / 2.0;
                double a0 =  1.0 + alpha;
                double a1 = -2.0 * cosw0;
                double a2 =  1.0 - alpha;

                // ── DRONE ─────────────────────────────────────────────────────
                float droneMix = 0f;
                if (playDrone)
                {
                    for (int v = 0; v < ND; v++)
                    {
                        _dFreqCur[v] += dAlpha * (_dFreqTgt[v] - _dFreqCur[v]);
                        _dPhase[v]   += TWO_PI * _dFreqCur[v] / _sr;
                        if (_dPhase[v] > TWO_PI) _dPhase[v] -= TWO_PI;

                        float s = 0f;
                        for (int h = 0; h < _dHArms.Length; h++)
                            s += _dHArms[h] * (float)System.Math.Sin(_dPhase[v] * (h + 1));
                        droneMix += s / _dHArms.Length;
                    }
                    droneMix /= ND;

                    float filtered = (float)(
                        (b0/a0) * droneMix + (b1/a0) * _bfx1 + (b2/a0) * _bfx2
                        - (a1/a0) * _bfy1  - (a2/a0) * _bfy2);
                    _bfx2 = _bfx1; _bfx1 = droneMix;
                    _bfy2 = _bfy1; _bfy1 = filtered;

                    _lfoPhase += TWO_PI * 0.06 / _sr;
                    if (_lfoPhase > TWO_PI) _lfoPhase -= TWO_PI;
                    float lfoG = 0.88f + 0.12f * (float)System.Math.Sin(_lfoPhase);

                    droneMix = filtered * lfoG * droneVolume;
                }

                // ── BAJO ──────────────────────────────────────────────────────
                float bassMix = 0f;
                if (playBass)
                {
                    _bFreqCur += bAlpha * (_bFreqTgt - _bFreqCur);
                    _bPhase   += TWO_PI * _bFreqCur / _sr;
                    if (_bPhase > TWO_PI) _bPhase -= TWO_PI;

                    _bEnv += ((bOn ? 1f : 0f) - _bEnv) * (bOn ? bAtkA : bRelA);
                    bassMix = (float)System.Math.Sin(_bPhase) * _bEnv * bassVolume;
                }

                // ── Mezcla ────────────────────────────────────────────────────
                float outS = (data[i] + droneMix + bassMix) * _globalAmp * masterVolume;
                outS = outS / (1f + Mathf.Abs(outS) * 0.30f);

                data[i] = outS;
                if (channels == 2) data[i + 1] = outS;
            }
        }
    }
}