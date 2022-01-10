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
        const string s_TempComputeShaderStripJson = "Temp/compute-shader-strip.json";

        /// <summary>
        /// Multiple callback may be implemented. The first one executed is the one where callbackOrder is returning the smallest number.
        /// </summary>
        public int callbackOrder => 0;

        protected override string exportFilename => s_TempComputeShaderStripJson;

        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> compilerDataList)
        {
            if (!StripShaderVariants(shader, kernelName, compilerDataList))
            {
                Debug.LogError("Error while stripping compute shader");
            }
        }

        /// <summary>
        /// Obtains a JSON valid formatted <see cref="string"/> with the shader name and the valuable info about the variant
        /// </summary>
        /// <param name="shader">The <see cref="Shader"/> or the <see cref="ComputeShader"/></param>
        /// <param name="variant">The variant for the given type of shader</param>
        /// <returns>A formatted <see cref="string"/></returns>
        protected override string ToJson(ComputeShader shader, string kernelName)
        {
            return $"\"shader\": \"{shader?.name}\", \"kernel\": \"{kernelName}\"";
        }

        /// <summary>
        /// Obtains a formatted <see cref="string"/> with the shader name and the valuable info about the variant
        /// </summary>
        /// <param name="shader">The <see cref="Shader"/> or the <see cref="ComputeShader"/></param>
        /// <param name="variant">The variant for the given type of shader</param>
        /// <returns>A formatted <see cref="string"/></returns>
        protected override string ToLog(ComputeShader shader, string kernelName)
        {
            return $"{shader.name} (kernel: {kernelName})";
        }
    }
}
