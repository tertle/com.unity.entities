# Entities Fork
This fork provides performance optimizations, quick fixes, and improvements to the entities package. It will never add new features. Therefore, as long as you don't depend on some obscure behavior, you should always be able to switch between the official package and this fork without any issues.

## Changes
### Fixed
- FixedRateCatchUpManager breaking RewindAllocator when running 3+ times.
- WithNone when IEnableable not adding dependency to system.
-  Workaround for entities hierarchy and system windows unable to select things in Unity 2023.X.

### Changed
- SystemState.BeforeOnUpdate.Complete() removed to stop sync points in fixed updated. Replaced with CompleteWorldDependencySystem.
- GetEntity no longer forces LinkedEntityGroup and TransformUsageFlags.Dynamic on prefabs that don't have children.
- Added readonly to a few DynamicBuffer, LocalTransform and Entity methods to stop 'struct value always copied before invocation'.

### Added
- A toggle in Preferences -> Entities to stop Closed SubScenes that have been Loaded to no longer bake on changes during play mode.
- Support for PropertyInspector<DynamicBuffer<T>>.

# About Entities
The Entities package provides a modern Entity Component System (ECS) implementation with a basic set of systems and components made for Unity.

## Installing Entities
To install this package, follow the instructions [in the documentation](Documentation~/getting-started-installation.md).

## Using Entities
For information on how to use the package, see the [User manual](Documentation~/index.md)
