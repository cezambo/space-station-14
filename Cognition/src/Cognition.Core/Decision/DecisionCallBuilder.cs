using System.Text;
using Cognition.Core.Config;
using Cognition.Core.Perception;
using Cognition.Core.Prompts;
using Cognition.Core.Providers;

namespace Cognition.Core.Decision;

public enum ActionCategory
{
    Nothing,
    Move,
    Interact,
    UseItem,
    Inventory,
    Speak,
    Think,
    Sleep,
}

/// <summary>
/// What the menus are built from (T1.15). <see cref="Labels"/> (from <see cref="PerceptionFormatter.Labels"/>)
/// must cover every ref an option needs; an option whose ref has no label is left out with a warning (RJ-16).
/// </summary>
public sealed record MenuInput(
    ActionAffordances Affordances,
    IReadOnlyDictionary<string, EntityLabel> Labels,
    Vec2 Self,
    NeedBand FatigueBand,
    ThinkModes Affordable,
    bool HasImmediateGoal,
    int DecisionsSinceEmotionCheck);

/// <summary>
/// One option of one menu: the short key Jev answers with (RJ-20), its description, and what it means.
/// </summary>
public sealed record MenuOption(string Key, string Criteria)
{
    public ActionCategory? Category { get; init; }
    public Affordance? Action { get; init; }
    public string? DestinationRef { get; init; }
    public Compass? Direction { get; init; }
    public string? ListenerRef { get; init; }
    public bool Everyone { get; init; }
    public ThinkModes Think { get; init; }
    public string? BedRef { get; init; }
}

/// <summary>
/// The menus for one decision. <see cref="Asked"/> holds the dynamic Choice menus sent to Jev (2+ options);
/// <see cref="Fixed"/> the menus with a single possible option, which are not asked. <see cref="Included"/> is
/// every question id to send. <see cref="NothingToDecide"/>: only "keep doing the current action" is possible.
/// </summary>
public sealed record DecisionMenus(
    IReadOnlyDictionary<string, IReadOnlyList<MenuOption>> Asked,
    IReadOnlyDictionary<string, MenuOption> Fixed,
    IReadOnlySet<string> Included,
    IReadOnlyList<string> Warnings)
{
    public bool NothingToDecide => !Asked.ContainsKey(DecisionQuestions.ActionCategory);

    public IReadOnlyList<string> Oversized(int max) =>
        Asked.Where(kv => kv.Value.Count > max).Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList();
}

public static class DecisionQuestions
{
    public const string ActionCategory = "action_category";
    public const string MoveTarget = "move_target";
    public const string MoveDirection = "move_direction";
    public const string MoveExtent = "move_extent";
    public const string InteractTarget = "interact_target";
    public const string UseItem = "use_item";
    public const string InventoryAction = "inventory_action";
    public const string SpeakTarget = "speak_target";
    public const string SpeakIntent = "speak_intent";
    public const string ThinkMode = "think_mode";
    public const string SleepWhere = "sleep_where";
    public const string GoalBlocked = "goal_blocked";
    public const string Emotion = "emotion";

    public const string NoneOfThese = "none_of_these";
    public const string Everyone = "everyone";
    public const string Here = "here";

    /// <summary>The criteria placeholder of each dynamic menu in <c>decision.yaml</c>.</summary>
    public static readonly IReadOnlyDictionary<string, string> Placeholder = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ActionCategory] = "category_options",
        [MoveTarget] = "move_target_options",
        [MoveDirection] = "direction_options",
        [InteractTarget] = "interaction_options",
        [UseItem] = "item_use_options",
        [InventoryAction] = "inventory_options",
        [SpeakTarget] = "listener_options",
        [ThinkMode] = "think_options",
        [SleepWhere] = "sleep_options",
    };

    public static ActionCategory? CategoryOf(ActionVerb verb) => verb switch
    {
        ActionVerb.Open or ActionVerb.Close or ActionVerb.Lock or ActionVerb.Unlock or ActionVerb.Wake => Decision.ActionCategory.Interact,
        ActionVerb.Use or ActionVerb.Eat or ActionVerb.Drink => Decision.ActionCategory.UseItem,
        ActionVerb.Pickup or ActionVerb.Drop or ActionVerb.Put or ActionVerb.Take or ActionVerb.Give => Decision.ActionCategory.Inventory,
        _ => null,
    };

    public static string MenuOf(ActionCategory category) => category switch
    {
        Decision.ActionCategory.Interact => InteractTarget,
        Decision.ActionCategory.UseItem => UseItem,
        Decision.ActionCategory.Inventory => InventoryAction,
        Decision.ActionCategory.Speak => SpeakTarget,
        Decision.ActionCategory.Think => ThinkMode,
        Decision.ActionCategory.Sleep => SleepWhere,
        _ => MoveTarget,
    };
}

