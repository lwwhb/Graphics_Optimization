using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using UnityEngine.Rendering;

#if PROFILE_BUILD
using UnityEngine.Profiling;
#endif

namespace UnityEditor.Rendering
{
    class ShaderPreprocessor<TShader, TShaderVariant>
    {
        public List<IVariantStripper<TShader, TShaderVariant>> strippers { get; } = new();

        protected int totalVariantsInputCount { get; set; } = 0;
        protected int totalVariantsOutputCount { get; set; } = 0;

        protected bool logStrippedVariants { get; }
        protected bool exportStrippedVariants { get; }

        public ShaderPreprocessor()
        {
            if (RenderPipelineManager.currentPipeline.defaultSettings is IShaderVariantSettings shaderVariantSettings)
            {
                logStrippedVariants = shaderVariantSettings.shaderVariantLogLevel != ShaderVariantLogLevel.Disabled;
                exportStrippedVariants = shaderVariantSettings.exportShaderVariants;
            }

            foreach (var stripper in TypeCache.GetTypesDerivedFrom<IVariantStripper<TShader, TShaderVariant>>())
            {
                if (stripper.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null) == null)
                    continue;

                var stripperInstance = Activator.CreateInstance(stripper) as IVariantStripper<TShader, TShaderVariant>;
                if (stripperInstance.isActive)
                    strippers.Add(stripperInstance);
            }
        }

        void RemoveBack(IList<ShaderCompilerData> compilerDataList, int inputShaderVariantCount)
        {
            if (compilerDataList is List<ShaderCompilerData> inputDataList)
                inputDataList.RemoveRange(inputShaderVariantCount, inputDataList.Count - inputShaderVariantCount);
            else
            {
                for (int i = compilerDataList.Count - 1; i >= inputShaderVariantCount; --i)
                    compilerDataList.RemoveAt(i);
            }
        }

        /// <summary>
        /// Strips the given <see cref="T"/>
        /// </summary>
        /// <param name="shader">The <see cref="T"/> that might be stripped.</param>
        /// <param name="snippetData">The <see cref="ShaderSnippetData"/></param>
        /// <param name="compilerDataList">A list of <see cref="ShaderCompilerData"/></param>
        protected unsafe bool StripShaderVariants(
            [NotNull] TShader shader,
            TShaderVariant shaderVariant,
            IList<ShaderCompilerData> compilerDataList,
            out double totalStripTime)
        {
#if PROFILE_BUILD
            Profiler.BeginSample(nameof(StripShaderVariants));
#endif
            totalStripTime = 0.0;

            if (shader == null || compilerDataList == null)
                return false;

            int currentVariantCount = compilerDataList.Count;

            // Early exit from the stripping
            if (currentVariantCount == 0)
                return true;

            int inputShaderVariantCount = currentVariantCount;

            double stripTimeMs = 0;
            using (TimedScope.FromPtr(&stripTimeMs))
            {
                // Go through all the shader variants
                for (int i = 0; i < inputShaderVariantCount;)
                {
                    // Stripping them by default until we find some stripper that needs it
                    bool canRemoveVariant = true;
                    foreach (var stripper in strippers)
                    {
                        canRemoveVariant &= stripper.IsVariantStripped(shader, shaderVariant, compilerDataList[i]);
                    }

                    // Remove at swap back
                    if (canRemoveVariant)
                        compilerDataList[i] = compilerDataList[--inputShaderVariantCount];
                    else
                        ++i;
                }

                // Remove the unneed shader variants that will be at the back
                RemoveBack(compilerDataList, inputShaderVariantCount);
            }

            // Accumulate diagnostics information
            totalVariantsInputCount += currentVariantCount;
            totalVariantsOutputCount += compilerDataList.Count;
            totalStripTime += stripTimeMs;

#if PROFILE_BUILD
            Profiler.EndSample();
#endif
            return true;
        }
    }
}
