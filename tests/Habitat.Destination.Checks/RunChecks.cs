using System.Diagnostics;
using Habitat.Services;

internal static class RunChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(ComposerDestinations.Ordered[^1] == ComposerDestination.Run &&
            ComposerDestinations.Ordered[^2] == ComposerDestination.Terminal &&
            ComposerDestinations.Cycle(ComposerDestination.Run, 1) == ComposerDestination.OpenAI &&
            ComposerDestinations.Label(ComposerDestination.Run) == "Run",
            "Run appears last, below Terminal, and participates in circular navigation");
        check(ComposerDestinations.FromLegacy(6) == ComposerDestination.Terminal && ComposerDestinations.FromLegacy(9) == ComposerDestination.VSCode &&
            ComposerDestinations.SearchUri(ComposerDestination.Run, "calc") is null,
            "Run preserves saved destination IDs and never creates a browser search");

        ProcessStartInfo? captured = null;
        var runs = 0;
        var launcher = new RunLauncher(AppContext.BaseDirectory, info => { captured = info; runs++; return true; });
        foreach (var command in new[] { "calc", "msconfig", "control", "services.msc" })
        {
            check(launcher.Run("  " + command + "  ") && captured!.FileName == command && captured.Arguments == "" &&
                captured.UseShellExecute && captured.WorkingDirectory == AppContext.BaseDirectory && captured.Verb == "" &&
                !captured.RedirectStandardOutput && !captured.CreateNoWindow,
                $"Run hands {command} directly to Windows without a command shell or forced elevation");
        }
        const string program = "C:\\Program Files\\Example 雪\\app.exe";
        const string arguments = "--title \"café & 雪\" --file \"C:\\a b\\note.txt\"";
        check(launcher.Run('"' + program + "\" " + arguments) &&
            captured!.FileName == program && captured.Arguments == arguments,
            "Run separates quoted program paths and preserves argument quoting and Unicode");
        check(launcher.Run("cmd /c echo one & echo two") && captured!.FileName == "cmd" &&
            captured.Arguments == "/c echo one & echo two",
            "Run passes shell syntax only to the explicitly requested program");

        var directory = Path.Combine(AppContext.BaseDirectory, "isolated-run", "folder with spaces 雪");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "sample document.txt");
        File.WriteAllText(file, "Synthetic Run check");
        check(launcher.Run(file) && captured!.FileName == file && captured.Arguments == "" &&
            launcher.Run(directory) && captured!.FileName == directory && captured.Arguments == "",
            "Run opens existing unquoted file and folder paths containing spaces as one target");
        const string variable = "HABITAT_RUN_CHECK_DIRECTORY";
        var previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, directory);
            check(launcher.Run('%' + variable + '%') && captured!.FileName == directory,
                "Run expands Windows environment variables before resolving paths");
        }
        finally { Environment.SetEnvironmentVariable(variable, previous); }
        var beforeInvalid = runs;
        foreach (var invalid in new[] { "", " \t ", "\"\"", "\"unclosed", "\"calc\"extra", "cal\"c", "calc\nmsconfig", "calc\r", "calc\0ignored" })
            check(!launcher.Run(invalid) && runs == beforeInvalid,
                "Run rejects empty, malformed or multiline input without starting a process");
        check(!new RunLauncher(AppContext.BaseDirectory, _ => false).Run("calc"),
            "Run reports rejected launches so the editor can retain the command");
    }
}
