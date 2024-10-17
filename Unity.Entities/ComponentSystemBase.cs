using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Core;
using UnityEngine.Profiling;
using UnityEngine.Scripting;

namespace Unity.Entities
{
    /// <summary>
    /// A system provides behavior in an ECS architecture.
    /// </summary>
    /// <remarks>
    /// System implementations should inherit <see cref="SystemBase"/>, which is a subclass of ComponentSystemBase.
    /// </remarks>
    [RequireDerived]
    public abstract unsafe partial class ComponentSystemBase
    {
        /// <summary>
        /// Initializes and returns an instance of a system.
        /// </summary>
        [RequiredMember]
        public ComponentSystemBase()
        {
        }

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        internal SystemState* m_StatePtr;

        // This property exists so that the SystemSate is visible in the .NET Debugger.
        SystemState_ SystemState => SystemState_.FromPointer(m_StatePtr);

        internal SystemState* CheckedState()
        {
            var state = m_StatePtr;
            if (state == null)
            {
                throw new InvalidOperationException("system state is not initialized or has already been destroyed");
            }
            return state;
        }


        /// <summary>
        /// Controls whether this system executes when its OnUpdate function is called.
        /// </summary>
        /// <value>True, if the system is enabled.</value>
        /// <remarks>The Enabled property is intended for debugging so that you can easily turn on and off systems
        /// from the Entity Debugger window. A system with Enabled set to false will not update, even if its
        /// <see cref="ShouldRunSystem"/> function returns true. </remarks>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public bool Enabled { get => CheckedState()->Enabled; set => CheckedState()->Enabled = value; }

        /// <summary>
        /// The query objects cached by this system.
        /// </summary>
        /// <remarks>A system caches any queries it implicitly creates through the IJob interfaces or
        /// <see cref="EntityQueryBuilder"/>, that you create explicitly by calling <see cref="GetEntityQuery"/>, or
        /// that you add to the system as a required query with <see cref="RequireForUpdate"/>.
        /// Implicit queries may be created lazily and not exist before a system has run for the first time. </remarks>
        /// <value>A read-only array of the cached <see cref="EntityQuery"/> objects.</value>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public EntityQuery[] EntityQueries => UnsafeListToRefArray(ref CheckedState()->EntityQueries);

        internal static EntityQuery[] UnsafeListToRefArray(ref UnsafeList<EntityQuery> objs)
        {
            EntityQuery[] result = new EntityQuery[objs.Length];
            for (int i = 0; i < result.Length; ++i)
            {
                result[i] = objs.Ptr[i];
            }
            return result;
        }

        /// <summary>
        /// The current change version number in this <see cref="World"/>.
        /// </summary>
        /// <remarks>The system updates the component version numbers inside any <see cref="ArchetypeChunk"/> instances
        /// that this system accesses with write permissions to this value. </remarks>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public uint GlobalSystemVersion => EntityManager.GlobalSystemVersion;

        /// <summary>
        /// The current version of this system.
        /// </summary>
        /// <remarks>
        /// LastSystemVersion is updated to match the <see cref="GlobalSystemVersion"/> whenever a system runs.
        ///
        /// When you use <seealso cref="EntityQuery.SetChangedVersionFilter(ComponentType)"/>
        /// or <seealso cref="ArchetypeChunk.DidChange"/>, LastSystemVersion provides the basis for determining
        /// whether a component could have changed since the last time the system ran.
        ///
        /// When a system accesses a component and has write permission, it updates the change version of that component
        /// type to the current value of LastSystemVersion. The system updates the component type's version whether or not
        /// it actually modifies data in any instances of the component type -- this is one reason why you should
        /// specify read-only access to components whenever possible.
        ///
        /// For efficiency, ECS tracks the change version of component types by chunks, not by individual entities. If a system
        /// updates the component of a given type for any entity in a chunk, then ECS assumes that the components of all
        /// entities in that chunk could have been changed. Change filtering allows you to save processing time by
        /// skipping all entities in an unchanged chunk, but does not support skipping individual entities in a chunk
        /// that does contain changes.
        /// </remarks>
        /// <value>The <see cref="GlobalSystemVersion"/> the last time this system ran.</value>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public uint LastSystemVersion => CheckedState()->m_LastSystemVersion;

