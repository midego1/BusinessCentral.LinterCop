#if !LessThenFall2023RV1
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using BusinessCentral.LinterCop.Helpers;
using Microsoft.Dynamics.Nav.CodeAnalysis;
using Microsoft.Dynamics.Nav.CodeAnalysis.Diagnostics;
using Microsoft.Dynamics.Nav.CodeAnalysis.Semantics;
using Microsoft.Dynamics.Nav.CodeAnalysis.Symbols;
using Microsoft.Dynamics.Nav.CodeAnalysis.Syntax;

namespace BusinessCentral.LinterCop.Design;

[DiagnosticAnalyzer]
public class Rule0051PossibleOverflowAssigning : DiagnosticAnalyzer
{
    private readonly Lazy<Regex> strSubstNoPatternLazy = new Lazy<Regex>(() => new Regex("[#%](\\d+)", RegexOptions.Compiled));

    // Build-in methods like Database.CompanyName() and Database.UserId() have indirectly a return length
    private static readonly Dictionary<string, int> BuiltInMethodNameWithReturnLength = new()
        {
            { "CompanyName", 30 },
            { "UserId", 50 }
        };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(DiagnosticDescriptors.Rule0051PossibleOverflowAssigning);

    private Regex StrSubstNoPattern => this.strSubstNoPatternLazy.Value;

    public override void Initialize(AnalysisContext context)
    {
        context.RegisterOperationAction(AnalyzeTypeKindLabel, OperationKind.AssignmentStatement, OperationKind.ExitStatement);
        context.RegisterOperationAction(AnalyzeSetFilter, OperationKind.InvocationExpression);
        context.RegisterOperationAction(AnalyzeValidate, OperationKind.InvocationExpression);
#if !LessThenSpring2024
        context.RegisterOperationAction(AnalyzeGetMethod, OperationKind.InvocationExpression);
#endif
    }

    #region AnalyzeTypeKindLabel
    private static readonly HashSet<string> labelExcludedProperties = new(StringComparer.OrdinalIgnoreCase) {
        LabelPropertyHelper.MaxLength,
        LabelPropertyHelper.Locked
    };

