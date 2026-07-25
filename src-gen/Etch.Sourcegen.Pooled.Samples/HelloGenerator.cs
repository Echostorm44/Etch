using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Etch.Sourcegen.Pooled.Samples;

[Generator]
public partial class HelloGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static output =>
        {
            output.AddSource("HelloAttribute.g.cs", """
                namespace Etch.Sourcegen.Pooled.Samples;

                [global::System.AttributeUsage(global::System.AttributeTargets.Class)]
                public sealed class HelloTargetAttribute : global::System.Attribute { }
                """);
        });

        context.RegisterSourceOutput(
            context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax cds && cds.AttributeLists.Count > 0,
                static (context, cancellationToken) =>
                {
                    var classDecl = (ClassDeclarationSyntax)context.Node;
                    foreach (var attributeList in classDecl.AttributeLists)
                    {
                        foreach (var attribute in attributeList.Attributes)
                        {
                            if (attribute.Name.GetText().ToString() == "HelloTargetAttribute")
                            {
                                return classDecl;
                            }
                        }
                    }
                    return null;
                }),
            static (output, classDecl) =>
            {
                if (classDecl == null) return;

                var className = classDecl.Identifier.Text;
                var namespaceName = classDecl.Parent is NamespaceDeclarationSyntax ns
                    ? ns.Name.ToString()
                    : "global";

                output.AddSource("Hello.g.cs", $$"""
                    namespace {{namespaceName}};

                    public partial class {{className}}
                    {
                        public static string Hello() => "Hello from {{className}}";
                    }
                    """);
            });
    }
}