        /// <summary>
        /// The EntityManager object of the <see cref="World"/> in which this system exists.
        /// </summary>
        /// <value>The EntityManager for this system.</value>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public EntityManager EntityManager => CheckedState()->m_EntityManager;

        /// <summary>
        /// The World in which this system exists.
        /// </summary>
        /// <value>The World of this system.</value>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public World World => m_StatePtr != null ? (World)m_StatePtr->m_World.Target : null;

        /// <summary>
        /// The SystemHandle of this system.
        /// </summary>
        /// <returns>
        /// If the system state is valid, the untyped system's handle, otherwise default.
        /// </returns>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public SystemHandle SystemHandle => m_StatePtr != null ? m_StatePtr->SystemHandle : default;

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        void CheckSystemState()
        {
            if (m_StatePtr == null)
            {
                throw new InvalidOperationException($"System state pointer is null.");
            }
        }

        /// <summary>
        /// Retrieve world update allocator from system state.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public Allocator WorldUpdateAllocator
        {
            get
            {
                CheckSystemState();
                return m_StatePtr->WorldUpdateAllocator;
            }
        }

        /// <summary>
        /// Retrieve world rewindable allocator from system state.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        internal ref RewindableAllocator WorldRewindableAllocator
        {
            get
            {
                CheckSystemState();
                return ref m_StatePtr->WorldRewindableAllocator;
            }
        }

        // ============


        internal void CreateInstance(World world)
        {
            ref var worldImpl = ref World.Unmanaged.GetImpl();
            var previousSystemGlobalState = new WorldUnmanagedImpl.PreviousSystemGlobalState(ref worldImpl, m_StatePtr);

            OnBeforeCreateInternal(world);
            try
            {
                // Bump global system version to mean that this system OnCreate begins
                m_StatePtr->m_EntityComponentStore->IncrementGlobalSystemVersion(in m_StatePtr->m_Handle);

                OnCreateForCompiler();
                OnCreate();
                previousSystemGlobalState.Restore(ref worldImpl, m_StatePtr);

                // Bump global system version again to mean that this system OnCreate ends
                m_StatePtr->m_EntityComponentStore->IncrementGlobalSystemVersion();
            }
            catch
            {
                previousSystemGlobalState.Restore(ref worldImpl, m_StatePtr);
                OnBeforeDestroyInternal();
                OnAfterDestroyInternal();
                throw;
            }
        }


        [EditorBrowsable(EditorBrowsableState.Never)]
        protected virtual void OnCreateForCompiler()
        {
            //do not remove, source generators will emit methods that implement this method.
        }

        internal void DestroyInstance()
        {
            var previousSystemGlobalState = new WorldUnmanagedImpl.PreviousSystemGlobalState(ref World.Unmanaged.GetImpl(), m_StatePtr);

            try
            {
                OnBeforeDestroyInternal();
                OnDestroy();
            }
            finally
            {
                previousSystemGlobalState.Restore(ref World.Unmanaged.GetImpl(), m_StatePtr);
                OnAfterDestroyInternal();
            }
        }

        /// <summary>
        /// Called when this system is created.
        /// </summary>
        /// <remarks>
        /// Implement an OnCreate() function to set up system resources when it is created.
        ///
        /// OnCreate is invoked before the the first time <see cref="OnStartRunning"/> and OnUpdate are invoked.
        /// </remarks>
        [RequiredMember]
        protected virtual void OnCreate()
        {
        }

