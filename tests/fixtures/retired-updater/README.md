# Retired AppUpdate updater

These files preserve the AppUpdateService/AppUpdateDownload/AppUpdateInstall flow
from the working tree audited on 6 November 2026. They compile only into the Core
checks, including the old helper as a test resource. Production uses
DesktopUpdateService, ReleaseUpdateClient and install-release-update.ps1.

The helper launcher delegates to the shared production UpdateHelperLauncher so
PowerShell host arguments and environment handling have one owner. The old
discovery, download and installation behavior remains exercised with synthetic
data; it is not included in Habitat.Core or release payloads.
