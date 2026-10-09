---
title: "cis repo example"
type: command-reference
status: Active
owner: "Andrew Spiteri"
last_reviewed: "2026-10-09"
review_cadence: "on command or recipe change"
cis:
  stable_id: change-impact-studio:manual:cis-repo-example
---

# `cis repo example`

Normal C# initialization already installs the bundled reference under `.cis/local/examples/dotnet-engineering/`, unless opted out. Use this command to inspect the bundle or export an additional fresh copy for comparison. The example demonstrates explicit application composition, native xUnit tests, Coverlet, Stryker, SDK/Sonar linting, PostgreSQL business acceptance and a thin application CLI with portable skills. Exporting it does not select a framework for the surrounding project or certify readiness.

```text
cis repo example [--repo <path>] [--destination <new-relative-directory>] [--dry-run] [--format human|json|agent]
```

Without a destination, `--format json` lists available files; human and agent output report their count. A destination must be a new directory inside the selected repository; existing files or directories are never overwritten. Preview with `--dry-run`, then repeat without that flag to copy. Exit 0 means the requested inspection/export succeeded. Exit 2 means the bundle is unavailable or the destination is invalid. No packages are installed and no tests or containers are started by export.

```powershell
cis repo example --format json
cis repo example --destination .cis/local/example-comparison --dry-run
cis repo example --destination .cis/local/example-comparison
```

Follow the copied `README.md` for exact native commands and pinned prerequisites. Choose an isolated database. Keep qualification outputs in its ignored `.cis/local/` directory. Current qualification and remaining release blockers are recorded in the repository's engineering-defaults progress record; a bundled example is not proof that every planned assurance layer has been qualified.

The example is shipped with the binary so an exported copy does not depend on a development checkout. It does not contain private project requirements, retained test runs, build output or credentials.
