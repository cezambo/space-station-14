using Cognition.Core.Perception;

namespace Cognition.Sandbox;

public sealed partial class SandboxWorld
{
    private bool InReach(Agent a, Cell c) =>
        Distance(a.Cell, c) <= _config.Perception.BandWithinReach && Grid.LineOfSight(a.Cell, c);

    private bool FreeHand(Agent a) => a.Held.Count < Agent.Hands;

    /// <summary>RJ-05: only actions that would succeed right now.</summary>
    public ActionAffordances GetAffordances(string agentGuid)
    {
        var a = AgentByGuid(agentGuid);
        if (a.Asleep || !a.Conscious)
            return new ActionAffordances([], [], []);

        var actions = new List<Affordance>();
        var people = _agents.Where(o => !ReferenceEquals(o, a)).ToList();
        var reachablePeople = people.Where(o => InReach(a, o.Cell)).ToList();

        if (FreeHand(a))
        {
            actions.AddRange(_items.Where(i => i.Cell is { } c && InReach(a, c))
                .Select(i => new Affordance(ActionVerb.Pickup, ItemRef: i.Ref)));
        }

        foreach (var item in a.Held)
        {
            actions.Add(new Affordance(ActionVerb.Drop, ItemRef: item.Ref));
            switch (item.Kind)
            {
                case ItemKind.Food:
                    actions.Add(new Affordance(ActionVerb.Eat, ItemRef: item.Ref));
                    break;
                case ItemKind.Drink:
                    actions.Add(new Affordance(ActionVerb.Drink, ItemRef: item.Ref));
                    break;
                case ItemKind.Medkit:
                    actions.Add(new Affordance(ActionVerb.Use, a.Ref, item.Ref));
                    actions.AddRange(reachablePeople.Select(o => new Affordance(ActionVerb.Use, o.Ref, item.Ref)));
                    break;
                case ItemKind.Extinguisher or ItemKind.Tool:
                    actions.Add(new Affordance(ActionVerb.Use, ItemRef: item.Ref));
                    break;
            }

            actions.AddRange(reachablePeople.Where(o => !o.Asleep && o.Conscious && FreeHand(o))
                .Select(o => new Affordance(ActionVerb.Give, o.Ref, item.Ref)));
        }

        foreach (var door in _doors.Values.Where(d => InReach(a, d.Cell)))
        {
            var key = a.Held.FirstOrDefault(i => i.Kind == ItemKind.Key && i.KeyId == door.LockId);
            switch (door.State)
            {
                case DoorState.Open when door.Cell != a.Cell && !_agents.Any(o => o.Cell == door.Cell):
                    actions.Add(new Affordance(ActionVerb.Close, door.Ref));
                    break;
                case DoorState.Closed:
                    actions.Add(new Affordance(ActionVerb.Open, door.Ref));
                    if (key is not null)
                        actions.Add(new Affordance(ActionVerb.Lock, door.Ref, key.Ref));
                    break;
                case DoorState.Locked when key is not null:
                    actions.Add(new Affordance(ActionVerb.Unlock, door.Ref, key.Ref));
                    break;
            }
        }

        foreach (var box in _containers.Values.Where(c => InReach(a, c.Cell)))
        {
            actions.Add(new Affordance(box.IsOpen ? ActionVerb.Close : ActionVerb.Open, box.Ref));
            if (!box.IsOpen)
                continue;
            actions.AddRange(a.Held.Select(i => new Affordance(ActionVerb.Put, box.Ref, i.Ref)));
            if (FreeHand(a))
                actions.AddRange(box.Contents.Select(i => new Affordance(ActionVerb.Take, box.Ref, i.Ref)));
        }

        actions.Add(new Affordance(ActionVerb.Sleep, _beds.GetValueOrDefault(a.Cell)?.Ref));
        actions.AddRange(reachablePeople.Where(o => o.Asleep && o.Conscious).Select(o => new Affordance(ActionVerb.Wake, o.Ref)));
        actions.Add(new Affordance(ActionVerb.Speak));
        actions.AddRange(people.Where(o => o.Conscious && !o.Asleep && CanSee(a, o.Cell))
            .Select(o => new Affordance(ActionVerb.Speak, o.Ref)));

        var destinations = new List<KnownDestination>();
        foreach (var r in a.SeenRefs.Order(StringComparer.Ordinal))
        {
            if (Entity(r) is not (Bed or Container or Door) || DestinationCell(a, r) is null)
                continue;
            var e = Entity(r)!;
            destinations.Add(new KnownDestination(r, e.Name, Center(CellOf(e)), e is Bed));
        }

        var open = Directions.Where(d => CanStep(a.Cell, a.Cell.Offset(d.Dx, d.Dy))).Select(d => d.Dir).ToList();
        return new ActionAffordances(actions, destinations, open);
    }

    private static Cell CellOf(Entity e) => e switch
    {
        Bed b => b.Cell,
        Door d => d.Cell,
        Container c => c.Cell,
        Agent a => a.Cell,
        Item { Cell: { } c } => c,
        Item { In: { } box } => box.Cell,
        Item { HeldBy: { } h } => h.Cell,
        _ => throw new ArgumentException($"'{e.Ref}' has no position"),
    };

