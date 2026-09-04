---
name: cis-ai-model-qualification
description: Qualify local or explicitly authorized remote models with runtime probes, deterministic benchmarks, prompt-regression datasets, route explanations, and task-class approvals. Use before assigning a model to repository work or after provider, model, prompt, dataset, or policy changes.
---

# CIS AI model qualification

1. Read `docs/references/ai-routing-profile.md`, `docs/references/ai-model-registry.md`, and the applicable evaluation dataset.
2. Run `cis ai model probe --provider <provider> --model <model> --format agent`.
3. Run `cis ai model benchmark --provider <provider> --model <model> --task-class <class> --format agent`.
4. Run `cis ai eval prompt-regression --provider <provider> --model <model> --task-class <class> --dataset <path> --format agent`.
5. Inspect hashed evidence under `.cis/local/ai/qualification/`; generated content is not retained.
6. Use `cis ai route explain <capability> --format agent` before selection.
7. Only a human reviewer may run `cis ai model approve ... --reviewer <name> --reason <reason> --yes` after all runtime evidence passes.

Remote qualification requires explicit `--allow-remote` authorization. Model output cannot approve itself or determine a score.
