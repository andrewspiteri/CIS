---
title: "Configuration Dictionary"
type: reference
status: Active
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: change-impact-studio:reference:configuration-dictionary
---

# Configuration Dictionary

Governed by `change-impact-studio:spec:configuration-dictionary`. These settings define
the repository-local CIS documentation boundary and configuration schema.

| Name | Path | Allowed values | Refresh class | Owner | Sensitive | Description | Status | Evidence |
|---|---|---|---|---|---|---|---|---|
| `schema_version` | `.cis/repository.yml#schema_version` | Positive supported integer | repository initialization | Repository maintainer | no | Selects the CIS repository configuration schema. | Active | `.cis/repository.yml` |
| `documentation_root` | `.cis/repository.yml#documentation_root` | Repository-relative directory | repository initialization | Repository maintainer | no | Locates canonical Markdown and its catalog. | Active | `.cis/repository.yml` |