/// <summary>
/// Builds the decision menus from affordances only (T1.15, RJ-05) and renders the Jev request. Option keys are
/// short and unique, descriptions come from <c>vocabulary.yaml</c> <c>menu</c> (RJ-16, RJ-20), sub-menu
/// instructions in <c>decision.yaml</c> are conditional (RJ-17). Menus over 255 options go through
/// <see cref="ShortlistAsync"/> first (RJ-04).
/// </summary>
public sealed class DecisionCallBuilder
{
    public const int MaxChoiceOptions = 255;
    private const int MaxRefChars = 16;

    private static readonly string[] Bands = ["ok", "mild", "strong", "critical"];

    private readonly JevTemplate _decision;
    private readonly JevTemplate _shortlist;
    private readonly DecisionConfig _config;
    private readonly PerceptionConfig _perception;
    private readonly Vocabulary _v;
    private readonly Dictionary<string, IncludeCondition> _conditions = new(StringComparer.Ordinal);

    public DecisionCallBuilder(PromptLibrary prompts, CognitionConfig config)
    {
        _decision = prompts.Jev("decision");
        _shortlist = prompts.Jev("menu_shortlist");
        _config = config.Decision;
        _perception = config.Perception;
        _v = prompts.Vocabulary;

        var errors = new List<string>();
        var known = Variables(new MenuInput(new ActionAffordances([], [], []), new Dictionary<string, EntityLabel>(), default,
            NeedBand.Ok, ThinkModes.None, false, 0), new Dictionary<string, IReadOnlyList<MenuOption>>()).Keys.ToHashSet();
        foreach (var q in _decision.Questions)
        {
            if (q.CriteriaPlaceholder is { } p && DecisionQuestions.Placeholder.GetValueOrDefault(q.Id) != p)
                errors.Add($"decision.{q.Id}: criteria placeholder '{p}' is not filled by the builder");
            if (q.IncludeWhen is null)
                continue;
            try
            {
                var c = IncludeCondition.Parse(q.IncludeWhen);
                errors.AddRange(c.Variables.Where(n => !known.Contains(n)).Order(StringComparer.Ordinal)
                    .Select(n => $"decision.{q.Id}: include_when uses unknown name '{n}'"));
                _conditions[q.Id] = c;
            }
            catch (IncludeConditionException ex)
            {
                errors.Add($"decision.{q.Id}: {ex.Message}");
            }
        }

        foreach (var id in DecisionQuestions.Placeholder.Keys.Where(id => _decision.Questions.All(q => q.Id != id)))
        {
            errors.Add($"decision.yaml: question '{id}' is missing");
        }

        if (errors.Count > 0)
            throw new PromptLoadException(errors);
    }

    // ------------------------------------------------------------------ menus

