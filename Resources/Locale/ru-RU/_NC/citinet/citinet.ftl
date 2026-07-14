# NC — Локализация CitiNet каналов (RU)
chat-radio-ncpd = НКПД
chat-radio-traumateam = Трэвма Тим
chat-radio-maxtac = МАКС-ТАК
chat-radio-militech = Милитек
chat-radio-biotechnica = Биотехника

# CitiNet BBS
citinet-bbs-channel-public = Городская сеть
citinet-bbs-channel-afterlife = Посмертие
citinet-bbs-channel-maelstrom = Мальстрём
citinet-bbs-channel-ncpd-dispatch = NCPD | Диспетчерская
citinet-bbs-channel-ncpd-detectives = NCPD | Детективы
citinet-bbs-channel-ncpd-command = NCPD | Командование
citinet-bbs-channel-maxtac-tactical = MaxTac | Тактика
citinet-bbs-channel-maxtac-command = MaxTac | Штаб
citinet-bbs-channel-biotech-general = Biotechnica | Общий
citinet-bbs-channel-biotech-operatives = Biotech | Оперативники
citinet-bbs-channel-biotech-command = Biotech | Директорат
citinet-bbs-channel-trauma-general = Trauma Team
citinet-bbs-channel-trauma-operatives = Оперативная группа
citinet-bbs-channel-trauma-comms = Корп. связь Trauma

# CitiNet Cartridge UI
citinet-cartridge-name = CitiNet
citinet-cartridge-description = Терминал городской сети связи Найт-Сити.

citinet-delivery-cartridge-name = "QuickPath Navi"

citinet-tab-calls = Звонки
citinet-tab-group = Тактика
citinet-tab-bbs = BBS

citinet-call-initiate = Позвонить
citinet-call-accept = Принять
citinet-call-decline = Отклонить
citinet-call-hangup = Сбросить
citinet-call-incoming = Входящий вызов от {$caller}...
citinet-call-ringing = Вызов {$target}...
citinet-call-flatline = FLATLINE — {$name} ликвидирован
citinet-flatline-dead = FLATLINE — {$name} убит
citinet-flatline-critical = КРИТИЧЕСКОЕ — {$name} при смерти
citinet-call-active = Соединение — {$target}
citinet-call-no-relay = [color=red]Нет сигнала — CitiNet Relay оффлайн[/color]
citinet-call-ping-location = {$sender} отправил координаты: {$coords}

citinet-group-create = Создать мост
citinet-group-invite = Пригласить
citinet-group-leave = Покинуть
citinet-group-participants = Участники: {$count}/{$max}
citinet-group-flatline = [color=red][СИСТЕМА]: Агент {$name} отключён. Статус: КРИТИЧЕСКИЙ (FLATLINE)[/color]

citinet-bbs-join = Подключиться
citinet-bbs-leave = Отключиться
citinet-bbs-send = Отправить
citinet-bbs-password-required = Требуется пароль
citinet-bbs-enter-password = Введите код доступа:
citinet-bbs-wrong-password = Доступ запрещён — неверный код
citinet-bbs-no-relay = [color=red]Канал недоступен — CitiNet оффлайн[/color]
citinet-bbs-anonymous = Аноним
citinet-bbs-invite-received = >> Вы приглашены в канал {$channel} агентом {$inviter}
citinet-bbs-invite-sent = >> Агент {$target} приглашён в {$channel}

# BurnerChip
citinet-burner-chip-name = Одноразовый чип
citinet-burner-chip-description = Дешёвый чип с чёрного рынка. Предоставляет временный анонимный ID, который невозможно отследить по базам НКПД.
citinet-burner-chip-inserted = Чип активирован. Временный ID: {$id}
citinet-burner-chip-removed = Чип деактивирован. Оригинальный ID восстановлен.
citinet-burner-chip-used = Этот чип уже был использован.
citinet-burner-chip-destroyed = Одноразовый чип уничтожен.

# CitiNet Relay
citinet-relay-name = CitiNet Relay
citinet-relay-description = Локальный городской узел связи. Маршрутизирует гражданские коммуникации — звонки и BBS-каналы. Требует питания.

citinet-sender-system = СИСТЕМА
citinet-sender-flatline = FLATLINE
citinet-call-busy = >> ЛИНИЯ ЗАНЯТА. ПОВТОРИТЕ ПОЗЖЕ.
citinet-call-connection-lost = >> ОБРЫВ СВЯЗИ. СИГНАЛ RELAY ПОТЕРЯН.


citinet-emergency-police-desc =  SOS: { $caller } вызов Траума.
citinet-emergency-trauma-desc =  SOS: { $caller } вызов патрульных в { $sector }.
citinet-p2p-game-chat = [CitiNet/ЛС] {$sender}: {$message}
citinet-group-game-chat = [CitiNet/Мост] {$sender}: {$message}
citinet-bbs-game-chat = [CitiNet/{$channel}] {$sender}: {$message}

# Delivery
nc-delivery-map-marker = Посылка готова: { $location }
nc-delivery-map-marker-pending = Посылка: { $location } ({ $seconds } с)

