using System.Diagnostics;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;

if (args.Length != 1) throw new ArgumentException("Supply the Vulkan loader/MoltenVK library path.");
using var gpu = new ExposureProbe(args[0], File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "exposure.spv")));
foreach (int count in new[] { 1, 37, 600 * 400 })
{
    var input = new float[count * 4];
    for (int i = 0; i < count; i++)
    {
        input[i * 4] = (i % 97 - 20) / 23f;
        input[i * 4 + 1] = (i % 51) / 31f;
        input[i * 4 + 2] = 0.001f + (i % 83) / 19f;
        input[i * 4 + 3] = (i % 5) / 4f;
    }
    foreach (float gain in new[] { 0.0625f, 1f, MathF.Pow(2, 0.5f), 16f })
    {
        var timer = Stopwatch.StartNew();
        float[] output = gpu.Process(input, gain);
        double error = 0;
        for (int i = 0; i < input.Length; i++)
        {
            float expected = i % 4 == 3 ? input[i] : input[i] * gain;
            error = Math.Max(error, Math.Abs(output[i] - expected));
            if (!float.IsFinite(output[i]) || Math.Abs(output[i] - expected) > 1e-6 * Math.Max(1, Math.Abs(expected)))
                throw new Exception($"GPU mismatch at {i}: {output[i]} expected {expected}");
            if (i % 4 == 3 && output[i] != expected) throw new Exception("GPU changed alpha");
        }
        Console.WriteLine($"PASS: GPU linear exposure {count} pixels, gain {gain:G6}, max error {error:G6}, total including allocation/upload/fence/readback {timer.Elapsed.TotalMilliseconds:F3}ms");
    }
}

/// <summary>Standalone Vulkan compute feasibility probe; owns handles and uses explicit host/shader barriers.</summary>
sealed unsafe class ExposureProbe : IDisposable
{
    private readonly Vk _vk;
    private Instance _instance;
    private PhysicalDevice _physical;
    private Device _device;
    private Queue _queue;
    private uint _queueIndex;
    private DescriptorSetLayout _setLayout;
    private PipelineLayout _layout;
    private Pipeline _pipeline;
    private CommandPool _commands;
    private bool _disposed;