    public DecisionMenus Menus(MenuInput input, Func<string, bool>? likely = null)
    {
        var warnings = new List<string>();
        var menus = new Dictionary<string, IReadOnlyList<MenuOption>>(StringComparer.Ordinal)
        {
            [DecisionQuestions.MoveTarget] = MoveTargets(input),
            [DecisionQuestions.MoveDirection] = Directions(input),
            [DecisionQuestions.InteractTarget] = Actions(input, ActionCategory.Interact, warnings),
            [DecisionQuestions.UseItem] = Actions(input, ActionCategory.UseItem, warnings),
            [DecisionQuestions.InventoryAction] = Actions(input, ActionCategory.Inventory, warnings),
            [DecisionQuestions.SpeakTarget] = Listeners(input, warnings),
            [DecisionQuestions.ThinkMode] = ThinkOptions(input),
            [DecisionQuestions.SleepWhere] = input.FatigueBand >= NeedBand.Mild ? SleepOptions(input) : [],
        };
        menus[DecisionQuestions.ActionCategory] = Categories(menus);

        var vars = Variables(input, menus);
        var included = new HashSet<string>(StringComparer.Ordinal);
        foreach (var q in _decision.Questions)
        {
            var wanted = !_conditions.TryGetValue(q.Id, out var c) || c.Evaluate(vars, likely ?? (_ => true));
            var menuOk = !menus.TryGetValue(q.Id, out var options) || options.Count >= 2;
            var dependsOk = q.Id switch
            {
                DecisionQuestions.MoveExtent => menus[DecisionQuestions.MoveDirection].Count > 0,
                DecisionQuestions.SpeakIntent => menus[DecisionQuestions.SpeakTarget].Count > 0,
                _ => true,
            };
            if (wanted && menuOk && dependsOk)
                included.Add(q.Id);
        }

        if (!included.Contains(DecisionQuestions.ActionCategory))
            included.Clear();
        var asked = menus.Where(kv => included.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var fixedMenus = menus.Where(kv => kv.Value.Count == 1 && kv.Key != DecisionQuestions.ActionCategory)
            .ToDictionary(kv => kv.Key, kv => kv.Value[0], StringComparer.Ordinal);
        return new DecisionMenus(asked, fixedMenus, included, warnings);
    }

    private Dictionary<string, object> Variables(MenuInput input, IReadOnlyDictionary<string, IReadOnlyList<MenuOption>> menus)
    {
        bool Has(string id) => menus.TryGetValue(id, out var o) && o.Count > 0;
        var vars = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["fanout"] = Vocabulary.SnakeCase(_config.Fanout.ToString()),
            ["full"] = "full",
            ["reduced"] = "reduced",
            ["has_interactions"] = Has(DecisionQuestions.InteractTarget),
            ["has_item_uses"] = Has(DecisionQuestions.UseItem),
            ["has_inventory_actions"] = Has(DecisionQuestions.InventoryAction),
            ["has_listeners"] = Has(DecisionQuestions.SpeakTarget),
            ["budget_allows_any"] = input.Affordable != ThinkModes.None,
            ["fatigue_band"] = (int)input.FatigueBand,
            ["has_immediate_goal"] = input.HasImmediateGoal,
            ["decisions_since_emotion_check"] = input.DecisionsSinceEmotionCheck,
            ["emotion_every_n"] = _config.EmotionEveryN,
        };
        for (var i = 0; i < Bands.Length; i++)
        {
            vars[Bands[i]] = i;
        }

        return vars;
    }

    private List<MenuOption> Categories(Dictionary<string, IReadOnlyList<MenuOption>> menus)
    {
        var options = new List<MenuOption> { Category(ActionCategory.Nothing) };
        var moves = menus[DecisionQuestions.MoveTarget].Count(o => o.DestinationRef is not null) + menus[DecisionQuestions.MoveDirection].Count;
        if (moves > 0)
            options.Add(Category(ActionCategory.Move));
        foreach (var c in new[] { ActionCategory.Interact, ActionCategory.UseItem, ActionCategory.Inventory, ActionCategory.Speak,
                     ActionCategory.Think, ActionCategory.Sleep })
        {
            if (menus[DecisionQuestions.MenuOf(c)].Count > 0)
                options.Add(Category(c));
        }

        return options;
    }

    private MenuOption Category(ActionCategory c)
    {
        var key = Vocabulary.SnakeCase(c.ToString());
        return new MenuOption(key, _v.Menu("category_" + key)) { Category = c };
    }

    private string Position(Vec2 self, Vec2 at)
    {
        var dir = Categorizers.Direction(at.X - self.X, at.Y - self.Y);
        return dir is null
            ? _v.Phrase("here")
            : _v.Phrase("position", ("distance", _v.Word(Categorizers.Distance(self.DistanceTo(at), _perception))), ("direction", _v.Word(dir.Value)));
    }

    private string At(string text, string? position) => position is null ? text : _v.Menu("at", ("text", text), ("position", position));

    private List<MenuOption> MoveTargets(MenuInput input)
    {
        var options = input.Affordances.Destinations
            .OrderBy(d => input.Self.DistanceTo(d.Position)).ThenBy(d => d.Ref, StringComparer.Ordinal)
            .Select(d => new MenuOption("go_" + Part(d.Ref), At(_v.Menu("walk_to", ("target", d.Name)), Position(input.Self, d.Position)))
            {
                DestinationRef = d.Ref,
            })
            .ToList();
        if (options.Count > 0)
            options.Add(new MenuOption(DecisionQuestions.NoneOfThese, _v.Menu("none_of_these")));
        return Unique(options);
    }

    private List<MenuOption> Directions(MenuInput input) =>
        input.Affordances.OpenDirections
            .Select(d => new MenuOption(Vocabulary.SnakeCase(d.ToString()), _v.Menu("walk_direction", ("direction", _v.Word(d)))) { Direction = d })
            .ToList();

