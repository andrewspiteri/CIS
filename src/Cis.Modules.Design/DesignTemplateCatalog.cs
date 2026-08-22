namespace Cis.Modules.Design;

public sealed class DesignTemplateCatalog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Dependencies =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["shell.standard-app"] = ["component.page-header", "component.button", "component.status-badge", "component.card"],
            ["shell.minimal-app"] = ["component.page-header", "component.button", "component.status-badge", "component.card"],
            ["component.page-header"] = ["component.button", "component.status-badge"],
            ["component.table"] = ["component.button", "component.status-badge"],
            ["component.filter-bar"] = ["component.button"],
            ["component.form"] = ["component.button", "component.text-input", "component.select", "component.textarea", "component.checkbox"],
            ["component.empty-state"] = ["component.button"],
            ["component.dialog"] = ["component.button"],
        };

    private static readonly IReadOnlyList<DesignTemplate> All =
    [
        new("shell.standard-app", "1.0", "shell", "Dark application rail, top bar, route context, and governed content canvas.", 900),
        new("shell.minimal-app", "1.0", "shell", "Header-only application shell for simple, single-surface products that do not need persistent navigation.", 700),
        new("component.page-header", "1.0", "component", "Consistent title, description, status, and primary action hierarchy.", 240),
        new("component.button", "1.0", "component", "Primary, secondary, destructive, icon, disabled, loading, and focus-visible button states.", 320),
        new("component.text-input", "1.0", "component", "Labeled text input with helper, placeholder, required, focus, disabled, and validation states.", 300),
        new("component.select", "1.0", "component", "Labeled select/dropdown with placeholder, selected, open, disabled, and validation states.", 340),
        new("component.textarea", "1.0", "component", "Multiline input with label, helper, character count, resize affordance, and validation states.", 260),
        new("component.checkbox", "1.0", "component", "Checkbox with checked, unchecked, indeterminate, focus, disabled, and error states.", 220),
        new("component.radio-group", "1.0", "component", "Accessible mutually-exclusive options with selected, focus, disabled, and error states.", 240),
        new("component.toggle", "1.0", "component", "Boolean switch with on/off, focus, disabled, and policy-impact labels.", 220),
        new("component.tabs", "1.0", "component", "Route-aware tabs with active, hover, focus, disabled, and overflow behavior.", 300),
        new("component.breadcrumbs", "1.0", "component", "Consistent hierarchical path, current-page semantics, and truncation behavior.", 180),
        new("component.pagination", "1.0", "component", "Result range, page navigation, page size, disabled boundaries, and loading state.", 300),
        new("component.dropdown-menu", "1.0", "component", "Anchored action menu with groups, destructive separation, keyboard focus, and disabled items.", 320),
        new("component.dialog", "1.0", "component", "Modal confirmation/form shell with title, description, focus boundary, actions, and destructive variant.", 440),
        new("component.alert", "1.0", "component", "Inline/banner feedback for information, success, warning, error, denied, and recovery actions.", 260),
        new("component.table", "1.0", "component", "Reusable table header, rows, status cells, empty state, and row action affordance.", 650),
        new("component.filter-bar", "1.0", "component", "Search, filters, applied-filter chips, and result count.", 360),
        new("component.status-badge", "1.0", "component", "Guideline-bound success, warning, critical, neutral, and accent status pills.", 180),
        new("component.card", "1.0", "component", "Standard bordered content card with restrained radius and hierarchy.", 180),
        new("component.form", "1.0", "component", "Labeled fields, helper text, validation, and action row.", 480),
        new("component.empty-state", "1.0", "component", "Consistent empty, denied, error, and recovery-state composition.", 260),
        new("component.timeline", "1.0", "component", "Audit/activity timeline with status-aware markers and metadata.", 300),
        new("component.accordion", "1.0", "component", "Expandable content groups with open, closed, focus, disabled, and nested-content behavior.", 260),
        new("component.avatar", "1.0", "component", "Person/entity identity with image, initials fallback, size, status, and accessible label.", 140),
    ];

    public IReadOnlyList<DesignTemplate> List() => All;

    public DesignTemplate? Find(string id)
        => All.FirstOrDefault(template => template.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> ResolveComponents(string shell, IReadOnlyList<string> requested)
    {
        var resolved = new HashSet<string>(requested, StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(new[] { shell }.Concat(requested));
        while (pending.TryDequeue(out var template))
        {
            if (!Dependencies.TryGetValue(template, out var dependencies)) continue;
            foreach (var dependency in dependencies)
            {
                if (resolved.Add(dependency)) pending.Enqueue(dependency);
            }
        }

        return All.Where(template => template.Kind == "component" && resolved.Contains(template.Id))
            .Select(template => template.Id).ToArray();
    }

    public IReadOnlyList<string> Validate(string shell, IReadOnlyList<string> components)
    {
        var errors = new List<string>();
        var shellTemplate = Find(shell);
        if (shellTemplate is null || shellTemplate.Kind != "shell")
        {
            errors.Add($"Unknown application-shell template: {shell}");
        }

        foreach (var component in components.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var template = Find(component);
            if (template is null || template.Kind != "component")
            {
                errors.Add($"Unknown component template: {component}");
            }
        }

        return errors;
    }
}
