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
            ["PressKey"] = "Натисніть клавішу або бокову кнопку миші…",
            ["HotkeyHint"] = "Працює навіть коли вікно гри активне. Esc — скасувати.",
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
            ["PressKey"] = "Press a key or a side mouse button…",
            ["HotkeyHint"] = "Works even while a game window is active. Esc to cancel.",
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
