using System.Collections.Generic;
using Antlr4.Runtime;
using OpenFga.Language.Antlr;
using OpenFga.Sdk.Model;

namespace OpenFga.Language.Parsing;

/// <summary>Walks the parse tree and builds an AuthorizationModel (JS OpenFgaDslListener).</summary>
internal sealed class DslListener : OpenFGAParserBaseListener
{
    private readonly Parser _parser;
    private readonly Stack<StackRelation> _rewriteStack = new();
    private TypeDefinition? _currentTypeDef;
    private PartialRelation? _currentRelation;
    private Condition? _currentCondition;
    private bool _isModularModel;
    private string? _moduleName;

    public DslListener(Parser parser)
    {
        _parser = parser;
    }

    private enum RelationOperator
    {
        None,
        Or,
        And,
        ButNot,
    }

    // TypeDefinitions is annotated non-nullable by the SDK, so it starts (and stays) a list, even an empty one.
    public AuthorizationModel Model { get; } = new() { TypeDefinitions = [] };

    public Dictionary<string, TypeDefinition> TypeDefExtensions { get; } = [];

    public override void ExitModuleHeader(OpenFGAParser.ModuleHeaderContext context)
    {
        if (context.moduleName == null)
        {
            return;
        }

        _isModularModel = true;
        _moduleName = context.moduleName.GetText();
    }

    public override void ExitModelHeader(OpenFGAParser.ModelHeaderContext context)
    {
        if (context.schemaVersion != null)
        {
            Model.SchemaVersion = context.schemaVersion.Text;
        }
    }

    public override void EnterTypeDef(OpenFGAParser.TypeDefContext context)
    {
        if (context.typeName == null)
        {
            return;
        }

        if (context.EXTEND() != null && !_isModularModel)
        {
            _parser.NotifyErrorListeners(context.typeName.Start, "extend can only be used in a modular model", null);
        }

        _currentTypeDef = new TypeDefinition
        {
            Type = context.typeName.GetText(),
            Relations = [],
            Metadata = new Metadata { Relations = [] },
        };

        if (_isModularModel)
        {
            _currentTypeDef.Metadata.Module = _moduleName;
        }
    }

    public override void ExitTypeDef(OpenFGAParser.TypeDefContext context)
    {
        var typeDef = _currentTypeDef;
        if (typeDef?.Type == null)
        {
            return;
        }

        if (typeDef.Metadata?.Relations is { Count: 0 })
        {
            if (_isModularModel)
            {
                typeDef.Metadata.Relations = null;
            }
            else
            {
                typeDef.Metadata = null;
            }
        }

        Model.TypeDefinitions.Add(typeDef);

        if (context.EXTEND() != null && _isModularModel)
        {
            if (TypeDefExtensions.ContainsKey(typeDef.Type))
            {
                _parser.NotifyErrorListeners(context.typeName.Start, $"'{typeDef.Type}' is already extended in file.", null);
            }
            else
            {
                TypeDefExtensions[typeDef.Type] = typeDef;
            }
        }

        _currentTypeDef = null;
    }

    public override void EnterRelationDeclaration(OpenFGAParser.RelationDeclarationContext context)
    {
        _currentRelation = new PartialRelation();
        _rewriteStack.Clear();
    }

    public override void ExitRelationDeclaration(OpenFGAParser.RelationDeclarationContext context)
    {
        var relationNameContext = context.relationName();
        var relation = _currentRelation;
        _currentRelation = null;
        if (relationNameContext == null || relation == null || _currentTypeDef?.Relations == null)
        {
            return;
        }

        var relationName = relationNameContext.GetText();
        var relationDef = ParseExpression(relation.Rewrites, relation.Operator);
        if (relationDef == null)
        {
            return;
        }

        if (_currentTypeDef.Relations.ContainsKey(relationName))
        {
            _parser.NotifyErrorListeners(relationNameContext.Start, $"'{relationName}' is already defined in '{_currentTypeDef.Type}'", null);
        }

        _currentTypeDef.Relations[relationName] = relationDef;
        var metadata = new RelationMetadata { DirectlyRelatedUserTypes = relation.DirectlyRelatedUserTypes };
        _currentTypeDef.Metadata ??= new Metadata { Relations = [] };
        _currentTypeDef.Metadata.Relations ??= [];
        _currentTypeDef.Metadata.Relations[relationName] = metadata;

        // Only relations declared inside an `extend type` carry their own module.
        if (_isModularModel && (context.Parent as OpenFGAParser.TypeDefContext)?.EXTEND() != null)
        {
            metadata.Module = _moduleName;
        }
    }

