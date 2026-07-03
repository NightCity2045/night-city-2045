# SPDX-FileCopyrightText: Made by Ceterai on GitHub.
#
# SPDX-License-Identifier: CC-BY-SA-4.0
#
# Title: Orchito

orchito-shared-suffix = DO NOT MAP, Orchi Dea

reagent-name-orchito = orchito
reagent-desc-orchito = A refreshing non-alcoholic tea mojito special from Stardust Orchid.
reagent-name-stardust-orchito = stardust orchito
reagent-desc-stardust-orchito = A refreshing dark cocktail with unknown properties. Drink only with a mysterious face.

ent-DrinkOrchitoGlass = { ent-DrinkGlass }
    .desc = { ent-DrinkGlass.desc }
    .suffix = Orchito, { orchito-shared-suffix }
ent-DrinkOrchitoJug = orchito jug
    .desc = { reagent-desc-orchito }
    .suffix = { orchito-shared-suffix }
ent-DrinkStardustOrchitoGlass = { ent-DrinkGlass }
    .desc = { ent-DrinkGlass.desc }
    .suffix = Stardust Orchito, { orchito-shared-suffix }
ent-DrinkStardustOrchitoJug = stardust orchito jug
    .desc = { reagent-desc-stardust-orchito }
    .suffix = { orchito-shared-suffix }
