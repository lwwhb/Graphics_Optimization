using System;
using System.Collections.Generic;
using UnityEditor.Build;
using UnityEngine;

namespace UnityEditor.Rendering
{
    /// <summary>
    /// Implements common functionality for SRP for the <see cref="IPreprocessShaders"/>
    /// </summary>
    sealed class PreprocessShaders :
        ShaderPreprocessor<Shader, ShaderSnippetData>,
        IPreprocessShaders
    {
        /// <summary>
        /// Multiple callback may be implemented. The first one executed is the one where callbackOrder is returning the smallest number.
        /// </summary>
        public int callbackOrder => 0;

        public void OnProcessShader(Shader shader, ShaderSnippetData snippetData, IList<ShaderCompilerData> compilerDataList)
        {
            int prevVariantsCount = compilerDataList.Count;
            if (StripShaderVariants(shader, snippetData, compilerDataList, out double totalStripTime))
            {
                int currVariantsCount = compilerDataList.Count;

                if (logStrippedVariants)
                    LogShaderVariants(shader, snippetData, prevVariantsCount, currVariantsCount, totalStripTime);

                if (exportStrippedVariants)
                    Export(shader, snippetData, prevVariantsCount, currVariantsCount, totalVariantsInputCount, totalVariantsOutputCount);
            }
            else
            {
                Debug.LogError("Error while stripping shader");
            }
        }

        void LogShaderVariants(Shader shader, ShaderSnippetData snippetData, int prevVariantsCount, int currVariantsCount, double stripTimeMs)
        {
            float percentageCurrent = currVariantsCount / (float)prevVariantsCount * 100f;
            float percentageTotal = totalVariantsOutputCount / (float)totalVariantsInputCount * 100f;

            string result = string.Format("STRIPPING: {0} ({1} pass) ({2}) -" +
                " Remaining shader variants = {3}/{4} = {5}% - Total = {6}/{7} = {8}% Time={9}ms",
                shader.name, snippetData.passName, snippetData.shaderType.ToString(), currVariantsCount,
                prevVariantsCount, percentageCurrent, totalVariantsOutputCount, totalVariantsInputCount,
                percentageTotal, stripTimeMs);
            Debug.Log(result);
        }

        const string s_TempShaderStripJson = "Temp/shader-strip.json";
        static void Export(Shader shader, ShaderSnippetData snippetData, int variantIn, int variantOut, int totalVariantIn, int totalVariantOut)
        {
            try
            {
                System.IO.File.AppendAllText(
                            s_TempShaderStripJson,
                            $"{{ \"shader\": \"{shader?.name}\", \"pass\": \"{snippetData.passName ?? string.Empty}\", \"passType\": \"{snippetData.passType}\", \"shaderType\": \"{snippetData.shaderType}\", \"variantIn\": \"{variantIn}\", \"variantOut\": \"{variantOut}\", \"totalVariantIn\": \"{totalVariantIn}\", \"totalVariantOut\": \"{totalVariantOut}\" }}\r\n"
                        );
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
