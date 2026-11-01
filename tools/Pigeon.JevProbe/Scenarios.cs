namespace Pigeon.JevProbe;

/// <summary>Трейты ТЗ §5.2, шкала 0..100.</summary>
internal static class Traits
{
    public const string Curiosity = "curiosity";
    public const string Playfulness = "playfulness";
    public const string Sociability = "sociability";
    public const string Boldness = "boldness";
    public const string Calmness = "calmness";
    public const string Independence = "independence";
    public const string Energy = "energy";
    public const string Mischief = "mischief";
    public const string Greed = "greed";
    public const string Territoriality = "territoriality";

    public static readonly string[] All =
    [
        Curiosity, Playfulness, Sociability, Boldness, Calmness,
        Independence, Energy, Mischief, Greed, Territoriality,
    ];
}

/// <summary>Потребности ТЗ §6.1: срочность 0..1.</summary>
internal static class Needs
{
    public const string Fatigue = "fatigue";
    public const string Boredom = "boredom";
    public const string Attention = "attention";
    public const string Hunger = "hunger";
    public const string ToiletPressure = "toilet_pressure";

    public static readonly string[] All = [Fatigue, Boredom, Attention, Hunger, ToiletPressure];
}

/// <summary>Намерения из каталога ТЗ §8.1, которые встречаются в сценариях.</summary>
internal enum Intent
{
    Rest,
    Sleep,
    Wander,
    Explore,
    Observe,
    Perch,
    SeekAttention,
    FollowTarget,
    Play,
    Eat,
    Groom,
    Poop,
    Mischief,
    Hide,
    Flee,
    Relocate,
    LeaveOffscreen,
}

internal sealed record Archetype(string Key, string Description, IReadOnlyDictionary<string, int> Traits);

/// <param name="Key">Ключ варианта в вопросе <c>choice</c>: имя, а не индекс.</param>
/// <param name="Attractiveness">Привлекательность цели для Utility AI, 0..1.</param>
internal sealed record IntentOption(string Key, Intent Intent, string Description, double Attractiveness);

/// <param name="UnusualEvent">Код необычного события: задаёт вопрос <c>react</c> (noul).</param>
/// <param name="SignificantEvent">Код важного события: задаёт вопрос <c>remember</c> (score).</param>
internal sealed record Situation(
    string Key,
    string Summary,
    IReadOnlyDictionary<string, double> Needs,
    string Mood,
    string TimeOfDay,
    UserContext User,
    IReadOnlyList<string> RecentEvents,
    string? UnusualEvent,
    string? SignificantEvent,
    IReadOnlyList<IntentOption> Options);

/// <summary>
/// Что пользователь делает: сырые поля как в Desktop World. Имя приложения и заголовок окна
/// нужны, чтобы privacy-фильтр было что вырезать (ТЗ §14).
/// </summary>
internal sealed record UserContext(
    bool Present,
    int IdleMinutes,
    string AppName,
    string WindowTitle,
    string AppCategory,
    bool Fullscreen);

internal sealed record Scenario(Archetype Archetype, Situation Situation)
{
    public string Key => $"{Archetype.Key}/{Situation.Key}";
}

