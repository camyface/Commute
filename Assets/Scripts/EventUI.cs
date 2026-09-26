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

    [Header("Summary Panel")]
    public GameObject summaryPanel;
    public TMP_Text summaryText;

    private List<DayStage> day;
    private int stageIndex;
    private int eventIndex;
    private List<EventChoice> currentChoices = new List<EventChoice>();
    private bool showingResult;   // true while the "what happened" screen is up

    void Start()
    {
        StartDay();
    }

    // ================= BUTTONS =================

    public void ChooseOption(int option)
    {
        // On the result screen the only button is Continue.
        if (showingResult)
        {
            showingResult = false;
            ShowNextEvent();
            return;
        }

        if (option < 0 || option >= currentChoices.Count) return;

        // Snapshot the state so we can show what the choice changed.
        var gs = GameState.Instance;
        int timeBefore = gs.timeMinutes;
        int energyBefore = gs.energy;
        int moodBefore = gs.mood;
        int historyBefore = gs.GetHistory().Count;

        currentChoices[option].onChoose?.Invoke();

        // Choices that don't change time or stats skip the result screen.
        if (gs.timeMinutes == timeBefore && gs.energy == energyBefore && gs.mood == moodBefore)
        {
            ShowNextEvent();
            return;
        }

        ShowResult(historyBefore, gs.timeMinutes - timeBefore, gs.energy - energyBefore, gs.mood - moodBefore);
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
        showingResult = false;

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

    // ================= CHOICE RESULT =================

    // Shows the history lines the choice added plus the time/stat changes, with one Continue button.
    private void ShowResult(int historyStart, int minutes, int energy, int mood)
    {
        showingResult = true;

        var sb = new StringBuilder();
        IReadOnlyList<string> history = GameState.Instance.GetHistory();
        for (int i = historyStart; i < history.Count; i++)
        {
            sb.AppendLine(StripTimestamp(history[i]) + ".");
        }
        if (sb.Length > 0) sb.AppendLine();
        sb.Append($"<b>{FormatChanges(minutes, energy, mood)}</b>");

        eventText.text = sb.ToString();

        for (int i = 0; i < choiceButtons.Length; i++)
        {
            choiceButtons[i].gameObject.SetActive(i == 0);
        }
        choiceButtons[0].GetComponentInChildren<TMP_Text>().text = "Continue";

        UpdateHUD();
    }

    // History entries look like "[7:45 AM] Took the bus"; drop the time part.
    private static string StripTimestamp(string entry)
    {
        int end = entry.IndexOf("] ");
        return end >= 0 ? entry.Substring(end + 2) : entry;
    }

    // e.g. "+1 hr 5 min  ·  -2 mood"
    private static string FormatChanges(int minutes, int energy, int mood)
    {
        var parts = new List<string>();
        if (minutes != 0) parts.Add(FormatMinutes(minutes));
        if (energy != 0) parts.Add($"{energy:+0;-0} energy");
        if (mood != 0) parts.Add($"{mood:+0;-0} mood");
        return string.Join("  ·  ", parts);
    }

    private static string FormatMinutes(int minutes)
    {
        string sign = minutes > 0 ? "+" : "-";
        int total = Mathf.Abs(minutes);
        int hours = total / 60;
        int mins = total % 60;

        if (hours == 0) return $"{sign}{mins} min";
        return mins == 0 ? $"{sign}{hours} hr" : $"{sign}{hours} hr {mins} min";
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