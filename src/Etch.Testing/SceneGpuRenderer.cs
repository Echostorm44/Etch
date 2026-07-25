using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Etch.Geometry;
using Etch.Gpu;
using Etch.Gpu.Compositor;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;
using Etch.Gpu.Validation;
using Etch.Scene;

#pragma warning disable ET0201 // Raw handle types used in private GPU implementation methods — not a public API surface

namespace Etch.Testing;

public static class SceneGpuRenderer
{
    private const string WgslShader = """
        struct PerDraw {
            rect_min: vec2<f32>,
            rect_max: vec2<f32>,
            color: vec4<f32>,
            shape_type: u32,
        };

        @group(0) @binding(0)
        var<uniform> per_draw: PerDraw;

        @vertex
        fn vs(@builtin(vertex_index) idx: u32) -> @builtin(position) vec4<f32> {
            var p = array<vec2<f32>, 3>(
                vec2(-1.0, -1.0),
                vec2( 3.0, -1.0),
                vec2(-1.0,  3.0));
            return vec4<f32>(p[idx], 0.0, 1.0);
        }

        @fragment
        fn fs(@builtin(position) pos: vec4<f32>) -> @location(0) vec4<f32> {
            if (per_draw.shape_type == 0u) {
                if (pos.x < per_draw.rect_min.x || pos.x > per_draw.rect_max.x ||
                    pos.y < per_draw.rect_min.y || pos.y > per_draw.rect_max.y) {
                    discard;
                }
            } else {
                let cx = per_draw.rect_min.x;
                let cy = per_draw.rect_min.y;
                let r = per_draw.rect_max.x;
                let dx = pos.x - cx;
                let dy = pos.y - cy;
                if (dx * dx + dy * dy > r * r) {
                    discard;
                }
            }
            return per_draw.color;
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct PerDrawData
    {
        public float RectMinX, RectMinY;
        public float RectMaxX, RectMaxY;
        public float R, G, B, A;
        public uint ShapeType;
        private uint _pad0, _pad1;
        public const uint SizeInBytes = 48;
    }

    public static byte[] RenderToRgba8(SceneBuffer scene, int width, int height)
        => RenderToRgba8(scene, width, height, BackendType.Undefined);

    public static byte[] RenderToRgba8(SceneBuffer scene, int width, int height, BackendType backendType)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (width <= 0 || height <= 0)
            Etch.Panic.Invariant(Etch.PanicCodes.InvalidSurfaceSize, "Render dimensions must be positive");

        using var instance = Instance.Create();
        var (adapterStatus, adapter) = AsyncRequest.RequestAdapterSync(
            instance, compatibleSurface: null, preference: PowerPreference.HighPerformance, backendType: backendType);

        if (adapterStatus != RequestAdapterStatus.Success || adapter.IsInvalid)
            Etch.Panic.Invariant(Etch.PanicCodes.GpuAdapterUnavailable,
                $"GPU path failed: no adapter for backend {backendType}.");

        DeviceDescriptor deviceDesc = default;
        unsafe
        {
            ValidationBridge.ConfigureDeviceDescriptor(&deviceDesc);
            var (deviceStatus, device) = AsyncRequest.RequestDeviceSync(instance, adapter, &deviceDesc);
            adapter.Dispose();
            if (deviceStatus != RequestDeviceStatus.Success || device.IsInvalid)
                Etch.Panic.Invariant(Etch.PanicCodes.GpuDeviceCreationFailed, "GPU path failed: could not create device.");

            try
            {
                using var compositor = new GpuCompositor(device);
                return compositor.RenderToRgba8(scene, width, height);
            }
            finally { device.Dispose(); }
        }
    }

    private static unsafe byte[] RenderWithDevice(Device device, SceneBuffer scene, int width, int height)
    {
        var result = new byte[width * height * 4];

        using var texture = device.CreateTexture(new TextureDescriptor
        {
            Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
            Format = TextureFormat.Bgra8UnormSrgb,
            Usage = (ulong)(TextureUsage.RenderAttachment | TextureUsage.CopySrc),
            MipLevelCount = 1, SampleCount = 1,
        });
        using var texView = texture.CreateView();

        uint bytesPerRow = (uint)(width * 4);
        uint alignedRow = (bytesPerRow + 255u) & ~255u;
        using var readback = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.MapRead | BufferUsage.CopyDst),
            Size = alignedRow * (uint)height,
        });

        using var ubo = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.Uniform | BufferUsage.CopyDst),
            Size = (ulong)PerDrawData.SizeInBytes,
        });

        using var shader = device.CreateShaderModuleWgsl(WgslShader, "SolidFill Shader");
        using var pipeline = BuildPipeline(device, shader, ubo, out var bgLayout, out var bg);

        var drawCommands = BuildDrawCommands(scene, width, height);

        using var encoder = device.CreateCommandEncoder();
        var colorAttachment = new RenderPassColorAttachment
        {
            View = (nint)texView.Handle,
            DepthSlice = 0xFFFFFFFFu,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = default,
        };
        var passDesc = new RenderPassDescriptor
        {
            ColorAttachmentCount = (UIntPtr)1,
            ColorAttachments = (nint)(&colorAttachment),
        };

        using var pass = encoder.BeginRenderPass(passDesc);
        pass.SetPipeline(pipeline);
        pass.SetBindGroup(0, bg);

        for (int d = 0; d < drawCommands.Count; d++)
        {
            var data = drawCommands[d];
            var span = new ReadOnlySpan<byte>(&data, (int)PerDrawData.SizeInBytes);
            device.Queue.WriteBuffer(ubo, 0, span);
            pass.Draw(3);
        }
        pass.End();

        if (drawCommands.Count > 0)
        {
            var si = new WGPUTexelCopyTextureInfo { Texture = texture.Handle, MipLevel = 0, Aspect = 1 };
            var di = new WGPUTexelCopyBufferLayout { BytesPerRow = alignedRow, RowsPerImage = (uint)height };
            var cs = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 };
            WebGPU.CommandEncoderCopyTextureToBuffer(encoder.Handle, (nint)(&si), (nint)(&di), (nint)(&cs));
        }

        var cb = encoder.Finish();
        var cmds = new ReadOnlySpan<CommandBuffer>(in cb);
        device.Queue.Submit(cmds);
        cb.Dispose();

        if (drawCommands.Count > 0)
        {
            readback.MapSync(device, MapMode.Read, 0, alignedRow * (uint)height);
            var mapped = readback.GetConstMappedRange(0, alignedRow * (uint)height);
            if (!mapped.IsEmpty)
            {
                fixed (byte* dst = result)
                {
                    for (int y = 0; y < height; y++)
                    {
                        var srcPtr = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(mapped)) + y * alignedRow;
                        System.Buffer.MemoryCopy(srcPtr, dst + y * bytesPerRow, bytesPerRow, bytesPerRow);
                    }
                }
            }
            readback.Unmap();
        }

        device.Poll(false);
        bg.Dispose();
        bgLayout.Dispose();

        return result;
    }

    private static List<PerDrawData> BuildDrawCommands(SceneBuffer scene, int width, int height)
    {
        var commands = new List<PerDrawData>();
        Affine cur = Affine.Identity;

        for (int i = 0; i < scene.Commands.Length; i++)
        {
            ref readonly var cmd = ref scene.Commands[i];
            switch (cmd.Op)
            {
            case SceneOpcode.SetTransform:
                cur = scene.GetTransform(cmd.SetTransform.TransformId);
                break;
            case SceneOpcode.FillRect:
                {
                    var paint = scene.GetPaint(cmd.FillRect.PaintId);
                    if (paint.Kind != PaintKind.Solid) continue;
                    var rect = scene.GetRect(cmd.FillRect.RectId);
                    var xf = cur * scene.GetTransform(cmd.FillRect.TransformId);
                    commands.Add(BuildRectData(rect, xf, paint.Color));
                    break;
                }
            case SceneOpcode.FillPath:
                {
                    var paint = scene.GetPaint(cmd.FillPath.PaintId);
                    if (paint.Kind != PaintKind.Solid) continue;
                    if (scene.TryGetPath(cmd.FillPath.PathId, out var pathData))
                    {
                        var xf = cur * scene.GetTransform(cmd.FillPath.TransformId);
                        var (isCircle, center, radius) = TryDetectCircle(pathData.Path);
                        if (isCircle)
                            commands.Add(BuildCircleData(center, (float)radius, xf, paint.Color));
                        else
                        {
                            var aabb = pathData.Path.Aabb();
                            if (!aabb.IsEmpty)
                                commands.Add(BuildRectData(
                                    new Rect(aabb.MinX, aabb.MinY, aabb.MaxX, aabb.MaxY), xf, paint.Color));
                        }
                    }
                    break;
                }
            }
        }
        return commands;
    }

    private static PerDrawData BuildRectData(Rect rect, Affine transform, uint argb)
    {
        var tl = transform.Transform(new Point(rect.MinX, rect.MinY));
        var br = transform.Transform(new Point(rect.MaxX, rect.MaxY));
        float a = ((argb >> 24) & 0xFF) / 255.0f;
        float r = ((argb >> 16) & 0xFF) / 255.0f;
        float g = ((argb >> 8) & 0xFF) / 255.0f;
        float b = (argb & 0xFF) / 255.0f;
        return new PerDrawData
        {
            RectMinX = (float)Math.Min(tl.X, br.X), RectMinY = (float)Math.Min(tl.Y, br.Y),
            RectMaxX = (float)Math.Max(tl.X, br.X), RectMaxY = (float)Math.Max(tl.Y, br.Y),
            R = r, G = g, B = b, A = a, ShapeType = 0,
        };
    }

    private static PerDrawData BuildCircleData(Point center, float radius, Affine transform, uint argb)
    {
        var tc = transform.Transform(center);
        float a = ((argb >> 24) & 0xFF) / 255.0f;
        float cr = ((argb >> 16) & 0xFF) / 255.0f;
        float cg = ((argb >> 8) & 0xFF) / 255.0f;
        float cb = (argb & 0xFF) / 255.0f;
        return new PerDrawData
        {
            RectMinX = (float)tc.X, RectMinY = (float)tc.Y,
            RectMaxX = radius, RectMaxY = 0,
            R = cr, G = cg, B = cb, A = a, ShapeType = 1,
        };
    }

    private static (bool, Point, double) TryDetectCircle(BezPath path)
    {
        var enumerator = path.Iterate();
        int cubicCount = 0;
        double cx = 0, cy = 0, rx = 0, ry = 0;
        bool hasMove = false;
        while (enumerator.MoveNext())
        {
            var seg = enumerator.Current;
            switch (seg.Verb)
            {
            case PathVerb.MoveTo: rx = seg.End.X; ry = seg.End.Y; hasMove = true; break;
            case PathVerb.CubicTo: cubicCount++; cx += seg.Control0.X + seg.Control1.X + seg.End.X; cy += seg.Control0.Y + seg.Control1.Y + seg.End.Y; break;
            }
        }
        if (cubicCount == 4 && hasMove)
        {
            double avgX = cx / (cubicCount * 3), avgY = cy / (cubicCount * 3);
            double dx = rx - avgX, dy = ry - avgY;
            double radius = Math.Sqrt(dx * dx + dy * dy);
            return (true, new Point(avgX, avgY), radius);
        }
        return (false, default, 0);
    }

    private static unsafe RenderPipeline BuildPipeline(Device device, ShaderModule shader, Gpu.Buffer ubo, out BindGroupLayout bgLayout, out BindGroup bg)
    {
        var bglEntry = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = (ulong)ShaderStage.Fragment,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = (ulong)PerDrawData.SizeInBytes },
        };
        var bglDesc = new BindGroupLayoutDescriptor { EntryCount = (UIntPtr)1, Entries = (nint)(&bglEntry) };
        bgLayout = device.CreateBindGroupLayout(bglDesc);

        var bglHandle = bgLayout.Handle;
        var plDesc = new PipelineLayoutDescriptor
        {
            BindGroupLayoutCount = (UIntPtr)1,
            BindGroupLayouts = (nint)(&bglHandle),
        };
        using var pl = device.CreatePipelineLayout(plDesc);

        Span<byte> vsName = stackalloc byte[3];
        Span<byte> fsName = stackalloc byte[3];
        int vsLen = Encoding.UTF8.GetBytes("vs", vsName);
        int fsLen = Encoding.UTF8.GetBytes("fs", fsName);

        RenderPipeline pipeline;
        fixed (byte* vsPtr = vsName)
        fixed (byte* fsPtr = fsName)
        {
            var vertex = new VertexState
            {
                Module = shader.Handle,
                EntryPoint = new StringView { Data = (nint)vsPtr, Length = (UIntPtr)vsLen },
            };
            var colorTarget = new ColorTargetState
            {
                Format = TextureFormat.Bgra8UnormSrgb,
                WriteMask = (ulong)ColorWriteMask.All,
            };
            var fragment = new FragmentState
            {
                Module = shader.Handle,
                EntryPoint = new StringView { Data = (nint)fsPtr, Length = (UIntPtr)fsLen },
                TargetCount = (UIntPtr)1,
                Targets = (nint)(&colorTarget),
            };
            var desc = new RenderPipelineDescriptor
            {
                Layout = pl.Handle,
                Vertex = vertex,
                Fragment = (nint)(&fragment),
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList },
                Multisample = new MultisampleState { Count = 1, Mask = ~0u },
            };
            pipeline = device.CreateRenderPipeline(desc);
        }

        var bge = new BindGroupEntry
        {
            Binding = 0,
            Buffer = (nint)ubo.Handle,
            Size = (ulong)PerDrawData.SizeInBytes,
        };
        var bgDesc = new BindGroupDescriptor
        {
            Layout = bgLayout.Handle,
            EntryCount = (UIntPtr)1,
            Entries = (nint)(&bge),
        };
        bg = device.CreateBindGroup(bgDesc);
        return pipeline;
    }

    internal sealed class GpuRenderCache : IDisposable
    {
        private readonly int _width, _height;
        private readonly Instance _instance;
        private readonly Adapter _adapter;
        private readonly Device _device;
        private readonly ShaderModule _shader;
        private readonly RenderPipeline _pipeline;
        private readonly BindGroupLayout _bgLayout;
        private BindGroup _bg;
        private readonly Texture _texture;
        private readonly TextureView _texView;
        private readonly Gpu.Buffer _readback;
        private readonly Gpu.Buffer _ubo;
        private readonly uint _bytesPerRow, _alignedRow;
        private readonly List<PerDrawData> _drawCommands = new(256);
        private bool _disposed;

        internal GpuRenderCache(int width, int height)
        {
            _width = width; _height = height;
            _instance = Instance.Create();
            var (s, a) = AsyncRequest.RequestAdapterSync(_instance);
            if (s != RequestAdapterStatus.Success || a.IsInvalid)
                Etch.Panic.Invariant(Etch.PanicCodes.GpuAdapterUnavailable, "GPU adapter unavailable");
            _adapter = a;

            DeviceDescriptor dd = default;
            unsafe { ValidationBridge.ConfigureDeviceDescriptor(&dd); var (ds, d) = AsyncRequest.RequestDeviceSync(_instance, _adapter, &dd);
                if (ds != RequestDeviceStatus.Success || d.IsInvalid) Etch.Panic.Invariant(Etch.PanicCodes.GpuDeviceCreationFailed, "Device creation failed");
                _device = d; }

            _shader = _device.CreateShaderModuleWgsl(WgslShader, "SolidFill Shader");

            _texture = _device.CreateTexture(new TextureDescriptor
            {
                Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
                Format = TextureFormat.Bgra8UnormSrgb,
                Usage = (ulong)(TextureUsage.RenderAttachment | TextureUsage.CopySrc),
                MipLevelCount = 1, SampleCount = 1,
            });
            _texView = _texture.CreateView();

            _bytesPerRow = (uint)(width * 4);
            _alignedRow = (_bytesPerRow + 255u) & ~255u;
            _readback = _device.CreateBuffer(new BufferDescriptor
            {
                Usage = (ulong)(BufferUsage.MapRead | BufferUsage.CopyDst),
                Size = _alignedRow * (uint)height,
            });
            _ubo = _device.CreateBuffer(new BufferDescriptor
            {
                Usage = (ulong)(BufferUsage.Uniform | BufferUsage.CopyDst),
                Size = (ulong)PerDrawData.SizeInBytes,
            });

            _pipeline = BuildPipeline(_device, _shader, _ubo, out _bgLayout, out _bg);
        }

        public byte[] Render(SceneBuffer scene)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _drawCommands.Clear();
            BuildDrawCommandsInPlace(scene);

            var result = new byte[_width * _height * 4];

            unsafe
            {
                using var encoder = _device.CreateCommandEncoder();
                var ca = new RenderPassColorAttachment
                {
                    View = (nint)_texView.Handle, DepthSlice = 0xFFFFFFFFu,
                    LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store, ClearValue = default,
                };
                var passDesc = new RenderPassDescriptor { ColorAttachmentCount = (UIntPtr)1, ColorAttachments = (nint)(&ca) };
                using var pass = encoder.BeginRenderPass(passDesc);
                pass.SetPipeline(_pipeline);
                pass.SetBindGroup(0, _bg);

                for (int d = 0; d < _drawCommands.Count; d++)
                {
                    var data = _drawCommands[d];
                    _device.Queue.WriteBuffer(_ubo, 0, new ReadOnlySpan<byte>(&data, (int)PerDrawData.SizeInBytes));
                    pass.Draw(3);
                }
                pass.End();

                if (_drawCommands.Count > 0)
                {
                    var si = new WGPUTexelCopyTextureInfo { Texture = _texture.Handle, MipLevel = 0, Aspect = 1 };
                    var di = new WGPUTexelCopyBufferLayout { BytesPerRow = _alignedRow, RowsPerImage = (uint)_height };
                    var cs = new Extent3D { Width = (uint)_width, Height = (uint)_height, DepthOrArrayLayers = 1 };
                    WebGPU.CommandEncoderCopyTextureToBuffer(encoder.Handle, (nint)(&si), (nint)(&di), (nint)(&cs));
                }

                var cb = encoder.Finish();
                var cmds = new ReadOnlySpan<CommandBuffer>(in cb);
                _device.Queue.Submit(cmds);
                cb.Dispose();

                if (_drawCommands.Count > 0)
                {
                    _readback.MapSync(_device, MapMode.Read, 0, _alignedRow * (uint)_height);
                    var mapped = _readback.GetConstMappedRange(0, _alignedRow * (uint)_height);
                    if (!mapped.IsEmpty)
                    {
                        fixed (byte* dst = result)
                        {
                            for (int y = 0; y < _height; y++)
                            {
                                System.Buffer.MemoryCopy(
                                    (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(mapped)) + y * _alignedRow,
                                    dst + y * _bytesPerRow, _bytesPerRow, _bytesPerRow);
                            }
                        }
                    }
                    _readback.Unmap();
                }
                _device.Poll(false);
            }
            return result;
        }

        private void BuildDrawCommandsInPlace(SceneBuffer scene)
        {
            Affine cur = Affine.Identity;
            for (int i = 0; i < scene.Commands.Length; i++)
            {
                ref readonly var cmd = ref scene.Commands[i];
                switch (cmd.Op)
                {
                case SceneOpcode.SetTransform: cur = scene.GetTransform(cmd.SetTransform.TransformId); break;
                case SceneOpcode.FillRect:
                    { var p = scene.GetPaint(cmd.FillRect.PaintId); if (p.Kind != PaintKind.Solid) break;
                        _drawCommands.Add(BuildRectData(scene.GetRect(cmd.FillRect.RectId), cur * scene.GetTransform(cmd.FillRect.TransformId), p.Color)); break; }
                case SceneOpcode.FillPath:
                    { var p = scene.GetPaint(cmd.FillPath.PaintId); if (p.Kind != PaintKind.Solid) break;
                        if (scene.TryGetPath(cmd.FillPath.PathId, out var pd))
                        { var xf = cur * scene.GetTransform(cmd.FillPath.TransformId);
                            var (ic, c, r) = TryDetectCircle(pd.Path);
                            if (ic) _drawCommands.Add(BuildCircleData(c, (float)r, xf, p.Color));
                            else { var aabb = pd.Path.Aabb(); if (!aabb.IsEmpty) _drawCommands.Add(BuildRectData(new Rect(aabb.MinX, aabb.MinY, aabb.MaxX, aabb.MaxY), xf, p.Color)); } } break; }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            _bg.Dispose(); _bgLayout.Dispose(); _pipeline.Dispose(); _shader.Dispose();
            _ubo.Dispose(); _readback.Dispose(); _texView.Dispose(); _texture.Dispose();
            _device.Dispose(); _adapter.Dispose(); _instance.Dispose();
        }
    }
}
