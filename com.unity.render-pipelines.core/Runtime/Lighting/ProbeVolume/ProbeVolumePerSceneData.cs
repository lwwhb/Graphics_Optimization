using System;
using System.Collections.Generic;
using UnityEditor;

namespace UnityEngine.Experimental.Rendering
{
    /// <summary>
    /// A component that stores baked probe volume state and data references. Normally hidden from the user.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("")] // Hide.
    public class ProbeVolumePerSceneData : MonoBehaviour, ISerializationCallbackReceiver
    {
        [Serializable]
        struct SerializableAssetItem
        {
        	public ProbeVolumeBakingState state;
            public ProbeVolumeAsset asset;
            public TextAsset cellDataAsset;
            public TextAsset cellOptionalDataAsset;
            public TextAsset cellSupportDataAsset;
        }

        [SerializeField] List<SerializableAssetItem> serializedAssets = new();
        
        internal Dictionary<ProbeVolumeBakingState, ProbeVolumeAsset> assets = new();

        ProbeVolumeBakingState m_CurrentState = (ProbeVolumeBakingState)ProbeReferenceVolume.numBakingStates;

        /// <summary>
        /// OnAfterDeserialize implementation.
        /// </summary>
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            assets.Clear();
            foreach (var assetItem in serializedAssets)
            {
                assetItem.asset.cellDataAsset = assetItem.cellDataAsset;
                assetItem.asset.cellOptionalDataAsset = assetItem.cellOptionalDataAsset;
                assetItem.asset.cellSupportDataAsset = assetItem.cellSupportDataAsset;
                assets.Add(assetItem.state, assetItem.asset);
            }
        }

        /// <summary>
        /// OnBeforeSerialize implementation.
        /// </summary>
        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            serializedAssets.Clear();
            foreach (var k in assets.Keys)
            {
                SerializableAssetItem item;
                item.state = k;
                item.asset = assets[k];
                item.cellDataAsset = item.asset.cellDataAsset;
                item.cellOptionalDataAsset = item.asset.cellOptionalDataAsset;
                item.cellSupportDataAsset = item.asset.cellSupportDataAsset;
                serializedAssets.Add(item);
            }
        }

        internal void StoreAssetForState(ProbeVolumeBakingState state, ProbeVolumeAsset asset)
        {
            assets[state] = asset;
        }

        internal ProbeVolumeAsset GetAssetForState(ProbeVolumeBakingState state) => assets.GetValueOrDefault(state, null);

        internal void Clear()
        {
            InvalidateAllAssets();

#if UNITY_EDITOR
            AssetDatabase.StartAssetEditing();
            foreach (var asset in assets)
            {
                if (asset.Value != null)
                {
                    AssetDatabase.DeleteAsset(ProbeVolumeAsset.GetPath(gameObject.scene, asset.Key, false));
                    asset.Value.GetBlobFileNames(out var cellDataFilename, out var cellOptionalDataFilename, out var cellSupportDataFilename);
                    AssetDatabase.DeleteAsset(cellDataFilename);
                    AssetDatabase.DeleteAsset(cellOptionalDataFilename);
                    AssetDatabase.DeleteAsset(cellSupportDataFilename);
                }
            }
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
#endif

            assets.Clear();
        }

        internal void InvalidateAllAssets()
        {
            foreach (var asset in assets.Values)
            {
                if (asset != null)
                    ProbeReferenceVolume.instance.AddPendingAssetRemoval(asset);
            }
        }

        internal ProbeVolumeAsset GetCurrentStateAsset()
        {
            if (assets.ContainsKey(m_CurrentState)) return assets[m_CurrentState];
            else return null;
        }

        internal void QueueAssetLoading()
        {
            var refVol = ProbeReferenceVolume.instance;
            if (assets.TryGetValue(m_CurrentState, out var asset) && asset != null)
            {
                if (asset.ResolveCells())
                {
                    refVol.AddPendingAssetLoading(asset);

#if UNITY_EDITOR
                    if (refVol.sceneData != null)
                    {
                        refVol.dilationValidtyThreshold = refVol.sceneData.GetBakeSettingsForScene(gameObject.scene).dilationSettings.dilationValidityThreshold;
                    }
#endif
                }
            }
        }

        internal void QueueAssetRemoval()
        {
            if (assets.TryGetValue(m_CurrentState, out var asset) && asset != null)
                ProbeReferenceVolume.instance.AddPendingAssetRemoval(asset);
        }

        void OnEnable()
        {
            ProbeReferenceVolume.instance.RegisterPerSceneData(this);
            if (ProbeReferenceVolume.instance.sceneData != null)
                SetBakingState(ProbeReferenceVolume.instance.bakingState);
            // otherwise baking state will be initialized in ProbeReferenceVolume.Initialize when sceneData is loaded
        }

        void OnDisable()
        {
            OnDestroy();
            ProbeReferenceVolume.instance.UnregisterPerSceneData(this);
        }

        void OnDestroy()
        {
            QueueAssetRemoval();
            m_CurrentState = (ProbeVolumeBakingState)ProbeReferenceVolume.numBakingStates;
        }

        public void SetBakingState(ProbeVolumeBakingState state)
        {
            if (state == m_CurrentState)
                return;

            QueueAssetRemoval();
            m_CurrentState = state;
            QueueAssetLoading();
        }

#if UNITY_EDITOR
        public void StripSupportData()
        {
            foreach (var asset in assets.Values)
                asset.cellSupportDataAsset = null;
        }
#endif
    }
}
