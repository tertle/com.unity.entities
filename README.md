# Entities Fork
This fork provides performance optimizations, quick fixes, and improvements to the entities package. It will never add new features. Therefore, as long as you don't depend on some obscure behavior, you should always be able to switch between the official package and this fork without any issues.

## Changes
### Fixed
- EntityQuery now adds Any IEnableable dependencies to systems.

### Changed
- SystemState.BeforeOnUpdate.Complete() removed to stop sync points in fixed updated. Replaced with CompleteWorldDependencySystem.
- GetEntity no longer forces LinkedEntityGroup and TransformUsageFlags.Dynamic on prefabs that don't have children.
- Made DynamicBuffer readonly.
- Added readonly to a variety of LocalTransform and Entity methods.
- Added readonly to a few DynamicBuffer, LocalTransform and Entity methods to stop 'struct value always copied before invocation'.
- Removed a lot of obsolete methods.

### Added
- Support for PropertyInspector<DynamicBuffer<T>>.

# About Entities
The Entities package provides a modern Entity Component System (ECS) implementation with a basic set of systems and components made for Unity.

## Installing Entities
To install this package, follow the instructions [in the documentation](Documentation~/getting-started-installation.md).

## Using Entities
For information on how to use the package, see the [User manual](Documentation~/index.md)
