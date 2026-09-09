# How to use CIS

For an existing product, open **High-level wizard → Technical direction → Infer from existing
repositories** after drafting the BRD. CIS reads implementation/test evidence to explain the
current architecture and leaves unsupported future choices for review. You can draft while
the BRD is under detailed review; activation and downstream work still require the full
reviewed baseline. Evidence links remain in comments in the technical document.

CIS helps you define a product, understand what a change affects, and deliver it through
reviewed work. Start with the product’s purpose and boundaries, then work on one feature at
a time. Requirements, designs, decisions, plans, and results live in your repository.

Open **CIS: Getting Started** from the Command Palette, or choose **Getting Started** at the
top of the CIS Workspace view. Its buttons take you through the steps below.

## 1. Choose the authority folder

The **authority** is the folder that holds the shared product documents and decisions.
One authority governs one product. The **ecosystem** names the wider group of related products.

For a system spread across several repositories, a dedicated documentation repository is
a useful home for the authority. For example:

```text
bridgelink/
  bridgelink-docs/                ← authority for Fixed Term Deposits
  bridgelink-backend/             ← product application
  bridgelink-frontend/            ← customer application
  bridgelink-backoffice-frontend/ ← staff application
  bridgelink-azure-functions/     ← background functions
```

Open the authority folder in VS Code, or open a workspace containing these folders and use
**Select authority folder**. Selecting a folder tells the extension where to work; it does
not initialize files or import the other repositories.

## 2. Initialize the authority

Choose **Initialize authority**. Enter the product and ecosystem names and their stable IDs,
then choose a documentation folder such as `docs/cis`. Review the initialization plan and
confirm it. CIS creates the authority configuration and starting documents, then builds
the context graph used to connect evidence.

An existing initialized authority shows **Authority initialized**. Continue from there;
you do not need to initialize it again.

If a command cannot start, check **CIS: Executable Path** in VS Code Settings. It must point
to the CIS executable, with no command arguments. Use a current CLI build with the extension.
Workspace trust is required to run commands; you can read this guide without it.

## 3. Connect the application repositories

For an existing system, choose **Import repositories** before starting product definition.
Select the application repositories and identify them as owned by this product. CIS
classifies and indexes their existing source in place.

An external dependency belongs to another product. Import it as a dependency and describe
whether it provides capabilities to this product, consumes this product’s capabilities,
or does both. Its evidence helps explain integrations.

For a new product with no application code yet, continue to the wizard. Repositories can
be added as the implementation grows.

## 4. Work through the high-level wizard

Choose **Open high-level wizard**. Large systems may take a few minutes to load; VS Code
shows progress while CIS reads their evidence.
While a wizard action is running, its controls are disabled. Wait for it to finish; repeated
clicks do not start another preparation. Reopening the wizard brings back the same page.

| Page | What you do |
| --- | --- |
| Project foundation | Check the authority and available references. |
| Business definition | For an imported system, use **Prepare existing-system context** to populate and inspect the dictionaries, then **Infer from existing project** to draft the business requirements document (BRD) from its repository evidence. Review the draft and sources, run independent review, and resolve open questions. |
| Technical direction | Review evidence-supported choices and supply the decisions the existing system cannot establish. |
| Solution architecture | Review component ownership, interactions, and architecture diagrams. |
| Contracts and dictionaries | Review the shared APIs, records, events, permissions, and other applicable references. |
| Experience direction | Choose and review the product’s UI direction and representative preview. |
| Delivery map | Review outcomes, dependencies, and the high-level backlog. |
| Review and activate | Resolve the reported gaps and explicitly approve the complete product baseline. |

On **Business definition**, **Infer from existing project** preselects the imported
product repositories. Review that selection, choose an agent provider, and confirm the
evidence disclosure. CIS first populates draft API, data, relationship, workflow, permission
and route inventories and refreshes repository graphs. **Prepare existing-system context**
lets you inspect the counts and open those dictionaries beforehand. Existing reviewed content
is preserved. CIS gives the selected provider cached implementation and test files, a coverage
checklist and dictionary navigation. It traces how the product works and writes a business
narrative, with source references hidden in Markdown comments. The draft remains Review
Required; unsupported business decisions remain open questions. To inspect the source-sharing
payload locally first, use `cis agent discover brd --reference <owned-repo> --actor <human>`.
Then use
**Open BRD and source evidence**, **Independent review**, and **Answer open questions**.
For a new product, use **Draft from references** or edit the BRD directly.

On the other definition pages, use **Prepare or refresh page** to generate or update artifacts.
Use **Continue** to move forward when a page is complete. You can revisit any page and edit
its documents. Drafting and independent review help you work through the content; they do
not supply stakeholder decisions or approve the product for you.

## 5. Find the next action and deliver a feature

Open **Journey Map** to see the product’s current stage. The **Workspace** view also shows
a **Next** action based on the current evidence.

After product definition is approved:

1. Choose a feature from the backlog and prepare its specification.
2. Review the feature and its change plan, including affected repositories and designs.
3. Run the planned implementation tasks and inspect progress in **Changes** and **Runs**.
4. Review verification evidence, resolve findings, and accept the outcome.
5. Return to the next feature. Keep the shared product documents current as features land.

## When you need help

- **Getting Started** explains setup and lets you return to the wizard.
- **Journey Map** shows the broader sequence and your next available actions.
- **Evidence** opens the documents behind an assessment.
- **Repository Doctor** explains setup and health findings. Review a suggested fix, then
  choose **Run command** or **Copy command**. A command with placeholders needs editing first.
- **Runs** shows what an agent or verification process did and whether it completed.

For settings, provider setup, command details, and troubleshooting, read the
[CIS extension manual](README.md).
