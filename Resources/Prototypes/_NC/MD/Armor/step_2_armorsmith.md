# Armor Workbench Contract

The armor workbench assembles armor under the physical armor GDD.

## Inputs

- `ArmorBlueprintComponent` selects the result prototype, coverage, and required
  material amounts.
- `ArmorMaterialComponent` marks materials as `Base`, `Carrier`, `Plate`, or
  compatible combinations.
- Materials grant durability and stopping power through data fields. Stopping
  power uses the same 0-100 scale as projectile penetration.

## Output

The crafted item receives a `PhysicalArmorComponent` configured from the chosen
carrier material and blueprint coverage. Plate protection is represented by
separate physical plate entities inserted into armor `ItemSlots`.

## UI Terms

- Base material: required shell material for the recipe.
- Carrier material: vest or clothing carrier durability.
- Plate material: material that contributes `SP`.

Do not use the removed armor-class, soft-layer, or hard-layer terminology in
new armor workbench code or data.
