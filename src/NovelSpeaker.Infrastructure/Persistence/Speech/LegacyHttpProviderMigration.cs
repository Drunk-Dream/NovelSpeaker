using System.Globalization;
using System.Text.Json;
using Acornima;
using Acornima.Ast;
using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Infrastructure.Speech.Rules;

namespace NovelSpeaker.Infrastructure.Persistence.Speech;

internal static class LegacyHttpProviderMigration
{
    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        var legacyRules = await ReadLegacyRulesAsync(connection, transaction, cancellationToken);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SpeechProviderNameRules.MicrosoftEdgeName
        };
        var sortOrder = 0;
        var migratedAt = DateTimeOffset.UtcNow;

        foreach (var legacyRule in legacyRules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (legacyRule.Id <= 0 ||
                !TryConvert(legacyRule, migratedAt, out var converted))
            {
                continue;
            }

            var providerId = legacyRule.IsEnabled
                ? ProviderId.FromLegacyHttpTtsRuleId(legacyRule.Id)
                : ProviderId.New();
            var provider = new SpeechProviderInstance(
                providerId,
                MakeUniqueName(converted.Name, usedNames),
                sortOrder++,
                converted.Configuration,
                migratedAt,
                migratedAt);
            await InsertProviderAsync(connection, transaction, provider, cancellationToken);
        }

        await using var dropLegacyTable = connection.CreateCommand();
        dropLegacyTable.Transaction = transaction;
        dropLegacyTable.CommandText = "DROP TABLE HttpTtsRules;";
        await dropLegacyTable.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<LegacyHttpRule>> ReadLegacyRulesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT Id, Name, Url, ConcurrentRate, Header, RequestOptionsJson, IsEnabled
            FROM HttpTtsRules
            ORDER BY CASE WHEN LastUsedAt IS NULL THEN 1 ELSE 0 END,
                     LastUsedAt DESC,
                     Name,
                     Id;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rules = new List<LegacyHttpRule>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!TryReadId(reader, out var id) ||
                !TryReadNullableText(reader, 1, out var name) ||
                !TryReadNullableText(reader, 2, out var url) ||
                !TryReadNullableText(reader, 3, out var concurrentRate) ||
                !TryReadNullableText(reader, 4, out var headers) ||
                !TryReadNullableText(reader, 5, out var requestOptions) ||
                !TryReadEnabled(reader, out var isEnabled))
            {
                continue;
            }

            rules.Add(new LegacyHttpRule(id, name, url, concurrentRate, headers, requestOptions, isEnabled));
        }

        return rules;
    }

    private static bool TryReadId(SqliteDataReader reader, out long id)
    {
        try
        {
            id = reader.GetInt64(0);
            return true;
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            id = 0;
            return false;
        }
    }

    private static bool TryReadNullableText(SqliteDataReader reader, int ordinal, out string? value)
    {
        if (reader.IsDBNull(ordinal))
        {
            value = null;
            return true;
        }

        try
        {
            value = reader.GetString(ordinal);
            return true;
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            value = null;
            return false;
        }
    }

    private static bool TryReadEnabled(SqliteDataReader reader, out bool enabled)
    {
        try
        {
            var value = reader.GetInt64(6);
            enabled = value == 1;
            return value is 0 or 1;
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            enabled = false;
            return false;
        }
    }

    private static bool TryConvert(
        LegacyHttpRule legacy,
        DateTimeOffset migratedAt,
        out ConvertedRule converted)
    {
        converted = null!;
        if (string.IsNullOrWhiteSpace(legacy.Name) || string.IsNullOrWhiteSpace(legacy.Url))
        {
            return false;
        }

        var name = legacy.Name.Trim();
        var url = legacy.Url.Trim();
        IReadOnlyDictionary<string, string> headers;
        string? requestMethod;
        string? body;
        bool structuredBody;
        try
        {
            headers = TtsRuleStructuredFieldsCodec.ParseHeaders(legacy.Header);
            requestMethod = TtsRuleStructuredFieldsCodec.ParseRequestMethod(legacy.RequestOptionsJson);
            body = TtsRuleStructuredFieldsCodec.ParseRequestBody(legacy.RequestOptionsJson);
            structuredBody = TtsRuleStructuredFieldsCodec.IsRequestBodyJsonStructure(legacy.RequestOptionsJson);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }

        var method = string.IsNullOrWhiteSpace(requestMethod)
            ? string.IsNullOrWhiteSpace(body) ? "GET" : "POST"
            : requestMethod.Trim().ToUpperInvariant();
        if (method is not ("GET" or "POST") || !HaveValidTemplates(url, headers, body))
        {
            return false;
        }

        var mutableHeaders = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
        if (!TryMigrateBody(body, structuredBody, method, mutableHeaders, out var bodyTemplate) ||
            !TryParseRateLimit(legacy.ConcurrentRate, out var rateLimit))
        {
            return false;
        }

        var configuration = new HttpSpeechProviderConfiguration(
            url,
            method,
            mutableHeaders,
            bodyTemplate,
            rateLimit);
        var validationName = SpeechProviderNameRules.IsReserved(name) ? $"{name} (2)" : name;
        var validationProvider = new SpeechProviderInstance(
            ProviderId.New(),
            validationName,
            0,
            configuration,
            migratedAt,
            migratedAt);
        if (!ProviderConfigurationValidator.Validate(validationProvider).IsValid)
        {
            return false;
        }

        converted = new ConvertedRule(name, configuration);
        return true;
    }

    private static bool HaveValidTemplates(
        string url,
        IReadOnlyDictionary<string, string> headers,
        string? body)
    {
        try
        {
            return IsConvertibleTemplate(url) &&
                   headers.Values.All(IsConvertibleTemplate) &&
                   (body is null || IsConvertibleTemplate(body));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsConvertibleTemplate(string text)
    {
        var template = NormalizedTemplate.Parse(text);
        foreach (var segment in template.Segments.OfType<ExpressionTemplateSegment>())
        {
            Node expression;
            try
            {
                expression = new Parser().ParseExpression(segment.Expression);
            }
            catch (ParseErrorException)
            {
                return false;
            }

            if (ReferencesRemovedLegacyGlobal(expression))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ReferencesRemovedLegacyGlobal(Node root)
    {
        var parents = new Dictionary<Node, Node?>(ReferenceEqualityComparer.Instance)
        {
            [root] = null
        };
        var nodes = new Stack<Node>();
        nodes.Push(root);
        while (nodes.TryPop(out var node))
        {
            foreach (var child in node.ChildNodes)
            {
                if (parents.TryAdd(child, node))
                {
                    nodes.Push(child);
                }
            }
        }

        var localBindings = FindLocalDeclarations(root, parents);
        foreach (var (node, parent) in parents)
        {
            if (node is not Identifier identifier ||
                identifier.Name is not ("source" or "java" or "cookie" or "loginInfo") ||
                localBindings.BindingNodes.Contains(identifier) ||
                IsLocallyDeclared(identifier.Name, node, root, parents, localBindings.ByScope))
            {
                continue;
            }

            if (IsReference(identifier, parent))
            {
                return true;
            }
        }

        return false;
    }

    private static LocalBindings FindLocalDeclarations(
        Node root,
        IReadOnlyDictionary<Node, Node?> parents)
    {
        var byScope = new Dictionary<Node, HashSet<string>>(ReferenceEqualityComparer.Instance);
        var bindingNodes = new HashSet<Node>(ReferenceEqualityComparer.Instance);
        foreach (var (node, parent) in parents)
        {
            switch (node)
            {
                case VariableDeclarator declaration when parent is VariableDeclaration variableDeclaration:
                    {
                        var scope = variableDeclaration.Kind == VariableDeclarationKind.Var
                            ? FindNearestFunctionScope(parent, root, parents)
                            : FindNearestLexicalScope(parent, root, parents);
                        AddPatternBindings(declaration.Id, scope, byScope, bindingNodes);
                        break;
                    }
                case FunctionDeclaration declaration:
                    if (declaration.Id is not null)
                    {
                        AddPatternBindings(declaration.Id, FindNearestLexicalScope(parent, root, parents), byScope, bindingNodes);
                    }

                    AddParameterBindings(declaration.Params, declaration, byScope, bindingNodes);
                    break;
                case FunctionExpression declaration:
                    if (declaration.Id is not null)
                    {
                        AddPatternBindings(declaration.Id, declaration, byScope, bindingNodes);
                    }

                    AddParameterBindings(declaration.Params, declaration, byScope, bindingNodes);
                    break;
                case ArrowFunctionExpression declaration:
                    AddParameterBindings(declaration.Params, declaration, byScope, bindingNodes);
                    break;
                case CatchClause declaration when declaration.Param is not null:
                    AddPatternBindings(declaration.Param, declaration, byScope, bindingNodes);
                    break;
                case ClassDeclaration declaration:
                    if (declaration.Id is not null)
                    {
                        AddPatternBindings(declaration.Id, FindNearestLexicalScope(parent, root, parents), byScope, bindingNodes);
                    }

                    break;
                case ClassExpression declaration:
                    if (declaration.Id is not null)
                    {
                        AddPatternBindings(declaration.Id, declaration, byScope, bindingNodes);
                    }

                    break;
            }
        }

        return new LocalBindings(byScope, bindingNodes);
    }

    private static void AddParameterBindings(
        IEnumerable<Node> parameters,
        Node scope,
        IDictionary<Node, HashSet<string>> byScope,
        ISet<Node> bindingNodes)
    {
        foreach (var parameter in parameters)
        {
            AddPatternBindings(parameter, scope, byScope, bindingNodes);
        }
    }

    private static void AddPatternBindings(
        Node pattern,
        Node scope,
        IDictionary<Node, HashSet<string>> byScope,
        ISet<Node> bindingNodes)
    {
        switch (pattern)
        {
            case Identifier identifier:
                if (!byScope.TryGetValue(scope, out var names))
                {
                    names = new HashSet<string>(StringComparer.Ordinal);
                    byScope.Add(scope, names);
                }

                names.Add(identifier.Name);
                bindingNodes.Add(identifier);
                break;
            case AssignmentPattern assignment:
                AddPatternBindings(assignment.Left, scope, byScope, bindingNodes);
                break;
            case RestElement rest:
                AddPatternBindings(rest.Argument, scope, byScope, bindingNodes);
                break;
            case ArrayPattern array:
                foreach (var element in array.Elements)
                {
                    if (element is not null)
                    {
                        AddPatternBindings(element, scope, byScope, bindingNodes);
                    }
                }

                break;
            case ObjectPattern objectPattern:
                foreach (var property in objectPattern.Properties)
                {
                    switch (property)
                    {
                        case Property objectProperty:
                            AddPatternBindings(objectProperty.Value, scope, byScope, bindingNodes);
                            break;
                        case RestElement objectRest:
                            AddPatternBindings(objectRest.Argument, scope, byScope, bindingNodes);
                            break;
                    }
                }

                break;
        }
    }

    private static bool IsLocallyDeclared(
        string name,
        Node reference,
        Node root,
        IReadOnlyDictionary<Node, Node?> parents,
        IReadOnlyDictionary<Node, HashSet<string>> declarations)
    {
        for (Node? scope = reference; scope is not null; scope = parents[scope])
        {
            if (IsLexicalScope(scope) &&
                IsReferenceWithinScope(reference, scope, parents) &&
                declarations.TryGetValue(scope, out var names) &&
                names.Contains(name))
            {
                return true;
            }
        }

        return declarations.TryGetValue(root, out var rootNames) && rootNames.Contains(name);
    }

    private static Node FindNearestFunctionScope(
        Node? node,
        Node root,
        IReadOnlyDictionary<Node, Node?> parents)
    {
        for (var current = node; current is not null; current = parents[current])
        {
            if (IsFunctionScope(current))
            {
                return current;
            }
        }

        return root;
    }

    private static Node FindNearestLexicalScope(
        Node? node,
        Node root,
        IReadOnlyDictionary<Node, Node?> parents)
    {
        for (var current = node; current is not null; current = parents[current])
        {
            if (IsLexicalScope(current))
            {
                return current;
            }
        }

        return root;
    }

    private static bool IsFunctionScope(Node node) =>
        node is FunctionDeclaration or FunctionExpression or ArrowFunctionExpression or StaticBlock;

    private static bool IsLexicalScope(Node node) =>
        node is BlockStatement or
            FunctionDeclaration or FunctionExpression or ArrowFunctionExpression or
            ClassExpression or StaticBlock or CatchClause or
            ForStatement or ForInStatement or ForOfStatement or SwitchStatement;

    private static bool IsReferenceWithinScope(
        Node reference,
        Node scope,
        IReadOnlyDictionary<Node, Node?> parents)
    {
        if (scope is SwitchStatement switchStatement &&
            IsDescendantOf(reference, switchStatement.Discriminant, parents))
        {
            return false;
        }

        if (scope is ForInStatement forInStatement &&
            IsDescendantOf(reference, forInStatement.Right, parents))
        {
            return false;
        }

        return scope is not ForOfStatement forOfStatement ||
            !IsDescendantOf(reference, forOfStatement.Right, parents);
    }

    private static bool IsDescendantOf(
        Node node,
        Node ancestor,
        IReadOnlyDictionary<Node, Node?> parents)
    {
        for (Node? current = node; current is not null; current = parents[current])
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReference(
        Identifier identifier,
        Node? parent)
    {
        if (parent is MemberExpression member)
        {
            if (ReferenceEquals(member.Property, identifier) && !member.Computed)
            {
                return false;
            }

            if (ReferenceEquals(member.Object, identifier))
            {
                return true;
            }
        }

        if (parent is Property property && ReferenceEquals(property.Key, identifier) && !property.Computed)
        {
            if (!property.Shorthand)
            {
                return false;
            }

        }

        if (parent is MethodDefinition methodDefinition &&
                ReferenceEquals(methodDefinition.Key, identifier) && !methodDefinition.Computed ||
            parent is ClassProperty classProperty &&
                ReferenceEquals(classProperty.Key, identifier) && !classProperty.Computed)
        {
            return false;
        }

        if (parent is LabeledStatement labeledStatement && ReferenceEquals(labeledStatement.Label, identifier) ||
            parent is BreakStatement breakStatement && ReferenceEquals(breakStatement.Label, identifier) ||
            parent is ContinueStatement continueStatement && ReferenceEquals(continueStatement.Label, identifier))
        {
            return false;
        }

        if (parent is FunctionDeclaration functionDeclaration && ReferenceEquals(functionDeclaration.Id, identifier) ||
            parent is FunctionExpression functionExpression && ReferenceEquals(functionExpression.Id, identifier) ||
            parent is ClassDeclaration classDeclaration && ReferenceEquals(classDeclaration.Id, identifier) ||
            parent is ClassExpression classExpression && ReferenceEquals(classExpression.Id, identifier) ||
            parent is VariableDeclarator variableDeclarator && ReferenceEquals(variableDeclarator.Id, identifier) ||
            parent is ArrowFunctionExpression arrowFunction && arrowFunction.Params.Any(parameter => ReferenceEquals(parameter, identifier)) ||
            parent is FunctionDeclaration function && function.Params.Any(parameter => ReferenceEquals(parameter, identifier)) ||
            parent is FunctionExpression expression && expression.Params.Any(parameter => ReferenceEquals(parameter, identifier)))
        {
            return false;
        }

        return true;
    }

    private static bool TryMigrateBody(
        string? body,
        bool structuredBody,
        string method,
        Dictionary<string, string> headers,
        out string? bodyTemplate)
    {
        bodyTemplate = string.IsNullOrWhiteSpace(body) ? null : body;
        if (bodyTemplate is null)
        {
            return true;
        }

        var hasContentType = headers.TryGetValue("Content-Type", out var contentType);
        if (structuredBody)
        {
            if (!TryParseTemplateJson(bodyTemplate, out var rootKind))
            {
                return false;
            }

            if (rootKind == JsonValueKind.Null &&
                !bodyTemplate.Contains("{{", StringComparison.Ordinal) &&
                bodyTemplate.Trim().Equals("null", StringComparison.Ordinal))
            {
                bodyTemplate = null;
                return true;
            }

            if (method == "GET" || (hasContentType && IsFormContentType(contentType)))
            {
                return false;
            }

            if (rootKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                if (!hasContentType)
                {
                    headers["Content-Type"] = "application/json";
                }

                return true;
            }

            return rootKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False;
        }

        if (method != "POST" || (hasContentType && IsFormContentType(contentType)))
        {
            return false;
        }

        return !hasContentType || !IsJsonContentType(contentType) || TryParseTemplateJson(bodyTemplate, out _);
    }

    private static bool TryParseRateLimit(string? value, out ProviderRequestRateLimit? rateLimit)
    {
        rateLimit = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalized = value.Trim();
        if (long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval))
        {
            if (interval is <= 0 or > int.MaxValue)
            {
                return false;
            }

            rateLimit = new ProviderRequestRateLimit(1, (int)interval);
            return true;
        }

        var parts = normalized.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxRequests) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var window) ||
            maxRequests <= 0 || window <= 0)
        {
            return false;
        }

        rateLimit = new ProviderRequestRateLimit(maxRequests, window);
        return true;
    }

    private static bool IsFormContentType(string? value) =>
        value?.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsJsonContentType(string? value) =>
        value?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true;

    private static string MakeUniqueName(string sourceName, HashSet<string> usedNames)
    {
        if (usedNames.Add(sourceName))
        {
            return sourceName;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{sourceName} ({suffix})";
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool TryParseTemplateJson(string body, out JsonValueKind rootKind)
    {
        rootKind = JsonValueKind.Undefined;
        try
        {
            var template = NormalizedTemplate.Parse(body);
            var jsonParts = new List<string>(template.Segments.Count);
            var expressionMarkers = new HashSet<string>(StringComparer.Ordinal);
            var markerIndex = 0;
            foreach (var segment in template.Segments)
            {
                switch (segment)
                {
                    case LiteralTemplateSegment literal:
                        jsonParts.Add(literal.Text);
                        break;
                    case ExpressionTemplateSegment expressionSegment:
                        Node expression;
                        try
                        {
                            expression = new Parser().ParseExpression(expressionSegment.Expression);
                        }
                        catch (ParseErrorException)
                        {
                            return false;
                        }

                        if (!IsJsonSafeResultExpression(expression))
                        {
                            return false;
                        }

                        var marker = $"__NovelSpeakerJsonExpression{markerIndex++}__";
                        while (body.Contains(marker, StringComparison.Ordinal) || !expressionMarkers.Add(marker))
                        {
                            marker += "_";
                        }

                        jsonParts.Add(JsonSerializer.Serialize(marker));
                        break;
                }
            }

            var json = string.Concat(jsonParts);
            using var document = JsonDocument.Parse(json);
            var foundMarkers = new HashSet<string>(StringComparer.Ordinal);
            if (!ContainsJsonExpressionValues(document.RootElement, expressionMarkers, foundMarkers) ||
                !expressionMarkers.SetEquals(foundMarkers))
            {
                return false;
            }

            rootKind = document.RootElement.ValueKind;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            return false;
        }
    }

    private static bool IsJsonSafeResultExpression(Node expression) => expression switch
    {
        NumericLiteral numeric => double.IsFinite(numeric.Value),
        BooleanLiteral => true,
        Identifier identifier => identifier.Name == "speakSpeed",
        StringLiteral literal => IsJsonDocument(literal.Value),
        ObjectExpression objectExpression => IsJsonSerializableExpression(objectExpression),
        ArrayExpression arrayExpression => IsJsonSerializableExpression(arrayExpression),
        CallExpression call => IsJsonStringifyCall(call, out var argument) && IsJsonSerializableExpression(argument),
        _ => false
    };

    private static bool IsJsonSerializableExpression(Node expression) => expression switch
    {
        StringLiteral or NumericLiteral or BooleanLiteral or NullLiteral => true,
        Identifier identifier => identifier.Name is "speakText" or "speakSpeed",
        ObjectExpression objectExpression => objectExpression.Properties.All(property =>
            property is Property { Computed: false, Method: false } objectProperty &&
            IsJsonSerializableExpression(objectProperty.Value)),
        ArrayExpression arrayExpression => arrayExpression.Elements.All(element =>
            element is null || element is not SpreadElement && IsJsonSerializableExpression(element)),
        CallExpression call => IsJsonStringifyCall(call, out var argument) && IsJsonSerializableExpression(argument),
        _ => false
    };

    private static bool IsJsonStringifyCall(CallExpression call, out Node argument)
    {
        argument = null!;
        if (call.Callee is not MemberExpression
            {
                Computed: false,
                Object: Identifier { Name: "JSON" },
                Property: Identifier { Name: "stringify" }
            } ||
            call.Arguments.Count != 1 ||
            call.Arguments[0] is SpreadElement)
        {
            return false;
        }

        argument = call.Arguments[0];
        return true;
    }

    private static bool IsJsonDocument(string value)
    {
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ContainsJsonExpressionValues(
        JsonElement element,
        IReadOnlySet<string> expectedMarkers,
        ISet<string> foundMarkers)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (expectedMarkers.Contains(property.Name) ||
                        !ContainsJsonExpressionValues(property.Value, expectedMarkers, foundMarkers))
                    {
                        return false;
                    }
                }

                return true;
            case JsonValueKind.Array:
                return element.EnumerateArray()
                    .All(item => ContainsJsonExpressionValues(item, expectedMarkers, foundMarkers));
            case JsonValueKind.String:
                var value = element.GetString();
                if (value is not null && expectedMarkers.Contains(value))
                {
                    return foundMarkers.Add(value);
                }

                return value is null || !expectedMarkers.Any(value.Contains);
            default:
                return true;
        }
    }

    private static async Task InsertProviderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SpeechProviderInstance provider,
        CancellationToken cancellationToken)
    {
        var configuration = (HttpSpeechProviderConfiguration)provider.Configuration;
        await using (var providerCommand = connection.CreateCommand())
        {
            providerCommand.Transaction = transaction;
            providerCommand.CommandText =
                """
                INSERT INTO SpeechProviders (Id, Type, Name, NameKey, SortOrder, CreatedAt, UpdatedAt)
                VALUES ($id, $type, $name, $nameKey, $sortOrder, $createdAt, $updatedAt);
                """;
            providerCommand.Parameters.AddWithValue("$id", provider.Id.ToString());
            providerCommand.Parameters.AddWithValue("$type", (int)provider.Type);
            providerCommand.Parameters.AddWithValue("$name", provider.Name);
            providerCommand.Parameters.AddWithValue("$nameKey", provider.Name.ToUpperInvariant());
            providerCommand.Parameters.AddWithValue("$sortOrder", provider.SortOrder);
            providerCommand.Parameters.AddWithValue("$createdAt", SqliteDateTimeMapper.Format(provider.CreatedAt));
            providerCommand.Parameters.AddWithValue("$updatedAt", SqliteDateTimeMapper.Format(provider.UpdatedAt));
            await providerCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var configCommand = connection.CreateCommand();
        configCommand.Transaction = transaction;
        configCommand.CommandText =
            """
            INSERT INTO HttpSpeechProviderConfigs (
                ProviderId, UrlTemplate, Method, HeadersJson, BodyTemplate, MaxRequests, WindowMilliseconds)
            VALUES ($providerId, $urlTemplate, $method, $headersJson, $bodyTemplate, $maxRequests, $windowMilliseconds);
            """;
        configCommand.Parameters.AddWithValue("$providerId", provider.Id.ToString());
        configCommand.Parameters.AddWithValue("$urlTemplate", configuration.UrlTemplate);
        configCommand.Parameters.AddWithValue("$method", configuration.Method);
        configCommand.Parameters.AddWithValue("$headersJson", JsonSerializer.Serialize(configuration.Headers));
        configCommand.Parameters.AddWithValue("$bodyTemplate", (object?)configuration.BodyTemplate ?? DBNull.Value);
        configCommand.Parameters.AddWithValue("$maxRequests", (object?)configuration.RateLimit?.MaxRequests ?? DBNull.Value);
        configCommand.Parameters.AddWithValue("$windowMilliseconds", (object?)configuration.RateLimit?.WindowMilliseconds ?? DBNull.Value);
        await configCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record LegacyHttpRule(
        long Id,
        string? Name,
        string? Url,
        string? ConcurrentRate,
        string? Header,
        string? RequestOptionsJson,
        bool IsEnabled);

    private sealed record ConvertedRule(string Name, HttpSpeechProviderConfiguration Configuration);

    private sealed record LocalBindings(
        IReadOnlyDictionary<Node, HashSet<string>> ByScope,
        ISet<Node> BindingNodes);
}
