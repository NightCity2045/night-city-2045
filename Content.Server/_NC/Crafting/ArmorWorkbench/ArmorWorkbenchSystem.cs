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
                component.SelectedCarrierMaterial = null;
                component.SelectedPlateMaterial = null;
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
        else if (args.LayerType == ArmorWorkbenchLayerSlot.Carrier && SupportsCarrier(materialComp))
            component.SelectedCarrierMaterial = material;
        else if (args.LayerType == ArmorWorkbenchLayerSlot.Plate && SupportsPlate(materialComp))
            component.SelectedPlateMaterial = material;

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
        if (TryComp<PhysicalArmorComponent>(crafted, out var armor))
        {
            ApplyArmor(armor, context.CarrierMaterial ?? context.BaseMaterial, context.Blueprint.Coverage);
            Dirty(crafted, armor);
        }

        if (component.Storage.Contains(context.BlueprintUid))
            _container.Remove(context.BlueprintUid, component.Storage);

        QueueDel(context.BlueprintUid);
        ConsumeCraftMaterials(component, context);

        component.SelectedBaseMaterial = null;
        component.SelectedCarrierMaterial = null;
        component.SelectedPlateMaterial = null;
        UpdateUserInterface(uid, component);
    }

    private static void ApplyArmor(PhysicalArmorComponent armor, ArmorMaterialSnapshot material, List<Content.Shared._Shitmed.Targeting.TargetBodyPart> coverage)
    {
        armor.StoppingPower = material.GrantedStoppingPower;
        armor.MaxDurability = material.GrantedDurability;
        armor.CurrentDurability = material.GrantedDurability;
        armor.MaterialType = material.MaterialType;
        armor.BluntDamageMultiplier = material.BluntDamageMultiplier;
        armor.DurabilityDamageMultiplier = material.DurabilityDamageMultiplier;
        armor.Coverage = new List<Content.Shared._Shitmed.Targeting.TargetBodyPart>(coverage);
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

    private static bool SupportsCarrier(ArmorMaterialComponent material)
    {
        return material.LayerType == ArmorMaterialType.Carrier;
    }

    private static bool SupportsBase(ArmorMaterialComponent material)
    {
        return material.LayerType == ArmorMaterialType.Base;
    }

    private static bool SupportsPlate(ArmorMaterialComponent material)
    {
        return material.LayerType == ArmorMaterialType.Plate;
    }

    private void ValidateSelections(ArmorWorkbenchComponent component)
    {
        if (component.SelectedBaseMaterial is { } armorBase && !component.Storage.Contains(armorBase))
            component.SelectedBaseMaterial = null;

        if (component.SelectedCarrierMaterial is { } carrier && !component.Storage.Contains(carrier))
            component.SelectedCarrierMaterial = null;

        if (component.SelectedPlateMaterial is { } plate && !component.Storage.Contains(plate))
            component.SelectedPlateMaterial = null;
    }

    private CraftContext? GetCraftContext(ArmorWorkbenchComponent component)
    {
        ValidateSelections(component);

        EntityUid? blueprintUid = null;
        ArmorBlueprintComponent? blueprint = null;
        var baseMaterials = new List<EntityUid>();
        var carrierMaterials = new List<EntityUid>();
        var plateMaterials = new List<EntityUid>();

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

            if (SupportsCarrier(material))
                carrierMaterials.Add(entity);

            if (SupportsPlate(material))
                plateMaterials.Add(entity);
        }

        if (blueprint == null || blueprintUid == null)
            return null;

        if (component.SelectedBaseMaterial == null || !baseMaterials.Contains(component.SelectedBaseMaterial.Value))
            component.SelectedBaseMaterial = baseMaterials.FirstOrDefault();

        if (component.SelectedCarrierMaterial != null && !carrierMaterials.Contains(component.SelectedCarrierMaterial.Value))
            component.SelectedCarrierMaterial = null;

        if (component.SelectedPlateMaterial != null && !plateMaterials.Contains(component.SelectedPlateMaterial.Value))
            component.SelectedPlateMaterial = null;

        if (component.SelectedBaseMaterial == null)
            return null;

        if (!TryComp<ArmorMaterialComponent>(component.SelectedBaseMaterial.Value, out var baseMaterial))
            return null;

        var baseMaterialAmount = Math.Max(1, blueprint.BaseMaterialAmount);
        var carrierMaterialAmount = Math.Max(1, blueprint.CarrierMaterialAmount);
        var plateMaterialAmount = Math.Max(1, blueprint.PlateMaterialAmount);

        var requiredMaterialCounts = new Dictionary<EntityUid, int>
        {
            [component.SelectedBaseMaterial.Value] = baseMaterialAmount
        };

        var baseMaterialSnapshot = new ArmorMaterialSnapshot(
            baseMaterial.GrantedStoppingPower,
            baseMaterial.GrantedDurability,
            baseMaterial.MaterialType,
            baseMaterial.BluntDamageMultiplier,
            baseMaterial.DurabilityDamageMultiplier);

        ArmorMaterialSnapshot? carrierMaterial = null;
        if (component.SelectedCarrierMaterial != null &&
            TryComp<ArmorMaterialComponent>(component.SelectedCarrierMaterial.Value, out var resolvedCarrierMaterial))
        {
            carrierMaterial = new ArmorMaterialSnapshot(
                resolvedCarrierMaterial.GrantedStoppingPower,
                resolvedCarrierMaterial.GrantedDurability,
                resolvedCarrierMaterial.MaterialType,
                resolvedCarrierMaterial.BluntDamageMultiplier,
                resolvedCarrierMaterial.DurabilityDamageMultiplier);
            requiredMaterialCounts[component.SelectedCarrierMaterial.Value] =
                requiredMaterialCounts.GetValueOrDefault(component.SelectedCarrierMaterial.Value) + carrierMaterialAmount;
        }

        ArmorMaterialSnapshot? plateMaterial = null;
        if (component.SelectedPlateMaterial != null &&
            TryComp<ArmorMaterialComponent>(component.SelectedPlateMaterial.Value, out var resolvedPlateMaterial))
        {
            plateMaterial = new ArmorMaterialSnapshot(
                resolvedPlateMaterial.GrantedStoppingPower,
                resolvedPlateMaterial.GrantedDurability,
                resolvedPlateMaterial.MaterialType,
                resolvedPlateMaterial.BluntDamageMultiplier,
                resolvedPlateMaterial.DurabilityDamageMultiplier);
            requiredMaterialCounts[component.SelectedPlateMaterial.Value] =
                requiredMaterialCounts.GetValueOrDefault(component.SelectedPlateMaterial.Value) + plateMaterialAmount;
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
            component.SelectedCarrierMaterial,
            carrierMaterial,
            carrierMaterial != null ? carrierMaterialAmount : 0,
            component.SelectedPlateMaterial,
            plateMaterial,
            plateMaterial != null ? plateMaterialAmount : 0);
    }

    private void UpdateUserInterface(EntityUid uid, ArmorWorkbenchComponent component)
    {
        if (!_ui.HasUi(uid, ArmorWorkbenchUiKey.Key))
            return;

        NetEntity? blueprintEntity = null;
        var blueprintName = default(string);
        var resultName = default(string);
        var baseMaterialAmount = 1;
        var carrierMaterialAmount = 1;
        var plateMaterialAmount = 1;
        var baseEntries = new List<ArmorWorkbenchMaterialEntry>();
        var carrierEntries = new List<ArmorWorkbenchMaterialEntry>();
        var plateEntries = new List<ArmorWorkbenchMaterialEntry>();

        foreach (var entity in component.Storage.ContainedEntities)
        {
            if (TryComp<ArmorBlueprintComponent>(entity, out var blueprint) && blueprintName == null)
            {
                blueprintEntity = GetNetEntity(entity);
                blueprintName = MetaData(entity).EntityName;
                resultName = ResolveResultName(blueprint.ResultPrototype);
                baseMaterialAmount = Math.Max(1, blueprint.BaseMaterialAmount);
                carrierMaterialAmount = Math.Max(1, blueprint.CarrierMaterialAmount);
                plateMaterialAmount = Math.Max(1, blueprint.PlateMaterialAmount);
            }

            if (!TryComp<ArmorMaterialComponent>(entity, out var material))
                continue;

            var countSuffix = TryComp<StackComponent>(entity, out var stackComp)
                ? $" x{stackComp.Count}"
                : string.Empty;

            var entry = new ArmorWorkbenchMaterialEntry(
                GetNetEntity(entity),
                $"{MetaData(entity).EntityName}{countSuffix}",
                material.GrantedStoppingPower,
                material.GrantedDurability);

            if (SupportsBase(material))
                baseEntries.Add(entry);

            if (SupportsCarrier(material))
                carrierEntries.Add(entry);

            if (SupportsPlate(material))
                plateEntries.Add(entry);
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
            carrierMaterialAmount,
            plateMaterialAmount,
            baseEntries,
            carrierEntries,
            plateEntries,
            component.SelectedBaseMaterial != null ? GetNetEntity(component.SelectedBaseMaterial.Value) : null,
            component.SelectedCarrierMaterial != null ? GetNetEntity(component.SelectedCarrierMaterial.Value) : null,
            component.SelectedPlateMaterial != null ? GetNetEntity(component.SelectedPlateMaterial.Value) : null);

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

        if (context.CarrierUid != null && context.CarrierMaterialAmount > 0)
            materialCosts[context.CarrierUid.Value] =
                materialCosts.GetValueOrDefault(context.CarrierUid.Value) + context.CarrierMaterialAmount;

        if (context.PlateUid != null && context.PlateMaterialAmount > 0)
            materialCosts[context.PlateUid.Value] =
                materialCosts.GetValueOrDefault(context.PlateUid.Value) + context.PlateMaterialAmount;

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
        component.SelectedCarrierMaterial = null;
        component.SelectedPlateMaterial = null;
    }

    private sealed record CraftContext(
        EntityUid BlueprintUid,
        ArmorBlueprintComponent Blueprint,
        EntityUid BaseUid,
        ArmorMaterialSnapshot BaseMaterial,
        int BaseMaterialAmount,
        EntityUid? CarrierUid,
        ArmorMaterialSnapshot? CarrierMaterial,
        int CarrierMaterialAmount,
        EntityUid? PlateUid,
        ArmorMaterialSnapshot? PlateMaterial,
        int PlateMaterialAmount);

    private sealed record ArmorMaterialSnapshot(
        float GrantedStoppingPower,
        float GrantedDurability,
        PhysicalArmorMaterialType MaterialType,
        float BluntDamageMultiplier,
        float DurabilityDamageMultiplier);
}
