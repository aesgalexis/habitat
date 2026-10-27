# Legacy updater fixture

The client and three PowerShell files are pinned from `e5a2ecc`. Only the C#
namespace is changed to `Habitat.LegacyUpdates`, allowing the old and current
clients to run side by side. Do not update these fixtures when fixing production.

This is the historical DesktopUpdateService/ReleaseUpdateClient workflow reported
on the affected development installation. It is not the official `v0.16.1` tree:
that tag has the other AppUpdateInstall/UpdateHelper workflow. The affected
ProductVersion refers to a base commit with local changes; without that PC's DLL
hashes it cannot prove byte identity with this fixture.

Tests keep the old helper unchanged. A test wrapper redirects only its health
requests to a private random loopback port, leaving the real bridge untouched.
