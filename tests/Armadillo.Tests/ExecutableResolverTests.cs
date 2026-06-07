using Armadillo.Detection;
using Xunit;

namespace Armadillo.Tests;

public class ExecutableResolverTests
{
    [Fact]
    public void Resolves_name_via_pathext_in_search_dir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "armadillo-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var exe = Path.Combine(dir, "mytool.cmd");
            File.WriteAllText(exe, "@echo hi");

            var resolver = new ExecutableResolver(new[] { dir });
            var hit = resolver.Resolve(new[] { "mytool" });

            Assert.Equal(exe, hit, ignoreCase: true); // PATHEXT supplies the (upper-cased) extension
            Assert.Null(resolver.Resolve(new[] { "doesnotexist" }));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