        /// <summary>
        /// Called before the first call to OnUpdate and when a system resumes updating after being stopped or disabled.
        /// </summary>
        /// <remarks>If the <see cref="EntityQuery"/> objects defined for a system do not match any existing entities
        /// then the system skips updates until a successful match is found. Likewise, if you set <see cref="Enabled"/>
        /// to false, then the system stops running. In both cases, <see cref="OnStopRunning"/> is
        /// called when a running system stops updating; OnStartRunning is called when it starts updating again.
        /// </remarks>
        [RequiredMember]
        protected virtual void OnStartRunning()
        {
        }

        /// <summary>
        /// Called when this system stops running because no entities match the system's <see cref="EntityQuery"/>
        /// objects or because you change the system <see cref="Enabled"/> property to false.
        /// </summary>
        /// <remarks>If the <see cref="EntityQuery"/> objects defined for a system do not match any existing entities
        /// then the system skips updating until a successful match is found. Likewise, if you set <see cref="Enabled"/>
        /// to false, then the system stops running. In both cases, OnStopRunning is
        /// called when a running system stops updating; <see cref="OnStartRunning"/> is called when it starts updating again.
        /// </remarks>
        [RequiredMember]
        protected virtual void OnStopRunning()
        {
        }

        internal virtual void OnStopRunningInternal()
        {
            OnStopRunning();
        }

        /// <summary>
        /// Called when this system is destroyed.
        /// </summary>
        /// <remarks>Systems are destroyed when the application shuts down, the World is destroyed, or you
        /// call <see cref="World.DestroySystem"/>. In the Unity Editor, system destruction occurs when you exit
        /// Play Mode and when scripts are reloaded.</remarks>
        [RequiredMember]
        protected virtual void OnDestroy()
        {
        }

        internal void OnDestroy_Internal()
        {
            OnDestroy();
        }

        /// <summary>
        /// Executes the system immediately.
        /// </summary>
        /// <remarks>The exact behavior is determined by this system's specific subclass. </remarks>
        /// <seealso cref="SystemBase"/>
        /// <seealso cref="ComponentSystemGroup"/>
        /// <seealso cref="EntityCommandBufferSystem"/>
        abstract public void Update();

        // ===================


#if ENABLE_PROFILER
        internal string GetProfilerMarkerName()
        {
            string name = default;
            CheckedState()->m_ProfilerMarker.GetName(ref name);
            return name;
        }
#endif

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        void CheckExists()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
            if (m_StatePtr != null && World != null && World.IsCreated) return;

            throw new InvalidOperationException(
                $"System {GetType()} is invalid. This usually means it was not created with World.GetOrCreateSystem<{GetType()}>() or has already been destroyed.");
#endif
        }

        /// <summary>
        /// Reports whether this system satisfies the criteria to update. This function is used
        /// internally to determine whether the system's OnUpdate function can be skipped.
        /// </summary>
        /// <remarks>
        /// <p>
        /// By default, systems will invoke OnUpdate every frame.
        /// </p>
        /// <p>
        /// If a system calls <see cref="M:Unity.Entities.ComponentSystemBase.RequireForUpdate``1"/>
        /// or <see cref="M:Unity.Entities.ComponentSystemBase.RequireForUpdate(Unity.Entities.EntityQuery)"/>
        /// in OnCreate, it will only update if all of its required components exist and
        /// required queries match existing chunks. This check uses [IsEmptyIgnoreFilter], so the queries may
        /// still be empty if they use filters or [Enableable Components].
        /// </p>
        /// <p>
        /// If a system has the <see cref="RequireMatchingQueriesForUpdateAttribute"/> it will
        /// update if any EntityQuery it uses match existing chunks. This check also uses [IsEmptyIgnoreFilter],
        /// so all queries may still be empty if they use filters or [Enableable Components].
        /// </p>
        /// <p>
        /// Note: Other factors might prevent a system from updating, even if this method returns
        /// true. For example, a system will not be updated if its [Enabled] property is false.
        /// </p>
        ///
        /// [IsEmptyIgnoreFilter]: xref:Unity.Entities.EntityQuery.IsEmptyIgnoreFilter
        /// [Enableable Components]: xref:Unity.Entities.IEnableableComponent
        /// [Enabled]: xref:Unity.Entities.ComponentSystemBase.Enabled
        /// </remarks>
        /// <returns>True if the system should be updated, or false if not.</returns>
        public bool ShouldRunSystem() => CheckedState()->ShouldRunSystem();

