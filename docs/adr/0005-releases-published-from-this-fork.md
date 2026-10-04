# C# releases are published from this fork

C# releases are published as GitHub releases on **`evildarkarchon/Collective-Modding-Toolkit`**, the fork this port is built in. That repo is a fork of `RowanSkie/Collective-Modding-Toolkit`, which is itself a fork of `wxMichael/Collective-Modding-Toolkit`. It already hosts the Delta Patch mirror, so the Downgrader's base URL stays where it is. The decision was made in [Decide the CI and release pipeline](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/63).

The decision is hard to reverse because every shipped build looks for updates in this repo, and keeps doing so after newer releases appear elsewhere. So before release, the app's GitHub identifiers are repointed here: the update-check URL and `GITHUB_LINK`, which the update banner and the About tab use. This is a pre-release fix, not a parity change. The Reference Implementation's identifiers point at repos that will never carry a C# release, so the update check would never report one. If the app is also distributed on Nexus Mods, it goes on a **fork page**, not the original mod 87907, and `NEXUS_LINK` (which also feeds the Nexus update check) is repointed once that page exists.

## Considered Options

- **`RowanSkie/Collective-Modding-Toolkit`**: the current maintainer, and the repo `GITHUB_LINK` names today. Rejected because we only have read access and it has never published a release, so its owner would have to run every release. The Delta Patch mirror and its URL constant would also have to move.
- **`wxMichael/Collective-Modding-Toolkit`**: the original repo. Every release up to 0.6.1 came from here, and existing users' GitHub update check queries it. Rejected because it has been dormant since December 2025 and we have no write access.

## Consequences

- The release workflow lives here. A bare PEP 440 tag push (e.g. `0.7.0`, which must equal `<Version>`) builds a **draft** release, and a human publishes it.
- `releases/latest` must only ever resolve to an app release. The `delta-patches` release stays flagged pre-release permanently, and the release workflow publishes with `make_latest=true` set explicitly.
- Users still on wxMichael's 0.6.1 never see a C# release through either update check: their GitHub check reads wxMichael, and their Nexus check reads mod 87907. Reaching them depends on someone outside this repo posting a notice, which is outside this effort.
- Moving releases to another repo later means repointing the update check, the links and the Delta Patch base URL together, and stranding every build already shipped.
