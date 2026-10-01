using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Etch.BindingsGen;

internal sealed class BindingsGenerator
{
    private readonly string _outputDir;

    private static readonly Dictionary<string, FunctionBinding> Bindings = new()
    {
        ["wgpuCreateInstance"] = new("InstanceHandle", "CreateInstance", new[] { ("System.IntPtr", "descriptor") }),
        ["wgpuInstanceReference"] = new("void", "InstanceReference", new[] { ("InstanceHandle", "instance") }),
        ["wgpuInstanceRelease"] = new("void", "InstanceRelease", new[] { ("InstanceHandle", "instance") }),
        ["wgpuInstanceRequestAdapter"] = new("void", "InstanceRequestAdapter", new[] { ("InstanceHandle", "instance"), ("System.IntPtr", "options"), ("WGPURequestAdapterCallbackInfo", "callbackInfo") }),
        ["wgpuAdapterReference"] = new("void", "AdapterReference", new[] { ("AdapterHandle", "adapter") }),
        ["wgpuAdapterRelease"] = new("void", "AdapterRelease", new[] { ("AdapterHandle", "adapter") }),
        ["wgpuAdapterRequestDevice"] = new("void", "AdapterRequestDevice", new[] { ("AdapterHandle", "adapter"), ("System.IntPtr", "descriptor"), ("WGPURequestDeviceCallbackInfo", "callbackInfo") }),
        ["wgpuDeviceReference"] = new("void", "DeviceReference", new[] { ("DeviceHandle", "device") }),
        ["wgpuDeviceRelease"] = new("void", "DeviceRelease", new[] { ("DeviceHandle", "device") }),
        ["wgpuDeviceCreateShaderModule"] = new("ShaderModuleHandle", "DeviceCreateShaderModule", new[] { ("DeviceHandle", "device"), ("System.IntPtr", "descriptor") }),
        ["wgpuDeviceCreateBuffer"] = new("BufferHandle", "DeviceCreateBuffer", new[] { ("DeviceHandle", "device"), ("System.IntPtr", "descriptor") }),
        ["wgpuDeviceCreateBindGroupLayout"] = new("BindGroupLayoutHandle", "DeviceCreateBindGroupLayout", new[] { ("DeviceHandle", "device"), ("System.IntPtr", "descriptor") }),
        ["wgpuDeviceCreatePipelineLayout"] = new("PipelineLayoutHandle", "DeviceCreatePipelineLayout", new[] { ("DeviceHandle", "device"), ("System.IntPtr", "descriptor") }),
        ["wgpuDeviceCreateRenderPipeline"] = new("RenderPipelineHandle", "DeviceCreateRenderPipeline", new[] { ("DeviceHandle", "device"), ("System.IntPtr", "descriptor") }),
        ["wgpuDeviceCreateCommandEncoder"] = new("CommandEncoderHandle", "DeviceCreateCommandEncoder", new[] { ("DeviceHandle", "device"), ("System.IntPtr", "descriptor") }),
        ["wgpuDeviceGetQueue"] = new("QueueHandle", "DeviceGetQueue", new[] { ("DeviceHandle", "device") }),
        ["wgpuAdapterGetLimits"] = new("uint", "AdapterGetLimits", new[] { ("AdapterHandle", "adapter"), ("System.IntPtr", "limits") }, "Returns WGPUStatus (1 = success). `limits` points at a WGPULimits, optionally chained with\n// WGPUNativeLimits, which is filled in."),
        ["wgpuAdapterGetInfo"] = new("uint", "AdapterGetInfo", new[] { ("AdapterHandle", "adapter"), ("System.IntPtr", "info") }, "Returns WGPUStatus (1 = success). The string views in `info` belong to wgpu until\n// AdapterInfoFreeMembers is called on the same struct."),
        ["wgpuAdapterInfoFreeMembers"] = new("void", "AdapterInfoFreeMembers", new[] { ("WGPUAdapterInfo", "info") }),
        ["wgpuSurfaceGetCapabilities"] = new("uint", "SurfaceGetCapabilities", new[] { ("SurfaceHandle", "surface"), ("AdapterHandle", "adapter"), ("System.IntPtr", "capabilities") }, "Returns WGPUStatus (1 = success, i.e. the adapter can present to the surface)."),
        ["wgpuSurfaceCapabilitiesFreeMembers"] = new("void", "SurfaceCapabilitiesFreeMembers", new[] { ("WGPUSurfaceCapabilities", "capabilities") }),
        ["wgpuInstanceEnumerateAdapters"] = new("System.UIntPtr", "InstanceEnumerateAdapters", new[] { ("InstanceHandle", "instance"), ("System.IntPtr", "options"), ("System.IntPtr", "adapters") }, "wgpu.h: with `adapters` null, returns the count; otherwise fills `adapters` (caller-sized) and\n// returns how many were written. Each written adapter is owned by the caller."),
        ["wgpuDevicePoll"] = new("uint", "DevicePoll", new[] { ("DeviceHandle", "device"), ("uint", "wait"), ("System.IntPtr", "submissionIndex") }),
        ["wgpuDevicePushErrorScope"] = new("void", "DevicePushErrorScope", new[] { ("DeviceHandle", "device"), ("WGPUErrorFilter", "filter") }),
        ["wgpuDevicePopErrorScope"] = new("UIntPtr", "DevicePopErrorScope", new[] { ("DeviceHandle", "device"), ("WGPUPopErrorScopeCallbackInfo", "callbackInfo") }),
        ["wgpuInstanceProcessEvents"] = new("void", "InstanceProcessEvents", new[] { ("InstanceHandle", "instance") }),
        ["wgpuCommandEncoderCopyTextureToTexture"] = new("void", "CommandEncoderCopyTextureToTexture", new[] { ("CommandEncoderHandle", "encoder"), ("System.IntPtr", "source"), ("System.IntPtr", "destination"), ("System.IntPtr", "copySize") }),
        ["wgpuSetLogLevel"] = new("void", "SetLogLevel", new[] { ("int", "level") }),
        ["wgpuSetLogCallback"] = new("void", "SetLogCallback", new[] { ("System.IntPtr", "callback"), ("System.IntPtr", "userdata") }),
        ["wgpuQueueSubmit"] = new("void", "QueueSubmit", new[] { ("QueueHandle", "queue"), ("System.UIntPtr", "commandCount"), ("System.IntPtr", "commands") }),
        ["wgpuQueueWriteBuffer"] = new("void", "QueueWriteBuffer", new[] { ("QueueHandle", "queue"), ("BufferHandle", "buffer"), ("ulong", "bufferOffset"), ("System.IntPtr", "data"), ("System.UIntPtr", "size") }),
        ["wgpuQueueWriteTexture"] = new("void", "QueueWriteTexture", new[] { ("QueueHandle", "queue"), ("System.IntPtr", "destination"), ("System.IntPtr", "data"), ("System.UIntPtr", "dataSize"), ("System.IntPtr", "dataLayout"), ("System.IntPtr", "writeSize") }),
        ["wgpuBufferUnmap"] = new("void", "BufferUnmap", new[] { ("BufferHandle", "buffer") }),
        ["wgpuCommandEncoderFinish"] = new("CommandBufferHandle", "CommandEncoderFinish", new[] { ("CommandEncoderHandle", "encoder"), ("System.IntPtr", "descriptor") }),
        ["wgpuCommandEncoderBeginRenderPass"] = new("RenderPassEncoderHandle", "CommandEncoderBeginRenderPass", new[] { ("CommandEncoderHandle", "encoder"), ("System.IntPtr", "descriptor") }),
        ["wgpuRenderPassEncoderDraw"] = new("void", "RenderPassEncoderDraw", new[] { ("RenderPassEncoderHandle", "encoder"), ("uint", "vertexCount"), ("uint", "instanceCount"), ("uint", "firstVertex"), ("uint", "firstInstance") }),
        ["wgpuRenderPassEncoderDrawIndexed"] = new("void", "RenderPassEncoderDrawIndexed", new[] { ("RenderPassEncoderHandle", "encoder"), ("uint", "indexCount"), ("uint", "instanceCount"), ("uint", "firstIndex"), ("int", "baseVertex"), ("uint", "firstInstance") }),
        ["wgpuRenderPassEncoderEnd"] = new("void", "RenderPassEncoderEnd", new[] { ("RenderPassEncoderHandle", "encoder") }),
        ["wgpuRenderPassEncoderSetPipeline"] = new("void", "RenderPassEncoderSetPipeline", new[] { ("RenderPassEncoderHandle", "encoder"), ("RenderPipelineHandle", "pipeline") }),
        ["wgpuRenderPassEncoderSetVertexBuffer"] = new("void", "RenderPassEncoderSetVertexBuffer", new[] { ("RenderPassEncoderHandle", "encoder"), ("uint", "slot"), ("BufferHandle", "buffer"), ("ulong", "offset"), ("ulong", "size") }),
        ["wgpuRenderPassEncoderSetIndexBuffer"] = new("void", "RenderPassEncoderSetIndexBuffer", new[] { ("RenderPassEncoderHandle", "encoder"), ("BufferHandle", "buffer"), ("uint", "format"), ("ulong", "offset"), ("ulong", "size") }),
        ["wgpuRenderPassEncoderSetBindGroup"] = new("void", "RenderPassEncoderSetBindGroup", new[] { ("RenderPassEncoderHandle", "encoder"), ("uint", "groupIndex"), ("BindGroupHandle", "group"), ("System.UIntPtr", "dynamicOffsetCount"), ("System.IntPtr", "dynamicOffsets") }),
        ["wgpuShaderModuleReference"] = new("void", "ShaderModuleReference", new[] { ("ShaderModuleHandle", "module") }),
        ["wgpuShaderModuleRelease"] = new("void", "ShaderModuleRelease", new[] { ("ShaderModuleHandle", "module") }),
        ["wgpuRenderPipelineReference"] = new("void", "RenderPipelineReference", new[] { ("RenderPipelineHandle", "pipeline") }),
        ["wgpuRenderPipelineRelease"] = new("void", "RenderPipelineRelease", new[] { ("RenderPipelineHandle", "pipeline") }),
        ["wgpuBufferReference"] = new("void", "BufferReference", new[] { ("BufferHandle", "buffer") }),
        ["wgpuBufferRelease"] = new("void", "BufferRelease", new[] { ("BufferHandle", "buffer") }),
        ["wgpuBindGroupLayoutReference"] = new("void", "BindGroupLayoutReference", new[] { ("BindGroupLayoutHandle", "bindGroupLayout") }),
        ["wgpuBindGroupLayoutRelease"] = new("void", "BindGroupLayoutRelease", new[] { ("BindGroupLayoutHandle", "bindGroupLayout") }),
        ["wgpuPipelineLayoutReference"] = new("void", "PipelineLayoutReference", new[] { ("PipelineLayoutHandle", "pipelineLayout") }),
        ["wgpuPipelineLayoutRelease"] = new("void", "PipelineLayoutRelease", new[] { ("PipelineLayoutHandle", "pipelineLayout") }),
        ["wgpuCommandBufferReference"] = new("void", "CommandBufferReference", new[] { ("CommandBufferHandle", "commandBuffer") }),
        ["wgpuCommandBufferRelease"] = new("void", "CommandBufferRelease", new[] { ("CommandBufferHandle", "commandBuffer") }),
        ["wgpuQueueReference"] = new("void", "QueueReference", new[] { ("QueueHandle", "queue") }),
        ["wgpuQueueRelease"] = new("void", "QueueRelease", new[] { ("QueueHandle", "queue") }),
        ["wgpuRenderPassEncoderReference"] = new("void", "RenderPassEncoderReference", new[] { ("RenderPassEncoderHandle", "renderPass") }),
        ["wgpuRenderPassEncoderRelease"] = new("void", "RenderPassEncoderRelease", new[] { ("RenderPassEncoderHandle", "renderPass") }),
        ["wgpuGetVersion"] = new("uint", "GetVersion", Array.Empty<(string, string)>()),
        ["wgpuInstanceCreateSurface"] = new("SurfaceHandle", "InstanceCreateSurface", new[] { ("InstanceHandle", "instance"), ("System.IntPtr", "descriptor") }),
        ["wgpuSurfaceReference"] = new("void", "SurfaceReference", new[] { ("SurfaceHandle", "surface") }),
        ["wgpuSurfaceRelease"] = new("void", "SurfaceRelease", new[] { ("SurfaceHandle", "surface") }),
        ["wgpuDeviceCreateTexture"] = new("TextureHandle", "DeviceCreateTexture", new[] { ("DeviceHandle", "device"), ("System.IntPtr", "descriptor") }),
        ["wgpuDeviceCreateSampler"] = new("SamplerHandle", "DeviceCreateSampler", new[] { ("DeviceHandle", "device"), ("System.IntPtr", "descriptor") }),
        ["wgpuDeviceCreateBindGroup"] = new("BindGroupHandle", "DeviceCreateBindGroup", new[] { ("DeviceHandle", "device"), ("System.IntPtr", "descriptor") }),
        ["wgpuTextureReference"] = new("void", "TextureReference", new[] { ("TextureHandle", "texture") }),
        ["wgpuTextureRelease"] = new("void", "TextureRelease", new[] { ("TextureHandle", "texture") }),
        ["wgpuTextureCreateView"] = new("TextureViewHandle", "TextureCreateView", new[] { ("TextureHandle", "texture"), ("System.IntPtr", "descriptor") }),
        ["wgpuTextureViewReference"] = new("void", "TextureViewReference", new[] { ("TextureViewHandle", "textureView") }),
        ["wgpuTextureViewRelease"] = new("void", "TextureViewRelease", new[] { ("TextureViewHandle", "textureView") }),
        ["wgpuSamplerReference"] = new("void", "SamplerReference", new[] { ("SamplerHandle", "sampler") }),
        ["wgpuSamplerRelease"] = new("void", "SamplerRelease", new[] { ("SamplerHandle", "sampler") }),
        ["wgpuBindGroupReference"] = new("void", "BindGroupReference", new[] { ("BindGroupHandle", "bindGroup") }),
        ["wgpuBindGroupRelease"] = new("void", "BindGroupRelease", new[] { ("BindGroupHandle", "bindGroup") }),
        ["wgpuCommandEncoderRelease"] = new("void", "CommandEncoderRelease", new[] { ("CommandEncoderHandle", "encoder") }),
        ["wgpuSurfaceConfigure"] = new("void", "SurfaceConfigure", new[] { ("SurfaceHandle", "surface"), ("System.IntPtr", "config") }),
        ["wgpuSurfaceGetCurrentTexture"] = new("void", "SurfaceGetCurrentTexture", new[] { ("SurfaceHandle", "surface"), ("System.IntPtr", "texture") }),
        ["wgpuSurfacePresent"] = new("void", "SurfacePresent", new[] { ("SurfaceHandle", "surface") }),
        ["wgpuSurfaceUnconfigure"] = new("void", "SurfaceUnconfigure", new[] { ("SurfaceHandle", "surface") }),
        ["wgpuDeviceCreateQuerySet"] = new("QuerySetHandle", "DeviceCreateQuerySet", new[] { ("DeviceHandle", "device"), ("System.IntPtr", "descriptor") }),
        ["wgpuQuerySetDestroy"] = new("void", "QuerySetDestroy", new[] { ("QuerySetHandle", "querySet") }),
        ["wgpuQuerySetRelease"] = new("void", "QuerySetRelease", new[] { ("QuerySetHandle", "querySet") }),
        ["wgpuCommandEncoderWriteTimestamp"] = new("void", "CommandEncoderWriteTimestamp", new[] { ("CommandEncoderHandle", "encoder"), ("QuerySetHandle", "querySet"), ("uint", "queryIndex") }),
        ["wgpuCommandEncoderResolveQuerySet"] = new("void", "CommandEncoderResolveQuerySet", new[] { ("CommandEncoderHandle", "encoder"), ("QuerySetHandle", "querySet"), ("uint", "firstQuery"), ("uint", "queryCount"), ("BufferHandle", "destination"), ("ulong", "destinationOffset") }),
    };

