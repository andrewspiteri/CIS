---
title: "CIS-0005 local security closeout"
type: workflow-definition
status: Active
owner: "CIS maintainers"
last_reviewed: "2026-11-24"
review_cadence: "on scanner, mount, output, resource or evidence change"
cis:
  stable_id: change-impact-studio:workflow:cis-0005-closeout-security
---

# CIS-0005 local security closeout

This local Windows workflow uses the existing three pinned scanners and security
suite adapters. It does not change their severity policy, result locations or
acceptances. The explicit paths bind this closeout to the current checkout.

Before execution, retain the previous three reports and their hashes, prepare
ordinary source and dependency staging trees with the existing scanner helper's
selection rules, and retain their source hashes. Admit each phase against the
same shared time and storage allocation. Run no other native writer concurrently.
The current security reconciler requires all three configured suites, including
the dependency scan; two scanner reports cannot establish a passing manifest.

The controller must verify Semgrep and Gitleaks images were absent before this
operation and record their actual pulled identities. Only these newly acquired
images may be removed by the cleanup steps. If either image already exists, stop
and review ownership instead of executing its removal. Do not remove an existing
image, cache, report, accounting record or recovery input.

Each scanner has a unique container name, a read-only root filesystem and input
mounts, a 64 MiB temporary filesystem and disabled daemon logging. Semgrep and Gitleaks
have a 50 MiB per-file limit. Trivy permits a cache file up to 1.75 GiB; its report
still has the independently monitored 50 MiB evidence limit. Only the declared report directory and Trivy's fresh cache are writable.
The controller samples report/cache/log growth, enforces the admitted total and
deadline, and stops and drains the exact owned container on failure or expiry.
These sampled controls are not a machine-wide filesystem sandbox. Reserve a
nonzero allowance for owned image metadata and logs; verified tar/blob sizes alone
do not bound the complete execution. Recount after each image is released and
admit the next phase separately. Preserve actual reports, workflow logs and
failure evidence; cleanup is not evidence of a successful scan.

| Step | Command | Working directory | Test suites | Depends on | Continue on failure | Timeout seconds |
| --- | --- | --- | --- | --- | --- | --- |
| sast-scan | docker run --rm --name cis0005-closeout-sast-20261124 --read-only --log-driver none --tmpfs /tmp:rw,size=67108864 --ulimit fsize=52428800:52428800 --env HOME=/tmp -v "C:/miscwork/portfolio/andrewspiteri.dev/content/change-impact-studio/.cis/local/security/scan-input/cis0005-closeout-sources-20261124:/scan:ro" -v "C:/miscwork/portfolio/andrewspiteri.dev/content/change-impact-studio/.cis/local/security/results:/results:rw" -w /scan semgrep/semgrep@sha256:207983631beecdbe7fa29196c7f4a7a5f29033933cdb76c687ce4a672e07618d semgrep scan --config auto --no-git-ignore --json --output /results/semgrep.json . | . | cis-sast | - | no | 300 |
| release-semgrep-image | docker image rm semgrep/semgrep@sha256:207983631beecdbe7fa29196c7f4a7a5f29033933cdb76c687ce4a672e07618d | . | - | sast-scan | no | 60 |
| secret-scan | docker run --rm --name cis0005-closeout-secrets-20261124 --read-only --network none --log-driver none --tmpfs /tmp:rw,size=67108864 --ulimit fsize=52428800:52428800 --env HOME=/tmp -v "C:/miscwork/portfolio/andrewspiteri.dev/content/change-impact-studio:/src:ro" -v "C:/miscwork/portfolio/andrewspiteri.dev/content/change-impact-studio/.cis/local/security/results:/results:rw" -w /src ghcr.io/gitleaks/gitleaks@sha256:cdbb7c955abce02001a9f6c9f602fb195b7fadc1e812065883f695d1eeaba854 detect --source=/src --no-banner --redact --report-format=json --report-path=/results/gitleaks.json --exit-code=0 --config=/src/.gitleaks.toml | . | cis-secrets | release-semgrep-image | no | 180 |
| release-gitleaks-image | docker image rm ghcr.io/gitleaks/gitleaks@sha256:cdbb7c955abce02001a9f6c9f602fb195b7fadc1e812065883f695d1eeaba854 | . | - | secret-scan | no | 60 |
| filesystem-scan | docker run --rm --name cis0005-closeout-filesystem-20261124 --read-only --log-driver none --tmpfs /tmp:rw,size=67108864 --ulimit fsize=1879048192:1879048192 --env HOME=/tmp -v "C:/miscwork/portfolio/andrewspiteri.dev/content/change-impact-studio/.cis/local/security/scan-input/cis0005-closeout-dependencies-20261124:/scan:ro" -v "C:/miscwork/portfolio/andrewspiteri.dev/content/change-impact-studio/.cis/local/security/results:/results:rw" -v "C:/miscwork/portfolio/andrewspiteri.dev/content/change-impact-studio/.cis/local/security/cache/cis0005-closeout-trivy-20261124:/cache:rw" ghcr.io/aquasecurity/trivy@sha256:ee940acbf1f58ebadb42d01434ce4609530bf1b52536afbd1eee66cd7123c5c9 fs --cache-dir /cache --format json --output /results/trivy-fs.json --scanners vuln /scan | . | cis-filesystem | release-gitleaks-image | no | 300 |

Execution is conditional on actual admission, not granted by this definition.
Admit the complete sequential peak before launch and monitor the image-release
and next-scanner boundaries. If reclamation or the next phase does not fit, stop
the owned workflow and exact container; no unsupported pause command is implied.
The sum of step ceilings is not a new parent allocation. A failed or unavailable
scanner remains a closing blocker.
