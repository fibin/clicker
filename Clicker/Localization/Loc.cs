using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;

namespace Clicker.Localization;

/// <summary>
/// Runtime-switchable translations. XAML binds to the indexer (via {loc:Tr Key}),
/// so changing <see cref="Language"/> updates every text immediately.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string Ukrainian = "uk";
    public const string English = "en";

    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        [Ukrainian] = new()
        {
            ["AppTitle"] = "Автоклікер",
            ["Language"] = "Мова",
            ["StatusRunning"] = "Працює",
            ["StatusStopped"] = "Зупинено",
            ["ClickCount"] = "Кліків: {0}",
            ["Interval"] = "Інтервал між кліками (мс)",
            ["ClicksPerSecond"] = "≈ {0} кліків/с",
            ["InvalidInterval"] = "Введіть ціле число від {0} до {1}",
            ["MouseButton"] = "Кнопка миші",
            ["Left"] = "Ліва",
            ["Right"] = "Права",
            ["Middle"] = "Середня",
            ["Hotkey"] = "Гаряча клавіша старт / стоп",
            ["Change"] = "Змінити",
            ["Cancel"] = "Скасувати",
            ["PressKey"] = "Натисніть клавішу… (Esc — скасувати)",
            ["HotkeyHint"] = "Гарячі клавіші працюють навіть коли вікно гри активне.",
            ["Start"] = "Старт",
            ["Stop"] = "Стоп",
            ["RunAsAdmin"] = "Перезапустити від імені адміністратора",
            ["AdminHint"] = "Якщо гра запущена від адміністратора, Windows блокує кліки та гарячу клавішу від звичайних програм.",
            ["RunningAsAdmin"] = "Запущено від імені адміністратора",
            ["TrayShow"] = "Відкрити",
            ["TrayExit"] = "Вийти",
            ["TrayHint"] = "Програма працює у треї. Гаряча клавіша: {0}",
            ["TrayTooltip"] = "Автоклікер — {0}",
            ["HookError"] = "Не вдалося зареєструвати глобальну гарячу клавішу:\n{0}",
            ["AlreadyRunning"] = "Автоклікер уже запущено — шукайте його іконку в треї.",
            ["Mouse4"] = "Миша 4",
            ["Mouse5"] = "Миша 5",
            ["MouseMiddle"] = "Середня кнопка миші",
            ["Points"] = "Точки на екрані",
            ["RecordHotkey"] = "Гаряча клавіша «запам'ятати точку»",
            ["PointsEmpty"] = "Наведіть курсор на потрібне місце та натисніть {0} — точку буде збережено.",
            ["PointsHint"] = "Увімкненою може бути лише одна точка. Якщо всі вимкнено — клік у поточній позиції курсора.",
            ["MoveEachClick"] = "Повертати курсор у точку перед кожним кліком",
            ["PointName"] = "Точка {0}",
            ["DeletePoint"] = "Видалити",
            ["ActivatePoint"] = "Клікати в цю точку",
            ["TargetPoint"] = "Ціль: {0} ({1})",
            ["TargetCursor"] = "Ціль: поточна позиція курсора",
            ["PointSaved"] = "Збережено «{0}»: {1}",
            ["HotkeyInUse"] = "Ця комбінація вже зайнята іншою дією.",
            ["TabClicker"] = "Автоклік",
            ["TabPatterns"] = "Паттерни",
            ["TabHotkeys"] = "Гарячі клавіші",
            ["TabOverlay"] = "Оверлей",
            ["OverlayEnabled"] = "Показувати оверлей стану",
            ["OverlayShowWhenStopped"] = "Показувати також, коли все вимкнено",
            ["OverlayOpacity"] = "Непрозорість",
            ["OverlaySize"] = "Розмір",
            ["OverlayBlink"] = "Мигати, коли працює",
            ["BlinkSpeed"] = "Швидкість мигання",
            ["BlinkSlow"] = "Повільно",
            ["BlinkNormal"] = "Нормально",
            ["BlinkFast"] = "Швидко",
            ["MoveOverlay"] = "Перемістити оверлей",
            ["MoveOverlayDone"] = "Готово — закріпити",
            ["ResetOverlayPosition"] = "Скинути позицію",
            ["OverlayMoveHint"] = "Натисніть «Перемістити», перетягніть плашку мишею в потрібне місце і натисніть «Готово».",
            ["OverlayHint"] = "Оверлей прозорий для миші — кліки проходять крізь нього в гру. Поверх гри в ексклюзивному повноекранному режимі його не видно: перемкніть гру у «вікно без рамки» (borderless).",
            ["OverlayStopped"] = "Вимкнено",
            ["OverlayClicking"] = "Автоклік",
            ["OverlayPlaying"] = "Паттерн",
            ["OverlayRecording"] = "Запис",
            ["PatternHotkey"] = "Гаряча клавіша «запис паттерна»",
            ["Patterns"] = "Записані паттерни",
            ["PatternsEmpty"] = "Натисніть {0}, виконайте потрібні дії мишею та клавіатурою, потім натисніть {0} ще раз — паттерн збережеться.",
            ["PatternsHint"] = "Коли паттерн увімкнено, {0} відтворює його замість автокліку. Увімкненим може бути лише один.",
            ["PatternName"] = "Паттерн {0}",
            ["PatternInfo"] = "{0} с · подій: {1}",
            ["RecordPattern"] = "Записати паттерн",
            ["StopRecording"] = "Зупинити запис",
            ["Repeats"] = "Повторів (0 = безкінечно)",
            ["LoopDelay"] = "Пауза між повторами (мс)",
            ["RecordKeyboard"] = "Записувати також клавіатуру",
            ["StatusRecording"] = "Запис паттерна",
            ["StatusPlaying"] = "Відтворення паттерна",
            ["RecordingProgress"] = "{0} с · подій: {1}",
            ["LoopProgress"] = "Повтор {0} з {1}",
            ["LoopProgressEndless"] = "Повтор {0}",
            ["TargetPattern"] = "Режим: паттерн «{0}»",
            ["NothingRecorded"] = "Нічого не записано.",
            ["PatternSaved"] = "Паттерн «{0}» збережено: {1}",
            ["InvalidNumber"] = "Введіть ціле число від 0 до {0}",
            ["PatternWarning"] = "Паттерн відтворює рухи й кліки за абсолютними координатами екрана — не рухайте вікно гри між записом і відтворенням.",
        },
        [English] = new()
        {
            ["AppTitle"] = "Auto Clicker",
            ["Language"] = "Language",
            ["StatusRunning"] = "Running",
            ["StatusStopped"] = "Stopped",
            ["ClickCount"] = "Clicks: {0}",
            ["Interval"] = "Click interval (ms)",
            ["ClicksPerSecond"] = "≈ {0} clicks/s",
            ["InvalidInterval"] = "Enter a whole number from {0} to {1}",
            ["MouseButton"] = "Mouse button",
            ["Left"] = "Left",
            ["Right"] = "Right",
            ["Middle"] = "Middle",
            ["Hotkey"] = "Start / stop hotkey",
            ["Change"] = "Change",
            ["Cancel"] = "Cancel",
            ["PressKey"] = "Press a key… (Esc to cancel)",
            ["HotkeyHint"] = "Hotkeys work even while a game window is active.",
            ["Start"] = "Start",
            ["Stop"] = "Stop",
            ["RunAsAdmin"] = "Restart as administrator",
            ["AdminHint"] = "If the game runs as administrator, Windows blocks clicks and the hotkey from normal programs.",
            ["RunningAsAdmin"] = "Running as administrator",
            ["TrayShow"] = "Open",
            ["TrayExit"] = "Exit",
            ["TrayHint"] = "The app keeps running in the tray. Hotkey: {0}",
            ["TrayTooltip"] = "Auto Clicker — {0}",
            ["HookError"] = "Could not register the global hotkey:\n{0}",
            ["AlreadyRunning"] = "Auto Clicker is already running — look for its icon in the tray.",
            ["Mouse4"] = "Mouse 4",
            ["Mouse5"] = "Mouse 5",
            ["MouseMiddle"] = "Middle mouse button",
            ["Points"] = "Screen points",
            ["RecordHotkey"] = "\"Save point\" hotkey",
            ["PointsEmpty"] = "Hover over a spot and press {0} to save it as a point.",
            ["PointsHint"] = "Only one point can be on. If all are off, clicks go to the current cursor position.",
            ["MoveEachClick"] = "Move the cursor back to the point before every click",
            ["PointName"] = "Point {0}",
            ["DeletePoint"] = "Delete",
            ["ActivatePoint"] = "Click at this point",
            ["TargetPoint"] = "Target: {0} ({1})",
            ["TargetCursor"] = "Target: current cursor position",
            ["PointSaved"] = "Saved \"{0}\": {1}",
            ["HotkeyInUse"] = "This combination is already used by another action.",
            ["TabClicker"] = "Auto-click",
            ["TabPatterns"] = "Patterns",
            ["TabHotkeys"] = "Hotkeys",
            ["TabOverlay"] = "Overlay",
            ["OverlayEnabled"] = "Show status overlay",
            ["OverlayShowWhenStopped"] = "Also show when everything is stopped",
            ["OverlayOpacity"] = "Opacity",
            ["OverlaySize"] = "Size",
            ["OverlayBlink"] = "Blink while active",
            ["BlinkSpeed"] = "Blink speed",
            ["BlinkSlow"] = "Slow",
            ["BlinkNormal"] = "Normal",
            ["BlinkFast"] = "Fast",
            ["MoveOverlay"] = "Move overlay",
            ["MoveOverlayDone"] = "Done — lock in place",
            ["ResetOverlayPosition"] = "Reset position",
            ["OverlayMoveHint"] = "Press \"Move\", drag the badge with the mouse to where you want it, then press \"Done\".",
            ["OverlayHint"] = "The overlay is transparent to the mouse — clicks go through it into the game. It is not visible over games in exclusive fullscreen: switch the game to borderless window mode.",
            ["OverlayStopped"] = "Off",
            ["OverlayClicking"] = "Auto-click",
            ["OverlayPlaying"] = "Pattern",
            ["OverlayRecording"] = "Recording",
            ["PatternHotkey"] = "\"Record pattern\" hotkey",
            ["Patterns"] = "Recorded patterns",
            ["PatternsEmpty"] = "Press {0}, do your mouse and keyboard actions, then press {0} again to save the pattern.",
            ["PatternsHint"] = "When a pattern is on, {0} plays it instead of auto-clicking. Only one can be on.",
            ["PatternName"] = "Pattern {0}",
            ["PatternInfo"] = "{0} s · {1} events",
            ["RecordPattern"] = "Record pattern",
            ["StopRecording"] = "Stop recording",
            ["Repeats"] = "Repeats (0 = endless)",
            ["LoopDelay"] = "Pause between repeats (ms)",
            ["RecordKeyboard"] = "Record the keyboard too",
            ["StatusRecording"] = "Recording pattern",
            ["StatusPlaying"] = "Playing pattern",
            ["RecordingProgress"] = "{0} s · {1} events",
            ["LoopProgress"] = "Loop {0} of {1}",
            ["LoopProgressEndless"] = "Loop {0}",
            ["TargetPattern"] = "Mode: pattern \"{0}\"",
            ["NothingRecorded"] = "Nothing was recorded.",
            ["PatternSaved"] = "Pattern \"{0}\" saved: {1}",
            ["InvalidNumber"] = "Enter a whole number from 0 to {0}",
            ["PatternWarning"] = "Patterns replay moves and clicks at absolute screen coordinates — don't move the game window between recording and playback.",
        },
    };

    private Loc()
    {
    }

    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changed, for texts that are built in code.</summary>
    public event Action? LanguageChanged;

    public string Language { get; private set; } = English;

    public string this[string key] =>
        Strings[Language].TryGetValue(key, out string? value) ? value : key;

    public string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, this[key], args);

    public static string DetectSystemLanguage() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == Ukrainian ? Ukrainian : English;

    public void SetLanguage(string language)
    {
        if (!Strings.ContainsKey(language))
            language = English;

        if (language == Language) return;

        Language = language;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke();
    }
}
