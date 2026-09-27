using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Pairs a backdrop with its background image (filled in from the Inspector).
[Serializable]
public class BackdropImage
{
    public Backdrop backdrop;
    public Sprite sprite;
}

// A character's name and portraits (filled in from the Inspector).
// Only Neutral is required; missing expressions fall back to it.
[Serializable]
public class CharacterData
{
    public CharacterId id;
    public string displayName;
    public Sprite neutral;
    public Sprite happy;
    public Sprite annoyed;
    public Sprite sad;
    public Sprite surprised;

    public Sprite GetSprite(Expression expression)
    {
        Sprite sprite = null;
        switch (expression)
        {
            case Expression.Happy: sprite = happy; break;
            case Expression.Annoyed: sprite = annoyed; break;
            case Expression.Sad: sprite = sad; break;
            case Expression.Surprised: sprite = surprised; break;
        }
        return sprite != null ? sprite : neutral;
    }
}

// Runs the week and shows events on screen.
// Put this on an empty GameObject called "EventManager".
//
// Button wiring (do this once, in the Inspector):
//   ChoiceButton1  -> EventUI.ChooseOption(0)
//   ChoiceButton2  -> EventUI.ChooseOption(1)
//   ChoiceButton3  -> EventUI.ChooseOption(2)
//   SummaryButton  -> EventUI.SummaryButtonPressed()
public class EventUI : MonoBehaviour
{
    [Header("Event Panel")]
    public GameObject eventPanel;
    public TMP_Text eventText;
    public Button[] choiceButtons;

    [Header("HUD")]
    public TMP_Text timeText;      // day + clock in the top-right corner
    public TMP_Text statusText;    // optional: energy / mood / money / job

    [Header("Backgrounds")]
    public Image backgroundImage;
    public BackdropImage[] backgrounds;   // one per Backdrop

    [Header("Characters")]
    public Image characterImage;       // the other character (right side)
    public Image playerImage;          // the player (left side)
    [Tooltip("Color of the speaker's name in front of their line, e.g. \"Name: Hello there!\"")]
    public Color speakerNameColor = new Color(1f, 0.85f, 0.4f, 1f);
    public CharacterData[] characters; // include an entry with id Player for the player's portraits
    public string continueLabel = "Continue";

    [Header("Speaker Highlight")]
    public Color speakingColor = Color.white;
    public Color listeningColor = new Color(0.55f, 0.55f, 0.55f, 1f);  // the "shadow"
    public float highlightFadeSpeed = 10f;

    private Color characterTargetColor = Color.white;
    private Color playerTargetColor = Color.white;
    private Expression playerExpression = Expression.Neutral;

    [Header("Sounds")]
    public AudioSource ambientAudioSource;
    public AudioSource specialAudioSource;
    public AudioClip[] ambientAudioClips;   // one per Location, in enum order

    // Named sound effects. A clip whose id matches an event id (e.g. "Alarm", "PoliceRaid")
    // plays automatically when that event starts. Others can be played with PlaySpecialAudio("id").
    [Serializable]
    public class SpecialAudioClip
    {
        public string id;       // e.g. "Doorbell", "CarHorn"
        public AudioClip clip;
    }
    public List<SpecialAudioClip> specialAudioClips;

    [Header("Radiant Events")]
    [Tooltip("Chance (0-1) that a random event from RadiantEvents.cs pops up at the start of each stage.")]
    [Range(0f, 1f)] public float radiantEventChance = 0.1f;

    [Header("Feedback Popups")]
    [Tooltip("Shows stat changes (+$60, Job -1) and notes like \"Joe will remember that.\" after each choice.")]
    public TMP_Text feedbackText;
    public float feedbackDuration = 2.5f;
    public Color gainColor = new Color(0.42f, 0.8f, 0.47f, 1f);
    public Color lossColor = new Color(1f, 0.42f, 0.42f, 1f);

    [Header("Choice Timer")]
    [Tooltip("An Image with Image Type = Filled (Horizontal). Only shown on timed choices.")]
    public Image timerBar;
    public Color timerCalmColor = new Color(1f, 0.85f, 0.4f, 1f);
    public Color timerUrgentColor = new Color(1f, 0.3f, 0.3f, 1f);

