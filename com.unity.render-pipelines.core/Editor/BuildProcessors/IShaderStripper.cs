using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace UnityEditor.Rendering
{
    /// <summary>
    /// Stripper generic interface
    /// </summary>
    public interface IStripper
    {
        /// <summary>
        /// Returns if the stripper is active
        /// </summary>
        bool isActive { get; }

        /// <summary>
        /// Specifies the priority of the stripper
        /// </summary>
        int priority { get; }
    }

    /// <summary>
    /// Common interface for stripping shader variants
    /// </summary>
    /// <typeparam name="T">The shader type</typeparam>
    /// <typeparam name="U">The shader input type</typeparam>
    public interface IVariantStripper<T, U> : IStripper
    {
        /// <summary>
        /// Specifies if the variant is not used by the stripper, so it can be removed from the build
        /// </summary>
        /// <param name="shader">The shader to check if the variant can be stripped</param>
        /// <param name="shaderInput">The variant to check if it can be stripped</param>
        /// <param name="compilerData">The <see cref="ShaderCompilerData"/></param>
        /// <returns>If the Shader Variant can be stripped</returns>
        bool CanRemoveShaderVariant([NotNull] T shader, U shaderInput, ShaderCompilerData compilerData);

        /// <summary>
        /// Returns if the shader is being processed by the stripper
        /// </summary>
        /// <param name="shader">The shader to check if the variant can be stripped</param>
        /// <returns>true, if the shader belongs to the pipeline</returns>
        bool IsProcessed([NotNull] T shader);
    }

    /// <summary>
    /// Helper interface to declare <see cref="Shader"/> variants stripper
    /// </summary>
    public interface IShaderVariantStripper: IVariantStripper<Shader, ShaderSnippetData> { }

    /// <summary>
    /// Helper interface to declare <see cref="ComputeShader"/> variants stripper
    /// </summary>
    public interface IComputeVariantStripper : IVariantStripper<ComputeShader, string> { }
}
