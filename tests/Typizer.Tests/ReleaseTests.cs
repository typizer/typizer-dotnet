using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Xml.Linq;

namespace Typizer.Tests
{
    /// <summary>What ships: version, dependencies, target frameworks and public texts.</summary>
    public class ReleaseTests
    {
        private static string ReadPackageFile(params string[] path) => File.ReadAllText(Path.Combine(new[] { Repo.PackageDir }.Concat(path).ToArray()));

        [Fact(DisplayName = "TypizerClient.Version is the package version (Directory.Build.props)")]
        public void VersionMatchesThePackage()
        {
            string version = XDocument.Parse(ReadPackageFile("Directory.Build.props")).Descendants("TypizerVersion").Single().Value;
            Assert.Equal(TypizerClient.Version, version);
            Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", version);
            Assert.Contains("## [" + version + "]", ReadPackageFile("CHANGELOG.md"));
        }

        [Fact(DisplayName = "The only dependency of Typizer is System.Text.Json, and only for netstandard2.0")]
        public void OnlySystemTextJson()
        {
            XDocument project = XDocument.Parse(ReadPackageFile("src", "Typizer", "Typizer.csproj"));
            var references = project.Descendants("PackageReference").ToList();
            XElement only = Assert.Single(references);
            Assert.Equal("System.Text.Json", only.Attribute("Include")!.Value);
            Assert.Equal("'$(TargetFramework)' == 'netstandard2.0'", only.Parent!.Attribute("Condition")!.Value);
            Assert.Equal("netstandard2.0;net8.0", project.Descendants("TargetFrameworks").Single().Value);
            Assert.Empty(project.Descendants("ProjectReference"));
        }

        [Fact(DisplayName = "The tests run against the intended build of Typizer (net8.0 or netstandard2.0)")]
        public void RunsAgainstTheIntendedBuild()
        {
            string framework = typeof(TypizerClient).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName;
#if TYPIZER_NETSTANDARD
            Assert.Equal(".NETStandard,Version=v2.0", framework);
#else
            Assert.Equal(".NETCoreApp,Version=v8.0", framework);
#endif
        }

        [Fact(DisplayName = "The public surface does not depend on Microsoft.Bcl.AsyncInterfaces (absent from .NET 8)")]
        public void NoBclAsyncInterfacesInPublicSurface()
        {
            Assert.DoesNotContain(typeof(TypizerClient).Assembly.GetReferencedAssemblies(), a => a.Name == "Microsoft.Bcl.AsyncInterfaces");
        }

        [Fact(DisplayName = "README follows the SDK plan (SPEC §12) and installs the right package")]
        public void ReadmePlan()
        {
            string readme = ReadPackageFile("README.md");
            string[] sections =
            {
                "## Установка", "## Ключ API", "## Быстрый старт", "## Схема из каталога", "## Типизатор", "## Ошибки",
                "## Повторы и идемпотентность", "## Вебхуки", "## Прокси и свой HTTP-клиент", "## Логи", "## Документация", "## Лицензия",
            };
            int last = -1;
            foreach (string section in sections)
            {
                int at = readme.IndexOf(section, StringComparison.Ordinal);
                Assert.True(at > last, section + " is missing or out of order");
                last = at;
            }
            Assert.Contains("dotnet add package Typizer", readme);
            Assert.Contains("dotnet add package Typizer.Extensions.DependencyInjection", readme);
        }
    }
}