    // This rule is an extension of the CodeCop AA0139 to only check for Label variables without the MaxLength or Locked property explicitly set.
    // https://learn.microsoft.com/en-us/dynamics365/business-central/dev-itpro/developer/analyzers/codecop-aa0139
    private void AnalyzeTypeKindLabel(OperationAnalysisContext ctx)
    {
        if (ctx.IsObsoletePendingOrRemoved())
            return;

        // Early return if the source is not a Label type
        var sourceConvExpr = TryGetSourceValue(ctx.Operation);
        if (sourceConvExpr?.Operand is not IOperation operand || operand.Type.GetNavTypeKindSafe() != NavTypeKind.Label)
            return;

        if (operand.GetSymbol() is not IVariableSymbol variableSymbol)
            return;

        if (variableSymbol.DeclaringSyntaxReference?.GetSyntax() is not VariableDeclarationSyntax variableSyntax)
            return;

        if (variableSyntax.Type.DataType is not LabelDataTypeSyntax labelSyntax)
            return;

        // Let CodeCop AA0139 handle it if MaxLength or Locked is set
        if (HasMaxLengthOrLockedPropertySet(labelSyntax.Label.Properties))
            return;

        var targetTypeSymbol = OperationHelper.TryGetTargetTypeSymbol(ctx.Operation, ctx.Compilation);
        if (targetTypeSymbol == null)
            return;

        bool isError = false;
        int targetTypeLength = targetTypeSymbol.GetTypeLength(ref isError);
        if (isError || targetTypeLength == int.MaxValue)
            return;

        if (labelSyntax.Label.LabelText.Value.Value is not string labelValueText)
            return;

        // CodeCop AA0139 raises a diagnostic if the label value exceeds the target length.
        // In contrast here we're going to raises a diagnostic only when the label value is shorter than the target
        if (labelValueText.Length < targetTypeLength)
        {
            ctx.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.Rule0051PossibleOverflowAssigning,
                    operand.Syntax.GetLocation(),
                    variableSymbol.GetTypeSymbol().ToDisplayString(),
                    targetTypeSymbol.ToDisplayString()));
        }
    }

    private static IConversionExpression? TryGetSourceValue(IOperation operation)
    {
        return operation switch
        {
            IAssignmentStatement { Value: IConversionExpression ce } => ce,
            IExitStatement { ReturnedValue: IConversionExpression ce } => ce,
            _ => null
        };
    }


    private static bool HasMaxLengthOrLockedPropertySet(CommaSeparatedIdentifierEqualsLiteralListSyntax? properties)
    {
        if (properties is null)
            return false;

        foreach (var value in properties.Values)
        {
            var name = value.Identifier.Value?.ToString();
            if (!string.IsNullOrEmpty(name) && labelExcludedProperties.Contains(name))
                return true;
        }

        return false;
    }
    #endregion

    private void AnalyzeSetFilter(OperationAnalysisContext ctx)
    {
        if (ctx.IsObsoletePendingOrRemoved() || ctx.Operation is not IInvocationExpression operation)
            return;

        if (operation.TargetMethod.MethodKind != MethodKind.BuiltInMethod ||
            operation.TargetMethod.Name != "SetFilter" ||
            operation.Arguments.Length < 3 ||
            operation.Arguments[0].Value.Kind != OperationKind.ConversionExpression)
            return;

        var fieldOperand = ((IConversionExpression)operation.Arguments[0].Value).Operand;
        if (fieldOperand.Type is not ITypeSymbol fieldType)
            return;

        if (fieldType.GetNavTypeKindSafe() == NavTypeKind.Text)
            return;

        bool isError = false;
        int typeLength = fieldType.GetTypeLength(ref isError);
        if (isError || typeLength == int.MaxValue)
            return;

        foreach (int argIndex in GetArgumentIndexes(operation.Arguments[1].Value))
        {
            int index = argIndex + 1; // The placeholders are defines as %1, %2, %3, where in case of %1 we need the second (zero based) index of the arguments of the SetFilter method
            if ((index < 2) ||
                 (index >= operation.Arguments.Length) ||
                 (operation.Arguments[index].Value.Kind != OperationKind.ConversionExpression))
                continue;

            if (operation.Arguments[index].Value is not IConversionExpression argValue)
                continue;

            int expressionLength = this.CalculateMaxExpressionLength(argValue.Operand, ref isError);
            if (!isError && expressionLength > typeLength)
                ctx.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.Rule0051PossibleOverflowAssigning,
                    operation.Arguments[index].Syntax.GetLocation(),
                    GetDisplayString(operation.Arguments[index], operation),
                    GetDisplayString(operation.Arguments[0], operation)));
        }
    }

    private void AnalyzeValidate(OperationAnalysisContext ctx)
    {
        if (ctx.IsObsoletePendingOrRemoved() || ctx.Operation is not IInvocationExpression operation)
            return;

        if (operation.TargetMethod.MethodKind != MethodKind.BuiltInMethod ||
            operation.TargetMethod.Name != "Validate" ||
            operation.TargetMethod.ContainingSymbol?.Name != "Table" ||
            operation.Arguments.Length < 2 ||
            operation.Arguments[0].Value.Kind != OperationKind.ConversionExpression)
            return;

        var fieldOperand = ((IConversionExpression)operation.Arguments[0].Value).Operand;
        if (fieldOperand.Type is not ITypeSymbol fieldType)
            return;

        bool isError = false;
        int typeLength = fieldType.GetTypeLength(ref isError);
        if (isError || typeLength == int.MaxValue)
            return;

        if (operation.Arguments[1].Value is not IConversionExpression argValue)
            return;

        int expressionLength = this.CalculateMaxExpressionLength(argValue.Operand, ref isError);
        if (!isError && expressionLength > typeLength)
            ctx.ReportDiagnostic(
                Diagnostic.Create(
                    DiagnosticDescriptors.Rule0051PossibleOverflowAssigning,
                    operation.Arguments[1].Syntax.GetLocation(),
                    GetDisplayString(operation.Arguments[1], operation),
                    GetDisplayString(operation.Arguments[0], operation)));
    }

