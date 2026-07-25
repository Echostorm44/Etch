// This file intentionally attempts to store FrameContext as a field.
// FrameContext is a ref struct — it cannot be stored as a field. This file must NOT compile.
internal sealed class FrameContextEscapeFail
{
    private Etch.Gpu.Compositor.FrameContext _context;
}