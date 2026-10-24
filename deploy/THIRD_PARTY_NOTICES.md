# Distribution components

MintAPI source is distributed under the repository LICENSE. A release includes separately licensed components:

- .NET / ASP.NET Core and Playwright: upstream license and notice files remain in the application / browser directories. Chromium includes its own notices.
- `rosu-pp-ffi`: built from `native/rosu-pp-ffi` and its locked Cargo dependencies. Cargo.lock and source are available in the matching source release.
- osu-beatmap-preview: pinned source revision in `app/deployment/dependencies.json`; engine LICENSE and THIRD_PARTY_NOTICES.md are carried in `app/tools/osu-preview/`.
- Optional Windows MySQL Community: GPLv2 and separately licensed components. Its original LICENSE and docs remain in `app/deployment/mysql/`. Upstream source: https://dev.mysql.com/downloads/mysql/ . Installation initializes a dedicated MintAPI instance only.
- Optional Microsoft Visual C++ Redistributable: Microsoft license terms apply, https://visualstudio.microsoft.com/license-terms/ . The installer runs it only when optional Windows MySQL provisioning is requested.
- Fonts, icons and artwork under wwwroot retain their upstream terms; the application LICENSE does not relicense them. The help illustration was supplied by the project owner; HELP lettering is an edited derivative. Public redistribution rights for this illustration and all font assets must be confirmed before publishing.

Dependency URLs, source revisions and downloaded optional component SHA-256 hashes are recorded in each release. Do not remove upstream notices when repackaging. Debian MariaDB/Redis and other shared libraries are installed through the operating system package manager and retain their distribution licenses.