    public ExposureProbe(string library, byte[] shader)
    {
        _vk = new Vk(new DefaultNativeContext(library));
        try
        {
            var app = new ApplicationInfo { SType = StructureType.ApplicationInfo, ApiVersion = Vk.Version11 };
            nint portability = SilkMarshal.StringToPtr("VK_KHR_portability_enumeration");
            try
            {
                byte* extension = (byte*)portability;
                bool supported = _vk.IsInstanceExtensionPresent("VK_KHR_portability_enumeration");
                var create = new InstanceCreateInfo { SType = StructureType.InstanceCreateInfo, PApplicationInfo = &app,
                    Flags = supported ? InstanceCreateFlags.EnumeratePortabilityBitKhr : 0,
                    EnabledExtensionCount = supported ? 1u : 0u, PpEnabledExtensionNames = supported ? &extension : null };
                Check(_vk.CreateInstance(&create, null, out _instance));
            }
            finally { SilkMarshal.Free(portability); }
            _vk.CurrentInstance = _instance;
            foreach (var physical in _vk.GetPhysicalDevices(_instance))
            {
                _vk.GetPhysicalDeviceProperties(physical, out var properties);
                if (properties.DeviceType != PhysicalDeviceType.IntegratedGpu && properties.DeviceType != PhysicalDeviceType.DiscreteGpu) continue;
                uint count = 0; _vk.GetPhysicalDeviceQueueFamilyProperties(physical, &count, null);
                var families = new QueueFamilyProperties[count];
                fixed (QueueFamilyProperties* pointer = families)
                    _vk.GetPhysicalDeviceQueueFamilyProperties(physical, &count, pointer);
                for (uint i = 0; i < count; i++)
                    if ((families[i].QueueFlags & QueueFlags.ComputeBit) != 0)
                    { _physical = physical; _queueIndex = i; break; }
                if (_physical.Handle != 0)
                {
                    _vk.GetPhysicalDeviceFeatures(physical, out var features);
                    Console.WriteLine($"DEVICE: {SilkMarshal.PtrToString((nint)properties.DeviceName)}; type {properties.DeviceType}; shaderFloat64 {(bool)features.ShaderFloat64}");
                    break;
                }
            }
            if (_physical.Handle == 0) throw new NotSupportedException("No hardware Vulkan compute device.");
            float priority = 1;
            var queue = new DeviceQueueCreateInfo { SType = StructureType.DeviceQueueCreateInfo, QueueFamilyIndex = _queueIndex, QueueCount = 1, PQueuePriorities = &priority };
            nint subset = SilkMarshal.StringToPtr("VK_KHR_portability_subset");
            try
            {
                byte* extension = (byte*)subset;
                bool supported = _vk.IsDeviceExtensionPresent(_physical, "VK_KHR_portability_subset");
                var create = new DeviceCreateInfo { SType = StructureType.DeviceCreateInfo, QueueCreateInfoCount = 1, PQueueCreateInfos = &queue,
                    EnabledExtensionCount = supported ? 1u : 0u, PpEnabledExtensionNames = supported ? &extension : null };
                Check(_vk.CreateDevice(_physical, &create, null, out _device));
            }
            finally { SilkMarshal.Free(subset); }
            _vk.CurrentDevice = _device;
            _vk.GetDeviceQueue(_device, _queueIndex, 0, out _queue);
            var bindings = stackalloc DescriptorSetLayoutBinding[2];
            for (uint i = 0; i < 2; i++) bindings[i] = new DescriptorSetLayoutBinding { Binding = i, DescriptorType = DescriptorType.StorageBuffer, DescriptorCount = 1, StageFlags = ShaderStageFlags.ComputeBit };
            var set = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 2, PBindings = bindings };
            Check(_vk.CreateDescriptorSetLayout(_device, &set, null, out _setLayout));
            var push = new PushConstantRange { StageFlags = ShaderStageFlags.ComputeBit, Offset = 0, Size = 8 };
            var setLayout = _setLayout;
            var layout = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 1, PSetLayouts = &setLayout, PushConstantRangeCount = 1, PPushConstantRanges = &push };
            Check(_vk.CreatePipelineLayout(_device, &layout, null, out _layout));
            ShaderModule module = default;
            nint main = SilkMarshal.StringToPtr("main");
            try
            {
                fixed (byte* code = shader)
                {
                    var info = new ShaderModuleCreateInfo { SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)shader.Length, PCode = (uint*)code };
                    Check(_vk.CreateShaderModule(_device, &info, null, out module));
                }
                var pipeline = new ComputePipelineCreateInfo { SType = StructureType.ComputePipelineCreateInfo, Layout = _layout,
                    Stage = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.ComputeBit, Module = module, PName = (byte*)main } };
                Check(_vk.CreateComputePipelines(_device, default, 1, &pipeline, null, out _pipeline));
            }
            finally { if (module.Handle != 0) _vk.DestroyShaderModule(_device, module, null); SilkMarshal.Free(main); }
            var pool = new CommandPoolCreateInfo { SType = StructureType.CommandPoolCreateInfo, QueueFamilyIndex = _queueIndex };
            Check(_vk.CreateCommandPool(_device, &pool, null, out _commands));
        }
        catch { Dispose(); throw; }
    }

    public float[] Process(float[] input, float gain)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ulong bytes = checked((ulong)input.Length * sizeof(float));
        (VkBuffer Buffer, DeviceMemory Memory) source = default, target = default;
        DescriptorPool descriptors = default;
        Fence fence = default;
        CommandBuffer command = default;
        try
        {
            source = Buffer(bytes); target = Buffer(bytes);
            void* data;
            Check(_vk.MapMemory(_device, source.Memory, 0, bytes, 0, &data));
            try { input.AsSpan().CopyTo(new Span<float>(data, input.Length)); }
            finally { _vk.UnmapMemory(_device, source.Memory); }
            var size = new DescriptorPoolSize { Type = DescriptorType.StorageBuffer, DescriptorCount = 2 };
            var pool = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = 1, PoolSizeCount = 1, PPoolSizes = &size };
            Check(_vk.CreateDescriptorPool(_device, &pool, null, out descriptors));
            var setLayout = _setLayout;
            var allocate = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = descriptors, DescriptorSetCount = 1, PSetLayouts = &setLayout };
            Check(_vk.AllocateDescriptorSets(_device, &allocate, out var set));
            var infos = stackalloc DescriptorBufferInfo[2];
            infos[0] = new DescriptorBufferInfo { Buffer = source.Buffer, Range = bytes };
            infos[1] = new DescriptorBufferInfo { Buffer = target.Buffer, Range = bytes };
            var writes = stackalloc WriteDescriptorSet[2];
            for (uint i = 0; i < 2; i++) writes[i] = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = set, DstBinding = i, DescriptorCount = 1, DescriptorType = DescriptorType.StorageBuffer, PBufferInfo = &infos[i] };
            _vk.UpdateDescriptorSets(_device, 2, writes, 0, null);
            var commandInfo = new CommandBufferAllocateInfo { SType = StructureType.CommandBufferAllocateInfo, CommandPool = _commands, Level = CommandBufferLevel.Primary, CommandBufferCount = 1 };
            Check(_vk.AllocateCommandBuffers(_device, &commandInfo, out command));
            var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
            Check(_vk.BeginCommandBuffer(command, &begin));
            var before = Barrier(source.Buffer, bytes, AccessFlags.HostWriteBit, AccessFlags.ShaderReadBit);
            _vk.CmdPipelineBarrier(command, PipelineStageFlags.HostBit, PipelineStageFlags.ComputeShaderBit, 0, 0, null, 1, &before, 0, null);
            _vk.CmdBindPipeline(command, PipelineBindPoint.Compute, _pipeline);
            _vk.CmdBindDescriptorSets(command, PipelineBindPoint.Compute, _layout, 0, 1, &set, 0, null);
            uint* values = stackalloc uint[2]; values[0] = (uint)(input.Length / 4); values[1] = BitConverter.SingleToUInt32Bits(gain);
            _vk.CmdPushConstants(command, _layout, ShaderStageFlags.ComputeBit, 0, 8, values);
            _vk.CmdDispatch(command, (values[0] + 63) / 64, 1, 1);
            var after = Barrier(target.Buffer, bytes, AccessFlags.ShaderWriteBit, AccessFlags.HostReadBit);
            _vk.CmdPipelineBarrier(command, PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.HostBit, 0, 0, null, 1, &after, 0, null);
            Check(_vk.EndCommandBuffer(command));
            var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
            Check(_vk.CreateFence(_device, &fenceInfo, null, out fence));
            var submit = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &command };
            Check(_vk.QueueSubmit(_queue, 1, &submit, fence));
            Check(_vk.WaitForFences(_device, 1, &fence, true, 5_000_000_000));
            var output = new float[input.Length];
            Check(_vk.MapMemory(_device, target.Memory, 0, bytes, 0, &data));
            try { new ReadOnlySpan<float>(data, input.Length).CopyTo(output); }
            finally { _vk.UnmapMemory(_device, target.Memory); }
            return output;
        }
        finally
        {
            _vk.DeviceWaitIdle(_device);
            if (fence.Handle != 0) _vk.DestroyFence(_device, fence, null);
            if (command.Handle != 0) _vk.FreeCommandBuffers(_device, _commands, 1, &command);
            if (descriptors.Handle != 0) _vk.DestroyDescriptorPool(_device, descriptors, null);
            Release(source); Release(target);
        }
    }

    private (VkBuffer Buffer, DeviceMemory Memory) Buffer(ulong bytes)
    {
        VkBuffer buffer = default; DeviceMemory memory = default;
        try
        {
            var info = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = bytes, Usage = BufferUsageFlags.StorageBufferBit, SharingMode = SharingMode.Exclusive };
            Check(_vk.CreateBuffer(_device, &info, null, out buffer));
            _vk.GetBufferMemoryRequirements(_device, buffer, out var requirements);
            _vk.GetPhysicalDeviceMemoryProperties(_physical, out var properties);
            uint index = uint.MaxValue;
            var required = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
            for (uint i = 0; i < properties.MemoryTypeCount; i++)
                if ((requirements.MemoryTypeBits & (1u << (int)i)) != 0 && (properties.MemoryTypes[(int)i].PropertyFlags & required) == required)
                { index = i; break; }
            if (index == uint.MaxValue) throw new NotSupportedException("No coherent host-visible memory for probe.");
            var allocate = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo, AllocationSize = requirements.Size, MemoryTypeIndex = index };
            Check(_vk.AllocateMemory(_device, &allocate, null, out memory));
            Check(_vk.BindBufferMemory(_device, buffer, memory, 0));
            return (buffer, memory);
        }
        catch { Release((buffer, memory)); throw; }
    }

    private static BufferMemoryBarrier Barrier(VkBuffer buffer, ulong bytes, AccessFlags source, AccessFlags target)
        => new() { SType = StructureType.BufferMemoryBarrier, SrcAccessMask = source, DstAccessMask = target,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored, DstQueueFamilyIndex = Vk.QueueFamilyIgnored, Buffer = buffer, Size = bytes };

    private void Release((VkBuffer Buffer, DeviceMemory Memory) allocation)
    {
        if (allocation.Buffer.Handle != 0) _vk.DestroyBuffer(_device, allocation.Buffer, null);
        if (allocation.Memory.Handle != 0) _vk.FreeMemory(_device, allocation.Memory, null);
    }

    private static void Check(Result result)
    {
        if (result != Result.Success) throw new InvalidOperationException("Vulkan: " + result);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_device.Handle != 0)
        {
            _vk.DeviceWaitIdle(_device);
            if (_commands.Handle != 0) _vk.DestroyCommandPool(_device, _commands, null);
            if (_pipeline.Handle != 0) _vk.DestroyPipeline(_device, _pipeline, null);
            if (_layout.Handle != 0) _vk.DestroyPipelineLayout(_device, _layout, null);
            if (_setLayout.Handle != 0) _vk.DestroyDescriptorSetLayout(_device, _setLayout, null);
            _vk.DestroyDevice(_device, null);
        }
        if (_instance.Handle != 0) _vk.DestroyInstance(_instance, null);
        _vk.Dispose();
    }
}
