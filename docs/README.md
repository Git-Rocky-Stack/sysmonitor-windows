# STX.1 System Monitor documentation

Documentation for STX.1 System Monitor v3.0.0, a Windows desktop system monitoring
and maintenance application.

The public page built from these files is served at
<https://git-rocky-stack.github.io/sysmonitor-windows/>.

These docs follow the [Diataxis](https://diataxis.fr/) split. Four kinds of document,
each for a reader in a different mode.

## Start here

| Document | Read it when |
|---|---|
| [Getting started](tutorial-getting-started.md) | You have just installed the app and want a first result |

## How-to guides

Task oriented. You know your way around and want to get one specific thing done.

| Guide | Task |
|---|---|
| [Free up disk space](howto-free-disk-space.md) | Reclaim space without deleting anything you wanted |
| [Securely wipe files](howto-securely-wipe-files.md) | Destroy a file's contents, and know what that does and does not guarantee |
| [Back up and restore](howto-back-up-and-restore.md) | Create a backup, encrypt it, verify it, and get files back |
| [Manage startup programs](howto-manage-startup-programs.md) | Stop programs launching at sign-in, reversibly |

## Reference

Information oriented. Complete and accurate. Look things up here.

| Reference | Covers |
|---|---|
| [Pages](reference-pages.md) | Every one of the 34 entries in the navigation menu |
| [Settings and data](reference-settings-and-data.md) | Every setting, where files are written, what to delete to reset |

## Explanation

Understanding oriented. Why the app behaves the way it does.

| Explanation | Question it answers |
|---|---|
| [What v3.0.0 removed, and why](explanation-honest-reporting.md) | Why did features disappear in a new version |
| [The Drive Wiper and solid-state drives](explanation-drive-wiper-and-ssds.md) | Why will the app not promise a wiped file is unrecoverable |

## Related files in the repository

- [README.md](../README.md), project overview and build instructions
- [CHANGELOG.md](../CHANGELOG.md), every behaviour change in v3.0.0 with the file it lives in
- [FEATURES_AND_USER_GUIDE.md](../FEATURES_AND_USER_GUIDE.md), the guide that also ships inside the app
- [PRIVACY_POLICY.md](../PRIVACY_POLICY.md)
- [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md)
- [LICENSE](../LICENSE), MIT

## A note on accuracy

Every behavioural claim in these documents is traceable to a file and line in this
repository, and was checked against the source rather than copied from older
documentation. Where the app cannot guarantee something, these documents say so
instead of implying otherwise. That is the point of the v3.0.0 release, and
[the explanation of it](explanation-honest-reporting.md) is worth reading before the
rest.
