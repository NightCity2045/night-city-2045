# SPDX-FileCopyrightText: Made by Ceterai on GitHub.
#
# SPDX-License-Identifier: CC-BY-SA-4.0
#
# Title: Orchito

orchito-shared-suffix = НЕ МАППИТЬ, Орхи Дея

reagent-name-orchito = орхито
reagent-desc-orchito = Освежающий безалкогольный мохито-спешл Орхидеи на чайной основе.
reagent-name-stardust-orchito = звёздное орхито
reagent-desc-stardust-orchito = Освежающий темный коктейль с неизвестными свойствами. Пить только с загадочным лицом.

ent-DrinkOrchitoGlass = { ent-DrinkGlass }
    .desc = { ent-DrinkGlass.desc }
    .suffix = Орхито, { orchito-shared-suffix }
ent-DrinkOrchitoJug = кувшин орхито
    .desc = { reagent-desc-orchito }
    .suffix = { orchito-shared-suffix }
ent-DrinkStardustOrchitoGlass = { ent-DrinkGlass }
    .desc = { ent-DrinkGlass.desc }
    .suffix = Звёздное Орхито, { orchito-shared-suffix }
ent-DrinkStardustOrchitoJug = кувшин звёздного орхито
    .desc = { reagent-desc-stardust-orchito }
    .suffix = { orchito-shared-suffix }
