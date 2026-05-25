using Tomino.Audio;
using Tomino.Input;
using Tomino.Model;
using Tomino.View;
using UnityEngine;

namespace Tomino
{
    public class GameController : MonoBehaviour
    {
        public GameConfig gameConfig;
        public AlertView alertView;
        public SettingsView settingsView;
        public AudioPlayer audioPlayer;
        public GameObject screenButtons;
        public AudioSource musicAudioSource;

        private Game _game;
        private UniversalInput _universalInput;

        internal void Awake()
        {
            Application.targetFrameRate = 60;

            HandlePlayerSettings();
            Settings.changedEvent += HandlePlayerSettings;
        }

        internal void Start()
        {
            Board board = new(10, 20);

            gameConfig.boardView.SetBoard(board);
            gameConfig.nextPieceView.SetBoard(board);

            _universalInput = new UniversalInput(new KeyboardInput(), gameConfig.boardView.touchInput);

            _game = new Game(board, _universalInput);
            _game.FinishedEvent += OnGameFinished;
            _game.PieceFinishedFallingEvent += audioPlayer.PlayPieceDropClip;
            _game.PieceRotatedEvent += audioPlayer.PlayPieceRotateClip;
            _game.PieceMovedEvent += audioPlayer.PlayPieceMoveClip;
            _game.RowsClearedEvent += OnRowsCleared;
            _game.FinishedEvent    += audioPlayer.PlayGameOverClip;
            _game.Start();
            

            gameConfig.scoreView.game = _game;
            gameConfig.levelView.game = _game;
        }

        public void OnPauseButtonTap()
        {
            _game.Pause();
            ShowPauseView();
        }

        public void OnMoveLeftButtonTap()
        {
            _game.SetNextAction(PlayerAction.MoveLeft);
        }

        public void OnMoveRightButtonTap()
        {
            _game.SetNextAction(PlayerAction.MoveRight);
        }

        public void OnMoveDownButtonTap()
        {
            _game.SetNextAction(PlayerAction.MoveDown);
        }

        public void OnRotateButtonTap()
        {
            _game.SetNextAction(PlayerAction.Rotate);
        }

        private void OnGameFinished()
        {
            alertView.SetTitle(TextID.GameFinished);
            alertView.AddButton(TextID.PlayAgain, _game.Start, audioPlayer.PlayNewGameClip);
            alertView.Show();
        }

        private void OnRowsCleared(int count)
        {
            if (count >= 4)
                AudioManager.instance?.PlayTetrisClearClip();
            else
                AudioManager.instance?.PlayLineClearClip(count);
        }

        internal void Update()
        {
            _game.Update(Time.deltaTime);
            UpdateDangerLevel(); // ← agregar
        }

        // Calcula el danger level según la altura de las piezas en el tablero.
        // Llama a SetDangerLevel en el ambient para que el filtro reaccione.
        private void UpdateDangerLevel()
        {
            if (_game?.Board == null) return;

            int highestRow = 0;
            foreach (var block in _game.Board.Blocks)
            {
                if (block.Position.Row > highestRow)
                    highestRow = block.Position.Row;
            }

            // Peligro desde fila 0 hasta fila 16 (tablero de 20 filas)
            float danger = Mathf.Clamp01(highestRow / 16f);
            AudioManager.instance?.SetDangerLevel(danger);
        }

        private void ShowPauseView()
        {
            alertView.SetTitle(TextID.GamePaused);
            alertView.AddButton(TextID.Resume, _game.Resume, audioPlayer.PlayResumeClip);
            alertView.AddButton(TextID.NewGame, _game.Start, audioPlayer.PlayNewGameClip);
            alertView.AddButton(TextID.Settings, ShowSettingsView, audioPlayer.PlayResumeClip);
            alertView.Show();
        }

        private void ShowSettingsView()
        {
            settingsView.Show(ShowPauseView);
        }

        private void HandlePlayerSettings()
        {
            screenButtons.SetActive(Settings.ScreenButtonsEnabled);
            gameConfig.boardView.touchInput.Enabled = !Settings.ScreenButtonsEnabled;
            AudioManager.instance?.SetMusicEnabled(Settings.MusicEnabled);
            if (musicAudioSource != null)
                musicAudioSource.gameObject.SetActive(false);
        }
    }   
}
