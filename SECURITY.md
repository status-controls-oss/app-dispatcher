# Security policy

App Dispatcher starts programs using links and file names that can come from anywhere, such as a web page, an email or a downloaded file. A flaw that lets one of those run a command, start an unexpected program or pass it unexpected options is a vulnerability, and we want to hear about it.

## Reporting a vulnerability

Report it privately through [GitHub's private vulnerability reporting](https://github.com/status-controls-oss/app-dispatcher/security/advisories/new). Please don't open a public issue for it.

Include what you did, what happened, and the version (shown in the release name, or in the file properties of `AppDispatcher.exe`). We aim to reply within a week. Once a fix is released we'll publish an advisory, crediting you unless you'd rather not be named.

## Supported versions

Only the latest release gets fixes.

## What's in scope

- Links or file names that run commands, start a program other than the one chosen, or inject options into it.
- Programs that run commands from their arguments but aren't yet refused, like the shells App Dispatcher already blocks.
- Anything that makes App Dispatcher write outside the locations listed under [Security and privacy](README.md#security-and-privacy) in the README.
- The installer and uninstaller deleting or changing files they shouldn't.

Out of scope: anything that needs the attacker to already be able to edit your config file or registry, run programs as you, or have admin rights, since they could then run what they like anyway.
