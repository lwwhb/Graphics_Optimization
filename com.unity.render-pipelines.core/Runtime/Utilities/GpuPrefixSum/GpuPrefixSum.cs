using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering
{
    [GenerateHLSL]
    internal static class GpuPrefixSumDefs
    {
        public const int GroupSize = 128;

        public static int DivUpGroup(int value)
        {
            return (value + GroupSize - 1) / GroupSize;
        }

        public static int AlignUpGroup(int value)
        {
            return DivUpGroup(value) * GroupSize;
        }
    }

    public struct GpuPrefixSumSupportResources
    {
        public int supportMaxCount;
        public ComputeBuffer prefixBuffer0;

        public ComputeBuffer output => prefixBuffer0;

        public static GpuPrefixSumSupportResources Create(int supportMaxCount)
        {
            int alignedSupportMaxCount = GpuPrefixSumDefs.AlignUpGroup(supportMaxCount);
            return new GpuPrefixSumSupportResources()
            {
                prefixBuffer0 = new ComputeBuffer(alignedSupportMaxCount, 4, ComputeBufferType.Raw),
                supportMaxCount = alignedSupportMaxCount
            };
        }

        public void Dispose()
        {
            prefixBuffer0.Dispose();
            prefixBuffer0 = null;
        }
    }

    public struct GpuPrefixSumArgs
    {
        public int inputCount;
        public ComputeBuffer input;
        public GpuPrefixSumSupportResources supportResources;
    }

    public struct GpuPrefixSum
    {
        private static class KernelIDs
        {
            public static readonly int _InputBuffer = Shader.PropertyToID("_InputBuffer");
            public static readonly int _OutputBuffer = Shader.PropertyToID("_OutputBuffer");
            public static readonly int _PrefixSumIntArgs = Shader.PropertyToID("_PrefixSumIntArgs");
        }

        ComputeShader m_PrefixSumCS;
        private int m_KernelMainPrefixSumOnGroup;
        private int m_KernelMainPrefixSumNextInput;
        private int m_KernelMainPrefixSumResolveParent;

        private void LoadShaders()
        {
            m_PrefixSumCS = (ComputeShader)Resources.Load("GpuPrefixSumKernels");
            m_KernelMainPrefixSumOnGroup = m_PrefixSumCS.FindKernel("MainPrefixSumOnGroup");
            m_KernelMainPrefixSumNextInput = m_PrefixSumCS.FindKernel("MainPrefixSumNextInput");
            m_KernelMainPrefixSumResolveParent = m_PrefixSumCS.FindKernel("MainPrefixSumResolveParent");
        }

        Vector4 PackPrefixSumArgs(int inputCount, int inputOffset, int outputOffset, int parentOffset)
        {
            unsafe
            {
                return new Vector4(
                    *((float*)&inputCount),
                    *((float*)&inputOffset),
                    *((float*)&outputOffset),
                    *((float*)&parentOffset));
            }
        }

        public void Execute(CommandBuffer cmdBuffer, GpuPrefixSumArgs arguments)
        {
            if (arguments.input == null)
                throw new Exception("Input source buffer cannot be null.");

            if (arguments.supportResources.prefixBuffer0 == null)
                throw new Exception("Prefix buffer0 is not instantiated.");

            if (arguments.inputCount > arguments.supportResources.supportMaxCount)
                throw new Exception("Input count exceeds maximum count of support resources. Ensure to create support resources with enough space.");

            var packedArgs = PackPrefixSumArgs(arguments.inputCount, 0, 0, 0);
            cmdBuffer.SetComputeVectorParam(m_PrefixSumCS, KernelIDs._PrefixSumIntArgs, packedArgs);
            cmdBuffer.SetComputeBufferParam(m_PrefixSumCS, m_KernelMainPrefixSumOnGroup, KernelIDs._InputBuffer, arguments.input);
            cmdBuffer.SetComputeBufferParam(m_PrefixSumCS, m_KernelMainPrefixSumOnGroup, KernelIDs._OutputBuffer, arguments.supportResources.output);
            cmdBuffer.DispatchCompute(m_PrefixSumCS, m_KernelMainPrefixSumOnGroup, GpuPrefixSumDefs.DivUpGroup(arguments.inputCount), 1, 1);
        }

        public void Initialize()
        {
            LoadShaders();
        }

        public void Dispose()
        {
            m_PrefixSumCS = null;
        }
    }
}