    [Header("Game Feel")]
    [Tooltip("Letters per second for the typewriter effect. 0 = show text instantly.")]
    public float charsPerSecond = 50f;
    [Tooltip("Full-screen white Image with alpha 0 and Raycast Target off. Flashes on big moments.")]
    public Image flashImage;
    [Tooltip("What shakes on big moments. Defaults to the event panel.")]
    public RectTransform shakeTarget;
    [Tooltip("How much a portrait bounces when that character speaks.")]
    public float portraitBounce = 0.06f;
    [Tooltip("Clock color when you're about to be late for work.")]
    public Color lateClockColor = new Color(1f, 0.35f, 0.35f, 1f);

    [Header("Summary Panel")]
    public GameObject summaryPanel;
    public TMP_Text summaryText;
    public TMP_Text summaryButtonText;   // label on the summary panel's button
    public ScrollRect summaryScroll;     // optional: the Scroll View around SummaryText

    private List<DayStage> day;
    private int stageIndex;
    private int eventIndex;
    private List<EventChoice> currentChoices = new List<EventChoice>();
    private readonly HashSet<string> usedGroups = new HashSet<string>();
    private bool radiantRolled;   // one radiant roll per stage
    private bool inEpilogue;      // playing the end-of-week character slides

    private GameEvent currentEvent;

    // Feel state
    private bool typing;
    private float typedChars;
    private int totalChars;
    private bool timerActive;
    private float timerLeft;
    private float feedbackTimer;
    private float flashTimer;
    private float shakeTimer;
    private Vector2 shakeBasePos;
    private Color timeTextBaseColor = Color.white;
    private const float PunchTime = 0.25f;
    private const float ImpactTime = 0.35f;
    private float characterPunch, playerPunch;
    private bool characterShake, playerShake;
    private Vector3 characterBaseScale = Vector3.one, playerBaseScale = Vector3.one;
    private Vector2 characterBasePos, playerBasePos;

    // Stats before a choice, to show what changed.
    private struct StatSnapshot
    {
        public int energy, mood, money, standing;
        public static StatSnapshot Take()
        {
            var gs = GameState.Instance;
            return new StatSnapshot { energy = gs.energy, mood = gs.mood, money = gs.money, standing = gs.standing };
        }
    }

    // Dialogue playback
    private readonly Queue<DialogueLine> lineQueue = new Queue<DialogueLine>();
    private List<EventChoice> choicesAfterLines;   // null = go to next event when lines finish
    private bool waitingForContinue;

    // Lets event code call things like EventUI.Instance.PlaySpecial(0).
    public static EventUI Instance { get; private set; }

    void Awake()
    {
        Instance = this;

        // Remember where things sit, so bounces and shakes always return home.
        // (A flipped portrait keeps its negative X scale.)
        if (characterImage != null)
        {
            characterBaseScale = characterImage.rectTransform.localScale;
            characterBasePos = characterImage.rectTransform.anchoredPosition;
        }
        if (playerImage != null)
        {
            playerBaseScale = playerImage.rectTransform.localScale;
            playerBasePos = playerImage.rectTransform.anchoredPosition;
        }
        if (shakeTarget == null && eventPanel != null) shakeTarget = eventPanel.GetComponent<RectTransform>();
        if (shakeTarget != null) shakeBasePos = shakeTarget.anchoredPosition;
        if (timeText != null) timeTextBaseColor = timeText.color;

        if (feedbackText != null) feedbackText.text = "";
        if (timerBar != null) timerBar.gameObject.SetActive(false);
        if (flashImage != null)
        {
            Color c = flashImage.color;
            c.a = 0f;
            flashImage.color = c;
        }
    }

