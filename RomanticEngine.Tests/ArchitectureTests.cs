using System;
using System.IO;
using System.Linq;
using Xunit;

namespace RomanticEngine.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Test_State_Mutation_Abstraction_Guardrail()
    {
        // We want to ensure that direct calls to MakeMove/TakeMove 
        // on the library's IPosition are only happening in approved files.
        // Approved files: PositionDriver.cs (the owner) and Engine.cs (setup history)
        
        string projectDir = FindProjectDirectory();
        var coreFiles = Directory.GetFiles(Path.Combine(projectDir, "RomanticEngine.Core"), "*.cs", SearchOption.AllDirectories);
        
        var violations = coreFiles
            .Where(f => !f.EndsWith("PositionDriver.cs") && !f.EndsWith("Engine.cs"))
            .Select(f => new { File = f, Content = File.ReadAllText(f) })
            .Where(x => x.Content.Contains(".MakeMove(") || x.Content.Contains(".TakeMove("))
            .ToList();

        Assert.Empty(violations.Select(v => Path.GetFileName(v.File)));
    }

    private string FindProjectDirectory()
    {
        var current = Directory.GetCurrentDirectory();
        while (current != null && !Directory.GetFiles(current, "*.sln").Any())
        {
            current = Directory.GetParent(current)?.FullName;
        }
        return current ?? throw new InvalidOperationException("Could not find solution directory.");
    }
}
