using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Typizer.Tests
{
    /// <summary>
    /// Every ```csharp block of README.md is a complete program and compiles against this SDK and
    /// ASP.NET Core, with the implicit usings of a `dotnet new web` project. (```cs blocks are fragments.)
    /// </summary>
    public class ReadmeTests
    {
        private const string ImplicitUsings = @"
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Net;
global using System.Net.Http;
global using System.Net.Http.Json;
global using System.Threading;
global using System.Threading.Tasks;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Hosting;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Routing;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
";

        public static IEnumerable<object[]> Snippets()
        {
            string readme = File.ReadAllText(Path.Combine(Repo.PackageDir, "README.md"));
            MatchCollection blocks = Regex.Matches(readme, "```csharp\r?\n(.*?)```", RegexOptions.Singleline);
            Assert.True(blocks.Count >= 10, "README has fewer C# examples than expected");
            for (int i = 0; i < blocks.Count; i++)
            {
                int line = readme.Substring(0, blocks[i].Index).Count(c => c == '\n') + 1;
                yield return new object[] { line, blocks[i].Groups[1].Value };
            }
        }

        private static readonly Lazy<List<MetadataReference>> References = new Lazy<List<MetadataReference>>(() =>
        {
            var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).ToList();
            paths.AddRange(Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"));
            return paths.GroupBy(Path.GetFileName).Select(g => (MetadataReference)MetadataReference.CreateFromFile(g.First())).ToList();
        });

        [Theory(DisplayName = "README example compiles")]
        [MemberData(nameof(Snippets))]
        public void SnippetCompiles(int line, string code)
        {
            var parse = new CSharpParseOptions(LanguageVersion.CSharp12);
            CSharpCompilation compilation = CSharpCompilation.Create(
                "ReadmeSnippet" + line,
                new[] { CSharpSyntaxTree.ParseText(ImplicitUsings, parse), CSharpSyntaxTree.ParseText(code, parse) },
                References.Value,
                new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
            var errors = compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error || (d.Severity == DiagnosticSeverity.Warning && d.Id.StartsWith("CS86", StringComparison.Ordinal)))
                .Select(d => d.ToString())
                .ToList();
            Assert.True(errors.Count == 0, "README.md line " + line + ":\n" + string.Join("\n", errors));
        }
    }
}
