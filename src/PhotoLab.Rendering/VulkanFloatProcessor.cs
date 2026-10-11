using PhotoLab.Core;
using PhotoLab.Imaging;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace PhotoLab.Rendering;

/// <summary>Serialized linear-float compute with persistent buffers, explicit barriers and owned readback results.</summary>
public sealed unsafe class VulkanFloatProcessor : IFloatImageProcessor, IDisposable
{
    private readonly Vk _vk;
    private readonly object _gate = new();
    private ulong _maxBytes, _capacity;
    private uint _maxGroupsX, _maxGroupsY;
    private (VkBuffer Buffer, DeviceMemory Memory, nint Address) _source, _target;
    private FloatImageBuffer? _uploadedSource;
    private DescriptorPool _descriptors;
    private DescriptorSet _set;
    private Fence _fence;
    private CommandBuffer _command;
    private Instance _instance;
    private PhysicalDevice _physical;
    private Device _device;
    private Queue _queue;
    private uint _queueIndex;
    private DescriptorSetLayout _setLayout;
    private PipelineLayout _layout;
    private Pipeline _pipeline;
    private CommandPool _commands;
    private bool _disposed, _faulted;

    public VulkanFloatProcessor(string library)
    {
        byte[] shader = ReadShader();
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
                    DeviceName = SilkMarshal.PtrToString((nint)properties.DeviceName) ?? "Unknown";
                    _maxBytes = properties.Limits.MaxStorageBufferRange;
                    _maxGroupsX = properties.Limits.MaxComputeWorkGroupCount[0];
                    _maxGroupsY = properties.Limits.MaxComputeWorkGroupCount[1];
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
            var push = new PushConstantRange { StageFlags = ShaderStageFlags.ComputeBit, Offset = 0, Size = 44 };
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
            var pool = new CommandPoolCreateInfo { SType = StructureType.CommandPoolCreateInfo, QueueFamilyIndex = _queueIndex, Flags = CommandPoolCreateFlags.ResetCommandBufferBit };
            Check(_vk.CreateCommandPool(_device, &pool, null, out _commands));
            var commandInfo = new CommandBufferAllocateInfo { SType = StructureType.CommandBufferAllocateInfo, CommandPool = _commands, Level = CommandBufferLevel.Primary, CommandBufferCount = 1 };
            Check(_vk.AllocateCommandBuffers(_device, &commandInfo, out _command));
            var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
            Check(_vk.CreateFence(_device, &fenceInfo, null, out _fence));
            var descriptorSize = new DescriptorPoolSize { Type = DescriptorType.StorageBuffer, DescriptorCount = 2 };
            var descriptorPool = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = 1, PoolSizeCount = 1, PPoolSizes = &descriptorSize };
            Check(_vk.CreateDescriptorPool(_device, &descriptorPool, null, out _descriptors));
            var allocationLayout = _setLayout;
            var allocate = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = _descriptors, DescriptorSetCount = 1, PSetLayouts = &allocationLayout };
            Check(_vk.AllocateDescriptorSets(_device, &allocate, out _set));
        }
        catch { Dispose(); throw; }
    }

    public string Name => "Vulkan";
    public string DeviceName { get; private set; } = "";
    public int BufferAllocationCount { get; private set; }
    public int SourceUploadCount { get; private set; }
    public ulong BufferCapacityBytes => _capacity * 2;

    public FloatImageBuffer Process(FloatImageBuffer source, EditSettings edit, CancellationToken cancellation = default)
    {
        var controls = FloatAdjustments.From(edit);
        cancellation.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_faulted) throw new InvalidOperationException("GPU processor failed; recreate it before reuse.");
            var input = source.Pixels; // Validate lifetime even when the immutable source is already uploaded.
            ulong bytes = checked((ulong)input.Length * sizeof(float));
            if (bytes > _maxBytes) throw new NotSupportedException("Image exceeds the GPU storage-buffer limit.");
            EnsureBuffers(bytes);
            if (!ReferenceEquals(source, _uploadedSource))
            {
                input.CopyTo(new Span<float>((void*)_source.Address, input.Length));
                _uploadedSource = source; SourceUploadCount++;
            }
            cancellation.ThrowIfCancellationRequested();
            Check(_vk.ResetCommandBuffer(_command, 0));
            var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
            Check(_vk.BeginCommandBuffer(_command, &begin));
            var before = Barrier(_source.Buffer, bytes, AccessFlags.HostWriteBit, AccessFlags.ShaderReadBit);
            _vk.CmdPipelineBarrier(_command, PipelineStageFlags.HostBit, PipelineStageFlags.ComputeShaderBit, 0, 0, null, 1, &before, 0, null);
            _vk.CmdBindPipeline(_command, PipelineBindPoint.Compute, _pipeline);
            var set = _set;
            _vk.CmdBindDescriptorSets(_command, PipelineBindPoint.Compute, _layout, 0, 1, &set, 0, null);
            uint* values = stackalloc uint[11];
            values[0] = (uint)(input.Length / 4); values[1] = edit == new EditSettings() ? 1u : 0u;
            values[2] = BitConverter.SingleToUInt32Bits(controls.Exposure); values[3] = BitConverter.SingleToUInt32Bits(controls.Red);
            values[4] = BitConverter.SingleToUInt32Bits(controls.Green); values[5] = BitConverter.SingleToUInt32Bits(controls.Blue);
            values[6] = BitConverter.SingleToUInt32Bits(controls.Contrast); values[7] = BitConverter.SingleToUInt32Bits(controls.Highlights);
            values[8] = BitConverter.SingleToUInt32Bits(controls.Shadows); values[9] = BitConverter.SingleToUInt32Bits(controls.Saturation);
            values[10] = BitConverter.SingleToUInt32Bits(controls.Vibrance);
            _vk.CmdPushConstants(_command, _layout, ShaderStageFlags.ComputeBit, 0, 44, values);
            uint groups = (values[0] + 63) / 64, groupsX = Math.Min(groups, _maxGroupsX);
            uint groupsY = (groups + groupsX - 1) / groupsX;
            if (groupsY > _maxGroupsY) throw new NotSupportedException("Image exceeds GPU dispatch limits.");
            _vk.CmdDispatch(_command, groupsX, groupsY, 1);
            var after = Barrier(_target.Buffer, bytes, AccessFlags.ShaderWriteBit, AccessFlags.HostReadBit);
            _vk.CmdPipelineBarrier(_command, PipelineStageFlags.ComputeShaderBit, PipelineStageFlags.HostBit, 0, 0, null, 1, &after, 0, null);
            Check(_vk.EndCommandBuffer(_command));
            var fence = _fence; var command = _command;
            Check(_vk.ResetFences(_device, 1, &fence));
            var submit = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &command };
            Check(_vk.QueueSubmit(_queue, 1, &submit, fence));
            // Cancellation discards output after the fence; submitted work must finish before reuse/free.
            Check(_vk.WaitForFences(_device, 1, &fence, true, 5_000_000_000));
            cancellation.ThrowIfCancellationRequested();
            var output = new ReadOnlySpan<float>((void*)_target.Address, input.Length);
            for (int i = 0; i < output.Length; i++)
                if (!float.IsFinite(output[i])) throw new ArithmeticException("Image processing exceeded the finite float range.");
            var result = FloatImageBuffer.FromLinearRgba32(source.Width, source.Height, output);
            try { cancellation.ThrowIfCancellationRequested(); return result; }
            catch { result.Dispose(); throw; }
        }
    }

    private void EnsureBuffers(ulong bytes)
    {
        if (bytes <= _capacity) return;
        var source = Buffer(bytes);
        (VkBuffer Buffer, DeviceMemory Memory, nint Address) target = default;
        try { target = Buffer(bytes); }
        catch { Release(source); throw; }
        Release(_source); Release(_target);
        _source = source; _target = target; _capacity = bytes; _uploadedSource = null;
        BufferAllocationCount += 2;
        var infos = stackalloc DescriptorBufferInfo[2];
        infos[0] = new DescriptorBufferInfo { Buffer = source.Buffer, Range = bytes };
        infos[1] = new DescriptorBufferInfo { Buffer = target.Buffer, Range = bytes };
        var writes = stackalloc WriteDescriptorSet[2];
        for (uint i = 0; i < 2; i++) writes[i] = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = _set, DstBinding = i, DescriptorCount = 1, DescriptorType = DescriptorType.StorageBuffer, PBufferInfo = &infos[i] };
        _vk.UpdateDescriptorSets(_device, 2, writes, 0, null);
    }

    private static byte[] ReadShader()
    {
        using var source = typeof(VulkanFloatProcessor).Assembly.GetManifestResourceStream("PhotoLab.Rendering.Shaders.develop.spv")
            ?? throw new InvalidOperationException("Embedded GPU shader is missing.");
        using var memory = new MemoryStream(); source.CopyTo(memory); return memory.ToArray();
    }

    private (VkBuffer Buffer, DeviceMemory Memory, nint Address) Buffer(ulong bytes)
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
            void* address;
            Check(_vk.MapMemory(_device, memory, 0, bytes, 0, &address));
            return (buffer, memory, (nint)address);
        }
        catch { Release((buffer, memory, 0)); throw; }
    }

    private static BufferMemoryBarrier Barrier(VkBuffer buffer, ulong bytes, AccessFlags source, AccessFlags target)
        => new() { SType = StructureType.BufferMemoryBarrier, SrcAccessMask = source, DstAccessMask = target,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored, DstQueueFamilyIndex = Vk.QueueFamilyIgnored, Buffer = buffer, Size = bytes };

    private void Release((VkBuffer Buffer, DeviceMemory Memory, nint Address) allocation)
    {
        if (allocation.Address != 0) _vk.UnmapMemory(_device, allocation.Memory);
        if (allocation.Buffer.Handle != 0) _vk.DestroyBuffer(_device, allocation.Buffer, null);
        if (allocation.Memory.Handle != 0) _vk.FreeMemory(_device, allocation.Memory, null);
    }

    private void Check(Result result)
    {
        if (result != Result.Success)
        {
            _faulted = true;
            throw new InvalidOperationException("Vulkan: " + result);
        }
    }

    public void Dispose()
    {
        lock (_gate) DisposeCore();
    }

    private void DisposeCore()
    {
        if (_disposed) return;
        _disposed = true;
        if (_device.Handle != 0)
        {
            _vk.DeviceWaitIdle(_device);
            Release(_source); Release(_target); _uploadedSource = null;
            if (_fence.Handle != 0) _vk.DestroyFence(_device, _fence, null);
            if (_descriptors.Handle != 0) _vk.DestroyDescriptorPool(_device, _descriptors, null);
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
