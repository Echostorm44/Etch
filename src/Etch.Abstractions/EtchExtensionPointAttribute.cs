namespace Etch.Abstractions;

/// <summary>
/// Marks an interface as an intentional extension point, exempting it from the ET0107
/// (no-single-implementer) rule.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class EtchExtensionPointAttribute : Attribute
{
}