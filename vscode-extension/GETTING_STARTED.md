# How to use CIS

If you update a feature BRD externally, reopen the feature under **High-level features**
and select **Reimport BRD**. Choose the updated Markdown file, compare its changes, then
select **Apply updated BRD**. CIS keeps saved answers, repository work and earlier source
versions. Review the retained answers against the updated requirements before saving again.

The feature wizard presents separate questions for technical choices, architecture,
integration contracts, experience and delivery. Review each formatted suggestion and use
**Edit answer** to change it. **Save and continue** moves to the next completed step;
partial answers are saved and the remaining questions stay visible.

For an existing product, open **High-level wizard → Technical direction → Infer from existing
repositories** after drafting the BRD. CIS reads implementation/test evidence to explain the
current architecture and leaves unsupported future choices for review. You can draft while
the BRD is under detailed review; activation and downstream work still require the full
reviewed baseline. Evidence links remain in comments in the technical document.

CIS helps you define a product, understand what a change affects, and deliver it through
reviewed work. Start with the product’s purpose and boundaries, then work on one feature at
a time. Requirements, designs, decisions, plans, and results live in your repository.

Open **CIS: Getting Started** from the Command Palette, or choose **Getting Started** at the
top of the CIS Product view. Its buttons take you through the steps below.

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

Open **Product** for the product definition and repositories. **High-level features**
keeps every saved feature visible, and **Workflow guide** explains the delivery stages.

After product definition is approved:

If you have a prepared BRD for a new feature, choose **Add feature from BRD** in
Getting Started, the **+** button in High-level features, or **CIS: Add Feature
from BRD** in the Command Palette. Select the Markdown BRD, enter a feature name,
choose a new or existing implementation repository, and select integration targets.
Review the setup preview, then choose **Create feature request**. CIS creates and
connects the repository and preserves the supplied BRD under the authority. This
works when the current backlog has no planned work.

The resulting request is Draft. **Review feature request** and **Open original BRD**
open documents beside the setup page. Review unresolved decisions and reconcile
the proposed scope into the product backlog before starting its specification.

In the feature's **Solution architecture and diagrams** step, review the suggested
direction and choose **Save answers and generate C4 diagrams**. The context, container
and component views appear in the wizard, with buttons to open full-size diagrams or
save SVGs. Edit the architecture answers and regenerate when the proposed design changes.

In **Experience direction and UI impact**, review the prefilled answers and choose
**Save answers and generate screens**. CIS creates proposed screens from this feature's
BRD and the existing UI baseline using your local model. Open the images in new tabs or
save JPGs for review. To refine one screen, describe the changes below its image and choose
**Apply changes to this screen**. Choose **Not needed** to exclude it, or **Include this
screen again** to undo an exclusion. CIS retains these decisions with the feature.
If an amendment fails, the saved feedback and previous image remain available.
Edit the experience answers and regenerate when the overall direction changes.

Continue through the **Feature definition wizard**: business definition, technical
direction, architecture, integrations and dictionaries, UI impact, delivery and final
review. Each page links to the existing product baseline and explains what needs
attention. Review the suggested BRD excerpts and use **Save reviewed answers**. Closing
the wizard keeps your page and draft text; **CIS: Open Feature Definition Wizard**
resumes it. Click any saved feature in **High-level features** to reopen it directly.
The wizard also keeps links to the other features available while you review a feature.

In **Delivery and acceptance**, choose **Add repository feature** to break the product
feature into work for its repositories. Give each item a title and scope, select dependencies,
and optionally link existing change dossiers. Several items can belong to the same repository.
Independent items can be planned in parallel. Use **Save reviewed answers** to save the
breakdown with the delivery page. Other features retain their own answers and drafts.

Recording the final definition review does not approve implementation. Its next-action
buttons lead to product reconciliation and the approved backlog's feature-specification flow.

1. Choose a feature from the backlog and prepare its specification.
2. Review the feature and its change plan, including affected repositories and designs.
3. Run the planned implementation tasks and inspect progress in **Changes** and **Runs**.
4. Review verification evidence, resolve findings, and accept the outcome.
5. Return to the next feature. Keep the shared product documents current as features land.

## When you need help

- **Getting Started** explains setup and lets you return to the wizard.
- **Workflow guide** shows the broader sequence and your next available actions.
- **Documents** opens the documents behind an assessment.
- **Repository Doctor** explains setup and health findings. Review a suggested fix, then
  choose **Run command** or **Copy command**. A command with placeholders needs editing first.
- **Runs** shows what an agent or verification process did and whether it completed.

For settings, provider setup, command details, and troubleshooting, read the
[CIS extension manual](README.md).