        internal virtual void OnBeforeCreateInternal(World world)
        {
        }

        internal void OnAfterDestroyInternal()
        {
            var state = CheckedState();
            World.Unmanaged.DestroyManagedSystemState(state);
            m_StatePtr = null;
        }

        private static void DisposeQueries(ref UnsafeList<GCHandle> queries)
        {
            for (var i = 0; i < queries.Length; ++i)
            {
                var query = (EntityQuery)queries[i].Target;

#if ENABLE_UNITY_COLLECTIONS_CHECKS || UNITY_DOTS_DEBUG
                query._GetImpl()->_DisallowDisposing = 0;
#endif
                query.Dispose();
                queries[i].Free();
            }
        }

        internal virtual void OnBeforeDestroyInternal()
        {
            var state = CheckedState();

            if (state->PreviouslyEnabled)
            {
                state->PreviouslyEnabled = false;
                OnStopRunning();
            }
        }

        internal void BeforeUpdateVersioning()
        {
            var state = CheckedState();
            var store = state->m_EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore;

            store->IncrementGlobalSystemVersion(state->SystemHandle);

            ref var qs = ref state->EntityQueries;
            for (int i = 0; i < qs.Length; ++i)
            {
                qs[i].SetChangedFilterRequiredVersion(state->m_LastSystemVersion);
            }
        }

        internal void AfterUpdateVersioning()
        {
            var state = CheckedState();
            var store = state->m_EntityManager.GetCheckedEntityDataAccess()->EntityComponentStore;

            // Store global system version before incrementing it again
            state->m_LastSystemVersion = store->GlobalSystemVersion;

            // Passing 'default' to mean that we are no longer within an executing system
            store->IncrementGlobalSystemVersion(default);
        }

        internal void CompleteDependencyInternal()
        {
            CheckedState()->CompleteDependencyInternal();
        }

        /// <summary>
        /// Manually gets the run-time type information required to access an array of component data in a chunk.
        /// </summary>
        /// <remarks>Remember to call <see cref="ComponentTypeHandle{T}.Update(SystemBase)"/>. </remarks>
        /// <param name="isReadOnly">Whether the component data is only read, not written. Access components as
        /// read-only whenever possible.</param>
        /// <typeparam name="T">A struct that implements <see cref="IComponentData"/>.</typeparam>
        /// <returns>An object representing the type information required to safely access component data stored in a
        /// chunk.</returns>
        /// <remarks>Pass an <see cref="ComponentTypeHandle{T}"/> instance to a job that has access to chunk data,
        /// such as an <see cref="IJobChunk"/> job, to access that type of component inside the job. </remarks>
        /// <remarks> Prefer using <see cref="SystemAPI.GetComponentTypeHandle{T}"/> in <see cref="SystemAPI"/> as it will cache in OnCreate for you
        /// and call .Update(this) at the call-site. </remarks>
        public ComponentTypeHandle<T> GetComponentTypeHandle<T>(bool isReadOnly = false) where T : unmanaged, IComponentData
        {
            return CheckedState()->GetComponentTypeHandle<T>(isReadOnly);
        }

        /// <summary>
        /// Manually gets the run-time type information required to access an array of component data in a chunk.
        /// </summary>
        /// <remarks>Remember to call <see cref="DynamicComponentTypeHandle.Update(SystemBase)"/>. </remarks>
        /// <param name="componentType">Type of the component</param>
        /// <returns>An object representing the type information required to safely access component data stored in a
        /// chunk.</returns>
        /// <remarks>Pass an DynamicComponentTypeHandle instance to a job that has access to chunk data, such as an
        /// <see cref="IJobChunk"/> job, to access that type of component inside the job. </remarks>
        public DynamicComponentTypeHandle GetDynamicComponentTypeHandle(ComponentType componentType)
        {
            return CheckedState()->GetDynamicComponentTypeHandle(componentType);
        }

