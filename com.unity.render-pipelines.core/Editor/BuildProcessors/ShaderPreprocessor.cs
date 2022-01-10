using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using UnityEngine.Rendering;
using UnityEngine;

#if PROFILE_BUILD
using UnityEngine.Profiling;
#endif

namespace UnityEditor.Rendering
{
    abstract class ShaderPreprocessor<TShader, TShaderVariant>
    {
        List<IVariantStripper<TShader, TShaderVariant>> strippers { get; } = new();

        int totalVariantsInputCount { get; set; } = 0;
        int totalVariantsOutputCount { get; set; } = 0;

        ShaderVariantLogLevel logStrippedVariants { get; }
        bool exportStrippedVariants { get; }

        /// <summary>
        /// Constructor that fetch all the IVariantStripper defined on the assemblies
        /// </summary>
        public ShaderPreprocessor()
        {
            // Obtain logging and export information if the Global settings are configured as IShaderVariantSettings
            if (RenderPipelineManager.currentPipeline != null && RenderPipelineManager.currentPipeline.defaultSettings is IShaderVariantSettings shaderVariantSettings)
            {
                logStrippedVariants = shaderVariantSettings.shaderVariantLogLevel;
                exportStrippedVariants = shaderVariantSettings.exportShaderVariants;
            }

            // Gather all the implementations of IVariantStripper and add them as the strippers
            foreach (var stripper in TypeCache.GetTypesDerivedFrom<IVariantStripper<TShader, TShaderVariant>>())
            {
                if (stripper.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null) != null)
                {
                    var stripperInstance = Activator.CreateInstance(stripper) as IVariantStripper<TShader, TShaderVariant>;
                    if (stripperInstance.isActive)
                        strippers.Add(stripperInstance);
                }
            }
        }

        /// <summary>
        /// Removes a given count from the back of the given list
        /// </summary>
        /// <param name="compilerDataList">The list to be removed</param>
        /// <param name="inputShaderVariantCount">The number of elements to be removed from the back</param>
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
        /// Strips the given <see cref="TShader"/>
        /// </summary>
        /// <param name="shader">The <see cref="T"/> that might be stripped.</param>
        /// <param name="shaderVariant">The <see cref="TShaderVariant"/></param>
        /// <param name="compilerDataList">A list of <see cref="ShaderCompilerData"/></param>
        protected unsafe bool StripShaderVariants(
            [NotNull] TShader shader,
            TShaderVariant shaderVariant,
            IList<ShaderCompilerData> compilerDataList)
        {
#if PROFILE_BUILD
            Profiler.BeginSample(nameof(StripShaderVariants));
#endif
            if (shader == null || compilerDataList == null)
                return false;

            int inputVariantCount = compilerDataList.Count;

            // Early exit from the stripping
            if (inputVariantCount == 0)
                return true;

            int inputShaderVariantCount = inputVariantCount;

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
                        canRemoveVariant &= stripper.CanRemoveShaderVariant(shader, shaderVariant, compilerDataList[i]);
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
            int outputVariantCount = compilerDataList.Count;
            totalVariantsInputCount += inputVariantCount;
            totalVariantsOutputCount += compilerDataList.Count;

            // Dump information if need
            LogShaderVariants(shader, shaderVariant, inputVariantCount, outputVariantCount, stripTimeMs);
            Export(shader, shaderVariant, inputVariantCount, outputVariantCount, totalVariantsInputCount, totalVariantsOutputCount);

#if PROFILE_BUILD
            Profiler.EndSample();
#endif
            return true;
        }

        #region Logging

        /// <summary>
        /// Obtains a formatted <see cref="string"/> with the shader name and the valuable info about the variant
        /// </summary>
        /// <param name="shader">The <see cref="Shader"/> or the <see cref="ComputeShader"/></param>
        /// <param name="variant">The variant for the given type of shader</param>
        /// <returns>A formatted <see cref="string"/></returns>
        protected abstract string ToLog(TShader shader, TShaderVariant variant);

        void LogShaderVariants([NotNull] TShader shader, TShaderVariant shaderVariant, int prevVariantsCount, int currVariantsCount, double stripTimeMs)
        {
            if (logStrippedVariants == ShaderVariantLogLevel.Disabled)
                return;

            float percentageCurrent = currVariantsCount / (float)prevVariantsCount * 100f;
            float percentageTotal = totalVariantsOutputCount / (float)totalVariantsInputCount * 100f;
            Debug.Log(@$"STRIPPING: {ToLog(shader, shaderVariant)} -
                      Remaining shader variants = {currVariantsCount}/{prevVariantsCount} = {percentageCurrent}% -
                      Total = {totalVariantsOutputCount}/{totalVariantsInputCount} = {percentageTotal}% Time={stripTimeMs}ms");
        }

        #endregion

        #region Export

        /// <summary>
        /// Specifies the export filename for the variants stripping information
        /// </summary>
        protected abstract string exportFilename { get; }

        /// <summary>
        /// Obtains a JSON valid formatted <see cref="string"/> with the shader name and the valuable info about the variant
        /// </summary>
        /// <param name="shader">The <see cref="Shader"/> or the <see cref="ComputeShader"/></param>
        /// <param name="variant">The variant for the given type of shader</param>
        /// <returns>A formatted <see cref="string"/></returns>
        protected abstract string ToJson(TShader shader, TShaderVariant variant);

        void Export([NotNull] TShader shader, TShaderVariant shaderVariant, int variantIn, int variantOut, int totalVariantIn, int totalVariantOut)
        {
            if (!exportStrippedVariants)
                return;

            try
            {
                System.IO.File.AppendAllText(
                            exportFilename,
                            $"{{ {ToJson(shader, shaderVariant)}, \"variantIn\": \"{variantIn}\", \"variantOut\": \"{variantOut}\", \"totalVariantIn\": \"{totalVariantIn}\", \"totalVariantOut\": \"{totalVariantOut}\" }}\r\n"
                        );
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
        #endregion
    }
}
