// This file intentionally attempts to return a BezPathBuilder from a method.
// BezPathBuilder is a ref struct — it cannot escape its scope. This file must NOT compile.
internal sealed class BezPathBuilderEscapeFail
{
    static BezPathBuilder CreateBuilder()
    {
        return Etch.Geometry.BezPathBuilder.Begin(4); // ERROR: ref struct return
    }
}
