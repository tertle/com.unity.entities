//#define LOG_RESOLVING
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
#if UNITY_EDITOR
using Unity.Assertions;
using Unity.Entities.Conversion;
using UnityEditor;
#endif
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes
{
    /// <summary>
    /// The ResolvedSectionEntity buffer registers all the scene section entities in an scene entity.
    /// </summary>
    public struct ResolvedSectionEntity : IBufferElementData
    {
        /// <summary>
        /// Section Entity
        /// </summary>
        public Entity SectionEntity;
    }


    /// <summary>
    /// This component references the scene entity that contains this scene section entity.
    /// </summary>
    public struct SceneEntityReference : IComponentData
    {
        /// <summary>
        /// Scene Entity
        /// </summary>
        public Entity SceneEntity;
    }

    struct ResolvedSceneHash : IComponentData
    {
        public Hash128 ArtifactHash;
    }

    struct ResolvedSectionPath : IComponentData
    {
        public FixedString512Bytes ScenePath;
        public UntypedWeakReferenceId HybridReferenceId;
    }

    struct SceneSectionCustomMetadata
    {
        public ulong StableTypeHash;
        public BlobArray<byte> Data;
    }

    // Size of SceneMetaData should be 16 byte aligned since this is stored as a blob with additional data following it
    [StructLayout(LayoutKind.Sequential, Size = 48)]
    struct SceneMetaData
    {
        public BlobArray<SceneSectionData> Sections;
        public BlobString                  SceneName;
        public BlobArray<BlobArray<SceneSectionCustomMetadata>> SceneSectionCustomMetadata;
        public int HeaderBlobAssetBatchSize;
    }

    /// <summary>
    /// Component to indicate that the scene or section loading should be temporarily disabled.
    /// </summary>
    /// <remarks>This component is applied during the live conversion patching.</remarks>
    public struct DisableSceneResolveAndLoad : IComponentData
    {
    }

    static class SceneMetaDataSerializeUtility
    {
        public static readonly int CurrentFileFormatVersion = 3;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Scenes are made out of sections, but to find out how many sections there are and extract their data like bounding volume or file size.
    /// The meta data for the scene has to be loaded first.
    /// ResolveSceneReferenceSystem creates section entities for each scene by loading the scenesection's metadata from disk.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor | WorldSystemFilterFlags.Streaming)]
    [UpdateInGroup(typeof(SceneSystemGroup))]
    [UpdateAfter(typeof(SceneSystem))]
    partial class ResolveSceneReferenceSystem : SystemBase
    {
        struct AssetDependencyTrackerState : ICleanupComponentData
        {
            public UnityEditor.GUID SceneAndBuildConfigGUID;
        }

        EntityQuery m_AddScenes;
        EntityQuery m_RemoveScenesWithoutSceneReference;
        EntityQuery m_RemoveScenesWithDisableSceneResolveAndLoad;
        EntityQuery m_RemoveScenesWithDisabled;
        EntityQuery m_ValidSceneQuery;
        EntityQueryMask m_ValidSceneMask;
        ResolveSceneSectionArchetypes m_ResolveSceneSectionArchetypes;

        AssetDependencyTracker<Entity> _AssetDependencyTracker;
        NativeList<AssetDependencyTracker<Entity>.Completed> _Changed;

        SystemHandle _sceneSystem;

        SceneHeaderUtility _SceneHeaderUtility;

        [Conditional("LOG_RESOLVING")]
        void LogResolving(string type, Hash128 sceneGUID)
        {
            Debug.Log(type + ": " + sceneGUID);
        }

        [Conditional("LOG_RESOLVING")]
        void LogResolving(string log)
        {
            Debug.Log(log);
        }

        protected override unsafe void OnUpdate()
        {
            SceneWithBuildConfigurationGUIDs.ValidateBuildSettingsCache();

            var buildConfigurationGUID = EntityManager.GetComponentData<SceneSystemData>(_sceneSystem).BuildConfigurationGUID;

            // Add scene entities that haven't been encountered yet
            if (!m_AddScenes.IsEmptyIgnoreFilter)
            {
                //@TODO: Should use Entities.ForEach but we are missing Entities.ForEach support for explicit queries

                using (var addScenes = m_AddScenes.ToEntityArray(Allocator.TempJob))
                {
                    var trackerStates = new NativeArray<AssetDependencyTrackerState>(addScenes.Length, Allocator.Temp);
                    for (int i = 0; i != addScenes.Length; i++)
                    {
                        var sceneEntity = addScenes[i];
                        var scene = EntityManager.GetComponentData<SceneReference>(sceneEntity);
                        var requestSceneLoaded = EntityManager.GetComponentData<RequestSceneLoaded>(sceneEntity);

                        var guid = SceneWithBuildConfigurationGUIDs.EnsureExistsFor(scene.SceneGUID,
                            buildConfigurationGUID, true, out var requireRefresh);
                        var async = (requestSceneLoaded.LoadFlags & SceneLoadFlags.BlockOnImport) == 0;

                        LogResolving(async ? "Adding Async" : "Adding Sync", guid);

                        _AssetDependencyTracker.Add(guid, sceneEntity, async);
                        if (requireRefresh)
                            _AssetDependencyTracker.RequestRefresh();

                        trackerStates[i] = new AssetDependencyTrackerState {SceneAndBuildConfigGUID = guid};
                    }

                    EntityManager.AddComponentData(m_AddScenes, trackerStates);
                    trackerStates.Dispose();
                }
            }

            // Remove scene entities that were added and should no longer be tracked
            RemoveSceneEntities(m_RemoveScenesWithoutSceneReference);
            RemoveSceneEntities(m_RemoveScenesWithDisableSceneResolveAndLoad);
            RemoveSceneEntities(m_RemoveScenesWithDisabled);

            // Process any scenes that have completed their asset import
            var isDone = _AssetDependencyTracker.GetCompleted(_Changed);
            foreach (var change in _Changed)
            {
                var sceneEntity = change.UserKey;
                LogResolving($"Resolving: {change.Asset} -> {change.ArtifactID}");

                // This happens when instantiating an already fully resolved scene entity,
                // AssetDependencyTrackerState will be added and results in a completed change request,
                // but since this scene is already fully resolved with the same hash we can simply skip it.
                if (SystemAPI.HasComponent<ResolvedSceneHash>(sceneEntity) &&
                    SystemAPI.GetComponent<ResolvedSceneHash>(sceneEntity).ArtifactHash == (Hash128) change.ArtifactID)
                {
                    if (!SystemAPI.HasBuffer<ResolvedSectionEntity>(sceneEntity))
                        throw new InvalidOperationException(
                            $"Entity {sceneEntity} used for a scene load has a {nameof(ResolvedSceneHash)} component but no {nameof(ResolvedSectionEntity)} buffer. " +
                            "This suggests that you copied a scene entity after loading it, but before its scene data had been fully resolved. " +
                            $"Please only copy it after resolving has finished, which will add {nameof(ResolvedSectionEntity)} to the entity.");
                    continue;
                }

                if (!m_ValidSceneMask.MatchesIgnoreFilter(sceneEntity))
                    throw new InvalidOperationException("entity should have been removed from tracker already");

#if UNITY_EDITOR
                if (UnityEditor.EditorApplication.isPlaying
                    && !CustomBakingSettings.PlayModeClosedSubSceneBaking
                    && SceneSystem.IsSceneLoaded(World.Unmanaged, sceneEntity))
                {
                    continue;
                }
#endif

                // Unload any previous state
                SceneSystem.UnloadSceneSectionMetaEntitiesOnly(World.Unmanaged, sceneEntity, false);

                // Resolve new state
                var scene = EntityManager.GetComponentData<SceneReference>(change.UserKey);
                var request = EntityManager.GetComponentData<RequestSceneLoaded>(change.UserKey);

                SceneWithBuildConfigurationGUIDs.EnsureExistsFor(scene.SceneGUID,
                    buildConfigurationGUID, true, out var requireRefresh);

                if (change.ArtifactID != default)
                {
                    LogResolving($"Schedule header load: {change.ArtifactID}");
                    SceneHeaderUtility.ScheduleHeaderLoadOnEntity(EntityManager, change.UserKey, scene.SceneGUID,
                        request, change.ArtifactID, SceneSystem.SceneLoadDir);
                }
                else
                    Debug.LogError(
                        $"Failed to import entity scene because the automatically generated SceneAndBuildConfigGUID asset was not present: '{AssetDatabaseCompatibility.GuidToPath(scene.SceneGUID)}' -> '{AssetDatabaseCompatibility.GuidToPath(change.Asset)}'");
            }

            _SceneHeaderUtility.CleanupHeaders(EntityManager);

            bool headerLoadInProgress = false;

            var query = SystemAPI.QueryBuilder().WithNone<DisableSceneResolveAndLoad, ResolvedSectionEntity>()
                .WithAll<RequestSceneHeader, SceneReference, ResolvedSceneHash, RequestSceneLoaded>().Build();

            var sceneEntities = query.ToEntityArray(Allocator.Temp);
            foreach (var sceneEntity in sceneEntities)
			{
                var requestHeader = SystemAPI.GetComponent<RequestSceneHeader>(sceneEntity);
                var sceneReference = SystemAPI.GetComponent<SceneReference>(sceneEntity);
                var requestSceneLoaded = SystemAPI.GetComponent<RequestSceneLoaded>(sceneEntity);

                if (!requestHeader.IsCompleted)
                {
                    if ((requestSceneLoaded.LoadFlags & SceneLoadFlags.BlockOnImport) == 0)
                    {
                        headerLoadInProgress = true;
		                continue;
                    }

                    requestHeader.Complete();
                }

                var headerLoadResult = SceneHeaderUtility.FinishHeaderLoad(requestHeader, sceneReference.SceneGUID, SceneSystem.SceneLoadDir);

                LogResolving($"Finished header load: {sceneReference.SceneGUID}");
                if (!headerLoadResult.Success)
                {
                    requestHeader.Dispose();

                    EntityManager.AddBuffer<ResolvedSectionEntity>(sceneEntity);
                    EntityManager.RemoveComponent<RequestSceneHeader>(sceneEntity);

                    continue;
                }

                ResolveSceneSectionUtility.ResolveSceneSections(EntityManager,
                    sceneEntity, requestSceneLoaded, ref headerLoadResult.SceneMetaData.Value,
                            m_ResolveSceneSectionArchetypes, headerLoadResult.SectionPaths, headerLoadResult.HeaderBlobOwner);

                requestHeader.Dispose();

                EntityManager.RemoveComponent<RequestSceneHeader>(sceneEntity);

                if (EntityManager.HasComponent<SubScene>(sceneEntity))
                {
                    var subScene = EntityManager.GetComponentObject<SubScene>(sceneEntity);

                    // Add SubScene component to section entities
                    using (var sectionEntities = EntityManager.GetBuffer<ResolvedSectionEntity>(sceneEntity)
                        .ToNativeArray(Allocator.Temp))
					{
                        for (int iSection = 0; iSection < sectionEntities.Length; ++iSection)
							EntityManager.AddComponentObject(sectionEntities[iSection].SectionEntity, subScene);
                    }
				}
			}

            if(headerLoadInProgress)
                EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();

            if (!isDone)
                EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
        }

        private void RemoveSceneEntities(EntityQuery query)
        {
            if (!query.IsEmptyIgnoreFilter)
            {
                using (var removeEntities = query.ToEntityArray(Allocator.TempJob))
                using (var removeGuids =
                       query.ToComponentDataArray<AssetDependencyTrackerState>(Allocator.TempJob))
                {
                    for (int i = 0; i != removeEntities.Length; i++)
                    {
                        LogResolving("Removing", removeGuids[i].SceneAndBuildConfigGUID);
                        _AssetDependencyTracker.Remove(removeGuids[i].SceneAndBuildConfigGUID, removeEntities[i]);
                    }
                }

                EntityManager.RemoveComponent<AssetDependencyTrackerState>(query);
            }
        }

        protected override void OnCreate()
        {
            _sceneSystem = World.GetExistingSystem<SceneSystem>();
            _AssetDependencyTracker =
                new AssetDependencyTracker<Entity>(EntityScenesPaths.SubSceneImporterType, "Import EntityScene");
            _Changed = new NativeList<AssetDependencyTracker<Entity>.Completed>(32, Allocator.Persistent);
            _SceneHeaderUtility = new SceneHeaderUtility(this);
            m_ValidSceneQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<SceneReference, RequestSceneLoaded, AssetDependencyTrackerState>()
                .WithNone<DisableSceneResolveAndLoad>()
                .Build(this);
            Assert.IsFalse(m_ValidSceneQuery.HasFilter(), "The use of EntityQueryMask in this system will not respect the query's active filter settings.");
            m_ValidSceneMask = m_ValidSceneQuery.GetEntityQueryMask();

            m_AddScenes = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<SceneReference,RequestSceneLoaded>()
                .WithNone<DisableSceneResolveAndLoad,AssetDependencyTrackerState>()
                .Build(this);
            m_RemoveScenesWithoutSceneReference = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AssetDependencyTrackerState>().WithNone<SceneReference,RequestSceneLoaded>().Build(this);
            m_RemoveScenesWithDisableSceneResolveAndLoad = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AssetDependencyTrackerState,DisableSceneResolveAndLoad>().Build(this);
            m_RemoveScenesWithDisabled = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AssetDependencyTrackerState,Disabled>().Build(this);

            m_ResolveSceneSectionArchetypes = ResolveSceneSectionUtility.CreateResolveSceneSectionArchetypes(EntityManager);
        }

        protected override void OnDestroy()
        {
            _AssetDependencyTracker.Dispose();
            _Changed.Dispose();
            _SceneHeaderUtility.Dispose(EntityManager);
        }
    }

#else
    /// <summary>
    /// Scenes are made out of sections, but to find out how many sections there are and extract their data like bounding volume or file size.
    /// The meta data for the scene has to be loaded first.
    /// ResolveSceneReferenceSystem creates section entities for each scene by loading the scenesection's metadata from disk.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor | WorldSystemFilterFlags.Streaming)]
    [UpdateInGroup(typeof(SceneSystemGroup))]
    [UpdateAfter(typeof(SceneSystem))]
    partial class ResolveSceneReferenceSystem : SystemBase
    {
        private SceneHeaderUtility _SceneHeaderUtility;
        private ResolveSceneSectionArchetypes m_ResolveSceneSectionArchetypes;

        protected override void OnCreate()
        {
            _SceneHeaderUtility = new SceneHeaderUtility(this);
            m_ResolveSceneSectionArchetypes = ResolveSceneSectionUtility.CreateResolveSceneSectionArchetypes(EntityManager);
        }

        protected override void OnDestroy()
        {
            _SceneHeaderUtility.Dispose(EntityManager);
        }

        void ScheduleHeaderLoad()
        {
            var scheduleHeaderLoadQuery = SystemAPI.QueryBuilder()
                .WithNone<DisableSceneResolveAndLoad, ResolvedSectionEntity, RequestSceneHeader>()
                .WithAll<SceneReference, RequestSceneLoaded>().Build();

            if (scheduleHeaderLoadQuery.CalculateEntityCount() == 0)
                return;
            using var scheduleHeaderLoadEntities = scheduleHeaderLoadQuery.ToEntityArray(Allocator.Temp);
            using var scheduleHeaderLoadSceneReferences =
                scheduleHeaderLoadQuery.ToComponentDataArray<SceneReference>(Allocator.Temp);
            using var scheduleHeaderLoadRequestSceneLoadeds =
                scheduleHeaderLoadQuery.ToComponentDataArray<RequestSceneLoaded>(Allocator.Temp);

            for (var i = 0; i < scheduleHeaderLoadEntities.Length; i++)
                {
                SceneHeaderUtility.ScheduleHeaderLoadOnEntity(EntityManager,
                    scheduleHeaderLoadEntities[i], scheduleHeaderLoadSceneReferences[i].SceneGUID,
                    scheduleHeaderLoadRequestSceneLoadeds[i], default, SceneSystem.SceneLoadDir);
            }

            _SceneHeaderUtility.CleanupHeaders(EntityManager);
        }

        void FinishHeaderLoad()
		{
            var finishHeaderLoadQuery = SystemAPI.QueryBuilder()
                .WithNone<DisableSceneResolveAndLoad, ResolvedSectionEntity>()
                .WithAll<RequestSceneHeader, SceneReference, ResolvedSceneHash, RequestSceneLoaded>().Build();

            if (finishHeaderLoadQuery.CalculateEntityCount() == 0)
                return;
            using var finishHeaderLoadEntities = finishHeaderLoadQuery.ToEntityArray(Allocator.Temp);
            using var finishHeaderLoadRequestSceneHeaders =
                finishHeaderLoadQuery.ToComponentDataArray<RequestSceneHeader>(Allocator.Temp);
            using var finishHeaderLoadSceneReferences =
                finishHeaderLoadQuery.ToComponentDataArray<SceneReference>(Allocator.Temp);
            using var finishHeaderLoadResolvedSceneHashs =
                finishHeaderLoadQuery.ToComponentDataArray<ResolvedSceneHash>(Allocator.Temp);
            using var finishHeaderLoadRequestSceneLoadeds =
                finishHeaderLoadQuery.ToComponentDataArray<RequestSceneLoaded>(Allocator.Temp);

            for (var i = 0; i < finishHeaderLoadEntities.Length; i++)
            {
                var sceneEntity = finishHeaderLoadEntities[i];
                var requestHeader = finishHeaderLoadRequestSceneHeaders[i];
                var scene = finishHeaderLoadSceneReferences[i];
                var requestSceneLoaded = finishHeaderLoadRequestSceneLoadeds[i];

                if (!requestHeader.IsCompleted)
                {
                    if ((requestSceneLoaded.LoadFlags & SceneLoadFlags.BlockOnImport) == 0)
                        return;
                    requestHeader.Complete();
                }

                var headerLoadResult = SceneHeaderUtility.FinishHeaderLoad(requestHeader, scene.SceneGUID, SceneSystem.SceneLoadDir);
                if (!headerLoadResult.Success)
                {
                    requestHeader.Dispose();
                    EntityManager.AddBuffer<ResolvedSectionEntity>(sceneEntity);
                    EntityManager.RemoveComponent<RequestSceneHeader>(sceneEntity);
                    return;
                }

                ResolveSceneSectionUtility.ResolveSceneSections(EntityManager, sceneEntity, requestSceneLoaded,
                    ref headerLoadResult.SceneMetaData.Value, m_ResolveSceneSectionArchetypes, headerLoadResult.SectionPaths,
                    headerLoadResult.HeaderBlobOwner);

                requestHeader.Dispose();
                EntityManager.RemoveComponent<RequestSceneHeader>(sceneEntity);
            }
        }

        protected unsafe override void OnUpdate()
        {
            ScheduleHeaderLoad();

            FinishHeaderLoad();
        }
    }
#endif

}
