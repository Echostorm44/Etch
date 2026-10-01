using System.Threading;

namespace Etch.Gpu;

// ═══════════════════════════════════════════════════════════════════════════
// WARP (D3D12 "Microsoft Basic Render Driver") serialization.
//
// D3D12 devices are per-adapter singletons inside a process, so every wgpu
// device created on WARP shares one ID3D12Device, each with its own command
// queue. WARP's shader JIT is not safe across those queues: executing command
// lists on two queues at once (UMCommandQueue::ExecuteCommandLists ->
// UMContext::CompilePixelPipeline, both JIT-compiling into shared state), or
// destroying a pipeline meanwhile (UMDevice::DestroyShader ->
// JITProcessorsCache::OnDeleteShader, which walks every queue's render context),
// corrupts the JIT state. It crashes with an access violation either in the
// submitting thread (JITBaseVariable::OptimizeCopy) or later on WARP's own
// compile threads (PixelJitProgram::ClassifyVars). D3D12 documents the device
// as free-threaded, so this is a driver bug. One wgpu device used from many
// threads does not hit it (wgpu serializes its queue); several wgpu devices
// used in parallel (parallel tests, one device per window) do.
//
// Once a device is created on a software D3D12 adapter, Etch takes one
// process-wide gate around every call that can submit work or destroy a native
// object (wgpu destroys pipelines transitively, e.g. when the last command
// buffer or encoder referencing a released pipeline goes away, or when a poll
// retires a submission). Hardware adapters never activate it.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Process-wide gate that works around WARP's cross-queue shader-JIT race.</summary>
internal static class WarpSerialization
{
    private static readonly Lock s_gate = new();
    private static volatile bool s_active;

    /// <summary>True once a device on a software D3D12 adapter has been created in this process.</summary>
    public static bool IsActive => s_active;

    /// <summary>Turns the gate on for the rest of the process when <paramref name="adapter"/> is WARP.</summary>
    public static void ActivateFor(AdapterDescription adapter)
    {
        if (adapter.IsSoftware && adapter.Backend == BackendType.D3D12)
        {
            s_active = true;
        }
    }

    /// <summary>Holds the gate until the scope is disposed; a no-op scope while the gate is inactive.</summary>
    public static Scope Enter()
    {
        if (!s_active)
        {
            return default;
        }
        s_gate.Enter();
        return new Scope(held: true);
    }

    public readonly ref struct Scope
    {
        private readonly bool _held;

        public Scope(bool held) => _held = held;

        public void Dispose()
        {
            if (_held)
            {
                s_gate.Exit();
            }
        }
    }
}
