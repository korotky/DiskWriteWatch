# Contributing

Thanks for considering a contribution.

## Development requirements

- Windows 10/11 x64
- .NET 10 SDK
- Administrator rights only when running the live ETW collector or installing the service

## Local checks

```powershell
.\build.ps1
dotnet test DiskWriteWatch.slnx
```

Package a self-contained release locally:

```powershell
.\package.ps1 -Version 0.0.0-local
```

## Design constraints

- No kernel driver. Collection must use Windows ETW.
- Dashboard assets must be local: no CDN, tracking, analytics, or external runtime dependencies.
- The dashboard must remain loopback-only unless authentication and threat modeling are added first.
- Avoid writing fallback data to `C:` when the configured data directory is unavailable.
- Be careful with path and process metadata: it is useful for attribution but can be sensitive.

## Pull requests

Please keep changes focused and include tests for aggregation, filtering, retention, or configuration behavior when applicable. For UI-only changes, at least run a JavaScript syntax check and `dotnet test`.

