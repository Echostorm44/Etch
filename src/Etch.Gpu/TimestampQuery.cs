using System;
using System.Runtime.InteropServices;
using Etch.Gpu.Native;

namespace Etch.Gpu;

public readonly struct TimestampQuerySet : IDisposable
{
    private readonly QuerySetHandle _handle;
    private readonly Buffer _resolveBuffer;

    internal TimestampQuerySet(QuerySetHandle handle, Buffer resolveBuffer)
    {
        _handle = handle;
        _resolveBuffer = resolveBuffer;
    }

    public bool IsValid => !_handle.IsInvalid;

    public void Dispose()
    {
        if (!_handle.IsInvalid)
        {
            WebGPU.QuerySetDestroy(_handle);
            WebGPU.QuerySetRelease(_handle);
            _resolveBuffer.Dispose();
        }
    }

    public unsafe void WriteTimestamp(CommandEncoder encoder, uint queryIndex)
    {
        WebGPU.CommandEncoderWriteTimestamp(encoder.Handle, _handle, queryIndex);
    }

    public unsafe void Resolve(CommandEncoder encoder, uint firstQuery, uint queryCount)
    {
        WebGPU.CommandEncoderResolveQuerySet(
            encoder.Handle,
            _handle,
            firstQuery,
            queryCount,
            _resolveBuffer.Handle,
            0);
    }
}