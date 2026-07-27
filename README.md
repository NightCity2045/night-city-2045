<div align="center"><img alt="NC logo" src="https://raw.githubusercontent.com/NightCitySS14/night-station/dev/Resources/Textures/_NC/Logo/14062025_092658.png" width="512px" /></div>

---

Night City 2045 билд по настольной версии Киберпанк РЕД

## Ссылки

[Steam](https://store.steampowered.com/app/3731580/Space_Station_Beyond/) | [Клиент без Steam](https://github.com/Simple-Station/SimpleStationLauncher/releases/latest) | [Основной репозиторий](https://github.com/Simple-Station/Einstein-Engines) | [Discord](https://discord.gg/vu5VT7ETDX)

## Контрибуция
Внимание, имейте в виду, что при контрибуции, вы автоматически соглашаетесь на условия которые описаны в нашем контрибуторском соглашении! Прочитать на английском: [NIGHTCITYSS14-LICENSE-ICLA-EN](./NIGHTCITYSS14-LICENSE-ICLA-EN.txt). Прочитать на русском: [NIGHTCITYSS14-LICENSE-ICLA-RU](./NIGHTCITYSS14-LICENSE-ICLA-RU.txt).

Абсолютно каждый и любой имеет право на контрибуцию в данный репозиторий и мы будем безмерно рады вашему вкладу в наше общее дело, однако, вы должны понимать, что не каждая контрибуция будет принята.

Если вы желаете, чтобы Ваша Контрибуция была принята, ознакомьтесь в первую очередь с дизайнерскими документами.. Это касается в первую очередь гейм-дизайна различных нововведений, стиля спрайтов и общей атмосферы.

Так как наличие контрибуторского соглашения может насторожить вас, будущих контрибуторов, я, Astro, владелец данного билда и его главный архитектор, хочу заверить вас, что в первую очередь - это нужно для защиты вашей же контрибуции.

Так как NightCity2045 полностью придерживается принципов некоммерческого проекта с открытым кодом, мы так-же хотим защитить ваш (и в будущем, наш) код от коммерциализации и неправильного использования. Контрибуция в наш репозиторий будет означать то, что мы сможем использовать все ресурсы NightCity2045 для защиты ваших интересов.

Потому, ваша контрибуция будет означать, что будет сделано всё возможное, чтобы ваши интересы были учтены, и в первую очередь, ваш код не был использован на коммерческих проектах и других проектах, где вы бы не хотели его видеть. Вы всё ещё, однако, будете обладать полными правами на свою оригинальную работу, если вы желаете перелицензировать или продать свою работу.

## Сборка

Следуйте [гайду от Space Wizards](https://docs.spacestation14.com/en/general-development/setup/setting-up-a-development-environment.html) по настройке рабочей среды, но учитывайте, что наши репозитории отличаются и некоторые вещи могут отличаться.
Мы предлагаем несколько скриптов, показанных ниже, чтобы облегчить работу.

### Необходимые зависимости

> - Git
> - .NET SDK 9.0.101


### Windows

> 1. Склонируйте данный репозиторий
> 2. Запустите `git submodule update --init --recursive` в командной строке, чтобы скачать движок игры
> 3. Запускайте `Scripts/bat/buildAllDebug.bat` после любых изменений в коде проекта
> 4. Запустите `Scripts/bat/runQuickAll.bat`, чтобы запустить клиент и сервер
> 5. Подключитесь к локальному серверу и играйте

### Linux

> 1. Склонируйте данный репозиторий.
> 2. Запустите `git submodule update --init --recursive` в командной строке, чтобы скачать движок игры
> 3. Запускайте `Scripts/sh/buildAllDebug.sh` после любых изменений в коде проекта
> 4. Запустите `Scripts/sh/runQuickAll.sh`, чтобы запустить клиент и сервер
> 5. Подключитесь к локальному серверу и играйте

### MacOS

> Предположительно, также, как и на Линуксе.

## Лицензия

Содержимое, добавленное в этот репозиторий после коммита 87c70a89a67d0521a56388e6b1c3f2cb947943e4 (`17 February 2024 23:00:00 UTC`), распространяется по лицензии GNU Affero General Public License версии 3.0, если не указано иное.
См. [LICENSE-AGPLv3](./LICENSE-AGPLv3.txt).

Содержимое, добавленное в этот репозиторий до коммита 87c70a89a67d0521a56388e6b1c3f2cb947943e4 (`17 February 2024 23:00:00 UTC`) распространяется по лицензии MIT, если не указано иное.
См. [LICENSE-MIT](./LICENSE-MIT.txt).

Содержимое, добавленное в этот репозиторий после коммита aa760f196d8e6dfc65136ece0dbbf51b92645ea8 (`28 January 2026 20:00:00 UTC`), распространяется по двойной лицензии GNU Affero General Public License версии 3.0 и WILDCARD WHITE DREAM PROJECT INDIVIDUAL CONTRIBUTOR LICENSE AGREEMENT, если не указано иное.
См. [LICENSE-ICLA-EN](./LICENSE-ICLA-EN.txt).

## Лицензирование оригинального кода Night City 2045

Этот репозиторий содержит материалы, распространяемые по различным лицензиям.

### Код Night City 2045

Все файлы исходного кода, которые:

1. расположены в каталогах с названием `_NC`; и
2. являются оригинальной работой Astro и проекта Night City 2045,

если непосредственно в файле не указано иное, предоставляются на условиях **PolyForm Noncommercial License 1.0.0**.

Официальный текст лицензии:

`https://polyformproject.org/licenses/noncommercial/1.0.0`

Этот код разрешается использовать, запускать, копировать, изменять и распространять исключительно в некоммерческих целях и при соблюдении условий указанной лицензии.

Любое коммерческое использование оригинального кода Night City 2045 требует предварительного письменного разрешения правообладателя и отдельной коммерческой лицензии.

К коммерческому использованию может относиться, среди прочего, использование кода в составе платного продукта, платного игрового сервера, коммерческой услуги либо проекта, основной целью которого является получение коммерческой выгоды или денежного вознаграждения.

Настоящее уведомление распространяется только на оригинальный код Night City 2045. Оно не изменяет и не отменяет лицензии кода, ресурсов и других материалов, полученных из Space Station 14, Einstein Engines, WWhiteDreamProject или иных сторонних источников. Такие материалы продолжают распространяться на условиях их первоначальных лицензий.

**Дата вступления в силу:** коммит `2c607ed6de475a9c9f40e972bb939760bf161672` (`27 July 2026 20:00:00 UTC`),

Required Notice: Copyright © 2026 Astro and Night City 2045. Original source code located in directories named `_NC` is licensed under the PolyForm Noncommercial License 1.0.0 unless otherwise stated.

По вопросам получения коммерческой лицензии писать в дискорд: nre500.

## Licensing of Original Night City 2045 Code

This repository contains materials distributed under various licenses.

### Night City 2045 Code

All source code files that:

1. are located in directories named `_NC`; and
2. are the original work of Astro and the Night City 2045 project,

unless otherwise stated directly in the file, are provided under the terms of the **PolyForm Noncommercial License 1.0.0**.

Official license text:

`https://polyformproject.org/licenses/noncommercial/1.0.0`

This code may be used, run, copied, modified, and distributed exclusively for noncommercial purposes and in compliance with the terms of the specified license.

Any commercial use of the original Night City 2045 code requires the prior written permission of the copyright holder and a separate commercial license.

Commercial use may include, among other things, the use of the code as part of a paid product, paid game server, commercial service, or project whose primary purpose is obtaining commercial benefit or monetary compensation.

This notice applies only to the original Night City 2045 code. It does not modify or revoke the licenses applicable to code, resources, or other materials obtained from Space Station 14, Einstein Engines, WWhiteDreamProject, or any other third-party sources. Such materials remain distributed under the terms of their original licenses.

**Effective date:** commit `2c607ed6de475a9c9f40e972bb939760bf161672` (`27 July 2026 20:00:00 UTC`).

Required Notice: Copyright © 2026 Astro and Night City 2045. Original source code located in directories named `_NC` is licensed under the PolyForm Noncommercial License 1.0.0 unless otherwise stated.


Большинство ресурсов лицензировано под [CC-BY-SA 3.0](https://creativecommons.org/licenses/by-sa/3.0/), если не указано иное. Лицензия и авторские права на ресурсах указаны в файле метаданных.
[Example](./Resources/Textures/Objects/Tools/crowbar.rsi/meta.json).

Обратите внимание, что активы, созданные командой WWDP и Night City 2045, лицензированы под некоммерческой [CC-BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/) или аналогичной некоммерческой лицензией и должны быть удалены, если вы хотите использовать этот проект в коммерческих целях. В случае, если в файле метаданных указано иное - укажите нам об этом, прошлая лицензия будет применяться ретроактивно вместе с CC-BY-NC-SA 4.0.
