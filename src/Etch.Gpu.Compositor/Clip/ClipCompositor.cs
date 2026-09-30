using System;
using Etch.Gpu.Compositor.Clip;

namespace Etch.Gpu.Compositor;

public sealed class ClipCompositor : IDisposable
{
    private readonly ClipMaskBuffers _buffers;
    private readonly int[] _slotAllocation;
    private readonly int[] _freeList;
    private int _currentDepth;
    private int _freeListCount;
    private int _freeListHead;
    private bool _disposed;

    public ClipCompositor(ClipMaskBuffers buffers)
    {
        _buffers = buffers;
        _slotAllocation = new int[ClipMaskBuffers.MaxClipLevels];
        _freeList = new int[ClipMaskBuffers.MaxClipLevels];
        _currentDepth = 0;
        _freeListCount = 0;
        _freeListHead = 0;
    }

    public int PushClip()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_currentDepth >= ClipMaskBuffers.MaxClipLevels)
        {
            Etch.Panic.Invariant(
                Etch.PanicCodes.GpuClipStackOverflow,
                $"GPU clip stack overflow: depth {_currentDepth} exceeds {ClipMaskBuffers.MaxClipLevels}");
        }

        int slot = AllocateSlot();
        _slotAllocation[_currentDepth] = slot;
        _currentDepth++;
        return slot;
    }

    public void PopClip()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_currentDepth <= 0)
        {
            return;
        }

        _currentDepth--;
        int slot = _slotAllocation[_currentDepth];
        FreeSlot(slot);
    }

    public int CurrentClipIndex => _currentDepth > 0 ? _slotAllocation[_currentDepth - 1] : 0;

    public int CurrentDepth => _currentDepth;

    public ClipMaskBuffers Buffers => _buffers;

    // Freed slots are reused first-freed-first: a popped clip's mask may still be sampled by draws
    // already recorded this frame, so the slot freed longest ago is the safest one to overwrite.
    private int AllocateSlot()
    {
        if (_freeListCount > 0)
        {
            int slot = _freeList[_freeListHead];
            _freeListHead = (_freeListHead + 1) % _freeList.Length;
            _freeListCount--;
            return slot;
        }

        if (_currentDepth + _freeListCount >= ClipMaskBuffers.MaxClipLevels)
        {
            return -1;
        }

        return _currentDepth + _freeListCount;
    }

    private void FreeSlot(int slot)
    {
        if (_freeListCount < _freeList.Length)
        {
            _freeList[(_freeListHead + _freeListCount) % _freeList.Length] = slot;
            _freeListCount++;
        }
    }

    public void Reset()
    {
        _currentDepth = 0;
        _freeListCount = 0;
        _freeListHead = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _buffers.Dispose();
    }
}
