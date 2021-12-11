using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace UnityEditor.Rendering
{
    public interface IStripper
    {
        bool isActive { get; }
    }

    public interface IVariantStripper<T, U> : IStripper
    {
        bool IsVariantStripped([NotNull] T shader, U shaderInput, ShaderCompilerData compilerData);
    }


    public interface IShaderVariantStripper: IVariantStripper<Shader, ShaderSnippetData>
    {

    }

    public interface IComputeVariantStripper : IVariantStripper<ComputeShader, string>
    {

    }
}
