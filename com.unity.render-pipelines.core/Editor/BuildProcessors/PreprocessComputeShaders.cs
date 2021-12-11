using System;
using System.Collections.Generic;
using UnityEditor.Build;
using UnityEngine;

namespace UnityEditor.Rendering
{
    /// <summary>
    /// Implements common functionality for SRP for the <see cref="IPreprocessComputeShaders"/>
    /// </summary>
    sealed class PreprocessComputeShaders : ShaderPreprocessor<ComputeShader, string>, IPreprocessComputeShaders
    {
        /// <summary>
        /// Multiple callback may be implemented. The first one executed is the one where callbackOrder is returning the smallest number.
        /// </summary>
        public int callbackOrder => 0;

        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> compilerDataList)
        {
            int prevVariantsCount = compilerDataList.Count;
            if (StripShaderVariants(shader, kernelName, compilerDataList, out double totalStripTime))
            {
                int currVariantsCount = compilerDataList.Count;

                if (logStrippedVariants)
                    LogShaderVariants(shader, kernelName, prevVariantsCount, currVariantsCount, totalStripTime);

                if (exportStrippedVariants)
                    Export(shader, kernelName, prevVariantsCount, currVariantsCount, totalVariantsInputCount, totalVariantsOutputCount);
            }
            else
            {
                Debug.LogError("Error while stripping compute shader");
            }
        }

        void LogShaderVariants(ComputeShader shader, string kernelName, int prevVariantsCount, int currVariantsCount, double stripTimeMs)
        {
            float percentageCurrent = (float)currVariantsCount / prevVariantsCount * 100.0f;
            float percentageTotal = (float)totalVariantsOutputCount / totalVariantsInputCount * 100.0f;

            string result = string.Format("STRIPPING: {0} (kernel: {1}) -" +
                " Remaining compute shader variants = {2}/{3} = {4}% - Total = {5}/{6} = {7}% Time={8}ms",
                shader.name, kernelName, currVariantsCount,
                prevVariantsCount, percentageCurrent, totalVariantsOutputCount, totalVariantsInputCount,
                percentageTotal, stripTimeMs);
            Debug.Log(result);
        }

        const string s_TempComputeShaderStripJson = "Temp/compute-shader-strip.json";
        static void Export(ComputeShader shader, string kernelName, int variantIn, int variantOut, int totalVariantIn, int totalVariantOut)
        {
            try
            {
                System.IO.File.AppendAllText(
                            s_TempComputeShaderStripJson,
                            $"{{ \"shader\": \"{shader?.name}\",  \"kernel\": \"{kernelName}\", \"variantIn\": \"{variantIn}\", \"variantOut\": \"{variantOut}\", \"totalVariantIn\": \"{totalVariantIn}\", \"totalVariantOut\": \"{totalVariantOut}\" }}\r\n"
                        );
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