    public override void EnterRelationDefDirectAssignment(OpenFGAParser.RelationDefDirectAssignmentContext context)
    {
        if (_currentRelation != null)
        {
            _currentRelation.DirectlyRelatedUserTypes = [];
        }
    }

    public override void ExitRelationDefDirectAssignment(OpenFGAParser.RelationDefDirectAssignmentContext context)
        => _currentRelation?.Rewrites.Add(new Userset { This = new object() });

    public override void ExitRelationDefTypeRestriction(OpenFGAParser.RelationDefTypeRestrictionContext context)
    {
        var baseRestriction = context.relationDefTypeRestrictionBase();
        if (baseRestriction == null || _currentRelation == null)
        {
            return;
        }

        var reference = new RelationReference { Type = baseRestriction.relationDefTypeRestrictionType?.GetText() ?? string.Empty };

        if (context.conditionName() != null)
        {
            reference.Condition = context.conditionName().GetText();
        }
        else if (context.DOLLAR_EXPRESSION() != null)
        {
            // `$expression` is the reserved inline-expression condition name; it is carried like a named condition.
            reference.Condition = context.DOLLAR_EXPRESSION().GetText();
        }

        if (baseRestriction.relationDefTypeRestrictionRelation != null)
        {
            reference.Relation = baseRestriction.relationDefTypeRestrictionRelation.GetText();
        }

        if (baseRestriction.relationDefTypeRestrictionWildcard != null)
        {
            reference.Wildcard = new object();
        }

        _currentRelation.DirectlyRelatedUserTypes.Add(reference);
    }

    public override void ExitRelationDefRewrite(OpenFGAParser.RelationDefRewriteContext context)
    {
        var computedUserset = new ObjectRelation { Relation = context.rewriteComputedusersetName?.GetText() };
        var rewrite = context.rewriteTuplesetName == null
            ? new Userset { ComputedUserset = computedUserset }
            : new Userset
            {
                TupleToUserset = new TupleToUserset
                {
                    ComputedUserset = computedUserset,
                    Tupleset = new ObjectRelation { Relation = context.rewriteTuplesetName.GetText() },
                },
            };

        _currentRelation?.Rewrites.Add(rewrite);
    }

    public override void ExitRelationRecurse(OpenFGAParser.RelationRecurseContext context)
    {
        if (_currentRelation == null)
        {
            return;
        }

        var relationDef = ParseExpression(_currentRelation.Rewrites, _currentRelation.Operator);
        if (relationDef != null)
        {
            _currentRelation.Rewrites = [relationDef];
        }
    }

    public override void EnterRelationRecurseNoDirect(OpenFGAParser.RelationRecurseNoDirectContext context)
    {
        if (_currentRelation == null)
        {
            return;
        }

        _rewriteStack.Push(new StackRelation(_currentRelation.Rewrites, _currentRelation.Operator));
        _currentRelation.Rewrites = [];
    }

    public override void ExitRelationRecurseNoDirect(OpenFGAParser.RelationRecurseNoDirectContext context)
    {
        if (_currentRelation == null)
        {
            return;
        }

        var relationDef = ParseExpression(_currentRelation.Rewrites, _currentRelation.Operator);
        var popped = _rewriteStack.Count > 0 ? _rewriteStack.Pop() : null;
        if (relationDef != null && popped != null)
        {
            _currentRelation.Operator = popped.Operator;
            _currentRelation.Rewrites = [.. popped.Rewrites, relationDef];
        }
    }

