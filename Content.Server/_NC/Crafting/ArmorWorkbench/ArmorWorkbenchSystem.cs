using Content.Server.Popups;
using Content.Server.Power.EntitySystems;
using Content.Shared._NC.Armor.Components;
using Content.Shared._NC.Crafting.ArmorWorkbench;
using Content.Shared._NC.Crafting.ArmorWorkbench.Components;
using Content.Shared._NC.Crafting.ArmorWorkbench.Events;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using System.Linq;

namespace Content.Server._NC.Crafting.ArmorWorkbench;

/// <summary>
/// Server-side logic for the Night City armor crafting workbench.
/// </summary>
public sealed class ArmorWorkbenchSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ArmorWorkbenchComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ArmorWorkbenchComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<ArmorWorkbenchComponent, CanDropTargetEvent>(OnCanDropTarget);
        SubscribeLocalEvent<ArmorWorkbenchComponent, DragDropTargetEvent>(OnDragDropTarget);
        SubscribeLocalEvent<ArmorWorkbenchComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<ArmorWorkbenchComponent, EntInsertedIntoContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<ArmorWorkbenchComponent, EntRemovedFromContainerMessage>(OnContainerModified);
        SubscribeLocalEvent<ArmorWorkbenchComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<ArmorWorkbenchComponent, ArmorWorkbenchSelectMaterialMessage>(OnSelectMaterial);
        SubscribeLocalEvent<ArmorWorkbenchComponent, ArmorWorkbenchEjectRequestMessage>(OnEjectRequest);
        SubscribeLocalEvent<ArmorWorkbenchComponent, ArmorWorkbenchStartCraftMessage>(OnStartCraft);
        SubscribeLocalEvent<ArmorWorkbenchComponent, ArmorWorkbenchDoAfterEvent>(OnCraftDoAfter);
    }

    private void OnInit(EntityUid uid, ArmorWorkbenchComponent component, ComponentInit args)
    {
        component.Storage = _container.EnsureContainer<Container>(uid, ArmorWorkbenchComponent.StorageContainerId);
    }

    private void OnInteractUsing(EntityUid uid, ArmorWorkbenchComponent component, InteractUsingEvent args)
    {
        if (args.Handled || component.IsCrafting)
            return;

        if (!CanInsert(uid, component, args.Used))
            return;

        if (_container.Insert(args.Used, component.Storage))
        {
            args.Handled = true;
            _ui.TryOpenUi(uid, ArmorWorkbenchUiKey.Key, args.User);
            UpdateUserInterface(uid, component);
        }
    }

    private void OnCanDropTarget(EntityUid uid, ArmorWorkbenchComponent component, ref CanDropTargetEvent args)
    {
        if (args.Handled)
            return;

        args.CanDrop = !component.IsCrafting && CanAcceptItem(args.Dragged);
        args.Handled = true;
    }

    private void OnDragDropTarget(EntityUid uid, ArmorWorkbenchComponent component, ref DragDropTargetEvent args)
    {
        if (args.Handled || component.IsCrafting || !CanInsert(uid, component, args.Dragged))
            return;

        if (_container.Insert(args.Dragged, component.Storage))
        {
            args.Handled = true;
            _ui.TryOpenUi(uid, ArmorWorkbenchUiKey.Key, args.User);
            UpdateUserInterface(uid, component);
        }
    }

    private void OnUiOpened(EntityUid uid, ArmorWorkbenchComponent component, BoundUIOpenedEvent args)
    {
        UpdateUserInterface(uid, component);
    }

    private void OnContainerModified(EntityUid uid, ArmorWorkbenchComponent component, ContainerModifiedMessage args)
    {
        ValidateSelections(component);
        UpdateUserInterface(uid, component);
    }

    private void OnGetVerbs(EntityUid uid, ArmorWorkbenchComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || component.Storage.ContainedEntities.Count == 0 || component.IsCrafting)
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("armor-workbench-verb-eject"),
            Category = VerbCategory.Eject,
            Priority = 1,
            Act = () =>
            {
                foreach (var entity in component.Storage.ContainedEntities.ToArray())
                {
                    _container.Remove(entity, component.Storage);
                }

                component.SelectedBaseMaterial = null;
                component.SelectedSoftMaterial = null;
                component.SelectedHardMaterial = null;
                UpdateUserInterface(uid, component);
            }
        });
    }

    private void OnSelectMaterial(EntityUid uid, ArmorWorkbenchComponent component, ArmorWorkbenchSelectMaterialMessage args)
    {
        if (component.IsCrafting || !TryGetEntity(args.Material, out var materialUid))
            return;

        var material = materialUid.Value;

        if (!component.Storage.Contains(material) || !TryComp<ArmorMaterialComponent>(material, out var materialComp))
            return;

        if (args.LayerType == ArmorWorkbenchLayerSlot.Base && SupportsBase(materialComp))
            component.SelectedBaseMaterial = material;
        else if (args.LayerType == ArmorWorkbenchLayerSlot.Soft && SupportsSoft(materialComp))
            component.SelectedSoftMaterial = material;
        else if (args.LayerType == ArmorWorkbenchLayerSlot.Hard && SupportsHard(materialComp))
            component.SelectedHardMaterial = material;

        UpdateUserInterface(uid, component);
    }

    private void OnEjectRequest(EntityUid uid, ArmorWorkbenchComponent component, ArmorWorkbenchEjectRequestMessage args)
    {
        if (component.IsCrafting)
            return;

        switch (args.Target)
        {
            case ArmorWorkbenchEjectTarget.Blueprint:
                EjectBlueprint(component);
                break;
            case ArmorWorkbenchEjectTarget.Materials:
                EjectMaterials(component);
                break;
        }

        ValidateSelections(component);
        UpdateUserInterface(uid, component);
    }

    private void OnStartCraft(EntityUid uid, ArmorWorkbenchComponent component, ArmorWorkbenchStartCraftMessage args)
    {
        if (component.IsCrafting || !_power.IsPowered(uid))
            return;

        var context = GetCraftContext(component);
        if (context == null)
        {
            _popup.PopupEntity(Loc.GetString("armor-workbench-popup-missing-materials"), uid, args.Actor);
            UpdateUserInterface(uid, component);
            return;
        }

        component.IsCrafting = true;

        var doAfterArgs = new DoAfterArgs(EntityManager, args.Actor, component.CraftDuration, new ArmorWorkbenchDoAfterEvent(), uid, target: uid)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = false
        };

        if (!_doAfter.TryStartDoAfter(doAfterArgs))
        {
            component.IsCrafting = false;
        }

        UpdateUserInterface(uid, component);
    }

    private void OnCraftDoAfter(EntityUid uid, ArmorWorkbenchComponent component, ArmorWorkbenchDoAfterEvent args)
    {
        component.IsCrafting = false;

        if (args.Handled || args.Cancelled)
        {
            args.Handled = true;
            UpdateUserInterface(uid, component);
            return;
        }

        args.Handled = true;

        var context = GetCraftContext(component);
        if (context == null)
        {
            UpdateUserInterface(uid, component);
            return;
        }

        var crafted = Spawn(context.Blueprint.ResultPrototype, Transform(uid).Coordinates);
        if (TryComp<NCLayeredArmorComponent>(crafted, out var armor))
        {
            ApplyLayer(armor.SoftLayer, context.SoftMaterial, context.Blueprint.Coverage);
            ApplyLayer(armor.HardLayer, context.HardMaterial, context.Blueprint.Coverage);
            Dirty(crafted, armor);
        }

        if (component.Storage.Contains(context.BlueprintUid))
            _container.Remove(context.BlueprintUid, component.Storage);

        QueueDel(context.BlueprintUid);
        ConsumeCraftMaterials(component, context);

        component.SelectedBaseMaterial = null;
        component.SelectedSoftMaterial = null;
        component.SelectedHardMaterial = null;
        UpdateUserInterface(uid, component);
    }

    private static void ApplyLayer(NCLayeredArmorLayer layer, ArmorMaterialSnapshot? material, List<Content.Shared._Shitmed.Targeting.TargetBodyPart> coverage)
    {
        if (material == null)
        {
            layer.ArmorClass = 0;
            layer.MaxDurability = 0f;
            layer.CurrentDurability = 0f;
            layer.Coverage = new List<Content.Shared._Shitmed.Targeting.TargetBodyPart>();
            return;
        }

        layer.ArmorClass = material.GrantedArmorClass;
        layer.MaxDurability = material.GrantedDurability;
        layer.CurrentDurability = material.GrantedDurability;
        layer.Coverage = new List<Content.Shared._Shitmed.Targeting.TargetBodyPart>(coverage);
    }

    private bool CanInsert(EntityUid uid, ArmorWorkbenchComponent component, EntityUid item)
    {
        if (!CanAcceptItem(item))
            return false;

        if (!_power.IsPowered(uid))
        {
            _popup.PopupEntity(Loc.GetString("armor-workbench-popup-no-power"), uid, uid);
            return false;
        }

        if (component.Storage.Contains(item))
            return false;

        return true;
    }

    private bool CanAcceptItem(EntityUid item)
    {
        return HasComp<ArmorBlueprintComponent>(item) || HasComp<ArmorMaterialComponent>(item);
    }

    private static bool SupportsSoft(ArmorMaterialComponent material)
    {
        return material.LayerType == ArmorMaterialType.Armor;
    }

    private static bool SupportsBase(ArmorMaterialComponent material)
    {
        return material.LayerType == ArmorMaterialType.Base;
    }

    private static bool SupportsHard(ArmorMaterialComponent material)
    {
        return material.LayerType == ArmorMaterialType.Armor;
    }

    private void ValidateSelections(ArmorWorkbenchComponent component)
    {
        if (component.SelectedBaseMaterial is { } armorBase && !component.Storage.Contains(armorBase))
            component.SelectedBaseMaterial = null;

        if (component.SelectedSoftMaterial is { } soft && !component.Storage.Contains(soft))
            component.SelectedSoftMaterial = null;

        if (component.SelectedHardMaterial is { } hard && !component.Storage.Contains(hard))
            component.SelectedHardMaterial = null;
    }

    private CraftContext? GetCraftContext(ArmorWorkbenchComponent component)
    {
        ValidateSelections(component);

        EntityUid? blueprintUid = null;
        ArmorBlueprintComponent? blueprint = null;
        var baseMaterials = new List<EntityUid>();
        var softMaterials = new List<EntityUid>();
        var hardMaterials = new List<EntityUid>();

        foreach (var entity in component.Storage.ContainedEntities)
        {
            if (blueprint == null && TryComp<ArmorBlueprintComponent>(entity, out var foundBlueprint))
            {
                blueprintUid = entity;
                blueprint = foundBlueprint;
            }

            if (!TryComp<ArmorMaterialComponent>(entity, out var material))
                continue;

            if (SupportsBase(material))
                baseMaterials.Add(entity);

            if (SupportsSoft(material))
                softMaterials.Add(entity);

            if (SupportsHard(material))
                hardMaterials.Add(entity);
        }

        if (blueprint == null || blueprintUid == null)
            return null;

        if (component.SelectedBaseMaterial == null || !baseMaterials.Contains(component.SelectedBaseMaterial.Value))
            component.SelectedBaseMaterial = baseMaterials.FirstOrDefault();

        if (component.SelectedSoftMaterial != null && !softMaterials.Contains(component.SelectedSoftMaterial.Value))
            component.SelectedSoftMaterial = null;

        if (component.SelectedHardMaterial != null && !hardMaterials.Contains(component.SelectedHardMaterial.Value))
            component.SelectedHardMaterial = null;

        if (component.SelectedBaseMaterial == null)
            return null;

        if (!TryComp<ArmorMaterialComponent>(component.SelectedBaseMaterial.Value, out var baseMaterial))
            return null;

        var baseMaterialAmount = Math.Max(1, blueprint.BaseMaterialAmount);
        var softMaterialAmount = Math.Max(1, blueprint.SoftMaterialAmount);
        var hardMaterialAmount = Math.Max(1, blueprint.HardMaterialAmount);

        var requiredMaterialCounts = new Dictionary<EntityUid, int>
        {
            [component.SelectedBaseMaterial.Value] = baseMaterialAmount
        };

        var baseMaterialSnapshot = new ArmorMaterialSnapshot(
            baseMaterial.GrantedArmorClass,
            baseMaterial.GrantedDurability);

        ArmorMaterialSnapshot? softMaterial = null;
        if (component.SelectedSoftMaterial != null &&
            TryComp<ArmorMaterialComponent>(component.SelectedSoftMaterial.Value, out var resolvedSoftMaterial))
        {
            softMaterial = new ArmorMaterialSnapshot(
                resolvedSoftMaterial.GrantedArmorClass,
                resolvedSoftMaterial.GrantedDurability);
            requiredMaterialCounts[component.SelectedSoftMaterial.Value] =
                requiredMaterialCounts.GetValueOrDefault(component.SelectedSoftMaterial.Value) + softMaterialAmount;
        }

        ArmorMaterialSnapshot? hardMaterial = null;
        if (component.SelectedHardMaterial != null &&
            TryComp<ArmorMaterialComponent>(component.SelectedHardMaterial.Value, out var resolvedHardMaterial))
        {
            hardMaterial = new ArmorMaterialSnapshot(
                resolvedHardMaterial.GrantedArmorClass,
                resolvedHardMaterial.GrantedDurability);
            requiredMaterialCounts[component.SelectedHardMaterial.Value] =
                requiredMaterialCounts.GetValueOrDefault(component.SelectedHardMaterial.Value) + hardMaterialAmount;
        }

        foreach (var (materialUid, requiredAmount) in requiredMaterialCounts)
        {
            if (!HasEnoughMaterial(materialUid, requiredAmount))
                return null;
        }

        return new CraftContext(
            blueprintUid.Value,
            blueprint,
            component.SelectedBaseMaterial.Value,
            baseMaterialSnapshot,
            baseMaterialAmount,
            component.SelectedSoftMaterial,
            softMaterial,
            softMaterial != null ? softMaterialAmount : 0,
            component.SelectedHardMaterial,
            hardMaterial,
            hardMaterial != null ? hardMaterialAmount : 0);
    }

    private void UpdateUserInterface(EntityUid uid, ArmorWorkbenchComponent component)
    {
        if (!_ui.HasUi(uid, ArmorWorkbenchUiKey.Key))
            return;

        NetEntity? blueprintEntity = null;
        var blueprintName = default(string);
        var resultName = default(string);
        var baseMaterialAmount = 1;
        var softMaterialAmount = 1;
        var hardMaterialAmount = 1;
        var baseEntries = new List<ArmorWorkbenchMaterialEntry>();
        var softEntries = new List<ArmorWorkbenchMaterialEntry>();
        var hardEntries = new List<ArmorWorkbenchMaterialEntry>();

        foreach (var entity in component.Storage.ContainedEntities)
        {
            if (TryComp<ArmorBlueprintComponent>(entity, out var blueprint) && blueprintName == null)
            {
                blueprintEntity = GetNetEntity(entity);
                blueprintName = MetaData(entity).EntityName;
                resultName = ResolveResultName(blueprint.ResultPrototype);
                baseMaterialAmount = Math.Max(1, blueprint.BaseMaterialAmount);
                softMaterialAmount = Math.Max(1, blueprint.SoftMaterialAmount);
                hardMaterialAmount = Math.Max(1, blueprint.HardMaterialAmount);
            }

            if (!TryComp<ArmorMaterialComponent>(entity, out var material))
                continue;

            var countSuffix = TryComp<StackComponent>(entity, out var stackComp)
                ? $" x{stackComp.Count}"
                : string.Empty;

            var entry = new ArmorWorkbenchMaterialEntry(
                GetNetEntity(entity),
                $"{MetaData(entity).EntityName}{countSuffix}",
                material.GrantedArmorClass,
                material.GrantedDurability);

            if (SupportsBase(material))
                baseEntries.Add(entry);

            if (SupportsSoft(material))
                softEntries.Add(entry);

            if (SupportsHard(material))
                hardEntries.Add(entry);
        }

        ValidateSelections(component);

        var status = ArmorWorkbenchUiStatus.WaitingInput;
        if (component.IsCrafting)
            status = ArmorWorkbenchUiStatus.Crafting;
        else if (blueprintName == null)
            status = ArmorWorkbenchUiStatus.MissingBlueprint;
        else if (baseEntries.Count == 0)
            status = ArmorWorkbenchUiStatus.MissingBaseMaterial;
        else if (GetCraftContext(component) == null)
            status = ArmorWorkbenchUiStatus.MissingRecipeMaterials;
        else
            status = ArmorWorkbenchUiStatus.Ready;

        var state = new ArmorWorkbenchBoundUserInterfaceState(
            status,
            component.IsCrafting,
            component.CraftDuration,
            blueprintEntity,
            blueprintName,
            resultName,
            baseMaterialAmount,
            softMaterialAmount,
            hardMaterialAmount,
            baseEntries,
            softEntries,
            hardEntries,
            component.SelectedBaseMaterial != null ? GetNetEntity(component.SelectedBaseMaterial.Value) : null,
            component.SelectedSoftMaterial != null ? GetNetEntity(component.SelectedSoftMaterial.Value) : null,
            component.SelectedHardMaterial != null ? GetNetEntity(component.SelectedHardMaterial.Value) : null);

        _ui.SetUiState(uid, ArmorWorkbenchUiKey.Key, state);
    }

    private string ResolveResultName(string prototypeId)
    {
        return _prototype.TryIndex<EntityPrototype>(prototypeId, out var proto)
            ? proto.Name
            : prototypeId;
    }

    private bool HasEnoughMaterial(EntityUid uid, int requiredAmount)
    {
        if (requiredAmount <= 0)
            return true;

        return !TryComp<StackComponent>(uid, out var stackComp) || stackComp.Count >= requiredAmount;
    }

    private void ConsumeCraftMaterials(ArmorWorkbenchComponent component, CraftContext context)
    {
        var materialCosts = new Dictionary<EntityUid, int>
        {
            [context.BaseUid] = context.BaseMaterialAmount
        };

        if (context.SoftUid != null && context.SoftMaterialAmount > 0)
            materialCosts[context.SoftUid.Value] =
                materialCosts.GetValueOrDefault(context.SoftUid.Value) + context.SoftMaterialAmount;

        if (context.HardUid != null && context.HardMaterialAmount > 0)
            materialCosts[context.HardUid.Value] =
                materialCosts.GetValueOrDefault(context.HardUid.Value) + context.HardMaterialAmount;

        foreach (var (materialUid, amount) in materialCosts)
        {
            if (Deleted(materialUid) || !component.Storage.Contains(materialUid))
                continue;

            if (TryComp<StackComponent>(materialUid, out var stackComp))
            {
                _stack.Use(materialUid, amount, stackComp);
                continue;
            }

            _container.Remove(materialUid, component.Storage);
            QueueDel(materialUid);
        }
    }

    private void EjectBlueprint(ArmorWorkbenchComponent component)
    {
        foreach (var entity in component.Storage.ContainedEntities.ToArray())
        {
            if (!HasComp<ArmorBlueprintComponent>(entity))
                continue;

            _container.Remove(entity, component.Storage);
            break;
        }
    }

    private void EjectMaterials(ArmorWorkbenchComponent component)
    {
        foreach (var entity in component.Storage.ContainedEntities.ToArray())
        {
            if (!HasComp<ArmorMaterialComponent>(entity))
                continue;

            _container.Remove(entity, component.Storage);
        }

        component.SelectedBaseMaterial = null;
        component.SelectedSoftMaterial = null;
        component.SelectedHardMaterial = null;
    }

    private sealed record CraftContext(
        EntityUid BlueprintUid,
        ArmorBlueprintComponent Blueprint,
        EntityUid BaseUid,
        ArmorMaterialSnapshot BaseMaterial,
        int BaseMaterialAmount,
        EntityUid? SoftUid,
        ArmorMaterialSnapshot? SoftMaterial,
        int SoftMaterialAmount,
        EntityUid? HardUid,
        ArmorMaterialSnapshot? HardMaterial,
        int HardMaterialAmount);

    private sealed record ArmorMaterialSnapshot(
        int GrantedArmorClass,
        float GrantedDurability);
}
