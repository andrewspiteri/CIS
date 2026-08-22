---
title: "UI Framework Resolution Specification"
type: architecture-specification
status: Draft
owner: Andrew Spiteri
review_cadence: on supported frontend classification change
cis:
  stable_id: change-impact-studio:spec:ui-framework-resolution
---

# UI Framework Resolution Specification

## Purpose

Define how `cis repo init` discovers an existing UI component framework and, only
when none is evidenced, assigns a platform-appropriate default. The result guides
wireframing, design rendering, planning, and implementation without making repository
initialization a package installer.

## Required workflow

For every classified UI-bearing component, CIS must:

1. inspect package manifests, framework configuration, imports and source markers,
   shared component directories, the application shell, and theme assets;
2. record an evidenced existing component framework as `Existing`;
3. otherwise record the classification-bound framework as `Default`;
4. write the result to `<root>/references/ui-framework-profile.md` and seed a scoped
   agent instruction;
5. preserve human-authored exceptions and require an ADR or approved design-system
   decision when the exception changes architecture; and
6. reconcile the profile idempotently when projects or dependencies change.

An existing system always takes precedence over a CIS default. Styling infrastructure
such as Tailwind CSS does not by itself prove that a component framework exists.
Initialization records a policy choice; it must not install, remove, or migrate packages.

## Default matrix

| Classified application | Default component framework | Styling and theming | Motion policy |
|---|---|---|---|
| React or Next.js | shadcn/ui | Tailwind CSS | CSS transitions first; Motion for React only for approved complex motion |
| Angular | Angular Material | Angular Material theming | CSS and framework-native transitions first |
| Vue or Nuxt | Nuxt UI | Tailwind CSS | CSS transitions first; add a motion library only when required |
| Apple native | SwiftUI | SwiftUI styles, environment, and design tokens | SwiftUI animation APIs |
| Android native | Jetpack Compose Material 3 | Material 3 theme and design tokens | Compose animation APIs |
| Godot | Control nodes and Theme resources | Godot Theme resources | AnimationPlayer or Tween only when behavior requires motion |

The default is a starting constraint, not permission to replace an existing system.
When platform or deployment constraints make the default unsuitable, the change must
surface a human decision rather than silently choosing another framework.

## React and the Sera UI example

Sera UI is a valid React/Next option: it distributes source-owned components through
a shadcn-compatible registry, uses Tailwind CSS for theming, and uses Framer Motion.
CIS therefore detects and preserves Sera UI when its registry or source markers exist.
It is not the universal default because it is React-specific and because animation is
not a baseline requirement for every product.

For an otherwise unconfigured React or Next.js component, CIS defaults to shadcn/ui.
Its open-code, composable, registry/schema model fits agent-driven delivery while
keeping the application in control of component source. Sera UI remains an explicitly
selectable compatible registry source. The current Motion package name is `motion`;
legacy `framer-motion` is also detected. Simple visual effects should remain CSS.

## Existing-framework evidence

Deterministic package detection currently recognizes Angular Material, Material UI,
Chakra UI, Ant Design, Mantine, HeroUI/NextUI, Radix UI, Headless UI, React Aria,
Fluent UI, PrimeReact, PrimeVue, Vuetify, Nuxt UI, Quasar, Naive UI, Element Plus,
Bootstrap, daisyUI, and Flowbite. It also detects shadcn `components.json`, Sera registry
markers, Tailwind CSS, and Motion dependencies. SwiftUI, UIKit, AppKit, Jetpack Compose,
and Android Views are preserved from native source, build, and layout evidence.

Detection is deliberately conservative. If source evidence conflicts with the generated
profile, an agent must stop, rerun initialization, and present the reconciliation for
human review rather than introducing a second system.

## Generated contract

The canonical generated profile contains component ID and root, classified application
framework, `Existing` or `Default` resolution, effective UI framework, styling/theming
and motion conventions, deterministic evidence, and per-component rationale.

The scoped agent instruction requires the same evidence-first check before design or
frontend work. The design-review skill consumes the profile before reusable shell and
component templates are selected.

## Primary references

- [Sera UI documentation](https://seraui.com/docs)
- [Sera UI source and registry usage](https://github.com/seraui/seraui)
- [shadcn/ui documentation](https://ui.shadcn.com/docs)
- [Motion for React documentation](https://motion.dev/docs/react)
- [Angular Material](https://material.angular.dev/)
- [Nuxt UI](https://ui.nuxt.com/)
- [SwiftUI](https://developer.apple.com/documentation/swiftui)
- [Jetpack Compose](https://developer.android.com/develop/ui/compose/documentation)
