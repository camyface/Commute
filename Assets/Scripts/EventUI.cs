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

// Runs the week and shows events on screen.
// Put this on an empty GameObject called "EventManager".
//
// Button wiring (do this once, in the Inspector):
//   ChoiceButton1  -> EventUI.ChooseOption(0)
//   ChoiceButton2  -> EventUI.ChooseOption(1)
//   SummaryButton  -> EventUI.SummaryButtonPressed()
public class EventUI : MonoBehaviour
{
    [Header("Event Panel")]
    public GameObject eventPanel;
    public TMP_Text eventText;
    public Button[] choiceButtons;

    [Header("HUD")]
    public TMP_Text timeText;      // day + clock in the top-right corner
    public TMP_Text statusText;    // optional: energy / mood

    [Header("Backgrounds")]
    public Image backgroundImage;
    public LocationBackground[] backgrounds;

    [Header("Sounds")]
    public AudioSource ambientAudioSource;
    public AudioSource specialAudioSource;
    public AudioClip[] ambientAudioClips;   // one per Location, in enum order

    [System.Serializable]
    public class SpecialSound
    {
        public string id;          // for debugging, e.g. "Doorbell"
        public AudioClip clip;     // the sound effect
    }
    public List<SpecialSound> specialAudioClips;   // for one-shot sound effects

    [Header("Summary Panel")]
    public GameObject summaryPanel;
    public TMP_Text summaryText;
    public TMP_Text summaryButtonText;   // label on the summary panel's button
    public ScrollRect summaryScroll;     // optional: the Scroll View around SummaryText

    private List<DayStage> day;
    private int stageIndex;
    private int eventIndex;
    private List<EventChoice> currentChoices = new List<EventChoice>();

    // Lets event code call things like EventUI.Instance.PlaySpecial(0).
    public static EventUI Instance { get; private set; }

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        StartWeek();
    }

    // ================= BUTTONS =================

    public void ChooseOption(int option)
    {
        if (option < 0 || option >= currentChoices.Count) return;

        currentChoices[option].onChoose?.Invoke();
        ShowNextEvent();
    }

    // "Next day" after days 1-4, "Play again" after day 5.
    public void SummaryButtonPressed()
    {
        if (GameState.Instance.IsLastDay)
        {
            StartWeek();
        }
        else
        {
            GameState.Instance.StartNewDay();
            StartDay();
        }
    }

    // ================= WEEK / DAY FLOW =================

    private void StartWeek()
    {
        GameState.Instance.ResetState();
        StartDay();
    }

    private void StartDay()
    {
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

        ShowDaySummary();
    }

    private void ShowEvent(GameEvent ev, Location location)
    {
        GameState.Instance.currentLocation = location;
        SetBackground(location);
        SetAudio(location);
        PlaySpecialSound(ev.id);

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

    // ================= AUDIO =================

    // Plays the ambient loop for a location. Clips are matched by enum order:
    // 0 Home, 1 OutsideHome, 2 CommuteToWork, 3 OutsideWork, 4 Work, 5 CommuteHome
    private void SetAudio(Location location)
    {
        if (ambientAudioSource == null || ambientAudioClips == null || ambientAudioClips.Length == 0) return;

        int index = (int)location;

        if (index < 0 || index >= ambientAudioClips.Length || ambientAudioClips[index] == null)
        {
            Debug.LogWarning($"No ambient audio clip assigned for {location} ({index})");
            return;
        }

        // Already playing this location's clip: keep it going without restarting.
        if (ambientAudioSource.clip == ambientAudioClips[index] && ambientAudioSource.isPlaying)
            return;

        Debug.Log($"Playing audio for {location}: {ambientAudioClips[index].name} ({index})");
        ambientAudioSource.Stop();
        ambientAudioSource.clip = ambientAudioClips[index];
        ambientAudioSource.Play();
    }

    // Plays a one-shot sound effect over the ambient audio.
    // Call from an event choice, e.g. EventUI.Instance.PlaySpecialSound(string id)
    public void PlaySpecialSound(string id)
    {
        if (specialAudioSource == null || specialAudioClips == null) return;

        var specialSound = specialAudioClips.Find(sound => sound.id == id);
        if (specialSound == null || specialSound.clip == null)
        {
            Debug.LogWarning($"No special audio clip found for ID: {id}");
            return;
        }

        specialAudioSource.PlayOneShot(specialSound.clip);
    }

    // ================= HUD =================

    private void UpdateHUD()
    {
        var gs = GameState.Instance;

        if (timeText != null)
            timeText.text = $"{gs.DayName}  {gs.GetTimeString()}";

        if (statusText != null)
            statusText.text = $"Energy: {gs.energy}   Mood: {gs.mood}";
    }

    // ================= SUMMARIES =================

    private void ShowDaySummary()
    {
        var gs = GameState.Instance;
        gs.EndDay();

        var sb = new StringBuilder();

        if (gs.IsLastDay)
        {
            BuildWeekSummary(sb);
            if (summaryButtonText != null) summaryButtonText.text = "Play again";
        }
        else
        {
            sb.AppendLine($"<b>{gs.DayName} is over.</b>");
            sb.AppendLine(gs.HasFlag("LateToWork") ? "You were late to work." : "You made it to work on time.");
            sb.AppendLine($"Energy: {gs.energy}   Mood: {gs.mood}");
            sb.AppendLine();

            foreach (string entry in gs.GetHistoryForDay(gs.currentDay))
                sb.AppendLine(entry);

            if (summaryButtonText != null) summaryButtonText.text = "Next day";
        }

        summaryText.text = sb.ToString();

        if (eventPanel != null) eventPanel.SetActive(false);
        summaryPanel.SetActive(true);

        // Start scrolled to the top.
        if (summaryScroll != null)
        {
            Canvas.ForceUpdateCanvases();
            summaryScroll.verticalNormalizedPosition = 1f;
        }

        UpdateHUD();
    }

    private void BuildWeekSummary(StringBuilder sb)
    {
        var gs = GameState.Instance;
        int late = gs.GetCount("TimesLate");
        int productive = gs.GetCount("ProductiveDays");
        int friendship = gs.GetCount("NeighborFriendship");

        sb.AppendLine("<b>The week is over.</b>");
        sb.AppendLine();

        foreach (string recap in gs.GetDayRecaps())
            sb.AppendLine(recap);

        sb.AppendLine();
        sb.AppendLine($"Days late: {late}   Productive days: {productive}");
        sb.AppendLine($"Final energy: {gs.energy}   Final mood: {gs.mood}");

        if (friendship >= 2) sb.AppendLine("You and your neighbor are becoming friends.");
        else if (friendship <= -2) sb.AppendLine("Your neighbor has stopped waving.");

        sb.AppendLine();

        // Ending
        if (late >= 3)
            sb.AppendLine("<b>Ending: On Thin Ice.</b> Your boss has put you on a final warning.");
        else if (productive >= 4 && late <= 1)
            sb.AppendLine("<b>Ending: Rising Star.</b> Your boss mentions a promotion.");
        else if (gs.mood >= 8)
            sb.AppendLine("<b>Ending: Good Vibes.</b> Work was fine, but you enjoyed your week.");
        else
            sb.AppendLine("<b>Ending: Survived.</b> Another week down.");
    }
}