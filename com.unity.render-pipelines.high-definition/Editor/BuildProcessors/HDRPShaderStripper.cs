using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace UnityEditor.Rendering.HighDefinition
{
    internal class HDRPShaderStripper : IShaderVariantStripper, IComputeVariantStripper
    {
        readonly List<BaseShaderPreprocessor> shaderProcessorsList;

        public HDRPShaderStripper()
        {
            shaderProcessorsList = HDShaderUtils.GetBaseShaderPreprocessorList();
        }

        public bool isActive
        {
            get
            {
                if (HDRenderPipeline.currentAsset == null)
                    return false;

                if (HDRenderPipelineGlobalSettings.Ensure(canCreateNewAsset: false) == null)
                    return false;

                // TODO: Grab correct configuration/quality asset.
                var hdPipelineAssets = ShaderBuildPreprocessor.hdrpAssets;

                // Test if striping is enabled in any of the found HDRP assets.
                if (hdPipelineAssets.Count == 0 || !hdPipelineAssets.Any(a => a.allowShaderVariantStripping))
                    return false;

                return true;
            }
        }


        #region IShaderStripper

        public bool IsLogEnabled(Shader shader)
        {
            var logLevel = HDRenderPipelineGlobalSettings.instance.shaderVariantLogLevel;

            switch (logLevel)
            {
                case ShaderVariantLogLevel.Disabled:
                    return false;
                case ShaderVariantLogLevel.OnlyHDRPShaders:
                    return HDShaderUtils.IsHDRPShader(shader);
                case ShaderVariantLogLevel.AllShaders:
                    return true;
            }

            Debug.LogError("Missing ShaderVariant Log Level");
            return false;
        }

        public bool IsVariantStripped([NotNull] Shader shader, ShaderSnippetData snippetData, ShaderCompilerData inputData)
        {
            // Remove the input by default, until we find a HDRP Asset in the list that needs it.
            bool removeInput = true;

            foreach (var hdAsset in ShaderBuildPreprocessor.hdrpAssets)
            {
                var strippedByPreprocessor = false;

                // Call list of strippers
                // Note that all strippers cumulate each other, so be aware of any conflict here
                foreach (BaseShaderPreprocessor shaderPreprocessor in shaderProcessorsList)
                {
                    if (shaderPreprocessor.ShadersStripper(hdAsset, shader, snippetData, inputData))
                    {
                        strippedByPreprocessor = true;
                        break;
                    }
                }

                if (!strippedByPreprocessor)
                {
                    removeInput = false;
                    break;
                }
            }

            return removeInput;
        }

        #endregion

        #region IComputeShaderStripper

        protected ShadowKeywords m_ShadowKeywords = new ShadowKeywords();
        protected ShaderKeyword m_EnableAlpha = new ShaderKeyword("ENABLE_ALPHA");
        protected ShaderKeyword m_MSAA = new ShaderKeyword("ENABLE_MSAA");
        protected ShaderKeyword m_ScreenSpaceShadowOFFKeywords = new ShaderKeyword("SCREEN_SPACE_SHADOWS_OFF");
        protected ShaderKeyword m_ScreenSpaceShadowONKeywords = new ShaderKeyword("SCREEN_SPACE_SHADOWS_ON");
        protected ShaderKeyword m_ProbeVolumesL1 = new ShaderKeyword("PROBE_VOLUMES_L1");
        protected ShaderKeyword m_ProbeVolumesL2 = new ShaderKeyword("PROBE_VOLUMES_L2");

        // Modify this function to add more stripping clauses
        internal bool StripShader(HDRenderPipelineAsset hdAsset, ComputeShader shader, string kernelName, ShaderCompilerData inputData)
        {
            // Strip every useless shadow configs
            var shadowInitParams = hdAsset.currentPlatformRenderPipelineSettings.hdShadowInitParams;

            foreach (var shadowVariant in m_ShadowKeywords.ShadowVariants)
            {
                if (shadowVariant.Key != shadowInitParams.shadowFilteringQuality)
                {
                    if (inputData.shaderKeywordSet.IsEnabled(shadowVariant.Value))
                        return true;
                }
            }

            // Screen space shadow variant is exclusive, either we have a variant with dynamic if that support screen space shadow or not
            // either we have a variant that don't support at all. We can't have both at the same time.
            if (inputData.shaderKeywordSet.IsEnabled(m_ScreenSpaceShadowOFFKeywords) && shadowInitParams.supportScreenSpaceShadows)
                return true;

            if (inputData.shaderKeywordSet.IsEnabled(m_MSAA) && (hdAsset.currentPlatformRenderPipelineSettings.supportedLitShaderMode == RenderPipelineSettings.SupportedLitShaderMode.DeferredOnly))
            {
                return true;
            }

            if (inputData.shaderKeywordSet.IsEnabled(m_ScreenSpaceShadowONKeywords) && !shadowInitParams.supportScreenSpaceShadows)
                return true;

            if (inputData.shaderKeywordSet.IsEnabled(m_EnableAlpha) && !hdAsset.currentPlatformRenderPipelineSettings.SupportsAlpha())
            {
                return true;
            }

            // Global Illumination
            if (inputData.shaderKeywordSet.IsEnabled(m_ProbeVolumesL1) &&
                (!hdAsset.currentPlatformRenderPipelineSettings.supportProbeVolume || hdAsset.currentPlatformRenderPipelineSettings.probeVolumeSHBands != ProbeVolumeSHBands.SphericalHarmonicsL1))
                return true;

            if (inputData.shaderKeywordSet.IsEnabled(m_ProbeVolumesL2) &&
                (!hdAsset.currentPlatformRenderPipelineSettings.supportProbeVolume || hdAsset.currentPlatformRenderPipelineSettings.probeVolumeSHBands != ProbeVolumeSHBands.SphericalHarmonicsL2))
                return true;

            return false;
        }

        public bool IsVariantStripped([NotNull] ComputeShader shader, string kernelName, ShaderCompilerData compilerData)
        {
            // Discard any compute shader use for raytracing if none of the RP asset required it
            if (!ShaderBuildPreprocessor.playerNeedRaytracing &&
                ShaderBuildPreprocessor.computeShaderCache.TryGetValue(shader.GetInstanceID(), out _))
                return false;

            bool removeInput = true;

            foreach (var hdAsset in ShaderBuildPreprocessor.hdrpAssets)
            {
                if (!StripShader(hdAsset, shader, kernelName, compilerData))
                {
                    removeInput = false;
                    break;
                }
            }

            return removeInput;
        }

        public bool IsLogEnabled(ComputeShader _) => HDRenderPipelineGlobalSettings.instance.shaderVariantLogLevel != ShaderVariantLogLevel.Disabled;
        #endregion
    }
}
