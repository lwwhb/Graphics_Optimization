using System;
using System.Collections.Generic;

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

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
        internal struct PerStateData
        {
            public int sceneHash;
            public TextAsset cellDataAsset; // Contains L0 L1 SH data
            public TextAsset cellOptionalDataAsset; // Contains L2 SH data
        }

        [Serializable]
        struct SerializablePerStateDataItem
        {
            public string state;
            public PerStateData data;
        }
        
        [SerializeField] internal ProbeVolumeAsset asset;
        [SerializeField] internal TextAsset cellSharedDataAsset; // Contains bricks data
        [SerializeField] internal TextAsset cellSupportDataAsset; // Contains debug data
        [SerializeField] List<SerializablePerStateDataItem> serializedStates = new();

        internal Dictionary<string, PerStateData> states = new();

        string m_CurrentState = ProbeReferenceVolume.defaultBakingState;

        /// <summary>
        /// OnAfterDeserialize implementation.
        /// </summary>
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            states.Clear();
            foreach (var stateData in serializedStates)
                states.Add(stateData.state, stateData.data);
        }

        /// <summary>
        /// OnBeforeSerialize implementation.
        /// </summary>
        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            serializedStates.Clear();
            foreach (var kvp in states)
            {
                serializedStates.Add(new SerializablePerStateDataItem()
                {
                    state = kvp.Key,
                    data = kvp.Value,
                });
            }
        }

        internal void Clear()
        {
            QueueAssetRemoval();

#if UNITY_EDITOR
            try
            {
                AssetDatabase.StartAssetEditing();
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(asset));
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(cellSharedDataAsset));
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(cellSupportDataAsset));
                foreach (var stateData in states.Values)
                {
                    AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(stateData.cellDataAsset));
                    AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(stateData.cellOptionalDataAsset));
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
                EditorUtility.SetDirty(this);
            }
#endif

            states.Clear();
        }

        internal void RemoveBakingState(string state)
        {
#if UNITY_EDITOR
            if (states.TryGetValue(state, out var stateData))
            {
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(stateData.cellDataAsset));
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(stateData.cellOptionalDataAsset));
                EditorUtility.SetDirty(this);
            }
#endif
            states.Remove(state);
        }

        internal void RenameBakingState(string state, string newName)
        {
            if (!states.TryGetValue(state, out var stateData))
                return;
            states.Remove(state);
            states.Add(newName, stateData);

#if UNITY_EDITOR
            AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(stateData.cellDataAsset), newName + ".CellData.bytes");
            AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(stateData.cellOptionalDataAsset), newName + ".CellOptionalData.bytes");
            EditorUtility.SetDirty(this);
#endif
        }

        internal bool ResolveCells()
        {
            if (!states.TryGetValue(m_CurrentState, out var stateData))
                return false;
            return asset.ResolveCells(stateData.cellDataAsset, stateData.cellOptionalDataAsset, cellSharedDataAsset, cellSupportDataAsset);
        }

        internal void QueueAssetLoading()
        {
            if (asset == null || !ResolveCells())
                return;

            var refVol = ProbeReferenceVolume.instance;
            refVol.AddPendingAssetLoading(asset);
#if UNITY_EDITOR
            if (refVol.sceneData != null)
            {
                refVol.dilationValidtyThreshold = refVol.sceneData.GetBakeSettingsForScene(gameObject.scene).dilationSettings.dilationValidityThreshold;
            }
#endif
        }

        internal void QueueAssetRemoval()
        {
            if (asset != null)
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
            m_CurrentState = ProbeReferenceVolume.defaultBakingState;
        }

        public void SetBakingState(string state)
        {
            if (state == m_CurrentState)
                return;

            QueueAssetRemoval();
            m_CurrentState = state;
            QueueAssetLoading();
        }

#if UNITY_EDITOR
        public void GetBlobFileNames(out string cellDataFilename, out string cellOptionalDataFilename, out string cellSharedDataFilename, out string cellSupportDataFilename)
        {
            var state = ProbeReferenceVolume.instance.bakingState;
            string basePath = Path.Combine(ProbeVolumeAsset.GetDirectory(gameObject.scene.path, gameObject.scene.name), ProbeVolumeAsset.assetName);

            string GetOrCreateFileName(Object o, string extension)
            {
                var res = AssetDatabase.GetAssetPath(o);
                if (string.IsNullOrEmpty(res)) res = basePath + extension;
                return res;
            }
            cellDataFilename = GetOrCreateFileName(states[state].cellDataAsset, "-" + state + ".CellData.bytes");
            cellOptionalDataFilename = GetOrCreateFileName(states[state].cellOptionalDataAsset, "-" + state + ".CellOptionalData.bytes");
            cellSharedDataFilename = GetOrCreateFileName(cellSharedDataAsset, ".CellSharedData.bytes");
            cellSupportDataFilename = GetOrCreateFileName(cellSupportDataAsset, ".CellSupportData.bytes");
        }

        public void StripSupportData()
        {
            cellSupportDataAsset = null;
        }
#endif
    }
}
