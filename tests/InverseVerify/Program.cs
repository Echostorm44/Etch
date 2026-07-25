using Etch.Geometry;

var a = new Affine(-0.6188107294118081, -0.827963066672889, 1.9607494473274563, -1.6992271094113716, 494.2285283441788, -162.074539187399);
Console.WriteLine($"A = {a}");
Console.WriteLine($"det = {a.Determinant()}");

var inv = a.Inverse();
Console.WriteLine($"A.Inverse() = {inv}");

var composed = inv * a;
Console.WriteLine($"Inverse * A = {composed}");

Console.WriteLine($"Identity check:");
Console.WriteLine($"  M00={composed.M00} (should be ~1)");
Console.WriteLine($"  M11={composed.M11} (should be ~1)");
Console.WriteLine($"  M01={composed.M01} (should be ~0)");
Console.WriteLine($"  M10={composed.M10} (should be ~0)");
Console.WriteLine($"  M02={composed.M02} (should be ~0)");
Console.WriteLine($"  M12={composed.M12} (should be ~0)");

Console.WriteLine();
Console.WriteLine("Testing with identity:");
var id = Affine.Identity;
var idInv = id.Inverse();
Console.WriteLine($"Identity.Inverse() = {idInv}");
var idComposed = idInv * id;
Console.WriteLine($"Inverse * Identity = {idComposed}");