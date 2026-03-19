# sweet16

`sweet16` is a small .NET CLI for pulling NCAA tournament fields and generating bracket simulation data.

Right now the repo includes:

- men's tournament CSV import into a normalized JSON field file
- men's `seedweight` scenario simulation
- NCAA.com scraping for men's and women's tournament entrants and seeds

## Requirements

- .NET 10 SDK

## Quick Start

```powershell
dotnet build
```

Show the available commands:

```powershell
dotnet run -- --help
```

## Commands

### Scrape NCAA entrants

Pull the men's and women's fields for a season from official NCAA pages:

```powershell
dotnet run -- scrape --year 2026
```

This writes output under a local `data/` directory.

### Import men's bracket data from CSV

Normalize a men's bracket CSV into the repo's field format:

```powershell
dotnet run -- import-men --csv "C:\path\to\Tournament Matchups.csv" --year 2026
```

By default this writes to a local file under `data/mens/`.

### Simulate a men's seedweight bracket

Generate a seeded random scenario from an imported men's field:

```powershell
dotnet run -- seedweight-men --input data\mens\2026.json
```

To make runs reproducible:

```powershell
dotnet run -- seedweight-men --input data\mens\2026.json --seed 16
```

By default this writes a timestamped scenario file under `data/mens/scenarios/`.

## Notes

- The current CLI surface exposes `import-men`, `seedweight-men`, and `scrape`.
- Generated/imported tournament files live in local `data/` output and are not intended to be committed.
- Women's entrant data is scraped and stored, but there is not yet a dedicated `seedweight-women` command exposed by the CLI.
- The project currently lives in a single-file app for speed while the data model and workflows settle down.