#if !LessThenSpring2024
    private void AnalyzeGetMethod(OperationAnalysisContext ctx)
    {
        if (ctx.IsObsoletePendingOrRemoved() || ctx.Operation is not IInvocationExpression operation)
            return;

        if (operation.TargetMethod.MethodKind != MethodKind.BuiltInMethod ||
            operation.TargetMethod.Name != "Get" ||
            operation.TargetMethod.ContainingSymbol?.Name != "Table" ||
            operation.Arguments.Length < 1)
            return;

        if (operation.Instance?.Type.GetTypeSymbol()?.OriginalDefinition is not ITableTypeSymbol table)
            return;

        if (operation.Arguments.Length < table.PrimaryKey.Fields.Length)
            return;

        for (int index = 0; index < table.PrimaryKey.Fields.Length; index++)
        {
            var fieldType = table.PrimaryKey.Fields[index].Type;
            var argumentType = operation.Arguments[index].GetTypeSymbol();

            if (fieldType is null || argumentType is null || argumentType.HasLength)
                continue;

            bool isError = false;
            int fieldLength = fieldType.GetTypeLength(ref isError);
            if (isError || fieldLength == 0)
                continue;

            if (operation.Arguments[index].Value is not IConversionExpression argValue)
                continue;

            int expressionLength = this.CalculateMaxExpressionLength(argValue.Operand, ref isError);
            if (!isError && expressionLength > fieldLength)
            {
                string lengthSuffix = expressionLength < int.MaxValue
                    ? $"[{expressionLength}]"
                    : string.Empty;

                ctx.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.Rule0051PossibleOverflowAssigning,
                    operation.Arguments[index].Syntax.GetLocation(),
                    $"{argumentType.ToDisplayString()}{lengthSuffix}",
                    fieldType.ToDisplayString()));
            }
        }
    }
