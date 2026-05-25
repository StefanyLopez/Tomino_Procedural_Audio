# Implementación de un Sistema Sonoro Procedural en Tomino
**Actividad de Implementación de Audio Procedural en Videojuego Puzzle — Tercer Corte**

| | |
|---|---|
| **Integrantes** | Juan José Camacho | Stefany López
| **Curso** | Audio Procedural |
| **Corte** | Tercer corte |

---

## a) Descripción General de la Propuesta Sonora

El sistema reemplaza completamente el audio pregrabado de Tomino por síntesis digital en tiempo real. La identidad sonora es **retro-clásica con reactividad al estado del juego**: la música de fondo reproduce la melodía de Korobeiniki (tema original del Tetris de Game Boy) sintetizada proceduralmente, mientras los efectos responden a cada evento del gameplay con síntesis específica para cada tipo de acción.

La propuesta se divide en dos subsistemas independientes:

- **SFX de interfaz e in-game** — gestionados por `AudioManager.cs` mediante un pool de `SynthSFX`. Sonidos cortos, articulados y diferenciados por timbre y técnica de síntesis.
- **Música de fondo** — gestionada por `SynthAmbient.cs`. Melodía monofónica de Korobeiniki con acompañamiento de acordes, bajo y drone pad, todo generado sample a sample en `OnAudioFilterRead`.

El enfoque estético prioriza la coherencia con el referente visual de Tomino (puzzle de bloques, estética limpia) y la legibilidad funcional: cada acción del jugador tiene un sonido reconocible que comunica el resultado sin necesidad de información visual adicional.

---

## b) Proyecto Base — Tomino

**Asset:** Tomino v2.0.0 — Unity Asset Store  
**URL:** https://assetstore.unity.com/packages/templates/packs/tomino-159004  
**Versión Unity:** 2022.3.16 LTS  
**Plataforma objetivo:** Windows PC (.exe)

El proyecto se importó directamente desde la Asset Store. El sistema de audio original (`AudioPlayer.cs`) fue deshabilitado y sus referencias reemplazadas por llamadas al nuevo `AudioManager`. No se eliminaron los scripts originales para preservar la integridad del proyecto base; se desactivaron sus componentes en la jerarquía de la escena.

**Verificación previa:** se confirmó que el juego ejecutaba correctamente (generación de piezas, movimiento, rotación, caída, eliminación de líneas, game over) antes de intervenir el sistema de audio.

---

## c) Arquitectura de Implementación

### Scripts creados o modificados

| Script | Estado | Responsabilidad |
|---|---|---|
| `AudioManager.cs` | Nuevo | Controlador central. Singleton. Pool round-robin de `SynthSFX`. Gestiona todos los SFX de UI e in-game. Contiene `OnAudioFilterRead` propio para HardDrop (buffer pre-generado) y SoftDrop (síntesis continua). |
| `SynthAmbient.cs` | Nuevo | Música de fondo. Melodía de Korobeiniki hardcodeada nota a nota. Acompañamiento de acordes (Am–G–C–E). Drone pad con filtro biquad reactivo al `dangerLevel`. Bajo en seno puro. |
| `SynthSFX.cs` | Reutilizado de Nodulus | Sintetizador one-shot. Soporta: Sine, Square, Saw, Additive, FM, Wavetable. ADSR completo. Doble buffer volátil para comunicación segura entre hilo principal y hilo de audio. |
| `AudioPlayer.cs` | Deshabilitado | Sistema original. Se desactivó el componente en el Inspector sin eliminar el script. |

### Jerarquía de GameObjects en Unity

```
GameAudio  (Tag: GameAudio)
├── AudioManager.cs + AudioSource
├── SynthAmbient.cs + AudioSource
│
├── SFX_0 … SFX_5     → SynthSFX + AudioSource  (pool de efectos)
│
├── Mel_0 … Mel_3     → SynthSFX + AudioSource  (pool de melodía)
└── Chord_0 … Chord_3 → SynthSFX + AudioSource  (pool de acordes)
```

### Flujo de disparo de un sonido

1. El juego detecta un evento (ej: pieza rotada)
2. Llama `AudioManager.instance.PlayPieceRotateClip()`
3. `AudioManager` selecciona el siguiente `SynthSFX` disponible del pool (round-robin)
4. Escribe los parámetros en el buffer volátil del `SynthSFX`
5. Activa `_pendingReset = true`
6. `OnAudioFilterRead()` del `SynthSFX` detecta el flag, reinicia el estado y sintetiza las muestras

---

## d) Tabla de Sonidos Implementados

