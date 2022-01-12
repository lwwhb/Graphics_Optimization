using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEditor.Rendering
{
    /// <summary>
    /// Implements common functionality for SRP for the <see cref="IPreprocessShaders"/>
    /// </summary>
    sealed class PreprocessShaders :
        ShaderPreprocessor<Shader, ShaderSnippetData>,
        IPreprocessShaders
    {
        static readonly ShaderTagId s_RenderPipelineTag = new ShaderTagId("RenderPipeline");

        const string s_TempShaderStripJson = "Temp/shader-strip.json";

        /// <summary>
        /// Multiple callback may be implemented. The first one executed is the one where callbackOrder is returning the smallest number.
        /// </summary>
        public int callbackOrder => 0;

        /// <summary>
        /// Specifies the export filename for the variants stripping information
        /// </summary>
        protected override string exportFilename => s_TempShaderStripJson;

        public void OnProcessShader([NotNull] Shader shader, ShaderSnippetData snippetData, IList<ShaderCompilerData> compilerDataList)
        {
            if (!StripShaderVariants(shader, snippetData, compilerDataList))
            {
                Debug.LogError("Error while stripping shader");
            }
        }

        /// <summary>
        /// Obtains a formatted <see cref="string"/> with the shader name and the valuable info about the variant
        /// </summary>
        /// <param name="shader">The <see cref="Shader"/> or the <see cref="ComputeShader"/></param>
        /// <param name="variant">The variant for the given type of shader</param>
        /// <returns>A formatted <see cref="string"/></returns>
        protected override string ToLog(Shader shader, ShaderSnippetData snippetData)
        {
            return $"{shader.name} ({snippetData.passName} pass) ({snippetData.shaderType})";
        }

        /// <summary>
        /// Obtains a JSON valid formatted <see cref="string"/> with the shader name and the valuable info about the variant
        /// </summary>
        /// <param name="shader">The <see cref="Shader"/> or the <see cref="ComputeShader"/></param>
        /// <param name="variant">The variant for the given type of shader</param>
        /// <returns>A formatted <see cref="string"/></returns>
        protected override string ToJson(Shader shader, ShaderSnippetData snippetData)
        {
           return $"\"shader\": \"{shader?.name}\", \"pass\": \"{snippetData.passName ?? string.Empty}\", \"passType\": \"{snippetData.passType}\"";
        }
    }
}
