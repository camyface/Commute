using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Saved player settings, shared by the title screen and the game.
public static class GameSettings
{
    private const string VolumeKey = "Volume";
    private const string TextSpeedKey = "TextSpeed";

    // 0 = instant, otherwise letters per second.
    public const float MinTextSpeed = 15f;
    public const float MaxTextSpeed = 150f;   // the slider's top notch means "instant"

    public static float Volume
    {
        get => PlayerPrefs.GetFloat(VolumeKey, 1f);
        set { PlayerPrefs.SetFloat(VolumeKey, value); PlayerPrefs.Save(); AudioListener.volume = value; }
    }

    public static bool HasTextSpeed => PlayerPrefs.HasKey(TextSpeedKey);

    public static float TextSpeed
    {
        get => PlayerPrefs.GetFloat(TextSpeedKey, 50f);
        set { PlayerPrefs.SetFloat(TextSpeedKey, value); PlayerPrefs.Save(); }
    }

    public static void ApplyVolume() => AudioListener.volume = Volume;
}

// Esc (or a HUD pause button) opens this. Freezes the game, including choice timers.
// Put this on an empty GameObject called "PauseManager" in the game scene.
//
// Wiring (Inspector):
//   PauseButton (optional, in the HUD) -> PauseMenu.TogglePause()
//   ResumeButton                       -> PauseMenu.Resume()
//   QuitButton                         -> PauseMenu.QuitToMenu()
//   Volume Slider / Text Speed Slider  -> just drag them into the fields below
public class PauseMenu : MonoBehaviour
{
    public static bool IsPaused { get; private set; }

    [Tooltip("The pause overlay. Should cover the whole screen so it blocks clicks behind it.")]
    public GameObject pausePanel;
    public Slider volumeSlider;
    public Slider textSpeedSlider;
    [Tooltip("Optional label next to the text speed slider, e.g. \"Fast\" / \"Instant\".")]
    public TMP_Text textSpeedLabel;

    void Start()
    {
        ForceUnpause();
        GameSettings.ApplyVolume();

        if (volumeSlider != null)
        {
            volumeSlider.minValue = 0f;
            volumeSlider.maxValue = 1f;
            volumeSlider.value = GameSettings.Volume;
            volumeSlider.onValueChanged.AddListener(v => GameSettings.Volume = v);
        }

        if (textSpeedSlider != null)
        {
            textSpeedSlider.minValue = GameSettings.MinTextSpeed;
            textSpeedSlider.maxValue = GameSettings.MaxTextSpeed;
            float speed = EventUI.Instance != null ? EventUI.Instance.charsPerSecond : GameSettings.TextSpeed;
            textSpeedSlider.value = speed <= 0f ? GameSettings.MaxTextSpeed : speed;
            textSpeedSlider.onValueChanged.AddListener(OnTextSpeedChanged);
            UpdateTextSpeedLabel(textSpeedSlider.value);
        }

        if (pausePanel != null) pausePanel.SetActive(false);
    }

    void Update()
    {
        if (EscapePressed()) TogglePause();
    }

    public void TogglePause()
    {
        if (IsPaused) Resume();
        else Pause();
    }

    public void Pause()
    {
        IsPaused = true;
        Time.timeScale = 0f;          // freezes typing, timers, fades
        AudioListener.pause = true;
        if (pausePanel != null) pausePanel.SetActive(true);
    }

    public void Resume()
    {
        ForceUnpause();
        if (pausePanel != null) pausePanel.SetActive(false);
    }

    public void QuitToMenu()
    {
        if (EventUI.Instance != null) EventUI.Instance.GoToMainMenu();
    }

    // Always leave the game un-paused when changing scenes.
    public static void ForceUnpause()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
    }

    void OnDestroy()
    {
        ForceUnpause();
    }

    private void OnTextSpeedChanged(float value)
    {
        float speed = value >= GameSettings.MaxTextSpeed - 0.5f ? 0f : value;   // top notch = instant
        GameSettings.TextSpeed = speed;
        if (EventUI.Instance != null) EventUI.Instance.charsPerSecond = speed;
        UpdateTextSpeedLabel(value);
    }

    private void UpdateTextSpeedLabel(float value)
    {
        if (textSpeedLabel == null) return;

        if (value >= GameSettings.MaxTextSpeed - 0.5f) textSpeedLabel.text = "Instant";
        else if (value >= 90f) textSpeedLabel.text = "Fast";
        else if (value >= 40f) textSpeedLabel.text = "Normal";
        else textSpeedLabel.text = "Slow";
    }

    // Works with both the new Input System and the old Input Manager.
    private static bool EscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }
}