/// <summary>10 архетипов × 10 ситуаций (MVP-0, S0.10).</summary>
internal static class ScenarioCatalog
{
    public static readonly IReadOnlyList<Archetype> Archetypes =
    [
        Make("explorer", "Curious and bold, always checks out anything new",
            curiosity: 90, playfulness: 55, sociability: 45, boldness: 85, calmness: 50,
            independence: 65, energy: 70, mischief: 40, greed: 40, territoriality: 30),
        Make("couch_potato", "Low energy, calm, loves warm still spots",
            curiosity: 25, playfulness: 30, sociability: 50, boldness: 45, calmness: 85,
            independence: 50, energy: 10, mischief: 20, greed: 60, territoriality: 55),
        Make("clingy", "Very sociable, depends on the user's attention",
            curiosity: 50, playfulness: 60, sociability: 95, boldness: 50, calmness: 45,
            independence: 10, energy: 55, mischief: 30, greed: 50, territoriality: 40),
        Make("loner", "Independent, keeps distance from the user",
            curiosity: 55, playfulness: 30, sociability: 10, boldness: 60, calmness: 65,
            independence: 95, energy: 50, mischief: 35, greed: 45, territoriality: 60),
        Make("prankster", "Playful troublemaker, loves small surprises",
            curiosity: 65, playfulness: 85, sociability: 60, boldness: 70, calmness: 35,
            independence: 55, energy: 75, mischief: 95, greed: 50, territoriality: 40),
        Make("coward", "Timid and jumpy, avoids anything unusual",
            curiosity: 30, playfulness: 40, sociability: 55, boldness: 5, calmness: 15,
            independence: 40, energy: 50, mischief: 20, greed: 50, territoriality: 35),
        Make("glutton", "Greedy, food comes first",
            curiosity: 45, playfulness: 45, sociability: 50, boldness: 60, calmness: 50,
            independence: 50, energy: 45, mischief: 40, greed: 95, territoriality: 50),
        Make("guardian", "Territorial, defends favourite perches",
            curiosity: 40, playfulness: 35, sociability: 40, boldness: 75, calmness: 55,
            independence: 60, energy: 55, mischief: 30, greed: 50, territoriality: 95),
        Make("hyper", "Restless, energetic and easily excited",
            curiosity: 70, playfulness: 80, sociability: 65, boldness: 60, calmness: 5,
            independence: 45, energy: 95, mischief: 55, greed: 55, territoriality: 40),
        Make("balanced", "Average in every trait",
            curiosity: 50, playfulness: 50, sociability: 50, boldness: 50, calmness: 50,
            independence: 50, energy: 50, mischief: 50, greed: 50, territoriality: 50),
    ];

