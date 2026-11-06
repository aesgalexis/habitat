using Habitat.Features.Drive;

namespace Habitat.Features.Contacts;

// Contacts retain their existing CSV schema and markers on the shared client.
internal sealed class ContactDriveClient(DriveWorkspaceContext workspace) : DriveDocumentClient(workspace, DriveDocumentKind.Contacts)
{
    internal const string FileName = "HABITAT Contacts.csv";
}
