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
the repository-local CIS documentation boundary, runtime settings and example/tool configuration.
Environment variables use `environment:` locators; Stryker settings use their JSON source path.
A connection string may contain credentials and must not be copied into reports or transcripts.

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
| `stryker-config` | `src/Cis.Abstractions/stryker-config.json#stryker-config` | See JSON source | test invocation | Repository maintainer | no | Native Stryker JSON configuration; not an environment variable. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:additional-timeout` | `src/Cis.Abstractions/stryker-config.json#stryker-config.additional-timeout` | See JSON source | test invocation | Repository maintainer | no | Native Stryker JSON configuration; not an environment variable. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:concurrency` | `src/Cis.Abstractions/stryker-config.json#stryker-config.concurrency` | See JSON source | test invocation | Repository maintainer | no | Native Stryker JSON configuration; not an environment variable. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:configuration` | `src/Cis.Abstractions/stryker-config.json#stryker-config.configuration` | See JSON source | test invocation | Repository maintainer | no | Native Stryker JSON configuration; not an environment variable. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:coverage-analysis` | `src/Cis.Abstractions/stryker-config.json#stryker-config.coverage-analysis` | See JSON source | test invocation | Repository maintainer | no | Native Stryker JSON configuration; not an environment variable. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:mutate` | `src/Cis.Abstractions/stryker-config.json#stryker-config.mutate` | See JSON source | test invocation | Repository maintainer | no | Native Stryker JSON configuration; not an environment variable. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:mutation-level` | `src/Cis.Abstractions/stryker-config.json#stryker-config.mutation-level` | See JSON source | test invocation | Repository maintainer | no | Native Stryker JSON configuration; not an environment variable. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:report-file-name` | `src/Cis.Abstractions/stryker-config.json#stryker-config.report-file-name` | See JSON source | test invocation | Repository maintainer | no | Native Stryker JSON configuration; not an environment variable. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:reporters` | `src/Cis.Abstractions/stryker-config.json#stryker-config.reporters` | See JSON source | test invocation | Repository maintainer | no | Native Stryker JSON configuration; not an environment variable. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:target-framework` | `src/Cis.Abstractions/stryker-config.json#stryker-config.target-framework` | See JSON source | test invocation | Repository maintainer | no | Native Stryker JSON configuration; not an environment variable. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:test-projects` | `src/Cis.Abstractions/stryker-config.json#stryker-config.test-projects` | See JSON source | test invocation | Repository maintainer | no | Native Stryker JSON configuration; not an environment variable. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:test-runner` | `src/Cis.Abstractions/stryker-config.json#stryker-config.test-runner` | See JSON source | test invocation | Repository maintainer | no | Native Stryker JSON configuration; not an environment variable. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:thresholds` | `src/Cis.Abstractions/stryker-config.json#stryker-config.thresholds` | See JSON source | test invocation | Repository maintainer | no | Native Stryker JSON configuration; not an environment variable. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:thresholds:break` | `src/Cis.Abstractions/stryker-config.json#stryker-config.thresholds.break` | See JSON source | test invocation | Repository maintainer | no | Native Stryker JSON configuration; not an environment variable. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:thresholds:high` | `src/Cis.Abstractions/stryker-config.json#stryker-config.thresholds.high` | See JSON source | test invocation | Repository maintainer | no | Native Stryker JSON configuration; not an environment variable. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `stryker-config:thresholds:low` | `src/Cis.Abstractions/stryker-config.json#stryker-config.thresholds.low` | See JSON source | test invocation | Repository maintainer | no | Native Stryker JSON configuration; not an environment variable. | Active | `src/Cis.Abstractions/stryker-config.json` |
| `CIS_CLAUDE_EXECUTABLE` | `environment:CIS_CLAUDE_EXECUTABLE` | Executable path or command | process start | Repository maintainer | no | Overrides Claude CLI discovery; absent values use native discovery. | Active | `src/Cis.Providers.Agent.Claude/ClaudeAgentProvider.cs:20` |
| `CIS_CODEX_EXECUTABLE` | `environment:CIS_CODEX_EXECUTABLE` | Executable path or command | process start | Repository maintainer | no | Overrides Codex CLI discovery; absent values use native discovery. | Active | `src/Cis.Providers.Agent.Codex/CodexAgentProvider.cs:17` |
| `CIS_PERF_TRACE` | `environment:CIS_PERF_TRACE` | `1` enables; otherwise disabled | process start | Repository maintainer | no | Emits local phase timings to standard error without repository content. | Active | `src/Cis.Abstractions/CisPerformanceTrace.cs:18` |
| `CIS_PROFILE_OUTPUT` | `environment:CIS_PROFILE_OUTPUT` | Output file path; unset disables recording | process start | Repository maintainer | no | Used by the opt-in function profiler runtime; normal CIS builds have no profiler dependency. | Active | `tools/function-profiler/Runtime/Probe.cs:12` |
| `CODEX_HOME` | `environment:CODEX_HOME` | Codex directory path | process start | Repository maintainer | no | Locates the public model catalogue for the Codex text-generation provider; defaults to the user profile .codex directory. | Active | `src/Cis.Providers.Agent.Codex/CodexTextGenerationProvider.cs:18` |
| `EXAMPLE_DATABASE_CONNECTION` | `environment:EXAMPLE_DATABASE_CONNECTION` | Npgsql connection string for an isolated example database | process start | Repository maintainer | yes | Required by the synthetic reading CLI; may include credentials. Example-only, not CIS product configuration. | Active | `examples/dotnet-engineering/src/Example.Cli/ReadingCommand.cs:24` |
| `LOCALAPPDATA` | `environment:LOCALAPPDATA` | Windows local application-data directory | process start | Repository maintainer | no | Ambient Windows input to Codex executable discovery. | Active | `src/Cis.Providers.Agent.Codex/CodexAgentProvider.cs:19` |
| `stryker-config:project` | `examples/dotnet-engineering/stryker-config.json#stryker-config.project` | See JSON source | test invocation | Repository maintainer | no | Native Stryker JSON configuration; not an environment variable. | Active | `examples/dotnet-engineering/stryker-config.json` |
| `stryker-config:solution` | `examples/dotnet-engineering/stryker-config.json#stryker-config.solution` | See JSON source | test invocation | Repository maintainer | no | Native Stryker JSON configuration; not an environment variable. | Active | `examples/dotnet-engineering/stryker-config.json` |
| `USERPROFILE` | `environment:USERPROFILE` | Windows user-profile directory | process start | Repository maintainer | no | Ambient Windows input to Claude executable discovery. | Active | `src/Cis.Providers.Agent.Claude/ClaudeAgentProvider.cs:22` |