    void Start()
    {
        StartWeek();
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // Smoothly fade portraits toward lit / shadowed.
        float t = dt * highlightFadeSpeed;
        if (characterImage != null)
            characterImage.color = Color.Lerp(characterImage.color, characterTargetColor, t);
        if (playerImage != null)
            playerImage.color = Color.Lerp(playerImage.color, playerTargetColor, t);

        // Typewriter.
        if (typing)
        {
            typedChars += charsPerSecond * dt;
            int shown = Mathf.Min((int)typedChars, totalChars);
            eventText.maxVisibleCharacters = shown;
            if (shown >= totalChars) FinishTyping();
        }

        // Choice timer (starts once the text has finished typing).
        if (timerActive && !typing)
        {
            timerLeft -= dt;
            if (timerBar != null && currentEvent != null)
            {
                float fraction = Mathf.Clamp01(timerLeft / currentEvent.timerSeconds);
                timerBar.fillAmount = fraction;
                timerBar.color = Color.Lerp(timerUrgentColor, timerCalmColor, fraction);
            }
            if (timerLeft <= 0f)
            {
                StopTimer();
                OnTimerExpired();
            }
        }

        // Feedback popup fades out over its last half second.
        if (feedbackText != null && feedbackTimer > 0f)
        {
            feedbackTimer -= dt;
            feedbackText.alpha = Mathf.Clamp01(feedbackTimer / 0.5f);
            if (feedbackTimer <= 0f) feedbackText.text = "";
        }

        // Impact flash + shake.
        if (flashImage != null && flashTimer > 0f)
        {
            flashTimer -= dt;
            Color c = flashImage.color;
            c.a = Mathf.Clamp01(flashTimer / ImpactTime) * 0.8f;
            flashImage.color = c;
        }
        if (shakeTarget != null && shakeTimer > 0f)
        {
            shakeTimer -= dt;
            shakeTarget.anchoredPosition = shakeTimer > 0f
                ? shakeBasePos + UnityEngine.Random.insideUnitCircle * 14f * (shakeTimer / ImpactTime)
                : shakeBasePos;
        }

        // Speaking portraits bounce (and shake when angry or shocked).
        AnimatePortrait(characterImage, ref characterPunch, characterBaseScale, characterBasePos, characterShake, dt);
        AnimatePortrait(playerImage, ref playerPunch, playerBaseScale, playerBasePos, playerShake, dt);

        // Clock pulses red when you're about to be late for work.
        if (timeText != null)
        {
            timeText.color = IsRunningLate()
                ? Color.Lerp(timeTextBaseColor, lateClockColor, 0.5f + 0.5f * Mathf.Sin(Time.time * 6f))
                : timeTextBaseColor;
        }
    }

    // ================= BUTTONS =================

    public void ChooseOption(int option)
    {
        // Text still typing: the first click just finishes it.
        if (typing)
        {
            FinishTyping();
            return;
        }

        // Mid-conversation: the only button is "Continue".
        if (waitingForContinue)
        {
            waitingForContinue = false;
            ShowNextLine();
            return;
        }

        if (option < 0 || option >= currentChoices.Count) return;

        ResolveChoice(currentChoices[option], timedOut: false);
    }

    private void ResolveChoice(EventChoice choice, bool timedOut)
    {
        StopTimer();

        StatSnapshot before = StatSnapshot.Take();
        choice.onChoose?.Invoke();
        ShowFeedback(before, currentEvent != null ? currentEvent.GetRememberNote(choice) : null, timedOut);

        // Let the character react before moving on.
        if (choice.replies.Count > 0)
            PlayLines(choice.replies, null);
        else
            ShowNextEvent();
    }

    // Time ran out: use the event's OnTimeout choice, or the last visible choice.
    private void OnTimerExpired()
    {
        if (currentEvent == null) return;

        EventChoice choice = currentEvent.GetTimeoutChoice();
        if (choice == null && currentChoices.Count > 0) choice = currentChoices[currentChoices.Count - 1];
        if (choice != null) ResolveChoice(choice, timedOut: true);
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
        usedGroups.Clear();
        radiantRolled = false;
        inEpilogue = false;

        summaryPanel.SetActive(false);
        if (eventPanel != null) eventPanel.SetActive(true);

        ShowNextEvent();
    }

    // Finds the next event whose conditions pass, moving through locations in order.
    // Grouped events: the first time a group is reached, one eligible event is picked at random.
    private void ShowNextEvent()
    {
        var gs = GameState.Instance;

        while (stageIndex < day.Count)
        {
            // Fired, arrested, etc: skip whatever is left of the day.
            if (!inEpilogue && (gs.weekOver || gs.HasFlag(GameState.EndDayFlag))) break;

            DayStage stage = day[stageIndex];

            // Arriving at a new stage: small chance of a radiant event first.
            if (!radiantRolled && !inEpilogue)
            {
                radiantRolled = true;
                GameEvent radiant = PickRadiantEvent(stage.location);
                if (radiant != null)
                {
                    ShowEvent(radiant, stage);
                    return;
                }
            }

            while (eventIndex < stage.events.Count)
            {
                GameEvent ev = stage.events[eventIndex];
                eventIndex++;

                if (!string.IsNullOrEmpty(ev.group))
                {
                    if (!usedGroups.Add(ev.group)) continue;   // group already rolled
                    ev = PickFromGroup(stage, ev.group);
                    if (ev == null) continue;
                }
                else if (!ev.CanShow())
                {
                    continue;
                }

                ShowEvent(ev, stage);
                return;
            }

            stageIndex++;
            eventIndex = 0;
            usedGroups.Clear();
            radiantRolled = false;
        }

        // Last day done (or the week ended early): play the epilogue before the summary.
        if (gs.IsLastDay && !inEpilogue)
        {
            inEpilogue = true;
            gs.EndDay();   // final bookkeeping first, so the epilogue and summary agree
            day = new List<DayStage> { DayEvents.BuildEpilogue() };
            stageIndex = 0;
            eventIndex = 0;
            usedGroups.Clear();
            ShowNextEvent();
            return;
        }

        ShowDaySummary();
    }