# Store Categories
citinet-store-category-tools = Инструменты
citinet-store-category-medical = Медицинские принадлежности
citinet-store-category-equipment = Оборудование
citinet-store-category-style = Стиль и Мода
citinet-store-category-botany = Ботаника
citinet-store-category-workwear = Спецодежда
citinet-store-category-weapons = Оружие
citinet-store-category-armor = Защита
citinet-store-category-ammo = Боеприпасы
citinet-store-category-cyberware = Кибер-Импланты
citinet-store-category-industrial = Пром. Оборудование
citinet-store-category-chemistry = Химия
citinet-store-category-seeds = Семена

citinet-store-cart-empty = Корзина пуста.
citinet-store-cart-added = Добавлено в корзину: { $amount } шт.
citinet-store-cart-invalid = В корзине есть недоступные позиции. Очистите ее и попробуйте снова.
citinet-store-stock-insufficient = На городском складе не хватает товара для этой корзины.
citinet-store-corporate-account-unavailable = Корпоративный счет недоступен.
citinet-store-corporate-data-insufficient = На счете фракции недостаточно корпоративных данных.
citinet-store-corporate-funds-insufficient = На корпоративном счете недостаточно эдди.
citinet-store-personal-funds-insufficient = На вашем счете недостаточно эдди.
citinet-store-delivery-failed-refunded = Ошибка доставки: { $reason } Деньги и данные возвращены.

citinet-delivery-no-drop-points = Нет доступных точек доставки. Попробуйте позже.
citinet-delivery-no-corporate-zones = Нет доступных корпоративных зон выдачи. Попробуйте позже.
citinet-delivery-packaging-error = Ошибка упаковки товара. Свяжитесь с техподдержкой.
citinet-delivery-corporate-dropbox-ready = Грузовой ящик ({ $count } шт.) доставлен в { $location }. PIN: { $pin }. Срок хранения: 15 минут. Навигационный чип выдан.
citinet-delivery-dead-drop-ready = Заказ ({ $count } шт.) оставлен в { $location }. Навигационный чип выдан.
citinet-delivery-corporate-zone-scheduled = Корпоративный заказ ({ $count } шт.) подтвержден. Зона выдачи: { $location }. Ожидание: { $minutes } мин. Навигационный чип выдан.
citinet-delivery-keypad-unlocked = Код верный. Замок открыт.
citinet-delivery-keypad-access-granted = Доступ разрешен. Заберите ваш груз.
citinet-delivery-keypad-wrong-pin = Неверный код!
citinet-delivery-chip-examine-ready = Выдача готова: { $location }.
citinet-delivery-chip-examine-pending = Маршрут выдачи: { $location }. Ожидание: { $seconds } сек.

citinet-store-ui-balance = БАЛАНС: { $balance } ED
citinet-store-ui-corp-funds = СРЕДСТВА: { $balance } ED
citinet-store-ui-corp-data = ДАННЫЕ: { $data }
citinet-store-ui-corp-status-no-account = СТАТУС: НЕТ МАРШРУТА К КОРПОРАТИВНОМУ СЧЕТУ
citinet-store-ui-corp-status-dropbox = СТАТУС: МАРШРУТ ЧЕРЕЗ ЗАЩИЩЕННЫЙ ПОЧТОМАТ
citinet-store-ui-corp-status-zone = СТАТУС: ВЫЕЗДНАЯ ЗОНА ВЫДАЧИ ЗА ГОРОДОМ
citinet-store-ui-stock = СКЛАД: { $count }
citinet-store-ui-stock-depleted = СКЛАД: ПУСТО
citinet-store-ui-sold-out = НЕТ В НАЛИЧИИ
citinet-store-ui-add-to-cart = В КОРЗИНУ
citinet-store-ui-price = { $price } ED + { $data } DATA
citinet-store-ui-price-money = { $price } ED
citinet-store-ui-cart-summary = КОРЗИНА: { $price } ED + { $data } DATA
citinet-store-ui-cart-summary-money = КОРЗИНА: { $price } ED
citinet-store-ui-cart-clear = ОЧИСТИТЬ
citinet-store-ui-cart-checkout = ЗАКАЗАТЬ
citinet-store-ui-cart-empty = Корзина пуста.
citinet-store-ui-cart-line = { $name } x{ $amount }
citinet-store-ui-cart-line-price = { $price } ED + { $data } DATA
citinet-store-ui-cart-line-price-money = { $price } ED

# Store Items
citinet-pill-canister-desc = 10 таблеток по 10 унций.

# NetSites
citinet-site-name-home = CitiNet Home
citinet-site-name-comm = CitiNet Comm
citinet-site-name-flatline = База данных Flatline
citinet-site-name-astrozon = Astrozon
citinet-site-name-night-market = Ночной рынок
citinet-site-name-ncpd-records = NCPD Central Database
citinet-site-name-trauma-monitor = Trauma Care Monitor

# Map
citinet-map-beacon-default = Новый POI
citinet-map-sector-default = Новый сектор
ent-CitiNetMapCartridge = картридж карты CitiNet
ent-CitiNetMapCartridge-desc = Программа для визуализации слоев тактической карты.
