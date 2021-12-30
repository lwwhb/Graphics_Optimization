using System;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Tests
{
    class GpuPrefixSumTests
    {
        private GpuPrefixSum m_PrefixSumSystem;

        [SetUp]
        public void OnSetup()
        {
            m_PrefixSumSystem.Initialize();
        }

        [TearDown]
        public void OnTeardown()
        {
            m_PrefixSumSystem.Dispose();
        }

        ComputeBuffer CreateBuffer(uint[] numbers)
        {
            ComputeBuffer buffer = new ComputeBuffer(numbers.Length, 4, ComputeBufferType.Raw);
            buffer.SetData(numbers);
            return buffer;
        }

        uint[] DownloadData(ComputeBuffer buffer)
        {
            CommandBuffer cmdBuffer = new CommandBuffer();
            uint[] outBuffer = null;
            cmdBuffer.RequestAsyncReadback(buffer, (AsyncGPUReadbackRequest req) =>
            {
                if (req.done)
                {
                    var data = req.GetData<uint>();
                    outBuffer = data.ToArray();
                }
            });
            cmdBuffer.WaitAllAsyncReadbackRequests();
            Graphics.ExecuteCommandBuffer(cmdBuffer);

            return outBuffer;
        }

        uint[] CpuPrefixSum(uint[] input, bool isExclusive = false)
        {
            uint[] output = new uint[input.Length];
            uint sum = 0;
            if (isExclusive)
            {
                for (int i = 0; i < input.Length; ++i)
                {
                    output[i] = sum;
                    sum += input[i];
                }
            }
            else
            {
                for (int i = 0; i < input.Length; ++i)
                {
                    sum += input[i];
                    output[i] = sum;
                }
            }
            return output;
        }

        uint[] CreateInputArray0(int count)
        {
            uint[] output = new uint[Math.Max(count, 1)];
            for (int i = 0; i < output.Length; ++i)
                output[i] = (uint)(i * 2) + 1;
            return output;
        }

        bool TestCompareArrays(uint[] a, uint[] b, int offset = 0, int length = -1)
        {
            Assert.Less(offset, a.Length);
            Assert.Less(offset, b.Length);
            if (offset >= a.Length || offset >= b.Length)
                return false;

            int endIdx = 0;
            if (length < 0)
            {
                Assert.IsTrue(a.Length == b.Length);
                if (a.Length != b.Length)
                {
                    return false;
                }
                endIdx = a.Length;
            }
            else
            {
                endIdx = offset + length;
                Assert.GreaterOrEqual(a.Length, endIdx);
                Assert.GreaterOrEqual(b.Length, endIdx);
            }

            for (int i = offset; i < endIdx; ++i)
            {
                if (a[i] != b[i])
                {
                    Assert.Fail("Mismatching array: a[{0}]={1} and b[{0}]={2}.", i, a[i], b[i]);
                    return false;
                }
            }

            return true;
        }

        void ClearOutput(GpuPrefixSumSupportResources resources)
        {
            uint[] zeroArray = new uint[resources.maxBufferCount];
            resources.output.SetData(zeroArray);
        }

        public void TestPrefixSumDirectCommon(int bufferCount)
        {
            uint[] inputArray = CreateInputArray0(bufferCount);
            var inputBuffer = CreateBuffer(inputArray);

            CommandBuffer cmdBuffer = new CommandBuffer();
            var resources = GpuPrefixSumSupportResources.Create(inputArray.Length);

            //Clear the output
            ClearOutput(resources);

            var arguments = new GpuPrefixSumDirectArgs();
            arguments.input = inputBuffer;
            arguments.inputCount = inputArray.Length;
            arguments.supportResources = resources;
            m_PrefixSumSystem.DispatchDirect(cmdBuffer, arguments);

            Graphics.ExecuteCommandBuffer(cmdBuffer);

            var referenceOutput = CpuPrefixSum(inputArray);
            var results = DownloadData(arguments.supportResources.output);

            TestCompareArrays(referenceOutput, results, 0, bufferCount);

            cmdBuffer.Release();
            inputBuffer.Dispose();
            resources.Dispose();
        }

        public void TestPrefixSumIndirectCommon(int bufferCount)
        {
            uint[] inputArray = CreateInputArray0(bufferCount);
            var inputBuffer = CreateBuffer(inputArray);

            var countBuffer = CreateBuffer(new uint[] { (uint)bufferCount });

            CommandBuffer cmdBuffer = new CommandBuffer();
            var resources = GpuPrefixSumSupportResources.Create(inputArray.Length);

            //Clear the output
            ClearOutput(resources);

            var arguments = new GpuPrefixSumIndirectDirectArgs();
            arguments.input = inputBuffer;
            arguments.inputCountBuffer = countBuffer;
            arguments.inputCountBufferByteOffset = 0;
            arguments.supportResources = resources;
            m_PrefixSumSystem.DispatchIndirect(cmdBuffer, arguments);

            Graphics.ExecuteCommandBuffer(cmdBuffer);

            var referenceOutput = CpuPrefixSum(inputArray);
            var results = DownloadData(arguments.supportResources.output);

            TestCompareArrays(referenceOutput, results, 0, bufferCount);

            cmdBuffer.Release();
            inputBuffer.Dispose();
            countBuffer.Dispose();
            resources.Dispose();
        }

        [Test]
        public void TestPrefixSumOnSingleGroup()
        {
            TestPrefixSumDirectCommon(GpuPrefixSumDefs.GroupSize);
        }

        [Test]
        public void TestPrefixSumIndirectOnSingleGroup()
        {
            TestPrefixSumIndirectCommon(GpuPrefixSumDefs.GroupSize);
        }

        [Test]
        public void TestPrefixSumIndirectOnZero()
        {
            TestPrefixSumIndirectCommon(0);
        }

        [Test]
        public void TestPrefixSumIndirectOnOne()
        {
            TestPrefixSumIndirectCommon(1);
        }
    }
}
