using System;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;

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

    [GenerateHLSL(PackingRules.Exact, false, false)]
    internal struct GpuPrefixSumLevelOffsets
    {
        public uint count;
        public uint inputOffset;
        public uint outputOffset;
        public uint parentOffset;
    }

    public struct GpuPrefixSumSupportResources
    {
        public int alignedElementCount;
        public int maxBufferCount;
        public int maxLevelCount;
        public ComputeBuffer prefixBuffer0;
        public ComputeBuffer prefixBuffer1;
        public ComputeBuffer totalLevelCountBuffer;
        public ComputeBuffer levelOffsetBuffer;
        public ComputeBuffer indirectDispatchArgsBuffer;
        public ComputeBuffer output => prefixBuffer0;

        public static GpuPrefixSumSupportResources Create(int maxElementCount)
        {
            var resources = new GpuPrefixSumSupportResources() { alignedElementCount = 0 };
            resources.Resize(maxElementCount);
            return resources;
        }

        public void Resize(int newMaxElementCount)
        {
            newMaxElementCount = Math.Max(newMaxElementCount, 1); //at bare minimum support a single group.
            if (alignedElementCount >= newMaxElementCount)
                return;

            Dispose();
            CalculateTotalBufferSize(newMaxElementCount, out int totalSize, out int levelCounts);
            prefixBuffer0 = new ComputeBuffer(totalSize, 4, ComputeBufferType.Raw);
            prefixBuffer1 = new ComputeBuffer(newMaxElementCount, 4, ComputeBufferType.Raw);
            totalLevelCountBuffer = new ComputeBuffer(1, 4, ComputeBufferType.Raw);
            levelOffsetBuffer = new ComputeBuffer(levelCounts, System.Runtime.InteropServices.Marshal.SizeOf<GpuPrefixSumLevelOffsets>(), ComputeBufferType.Structured);
            indirectDispatchArgsBuffer = new ComputeBuffer(levelCounts, 4 * 3, ComputeBufferType.Structured | ComputeBufferType.IndirectArguments);
            alignedElementCount = GpuPrefixSumDefs.AlignUpGroup(newMaxElementCount);
            maxBufferCount = totalSize;
            maxLevelCount = levelCounts;
        }

        public static void CalculateTotalBufferSize(int maxElementCount, out int totalSize, out int levelCounts)
        {
            int alignedSupportMaxCount = GpuPrefixSumDefs.AlignUpGroup(maxElementCount);
            totalSize = alignedSupportMaxCount;
            levelCounts = 1;
            while (alignedSupportMaxCount > GpuPrefixSumDefs.GroupSize)
            {
                alignedSupportMaxCount = GpuPrefixSumDefs.AlignUpGroup(GpuPrefixSumDefs.DivUpGroup(alignedSupportMaxCount));
                totalSize += alignedSupportMaxCount;
                ++levelCounts;
            }
        }

        public void Dispose()
        {
            if (alignedElementCount == 0)
                return;

            alignedElementCount = 0;
            if (prefixBuffer0 != null)
            {
                prefixBuffer0.Dispose();
                prefixBuffer0 = null;
            }

            if (prefixBuffer1 != null)
            {
                prefixBuffer1.Dispose();
                prefixBuffer1 = null;
            }

            if (levelOffsetBuffer != null)
            {
                levelOffsetBuffer.Dispose();
                levelOffsetBuffer = null;
            }

            if (indirectDispatchArgsBuffer != null)
            {
                indirectDispatchArgsBuffer.Dispose();
                indirectDispatchArgsBuffer = null;
            }

            if (totalLevelCountBuffer != null)
            {
                totalLevelCountBuffer.Dispose();
                totalLevelCountBuffer = null;
            }
        }
    }

    public struct GpuPrefixSumDirectArgs
    {
        public int inputCount;
        public ComputeBuffer input;
        public GpuPrefixSumSupportResources supportResources;
    }

    public struct GpuPrefixSumIndirectDirectArgs
    {
        public int inputCountBufferByteOffset;
        public ComputeBuffer inputCountBuffer;
        public ComputeBuffer input;
        public GpuPrefixSumSupportResources supportResources;
    }

    public struct GpuPrefixSum
    {
        private static class KernelIDs
        {
            public static readonly int _InputBuffer = Shader.PropertyToID("_InputBuffer");
            public static readonly int _OutputBuffer = Shader.PropertyToID("_OutputBuffer");
            public static readonly int _InputCountBuffer = Shader.PropertyToID("_InputCountBuffer");
            public static readonly int _TotalLevelsBuffer = Shader.PropertyToID("_TotalLevelsBuffer");
            public static readonly int _OutputTotalLevelsBuffer = Shader.PropertyToID("_OutputTotalLevelsBuffer");
            public static readonly int _OutputDispatchLevelArgsBuffer = Shader.PropertyToID("_OutputDispatchLevelArgsBuffer");
            public static readonly int _LevelsOffsetsBuffer = Shader.PropertyToID("_LevelsOffsetsBuffer");
            public static readonly int _OutputLevelsOffsetsBuffer = Shader.PropertyToID("_OutputLevelsOffsetsBuffer");
            public static readonly int _PrefixSumIntArgs0 = Shader.PropertyToID("_PrefixSumIntArgs0");
            public static readonly int _PrefixSumIntArgs1 = Shader.PropertyToID("_PrefixSumIntArgs1");
        }

        ComputeShader m_PrefixSumCS;
        private int m_KernelMainCalculateLevelDispatchArgsFromConst;
        private int m_KernelMainCalculateLevelDispatchArgsFromBuffer;
        private int m_KernelMainPrefixSumOnGroup;
        private int m_KernelMainPrefixSumNextInput;
        private int m_KernelMainPrefixSumResolveParent;

        private void LoadShaders()
        {
            m_PrefixSumCS = (ComputeShader)Resources.Load("GpuPrefixSumKernels");
            m_KernelMainCalculateLevelDispatchArgsFromConst = m_PrefixSumCS.FindKernel("MainCalculateLevelDispatchArgsFromConst");
            m_KernelMainCalculateLevelDispatchArgsFromBuffer = m_PrefixSumCS.FindKernel("MainCalculateLevelDispatchArgsFromBuffer");
            m_KernelMainPrefixSumOnGroup = m_PrefixSumCS.FindKernel("MainPrefixSumOnGroup");
            m_KernelMainPrefixSumNextInput = m_PrefixSumCS.FindKernel("MainPrefixSumNextInput");
            m_KernelMainPrefixSumResolveParent = m_PrefixSumCS.FindKernel("MainPrefixSumResolveParent");
        }

        Vector4 PackPrefixSumArgs(int a, int b, int c, int d)
        {
            unsafe
            {
                return new Vector4(
                    *((float*)&a),
                    *((float*)&b),
                    *((float*)&c),
                    *((float*)&d));
            }
        }

        private void ExecuteCommonIndirect(CommandBuffer cmdBuffer, ComputeBuffer inputBuffer, in GpuPrefixSumSupportResources supportResources)
        {
            //hierarchy up
            for (int levelId = 0; levelId < supportResources.maxLevelCount; ++levelId)
            {
                var packedArgs = PackPrefixSumArgs(0, 0, 0, levelId);
                cmdBuffer.SetComputeVectorParam(m_PrefixSumCS, KernelIDs._PrefixSumIntArgs1, packedArgs);

                cmdBuffer.SetComputeBufferParam(m_PrefixSumCS, m_KernelMainPrefixSumOnGroup, KernelIDs._InputBuffer, levelId == 0 ? inputBuffer : supportResources.prefixBuffer1);
                cmdBuffer.SetComputeBufferParam(m_PrefixSumCS, m_KernelMainPrefixSumOnGroup, KernelIDs._LevelsOffsetsBuffer, supportResources.levelOffsetBuffer);
                cmdBuffer.SetComputeBufferParam(m_PrefixSumCS, m_KernelMainPrefixSumOnGroup, KernelIDs._OutputBuffer, supportResources.prefixBuffer0);
                cmdBuffer.DispatchCompute(m_PrefixSumCS, m_KernelMainPrefixSumOnGroup, supportResources.indirectDispatchArgsBuffer, (uint)(levelId * 3 * 4));

                if (levelId == supportResources.maxLevelCount - 1)
                    continue;

                cmdBuffer.SetComputeBufferParam(m_PrefixSumCS, m_KernelMainPrefixSumNextInput, KernelIDs._InputBuffer, supportResources.prefixBuffer0);
                cmdBuffer.SetComputeBufferParam(m_PrefixSumCS, m_KernelMainPrefixSumNextInput, KernelIDs._LevelsOffsetsBuffer, supportResources.levelOffsetBuffer);
                cmdBuffer.SetComputeBufferParam(m_PrefixSumCS, m_KernelMainPrefixSumNextInput, KernelIDs._OutputBuffer, supportResources.prefixBuffer1);
                cmdBuffer.DispatchCompute(m_PrefixSumCS, m_KernelMainPrefixSumNextInput, supportResources.indirectDispatchArgsBuffer, (uint)((levelId + 1) * 3 * 4));
            }

            //down the hierarchy
            for (int levelId = supportResources.maxLevelCount - 1; levelId >= 1; --levelId)
            {
                var packedArgs = PackPrefixSumArgs(0, 0, 0, levelId);
                cmdBuffer.SetComputeVectorParam(m_PrefixSumCS, KernelIDs._PrefixSumIntArgs1, packedArgs);
                cmdBuffer.SetComputeBufferParam(m_PrefixSumCS, m_KernelMainPrefixSumResolveParent, KernelIDs._OutputBuffer, supportResources.prefixBuffer0);
                cmdBuffer.SetComputeBufferParam(m_PrefixSumCS, m_KernelMainPrefixSumResolveParent, KernelIDs._LevelsOffsetsBuffer, supportResources.levelOffsetBuffer);
                cmdBuffer.DispatchCompute(m_PrefixSumCS, m_KernelMainPrefixSumResolveParent, supportResources.indirectDispatchArgsBuffer, (uint)((levelId - 1) * 3 * 4));
            }
        }

        public void DispatchDirect(CommandBuffer cmdBuffer, in GpuPrefixSumDirectArgs arguments)
        {
            if (arguments.supportResources.prefixBuffer0 == null || arguments.supportResources.prefixBuffer1 == null)
                throw new Exception("Support resources are not valid.");

            if (arguments.input == null)
                throw new Exception("Input source buffer cannot be null.");

            if (arguments.inputCount > arguments.supportResources.alignedElementCount)
                throw new Exception("Input count exceeds maximum count of support resources. Ensure to create support resources with enough space.");

            //Generate level offsets first, from const value.
            var packedArgs = PackPrefixSumArgs(arguments.inputCount, arguments.supportResources.maxLevelCount, 0, 0);
            cmdBuffer.SetComputeVectorParam(m_PrefixSumCS, KernelIDs._PrefixSumIntArgs1, packedArgs);
            cmdBuffer.SetComputeBufferParam(m_PrefixSumCS, m_KernelMainCalculateLevelDispatchArgsFromConst, KernelIDs._OutputLevelsOffsetsBuffer, arguments.supportResources.levelOffsetBuffer);
            cmdBuffer.SetComputeBufferParam(m_PrefixSumCS, m_KernelMainCalculateLevelDispatchArgsFromConst, KernelIDs._OutputDispatchLevelArgsBuffer, arguments.supportResources.indirectDispatchArgsBuffer);
            cmdBuffer.SetComputeBufferParam(m_PrefixSumCS, m_KernelMainCalculateLevelDispatchArgsFromConst, KernelIDs._OutputTotalLevelsBuffer, arguments.supportResources.totalLevelCountBuffer);
            cmdBuffer.DispatchCompute(m_PrefixSumCS, m_KernelMainCalculateLevelDispatchArgsFromConst, 1, 1, 1);
            ExecuteCommonIndirect(cmdBuffer, arguments.input, arguments.supportResources);
        }

        public void DispatchIndirect(CommandBuffer cmdBuffer, in GpuPrefixSumIndirectDirectArgs arguments)
        {
            if (arguments.supportResources.prefixBuffer0 == null || arguments.supportResources.prefixBuffer1 == null)
                throw new Exception("Support resources are not valid.");

            if (arguments.input == null || arguments.inputCountBuffer == null)
                throw new Exception("Input source buffer and inputCountBuffer cannot be null.");

            //Generate level offsets first, from const value.
            var packedArgs = PackPrefixSumArgs(0, arguments.supportResources.maxLevelCount, arguments.inputCountBufferByteOffset, 0);
            cmdBuffer.SetComputeVectorParam(m_PrefixSumCS, KernelIDs._PrefixSumIntArgs1, packedArgs);
            cmdBuffer.SetComputeBufferParam(m_PrefixSumCS, m_KernelMainCalculateLevelDispatchArgsFromBuffer, KernelIDs._InputCountBuffer, arguments.inputCountBuffer);
            cmdBuffer.SetComputeBufferParam(m_PrefixSumCS, m_KernelMainCalculateLevelDispatchArgsFromBuffer, KernelIDs._OutputLevelsOffsetsBuffer, arguments.supportResources.levelOffsetBuffer);
            cmdBuffer.SetComputeBufferParam(m_PrefixSumCS, m_KernelMainCalculateLevelDispatchArgsFromBuffer, KernelIDs._OutputDispatchLevelArgsBuffer, arguments.supportResources.indirectDispatchArgsBuffer);
            cmdBuffer.SetComputeBufferParam(m_PrefixSumCS, m_KernelMainCalculateLevelDispatchArgsFromBuffer, KernelIDs._OutputTotalLevelsBuffer, arguments.supportResources.totalLevelCountBuffer);
            cmdBuffer.DispatchCompute(m_PrefixSumCS, m_KernelMainCalculateLevelDispatchArgsFromBuffer, 1, 1, 1);

            ExecuteCommonIndirect(cmdBuffer, arguments.input, arguments.supportResources);
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
