using Habitat.Services;
using System.Security.Cryptography;

try
{
    string? Option(string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
    var payload = Path.GetFullPath(Option("--payload") ?? throw new ArgumentException("Specify --payload directory."));
    var available = ReleasePayload.VerifyDrive(Path.Combine(payload, "HABITAT.dll"), args.Contains("--require-drive"),
        Option("--expected-drive"));
    if (Option("--match") is { } reference)
        foreach (var file in new[] { "HABITAT.exe", "HABITAT.dll", "HABITAT.Hotkey.exe", "HABITAT.Hotkey.dll", "Habitat.Core.dll", "Habitat.Activation.dll" })
        {
            using var original = File.OpenRead(Path.Combine(reference, file));
            using var packaged = File.OpenRead(Path.Combine(payload, file));
            if (!SHA256.HashData(original).AsSpan().SequenceEqual(SHA256.HashData(packaged)))
                throw new InvalidDataException("The packaged binaries differ from the verified release payload.");
        }
    Console.WriteLine(available ? "PASS: release payload includes valid built-in Google Drive sign-in." :
        "PASS: custom build has no built-in Drive client; advanced configuration remains available.");
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or BadImageFormatException or
    ArgumentException or InvalidOperationException or System.Text.Json.JsonException or OverflowException)
{
    // Configuration bytes and client credentials never enter build logs.
    Console.Error.WriteLine("Release payload verification failed: " + error.GetType().Name);
    return 1;
}
