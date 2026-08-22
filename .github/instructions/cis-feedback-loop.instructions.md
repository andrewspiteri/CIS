---
applyTo: "**"
---

# CIS feedback-loop guidance

- CIS automatically records every CLI invocation that can be associated with an initialized repository or workspace.
- Treat `.cis/local/feedback/tool-usage.jsonl` as disposable local evidence, not canonical documentation.
- Use `cis feedback summary` and `cis feedback opportunities`; do not parse or edit the ledger directly in normal workflows.
- Token savings are estimates. Preserve the recorded basis and confidence, and report zero when no defensible counterfactual exists.
- Command output and option values must not be persisted. Never add secrets, prompts, source content, or credentials to the ledger schema.
- After repeated failures, inspect recent evidence and run `cis repo doctor` before retrying blindly.