        /// <summary>
        /// Manually gets the run-time type information required to access an array of buffer components in a chunk.
        /// </summary>
        /// <remarks>Remember to call <see cref="BufferTypeHandle{T}.Update(SystemBase)"/>. </remarks>
        /// <param name="isReadOnly">Whether the data is only read, not written. Access data as
        /// read-only whenever possible.</param>
        /// <typeparam name="T">A struct that implements <see cref="IBufferElementData"/>.</typeparam>
        /// <returns>An object representing the type information required to safely access buffer components stored in a
        /// chunk.</returns>
        /// <remarks>Pass a BufferTypeHandle instance to a job that has access to chunk data, such as an
        /// <see cref="IJobChunk"/> job, to access that type of buffer component inside the job. </remarks>
        /// <remarks> Prefer using <see cref="SystemAPI.GetBufferTypeHandle{T}"/> in <see cref="SystemAPI"/> as it will cache in OnCreate for you
        /// and call .Update(this) at the call-site. </remarks>
        public BufferTypeHandle<T> GetBufferTypeHandle<T>(bool isReadOnly = false)
            where T : unmanaged, IBufferElementData
        {
            return CheckedState()->GetBufferTypeHandle<T>(isReadOnly);
        }

        /// <summary>
        /// Manually gets the run-time type information required to access a shared component data in a chunk.
        /// </summary>
        /// <remarks>Remember to call <see cref="SharedComponentTypeHandle{T}.Update(SystemBase)"/>. </remarks>
        /// <typeparam name="T">A struct that implements <see cref="ISharedComponentData"/>.</typeparam>
        /// <returns>An object representing the type information required to safely access shared component data stored in a
        /// chunk.</returns>
        /// <remarks> Prefer using <see cref="SystemAPI.GetSharedComponentTypeHandle{T}"/> in <see cref="SystemAPI"/> as it will cache in OnCreate for you
        /// and call .Update(this) at the call-site. </remarks>
        public SharedComponentTypeHandle<T> GetSharedComponentTypeHandle<T>()
            where T : struct, ISharedComponentData
        {
            return CheckedState()->GetSharedComponentTypeHandle<T>();
        }

        /// <summary>
        /// Manually gets the run-time type information required to access a shared component data in a chunk.
        /// </summary>
        /// <remarks>Remember to call <see cref="DynamicSharedComponentTypeHandle.Update(SystemBase)"/>. </remarks>
        /// <param name="componentType">The component type to get access to.</param>
        /// <returns>An object representing the type information required to safely access shared component data stored in a
        /// chunk.</returns>
        public DynamicSharedComponentTypeHandle GetDynamicSharedComponentTypeHandle(ComponentType componentType)
        {
            return CheckedState()->GetDynamicSharedComponentTypeHandle(componentType);
        }

        /// <summary>
        /// Manually gets the run-time type information required to access the array of <see cref="Entity"/> objects in a chunk.
        /// </summary>
        /// <remarks>Remember to call <see cref="EntityTypeHandle.Update(SystemBase)"/>. </remarks>
        /// <returns>An object representing the type information required to safely access Entity instances stored in a
        /// chunk.</returns>
        /// <remarks> Prefer using <see cref="SystemAPI.GetEntityTypeHandle"/> in <see cref="SystemAPI"/> as it will cache in OnCreate for you
        /// and call .Update(this) at the call-site. </remarks>
        public EntityTypeHandle GetEntityTypeHandle()
        {
            return CheckedState()->GetEntityTypeHandle();
        }

