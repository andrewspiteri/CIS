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
| `CIS_AI_API_KEY` | `environment:CIS_AI_API_KEY` | Runtime-configured value | process start | Repository maintainer | yes | Discovered runtime configuration identity. | Active | `src/Cis.Modules.Ai/OpenAiCompatibleProvider.cs:39` |
| `CIS_AI_ENDPOINT` | `environment:CIS_AI_ENDPOINT` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Modules.Ai/OpenAiCompatibleProvider.cs:14` |
| `CIS_AI_MODEL` | `environment:CIS_AI_MODEL` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Modules.Ai/OpenAiCompatibleProvider.cs:15` |
| `CIS_ASSURANCE_TECHNIQUE` | `environment:CIS_ASSURANCE_TECHNIQUE` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Modules.Testing/TestingService.cs:184` |
| `CIS_ASSURER` | `environment:CIS_ASSURER` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Modules.Testing/TestingService.cs:183` |
| `CIS_IMPLEMENTER` | `environment:CIS_IMPLEMENTER` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Modules.Testing/TestingService.cs:182` |
| `CIS_SHARP_NODE_MODULES` | `environment:CIS_SHARP_NODE_MODULES` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Modules.Design/DesignProcessRunner.cs:46` |
| `GITHUB_REPOSITORY` | `environment:GITHUB_REPOSITORY` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Modules.Ci/CiService.cs:151` |
| `NODE_PATH` | `environment:NODE_PATH` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Modules.Design/DesignProcessRunner.cs:73` |
| `OLLAMA_HOST` | `environment:OLLAMA_HOST` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Modules.Ai/OllamaAiProvider.cs:124` |
| `PATH` | `environment:PATH` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Modules.Agent/AgentService.cs:561` |
| `PATHEXT` | `environment:PATHEXT` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Modules.Agent/AgentService.cs:559` |
| `stryker-config` | `environment:stryker-config` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:additional-timeout` | `environment:stryker-config:additional-timeout` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:concurrency` | `environment:stryker-config:concurrency` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:configuration` | `environment:stryker-config:configuration` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:coverage-analysis` | `environment:stryker-config:coverage-analysis` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:mutate` | `environment:stryker-config:mutate` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:mutation-level` | `environment:stryker-config:mutation-level` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:report-file-name` | `environment:stryker-config:report-file-name` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:reporters` | `environment:stryker-config:reporters` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:target-framework` | `environment:stryker-config:target-framework` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:test-projects` | `environment:stryker-config:test-projects` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:test-runner` | `environment:stryker-config:test-runner` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:thresholds` | `environment:stryker-config:thresholds` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:thresholds:break` | `environment:stryker-config:thresholds:break` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:thresholds:high` | `environment:stryker-config:thresholds:high` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:thresholds:low` | `environment:stryker-config:thresholds:low` | Runtime-configured value | process start | Repository maintainer | no | Discovered runtime configuration identity. | Active | `src/Cis.Abstractions/stryker-config.json` |
