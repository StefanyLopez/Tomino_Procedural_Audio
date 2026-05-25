using System.Collections;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
// AudioManager v3 — controlador central de audio, maneja la reproducción de SFX y música.
//
// Implementa exactamente las especificaciones del diseño sonoro:
//   Move:      pitch drop 380→220Hz / 8ms, seno puro
//   Rotate:    doble transiente con sweep oposición ±160Hz
//   SoftDrop:  zumbido tonal continuo con LFO reactivo (llamar cada frame)
//   HardDrop:  pitch drop brutal 800→45Hz + ruido blanco + sub punch
//   LineClear: chime staccato con strum temporal, pitch sube por combo
//   Tetris:    sawtooth Maj7 + filter sweep + explosión ruido estéreo
// ═══════════════════════════════════════════════════════════════════════════════

namespace Tomino.Audio
{
    [RequireComponent(typeof(AudioSource))]
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager instance { get; private set; }

        [Header("Pool de SynthSFX (6 instancias)")]
        public SynthSFX[] sfxPool;

        [Header("Música de fondo")]
        public SynthAmbient synthAmbient;

        [Header("Volúmenes")]
        [Range(0f, 1f)] public float sfxVolume    = 1f;
        [Range(0f, 1f)] public float masterVolume = 1f;

        // ── Estado interno ────────────────────────────────────────────────────
        private int           _poolIdx      = 0;
        private System.Random _rng          = new System.Random();
        private int           _comboCount   = 0;   // sube con cada LineClear
        private float         _softDropTime = 0f;  // tiempo sostenido del soft drop
        private bool          _softDropActive = false;

        // AudioSource dedicado para HardDrop (necesita dos canales para ruido estéreo)
        private AudioSource _hardDropSource;

        // ── Buffers pre-generados para HardDrop ───────────────────────────────
        // Se generan en Awake una sola vez para máxima eficiencia
        private float[] _hardDropTonal;
        private float[] _hardDropNoise;
        private float[] _hardDropSub;
        private int     _hdPos = -1; // -1 = no activo

        // Buffers para ruido del Tetris
        private float[] _tetrisNoiseL;
        private float[] _tetrisNoiseR;
        private int     _tetrisPos = -1;

        // SoftDrop — síntesis continua en OnAudioFilterRead
        private bool   _sdActive   = false;
        private double _sdPhase1   = 0.0;
        private double _sdPhase2   = 0.0;
        private double _sdLFOPhase = 0.0;
        private float  _sdEnv      = 0f;
        private float  _sdHoldTime = 0f;

        private float _sr;

        // ─────────────────────────────────────────────────────────────────────

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;

            _sr = AudioSettings.outputSampleRate;

            var aud          = GetComponent<AudioSource>();
            aud.clip         = AudioClip.Create("tomino_sfx", (int)_sr, 2, (int)_sr, false);
            aud.loop         = true;
            aud.playOnAwake  = false;
            aud.spatialBlend = 0f;
            aud.volume       = 1f;
            aud.Play();