        /// <summary>
        /// Manually gets a dictionary-like container containing all components of type T, keyed by Entity.
        /// </summary>
        /// <remarks>Remember to call <see cref="ComponentLookup{T}.Update(SystemBase)"/>. </remarks>
        /// <param name="isReadOnly">Whether the data is only read, not written. Access data as
        /// read-only whenever possible.</param>
        /// <typeparam name="T">A struct that implements <see cref="IComponentData"/>.</typeparam>
        /// <returns>All component data of type T.</returns>
        /// <remarks> Prefer using <see cref="SystemAPI.GetComponentLookup{T}"/> as it will cache in OnCreate for you
        /// and call .Update(this) at the call-site. </remarks>
        public ComponentLookup<T> GetComponentLookup<T>(bool isReadOnly = false)
            where T : unmanaged, IComponentData
        {
            return CheckedState()->GetComponentLookup<T>(isReadOnly);
        }

        /// <summary>
        /// Manually gets a BufferLookup&lt;T&gt; object that can access a <seealso cref="DynamicBuffer{T}"/>.
        /// </summary>
        /// <remarks>Remember to call <see cref="BufferLookup{T}.Update(SystemBase)"/>. </remarks>
        /// <remarks>Assign the returned object to a field of your Job struct so that you can access the
        /// contents of the buffer in a Job. </remarks>
        /// <param name="isReadOnly">Whether the buffer data is only read or is also written. Access data in
        /// a read-only fashion whenever possible.</param>
        /// <typeparam name="T">The type of <see cref="IBufferElementData"/> stored in the buffer.</typeparam>
        /// <returns>An array-like object that provides access to buffers, indexed by <see cref="Entity"/>.</returns>
        /// <seealso cref="ComponentLookup{T}"/>
        /// <remarks> Prefer using <see cref="SystemAPI.GetBufferLookup{T}"/> as it will cache in OnCreate for you
        /// and call .Update(this) at the call-site. </remarks>
        public BufferLookup<T> GetBufferLookup<T>(bool isReadOnly = false) where T : unmanaged, IBufferElementData
        {
            return CheckedState()->GetBufferLookup<T>(isReadOnly);
        }

        /// <summary>
        /// Manually gets an EntityStorageInfoLookup object that can access a <seealso cref="EntityStorageInfo"/>.
        /// </summary>
        /// <remarks>Remember to call <see cref="EntityStorageInfoLookup.Update(SystemBase)"/>. </remarks>
        /// <remarks>Assign the returned object to a field of your Job struct so that you can access the
        /// contents in a Job. </remarks>
        /// <returns>An dictionary-like object that provides access to information about how Entities are stored,
        /// indexed by <see cref="Entity"/>.</returns>
        /// <seealso cref="EntityStorageInfoLookup"/>
        /// <remarks> Prefer using <see cref="SystemAPI.GetEntityStorageInfoLookup"/> as it will cache in OnCreate for you
        /// and call .Update(this) at the call-site. </remarks>
        public EntityStorageInfoLookup GetEntityStorageInfoLookup()
        {
            return CheckedState()->GetEntityStorageInfoLookup();
        }

        /// <summary>
        /// Adds a query that must match entities for the system to run. You can add multiple required queries to a
        /// system; all of them must match at least one entity for the system to run.
        /// </summary>
        /// <param name="query">A query that must match entities this frame in order for this system to run.</param>
        /// <remarks>Any queries added through RequireForUpdate override all other queries cached by this system.
        /// In other words, if any required query does not find matching entities, the update is skipped even
        /// if another query created for the system (either explicitly or implicitly) does match entities and
        /// vice versa. </remarks>
        /// <remarks>Note that query filters are ignored so for components that implement <see cref="T:Unity.Entities.IEnableableComponent"/>
        /// this method ignores whether the component is enabled or not, it only checks whether it exists. </remarks>
        /// <seealso cref="M:Unity.Entities.ComponentSystemBase.ShouldRunSystem"/>
        /// <seealso cref="M:Unity.Entities.ComponentSystemBase.RequireForUpdate``1"/>
        /// <seealso cref="T:Unity.Entities.RequireMatchingQueriesForUpdateAttribute"/>
        public void RequireForUpdate(EntityQuery query)
        {
            CheckedState()->RequireForUpdate(query);
        }

