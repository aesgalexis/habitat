using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Habitat.Models;

namespace Habitat.Services;

// Inspect metadata without loading WinUI, starting the app or reading an account.
internal static class ReleasePayload
{
    internal const string DriveResourceName = "Habitat.GoogleDriveOAuthClient.json";

    internal static GmailClientConfiguration? ReadDriveClient(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var matches = metadata.ManifestResources.Select(metadata.GetManifestResource)
            .Where(item => metadata.GetString(item.Name) == DriveResourceName).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1 || !matches[0].Implementation.IsNil || pe.PEHeaders.CorHeader is not { } header)
            throw new InvalidDataException("Invalid bundled Google Drive configuration resource.");
        var offset = checked((int)matches[0].Offset);
        var directory = header.ResourcesDirectory;
        if (offset < 0 || offset > directory.Size - 4)
            throw new InvalidDataException("Invalid bundled Google Drive configuration bounds.");
        var resources = pe.GetSectionData(directory.RelativeVirtualAddress).GetReader(offset, directory.Size - offset);
        var length = resources.ReadInt32();
        if (length is <= 0 or > 65536 || length > resources.RemainingBytes)
            throw new InvalidDataException("Invalid bundled Google Drive configuration size.");
        using var configuration = new StreamReader(new MemoryStream(resources.ReadBytes(length)));
        return GmailClientConfiguration.FromJson(configuration.ReadToEnd());
    }

    internal static bool VerifyDrive(string assemblyPath, bool required, string? expectedClientPath = null)
    {
        var client = ReadDriveClient(assemblyPath);
        if (client is null && (required || expectedClientPath is not null))
            throw new InvalidDataException("The release payload is missing built-in Google Drive sign-in.");
        if (expectedClientPath is not null)
        {
            using var expected = File.OpenRead(expectedClientPath);
            if (expected.Length > 65536) throw new InvalidDataException("The expected Google Drive configuration is too large.");
            using var reader = new StreamReader(expected);
            if (client != GmailClientConfiguration.FromJson(reader.ReadToEnd()))
                throw new InvalidDataException("The bundled Google Drive client differs from the release configuration.");
        }
        return client is not null;
    }
}