            PregenHardDrop();
            PregenTetrisNoise();
        }

        // ── Pool ──────────────────────────────────────────────────────────────

        private SynthSFX Next()
        {
            SynthSFX sfx = sfxPool[_poolIdx % sfxPool.Length];
            _poolIdx++;
            return sfx;
        }

        private float V(float vol) => vol * sfxVolume * masterVolume;

        // =========================================================================
        // PRE-GENERACIÓN DE BUFFERS COMPLEJOS
        // HardDrop y Tetris requieren síntesis más compleja que SynthSFX puede ofrecer.
        // Se pre-generan en Awake y se reproducen desde OnAudioFilterRead.
        // =========================================================================

        private void PregenHardDrop()
        {
            int lenTonal = (int)(_sr * 0.220f); // 220ms
            int lenNoise = (int)(_sr * 0.150f); // 150ms
            int lenSub   = (int)(_sr * 0.060f); // 60ms

            _hardDropTonal = new float[lenTonal];
            _hardDropNoise = new float[lenNoise];
            _hardDropSub   = new float[lenSub];

            // ── Componente tonal: seno + saw, pitch drop 800→45Hz exponencial ──
            double phase = 0.0;
            for (int i = 0; i < lenTonal; i++)
            {
                float t     = (float)i / _sr;
                float pitch = 45f + 755f * Mathf.Exp(-t / 0.018f);
                float amp   = Mathf.Exp(-t / 0.045f);

                // Mezcla seno (60%) + diente de sierra (40%)
                double sinVal = System.Math.Sin(phase);
                float  sawT   = _sr / pitch;
                float  sawVal = Mathf.Lerp(1f, -1f, (i % sawT) / sawT);

                _hardDropTonal[i] = ((float)sinVal * 0.60f + sawVal * 0.40f) * amp * 0.65f;

                phase += 2.0 * System.Math.PI * pitch / _sr;
                if (phase > 2.0 * System.Math.PI) phase -= 2.0 * System.Math.PI;
            }

            // ── Componente ruido: blanco filtrado, cutoff 3500→180Hz ───────────
            // Filtro paso-bajo biquad de primer orden (simple pero efectivo)
            float noiseState = 0f;
            for (int i = 0; i < lenNoise; i++)
            {
                float t       = (float)i / _sr;
                float cutoff  = 180f + 3320f * Mathf.Exp(-t / 0.015f);
                float amp     = Mathf.Exp(-t / 0.035f);

                // Ruido blanco
                float white   = (float)(_rng.NextDouble() * 2.0 - 1.0);

                // Filtro paso-bajo de primer orden: y[n] = a*x[n] + (1-a)*y[n-1]
                float rc = 1f / (2f * Mathf.PI * cutoff / _sr + 1f);
                noiseState = noiseState * rc + white * (1f - rc);

                _hardDropNoise[i] = noiseState * amp * 0.45f;
            }

            // ── Sub punch: seno 55Hz, solo 60ms ───────────────────────────────
            double subPhase = 0.0;
            for (int i = 0; i < lenSub; i++)
            {
                float t   = (float)i / _sr;
                float amp = Mathf.Exp(-t / 0.020f);
                _hardDropSub[i] = (float)System.Math.Sin(subPhase) * amp * 0.50f;
                subPhase += 2.0 * System.Math.PI * 55.0 / _sr;
            }
        }

        private void PregenTetrisNoise()
        {
            int len = (int)(_sr * 0.250f); // 250ms de ruido

            _tetrisNoiseL = new float[len];
            _tetrisNoiseR = new float[len];

            // Canal L: paso-banda 800-3000 Hz
            // Canal R: paso-banda 1200-4500 Hz
            // Implementado como HP + LP en cascada

            float stateHP_L = 0f, stateLP_L = 0f;
            float stateHP_R = 0f, stateLP_R = 0f;

            for (int i = 0; i < len; i++)
            {
                float t   = (float)i / _sr;
                float amp = Mathf.Exp(-t / 0.055f);

                float whiteL = (float)(_rng.NextDouble() * 2.0 - 1.0);
                float whiteR = (float)(_rng.NextDouble() * 2.0 - 1.0);

                // Canal L — BPF 800-3000 Hz
                float rcHP_L = 1f - 1f / (2f * Mathf.PI * 800f  / _sr + 1f);
                float rcLP_L =      1f / (2f * Mathf.PI * 3000f / _sr + 1f);
                stateHP_L = stateHP_L * rcHP_L + whiteL * (1f - rcHP_L);
                stateLP_L = stateLP_L * rcLP_L + stateHP_L * (1f - rcLP_L);
                _tetrisNoiseL[i] = stateLP_L * amp * 0.55f;

                // Canal R — BPF 1200-4500 Hz
                float rcHP_R = 1f - 1f / (2f * Mathf.PI * 1200f / _sr + 1f);
                float rcLP_R =      1f / (2f * Mathf.PI * 4500f / _sr + 1f);
                stateHP_R = stateHP_R * rcHP_R + whiteR * (1f - rcHP_R);
                stateLP_R = stateLP_R * rcLP_R + stateHP_R * (1f - rcLP_R);
                _tetrisNoiseR[i] = stateLP_R * amp * 0.55f;
            }
        }

        // =========================================================================
        // API PÚBLICA
        // =========================================================================

        /// <summary>
        /// Move/Shift — pitch drop 380→220Hz en 8ms.
        /// Seno puro, extremadamente corto y limpio.
        /// </summary>
        public void PlayPieceMoveClip()
        {
            if (!enabled) return;
            // Usamos SynthSFX con sweep para el pitch drop
            float f = 380f;
            Next().Play(f, SynthSFX.SynthType.Sine,
                atk: 0.001f, dec: 0.035f, sus: 0f, rel: 0.015f,
                vol: V(0.28f),
                sweep: true, sweepEnd: 220f, sweepDur: 0.008f);
        }

        /// <summary>
        /// Rotate — doble transiente con sweep de pitch opuesto.
        /// Sonido mecánico-digital de precisión.
        /// </summary>
        public void PlayPieceRotateClip()
        {
            if (!enabled) return;
            StartCoroutine(RotateSequence());
        }

        private IEnumerator RotateSequence()
        {
            // Transiente 1: sweep ascendente 520→680Hz
            Next().Play(520f, SynthSFX.SynthType.Sine,
                atk: 0.0005f, dec: 0.030f, sus: 0f, rel: 0.020f,
                vol: V(0.25f),
                sweep: true, sweepEnd: 680f, sweepDur: 0.012f);

            // 8ms después: transiente 2, sweep descendente 520→380Hz
            yield return new WaitForSeconds(0.008f);

            Next().Play(520f, SynthSFX.SynthType.Sine,
                atk: 0.0005f, dec: 0.025f, sus: 0f, rel: 0.015f,
                vol: V(0.20f),
                sweep: true, sweepEnd: 380f, sweepDur: 0.010f);
        }

        /// <summary>
        /// SoftDrop — activa/desactiva el zumbido continuo.
        /// Llamar StartSoftDrop() al presionar, StopSoftDrop() al soltar.
        /// </summary>
        public void StartSoftDrop()
        {
            _sdActive   = true;
            _sdHoldTime = 0f;
        }

        public void StopSoftDrop()
        {
            _sdActive = false;
        }

        /// <summary>
        /// HardDrop — pitch drop brutal + ruido + sub punch.
        /// Activa los buffers pre-generados.
        /// </summary>
        public void PlayPieceDropClip()
        {
            if (!enabled) return;
            _hdPos = 0;      // activar reproducción del buffer
        }

        /// <summary>
        /// LineClear — chime staccato con strum temporal.
        /// El pitch base sube un semitono por combo acumulado.
        /// </summary>
        public void PlayLineClearClip(int linesCleared = 1)
        {
            if (!enabled) return;
            StartCoroutine(LineClearChime(linesCleared, _comboCount));
            _comboCount++;
        }

        /// <summary>Resetear combo (llamar al inicio de cada partida).</summary>
        public void ResetCombo() => _comboCount = 0;

        private IEnumerator LineClearChime(int lines, int combo)
        {
            // Pitch base sube un semitono por combo: f = 523.25 * 2^(combo/12)
            float baseFreq = 523.25f * Mathf.Pow(2f, combo / 12f);

            // Frecuencias del acorde mayor: tónica, tercera, quinta, octava
            float f1 = baseFreq;
            float f2 = baseFreq * 1.2599f;  // tercera mayor
            float f3 = baseFreq * 1.4983f;  // quinta justa
            float f4 = baseFreq * 2.0000f;  // octava

            // Número de voces según líneas completadas
            // 1 línea: quinta abierta | 2: tríada | 3+: acorde completo
            float[][] voices = new float[][]
            {
                new[] { f1, f3 },        // 1 línea
                new[] { f1, f2, f3 },    // 2 líneas
                new[] { f1, f2, f3, f4 } // 3+ líneas
            };

            int voiceIdx = Mathf.Clamp(lines - 1, 0, voices.Length - 1);
            float[] chord = voices[voiceIdx];

            // Strum: cada voz entra 8ms después de la anterior
            for (int v = 0; v < chord.Length; v++)
            {
                SynthSFX sfx = Next();
                sfx.numberOfHarmonics  = 3;
                sfx.harmonicAmplitudes = new float[]
                    { 1f, 0.25f, 0.08f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };

                sfx.Play(chord[v], SynthSFX.SynthType.Additive,
                    atk: 0.003f, dec: 0.180f, sus: 0f, rel: 0.120f,
                    vol: V(0.38f - v * 0.02f));

                if (v < chord.Length - 1)
                    yield return new WaitForSeconds(0.008f);
            }
        }

        /// <summary>
        /// TETRIS — 4 líneas. Sawtooth Maj7 + filter sweep + explosión estéreo.
        /// El sonido más expansivo del juego.
        /// </summary>
        public void PlayTetrisClearClip()
        {
            if (!enabled) return;
            _comboCount++;
            StartCoroutine(TetrisExplosion());
        }

        private IEnumerator TetrisExplosion()
        {
            // Activar ruido estéreo pre-generado
            _tetrisPos = 0;

            // Acorde Cmaj7 con sawtooth: C5 E5 G5 B5
            // El filter sweep se implementa vía múltiples SynthSFX con diferentes decays
            // simulando que el filtro se abre (los agudos entran después)
            float[] notes  = { 523.25f, 659.25f, 783.99f, 987.77f };
            float[] delays = { 0f, 0.010f, 0.020f, 0.032f }; // entrada escalonada

            for (int v = 0; v < notes.Length; v++)
            {
                if (delays[v] > 0f)
                    yield return new WaitForSeconds(delays[v] - (v > 0 ? delays[v-1] : 0f));

                SynthSFX sfx = Next();
                sfx.numberOfHarmonics  = 6;
                // Armónicos de sawtooth: amplitudes decrecientes 1/n
                sfx.harmonicAmplitudes = new float[]
                    { 1f, 0.50f, 0.33f, 0.25f, 0.20f, 0.16f, 0f, 0f, 0f, 0f };

                // Sustain alto + release largo = el acorde sustenta y decae elegante
                sfx.Play(notes[v], SynthSFX.SynthType.Additive,
                    atk: 0.005f, dec: 0.400f, sus: 0.30f, rel: 0.500f,
                    vol: V(0.45f - v * 0.02f),
                    // LFO de pitch implícito: variación mínima usando sweep muy sutil
                    sweep: true, sweepEnd: notes[v] * 1.004f, sweepDur: 0.300f);
            }
        }

        // ── Métodos de compatibilidad con AudioPlayer ─────────────────────────

        public void PlayNewGameClip()
        {
            ResetCombo();
            if (!enabled) return;
            StartCoroutine(NewGameArpeggio());
        }

        private IEnumerator NewGameArpeggio()
        {
            float[] notes = { 261.63f, 329.63f, 392.00f, 523.25f };
            for (int i = 0; i < notes.Length; i++)
            {
                Next().Play(notes[i], SynthSFX.SynthType.FM,
                    atk: 0.004f, dec: 0.15f, sus: 0f, rel: 0.10f,
                    vol: V(0.38f + i * 0.02f),
                    fmFreq: notes[i] * 1.5f, fmIdx: 0.8f);
                yield return new WaitForSeconds(0.07f);
            }
        }

        public void PlayPauseClip()
        {
            if (!enabled) return;
            Next().Play(660f, SynthSFX.SynthType.Sine,
                atk: 0.008f, dec: 0.30f, sus: 0f, rel: 0.20f,
                vol: V(0.32f),
                sweep: true, sweepEnd: 330f, sweepDur: 0.28f);
        }

        public void PlayResumeClip()
        {
            if (!enabled) return;
            Next().Play(330f, SynthSFX.SynthType.Sine,
                atk: 0.008f, dec: 0.28f, sus: 0f, rel: 0.18f,
                vol: V(0.32f),
                sweep: true, sweepEnd: 660f, sweepDur: 0.28f);
        }

        public void PlayGameOverClip()
        {
            if (!enabled) return;
            ResetCombo();
            StartCoroutine(GameOverSequence());
        }

        private IEnumerator GameOverSequence()
        {
            float[] notes  = { 440f, 329.63f, 261.63f, 220f };
            float[] decays = { 0.35f, 0.35f, 0.45f, 0.70f };
            for (int i = 0; i < notes.Length; i++)
            {
                Next().Play(notes[i], SynthSFX.SynthType.FM,
                    atk: 0.008f, dec: decays[i], sus: 0f, rel: 0.28f,
                    vol: V(0.42f - i * 0.03f),
                    fmFreq: notes[i] * 0.5f, fmIdx: 1.2f);
                yield return new WaitForSeconds(0.20f);
            }
        }

        public void PlayToggleOnClip()  => PlayResumeClip();
        public void PlayToggleOffClip() => PlayPauseClip();

        // ── Música ────────────────────────────────────────────────────────────

        public void StartMusic()                => synthAmbient?.StartAmbient();
        public void StopMusic()                 => synthAmbient?.StopAmbient();
        public void SetMusicEnabled(bool on)
        {
            if (synthAmbient == null) return;
            if (on) synthAmbient.StartAmbient();
            else    synthAmbient.StopAmbient();
        }

        // ── Notificar nivel de peligro al ambient ─────────────────────────────

        public void SetDangerLevel(float danger)
        {
            if (synthAmbient != null) synthAmbient.SetDangerLevel(danger);
        }

        // =========================================================================
        // OnAudioFilterRead — SoftDrop continuo + HardDrop buffer + Tetris noise
        // =========================================================================

        void OnAudioFilterRead(float[] data, int channels)
        {
            float vol = sfxVolume * masterVolume;

            // Alphas del envelope del SoftDrop — calculados por buffer, no por sample
            // Evita Time.deltaTime que solo funciona en el hilo principal
            int samplesPerBuffer = data.Length / channels;
            float bufferDuration = samplesPerBuffer / (float)_sr;
            float sdAtkAlpha = 1f - Mathf.Exp(-bufferDuration * 12f); // ~83ms attack
            float sdRelAlpha = 1f - Mathf.Exp(-bufferDuration * 8f);  // ~125ms release

            for (int i = 0; i < data.Length; i += channels)
            {
                float sample = 0f;

                // SoftDrop
                if (_sdActive)
                {
                    _sdHoldTime += 1f / _sr;
                    _sdEnv      += (1f - _sdEnv) * sdAtkAlpha / samplesPerBuffer;

                    float hold  = Mathf.Clamp01(_sdHoldTime);
                    float freq1 = 95f;
                    float freq2 = 190f;
                    float lfoF  = 8f  + hold * 10f;
                    float depth = 0.5f;
                    float amp   = (0.18f + hold * 0.15f) * vol;

                    float s1  = (float)System.Math.Sin(_sdPhase1);
                    float s2  = (float)System.Math.Sin(_sdPhase2) * 0.4f;
                    float lfo = 1f - depth + depth * (0.5f + 0.5f * (float)System.Math.Sin(_sdLFOPhase));

                    sample += (s1 + s2) / 1.4f * lfo * amp * _sdEnv;

                    _sdPhase1   += 2.0 * System.Math.PI * freq1 / _sr;
                    _sdPhase2   += 2.0 * System.Math.PI * freq2 / _sr;
                    _sdLFOPhase += 2.0 * System.Math.PI * lfoF  / _sr;

                    if (_sdPhase1   > 2.0 * System.Math.PI) _sdPhase1   -= 2.0 * System.Math.PI;
                    if (_sdPhase2   > 2.0 * System.Math.PI) _sdPhase2   -= 2.0 * System.Math.PI;
                    if (_sdLFOPhase > 2.0 * System.Math.PI) _sdLFOPhase -= 2.0 * System.Math.PI;
                }
                else
                {
                    _sdEnv += (0f - _sdEnv) * sdRelAlpha / samplesPerBuffer;
                }


                // ── HardDrop — buffer pre-generado ────────────────────────────
                if (_hdPos >= 0)
                {
                    float hdSample = 0f;

                    if (_hdPos < _hardDropTonal.Length)
                        hdSample += _hardDropTonal[_hdPos];
                    if (_hdPos < _hardDropNoise.Length)
                        hdSample += _hardDropNoise[_hdPos];
                    if (_hdPos < _hardDropSub.Length)
                        hdSample += _hardDropSub[_hdPos];

                    sample += hdSample * vol;
                    _hdPos++;

                    // Desactivar cuando termina la componente más larga
                    if (_hdPos >= _hardDropTonal.Length) _hdPos = -1;
                }

                // ── Tetris noise — estéreo ────────────────────────────────────
                if (_tetrisPos >= 0 && _tetrisPos < _tetrisNoiseL.Length)
                {
                    if (channels == 2)
                    {
                        data[i]   += _tetrisNoiseL[_tetrisPos] * vol;
                        data[i+1] += _tetrisNoiseR[_tetrisPos] * vol;
                    }
                    else
                    {
                        float mono = (_tetrisNoiseL[_tetrisPos] + _tetrisNoiseR[_tetrisPos]) * 0.5f;
                        data[i] += mono * vol;
                    }
                    _tetrisPos++;
                    if (_tetrisPos >= _tetrisNoiseL.Length) _tetrisPos = -1;
                }

                // ── Mezcla del sample de este frame ───────────────────────────
                data[i] += sample;
                if (channels == 2) data[i+1] += sample;
            }
        }
    }
}