    public static readonly IReadOnlyList<Situation> Situations =
    [
        new("quiet_morning", "Morning, user is working in an IDE, nothing is happening",
            NeedsOf(fatigue: 0.15, boredom: 0.55, attention: 0.35, hunger: 0.2, toilet: 0.1),
            "Content", "morning",
            new UserContext(true, 0, "Code.exe", "PaymentService.cs — billing-core", "ide", false),
            [], null, null,
            [
                Option("wander_taskbar", Intent.Wander, "Stroll along the taskbar without a goal.", 0.6),
                Option("explore_new_window", Intent.Explore, "Walk over to the window that opened a minute ago and inspect it.", 0.7),
                Option("observe_cursor", Intent.Observe, "Sit and watch the cursor move.", 0.6),
                Option("perch_window_top", Intent.Perch, "Perch on the top edge of the active window.", 0.5),
                Option("groom_in_place", Intent.Groom, "Groom feathers where it stands.", 0.5),
                Option("seek_attention_user", Intent.SeekAttention, "Walk near the cursor and coo to get the user's attention.", 0.4),
            ]),
        new("tired_evening", "Evening, the pigeon has been active all day, user idle for 20 minutes",
            NeedsOf(fatigue: 0.85, boredom: 0.3, attention: 0.4, hunger: 0.35, toilet: 0.2),
            "Sleepy", "evening",
            new UserContext(true, 20, "chrome.exe", "Inbox (3) — mail", "browser", false),
            [], null, null,
            [
                Option("sleep_taskbar_corner", Intent.Sleep, "Fall asleep in the warm corner of the taskbar.", 0.8),
                Option("rest_window_top", Intent.Rest, "Sit still on top of the browser window and doze.", 0.6),
                Option("groom_in_place", Intent.Groom, "Groom feathers before settling down.", 0.5),
                Option("wander_desktop", Intent.Wander, "Keep strolling around the screen edge.", 0.4),
                Option("seek_attention_user", Intent.SeekAttention, "Coo at the idle user to wake them up.", 0.3),
            ]),
        new("user_returned", "User came back after two days away",
            NeedsOf(fatigue: 0.2, boredom: 0.6, attention: 0.7, hunger: 0.4, toilet: 0.2),
            "Excited", "afternoon",
            new UserContext(true, 0, "explorer.exe", "C:\\Users\\anna\\Documents\\taxes-2026", "file_manager", false),
            ["user_returned_after_long_absence"], null, "user_returned_after_long_absence",
            [
                Option("seek_attention_user", Intent.SeekAttention, "Run up to the cursor, coo and puff up.", 0.8),
                Option("follow_cursor", Intent.FollowTarget, "Follow the cursor around the screen.", 0.6),
                Option("play_cursor", Intent.Play, "Hop and try to catch the cursor.", 0.6),
                Option("ignore_user_groom", Intent.Groom, "Demonstratively groom and ignore the user for a while.", 0.4),
                Option("leave_offscreen", Intent.LeaveOffscreen, "Fly off the screen edge in a huff and come back later.", 0.3),
                Option("perch_window_top", Intent.Perch, "Perch on the window top and watch the user from above.", 0.5),
            ]),
        new("crumbs_dropped", "User dropped crumbs near the taskbar",
            NeedsOf(fatigue: 0.3, boredom: 0.4, attention: 0.3, hunger: 0.6, toilet: 0.15),
            "Happy", "noon",
            new UserContext(true, 1, "Teams.exe", "Weekly sync — Teams", "chat", false),
            ["crumbs_dropped_by_user"], null, null,
            [
                Option("eat_crumbs", Intent.Eat, "Walk to the crumbs and peck them.", 0.9),
                Option("observe_crumbs", Intent.Observe, "Watch the crumbs from a distance first.", 0.5),
                Option("seek_attention_user", Intent.SeekAttention, "Coo at the user as a thank-you.", 0.4),
                Option("mischief_scatter_crumbs", Intent.Mischief, "Kick the crumbs around the taskbar.", 0.4),
                Option("rest_in_place", Intent.Rest, "Stay put and rest.", 0.3),
            ]),
        new("window_shaken", "The window the pigeon perched on was suddenly dragged and shaken",
            NeedsOf(fatigue: 0.3, boredom: 0.3, attention: 0.3, hunger: 0.3, toilet: 0.2),
            "Startled", "afternoon",
            new UserContext(true, 0, "WINWORD.EXE", "Divorce settlement draft.docx", "office", false),
            ["perch_window_shaken"], "perch_window_shaken", null,
            [
                Option("flee_offscreen", Intent.Flee, "Flap away to the far side of the screen.", 0.6),
                Option("hide_behind_taskbar", Intent.Hide, "Hide behind the taskbar for a while.", 0.6),
                Option("perch_back_same_window", Intent.Perch, "Land back on the same window as if nothing happened.", 0.5),
                Option("observe_window", Intent.Observe, "Land nearby and stare at the window suspiciously.", 0.5),
                Option("mischief_peck_window", Intent.Mischief, "Return and peck the window edge in revenge.", 0.3),
            ]),
        new("cursor_circling", "User is idly circling the cursor near the pigeon",
            NeedsOf(fatigue: 0.25, boredom: 0.65, attention: 0.5, hunger: 0.3, toilet: 0.2),
            "Playful", "afternoon",
            new UserContext(true, 0, "slack.exe", "#random — Slack", "chat", false),
            ["cursor_circling_near_pigeon"], "cursor_circling_near_pigeon", null,
            [
                Option("play_catch_cursor", Intent.Play, "Hop after the cursor and try to catch it.", 0.8),
                Option("follow_cursor", Intent.FollowTarget, "Walk after the cursor at a polite distance.", 0.6),
                Option("observe_cursor", Intent.Observe, "Sit and follow the cursor with its head only.", 0.5),
                Option("flee_cursor", Intent.Flee, "Fly away from the cursor.", 0.3),
                Option("ignore_cursor_groom", Intent.Groom, "Ignore the cursor and groom.", 0.4),
                Option("mischief_sit_on_cursor", Intent.Mischief, "Plop down right where the cursor is heading.", 0.4),
            ]),
        new("new_monitor", "A second monitor was just connected",
            NeedsOf(fatigue: 0.2, boredom: 0.5, attention: 0.3, hunger: 0.25, toilet: 0.1),
            "Content", "morning",
            new UserContext(true, 0, "Code.exe", "secrets.env — infra", "ide", false),
            ["monitor_connected"], "monitor_connected", "first_multi_monitor_transition",
            [
                Option("relocate_new_monitor", Intent.Relocate, "Fly over to the new monitor and claim it.", 0.7),
                Option("explore_new_monitor_edge", Intent.Explore, "Walk to the edge between monitors and peek over.", 0.7),
                Option("observe_from_perch", Intent.Observe, "Watch the new monitor from the current perch.", 0.5),
                Option("perch_favourite", Intent.Perch, "Stay on the favourite perch on the main monitor.", 0.5),
                Option("hide_behind_taskbar", Intent.Hide, "Hide until the change settles.", 0.2),
            ]),
        new("fullscreen_video", "User is watching a fullscreen video (quiet mode, unobtrusive options only)",
            NeedsOf(fatigue: 0.4, boredom: 0.6, attention: 0.55, hunger: 0.3, toilet: 0.2),
            "Bored", "evening",
            new UserContext(true, 0, "vlc.exe", "Private holiday 2026.mp4", "video", true),
            [], null, null,
            [
                Option("rest_screen_corner", Intent.Rest, "Sit still in the screen corner.", 0.6),
                Option("observe_video", Intent.Observe, "Quietly watch the video from the corner.", 0.6),
                Option("groom_in_place", Intent.Groom, "Groom quietly.", 0.5),
                Option("leave_offscreen", Intent.LeaveOffscreen, "Quietly fly off the screen and come back later.", 0.4),
            ]),
        new("late_night_toilet", "Late night, user away, the pigeon ate a lot an hour ago",
            NeedsOf(fatigue: 0.6, boredom: 0.3, attention: 0.45, hunger: 0.1, toilet: 0.8),
            "Grumpy", "night",
            new UserContext(false, 95, "explorer.exe", "Program Manager", "none", false),
            [], null, null,
            [
                Option("poop_taskbar_corner", Intent.Poop, "Poop in a discreet corner of the taskbar.", 0.6),
                Option("poop_on_active_window", Intent.Poop, "Poop right on top of the active window edge.", 0.5),
                Option("leave_offscreen", Intent.LeaveOffscreen, "Fly off the screen to take care of business.", 0.5),
                Option("sleep_taskbar_corner", Intent.Sleep, "Ignore it and go to sleep.", 0.4),
                Option("wander_desktop", Intent.Wander, "Wander around restlessly.", 0.3),
            ]),
        new("favourite_perch_gone", "The window with the pigeon's favourite perch was closed",
            NeedsOf(fatigue: 0.35, boredom: 0.45, attention: 0.4, hunger: 0.3, toilet: 0.2),
            "Grumpy", "afternoon",
            new UserContext(true, 2, "Spotify.exe", "Sad songs playlist", "music", false),
            ["favourite_perch_window_closed"], "favourite_perch_window_closed", "lost_favourite_perch",
            [
                Option("perch_new_window", Intent.Perch, "Pick the nearest window top as a new perch.", 0.6),
                Option("mischief_sit_on_active", Intent.Mischief, "Sit on the active window in protest.", 0.5),
                Option("seek_attention_complain", Intent.SeekAttention, "Walk to the cursor and complain loudly.", 0.5),
                Option("explore_for_perch", Intent.Explore, "Explore the screen for a better perch.", 0.6),
                Option("relocate_other_area", Intent.Relocate, "Move to a different part of the screen entirely.", 0.4),
                Option("rest_taskbar", Intent.Rest, "Sulk on the taskbar.", 0.4),
            ]),
    ];