        /// <summary>
        /// Provide a set of queries, one of which must match entities for the system to run.
        /// </summary>
        /// <param name="queries">A set of queries, one of which must match entities this frame in order for
        /// this system to run.</param>
        /// <remarks>
        /// This method can only be called from a system's OnCreate method.
        ///
        /// You can call this method multiple times from the same system to add multiple sets of required
        /// queries. Each set must have at least one query that matches an entity for the system to run.
        ///
        /// Any queries added through RequireAnyForUpdate and [RequireForUpdate] override all other queries
        /// created by this system for the purposes of deciding whether to update. In other words, if any set
        /// of required queries does not find matching entities, the update is skipped even if another query
        /// created for the system (either explicitly or implicitly) does match entities and vice versa.
        ///
        /// [EntityQueries]: xref:Unity.Entities.EntityQuery
        /// [enableable components]: xref:T:Unity.Entities.IEnableableComponent
        /// </remarks>
        /// <seealso cref="M:Unity.Entities.ComponentSystemBase.ShouldRunSystem"/>
        /// <seealso cref="T:Unity.Entities.RequireMatchingQueriesForUpdateAttribute"/>
        public void RequireAnyForUpdate(params EntityQuery[] queries)
        {
            fixed(EntityQuery* queriesPtr = queries)
            {
                CheckedState()->RequireAnyForUpdate(queriesPtr, queries.Length);
            }
        }

        /// <summary>
        /// Provide a set of queries, one of which must match entities for the system to run.
        /// </summary>
        /// <param name="queries">A set of queries, one of which must match entities this frame in order for
        /// this system to run.</param>
        /// <remarks>
        /// This method can only be called from a system's OnCreate method.
        ///
        /// You can call this method multiple times from the same system to add multiple sets of required
        /// queries. Each set must have at least one query that matches an entity for the system to run.
        ///
        /// Any queries added through RequireAnyForUpdate and [RequireForUpdate] override all other queries
        /// created by this system for the purposes of deciding whether to update. In other words, if any set
        /// of required queries does not find matching entities, the update is skipped even if another query
        /// created for the system (either explicitly or implicitly) does match entities and vice versa.
        ///
        /// [EntityQueries]: xref:Unity.Entities.EntityQuery
        /// [enableable components]: xref:T:Unity.Entities.IEnableableComponent
        /// </remarks>
        /// <seealso cref="M:Unity.Entities.ComponentSystemBase.ShouldRunSystem"/>
        /// <seealso cref="T:Unity.Entities.RequireMatchingQueriesForUpdateAttribute"/>
        public void RequireAnyForUpdate(NativeArray<EntityQuery> queries)
        {
            CheckedState()->RequireAnyForUpdate(queries);
        }

        /// <summary>
        /// Require that a specific component exist for this system to run.
        /// Also includes any components added to a system.
        /// See <see cref="Unity.Entities.SystemHandle"/> for more info on that.
        /// </summary>
        /// <typeparam name="T">The <see cref="IComponentData"/> subtype of the component.</typeparam>
        /// <remarks>Note that for components that implement <see cref="T:Unity.Entities.IEnableableComponent"/>
        /// this method ignores whether the component is enabled or not, it only checks whether it exists. </remarks>
        /// <seealso cref="M:Unity.Entities.ComponentSystemBase.ShouldRunSystem"/>
        /// <seealso cref="M:Unity.Entities.ComponentSystemBase.RequireForUpdate(Unity.Entities.EntityQuery)"/>
        /// <seealso cref="T:Unity.Entities.RequireMatchingQueriesForUpdateAttribute"/>
        public void RequireForUpdate<T>()
        {
            CheckedState()->RequireForUpdate<T>();
        }

        internal EntityQuery GetEntityQueryInternal(ComponentType* componentTypes, int count)
        {
            return CheckedState()->GetEntityQueryInternal(componentTypes, count);
        }

