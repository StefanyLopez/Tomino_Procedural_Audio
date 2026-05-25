using UnityEngine;

namespace Tomino.Audio
{
    public class AudioPlayer : MonoBehaviour
    {
        public void PlayPauseClip()       => AudioManager.instance?.PlayPauseClip();
        public void PlayResumeClip()      => AudioManager.instance?.PlayResumeClip();
        public void PlayNewGameClip()     => AudioManager.instance?.PlayNewGameClip();
        public void PlayPieceMoveClip()   => AudioManager.instance?.PlayPieceMoveClip();
        public void PlayPieceRotateClip() => AudioManager.instance?.PlayPieceRotateClip();
        public void PlayPieceDropClip()   => AudioManager.instance?.PlayPieceDropClip();
        public void PlayToggleOnClip()    => AudioManager.instance?.PlayToggleOnClip();
        public void PlayToggleOffClip()   => AudioManager.instance?.PlayToggleOffClip();
        public void PlayGameOverClip()    => AudioManager.instance?.PlayGameOverClip();

        internal void Awake() { }
    }
}