| Evento | Técnica | Parámetros principales | Objetivo perceptual | Justificación |
|---|---|---|---|---|
| Mover pieza (←/→) | Seno + sweep descendente | 380→220 Hz, A:0.001s D:0.035s R:0.015s, vol 0.28 | Feedback breve de desplazamiento | Pitch drop corto da sensación de deslizamiento sin distraer |
| Rotar pieza | Seno doble transiente con sweep opuesto | Transiente 1: 520→680 Hz / Transiente 2: 520→380 Hz, separados 8ms, vol 0.25/0.20 | Comunicar cambio de orientación | Dos sweeps en direcciones opuestas imitan el giro físico |
| Soft drop (↓ sostenido) | Seno continuo + LFO | 95 Hz + octava 190 Hz, LFO 8–18 Hz, amplitud reactiva al tiempo sostenido | Zumbido de descenso controlado | Sonido continuo diferenciado del hard drop |
| Hard drop (espacio) | Buffer pre-generado: seno pitch-drop + ruido filtrado + sub punch | 800→45 Hz exponencial en 220ms, ruido 3500→180 Hz en 150ms, sub 55 Hz en 60ms | Impacto de peso y velocidad | Tres capas simultáneas = sensación física de impacto fuerte |
| Fijar pieza | Hard drop (mismo evento) | Ver hard drop | Confirmar bloqueo en tablero | El impacto del drop comunica la fijación |
| Línea eliminada ×1 | Aditiva (3 arm.) | Quinta abierta: tónica + quinta, baseFreq sube un semitono por combo, A:0.003s D:0.18s R:0.12s | Recompensa clara y proporcional | Interválica abierta = resolución sin saturar |
| Línea eliminada ×2 | Aditiva (3 arm.) | Tríada mayor: tónica + tercera + quinta, strum de 8ms entre voces | Mayor recompensa | Más voces = más densidad = más sensación de logro |
| Línea eliminada ×3–4 (Tetris) | Aditiva armónicos de sawtooth + sweep | Cmaj7: C5 E5 G5 B5, 6 arm. (1, 0.5, 0.33…), entrada escalonada 10ms, sus 0.3 rel 0.5s | Máxima recompensa, expansivo | Sawtooth con sustain largo = sonido más lleno y celebratorio |
| Combo progresivo | Aditiva | Pitch base sube un semitono por cada clear consecutivo (freq × 2^(combo/12)) | Intensificación perceptual | Subida de semitono en cada combo comunica escalada sin ser molesta si es consecutiva |
| Error / movimiento inválido | No implementado como error separado | — | — | El juego Tomino no expone este evento en la versión base |
| Pausa | Seno + sweep descendente | 660→330 Hz, A:0.008s D:0.30s R:0.20s | Cierre de estado | Descenso de octava = reducción, pausa |
| Reanudar | Seno + sweep ascendente | 330→660 Hz, A:0.008s D:0.28s R:0.18s | Apertura de estado | Ascenso de octava = expansión, reanudación |
| Nueva partida | FM arpegio ascendente | C4–G4–C5–E5, FM índice 0.8, separación 70ms | Arranque energético | Arpegio ascendente = apertura y expectativa |
| Game over | FM secuencia descendente | A4–F4–C4–A3, FM ratio 0.5 índice 1.2, decays progresivos 0.35–0.70s, separación 200ms | Cierre de partida, pérdida | Descenso cromático con FM grave = caída emocional gradual |
| Botón UI (toggle on) | Seno + sweep ascendente | Alias de Resume | Confirmación | Ascenso = acción positiva |
| Botón UI (toggle off) | Seno + sweep descendente | Alias de Pause | Cancelación | Descenso = acción de salida |

---

## e) Explicación Técnica de Parámetros

### Síntesis FM — Move, Rotate, GameOver

Fórmula: `sample = sin(2π·fc·t + I·sin(2π·fm·t))`

- **Move:** no usa FM, usa seno puro con pitch sweep. El sweep lineal de 380→220 Hz en 8 ms imita el deslizamiento físico de una pieza sobre la grilla.
- **Rotate:** doble transiente de seno puro. Dos sweeps en dirección opuesta (ascendente + descendente) separados 8 ms crean la percepción de giro sin FM. Se prefirió seno sobre FM para mantener limpieza tímbrica en un sonido que se repite con frecuencia.
- **GameOver:** FM con `fc = fm × 2` (ratio 0.5) e índice 1.2. Genera armónicos graves e inarmónicos que comunican tensión y peso sin ser agresivos.

### Síntesis Aditiva — LineClear, Tetris, NewGame, Acordes

`sample = Σ (arm[h] × sin(2π·f·(h+1)·t))`

