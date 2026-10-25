# Code signing policy

Status: **planned, inactive**. Decision recorded on 19 October 2026.

The project owner selected SignPath Foundation as the preferred future code-signing route, and explicitly deferred applying until HABITAT's packaging is improved. No application has been submitted and no signing service is configured by this preparation. Published 0.10.0 desktop packages are unsigned. HABITAT does not currently claim endorsement, sponsorship or a certificate from SignPath Foundation.

## Responsibilities

The current maintainer is [aesgalexis](https://github.com/aesgalexis), responsible for the repository, code review and release approval. These are the proposed signing roles; their configuration and multi-factor authentication must be confirmed before any application. Additional contributors and approvers must be recorded here before receiving signing access.

## Intended scope

Only HABITAT's own binaries listed in [preparation.json](../packaging/signpath/preparation.json) and its reviewed installer may be submitted after the owner explicitly resumes the process and SignPath accepts the project. Third-party runtimes, libraries, test programs, Chrome extension assets and VS Code extension assets must not be signed under HABITAT's signing identity.

Builds must originate from reviewed source in this repository on GitHub-hosted runners. Signing must retain verifiable build provenance and require a trusted maintainer's approval for every release. Downloaded or manually substituted binaries are not eligible inputs. Returned signatures and timestamps must be verified before packaging and publishing. Signing changes file bytes: manifests, SHA-256 files and WinGet metadata must be generated from the final artifacts.

## Privacy and eligibility

[Desktop privacy notes](PRIVACY.md) describe the current implementation, including automatic weather refreshes and authorized Gmail refreshes. The [Chrome extension policy](EXTENSION_PRIVACY.md) covers the browser bridge. The packaging review must resolve installer disclosure and network choices before requesting acceptance; this document does not assert that all Foundation requirements are already met.

## Activation

Follow the [preparation plan](SIGNING.md). Creating an account, installing a SignPath GitHub App, accepting terms, adding credentials, submitting an application or requesting any signature requires the owner to resume this work explicitly. A local preparation report is not an approval to perform those actions.

If the project is accepted, replace this inactive status with the actual signing policy, approved roles and the exact attribution required by SignPath. Do not claim free signing is provided before acceptance.
