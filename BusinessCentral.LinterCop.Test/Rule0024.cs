namespace BusinessCentral.LinterCop.Test;

public class Rule0024
{
    private string _testCaseDir = "";

    [SetUp]
    public void Setup()
    {
        _testCaseDir = Path.Combine(Directory.GetParent(Environment.CurrentDirectory)!.Parent!.Parent!.FullName,
            "TestCases", "Rule0024");
    }

    [Test]
    [TestCase("ProcedureWithSemicolonAfterDeclaration")]
    public async Task HasDiagnostic(string testCase)
    {
        var code = await File.ReadAllTextAsync(Path.Combine(_testCaseDir, "HasDiagnostic", $"{testCase}.al"))
            .ConfigureAwait(false);

        var fixture = RoslynFixtureFactory.Create<Rule0024SemicolonAfterMethodOrTriggerDeclaration>();
        fixture.HasDiagnosticAtAllMarkers(code, DiagnosticDescriptors.Rule0024SemicolonAfterMethodOrTriggerDeclaration.Id);
    }

    [Test]
    [TestCase("ObsoleteStatePending")]
    [TestCase("ProcedureWithoutBody")]
    public async Task NoDiagnostic(string testCase)
    {
        var code = await File.ReadAllTextAsync(Path.Combine(_testCaseDir, "NoDiagnostic", $"{testCase}.al"))
            .ConfigureAwait(false);

        var fixture = RoslynFixtureFactory.Create<Rule0024SemicolonAfterMethodOrTriggerDeclaration>();
        fixture.NoDiagnosticAtAllMarkers(code, DiagnosticDescriptors.Rule0024SemicolonAfterMethodOrTriggerDeclaration.Id);
    }
}