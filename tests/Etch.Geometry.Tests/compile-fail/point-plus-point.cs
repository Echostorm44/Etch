// This file intentionally attempts to add two Point values.
// Point + Point is not a defined operator — this file must NOT compile.
internal sealed class PointPlusPointCompileFail
{
    private static readonly Etch.Geometry.Point _ = new Etch.Geometry.Point(1.0, 2.0) + new Etch.Geometry.Point(3.0, 4.0);
}