    public BindingsGenerator(string outputDir)
    {
        _outputDir = outputDir;
    }

    public Task GenerateAsync()
    {
        Directory.CreateDirectory(_outputDir);
        GenerateTypesFile();
        GenerateMethodsFile();
        return Task.CompletedTask;
    }

    private void GenerateTypesFile()
    {
        var sb = new StringBuilder();
        sb.AppendLine("// ⚠️ AUTO-GENERATED by ClangSharpPInvokeGenerator - DO NOT EDIT DIRECTLY ⚠️");
        sb.AppendLine("// Handle types remapped from wgpu-native opaque pointers");
        sb.AppendLine();
        sb.AppendLine("namespace Etch.Gpu.Native;");
        sb.AppendLine();

        var handles = new string[] { "InstanceHandle", "AdapterHandle", "DeviceHandle", "ShaderModuleHandle",
            "BufferHandle", "BindGroupLayoutHandle", "PipelineLayoutHandle", "RenderPipelineHandle",
            "CommandEncoderHandle", "CommandBufferHandle", "QueueHandle", "RenderPassEncoderHandle",
            "BindGroupHandle", "SurfaceHandle", "TextureHandle", "TextureViewHandle", "SamplerHandle",
            "QuerySetHandle" };

        foreach (var handle in handles)
        {
            sb.AppendLine($"public readonly struct {handle} : IEquatable<{handle}>");
            sb.AppendLine("{");
            sb.AppendLine("    private readonly nint _handle;");
            sb.AppendLine($"    public {handle}(nint handle) => _handle = handle;");
            sb.AppendLine($"    public static {handle} Invalid => new(0);");
            sb.AppendLine("    public bool IsInvalid => _handle == 0;");
            sb.AppendLine($"    public bool Equals({handle} other) => _handle == other._handle;");
            sb.AppendLine($"    public override bool Equals(object? obj) => obj is {handle} other && Equals(other);");
            sb.AppendLine("    public override int GetHashCode() => _handle.GetHashCode();");
            sb.AppendLine($"    public static bool operator ==({handle} left, {handle} right) => left.Equals(right);");
            sb.AppendLine($"    public static bool operator !=({handle} left, {handle} right) => !left.Equals(right);");
            sb.AppendLine($"    public static implicit operator nint({handle} h) => h._handle;");
            sb.AppendLine($"    public override string ToString() => $\"{handle}(0x{{_handle:X}})\";");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        File.WriteAllText(Path.Combine(_outputDir, "WebGPU.Types.cs"), sb.ToString());
    }


    private void GenerateMethodsFile()
    {
        var sb = new StringBuilder();
        sb.AppendLine("// ⚠️ AUTO-GENERATED by ClangSharpPInvokeGenerator - DO NOT EDIT DIRECTLY ⚠️");
        sb.AppendLine("// This file is generated from webgpu.h and wgpu.h headers in native/wgpu-native/");
        sb.AppendLine("// To regenerate: dotnet run --project tools/gen-wgpu-bindings -- generate");
        sb.AppendLine();
        sb.AppendLine("namespace Etch.Gpu.Native;");
        sb.AppendLine();
        sb.AppendLine("public static partial class WebGPU");
        sb.AppendLine("{");

        foreach (var kvp in Bindings)
        {
            var entryPoint = kvp.Key;
            var binding = kvp.Value;

            if (binding.Comment is not null)
            {
                foreach (var commentLine in binding.Comment.Split('\n'))
                {
                    sb.AppendLine("    " + (commentLine.StartsWith("//", StringComparison.Ordinal) ? commentLine : "// " + commentLine));
                }
            }
            sb.AppendLine("    [System.Runtime.InteropServices.LibraryImport(\"wgpu_native\", EntryPoint = \"" + entryPoint + "\")]");
            sb.AppendLine("    [System.Runtime.InteropServices.UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]");

            var paramList = new List<string>();
            foreach (var (paramType, paramName) in binding.Parameters)
            {
                paramList.Add(paramType + " " + paramName);
            }

            var paramStr = string.Join(", ", paramList);
            sb.AppendLine($"    public static partial {binding.ReturnType} {binding.MethodName}({paramStr});");
            sb.AppendLine();
        }

        sb.AppendLine("}");

        File.WriteAllText(Path.Combine(_outputDir, "WebGPU.Generated.cs"), sb.ToString());
    }

    private sealed record FunctionBinding(string ReturnType, string MethodName, (string Type, string Name)[] Parameters, string? Comment = null);
}