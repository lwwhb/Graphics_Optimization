using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

#if PROFILE_BUILD
using UnityEngine.Profiling;
#endif

namespace UnityEditor.Rendering
{
    internal abstract class ShaderPreprocessor<TShader, TShaderVariant>
    {
        /// <summary>
        /// Constructor that fetch all the IVariantStripper defined on the assemblies
        /// </summary>
        protected ShaderPreprocessor()
        {
            // Obtain logging and export information if the Global settings are configured as IShaderVariantSettings
            if (RenderPipelineManager.currentPipeline != null &&
                RenderPipelineManager.currentPipeline.defaultSettings is IShaderVariantSettings shaderVariantSettings)
            {
                logStrippedVariants = shaderVariantSettings.shaderVariantLogLevel;
                exportStrippedVariants = shaderVariantSettings.exportShaderVariants;
            }

            var validStrippers = new List<IVariantStripper<TShader, TShaderVariant>>();
            // Gather all the implementations of IVariantStripper and add them as the strippers
            foreach (var stripper in TypeCache.GetTypesDerivedFrom<IVariantStripper<TShader, TShaderVariant>>())
            {
                if (stripper.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null) !=
                    null)
                {
                    var stripperInstance =
                        Activator.CreateInstance(stripper) as IVariantStripper<TShader, TShaderVariant>;
                    if (stripperInstance.isActive)
                        validStrippers.Add(stripperInstance);
                }
            }

            // Sort them by priority
            strippers = validStrippers
                .OrderByDescending(spp => spp.priority)
                .ToArray();
        }

        private IVariantStripper<TShader, TShaderVariant>[] strippers { get; }

        private int totalVariantsInputCount { get; set; }
        private int totalVariantsOutputCount { get; set; }

        private ShaderVariantLogLevel logStrippedVariants { get; }
        private bool exportStrippedVariants { get; }

        /// <summary>
        /// Strips the given <see cref="TShader" />
        /// </summary>
        /// <param name="shader">The <see cref="T" /> that might be stripped.</param>
        /// <param name="shaderVariant">The <see cref="TShaderVariant" /></param>
        /// <param name="compilerDataList">A list of <see cref="ShaderCompilerData" /></param>
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

            var beforeStrippingInputShaderVariantCount = compilerDataList.Count;

            // Early exit from the stripping
            if (beforeStrippingInputShaderVariantCount == 0)
                return true;

            var afterStrippingShaderVariantCount = beforeStrippingInputShaderVariantCount;

            double stripTimeMs = 0;
            using (TimedScope.FromPtr(&stripTimeMs))
            {
                // Go through all the shader variants
                for (var i = 0; i < afterStrippingShaderVariantCount;)
                {
                    // By default, all variants are stripped if there are not strippers using it
                    var canRemoveVariant = strippers
                        .Aggregate(true, (current, stripper) => current & stripper.CanRemoveShaderVariant(shader, shaderVariant, compilerDataList[i]));

                    // Remove at swap back
                    if (canRemoveVariant)
                        compilerDataList[i] = compilerDataList[--afterStrippingShaderVariantCount];
                    else
                        ++i;
                }

                // Remove the shader variants that will be at the back
                compilerDataList.RemoveBack(beforeStrippingInputShaderVariantCount - afterStrippingShaderVariantCount);
            }

            // Accumulate diagnostics information
            var outputVariantCount = compilerDataList.Count;
            totalVariantsInputCount += beforeStrippingInputShaderVariantCount;
            totalVariantsOutputCount += compilerDataList.Count;

            // Dump information if need
            LogShaderVariants(shader, shaderVariant, beforeStrippingInputShaderVariantCount, outputVariantCount, stripTimeMs);
            Export(shader, shaderVariant, beforeStrippingInputShaderVariantCount, outputVariantCount, totalVariantsInputCount,
                totalVariantsOutputCount);

#if PROFILE_BUILD
            Profiler.EndSample();
#endif
            return true;
        }

        #region Logging

        /// <summary>
        ///  Obtains a formatted <see cref="string" /> with the shader name and the valuable info about the variant
        /// </summary>
        /// <param name="shader">The <see cref="Shader" /> or the <see cref="ComputeShader" /></param>
        /// <param name="variant">The variant for the given type of shader</param>
        /// <returns>A formatted <see cref="string" /></returns>
        protected abstract string ToLog(TShader shader, TShaderVariant variant);

        private void LogShaderVariants([NotNull] TShader shader, TShaderVariant shaderVariant, int prevVariantsCount,
            int currVariantsCount, double stripTimeMs)
        {
            // If the log is disabled, or the shader that we are logging is not processed by any stripper, do not log the stripped variants
            switch (logStrippedVariants)
            {
                case ShaderVariantLogLevel.Disabled:
                case ShaderVariantLogLevel.OnlySRPShaders when !strippers.Any(s => s.IsProcessed(shader)):
                    return;
            }

            // The log the stripping
            var percentageCurrent = currVariantsCount / (float)prevVariantsCount * 100f;
            var percentageTotal = totalVariantsOutputCount / (float)totalVariantsInputCount * 100f;
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
        /// Obtains a JSON valid formatted <see cref="string" /> with the shader name and the valuable info about the variant
        /// </summary>
        /// <param name="shader">The <see cref="Shader" /> or the <see cref="ComputeShader" /></param>
        /// <param name="variant">The variant for the given type of shader</param>
        /// <returns>A formatted <see cref="string" /></returns>
        protected abstract string ToJson(TShader shader, TShaderVariant variant);

        private void Export([NotNull] TShader shader, TShaderVariant shaderVariant, int variantIn, int variantOut,
            int totalVariantIn, int totalVariantOut)
        {
            if (!exportStrippedVariants)
                return;

            try
            {
                File.AppendAllText(
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