    // Rolls radiantEventChance, then picks one eligible event from RadiantEvents.cs for this location.
    private GameEvent PickRadiantEvent(Location location)
    {
        if (UnityEngine.Random.value >= radiantEventChance) return null;

        List<GameEvent> candidates = RadiantEvents.For(location).FindAll(e => e.CanShow());
        if (candidates.Count == 0) return null;

        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    // Weighted random pick among the group's events that can show right now.
    private GameEvent PickFromGroup(DayStage stage, string group)
    {
        var candidates = new List<GameEvent>();
        float total = 0f;

        foreach (var ev in stage.events)
        {
            if (ev.group == group && ev.CanShow())
            {
                candidates.Add(ev);
                total += ev.weight;
            }
        }

        if (candidates.Count == 0) return null;

        float roll = UnityEngine.Random.value * total;
        foreach (var ev in candidates)
        {
            roll -= ev.weight;
            if (roll <= 0f) return ev;
        }
        return candidates[candidates.Count - 1];
    }

    private void ShowEvent(GameEvent ev, DayStage stage)
    {
        var gs = GameState.Instance;
        gs.currentLocation = stage.location;
        if (ev.once) gs.AddPermanentFlag(ev.SeenFlag);

        currentEvent = ev;
        StopTimer();
        if (ev.impact) TriggerImpact();

        SetBackground(ev.GetBackdrop() ?? stage.backdrop);
        SetAudio(stage.location);
        PlaySpecialAudio(ev.id, warnIfMissing: false);   // plays only if a clip has this event's id

        // Start with whoever is "present", or nobody.
        // The player appears whenever someone else is on screen.
        playerExpression = ev.presentCharacter == CharacterId.Player ? ev.presentExpression : Expression.Neutral;
        ShowPortrait(ev.presentCharacter, ev.presentExpression);
        ShowPlayer(ev.presentCharacter != CharacterId.None || EventHasPlayerLines(ev));
        SetSpeaker(CharacterId.None, instant: true);

        List<EventChoice> choices = ev.GetAvailableChoices();
        if (choices.Count == 0)
            choices.Add(new EventChoice { label = continueLabel });

        if (choices.Count > choiceButtons.Length)
            Debug.LogWarning($"Event '{ev.id}' has {choices.Count} choices but only {choiceButtons.Length} buttons.");

        PlayLines(ev.lines, choices);
    }

    // ================= DIALOGUE =================

    // Plays lines one at a time. Shows the choices on the last line,
    // or moves to the next event if there are none (used for replies).
    private void PlayLines(List<DialogueLine> lines, List<EventChoice> choicesAtEnd)
    {
        lineQueue.Clear();
        foreach (var line in lines)
            lineQueue.Enqueue(line);

        choicesAfterLines = choicesAtEnd;
        ShowNextLine();
    }

    private void ShowNextLine()
    {
        // Skip lines whose text came out empty (used for conditional lines).
        while (lineQueue.Count > 0)
        {
            DialogueLine line = lineQueue.Dequeue();
            string text = line.Text;
            if (string.IsNullOrEmpty(text)) continue;

            DisplayLine(line, text);

            if (!HasMoreLines() && choicesAfterLines != null)
                ShowChoices(choicesAfterLines);
            else
                ShowContinue();
            return;
        }

        if (choicesAfterLines != null) ShowChoices(choicesAfterLines);
        else ShowNextEvent();
    }

    private bool HasMoreLines()
    {
        foreach (var line in lineQueue)
        {
            if (!string.IsNullOrEmpty(line.Text)) return true;
        }
        return false;
    }

    private void DisplayLine(DialogueLine line, string text)
    {
        switch (line.speaker)
        {
            case CharacterId.None:
                // Narration: no name, keep whoever is on screen.
                eventText.text = text;
                break;

            case CharacterId.Player:
                // You're talking: keep the other character's portrait up.
                eventText.text = FormatDialogue(GetCharacterName(CharacterId.Player, "You"), text);
                playerExpression = line.Expression;
                ShowPlayer(true);
                break;

            default:
                eventText.text = FormatDialogue(GetCharacterName(line.speaker), text);
                ShowPortrait(line.speaker, line.Expression);
                ShowPlayer(true);
                break;
        }

        // Whoever speaks bounces; angry or shocked lines shake too.
        bool intense = line.Expression == Expression.Annoyed || line.Expression == Expression.Surprised;
        if (line.speaker == CharacterId.Player)
        {
            playerPunch = PunchTime;
            playerShake = intense;
        }
        else if (line.speaker != CharacterId.None)
        {
            characterPunch = PunchTime;
            characterShake = intense;
        }

        SetSpeaker(line.speaker);
        StartTyping();
        UpdateHUD();
    }

    private void ShowContinue()
    {
        waitingForContinue = true;
        SetButtons(new List<string> { continueLabel });
    }

    private void ShowChoices(List<EventChoice> choices)
    {
        waitingForContinue = false;
        choicesAfterLines = null;
        currentChoices = choices;
        SetButtons(choices.ConvertAll(c => c.label));

        if (currentEvent != null && currentEvent.timerSeconds > 0f)
            StartTimer(currentEvent.timerSeconds);
    }

    // ================= GAME FEEL =================

    private void StartTyping()
    {
        if (charsPerSecond <= 0f)
        {
            FinishTyping();
            return;
        }

        eventText.ForceMeshUpdate();
        totalChars = eventText.textInfo.characterCount;
        typedChars = 0f;
        eventText.maxVisibleCharacters = 0;
        typing = totalChars > 0;
        if (!typing) FinishTyping();
    }

    private void FinishTyping()
    {
        typing = false;
        eventText.maxVisibleCharacters = int.MaxValue;
    }

    private void StartTimer(float seconds)
    {
        timerActive = true;
        timerLeft = seconds;
        if (timerBar != null)
        {
            timerBar.gameObject.SetActive(true);
            timerBar.fillAmount = 1f;
            timerBar.color = timerCalmColor;
        }
    }

    private void StopTimer()
    {
        timerActive = false;
        if (timerBar != null) timerBar.gameObject.SetActive(false);
    }

    private void TriggerImpact()
    {
        flashTimer = ImpactTime;
        shakeTimer = ImpactTime;
    }

    private void AnimatePortrait(Image image, ref float punch, Vector3 baseScale, Vector2 basePos, bool shake, float dt)
    {
        if (image == null || punch <= 0f) return;

        punch -= dt;
        RectTransform rt = image.rectTransform;

        if (punch <= 0f)
        {
            rt.localScale = baseScale;
            rt.anchoredPosition = basePos;
            return;
        }

        float progress = 1f - punch / PunchTime;
        float bounce = 1f + Mathf.Sin(progress * Mathf.PI) * portraitBounce;
        rt.localScale = new Vector3(baseScale.x * bounce, baseScale.y * bounce, baseScale.z);
        rt.anchoredPosition = shake
            ? basePos + new Vector2(Mathf.Sin(Time.time * 90f) * 7f * (punch / PunchTime), 0f)
            : basePos;
    }

    // Morning, not at work yet, and within 20 minutes of start time (or past it).
    private bool IsRunningLate()
    {
        var gs = GameState.Instance;
        if (gs == null || (summaryPanel != null && summaryPanel.activeSelf)) return false;
        return (int)gs.currentLocation <= (int)Location.CommuteToWork
            && gs.timeMinutes >= gs.workStartMinutes - 20
            && gs.timeMinutes < GameState.TimeOf(12);
    }

    // "+$60   Job -1" in green/red, plus "Joe will remember that." in italics.
    private void ShowFeedback(StatSnapshot before, string note, bool timedOut)
    {
        if (feedbackText == null) return;

        var gs = GameState.Instance;
        var parts = new List<string>();
        AddDelta(parts, gs.money - before.money, "$", isMoney: true);
        AddDelta(parts, gs.standing - before.standing, "Job");
        AddDelta(parts, gs.energy - before.energy, "Energy");
        AddDelta(parts, gs.mood - before.mood, "Mood");

        var sb = new StringBuilder();
        if (timedOut) sb.AppendLine("<i>You hesitated...</i>");
        if (parts.Count > 0) sb.AppendLine(string.Join("    ", parts));
        if (!string.IsNullOrEmpty(note)) sb.AppendLine($"<i>{note}</i>");
        if (sb.Length == 0) return;

        feedbackText.text = sb.ToString().TrimEnd();
        feedbackText.alpha = 1f;
        feedbackTimer = feedbackDuration;
    }

    private void AddDelta(List<string> parts, int delta, string label, bool isMoney = false)
    {
        if (delta == 0) return;

        string hex = ColorUtility.ToHtmlStringRGB(delta > 0 ? gainColor : lossColor);
        string sign = delta > 0 ? "+" : "-";
        string body = isMoney ? $"{sign}${Math.Abs(delta)}" : $"{label} {sign}{Math.Abs(delta)}";
        parts.Add($"<color=#{hex}>{body}</color>");
    }

    private void SetButtons(List<string> labels)
    {
        for (int i = 0; i < choiceButtons.Length; i++)
        {
            bool used = i < labels.Count;
            choiceButtons[i].gameObject.SetActive(used);

            if (used)
                choiceButtons[i].GetComponentInChildren<TMP_Text>().text = labels[i];
        }
    }

    // ================= CHARACTERS =================

    private CharacterData FindCharacter(CharacterId id)
    {
        foreach (var c in characters)
        {
            if (c.id == id) return c;
        }
        return null;
    }

    private string GetCharacterName(CharacterId id, string fallback = null)
    {
        CharacterData data = FindCharacter(id);
        if (data != null && !string.IsNullOrEmpty(data.displayName)) return data.displayName;
        return fallback ?? id.ToString();
    }

    private bool EventHasPlayerLines(GameEvent ev)
    {
        foreach (var line in ev.lines)
        {
            if (line.speaker == CharacterId.Player) return true;
        }
        foreach (var choice in ev.choices)
        {
            foreach (var reply in choice.replies)
            {
                if (reply.speaker == CharacterId.Player) return true;
            }
        }
        return false;
    }

    // Shows or hides the player's portrait, using their current expression.
    private void ShowPlayer(bool show)
    {
        if (playerImage == null) return;

        CharacterData data = FindCharacter(CharacterId.Player);
        Sprite sprite = show && data != null ? data.GetSprite(playerExpression) : null;

        playerImage.gameObject.SetActive(sprite != null);
        if (sprite != null) playerImage.sprite = sprite;
    }

    // Lights up whoever is talking and shadows everyone else.
    // Narration (None) shadows both, so the text reads as the focus.
    private void SetSpeaker(CharacterId speaker, bool instant = false)
    {
        bool playerSpeaking = speaker == CharacterId.Player;
        bool otherSpeaking = speaker != CharacterId.None && !playerSpeaking;

        // With only one portrait on screen and no one talking, keep it lit.
        bool onlyOneVisible = (characterImage == null || !characterImage.gameObject.activeSelf)
                            ^ (playerImage == null || !playerImage.gameObject.activeSelf);
        bool narration = speaker == CharacterId.None;

        characterTargetColor = otherSpeaking || (narration && onlyOneVisible) ? speakingColor : listeningColor;
        playerTargetColor = playerSpeaking || (narration && onlyOneVisible) ? speakingColor : listeningColor;

        if (instant)
        {
            if (characterImage != null) characterImage.color = characterTargetColor;
            if (playerImage != null) playerImage.color = playerTargetColor;
        }
    }

    // Builds "Name: line" with the name bold and colored.
    private string FormatDialogue(string speakerName, string text)
    {
        string hex = ColorUtility.ToHtmlStringRGB(speakerNameColor);
        return $"<b><color=#{hex}>{speakerName}:</color></b> {text}";
    }

    private void ShowPortrait(CharacterId id, Expression expression)
    {
        if (characterImage == null) return;

        CharacterData data = (id == CharacterId.None || id == CharacterId.Player) ? null : FindCharacter(id);
        Sprite sprite = data != null ? data.GetSprite(expression) : null;

        characterImage.gameObject.SetActive(sprite != null);
        if (sprite != null) characterImage.sprite = sprite;
    }

    private void HideCharacter()
    {
        if (characterImage != null) characterImage.gameObject.SetActive(false);
        if (playerImage != null) playerImage.gameObject.SetActive(false);
    }

    // ================= BACKGROUND =================

    private void SetBackground(Backdrop backdrop)
    {
        if (backgroundImage == null) return;

        // Try the exact backdrop, then its fallback (e.g. no CarDay image -> use CarEvening).
        Backdrop? current = backdrop;
        while (current.HasValue)
        {
            Sprite sprite = FindBackground(current.Value);
            if (sprite != null)
            {
                backgroundImage.sprite = sprite;
                return;
            }
            current = FallbackFor(current.Value);
        }

        Debug.LogWarning($"No background assigned for {backdrop}");
    }

    private Sprite FindBackground(Backdrop backdrop)
    {
        foreach (var bg in backgrounds)
        {
            if (bg.backdrop == backdrop && bg.sprite != null) return bg.sprite;
        }
        return null;
    }

    // Similar image to use when a backdrop has no sprite assigned.
    private static Backdrop? FallbackFor(Backdrop backdrop)
    {
        switch (backdrop)
        {
            case Backdrop.CarDay: return Backdrop.CarEvening;
            case Backdrop.HomeEvening: return Backdrop.Home;
            case Backdrop.BusEvening: return Backdrop.BusDay;
            case Backdrop.OutsideHomeEvening: return Backdrop.OutsideHome;
            case Backdrop.OutsideWorkEvening: return Backdrop.OutsideWork;
            default: return null;
        }
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
    // Call from an event choice, e.g. EventUI.Instance.PlaySpecialAudio("CarHorn")
    public void PlaySpecialAudio(string id, bool warnIfMissing = true)
    {
        if (specialAudioSource == null || specialAudioClips == null) return;

        var audioClip = specialAudioClips.Find(c => c.id == id);
        if (audioClip == null || audioClip.clip == null)
        {
            if (warnIfMissing) Debug.LogWarning($"No special audio clip with id {id}");
            return;
        }

        specialAudioSource.PlayOneShot(audioClip.clip);
    }

    // ================= HUD =================

    private void UpdateHUD()
    {
        var gs = GameState.Instance;

        if (timeText != null)
            timeText.text = $"{gs.DayName}  {gs.GetTimeString()}";

        if (statusText != null)
            statusText.text = $"Energy: {gs.energy}   Mood: {gs.mood}   ${gs.money}   Job: {gs.standing}/10";
    }

    // ================= SUMMARIES =================

    private void ShowDaySummary()
    {
        var gs = GameState.Instance;
        if (!inEpilogue) gs.EndDay();   // the epilogue already did this on the last day

        FinishTyping();
        StopTimer();

        var sb = new StringBuilder();

        if (gs.IsLastDay)
        {
            BuildWeekSummary(sb);
            if (summaryButtonText != null) summaryButtonText.text = "Play again";
        }
        else
        {
            sb.AppendLine($"<b>{gs.DayName} is over.</b>");
            if (gs.HasFlag("Jailed")) sb.AppendLine("You spent the night in a jail cell.");
            else sb.AppendLine(gs.HasFlag("LateToWork") ? "You were late to work." : "You made it to work on time.");
            sb.AppendLine($"Energy: {gs.energy}   Mood: {gs.mood}");
            sb.AppendLine($"Money: ${gs.money}   Job standing: {gs.standing}/10");
            if (gs.standing <= 2) sb.AppendLine("<color=#FF6B6B>Your job is hanging by a thread.</color>");
            if (gs.money <= 10) sb.AppendLine("<color=#FF6B6B>You're almost out of money.</color>");
            sb.AppendLine();

            foreach (string entry in gs.GetHistoryForDay(gs.currentDay))
                sb.AppendLine(entry);

            if (summaryButtonText != null) summaryButtonText.text = "Next day";
        }

        summaryText.text = sb.ToString();

        HideCharacter();
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
        bool fired = gs.HasPermanentFlag("Fired");
        bool arrested = gs.HasPermanentFlag("Arrested");

        sb.AppendLine(fired ? $"<b>You were fired on {gs.DayName}.</b>" : "<b>The week is over.</b>");
        sb.AppendLine();

        foreach (string recap in gs.GetDayRecaps())
            sb.AppendLine(recap);

        sb.AppendLine();
        sb.AppendLine($"Days late: {late}   Productive days: {productive}");
        sb.AppendLine($"Job standing: {gs.standing}/10   Money: ${gs.money}");
        sb.AppendLine($"Final energy: {gs.energy}   Final mood: {gs.mood}");
        sb.AppendLine();

        // ---------- What your choices led to ----------
        if (friendship >= 2) sb.AppendLine("You and your neighbor are becoming friends.");
        else if (friendship <= -2) sb.AppendLine("Your neighbor has stopped waving.");

        if (gs.GetCount("CoworkerFriendship") >= 3) sb.AppendLine("Your coworker considers you a real friend.");
        if (gs.GetCount("CoffeeVisits") >= 3) sb.AppendLine("The barista knows your order by heart.");

        if (gs.HasPermanentFlag("HasDog")) sb.AppendLine("You adopted a scruffy stray. Best decision of the week.");
        else if (gs.HasPermanentFlag("DogGone")) sb.AppendLine("The stray dog found someone else to follow.");

        if (gs.HasPermanentFlag("VagrantHoused")) sb.AppendLine("Joe got a bed at the shelter, partly thanks to you.");
        else if (gs.GetCount("VagrantKindness") >= 2) sb.AppendLine("Joe always saves you a seat on the bus now.");

        if (gs.HasPermanentFlag("PetitionSigned")) sb.AppendLine("Route 9 was saved. Your signature counted.");
        else if (gs.HasPermanentFlag("MetPetitioner")) sb.AppendLine("Route 9 was cut. You had your chance to sign.");

        if (gs.HasPermanentFlag("SavedBoth")) sb.AppendLine("You fought corporate and saved both jobs.");
        else if (gs.HasPermanentFlag("CoworkerSacrifice")) sb.AppendLine("You took the fall so your coworker could keep their job.");
        else if (gs.HasPermanentFlag("BetrayedCoworker")) sb.AppendLine("You threw your coworker under the bus.");
        else if (gs.HasPermanentFlag("LaidOff")) sb.AppendLine("You were laid off on Friday.");
        else if (gs.HasPermanentFlag("CoworkerLaidOff")) sb.AppendLine("Your coworker was laid off on Friday.");

        if (gs.HasPermanentFlag("Promoted")) sb.AppendLine("You took your coworker's job, their account and a $150 raise.");
        if (gs.HasPermanentFlag("TookCredit")) sb.AppendLine("You took credit for your coworker's idea.");
        if (gs.HasPermanentFlag("PaddedExpenses")) sb.AppendLine("You padded your expense report and got away with it.");
        else if (gs.HasPermanentFlag("CaughtPadding")) sb.AppendLine("You got caught padding your expense report.");
        if (gs.HasPermanentFlag("KeptWallet")) sb.AppendLine("You kept your neighbor's rent money.");

        if (gs.HasPermanentFlag("ShowdownPaidOff")) sb.AppendLine("Friday night, you paid the man in the leather jacket to go away.");
        else if (gs.HasPermanentFlag("ShowdownWon")) sb.AppendLine($"Friday night, {gs.GetCount("ShowdownAllies")} people showed up to help you. He was arrested.");
        else if (gs.HasPermanentFlag("ShowdownEscaped")) sb.AppendLine("Friday night, you got away from the man in the leather jacket.");
        else if (gs.HasPermanentFlag("ShowdownLost") || gs.HasPermanentFlag("ShowdownPaid")) sb.AppendLine("Friday night, you faced him alone and lost.");
        else if (gs.HasPermanentFlag("ThugCaught")) sb.AppendLine("The mugger is behind bars thanks to your tip.");
        if (arrested) sb.AppendLine("You now have an arrest on your record.");
        if (gs.HasPermanentFlag("PresentationWin")) sb.AppendLine("You nailed the client presentation.");
        else if (gs.HasPermanentFlag("PresentationFail")) sb.AppendLine("The client presentation still haunts you.");

        sb.AppendLine();

        // ---------- Ending ----------
        var ending = DayEvents.GetEnding();
        sb.AppendLine($"<b>Ending: {ending.title}.</b> {ending.text}");

        // ---------- Endings gallery (saved between sessions) ----------
        string key = "Ending_" + ending.title;
        bool isNew = PlayerPrefs.GetInt(key, 0) == 0;
        PlayerPrefs.SetInt(key, 1);
        PlayerPrefs.Save();

        int found = 0;
        var names = new List<string>();
        foreach (string title in DayEvents.AllEndingTitles)
        {
            bool unlocked = PlayerPrefs.GetInt("Ending_" + title, 0) == 1;
            if (unlocked) found++;
            if (!unlocked) names.Add("???");
            else if (title == ending.title) names.Add($"<b>{title}</b>" + (isNew ? " <color=#FFD966>(new!)</color>" : ""));
            else names.Add(title);
        }

        sb.AppendLine();
        sb.AppendLine($"<b>Endings found: {found} / {DayEvents.AllEndingTitles.Length}</b>");
        sb.AppendLine(string.Join("  ·  ", names));
    }

    // Right-click the EventUI component in the Inspector to wipe the gallery while testing.
    [ContextMenu("Reset Endings Gallery")]
    private void ResetEndingsGallery()
    {
        foreach (string title in DayEvents.AllEndingTitles)
            PlayerPrefs.DeleteKey("Ending_" + title);
        PlayerPrefs.Save();
        Debug.Log("Endings gallery reset.");
    }
}