    public override void EnterRelationDefPartials(OpenFGAParser.RelationDefPartialsContext context)
    {
        if (_currentRelation == null)
        {
            return;
        }

        if (context.OR().Length > 0)
        {
            _currentRelation.Operator = RelationOperator.Or;
        }
        else if (context.AND().Length > 0)
        {
            _currentRelation.Operator = RelationOperator.And;
        }
        else if (context.BUT_NOT() != null)
        {
            _currentRelation.Operator = RelationOperator.ButNot;
        }
    }

    public override void EnterCondition(OpenFGAParser.ConditionContext context)
    {
        var conditionNameContext = context.conditionName();
        if (conditionNameContext == null)
        {
            return;
        }

        Model.Conditions ??= [];
        var conditionName = conditionNameContext.GetText();
        if (Model.Conditions.ContainsKey(conditionName))
        {
            _parser.NotifyErrorListeners(conditionNameContext.Start, $"condition '{conditionName}' is already defined in the model", null);
        }

        _currentCondition = new Condition { Name = conditionName, Expression = string.Empty, Parameters = [] };
        if (_isModularModel)
        {
            _currentCondition.Metadata = new ConditionMetadata { Module = _moduleName };
        }
    }

    public override void ExitConditionParameter(OpenFGAParser.ConditionParameterContext context)
    {
        var parameterNameContext = context.parameterName();
        var parameterType = context.parameterType();
        if (parameterNameContext == null || parameterType == null || _currentCondition == null)
        {
            return;
        }

        _currentCondition.Parameters ??= [];
        var parameterName = parameterNameContext.GetText();
        if (_currentCondition.Parameters.ContainsKey(parameterName))
        {
            _parser.NotifyErrorListeners(parameterNameContext.Start, $"parameter '{parameterName}' is already defined in the condition '{_currentCondition.Name}'", null);
        }

        var container = parameterType.CONDITION_PARAM_CONTAINER();
        var typeRef = new ConditionParamTypeRef();
        if (container != null)
        {
            typeRef.TypeName = ConditionTypeNames.FromDsl(container.GetText());
            var generic = parameterType.CONDITION_PARAM_TYPE();
            if (generic != null)
            {
                typeRef.GenericTypes = [new ConditionParamTypeRef { TypeName = ConditionTypeNames.FromDsl(generic.GetText()) }];
            }
        }
        else
        {
            typeRef.TypeName = ConditionTypeNames.FromDsl(parameterType.GetText());
        }

        _currentCondition.Parameters[parameterName] = typeRef;
    }

    public override void ExitConditionExpression(OpenFGAParser.ConditionExpressionContext context)
    {
        if (_currentCondition != null)
        {
            _currentCondition.Expression = context.GetText().Trim();
        }
    }

    public override void ExitCondition(OpenFGAParser.ConditionContext context)
    {
        if (_currentCondition == null)
        {
            return;
        }

        Model.Conditions ??= [];
        Model.Conditions[_currentCondition.Name] = _currentCondition;
        _currentCondition = null;
    }

    private static Userset? ParseExpression(List<Userset> rewrites, RelationOperator relationOperator)
    {
        if (rewrites.Count == 0)
        {
            return null;
        }

        if (rewrites.Count == 1)
        {
            return rewrites[0];
        }

        switch (relationOperator)
        {
            case RelationOperator.Or:
                return new Userset { Union = new Usersets { Child = rewrites } };
            case RelationOperator.And:
                return new Userset { Intersection = new Usersets { Child = rewrites } };
            case RelationOperator.ButNot:
                var difference = new Difference { Base = rewrites[0], Subtract = rewrites[1] };
                rewrites.RemoveRange(0, 2);
                return new Userset { Difference = difference };
            default:
                return null;
        }
    }

    private sealed class PartialRelation
    {
        public List<Userset> Rewrites { get; set; } = [];

        public RelationOperator Operator { get; set; }

        public List<RelationReference> DirectlyRelatedUserTypes { get; set; } = [];
    }

    private sealed class StackRelation(List<Userset> rewrites, RelationOperator relationOperator)
    {
        public List<Userset> Rewrites { get; } = rewrites;

        public RelationOperator Operator { get; } = relationOperator;
    }
}
