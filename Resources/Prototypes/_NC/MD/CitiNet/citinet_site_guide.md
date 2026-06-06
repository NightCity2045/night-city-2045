# Техническое руководство: Экосистема CitiNet

Система CitiNet предназначена для замены разрозненных консолей SS14 единым браузерным интерфейсом. Это позволяет создавать интерактивные терминалы, содержимое которых зависит от прав доступа пользователя и установленных модулей (Data Chips).

## 1. Основные компоненты

### NetSitePrototype (YAML)
Описывает метаданные сайта. Находится в `Resources/Prototypes/_NC/CitiNet/`.
*   `id`: Уникальный идентификатор прототипа.
*   `url`: Адрес сайта (например, `ncpd.gov/database`).
*   `uiKey`: Ключ, связывающий сайт с C# кодом интерфейса.
*   `requiredAccess`: Список `AccessLevelPrototype` (например, `NCPD_Command`), необходимых для доступа.

### NetBrowserComponent (Shared)
Хранит состояние терминала: текущий открытый URL и список "разблокированных" адресов (те, что были открыты чипами).

### DataChipComponent (Shared)
Компонент для предметов-чипов. Поле `unlockedSiteId` указывает, какой сайт будет разблокирован при вставке чипа в терминал.

---

## 2. Создание нового сайта

Чтобы добавить новый функционал (например, "Управление камерами"), выполните следующие шаги:

### Шаг 1: Создание UI (Client)
Создайте XAML-файл и Code-behind в `Content.Client/_NC/CitiNet/UI/`. Ваш класс должен наследоваться от `Control` (или `BoxContainer`) и иметь соответствующий `UIFragment`.

Пример фрагмента:
```csharp
public sealed partial class MySiteUiFragment : UIFragment
{
    private MySiteControl? _fragment;
    public override Control GetUIFragmentRoot() => _fragment!;
    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new MySiteControl();
    }
    public override void UpdateState(BoundUserInterfaceState state) { /* логика обновления */ }
}
```

### Шаг 2: Регистрация в BUI (Client)
Откройте `NetBrowserBoundUserInterface.cs` и добавьте ваш фрагмент в метод `GetUIFragment`:

```csharp
private UIFragment? GetUIFragment(string uiKey)
{
    return uiKey switch
    {
        "NetHome" => new NetHomeSiteUIFragment(),
        "MyNewSite" => new MySiteUiFragment(), // Ваша регистрация
        _ => null
    };
}
```

### Шаг 3: Описание в YAML (Prototypes)
Добавьте сайт в `citinet_sites.yml`:

```yaml
- type: netSite
  id: MyCoolService
  name: "Управление системами"
  url: "corp.net/admin"
  uiKey: MyNewSite
  requiredAccess:
  - DepartmentHead
```

---

## 3. Физические носители (Data Chips)

Если вы хотите, чтобы доступ к сайту давал физический предмет, создайте энтити с `DataChipComponent`:

```yaml
- type: entity
  parent: BaseItem
  id: AdminAccessChip
  components:
  - type: DataChip
    unlockedSiteId: MyCoolService
```

При вставке этого чипа в слот `chip_slot` терминала `CitiNetTerminal`, сайт автоматически появится в списке закладок и станет доступен даже пользователям без нужных прав в ID-карте.

---

## 4. Архитектурные особенности

1.  **Безопасность:** Проверка доступа происходит на сервере в `NetBrowserSystem`. Если у пользователя нет нужных `AccessTags` и сайт не разблокирован чипом, навигация будет прервана.
2.  **Динамический интерфейс:** Интерфейс браузера использует `Orphan` метод для переключения фрагментов. Не используйте `Dispose()` вручную для элементов UI внутри фрагментов, так как это управляется жизненным циклом BUI.
3.  **Синхронизация:** При вставке/извлечении предметов в терминал, список доступных сайтов обновляется автоматически для всех игроков, у которых открыто окно данного терминала.
