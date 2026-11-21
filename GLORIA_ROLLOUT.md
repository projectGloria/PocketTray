# Gloria rollout notes

Added `gloria.toml` and `.github/workflows/release.yml` so that pushing a `vX.Y.Z` tag builds PocketTray and attaches `PocketTray_<version>_x64-setup.exe` to the release. Nothing was built or run locally.

## Please check

- **Data folder is empty on purpose.** PocketTray stores `config.json`, logs and custom icons next to the exe (the install folder), not in AppData. `data_folders` only accepts `%APPDATA%`, `%LOCALAPPDATA%` or `%USERPROFILE%` paths, so it is empty. These files are not tracked by the installer, so updates and a normal uninstall should keep them. Please confirm that a `/PURGE` uninstall behaves as you want for them.
- **Old `%APPDATA%\PocketTray` is not listed.** The app only reads it once to migrate old settings; it no longer writes there, so I did not make the installer delete it.
- **Install folder is writable.** The default per-user folder under `%LOCALAPPDATA%\Programs` is writable, which the app needs. A custom folder such as `Program Files` would break saving settings.
- **Icon:** there is no `PocketTray.ico`, so `x-mark-512.ico` is used. It is the file the csproj already uses as the exe icon.
- **Version:** `gloria.toml` says `1.0.0`, matching `PocketTray.csproj`. The catalog says 1.0.2, but the tag overrides the manifest version at release time. The workflow also passes the tag version to `dotnet publish`.
- **No `legacy_names`:** the README says the app was always portable, with no installer, so none was added.
- **`needs_webview2 = false`:** this is a WinForms app, so no WebView2 bootstrapper is downloaded.
- **Runtime files:** only `PocketTray.exe` (self-contained single file) and `x-mark-512.ico` (read from disk at run time) go into `app-dist/`.
- **Secrets:** `WINDOWS_PFX_BASE64` and `WINDOWS_PFX_PASSWORD` must exist in the repo. If they are missing, the setup is built unsigned.
- **Manual runs:** a `workflow_dispatch` run builds and packs the setup but does not upload it, because there is no tag.
