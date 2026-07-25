using System;
using System.Collections.Concurrent;
using System.Threading;
using Etch.Text.Shape;

namespace Etch.Text.Atlas;

file static class FaceIdRegistry
{
    private static int _nextId = 1;
    private static readonly ConcurrentDictionary<int, ReferenceCountedFace> s_facesById = new();
    private static readonly ConcurrentDictionary<FontFace, int> s_faceToId = new();
    private static readonly ConcurrentQueue<int> s_recycledIds = new();

    public static int Register(FontFace face)
    {
        if (s_faceToId.TryGetValue(face, out int existingId))
        {
            s_facesById[existingId].AddRef();
            return existingId;
        }

        int id = AcquireId();
        s_faceToId.TryAdd(face, id);
        s_facesById.TryAdd(id, new ReferenceCountedFace(face, id));
        face.OnDisposed += OnFaceDisposed;

        return id;
    }

    public static void Unregister(int id)
    {
        if (s_facesById.TryGetValue(id, out var faceRef))
        {
            if (faceRef.Release() <= 0)
            {
                s_facesById.TryRemove(id, out _);
                s_recycledIds.Enqueue(id);
            }
        }
    }

    public static FontFace? GetFace(int id)
    {
        if (s_facesById.TryGetValue(id, out var faceRef))
        {
            return faceRef.Face;
        }
        return null;
    }

    private static int AcquireId()
    {
        if (s_recycledIds.TryDequeue(out int recycledId))
        {
            return recycledId;
        }
        return Interlocked.Increment(ref _nextId);
    }

    private static void OnFaceDisposed(FontFace face)
    {
        if (s_faceToId.TryRemove(face, out int id))
        {
            Unregister(id);
        }
    }

    private sealed class ReferenceCountedFace
    {
        public FontFace Face { get; }
        public int Id { get; }
        private int _refCount;

        public ReferenceCountedFace(FontFace face, int id)
        {
            Face = face;
            Id = id;
            _refCount = 1;
        }

        public int AddRef() => Interlocked.Increment(ref _refCount);

        public int Release() => Interlocked.Decrement(ref _refCount);
    }
}

public static class FontFaceExtensions
{
    public static int GetFaceId(this FontFace face)
    {
        return FaceIdRegistry.Register(face);
    }
}