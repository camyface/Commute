using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Pairs a location with its background image (filled in from the Inspector).
[Serializable]
public class LocationBackground
{
    public Location location;
    public Sprite sprite;
}

// Runs the day and shows events on screen.
// Put this on an empty GameObject called "EventManager".
//
// Button wiring (do this once, in the Inspector):
//   ChoiceButton1   -> EventUI.ChooseOption(0)
//   ChoiceButton2   -> EventUI.ChooseOption(1)
//   PlayAgainButton -> EventUI.RestartDay()
public class EventUI : MonoBehaviour
{
    [Header("Event Panel")]
    public GameObject eventPanel;
    public TMP_Text eventText;
    public Button[] choiceButtons;

    [Header("HUD")]
    public TMP_Text timeText;      // clock in the top-right corner
    public TMP_Text statusText;    // optional: energy / mood

    [Header("Backgrounds")]
    public Image backgroundImage;
    public LocationBackground[] backgrounds;

    [Header("Sounds")]
    public AudioSource ambientAudioSource;
    public AudioSource specialAudioSource;
    public AudioClip[] ambientAudioClips;
    public AudioClip[] specialAudioClips;

    [Header("Summary Panel")]
    public GameObject summaryPanel;
    public TMP_Text summaryText;

    private List<DayStage> day;
    private int stageIndex;
    private int eventIndex;
    private List<EventChoice> currentChoices = new List<EventChoice>();

    void Start()
    {
        StartDay();
    }

    // ================= BUTTONS =================

    public void ChooseOption(int option)
    {
        if (option < 0 || option >= currentChoices.Count) return;

        currentChoices[option].onChoose?.Invoke();
        ShowNextEvent();
    }

    public void RestartDay()
    {
        StartDay();
    }

    // ================= DAY FLOW =================

    private void StartDay()
    {
        GameState.Instance.ResetState();

        day = DayEvents.BuildDay();
        stageIndex = 0;
        eventIndex = 0;

        summaryPanel.SetActive(false);
        if (eventPanel != null) eventPanel.SetActive(true);

        ShowNextEvent();
    }

    // Finds the next event whose conditions pass, moving through locations in order.
    private void ShowNextEvent()
    {
        while (stageIndex < day.Count)
        {
            DayStage stage = day[stageIndex];

            while (eventIndex < stage.events.Count)
            {
                GameEvent ev = stage.events[eventIndex];
                eventIndex++;

                if (ev.CanShow())
                {
                    ShowEvent(ev, stage.location);
                    return;
                }
            }

            stageIndex++;
            eventIndex = 0;
        }

        ShowSummary();
    }

    private void ShowEvent(GameEvent ev, Location location)
    {
        GameState.Instance.currentLocation = location;
        SetBackground(location);
        SetAudio(location);

        eventText.text = ev.Text;

        currentChoices = ev.GetAvailableChoices();
        if (currentChoices.Count == 0)
            currentChoices.Add(new EventChoice { label = "Continue" });

        if (currentChoices.Count > choiceButtons.Length)
            Debug.LogWarning($"Event '{ev.id}' has {currentChoices.Count} choices but only {choiceButtons.Length} buttons.");

        for (int i = 0; i < choiceButtons.Length; i++)
        {
            bool used = i < currentChoices.Count;
            choiceButtons[i].gameObject.SetActive(used);

            if (used)
                choiceButtons[i].GetComponentInChildren<TMP_Text>().text = currentChoices[i].label;
        }

        UpdateHUD();
    }

    // ================= BACKGROUND =================

    private void SetBackground(Location location)
    {
        if (backgroundImage == null) return;

        foreach (var bg in backgrounds)
        {
            if (bg.location == location)
            {
                backgroundImage.sprite = bg.sprite;
                return;
            }
        }

        Debug.LogWarning($"No background assigned for {location}");
    }

    // ================== AUDIO =================
    private void SetAudio(Location location)
    {
        if (ambientAudioSource == null || ambientAudioClips.Length == 0) return;
        if (specialAudioSource == null || specialAudioClips.Length == 0) return;
        int index = (int)location;
        if (index < 0 || index >= ambientAudioClips.Length || (ambientAudioSource.clip == ambientAudioClips[index] && ambientAudioSource.isPlaying))
        {
            Debug.LogWarning($"No ambient audio clip assigned for {location} {index}");
            return;
        }
        Debug.Log($"Playing audio for {location}: {ambientAudioClips[index].name} {index}");
        ambientAudioSource.Stop();
        ambientAudioSource.clip = ambientAudioClips[index];
        ambientAudioSource.Play();
    }

    // ================= HUD =================

    private void UpdateHUD()
    {
        var gs = GameState.Instance;

        if (timeText != null)
            timeText.text = gs.GetTimeString();

        if (statusText != null)
            statusText.text = $"Energy: {gs.energy}   Mood: {gs.mood}";
    }

    // ================= SUMMARY =================

    private void ShowSummary()
    {
        var gs = GameState.Instance;

        var sb = new StringBuilder();
        sb.AppendLine(gs.HasFlag("LateToWork") ? "<b>You were late to work.</b>" : "<b>You made it to work on time!</b>");
        sb.AppendLine($"Energy: {gs.energy}   Mood: {gs.mood}");
        sb.AppendLine();

        foreach (string entry in gs.GetHistory())
            sb.AppendLine(entry);

        summaryText.text = sb.ToString();

        if (eventPanel != null) eventPanel.SetActive(false);
        summaryPanel.SetActive(true);
        UpdateHUD();
    }
}