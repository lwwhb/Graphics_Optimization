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
            uint[] output = new uint[count];
            for (int i = 0; i < output.Length; ++i)
                output[i] = (uint)(i * 2);
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
                return false;
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

        [Test]
        public void TestPrefixSumOnSingleGroup()
        {
            uint[] inputArray = CreateInputArray0(GpuPrefixSumDefs.GroupSize);
            var inputBuffer = CreateBuffer(inputArray);

            CommandBuffer cmdBuffer = new CommandBuffer();
            var resources = GpuPrefixSumSupportResources.Create(inputArray.Length);

            var arguments = new GpuPrefixSumDirectArgs();
            arguments.input = inputBuffer;
            arguments.inputCount = inputArray.Length;
            arguments.supportResources = resources;
            m_PrefixSumSystem.DispatchDirect(cmdBuffer, arguments);

            Graphics.ExecuteCommandBuffer(cmdBuffer);

            var referenceOutput = CpuPrefixSum(inputArray);
            var results = DownloadData(arguments.supportResources.output);

            TestCompareArrays(referenceOutput, results);

            cmdBuffer.Release();
            inputBuffer.Dispose();
            resources.Dispose();
        }
    }
}