- **LineClear:** 3 armónicos [1.0, 0.25, 0.08]. Timbre similar a xilófono/campana de cristal. Sustain 0 = sonido percusivo y articulado.
- **Tetris (4 líneas):** 6 armónicos siguiendo serie 1/n [1.0, 0.5, 0.33, 0.25, 0.20, 0.16] = aproximación de diente de sierra. Con sustain 0.3 y release 0.5s el acorde sustenta y comunica celebración.
- **Acordes de acompañamiento (música):** 3 armónicos [1.0, 0.40, 0.10], decay 0.6s con strum de 6 ms entre voces.

### Filtro Biquad LPF — Drone del Ambient

Implementado siguiendo las fórmulas de Robert Bristow-Johnson, calculado sample a sample:

```
w0    = 2π × cutoff / SR
alpha = sin(w0) / (2 × Q)
b0    = (1 - cos(w0)) / 2
a0    = 1 + alpha
```

El cutoff varía entre 380 Hz (calma) y 1980 Hz (peligro máximo). El suavizado del cutoff usa un filtro one-pole independiente para evitar zipper noise: `cutoffCur += (cutoffTarget - cutoffCur) × alpha`.

### ADSR — Diseño por intención perceptual

| Tipo de sonido | Ataque | Decay | Sustain | Release | Efecto |
|---|---|---|---|---|---|
| SFX repetitivos (move, rotate) | 0.001–0.005s | 0.025–0.035s | 0 | 0.015–0.020s | Brevedad total, no interfiere con el siguiente evento |
| SFX de impacto (hard drop) | Buffer pre-generado con envolvente exponencial | — | — | — | Control preciso de la curva de decaimiento |
| LineClear | 0.003s | 0.18s | 0 | 0.12s | Percusivo pero con cola audible |
| Tetris | 0.005s | 0.40s | 0.30 | 0.50s | Sustain largo = celebración sostenida |
| Melodía (Korobeiniki) | 0.005s | 75% de la duración de la nota | 0 | 0.04s | Articulación clara entre notas, ritmo reconocible |

### LFO — SoftDrop y Drone

- **SoftDrop:** LFO de 8–18 Hz (sube con el tiempo sostenido) modulando la amplitud de un oscilador a 95 Hz. A mayor tiempo de caída, más vibración → urgencia.
- **Drone pad:** LFO de 0.06 Hz (ciclo de ~17 segundos) con profundidad 0.12 → breathing muy lento, imperceptible como modulación pero que da vida al pad.

### HardDrop — Buffer Pre-generado

Tres capas calculadas en `Awake()` y reproducidas desde `OnAudioFilterRead()`:

1. **Tonal (220ms):** seno + diente de sierra, pitch drop 800→45 Hz exponencial con τ=18ms. Amplitud: exp(-t/45ms).
2. **Ruido (150ms):** ruido blanco filtrado paso-bajo, cutoff 3500→180 Hz exponencial. Amplitud: exp(-t/35ms).
3. **Sub punch (60ms):** seno puro 55 Hz. Amplitud: exp(-t/20ms). Añade presión física al impacto.

---

## f) Música de Fondo

### Estructura general

La música se compone de cuatro capas sintetizadas en `SynthAmbient.cs`:

| Capa | Técnica | Descripción |
|---|---|---|
| Melodía (Korobeiniki) | Aditiva (4 arm.) | Línea monofónica hardcodeada. Secciones A×2 + B×1 en loop. |
| Acordes de acompañamiento | Aditiva (3 arm.) | Am → G → C → E, strum de 6ms entre voces. Se dispara cada 4 corcheas. |
| Bajo | Seno puro | Fundamental del acorde. Pulso en beat 1 y quinta en beat 3. Glide one-pole. |
| Drone pad | Aditiva 4 voces + filtro biquad + LFO | Base armónica continua. Siempre presente, muy bajo en mezcla. |

### Melodía de Korobeiniki

- **Tonalidad:** La menor (tonalidad original del Game Boy)
- **Tempo base:** 160 BPM
- **Sección A:** frase principal (E5 D5 | C5 D5 E5 | A4 – | …), 20 notas
- **Sección B:** frase secundaria (E5 C5 | A4 – | D5 F5 | B4 – | …), 9 notas
- **Loop:** A – A – B – A – A – B …
- **Timbre:** síntesis aditiva con armónicos [1.0, 0.60, 0.20, 0.08]. El 2do armónico prominente produce el timbre de "piano de chip" reconocible del Tetris clásico.
- **Articulación:** decay = 75% de la duración de la nota, release = 40ms. La nota siguiente arranca antes del release anterior en notas largas pero el silencio es audible en notas cortas → ritmo reconocible.

### Reactividad al peligro (`dangerLevel` ∈ [0, 1])

