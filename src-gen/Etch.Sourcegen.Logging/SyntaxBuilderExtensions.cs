using System.Text;

namespace Etch.Sourcegen.Logging;

internal static partial class SyntaxBuilderExtensions
{
    internal static void AppendLine(this StringBuilder sb, string value)
    {
        sb.AppendLine(value);
    }
}