        internal EntityQuery GetEntityQueryInternal(ComponentType[] componentTypes)
        {
            fixed(ComponentType* componentTypesPtr = componentTypes)
            {
                return GetEntityQueryInternal(componentTypesPtr, componentTypes.Length);
            }
        }

        internal EntityQuery GetEntityQueryInternal(EntityQueryDesc[] desc)
        {
            return CheckedState()->GetEntityQueryInternal(desc);
        }

        /// <summary>
        /// Gets the cached query for the specified component types, if one exists; otherwise, creates a new query
        /// instance and caches it.
        /// </summary>
        /// <param name="componentTypes">An array or comma-separated list of component types.</param>
        /// <returns>The new or cached query.</returns>
        protected internal EntityQuery GetEntityQuery(params ComponentType[] componentTypes)
        {
            return GetEntityQueryInternal(componentTypes);
        }

        /// <summary>
        /// Gets the cached query for the specified component types, if one exists; otherwise, creates a new query
        /// instance and caches it.
        /// </summary>
        /// <param name="componentTypes">An array of component types.</param>
        /// <returns>The new or cached query.</returns>
        protected EntityQuery GetEntityQuery(NativeArray<ComponentType> componentTypes)
        {
            return GetEntityQueryInternal((ComponentType*)componentTypes.GetUnsafeReadOnlyPtr(),
                componentTypes.Length);
        }

        /// <summary>
        /// Combines an array of query description objects into a single query.
        /// </summary>
        /// <remarks>This function looks for a cached query matching the combined query descriptions, and returns it
        /// if one exists; otherwise, the function creates a new query instance and caches it. </remarks>
        /// <returns>The new or cached query.</returns>
        /// <param name="queryDesc">An array of query description objects to be combined to define the query.</param>
        protected internal EntityQuery GetEntityQuery(params EntityQueryDesc[] queryDesc)
        {
            return GetEntityQueryInternal(queryDesc);
        }

        /// <summary>
        /// Create an entity query from a query description builder.
        /// </summary>
        /// <remarks>This function looks for a cached query matching the combined query descriptions, and returns it
        /// if one exists; otherwise, the function creates a new query instance and caches it. </remarks>
        /// <returns>The new or cached query.</returns>
        /// <param name="builder">The description builder</param>
        public EntityQuery GetEntityQuery(in EntityQueryBuilder builder)
        {
            return CheckedState()->GetEntityQueryInternal(builder);
        }

#if UNITY_ENTITIES_RUNTIME_TOOLING
        /// <summary>
        /// Return the Stopwatch ticks at the start of when this system last actually executed.
        /// Only available with UNITY_ENTITIES_RUNTIME_TOOLING defined
        /// </summary>
        public long SystemStartTicks => this.m_StatePtr->m_LastSystemStartTime;

        /// <summary>
        /// Return the Stopwatch ticks at the end of when this system last actually executed.
        /// Only available with UNITY_ENTITIES_RUNTIME_TOOLING defined
        /// </summary>
        public long SystemEndTicks => this.m_StatePtr->m_LastSystemEndTime;

        /// <summary>
        /// Return the Stopwatch ticks for how long this system ran the last time Update() was called.
        /// If the system was disabled or didn't run due to no matching queries at last Update(), 0
        /// is returned.
        /// Only available with UNITY_ENTITIES_RUNTIME_TOOLING defined
        /// </summary>
        public long SystemElapsedTicks
        {
            get
            {
                if (!this.m_StatePtr->m_RanLastUpdate)
                    return 0;

                return SystemEndTicks - SystemStartTicks;
            }
        }

        /// <summary>
        /// Return SystemElapsedTicks converted to float milliseconds.
        /// Only available with UNITY_ENTITIES_RUNTIME_TOOLING defined
        /// </summary>
        public float SystemElapsedMilliseconds => (float) (SystemElapsedTicks * 1000.0 / Stopwatch.Frequency);
#endif
    }
}