| Parámetro | Valor en calma | Valor en peligro máximo |
|---|---|---|
| Cutoff filtro drone | 380 Hz | 1980 Hz |
| Q del filtro | 0.9 | 2.4 |
| Tempo de la melodía | 160 BPM | 185 BPM |
| Volumen melodía | ×1.0 | ×1.25 |
| LFO drone (frecuencia) | 0.06 Hz | 0.06 Hz (constante) |

El `dangerLevel` se suaviza con un filtro one-pole (τ ≈ 330ms) para evitar cambios abruptos del filtro (zipper noise).

---

## g) Proceso de Compilación y Despliegue

- **Plataforma:** Windows PC
- **Formato:** `.exe` (x86_64)
- **Scripting Backend:** Mono
- **Versión Unity:** 2022.3.16 LTS

**Configuraciones críticas:**

- `Edit > Project Settings > Audio > DSP Buffer Size: Best Latency` — reduce latencia del hilo de audio de ~85ms a ~12ms. Crítico para que los SFX respondan al input sin delay perceptible.
- Todos los `AudioSource` del pool con `Play On Awake: OFF`, `Loop: ON`, `Spatial Blend: 0` (2D obligatorio).
- Los arrays `MusicClips` y `SfxClips` del `AudioPlayer` original deben estar vacíos o el componente desactivado para evitar duplicidad de sonidos.

**El sistema es 100% procedural:** el build no contiene archivos `.wav` ni `.mp3`. Todo el audio se genera en tiempo real mediante código.

---

## h) Análisis de Resultados

### Qué funcionó correctamente

La arquitectura de pool round-robin de `SynthSFX` resolvió el problema de polifonía: múltiples sonidos coexisten sin interrumpirse. El doble buffer volátil (`_pendingReset`) garantiza comunicación segura entre el hilo principal de Unity y el hilo de audio de `OnAudioFilterRead` sin excepciones de threading.

La melodía de Korobeiniki hardcodeada nota a nota produce un resultado inmediatamente reconocible. El timbre aditivo con 2do armónico prominente (ratio [1, 0.6, 0.2, 0.08]) logra el sonido de "piano de chip" del Tetris original sin necesidad de wavetables externas.

El HardDrop con buffer pre-generado (tres capas: tonal + ruido filtrado + sub) fue el efecto que mayor impacto perceptual tuvo en pruebas: comunica peso y velocidad de forma convincente.

### Dificultades encontradas

**Threading:** las llamadas a `Debug.Log`, `Random.value` y accesos a la API de Unity desde `OnAudioFilterRead` generan excepciones. La solución fue el uso de `volatile bool` y copiar todos los parámetros necesarios a variables locales antes del loop de síntesis.

**Combo acumulativo:** la primera implementación del sistema de combo (`_comboCount++` en cada `PlayLineClearClip`) no reseteaba el contador entre partidas distintas, lo que causaba que el pitch siguiera subiendo indefinidamente. Se resolvió llamando `ResetCombo()` en `PlayNewGameClip()` y `PlayGameOverClip()`.

**Balance de mezcla:** los volúmenes iniciales de la melodía (0.55) tapaban los SFX de línea eliminada. Se calibró bajando el drone a 0.06 y la melodía a 0.52, con los SFX de LineClear a 0.38 para que la recompensa sea perceptiblemente más fuerte que la música de fondo.

### Decisiones de ajuste

- El tempo de la melodía sube de 160 a 185 BPM con `dangerLevel = 1`, replicando la mecánica del Tetris original donde la música acelera con la dificultad.
- El arpegio de LineClear usa strum de 8ms entre voces en lugar de tocar todas simultáneamente, lo que produce un sonido más natural y menos electrónico.

---

## i) Conclusiones

La implementación demuestra que es posible replicar la identidad sonora de un juego de puzzle clásico utilizando únicamente síntesis digital en tiempo real. La clave está en seleccionar la técnica de síntesis correcta para cada tipo de evento: seno puro para acciones repetitivas (move, rotate), FM para eventos de peso o error, aditiva para recompensas, y buffers pre-generados para impactos complejos.

La arquitectura centralizada (un controlador de audio que recibe eventos del juego) separa correctamente las responsabilidades: el código del juego no necesita conocer los detalles de síntesis, y el sistema de síntesis no necesita conocer la lógica del puzzle. Esto facilitó la iteración de parámetros sin tocar los scripts originales de Tomino.

**Posibles mejoras futuras:**
- Implementar un sistema de efectos de UI completo para todos los botones del menú principal.
- Agregar variación procedural en la melodía (ornamentaciones, notas de paso) para sesiones largas.
- Explorar síntesis FM para el drone pad con modulación reactiva al peligro, generando mayor tensión espectral a medida que el tablero se llena.
- Implementar una capa de percusión sintética (kick + hat) para densificar la música en niveles avanzados.
