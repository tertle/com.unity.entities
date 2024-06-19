# Entities Fork
This fork provides performance optimizations, quick fixes, and improvements to the entities package. It will never add new features. Therefore, as long as you don't depend on some obscure behavior, you should always be able to switch between the official package and this fork without any issues.

## Changes
## Fixes
- [WithPresent(T)] and EnabledRefRW<T> source generator query duplication for IJobEntity.
- [ChangeFilter] and [WithPresent] source generator query duplication.
- EntityComponentStore leak

### Changed
- SystemState.BeforeOnUpdate.Complete() removed to stop sync points in fixed updated. Replaced with CompleteWorldDependencySystem.
- GetEntity no longer forces LinkedEntityGroup and TransformUsageFlags.Dynamic on prefabs that don't have children.
- Made DynamicBuffer readonly.
- Added readonly to a variety of LocalTransform and Entity methods.
- Added readonly to a few DynamicBuffer, LocalTransform, Entity and ComponentType methods to stop 'struct value always copied before invocation'.
- Removed a lot of obsolete methods.
- System inspector now shows Disabled, Present, Absent and None components.

### Added
- A toggle in Preferences -> Entities to stop Closed SubScenes that have been Loaded to no longer bake on changes during play mode.
- Support for PropertyInspector<DynamicBuffer<T>>.

# About Entities
The Entities package provides a modern Entity Component System (ECS) implementation with a basic set of systems and components made for Unity.

## Installing Entities
To install this package, follow the instructions [in the documentation](Documentation~/getting-started-installation.md).

## Using Entities
For information on how to use the package, see the [User manual](Documentation~/index.md)