    private List<MenuOption> Actions(MenuInput input, ActionCategory category, List<string> warnings)
    {
        var options = new List<MenuOption>();
        foreach (var a in input.Affordances.Actions.Where(a => DecisionQuestions.CategoryOf(a.Verb) == category))
        {
            var verb = Vocabulary.SnakeCase(a.Verb.ToString());
            EntityLabel? target = null, item = null;
            if ((a.TargetRef is { } t && !input.Labels.TryGetValue(t, out target)) || (a.ItemRef is { } i && !input.Labels.TryGetValue(i, out item)))
            {
                warnings.Add($"{verb} {a.TargetRef} {a.ItemRef}: no label, option left out".Replace("  ", " ", StringComparison.Ordinal));
                continue;
            }

            var phrase = a.Verb == ActionVerb.Use && target is not null ? "use_on" : verb;
            var values = new List<(string, string)>();
            if (target is not null)
                values.Add(("target", target.Name));
            if (item is not null)
                values.Add(("item", item.Name));
            var text = _v.Menu(phrase, [.. values]);
            var where = target?.Position ?? item?.Position;
            var key = string.Join("_", new[] { verb, a.ItemRef, a.TargetRef }.Where(p => p is not null).Select(p => Part(p!)));
            options.Add(new MenuOption(key, At(text, where)) { Action = a });
        }

        return Unique(options);
    }

    private List<MenuOption> Listeners(MenuInput input, List<string> warnings)
    {
        var options = new List<MenuOption>();
        foreach (var a in input.Affordances.Actions.Where(a => a.Verb == ActionVerb.Speak && a.TargetRef is not null))
        {
            if (!input.Labels.TryGetValue(a.TargetRef!, out var who))
            {
                warnings.Add($"speak {a.TargetRef}: no label, option left out");
                continue;
            }

            options.Add(new MenuOption("to_" + Part(a.TargetRef!), At(_v.Menu("speak_to", ("target", who.Name)), who.Position))
            {
                ListenerRef = a.TargetRef,
            });
        }

        if (options.Count > 0)
            options.Add(new MenuOption(DecisionQuestions.Everyone, _v.Menu("speak_everyone")) { Everyone = true });
        return Unique(options);
    }

    private List<MenuOption> ThinkOptions(MenuInput input)
    {
        var options = new List<MenuOption>();
        if (input.Affordable.HasFlag(ThinkModes.Light))
            options.Add(new MenuOption("light", _v.Menu("think_light")) { Think = ThinkModes.Light });
        if (input.Affordable.HasFlag(ThinkModes.Deep))
            options.Add(new MenuOption("deep", _v.Menu("think_deep")) { Think = ThinkModes.Deep });
        return options;
    }

    private List<MenuOption> SleepOptions(MenuInput input)
    {
        var sleep = input.Affordances.Actions.FirstOrDefault(a => a.Verb == ActionVerb.Sleep);
        var options = new List<MenuOption>();
        if (sleep is not null)
        {
            options.Add(new MenuOption(DecisionQuestions.Here, _v.Menu(sleep.TargetRef is null ? "sleep_here" : "sleep_this_bed"))
            {
                Action = sleep,
                BedRef = sleep.TargetRef,
            });
        }

        var beds = input.Affordances.Destinations.Where(d => d.IsBed && d.Ref != sleep?.TargetRef)
            .OrderBy(d => input.Self.DistanceTo(d.Position)).ThenBy(d => d.Ref, StringComparer.Ordinal).ToList();
        for (var i = 0; i < beds.Count; i++)
        {
            var text = _v.Menu("sleep_bed", ("target", beds[i].Name));
            if (i == 0 && beds.Count > 1)
                text = _v.Menu("nearest", ("text", text));
            options.Add(new MenuOption("bed_" + Part(beds[i].Ref), At(text, Position(input.Self, beds[i].Position)))
            {
                BedRef = beds[i].Ref,
                DestinationRef = beds[i].Ref,
            });
        }

        return Unique(options);
    }

    private static string Part(string reference)
    {
        var sb = new StringBuilder();
        foreach (var ch in reference.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
                sb.Append(ch);
        }

        var s = sb.Length == 0 ? "x" : sb.ToString();
        return s.Length <= MaxRefChars ? s : s[..MaxRefChars];
    }

