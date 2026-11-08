using Habitat;

internal static class PreferencesRecoveryChecks
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var directory = Path.Combine(root, "preferences-recovery");
        var path = Path.Combine(directory, "preferences.json");
        var initial = Preferences.Load(path);
        check(initial.LoadState == PreferencesLoadState.New && initial.CanSave, "A new profile can save defaults.");
        var prior = initial with { Theme = "night", Opacity = 37, WeatherCity = "Original city", GlobalHotkeyEnabled = false };
        check(prior.Save(path) && (prior with { Opacity = 45 }).Save(path), "Changing preferences retains the previous readable file.");
        var current = File.ReadAllBytes(path);
        Preferences unavailable;
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            unavailable = Preferences.Load(path);
        check(unavailable.LoadState == PreferencesLoadState.Unavailable && !(unavailable with { WindowX = 20 }).Save(path) &&
            File.ReadAllBytes(path).AsSpan().SequenceEqual(current), "A close after a read lock cannot overwrite saved preferences.");
        check(Preferences.Load(path) is { CanSave: true, Opacity: 45, GlobalHotkeyEnabled: false },
            "Retrying the read after the lock recovers the saved settings.");

        const string damagedText = "{\"Theme\":\"night\",\"WeatherCity\":";
        File.WriteAllText(path, damagedText);
        var damaged = Preferences.Load(path);
        check(damaged.LoadState == PreferencesLoadState.Damaged && !damaged.Save(path) && !prior.Save(path) &&
            File.ReadAllText(path) == damagedText, "Ordinary saves leave damaged preferences intact.");
        var restored = Preferences.RecoverDamaged(path, restoreBackup: true);
        var retained = Directory.GetFiles(directory, "preferences.json.damaged-*");
        check(restored is { CanSave: true, Theme: "night", Opacity: 37, WeatherCity: "Original city", GlobalHotkeyEnabled: false } &&
            Preferences.Load(path).Opacity == 37 && retained.Length == 1 && File.ReadAllText(retained[0]) == damagedText,
            "Explicit recovery restores readable settings and retains the exact damaged file.");

        var missingBackup = Path.Combine(directory, "without-backup.json");
        File.WriteAllText(missingBackup, damagedText);
        check(!Preferences.RecoverDamaged(missingBackup, restoreBackup: true).CanSave &&
            File.ReadAllText(missingBackup) == damagedText, "A missing backup cannot silently turn a restore into a reset.");
        check(Preferences.RecoverDamaged(missingBackup, restoreBackup: false) is { CanSave: true, Theme: "hybrid", Opacity: 15 } &&
            File.ReadAllText(Directory.GetFiles(directory, "without-backup.json.damaged-*").Single()) == damagedText,
            "Explicit reset keeps the damaged file when no valid backup exists.");
    }
}