    /// <summary>Path to a destination: onto a bed or open door; next to (within reach of) anything else.</summary>
    private Queue<Cell>? DestinationCell(Agent a, string @ref)
    {
        var e = Entity(@ref);
        if (e is null)
            return null;
        var target = CellOf(e);
        return e switch
        {
            Bed or Door { State: DoorState.Open } => PathTo(a.Cell, c => c == target),
            _ => PathTo(a.Cell, c => Distance(c, target) <= _config.Perception.BandWithinReach && Grid.LineOfSight(c, target)),
        };
    }

    private void Start(Agent a, ActionIntent intent)
    {
        if (a.Asleep || !a.Conscious)
        {
            Failed(a, intent, a.Asleep ? "asleep" : "unconscious");
            return;
        }

        a.Moving = null;
        if (intent.Verb == ActionVerb.Move)
        {
            StartMove(a, intent);
            return;
        }

        if (intent.Verb == ActionVerb.Speak)
        {
            if (string.IsNullOrWhiteSpace(intent.Text))
            {
                Failed(a, intent, "no_text");
                return;
            }

            Speak(a, intent);
            Completed(a, intent);
            return;
        }

        var affordance = new Affordance(intent.Verb, intent.TargetRef, intent.ItemRef);
        if (!GetAffordances(Key(a.Guid)).Actions.Contains(affordance))
        {
            Failed(a, intent, "not_possible");
            return;
        }

        Execute(a, intent);
        Completed(a, intent);
    }

    private void StartMove(Agent a, ActionIntent intent)
    {
        Queue<Cell>? path;
        if (intent.DestinationRef is { } dest)
        {
            path = DestinationCell(a, dest);
        }
        else if (intent.Direction is { } dir)
        {
            var (_, dx, dy) = Directions[(int)dir];
            var steps = intent.Extent switch
            {
                MoveExtent.OneStep => 1,
                MoveExtent.Short => 3,
                MoveExtent.Medium => 8,
                _ => Grid.Width + Grid.Height,
            };
            path = new Queue<Cell>();
            var c = a.Cell;
            for (var i = 0; i < steps && CanStep(c, c.Offset(dx, dy)); i++)
            {
                c = c.Offset(dx, dy);
                path.Enqueue(c);
            }

            if (path.Count == 0)
                path = null;
        }
        else
        {
            path = null;
        }

        if (path is null)
        {
            Failed(a, intent, "no_path");
            return;
        }

        if (path.Count == 0)
        {
            Completed(a, intent);
            return;
        }

        a.Moving = new Movement { Path = path, Intent = intent };
    }

    private void Execute(Agent a, ActionIntent intent)
    {
        var item = intent.ItemRef is { } ir ? (Item)Entity(ir)! : null;
        var target = intent.TargetRef is { } tr ? Entity(tr) : null;
        switch (intent.Verb)
        {
            case ActionVerb.Pickup:
                item!.Cell = null;
                Hold(a, item);
                break;
            case ActionVerb.Drop:
                Release(a, item!);
                item!.Cell = a.Cell;
                break;
            case ActionVerb.Use when item!.Kind == ItemKind.Medkit:
                Heal((Agent)target!);
                break;
            case ActionVerb.Use:
                break;
            case ActionVerb.Open when target is Door d:
                SetDoor(d, DoorState.Open);
                break;
            case ActionVerb.Close when target is Door d:
                SetDoor(d, DoorState.Closed);
                break;
            case ActionVerb.Lock:
                SetDoor((Door)target!, DoorState.Locked);
                break;
            case ActionVerb.Unlock:
                SetDoor((Door)target!, DoorState.Closed);
                break;
            case ActionVerb.Open when target is Container c:
                c.IsOpen = true;
                break;
            case ActionVerb.Close when target is Container c:
                c.IsOpen = false;
                break;
            case ActionVerb.Put:
                Release(a, item!);
                item!.In = (Container)target!;
                item.In.Contents.Add(item);
                break;
            case ActionVerb.Take:
                ((Container)target!).Contents.Remove(item!);
                item!.In = null;
                Hold(a, item);
                break;
            case ActionVerb.Eat:
                a.Hunger = Math.Max(0, a.Hunger - Options.EatRelief);
                Consume(a, item!);
                break;
            case ActionVerb.Drink:
                a.Thirst = Math.Max(0, a.Thirst - Options.DrinkRelief);
                Consume(a, item!);
                break;
            case ActionVerb.Sleep:
                FallAsleep(a, "voluntary");
                break;
            case ActionVerb.Wake:
                WakeUp((Agent)target!, "woken");
                break;
            case ActionVerb.Give:
                Release(a, item!);
                Hold((Agent)target!, item!);
                break;
        }
    }

    private static void Hold(Agent a, Item item)
    {
        item.HeldBy = a;
        a.Held.Add(item);
    }

    private static void Release(Agent a, Item item)
    {
        a.Held.Remove(item);
        item.HeldBy = null;
    }

    private void Consume(Agent a, Item item)
    {
        Release(a, item);
        _items.Remove(item);
        _entities.Remove(item.Ref);
    }

    private void Heal(Agent patient)
    {
        var left = Options.MedkitHeal;
        foreach (var type in patient.Damage.OrderByDescending(d => d.Value).Select(d => d.Key).ToList())
        {
            var healed = Math.Min(left, patient.Damage[type]);
            patient.Damage[type] -= healed;
            left -= healed;
            if (patient.Damage[type] <= 0)
                patient.Damage.Remove(type);
            if (left <= 0)
                break;
        }

        patient.Bleeding = 0;
    }
}
