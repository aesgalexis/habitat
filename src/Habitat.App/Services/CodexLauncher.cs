using Windows.ApplicationModel.DataTransfer;
using Windows.Management.Deployment;

namespace Habitat.Services;

internal sealed class CodexLauncher
{
    public async Task<bool> OpenAsync(string text)
    {
        var package = new PackageManager().FindPackagesForUser(string.Empty)
            .FirstOrDefault(package => package.Id.Name.Equals("OpenAI.Codex", StringComparison.OrdinalIgnoreCase));
        if (package is null) return false;
        var entries = await package.GetAppListEntriesAsync();
        var entry = entries.FirstOrDefault();
        if (entry is null) return false;
        var content = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        content.SetText(text);
        Clipboard.SetContent(content);
        Clipboard.Flush();
        // Launch the registered application without navigating to another chat.
        // The user pastes and sends the instruction in their active chat.
        return await entry.LaunchAsync();
    }
}