#endif

    private int CalculateMaxExpressionLength(IOperation expression, ref bool isError)
    {
        if (expression.Syntax.Parent.IsKind(SyntaxKind.CaseLine))
        {
            isError = true;
            return 0;
        }
        switch (expression.Kind)
        {
            case OperationKind.LiteralExpression:
                if (expression.Type.IsTextType())
                    return expression.ConstantValue.Value.ToString()!.Length;
                ITypeSymbol type = expression.Type;
                if ((type is not null ? (type.NavTypeKind == NavTypeKind.Char ? 1 : 0) : 0) != 0)
                    return 1;
                break;
            case OperationKind.ConversionExpression:
                return this.CalculateMaxExpressionLength(((IConversionExpression)expression).Operand, ref isError);
            case OperationKind.InvocationExpression:
                IInvocationExpression invocation = (IInvocationExpression)expression;
                IMethodSymbol targetMethod = invocation.TargetMethod;
                if (targetMethod is not null && targetMethod.ContainingSymbol?.Kind == SymbolKind.Class)
                {
                    if (IsBuiltInMethodWithReturnLength(targetMethod, out int length))
                        return length;

                    switch (targetMethod.Name.ToLowerInvariant())
                    {
                        case "convertstr":
                        case "delchr":
                        case "delstr":
                        case "incstr":
                        case "lowercase":
                        case "uppercase":
                            if (invocation.Arguments.Length > 0)
                                return this.CalculateBuiltInMethodResultLength(invocation, 0, ref isError);
                            break;
                        case "copystr":
                            if (invocation.Arguments.Length == 3)
                                return this.CalculateBuiltInMethodResultLength(invocation, 2, ref isError);
                            break;
                        case "format":
                            return 0;
                        case "padstr":
                        case "substring":
                            if (invocation.Arguments.Length >= 2)
                                return this.CalculateBuiltInMethodResultLength(invocation, 1, ref isError);
                            break;
                        case "strsubstno":
                            if (invocation.Arguments.Length > 0)
                                return this.CalculateStrSubstNoMethodResultLength(invocation, ref isError);
                            break;
                        case "tolower":
                        case "toupper":
                            if (invocation.Instance is not null && invocation.Instance.IsBoundExpression())
                                return invocation.Instance.Type.GetTypeLength(ref isError);
                            break;
                    }
                }
                return expression.Type.GetTypeLength(ref isError);
            case OperationKind.LocalReferenceExpression:
            case OperationKind.GlobalReferenceExpression:
            case OperationKind.ReturnValueReferenceExpression:
            case OperationKind.ParameterReferenceExpression:
            case OperationKind.FieldAccess:
                return expression.Type.GetTypeLength(ref isError);
            case OperationKind.BinaryOperatorExpression:
                IBinaryOperatorExpression operatorExpression = (IBinaryOperatorExpression)expression;
                return Math.Min(int.MaxValue, this.CalculateMaxExpressionLength(operatorExpression.LeftOperand, ref isError) + this.CalculateMaxExpressionLength(operatorExpression.RightOperand, ref isError));
        }
        isError = true;
        return 0;
    }

    private static int? TryGetLength(IInvocationExpression invocation, int lengthArgPos)
    {
        if (!(SemanticFacts.GetBoundExpressionArgument(invocation, lengthArgPos) is IConversionExpression expressionArgument))
            return new int?();
        ITypeSymbol type = expressionArgument.Operand.Type;
        return type.HasLength ? new int?(type.Length) : new int?();
    }

    private int CalculateBuiltInMethodResultLength(
      IInvocationExpression invocation,
      int lengthArgPos,
      ref bool isError)
    {
        IOperation operation = invocation.Arguments[lengthArgPos].Value;
        switch (operation.Kind)
        {
            case OperationKind.LiteralExpression:
                Optional<object> constantValue = operation.ConstantValue;
                if (constantValue.HasValue)
                {
                    if (operation.Type.IsIntegralType())
                    {
                        constantValue = operation.ConstantValue;
                        return (int)constantValue.Value;
                    }
                    if (operation.Type.IsTextType())
                    {
                        constantValue = operation.ConstantValue;
                        return constantValue.Value.ToString()?.Length ?? 0;
                    }
                    break;
                }
                break;
            case OperationKind.InvocationExpression:
                invocation = (IInvocationExpression)operation;
                IMethodSymbol targetMethod = invocation.TargetMethod;
                if (targetMethod is not null && SemanticFacts.IsSameName(targetMethod.Name, "maxstrlen") && targetMethod.ContainingSymbol?.Kind == SymbolKind.Class)
                {
                    ImmutableArray<IArgument> arguments = invocation.Arguments;
                    if (arguments.Length == 1)
                    {
                        arguments = invocation.Arguments;
                        IOperation operand = arguments[0].Value;
                        if (operand.Kind == OperationKind.ConversionExpression)
                            operand = ((IConversionExpression)operand).Operand;
                        return operand.Type.GetTypeLength(ref isError);
                    }
                    break;
                }
                break;
        }
        return TryGetLength(invocation, lengthArgPos) ?? invocation.Type.GetTypeLength(ref isError);
    }

    private int CalculateStrSubstNoMethodResultLength(
      IInvocationExpression invocation,
      ref bool isError)
    {
        IOperation operation = invocation.Arguments[0].Value;
        if (!operation.Type.IsTextType())
        {
            isError = true;
            return -1;
        }
        Optional<object> constantValue = operation.ConstantValue;
        if (!constantValue.HasValue)
        {
            isError = true;
            return -1;
        }
        constantValue = operation.ConstantValue;
        string? input = constantValue.Value.ToString();
        if (input is null)
        {
            isError = true;
            return -1;
        }
        Match match = this.StrSubstNoPattern.Match(input);
        int num;
        for (num = input.Length; !isError && match.Success && num < int.MaxValue; match = match.NextMatch())
        {
            string s = match.Groups[1].Value;
            int result = 0;
            if (int.TryParse(s, out result) && 0 < result && result < invocation.Arguments.Length)
            {
                int expressionLength = this.CalculateMaxExpressionLength(invocation.Arguments[result].Value, ref isError);
                num = expressionLength == int.MaxValue ? expressionLength : num + expressionLength - s.Length - 1;
            }
        }
        return !isError ? num : -1;
    }

    private static string GetDisplayString(IArgument argument, IInvocationExpression operation)
    {
        return ((IConversionExpression)argument.Value).Operand.Type.ToDisplayString();
    }

    private List<int> GetArgumentIndexes(IOperation operand)
    {
        List<int> results = new List<int>();

        if (operand.Syntax.Kind != SyntaxKind.LiteralExpression)
            return results;

        foreach (Match match in this.StrSubstNoPattern.Matches(operand.Syntax.ToFullString()))
        {
            if (int.TryParse(match.Groups[1].Value, out int number))
                if (!results.Contains(number))
                    results.Add(number);
        }

        return results;
    }

    private static bool IsBuiltInMethodWithReturnLength(IMethodSymbol targetMethod, out int length)
    {
        length = 0;

        if (targetMethod.MethodKind != MethodKind.BuiltInMethod)
            return false;

        return BuiltInMethodNameWithReturnLength.TryGetValue(targetMethod.Name, out length);
    }
}
#endif