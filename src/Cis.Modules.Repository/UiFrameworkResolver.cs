namespace Cis.Modules.Repository;

public sealed record UiFrameworkResolution(
    string ComponentId,
    string ComponentRoot,
    string ApplicationFramework,
    string Resolution,
    string UiFramework,
    string StylingAndTheming,
    string AnimationAndMotion,
    IReadOnlyList<string> Evidence,
    string Rationale);

public static class UiFrameworkResolver
{
    private static readonly IReadOnlyList<(string Marker, string Display)> ExistingUiFrameworks =
    [
        ("sera-ui", "Sera UI"),
        ("shadcn-ui", "shadcn/ui"),
        ("angular-material", "Angular Material"),
        ("material-ui", "Material UI"),
        ("chakra-ui", "Chakra UI"),
        ("ant-design", "Ant Design"),
        ("mantine", "Mantine"),
        ("heroui", "HeroUI"),
        ("fluent-ui", "Fluent UI"),
        ("primereact", "PrimeReact"),
        ("primevue", "PrimeVue"),
        ("vuetify", "Vuetify"),
        ("nuxt-ui", "Nuxt UI"),
        ("quasar", "Quasar"),
        ("naive-ui", "Naive UI"),
        ("element-plus", "Element Plus"),
        ("bootstrap", "Bootstrap"),
        ("daisyui", "daisyUI"),
        ("flowbite", "Flowbite"),
        ("radix-ui", "Radix UI"),
        ("headless-ui", "Headless UI"),
        ("react-aria", "React Aria"),
        ("swiftui", "SwiftUI"),
        ("uikit", "UIKit"),
        ("appkit", "AppKit"),
        ("jetpack-compose", "Jetpack Compose"),
        ("android-views", "Android Views"),
    ];

    public static IReadOnlyList<UiFrameworkResolution> Resolve(RepositoryClassification classification)
        => classification.Components
            .Select(Resolve)
            .Where(resolution => resolution is not null)
            .Cast<UiFrameworkResolution>()
            .OrderBy(resolution => resolution.ComponentId, StringComparer.Ordinal)
            .ToArray();

    private static UiFrameworkResolution? Resolve(RepositoryComponentClassification component)
    {
        var applicationFramework = ResolveApplicationFramework(component);
        if (applicationFramework is null)
        {
            return null;
        }

        var existing = ExistingUiFrameworks.FirstOrDefault(candidate =>
            component.Frameworks.Contains(candidate.Marker, StringComparer.Ordinal));
        if (!string.IsNullOrWhiteSpace(existing.Marker))
        {
            return CreateExisting(component, applicationFramework, existing.Marker, existing.Display);
        }

        return applicationFramework switch
        {
            "Next.js" or "React" => CreateDefault(
                component,
                applicationFramework,
                "shadcn/ui",
                Has(component, "tailwindcss") ? "Tailwind CSS (existing)" : "Tailwind CSS",
                Has(component, "motion")
                    ? "Motion for React (existing); CSS transitions for simple effects"
                    : "CSS transitions by default; Motion for React only for approved complex motion",
                "No component framework was detected. Use the open-code shadcn/ui component model as the React default; Sera UI may be selected as a compatible registry source when explicitly justified."),
            "Angular" => CreateDefault(
                component,
                applicationFramework,
                "Angular Material",
                "Angular Material theming",
                "CSS and framework-native transitions; add no general animation dependency by default",
                "No component framework was detected. Use Angular Material for native Angular integration and accessibility foundations."),
            "Vue/Nuxt" => CreateDefault(
                component,
                applicationFramework,
                "Nuxt UI",
                Has(component, "tailwindcss") ? "Tailwind CSS (existing)" : "Tailwind CSS",
                "CSS transitions by default; add a motion library only for approved complex motion",
                "No component framework was detected. Use Nuxt UI as the Vue/Nuxt open-source accessible component default."),
            "Apple native" => CreateDefault(
                component,
                applicationFramework,
                "SwiftUI",
                "SwiftUI environment, styles, and design tokens",
                "SwiftUI animation APIs",
                "No Apple UI framework was detected. Use SwiftUI unless deployment constraints require an approved UIKit decision."),
            "Android native" => CreateDefault(
                component,
                applicationFramework,
                "Jetpack Compose Material 3",
                "Material 3 theme and design tokens",
                "Jetpack Compose animation APIs",
                "No Android UI framework was detected. Use Jetpack Compose with Material 3 unless platform constraints require an approved Views decision."),
            "Godot" => CreateDefault(
                component,
                applicationFramework,
                "Godot Control nodes and Theme resources",
                "Godot Theme resources",
                "Godot AnimationPlayer or Tween only where interaction behavior requires motion",
                "Use the engine-native UI system and shared Theme resources; do not introduce a web component framework."),
            _ => null,
        };
    }

    private static UiFrameworkResolution CreateExisting(
        RepositoryComponentClassification component,
        string applicationFramework,
        string marker,
        string display)
    {
        var styling = Has(component, "tailwindcss")
            ? "Tailwind CSS (existing)"
            : marker == "angular-material"
                ? "Angular Material theming"
                : "Preserve the detected framework's existing theming and tokens";
        var motion = Has(component, "motion")
            ? "Motion (existing); CSS transitions for simple effects"
            : "Preserve existing motion conventions; add no animation dependency by default";

        return new UiFrameworkResolution(
            component.Id,
            component.Root,
            applicationFramework,
            "Existing",
            display,
            styling,
            motion,
            RelevantEvidence(component, marker),
            "An existing component framework was detected and takes precedence over CIS defaults.");
    }

    private static UiFrameworkResolution CreateDefault(
        RepositoryComponentClassification component,
        string applicationFramework,
        string uiFramework,
        string styling,
        string motion,
        string rationale)
        => new(
            component.Id,
            component.Root,
            applicationFramework,
            "Default",
            uiFramework,
            styling,
            motion,
            component.Evidence,
            rationale);

    private static string? ResolveApplicationFramework(RepositoryComponentClassification component)
    {
        if (Has(component, "nextjs")) return "Next.js";
        if (Has(component, "react")) return "React";
        if (Has(component, "angular")) return "Angular";
        if (Has(component, "vue")) return "Vue/Nuxt";
        if (component.Languages.Contains("swift", StringComparer.Ordinal)
            && component.Roles.Any(role => role is "mobile-client" or "native-frontend")) return "Apple native";
        if (component.Languages.Contains("kotlin", StringComparer.Ordinal)
            && (Has(component, "android")
                || Has(component, "jetpack-compose")
                || component.Roles.Any(role => role is "mobile-client" or "native-frontend"))) return "Android native";
        if (Has(component, "godot")) return "Godot";
        return null;
    }

    private static IReadOnlyList<string> RelevantEvidence(RepositoryComponentClassification component, string marker)
    {
        var values = component.Evidence
            .Where(value => value.Contains(marker.Split('-')[0], StringComparison.OrdinalIgnoreCase)
                || value.Contains("package", StringComparison.OrdinalIgnoreCase)
                || value.Contains("registry", StringComparison.OrdinalIgnoreCase)
                || value.Contains("component", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return values.Length > 0 ? values : component.Evidence;
    }

    private static bool Has(RepositoryComponentClassification component, string framework)
        => component.Frameworks.Contains(framework, StringComparer.Ordinal);
}