    private static List<MenuOption> Unique(List<MenuOption> options)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < options.Count; i++)
        {
            var key = options[i].Key;
            var n = seen[key] = seen.GetValueOrDefault(key) + 1;
            if (n > 1)
                options[i] = options[i] with { Key = $"{key}_{n}" };
        }

        return options;
    }

    // ------------------------------------------------------------------ rendering

    /// <summary>Characters the menus add to the call (for <see cref="ContextInput.MenuChars"/>); oversized menus count as shortlisted.</summary>
    public int MenuChars(DecisionMenus menus)
    {
        var total = 0;
        foreach (var q in _decision.Questions.Where(q => menus.Included.Contains(q.Id)))
        {
            total += q.Instructions.Length;
            IEnumerable<KeyValuePair<string, string>> options = menus.Asked.TryGetValue(q.Id, out var asked)
                ? asked.Take(_config.ShortlistTop > 0 && asked.Count > MaxChoiceOptions ? _config.ShortlistTop : asked.Count)
                    .Select(o => new KeyValuePair<string, string>(o.Key, o.Criteria))
                : q.Options ?? [];
            total += options.Sum(o => o.Key.Length + o.Value.Length);
            total += q.Levels?.Sum(l => l.Length) ?? 0;
        }

        return total;
    }

    /// <summary>The decision state text, identical in the shortlist and decision calls.</summary>
    public string State(AssembledContext context)
    {
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var state = Placeholders.Render(_decision.StateTemplate, context.Values, Placeholders.TagsIn(_decision.StateTemplate),
            new HashSet<string>(StringComparer.Ordinal), missing);
        if (missing.Count > 0)
            throw new PromptRenderException(_decision.Name, missing.ToList());
        return state;
    }

    public RenderedJev Render(AssembledContext context, DecisionMenus menus, string model)
    {
        var oversized = menus.Oversized(MaxChoiceOptions);
        if (oversized.Count > 0)
            throw new InvalidOperationException($"menus {string.Join(", ", oversized)} exceed {MaxChoiceOptions} options: shortlist first (RJ-04)");
        if (menus.NothingToDecide)
            throw new InvalidOperationException("nothing to decide: do not call Jev");
        var input = new JevRenderInput(context.Values)
        {
            ChoiceOptions = menus.Asked.ToDictionary(kv => DecisionQuestions.Placeholder[kv.Key],
                kv => (IReadOnlyList<KeyValuePair<string, string>>)kv.Value.Select(o => new KeyValuePair<string, string>(o.Key, o.Criteria)).ToList(),
                StringComparer.Ordinal),
            Include = q => menus.Included.Contains(q.Id),
        };
        return _decision.Render(model, input);
    }

    // ------------------------------------------------------------------ RJ-04 two stages

    /// <summary>Shortlist requests for one oversized menu: one Score question per option, in batches.</summary>
    public IReadOnlyList<JevRequest> ShortlistRequests(string state, IReadOnlyList<MenuOption> options, string model)
    {
        var requests = new List<JevRequest>();
        for (var start = 0; start < options.Count; start += _config.ShortlistBatch)
        {
            var batch = options.Skip(start).Take(_config.ShortlistBatch)
                .Select(o => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(StringComparer.Ordinal) { ["option"] = o.Criteria })
                .ToList();
            var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["decision_state"] = state };
            requests.Add(_shortlist.Render(model, new JevRenderInput(values) { RepeatItems = batch }).Request);
        }

        return requests;
    }

    /// <summary>
    /// The <c>shortlist_top</c> best-fitting options, in their original order. Scores are only compared, never
    /// combined (RJ-19); ties keep the earlier option. A missing answer ranks lowest.
    /// </summary>
    public IReadOnlyList<MenuOption> Shortlisted(IReadOnlyList<MenuOption> options, IReadOnlyList<JevResponse> responses)
    {
        var scored = options.Select((o, i) =>
        {
            var response = responses.ElementAtOrDefault(i / _config.ShortlistBatch);
            var answer = response?.Answers.GetValueOrDefault($"fit_{i % _config.ShortlistBatch}") as ScoreAnswer;
            return (Option: o, Index: i, Score: answer?.Score ?? double.NegativeInfinity);
        });
        return scored.OrderByDescending(x => x.Score).ThenBy(x => x.Index).Take(_config.ShortlistTop)
            .OrderBy(x => x.Index).Select(x => x.Option).ToList();
    }

    /// <summary>Runs the RJ-04 first stage for every oversized menu and returns menus that fit a Choice.</summary>
    public async Task<DecisionMenus> ShortlistAsync(DecisionMenus menus, string state, IJevClient jev, string model, CancellationToken ct)
    {
        var asked = new Dictionary<string, IReadOnlyList<MenuOption>>(menus.Asked, StringComparer.Ordinal);
        foreach (var id in menus.Oversized(MaxChoiceOptions))
        {
            var responses = new List<JevResponse>();
            foreach (var request in ShortlistRequests(state, asked[id], model))
            {
                responses.Add(await jev.EvaluateAsync(request, ct).ConfigureAwait(false));
            }

            asked[id] = Shortlisted(asked[id], responses);
        }

        return menus with { Asked = asked };
    }
}