    public static IReadOnlyList<Scenario> All { get; } =
        [.. Archetypes.SelectMany(a => Situations.Select(s => new Scenario(a, s)))];

    private static Archetype Make(
        string key, string description,
        int curiosity, int playfulness, int sociability, int boldness, int calmness,
        int independence, int energy, int mischief, int greed, int territoriality) =>
        new(key, description, new Dictionary<string, int>
        {
            [Traits.Curiosity] = curiosity,
            [Traits.Playfulness] = playfulness,
            [Traits.Sociability] = sociability,
            [Traits.Boldness] = boldness,
            [Traits.Calmness] = calmness,
            [Traits.Independence] = independence,
            [Traits.Energy] = energy,
            [Traits.Mischief] = mischief,
            [Traits.Greed] = greed,
            [Traits.Territoriality] = territoriality,
        });

    private static Dictionary<string, double> NeedsOf(double fatigue, double boredom, double attention, double hunger, double toilet) =>
        new()
        {
            [Needs.Fatigue] = fatigue,
            [Needs.Boredom] = boredom,
            [Needs.Attention] = attention,
            [Needs.Hunger] = hunger,
            [Needs.ToiletPressure] = toilet,
        };

    private static IntentOption Option(string key, Intent intent, string description, double attractiveness) =>
        new(key, intent, description, attractiveness);
}
