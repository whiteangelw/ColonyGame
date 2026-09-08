using UnityEngine;
using UnityEngine.InputSystem;

public class GamePauseController : MonoBehaviour
{
    [Header("Atalho")]
    [SerializeField] private Key pauseKey = Key.O;

    [Header("Visual opcional")]
    [SerializeField] private GameObject pausedIndicator;

    [Header("Controles bloqueados durante a pausa")]
    [Tooltip("Arraste aqui PlayerInput e CameraController.")]
    [SerializeField] private Behaviour[] gameplayBehavioursToDisable;

    public bool IsPaused { get; private set; }

    private float timeScaleBeforePause = 1f;
    private bool[] previousBehaviourStates;

    private void Awake()
    {
        pausedIndicator?.SetActive(false);
    }

    private void Update()
    {
        if (Keyboard.current == null
            || !Keyboard.current[pauseKey].wasPressedThisFrame)
        {
            return;
        }

        SaveGameService saveService = SaveGameService.Instance;

        if (SaveGameRuntime.IsLoading
            || (saveService != null
                && (saveService.IsBusy
                    || saveService.IsWaitingForStartupChoice)))
        {
            return;
        }

        TogglePause();
    }

    public void TogglePause()
    {
        if (IsPaused)
        {
            ResumeGame();
        }
        else
        {
            PauseGame();
        }
    }

    public void PauseGame()
    {
        if (IsPaused)
        {
            return;
        }

        IsPaused = true;
        timeScaleBeforePause = Time.timeScale > 0f ? Time.timeScale : 1f;
        CaptureAndDisableGameplayBehaviours();
        Time.timeScale = 0f;
        pausedIndicator?.SetActive(true);
    }

    public void ResumeGame()
    {
        if (!IsPaused)
        {
            return;
        }

        IsPaused = false;
        Time.timeScale = timeScaleBeforePause;
        RestoreGameplayBehaviours();
        pausedIndicator?.SetActive(false);
    }

    private void CaptureAndDisableGameplayBehaviours()
    {
        if (gameplayBehavioursToDisable == null)
        {
            return;
        }

        previousBehaviourStates = new bool[gameplayBehavioursToDisable.Length];

        for (int i = 0; i < gameplayBehavioursToDisable.Length; i++)
        {
            Behaviour behaviour = gameplayBehavioursToDisable[i];

            if (behaviour == null)
            {
                continue;
            }

            previousBehaviourStates[i] = behaviour.enabled;
            behaviour.enabled = false;
        }
    }

    private void RestoreGameplayBehaviours()
    {
        if (gameplayBehavioursToDisable == null
            || previousBehaviourStates == null)
        {
            return;
        }

        int count = Mathf.Min(
            gameplayBehavioursToDisable.Length,
            previousBehaviourStates.Length
        );

        for (int i = 0; i < count; i++)
        {
            if (gameplayBehavioursToDisable[i] != null)
            {
                gameplayBehavioursToDisable[i].enabled =
                    previousBehaviourStates[i];
            }
        }
    }

    private void OnDestroy()
    {
        if (IsPaused)
        {
            ResumeGame();
        }
    }
}
